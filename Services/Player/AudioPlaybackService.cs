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

    // 中断标记：内置播放器没有"停"的接口，只能靠这个让等待循环立刻退出
    private volatile bool _stopRequested = false;

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
    public async Task PlayAudioAsync(string path, Action<long>? onPlaybackStarted = null)
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
        _stopRequested = false;
        _stateManager?.SetPlayingState(true, normalizedPath, "", audioDurationMs);
        _isPlaying = true;

        // 起播回报交给具体的播放路径在"播放器真的被拉起来之后"触发（见下面两个 PlayWithXxxAsync）。
        // 不能在这里提前发：这之后还隔着一次 Dispatcher.Invoke（静音占位）加 mpv 进程创建，
        // 或者内置播放器的 Dispatcher 派发，合计几十到几百毫秒且完全看当时机器状态 ——
        // 提前发的结果就是气泡稳定早于声音，早多少还每次都不一样。
        //
        // 只发一次：mpv 失败回退到内置播放器会把这句重播一遍，第二次回报对已经拿到
        // 信号的等待者是空操作，但会白白多打一轮日志、也让日志看起来像播了两次。
        var startNotified = 0;
        void NotifyPlaybackStarted()
        {
            if (Interlocked.Exchange(ref startNotified, 1) != 0)
            {
                return;
            }

            try
            {
                onPlaybackStarted?.Invoke(audioDurationMs);
            }
            catch (Exception ex)
            {
                // 订阅方（VPetLLM 的气泡显示）出问题不能拖累播放本身
                TTSLogger.Log($"[AudioPlaybackService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 起播回调异常: {ex.Message}");
            }
        }

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
                        await PlayWithMpvAsync(normalizedPath, NotifyPlaybackStarted);
                        return; // 播放成功
                    }
                    else if (_playerManager.CurrentPlayerType == PlayerType.VPetBuiltIn)
                    {
                        // 使用 VPet 内置播放器
                        await PlayWithVPetBuiltInAsync(normalizedPath, NotifyPlaybackStarted);
                        return; // 播放成功
                    }
                    else
                    {
                        throw new InvalidOperationException($"无效的播放器类型: {_playerManager.CurrentPlayerType}");
                    }
                }
                catch (Exception ex)
                {
                    // 我们自己停掉的播放不是故障。
                    // 不拦住的话：Kill 掉 mpv 抛出的异常里带着"mpv""进程""killed"这些词，
                    // ShouldRetryWithDifferentPlayer 一律判定为 mpv 故障，于是切到内置播放器
                    // 把这句话重放一遍 —— 用户点的是中断，结果桌宠反而又说了一遍。
                    if (_stopRequested)
                    {
                        TTSLogger.Log($"[AudioPlaybackService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 播放被主动停止，不做故障回退");
                        return;
                    }

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
    /// 停止当前播放。
    ///
    /// 两条播放路径都要停：mpv 有自己的停止接口；内置播放器（以及 mpv 播放期间用来
    /// 保持说话动画的静音占位）跑在宿主的 VoicePlayer 上，用 SilentVoiceAnimationHold.End
    /// 停掉它的 Clock 并复位 PlayingVoice —— 只停 mpv 的话，气泡和说话动画会继续演到超时。
    /// </summary>
    public async Task StopAsync()
    {
        _stopRequested = true;

        try
        {
            // 不看 UseMpvPlayer：播放中途切换过播放器的话，mpv 进程可能还在放，
            // 而这个开关已经指向内置播放器了。StopAsync 对没在播的实例是空操作
            var mpvPlayer = (_playerManager as PlayerManager)?.GetMpvPlayer();
            if (mpvPlayer is not null)
            {
                await mpvPlayer.StopAsync();
            }

            // 停掉宿主侧的播放（内置播放器的真实音频 / mpv 期间的静音占位）
            SilentVoiceAnimationHold.End(_mainWindow);

            _isPlaying = false;
            _stateManager?.SetPlayingState(false);
            TTSLogger.Log($"[AudioPlaybackService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 播放已停止");
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
    /// 使用 mpv 播放器播放音频。
    /// <paramref name="onPlaybackStarted"/> 在 mpv 进程拉起来之后触发，
    /// 而不是在方法入口 —— 静音占位的 Dispatcher.Invoke 和进程创建都要算进"出声之前"。
    /// </summary>
    private async Task PlayWithMpvAsync(string path, Action onPlaybackStarted)
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
                // 启动播放并等待完成。起播回报由 MpvPlayer 在 Process.Start 成功后回调，
                // 此时静音占位已经就位、进程也已拉起，是"声音即将出来"最准的时刻。
                await mpvPlayer.PlayAsync(path, onPlaybackStarted);
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
    /// 使用 VPet 内置播放器播放音频。
    /// <paramref name="onPlaybackStarted"/> 在 PlayVoice 真正派发到 UI 线程之后触发 ——
    /// UI 线程忙的时候这一跳本身就要排队，提前回报等于把这段排队时间算给了气泡。
    /// </summary>
    private async Task PlayWithVPetBuiltInAsync(string path, Action onPlaybackStarted)
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

            // PlayVoice 已经在 UI 线程上执行完毕（MediaElement 已拿到源并开始播放），
            // 到这里再回报起播。放在 Dispatcher 派发之前的话，UI 线程有多忙这里就早多少。
            onPlaybackStarted?.Invoke();

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

            // 等待播放完成（被中断时立刻退出，不再等宿主自然播完）
            while (_mainWindow.Main.PlayingVoice && !_stopRequested && elapsedTime < maxWaitTime)
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

            // 按内容认格式，不看扩展名 —— 缓存里的文件一律叫 .mp3（见 TTSCacheManager
            // 的 $"{cacheKey}.mp3"），但 Free/GPT-SoVITS 返回的其实是 WAV。
            // 早先这里用扩展名决定要不要解析 WAV 头，于是缓存里的 WAV 永远走不到精确解析，
            // 一路掉到下面按 128kbps 的粗估：16bit/32kHz 单声道实际是 512kbps，
            // 估出来正好是真实时长的四倍（日志里 17s 的音频报成 68s）。
            // 两个解析器都自带格式校验（RIFF/WAVE 魔数、MPEG 帧同步字），认不出就返回 -1，
            // 依次试过去是安全的。
            var wavMs = TryGetWavDurationMs(path);
            if (wavMs > 0) return wavMs;

            // MP3（OpenAI 这类云端 TTS 的常见输出）读第一个帧头拿真实码率
            var mp3Ms = TryGetMp3DurationMs(path, info.Length);
            if (mp3Ms > 0) return mp3Ms;

            // 实在认不出的容器按 128kbps 粗估。宁可给个量级正确的估算，
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

    // MPEG-1/2/2.5 Layer III 的码率表（kbps），索引即帧头里的 4 位码率字段。
    // 0 是"自由格式"、15 是无效值，都当作认不出。
    private static readonly int[] Mp3BitRatesV1 =
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
    private static readonly int[] Mp3BitRatesV2 =
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };

    /// <summary>
    /// 读第一个 MP3 帧头拿码率，据此估算时长。CBR 文件（云端 TTS 基本都是）能算得很准。
    /// 不是 MP3、或帧头认不出时返回 -1 交给上层按固定码率粗估。
    ///
    /// 只按扩展名判断不够：有些服务返回的是 .mp3 却带 ID3 头，也有直接叫 .tmp 的，
    /// 所以这里跳过 ID3 之后再找同步字，认得出就用，认不出就退回去。
    /// </summary>
    private static long TryGetMp3DurationMs(string path, long fileLength)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            long offset = 0;

            // ID3v2 头："ID3" + 版本(2) + 标志(1) + 大小(4，同步安全整数，每字节只用低 7 位)
            var header = new byte[10];
            if (fs.Read(header, 0, 10) < 10) return -1;

            if (header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
            {
                var tagSize = (header[6] & 0x7F) << 21 | (header[7] & 0x7F) << 14
                            | (header[8] & 0x7F) << 7 | (header[9] & 0x7F);
                offset = 10 + tagSize;
                if (offset >= fileLength) return -1;
            }

            // 在标签之后找第一个帧同步字（11 位全 1）。正常文件第一个字节就是，
            // 但填充字节的存在让"扫一小段"比"只看一个位置"稳妥。
            fs.Position = offset;
            var buffer = new byte[4096];
            var read = fs.Read(buffer, 0, buffer.Length);

            for (int i = 0; i + 3 < read; i++)
            {
                if (buffer[i] != 0xFF || (buffer[i + 1] & 0xE0) != 0xE0)
                    continue;

                var versionBits = (buffer[i + 1] >> 3) & 0x03;   // 3=MPEG1, 2=MPEG2, 0=MPEG2.5, 1=保留
                var layerBits = (buffer[i + 1] >> 1) & 0x03;     // 1=Layer III
                var bitRateIndex = (buffer[i + 2] >> 4) & 0x0F;

                // 版本保留值直接跳过；只认 Layer III —— 下面两张码率表是 Layer III 的，
                // 拿去套 Layer I/II 会算出一个错得离谱的时长，还不如退回按固定码率粗估。
                // 这两个条件同时也在过滤误命中：音频数据里凑巧出现 0xFF Ex 的概率不低。
                if (versionBits == 1 || layerBits != 1) continue;

                var table = versionBits == 3 ? Mp3BitRatesV1 : Mp3BitRatesV2;
                var kbps = table[bitRateIndex];
                if (kbps <= 0) continue;

                // 音频数据 = 文件去掉 ID3 标签。尾部可能还有 ID3v1(128字节)，
                // 相对整段音频可以忽略，不值得为它再读一次文件尾。
                var audioBytes = fileLength - offset;
                if (audioBytes <= 0) return -1;

                return (long)(audioBytes * 8.0 / kbps);
            }
        }
        catch { }

        return -1;
    }
}
