namespace VPet.Plugin.VPetTTS.Utils
{
    /// <summary>
    /// 优雅播放器管理器，解决VPetTTS播放器强制终止问题
    /// 实现播放器切换时的状态同步和优雅停止机制
    /// </summary>
    public class GracefulPlayerManager
    {
        private readonly object _lock = new object();
        private MpvPlayer _currentPlayer;
        private volatile bool _isTransitioning = false;
        private readonly SemaphoreSlim _transitionSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 播放器状态变化事件
        /// </summary>
        public event EventHandler<PlayerStateChangedEventArgs> PlayerStateChanged;

        /// <summary>
        /// 当前播放器是否正在播放
        /// </summary>
        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _currentPlayer?.IsPlaying == true && !_isTransitioning;
                }
            }
        }

        /// <summary>
        /// 是否正在切换播放器
        /// </summary>
        public bool IsTransitioning => _isTransitioning;

        /// <summary>
        /// 获取当前播放器状态
        /// </summary>
        public PlayerStatus GetCurrentStatus()
        {
            lock (_lock)
            {
                if (_isTransitioning)
                    return PlayerStatus.Transitioning;

                if (_currentPlayer is null)
                    return PlayerStatus.NotInitialized;

                var processStatus = _currentPlayer.GetProcessStatus();
                return MapProcessStatusToPlayerStatus(processStatus);
            }
        }

        /// <summary>
        /// 设置当前播放器（优雅切换）
        /// </summary>
        /// <param name="newPlayer">新的播放器实例</param>
        /// <param name="forceStop">是否强制停止当前播放器</param>
        public async Task<bool> SetPlayerAsync(MpvPlayer newPlayer, bool forceStop = false)
        {
            if (newPlayer is null)
                throw new ArgumentNullException(nameof(newPlayer));

            // 获取切换信号量，确保同时只有一个切换操作
            if (!await _transitionSemaphore.WaitAsync(5000))
            {
                LogMessage("设置播放器超时，可能存在死锁");
                return false;
            }

            try
            {
                _isTransitioning = true;
                OnPlayerStateChanged(PlayerStatus.Transitioning, "开始切换播放器");

                MpvPlayer oldPlayer;
                lock (_lock)
                {
                    oldPlayer = _currentPlayer;
                    _currentPlayer = newPlayer;
                }

                // 优雅停止旧播放器
                if (oldPlayer is not null)
                {
                    var stopSuccess = await StopPlayerGracefullyAsync(oldPlayer, forceStop);
                    if (!stopSuccess && !forceStop)
                    {
                        LogMessage("旧播放器停止失败，但继续使用新播放器");
                    }
                }

                // 设置新播放器的事件处理
                SetupPlayerEvents(newPlayer);

                OnPlayerStateChanged(PlayerStatus.Ready, "播放器切换完成");
                LogMessage("播放器切换成功");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"设置播放器失败: {ex.Message}");
                OnPlayerStateChanged(PlayerStatus.Error, $"切换失败: {ex.Message}");
                return false;
            }
            finally
            {
                _isTransitioning = false;
                _transitionSemaphore.Release();
            }
        }

        /// <summary>
        /// 播放音频文件
        /// </summary>
        /// <param name="filePath">音频文件路径</param>
        public async Task<bool> PlayAsync(string filePath)
        {
            if (_isTransitioning)
            {
                LogMessage("播放器正在切换中，等待切换完成");
                await _transitionSemaphore.WaitAsync();
                _transitionSemaphore.Release();
            }

            MpvPlayer player;
            lock (_lock)
            {
                player = _currentPlayer;
            }

            if (player is null)
            {
                LogMessage("没有可用的播放器");
                return false;
            }

            try
            {
                OnPlayerStateChanged(PlayerStatus.Playing, $"开始播放: {filePath}");
                await player.PlayAsync(filePath);
                OnPlayerStateChanged(PlayerStatus.Ready, "播放完成");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"播放失败: {ex.Message}");
                OnPlayerStateChanged(PlayerStatus.Error, $"播放失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止当前播放
        /// </summary>
        /// <param name="forceStop">是否强制停止</param>
        public async Task<bool> StopAsync(bool forceStop = false)
        {
            MpvPlayer player;
            lock (_lock)
            {
                player = _currentPlayer;
            }

            if (player is null)
            {
                return true; // 没有播放器，认为停止成功
            }

            return await StopPlayerGracefullyAsync(player, forceStop);
        }

        /// <summary>
        /// 优雅停止播放器
        /// </summary>
        /// <param name="player">要停止的播放器</param>
        /// <param name="forceStop">是否强制停止</param>
        private async Task<bool> StopPlayerGracefullyAsync(MpvPlayer player, bool forceStop)
        {
            if (player is null)
                return true;

            try
            {
                LogMessage($"开始优雅停止播放器，强制模式: {forceStop}");
                OnPlayerStateChanged(PlayerStatus.Stopping, "正在停止播放器");

                if (forceStop)
                {
                    // 强制模式：直接停止
                    await player.StopAsync();
                    LogMessage("播放器强制停止完成");
                }
                else
                {
                    // 优雅模式：先检查状态，再决定停止策略
                    var status = player.GetProcessStatus();

                    switch (status)
                    {
                        case ProcessStatus.NotStarted:
                        case ProcessStatus.Exited:
                        case ProcessStatus.Disposed:
                            LogMessage($"播放器已处于停止状态: {status}");
                            return true;

                        case ProcessStatus.Playing:
                            LogMessage("播放器正在播放，执行优雅停止");
                            await player.StopAsync();
                            break;

                        case ProcessStatus.Ready:
                            LogMessage("播放器处于就绪状态，直接停止");
                            await player.StopAsync();
                            break;

                        case ProcessStatus.Unknown:
                            LogMessage("播放器状态未知，尝试优雅停止");
                            await player.StopAsync();
                            break;
                    }
                }

                // 验证停止结果
                await Task.Delay(500); // 给播放器一些时间完成停止
                var finalStatus = player.GetProcessStatus();
                var success = finalStatus == ProcessStatus.Exited ||
                             finalStatus == ProcessStatus.Disposed ||
                             finalStatus == ProcessStatus.NotStarted;

                if (success)
                {
                    LogMessage("播放器优雅停止成功");
                    OnPlayerStateChanged(PlayerStatus.Stopped, "播放器已停止");
                }
                else
                {
                    LogMessage($"播放器停止后状态异常: {finalStatus}");
                    OnPlayerStateChanged(PlayerStatus.Error, $"停止后状态异常: {finalStatus}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogMessage($"优雅停止播放器失败: {ex.Message}");
                OnPlayerStateChanged(PlayerStatus.Error, $"停止失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置播放器事件处理
        /// </summary>
        private void SetupPlayerEvents(MpvPlayer player)
        {
            if (player is null)
                return;

            // 播放完成事件
            player.PlaybackCompleted += (sender, e) =>
            {
                LogMessage($"播放完成事件: 成功={e.Success}, 退出代码={e.ExitCode}");
                var status = e.Success ? PlayerStatus.Ready : PlayerStatus.Error;
                var message = e.Success ? "播放完成" : $"播放异常结束 (代码: {e.ExitCode})";
                OnPlayerStateChanged(status, message);
            };

            // 进程退出事件
            player.ProcessExited += (sender, e) =>
            {
                LogMessage($"进程退出事件: 退出代码={e.ExitCode}, 原因={e.Reason}");
                var status = e.ExitCode == 0 ? PlayerStatus.Stopped : PlayerStatus.Error;
                OnPlayerStateChanged(status, e.Reason);
            };
        }

        /// <summary>
        /// 映射进程状态到播放器状态
        /// </summary>
        private PlayerStatus MapProcessStatusToPlayerStatus(ProcessStatus processStatus)
        {
            return processStatus switch
            {
                ProcessStatus.NotStarted => PlayerStatus.NotInitialized,
                ProcessStatus.Ready => PlayerStatus.Ready,
                ProcessStatus.Playing => PlayerStatus.Playing,
                ProcessStatus.Exited => PlayerStatus.Stopped,
                ProcessStatus.Disposed => PlayerStatus.Disposed,
                ProcessStatus.Unknown => PlayerStatus.Error,
                _ => PlayerStatus.Unknown
            };
        }

        /// <summary>
        /// 触发播放器状态变化事件
        /// </summary>
        private void OnPlayerStateChanged(PlayerStatus status, string message)
        {
            try
            {
                PlayerStateChanged?.Invoke(this, new PlayerStateChangedEventArgs
                {
                    Status = status,
                    Message = message,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                LogMessage($"触发状态变化事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public async Task DisposeAsync()
        {
            try
            {
                _isTransitioning = true;

                MpvPlayer player;
                lock (_lock)
                {
                    player = _currentPlayer;
                    _currentPlayer = null;
                }

                if (player is not null)
                {
                    await StopPlayerGracefullyAsync(player, true);
                    player.Dispose();
                }

                _transitionSemaphore?.Dispose();
                LogMessage("GracefulPlayerManager 资源释放完成");
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
            TTSLogger.Log($"[GracefulPlayerManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }

    /// <summary>
    /// 播放器状态枚举
    /// </summary>
    public enum PlayerStatus
    {
        NotInitialized,  // 未初始化
        Ready,           // 就绪
        Playing,         // 播放中
        Stopping,        // 停止中
        Stopped,         // 已停止
        Transitioning,   // 切换中
        Error,           // 错误
        Disposed,        // 已释放
        Unknown          // 未知状态
    }

    /// <summary>
    /// 播放器状态变化事件参数
    /// </summary>
    public class PlayerStateChangedEventArgs : EventArgs
    {
        public PlayerStatus Status { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}