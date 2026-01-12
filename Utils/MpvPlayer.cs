using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
                
                if (_process == null)
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
                // 停止当前播放（如果有）
                await StopAsync();

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

                lock (_lock)
                {
                    _process = new Process { StartInfo = startInfo };
                    _process.EnableRaisingEvents = true;
                    _process.Exited += OnProcessExited;
                }

                // 启动进程
                var processStarted = _process.Start();
                if (!processStarted)
                {
                    throw new InvalidOperationException("无法启动 mpv 进程");
                }

                LogMessage($"mpv 进程已启动 (PID: {_process.Id})");

                // 启动进程监控
                StartProcessMonitoring();

                // 等待进程结束
                await _process.WaitForExitAsync(_cancellationTokenSource.Token);

                LogMessage($"mpv 进程已结束 (退出代码: {_process.ExitCode})");

                // 检查退出代码
                if (_process.ExitCode != 0)
                {
                    var errorOutput = await _process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(errorOutput))
                    {
                        LogMessage($"mpv 错误输出: {errorOutput}");
                        throw new InvalidOperationException($"mpv 播放失败 (退出代码: {_process.ExitCode}): {errorOutput}");
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
                    while (!_cancellationTokenSource.Token.IsCancellationRequested && _process != null && !_process.HasExited)
                    {
                        await Task.Delay(1000, _cancellationTokenSource.Token);
                        
                        // 检查进程是否响应
                        try
                        {
                            if (_process != null && !_process.HasExited && !_process.Responding)
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

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            StopAsync().Wait(5000); // 5秒超时
        }

        /// <summary>
        /// 异步停止播放
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                lock (_lock)
                {
                    if (_process != null && !_process.HasExited)
                    {
                        LogMessage("正在停止 mpv 进程...");
                        
                        try
                        {
                            // 尝试优雅地终止进程
                            _process.CloseMainWindow();
                            
                            // 等待进程退出
                            if (!_process.WaitForExit(3000))
                            {
                                LogMessage("进程未在3秒内退出，强制终止");
                                _process.Kill();
                                _process.WaitForExit(2000);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"停止进程时发生错误: {ex.Message}");
                        }
                    }

                    _isPlaying = false;
                }

                // 取消监控任务
                _cancellationTokenSource?.Cancel();
                
                if (_processMonitorTask != null)
                {
                    try
                    {
                        await _processMonitorTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"停止播放时发生错误: {ex.Message}");
            }
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
                
                // 停止播放
                StopAsync().Wait(5000);

                // 释放进程资源
                lock (_lock)
                {
                    if (_process != null)
                    {
                        try
                        {
                            _process.Exited -= OnProcessExited;
                            _process.Dispose();
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"释放进程资源时发生错误: {ex.Message}");
                        }
                        _process = null;
                    }
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
            Console.WriteLine($"[MpvPlayer] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
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
