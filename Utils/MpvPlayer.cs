using System.Diagnostics;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 基于 mpv.exe 的媒体播放器
    /// 支持高码率音频播放
    /// </summary>
    public class MpvPlayer : IDisposable
    {
        private Process _process;
        private readonly object _lock = new object();
        private bool _isPlaying = false;
        private readonly string _mpvExePath;
        private double _volume = 100.0;
        private bool _disposed = false;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _processMonitorTask;

        /// <summary>
        /// 进程异常退出事件
        /// </summary>
        public event EventHandler<ProcessExitedEventArgs> ProcessExited;

        /// <summary>
        /// 播放完成事件
        /// </summary>
        public event EventHandler<PlaybackCompletedEventArgs> PlaybackCompleted;

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _isPlaying && !_disposed;
                }
            }
        }

        /// <summary>
        /// 获取当前进程状态
        /// </summary>
        public ProcessStatus GetProcessStatus()
        {
            lock (_lock)
            {
                if (_disposed)
                    return ProcessStatus.Disposed;

                if (_process is null)
                    return ProcessStatus.NotStarted;

                try
                {
                    if (_process.HasExited)
                        return ProcessStatus.Exited;

                    return _isPlaying ? ProcessStatus.Playing : ProcessStatus.Ready;
                }
                catch
                {
                    return ProcessStatus.Unknown;
                }
            }
        }

        public MpvPlayer(string mpvExePath)
        {
            _mpvExePath = mpvExePath ?? throw new ArgumentNullException(nameof(mpvExePath));

            if (!File.Exists(_mpvExePath))
            {
                throw new FileNotFoundException($"mpv.exe 未找到: {_mpvExePath}");
            }

            _cancellationTokenSource = new CancellationTokenSource();
            LogMessage($"mpv 播放器初始化成功: {_mpvExePath}");
        }

        /// <summary>
        /// 播放音频文件
        /// </summary>
        public async Task PlayAsync(string filePath)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MpvPlayer));
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                LogMessage($"文件不存在: {filePath}");
                throw new FileNotFoundException($"音频文件不存在: {filePath}");
            }

            try
            {
                // 检查是否正在播放
                bool wasPlaying = false;
                lock (_lock)
                {
                    wasPlaying = _isPlaying;
                }

                if (wasPlaying)
                {
                    LogMessage($"警告: 正在播放其他音频，将停止当前播放");
                    // 停止当前播放（如果有）
                    await StopAsync();
                }

                // 重新创建 CancellationTokenSource（因为 StopAsync 会取消它）
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                lock (_lock)
                {
                    _isPlaying = true;
                }

                LogMessage($"开始播放: {Path.GetFileName(filePath)}");

                // 启动 mpv 进程
                await StartMpvProcessAsync(filePath);

                LogMessage($"播放完成: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                LogMessage($"播放错误: {ex.Message}");
                lock (_lock)
                {
                    _isPlaying = false;
                }
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _isPlaying = false;
                }
            }
        }

        /// <summary>
        /// 启动 mpv 进程
        /// </summary>
        private async Task StartMpvProcessAsync(string filePath)
        {
            try
            {
                // 构建命令行参数
                var args = $"--no-video --volume={_volume} --no-terminal --really-quiet \"{filePath}\"";

                // 创建进程启动信息
                var startInfo = new ProcessStartInfo
                {
                    FileName = _mpvExePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(_mpvExePath)
                };

                // 本次播放的进程对象。后面全程用这个局部变量而不是 _process 字段：
                // 字段随时可能被下一次播放替换并释放，await 之后再去读它会踩到
                // 已释放的对象（ObjectDisposedException）或者别人的进程。
                Process process;

                lock (_lock)
                {
                    // 先释放上一次播放遗留的进程对象。
                    // 每次播放都会新建 Process 并覆盖 _process，而原先只有 Dispose()
                    // （插件卸载时）才会释放，于是桌宠每说一句话就漏一个 Process ——
                    // 它持有原生进程句柄，且因为重定向了 stdout/stderr 还挂着两个管道流。
                    ReleaseProcess();

                    process = new Process { StartInfo = startInfo };
                    process.EnableRaisingEvents = true;
                    process.Exited += OnProcessExited;
                    _process = process;
                }

                // 启动进程
                var processStarted = process.Start();
                if (!processStarted)
                {
                    throw new InvalidOperationException("无法启动 mpv 进程");
                }

                // 挂进 Job：宿主进程无论怎么没的（Environment.Exit / 崩溃 / 强杀），
                // 系统都会把 mpv 一起收走，不会出现"VPet 退了还在说话"
                ChildProcessTracker.Track(process);

                LogMessage($"mpv 进程已启动 (PID: {process.Id})");

                // 启动进程监控
                StartProcessMonitoring();

                // 等待进程结束
                await process.WaitForExitAsync(_cancellationTokenSource.Token);

                LogMessage($"mpv 进程已结束 (退出代码: {process.ExitCode})");

                // 检查退出代码
                if (process.ExitCode != 0)
                {
                    var errorOutput = await process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(errorOutput))
                    {
                        LogMessage($"mpv 错误输出: {errorOutput}");
                        throw new InvalidOperationException($"mpv 播放失败 (退出代码: {process.ExitCode}): {errorOutput}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("mpv 播放被取消");
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"启动 mpv 进程失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动进程监控
        /// </summary>
        private void StartProcessMonitoring()
        {
            _processMonitorTask = Task.Run(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested && _process is not null && !_process.HasExited)
                    {
                        await Task.Delay(1000, _cancellationTokenSource.Token);

                        // 检查进程是否响应
                        try
                        {
                            if (_process is not null && !_process.HasExited && !_process.Responding)
                            {
                                LogMessage("检测到 mpv 进程无响应");
                                OnProcessExited(this, new ProcessExitedEventArgs
                                {
                                    ExitCode = -1,
                                    Reason = "进程无响应"
                                });
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"进程监控异常: {ex.Message}");
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不需要处理
                }
                catch (Exception ex)
                {
                    LogMessage($"进程监控任务异常: {ex.Message}");
                }
            }, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 解绑并释放当前的进程对象。调用方须持有 <see cref="_lock"/>。
        /// 先摘事件再 Dispose，避免已释放的进程还回调到 <see cref="OnProcessExited"/>。
        /// </summary>
        private void ReleaseProcess()
        {
            if (_process is null)
                return;

            try
            {
                _process.Exited -= OnProcessExited;
                _process.Dispose();
            }
            catch (Exception ex)
            {
                LogMessage($"释放进程资源时发生错误: {ex.Message}");
            }
            finally
            {
                _process = null;
            }
        }

        /// <summary>
        /// 进程退出事件处理
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            try
            {
                var exitCode = _process?.ExitCode ?? -1;
                var reason = exitCode == 0 ? "正常退出" : $"异常退出 (代码: {exitCode})";

                LogMessage($"mpv 进程退出: {reason}");

                lock (_lock)
                {
                    _isPlaying = false;
                }

                // 触发事件
                ProcessExited?.Invoke(this, new ProcessExitedEventArgs
                {
                    ExitCode = exitCode,
                    Reason = reason
                });

                PlaybackCompleted?.Invoke(this, new PlaybackCompletedEventArgs
                {
                    Success = exitCode == 0,
                    ExitCode = exitCode
                });
            }
            catch (Exception ex)
            {
                LogMessage($"处理进程退出事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>有窗口时留给优雅关闭的时间</summary>
        private const int GracefulExitMs = 200;

        /// <summary>Kill 之后等待进程真正消失的时间</summary>
        private const int KillWaitMs = 1000;

        /// <summary>
        /// 停止播放（同步包装）。
        /// 与 Dispose 同理走 Task.Run：直接 Wait 一个在 UI 线程上启动的异步方法，
        /// 续体会排在被 Wait 卡住的 UI 线程上，只能干等到超时。
        /// </summary>
        public void Stop()
        {
            if (!Task.Run(() => StopAsync()).Wait(KillWaitMs + GracefulExitMs))
            {
                LogMessage("同步停止超时，进程终止在后台继续");
            }
        }

        /// <summary>
        /// 异步停止播放。
        ///
        /// 顺序很重要：先取消令牌，再去处理进程。
        /// PlayAsync 等的是 WaitForExitAsync(token)，令牌一取消它当场返回，
        /// 上层（AudioPlaybackService → TTS 流水线 → VPetLLM 的中断）立刻解阻塞，
        /// 不用陪着等进程真的死透。
        /// </summary>
        public async Task StopAsync()
        {
            Process process;
            CancellationTokenSource cts;

            // 只在锁内取快照并翻状态位 —— 等进程退出是几百毫秒级的阻塞操作，
            // 放在锁里会把 IsPlaying / GetProcessStatus / PlayAsync 一起拖住
            lock (_lock)
            {
                process = _process;
                cts = _cancellationTokenSource;
                _isPlaying = false;
            }

            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { LogMessage($"取消播放令牌失败: {ex.Message}"); }

            await TerminateProcessAsync(process).ConfigureAwait(false);
            await WaitForMonitorExitAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 结束 mpv 进程。
        ///
        /// 这里过去是 CloseMainWindow() + WaitForExit(3000)：mpv 是以
        /// --no-video + CreateNoWindow 启动的，压根没有主窗口，CloseMainWindow()
        /// 直接返回 false 什么也没做，然后白白等满 3 秒才轮到 Kill() ——
        /// 每次中断都要卡这 3 秒。没有窗口就直接 Kill，音频进程没有什么状态可保存。
        /// </summary>
        private async Task TerminateProcessAsync(Process process)
        {
            if (process is null)
                return;

            try
            {
                if (process.HasExited)
                    return;

                LogMessage("正在停止 mpv 进程...");

                // 仅在真的有窗口时（将来若开启视频输出）才走优雅关闭，且只给很短的时间
                if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                {
                    if (await WaitForExitAsync(process, GracefulExitMs).ConfigureAwait(false))
                    {
                        LogMessage("mpv 进程已优雅退出");
                        return;
                    }
                }

                process.Kill();
                if (!await WaitForExitAsync(process, KillWaitMs).ConfigureAwait(false))
                {
                    LogMessage($"mpv 进程未在 {KillWaitMs}ms 内退出");
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已经退出或对象已释放，无事可做
            }
            catch (Exception ex)
            {
                LogMessage($"停止 mpv 进程时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 等待进程退出，超时返回 false（不抛异常）
        /// </summary>
        private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs)
        {
            using var timeout = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return true; // 进程已退出
            }
        }

        /// <summary>
        /// 等监控任务收尾。令牌已取消，正常情况下立刻结束；给个上限别把停止流程吊死。
        /// </summary>
        private async Task WaitForMonitorExitAsync()
        {
            var monitor = _processMonitorTask;
            if (monitor is null)
                return;

            // WhenAny 不会因为任务被取消而抛异常，正好用来做有界等待
            await Task.WhenAny(monitor, Task.Delay(GracefulExitMs)).ConfigureAwait(false);
        }

        /// <summary>
        /// 设置音量
        /// </summary>
        public void SetVolume(double volume)
        {
            _volume = Math.Clamp(volume, 0.0, 100.0);
            LogMessage($"音量设置为: {_volume}%");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            LogMessage("正在释放 mpv 播放器资源...");

            try
            {
                _disposed = true;

                // 停止播放。
                //
                // 这里过去是 StopAsync().Wait(5000)：Dispose 由 Application.Exit 在 UI 线程上
                // 调用，而 StopAsync 里 await 之后的续体要回到同一个 UI 线程——UI 线程却正卡在
                // Wait 上，续体永远排不上，只能干等满 5 秒超时。表现就是每次退出都卡 5 秒。
                //
                // Task.Run 把异步流程挪到线程池启动，续体不再需要 UI 线程，正常情况下瞬间返回；
                // 超时缩到 3 秒，仅作为进程真的杀不掉时的兜底。
                if (!Task.Run(() => StopAsync()).Wait(TimeSpan.FromSeconds(3)))
                {
                    LogMessage("停止 mpv 超时（3 秒），继续释放资源");
                }

                // 释放进程资源
                lock (_lock)
                {
                    ReleaseProcess();
                }

                // 释放取消令牌
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                LogMessage("mpv 播放器资源释放完成");
            }
            catch (Exception ex)
            {
                LogMessage($"释放资源时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private void LogMessage(string message)
        {
            TTSLogger.Log($"[MpvPlayer] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }

    /// <summary>
    /// 进程状态枚举
    /// </summary>
    public enum ProcessStatus
    {
        NotStarted,
        Ready,
        Playing,
        Exited,
        Disposed,
        Unknown
    }

    /// <summary>
    /// 进程退出事件参数
    /// </summary>
    public class ProcessExitedEventArgs : EventArgs
    {
        public int ExitCode { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// 播放完成事件参数
    /// </summary>
    public class PlaybackCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
    }
}
