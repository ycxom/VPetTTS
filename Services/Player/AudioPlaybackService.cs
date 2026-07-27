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
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return -1;

            var info = new FileInfo(path);
            if (info.Length <= 0) return -1;

            // WAV（Free/GPT-SoVITS 的默认输出）能从头部精确算出时长，不需要任何第三方库。
            if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                var wavMs = TryGetWavDurationMs(path);
                if (wavMs > 0) return wavMs;
            }

            // 其余格式按 128kbps 粗估。宁可给个量级正确的估算，
            // 也不要返回 -1 ——VPetLLM 拿 -1 无法安排动画时长。
            var estimated = (long)(info.Length * 8.0 / 128.0);
            return estimated > 0 ? estimated : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 解析 WAV 头得到精确时长：遍历 RIFF 块，用 fmt 的字节率除 data 块大小。
    /// 解析不出（非 PCM 容器、文件被截断）返回 -1 交给上层估算。
    /// </summary>
    private static long TryGetWavDurationMs(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 12) return -1;
            // 块标识固定是 ASCII，用 ReadBytes 而不是 ReadChars——后者按流的编码解码，
            // 遇到非 ASCII 字节会多吞字节导致后续偏移全错。
            if (ReadTag(reader) != "RIFF") return -1;
            reader.ReadUInt32(); // 整体大小，用不到
            if (ReadTag(reader) != "WAVE") return -1;

            uint byteRate = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                var chunkId = ReadTag(reader);
                var chunkSize = reader.ReadUInt32();

                if (chunkId == "fmt ")
                {
                    if (chunkSize < 16) return -1;
                    reader.ReadUInt16(); // audioFormat
                    reader.ReadUInt16(); // numChannels
                    reader.ReadUInt32(); // sampleRate
                    byteRate = reader.ReadUInt32();
                    // 已读 12 字节，跳过本块剩余部分（blockAlign/bitsPerSample/扩展字段）
                    fs.Position += chunkSize - 12 + (chunkSize % 2);
                }
                else if (chunkId == "data")
                {
                    if (byteRate == 0) return -1;
                    // data 块声明的大小可能超过实际文件（流式写入未回填），取两者较小值。
                    var actual = Math.Min((long)chunkSize, fs.Length - fs.Position);
                    if (actual <= 0) return -1;
                    return (long)(actual * 1000.0 / byteRate);
                }
                else
                {
                    // 块大小为奇数时有一个填充字节
                    fs.Position += chunkSize + (chunkSize % 2);
                }
            }
        }
        catch { }
        return -1;
    }

    /// <summary>读取 4 字节的 RIFF 块标识（纯 ASCII）。</summary>
    private static string ReadTag(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : "";
    }
}
