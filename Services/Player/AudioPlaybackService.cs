namespace Vpet.Plugin.CustomTTS.Services.Player;

/// <summary>
/// 音频播放服务
/// 负责音频文件验证、播放和状态跟踪
/// </summary>
public class AudioPlaybackService : IAudioPlaybackService
{
    private readonly IPlayerManager _playerManager;
    private readonly TTSStateManager _stateManager;
    private readonly IMainWindow _mainWindow;
    private readonly PlayerErrorHandler _errorHandler;

    private bool _isPlaying = false;

    public AudioPlaybackService(
        IPlayerManager playerManager,
        TTSStateManager stateManager,
        IMainWindow mainWindow,
        PlayerErrorHandler errorHandler)
    {
        _playerManager = playerManager ?? throw new ArgumentNullException(nameof(playerManager));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    }

    /// <summary>
    /// 是否正在播放
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// 播放音频文件
    /// </summary>
    public async Task PlayAudioAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("音频文件路径为空", nameof(path));
        }

        // 全面验证音频文件
        var validationResult = AudioPathHelper.ValidateAudioPath(path);
        if (!validationResult.IsValid)
        {
            var error = $"音频文件验证失败: {validationResult.ErrorMessage}";
            _stateManager?.SetError(error, new ArgumentException(validationResult.ErrorMessage), TTSOperationStage.Playing);
            throw new ArgumentException(error);
        }

        // 使用验证后的规范化路径
        var normalizedPath = validationResult.NormalizedPath;

        var originalPlayerType = _playerManager.CurrentPlayerType;
        var playbackAttempts = 0;
        const int maxAttempts = 2; // 最多尝试两种播放器

        // 获取音频时长（如果可能）
        long audioDurationMs = -1;
        try
        {
            audioDurationMs = GetAudioDurationMs(normalizedPath);
        }
        catch { }

        // 设置播放状态为true（开始播放），包含音频时长信息
        _stateManager?.SetPlayingState(true, normalizedPath, "", audioDurationMs);
        _isPlaying = true;

        try
        {
            while (playbackAttempts < maxAttempts)
            {
                playbackAttempts++;

                try
                {
                    if (_playerManager.CurrentPlayerType == PlayerType.MpvPlayer && _playerManager.UseMpvPlayer)
                    {
                        // 使用 mpv 播放器（高码率支持）
                        await PlayWithMpvAsync(normalizedPath);
                        return; // 播放成功
                    }
                    else if (_playerManager.CurrentPlayerType == PlayerType.VPetBuiltIn)
                    {
                        // 使用 VPet 内置播放器
                        await PlayWithVPetBuiltInAsync(normalizedPath);
                        return; // 播放成功
                    }
                    else
                    {
                        throw new InvalidOperationException($"无效的播放器类型: {_playerManager.CurrentPlayerType}");
                    }
                }
                catch (Exception ex)
                {
                    var error = $"播放器 {_playerManager.CurrentPlayerType} 播放失败: {ex.Message}";

                    // 使用错误处理器记录详细错误
                    _errorHandler.HandlePlayerError(_playerManager.CurrentPlayerType, ex, "音频播放", normalizedPath);

                    // 如果是第一次尝试且使用的是 mpv，尝试切换到内置播放器
                    if (playbackAttempts == 1 && _playerManager.CurrentPlayerType == PlayerType.MpvPlayer)
                    {
                        // 检查是否应该切换播放器
                        if (_errorHandler.ShouldRetryWithDifferentPlayer(ex, _playerManager.CurrentPlayerType))
                        {
                            await _playerManager.SwitchToFallbackPlayerAsync($"mpv 播放失败: {ex.Message}");
                            continue;
                        }
                    }

                    // 如果所有播放器都失败了
                    _stateManager?.SetError($"音频播放失败: {ex.Message}", ex, TTSOperationStage.Playing);
                    throw;
                }
            }
        }
        finally
        {
            // 确保播放状态被释放（无论成功还是失败）
            _stateManager?.SetPlayingState(false, normalizedPath, "");
            _isPlaying = false;
        }
    }

    /// <summary>
    /// 停止当前播放
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            if (_playerManager.UseMpvPlayer)
            {
                var mpvPlayer = (_playerManager as PlayerManager)?.GetMpvPlayer();
                if (mpvPlayer is not null)
                {
                    await mpvPlayer.StopAsync();
                }
            }

            _isPlaying = false;
            _stateManager?.SetPlayingState(false);
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(_playerManager.CurrentPlayerType, ex, "停止播放");
        }
    }

    // ============================================================================
    // 私有方法
    // ============================================================================

    /// <summary>
    /// 使用 mpv 播放器播放音频
    /// </summary>
    private async Task PlayWithMpvAsync(string path)
    {
        var mpvPlayer = (_playerManager as PlayerManager)?.GetMpvPlayer();
        if (mpvPlayer is null)
        {
            throw new InvalidOperationException("mpv 播放器未初始化");
        }

        CancellationTokenSource heartbeatCts = null;
        Task heartbeatTask = null;

        try
        {
            // 检查 mpv 播放器状态
            var processStatus = mpvPlayer.GetProcessStatus();
            if (processStatus == ProcessStatus.Disposed)
            {
                throw new ObjectDisposedException("mpv 播放器已被释放");
            }

            // 启动心跳更新任务（每500ms更新一次心跳）
            heartbeatCts = new CancellationTokenSource();
            heartbeatTask = StartHeartbeatUpdateAsync(heartbeatCts.Token);

            // mpv 播放时用静音占位驱动宿主保持说话动画（与 EdgeTTS 同机制）
            // 该能力恒开启，无需设置开关：纯增强，无实质代价
            var holdAnimation = SilentVoiceAnimationHold.Begin(_mainWindow);

            try
            {
                // 启动播放并等待完成
                await mpvPlayer.PlayAsync(path);
            }
            finally
            {
                if (holdAnimation)
                {
                    SilentVoiceAnimationHold.End(_mainWindow);
                }
            }
        }
        catch (FileNotFoundException ex)
        {
            throw new FileNotFoundException($"mpv 播放器找不到音频文件: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("损坏") || ex.Message.Contains("corrupt"))
        {
            throw new InvalidDataException($"音频文件可能已损坏: {ex.Message}", ex);
        }
        finally
        {
            // 停止心跳更新任务
            if (heartbeatCts is not null)
            {
                try
                {
                    heartbeatCts.Cancel();
                    if (heartbeatTask is not null)
                    {
                        await Task.WhenAny(heartbeatTask, Task.Delay(1000)); // 最多等待1秒
                    }
                }
                catch { }
                finally
                {
                    heartbeatCts.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 启动心跳更新任务（在播放期间定期更新心跳）
    /// </summary>
    private async Task StartHeartbeatUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 更新心跳
                _stateManager?.UpdateHeartbeat();

                // 等待500ms
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不需要处理
        }
        catch { }
    }

    /// <summary>
    /// 使用 VPet 内置播放器播放音频
    /// </summary>
    private async Task PlayWithVPetBuiltInAsync(string path)
    {
        try
        {
            // 验证音频文件路径
            var validationResult = AudioPathHelper.ValidateAudioPath(path);
            if (!validationResult.IsValid)
            {
                throw new ArgumentException($"音频文件验证失败: {validationResult.ErrorMessage}");
            }

            // 规范化路径为 URI 格式
            var audioUri = AudioPathHelper.NormalizeToUri(validationResult.NormalizedPath);

            // 验证 URI 格式
            if (!Uri.TryCreate(audioUri, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"无法创建有效的 URI: {audioUri}");
            }

            // 确保在主线程上调用
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _mainWindow.Main.PlayVoice(uri);
                }
                catch (Exception ex)
                {
                    // 检查是否是文件格式不支持的错误
                    if (ex.Message.Contains("format") || ex.Message.Contains("格式") ||
                        ex.Message.Contains("codec") || ex.Message.Contains("编解码器"))
                    {
                        throw new NotSupportedException($"VPet 内置播放器不支持此音频格式: {validationResult.FileExtension}", ex);
                    }

                    throw new InvalidOperationException($"VPet 内置播放器调用失败: {ex.Message}", ex);
                }
            });

            // 等待VPet内置播放器播放完成
            await WaitForPlaybackCompleteAsync();
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"VPet 内置播放器播放失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 等待VPet内置播放器播放完成
    /// </summary>
    private async Task WaitForPlaybackCompleteAsync()
    {
        try
        {
            if (_mainWindow?.Main is null) return;

            int maxWaitTime = 60000; // 最多等待60秒
            int checkInterval = 200;  // 每200ms检查一次
            int elapsedTime = 0;

            // 给播放器一些时间开始播放
            await Task.Delay(500);

            // 等待播放完成
            while (_mainWindow.Main.PlayingVoice && elapsedTime < maxWaitTime)
            {
                await Task.Delay(checkInterval);
                elapsedTime += checkInterval;
            }
        }
        catch { }
    }

    /// <summary>
    /// 获取音频时长（毫秒）
    /// </summary>
    private long GetAudioDurationMs(string path)
    {
        try
        {
            // 这里可以使用 TagLib 或其他库来获取音频时长
            // 暂时返回 -1 表示未知
            return -1;
        }
        catch
        {
            return -1;
        }
    }
}
