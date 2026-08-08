namespace Vpet.Plugin.CustomTTS.Services.TTS;

/// <summary>
/// TTS 处理服务
/// 负责 TTS 请求处理、缓存管理和音频生成
/// </summary>
public class TTSProcessingService : ITTSProcessingService
{
    private readonly TTSManager _ttsManager;
    private readonly TTSStateManager _stateManager;
    private readonly TTSCacheManager _cacheManager;
    private readonly PreloadService _preloadService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly Setting _settings;

    // 中断世代：每次中断自增，在途请求发现世代变了就放弃播放
    private long _interruptGeneration;

    public TTSProcessingService(
        TTSManager ttsManager,
        TTSStateManager stateManager,
        TTSCacheManager cacheManager,
        PreloadService preloadService,
        IAudioPlaybackService audioPlaybackService,
        Setting settings)
    {
        _ttsManager = ttsManager ?? throw new ArgumentNullException(nameof(ttsManager));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _preloadService = preloadService ?? throw new ArgumentNullException(nameof(preloadService));
        _audioPlaybackService = audioPlaybackService ?? throw new ArgumentNullException(nameof(audioPlaybackService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// 处理 TTS 请求
    /// </summary>
    public async Task ProcessTTSRequestAsync(string text, Action<long>? onPlaybackStarted = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // 本次请求所属的"中断世代"：中断后世代自增，比对不上说明这条请求已经作废。
        // 合成一段音频要几秒，中断多半发生在这期间，所以每个播放点前都要重新比对。
        var generation = Interlocked.Read(ref _interruptGeneration);

        try
        {
            // 1. 检查缓存（预加载 + 常规缓存）
            var cachedPath = await CheckCacheAsync(text);

            if (!string.IsNullOrEmpty(cachedPath))
            {
                // 缓存命中，等待当前播放完成后再播放
                await WaitForCurrentPlaybackAsync();
                if (IsInterrupted(generation, text)) return;
                await _audioPlaybackService.PlayAudioAsync(cachedPath, onPlaybackStarted);
                return;
            }

            // 2. 生成音频并缓存
            if (IsInterrupted(generation, text)) return;
            var audioPath = await GenerateAndCacheAudioAsync(text);

            // 3. 等待当前播放完成后再播放音频
            if (!string.IsNullOrEmpty(audioPath))
            {
                await WaitForCurrentPlaybackAsync();
                if (IsInterrupted(generation, text)) return;
                await _audioPlaybackService.PlayAudioAsync(audioPath, onPlaybackStarted);
            }
        }
        catch (Exception ex)
        {
            _stateManager?.SetError($"TTS 处理失败: {ex.Message}", ex, TTSOperationStage.Processing);
            throw;
        }
    }

    /// <summary>
    /// 中断当前 TTS：停播 + 作废所有在途请求。
    /// </summary>
    public async Task InterruptAsync()
    {
        Interlocked.Increment(ref _interruptGeneration);
        TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 收到中断请求，停止播放并作废在途请求");

        try
        {
            await _audioPlaybackService.StopAsync();
        }
        catch (Exception ex)
        {
            TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 中断停止播放失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 请求所属世代是否已被中断作废
    /// </summary>
    private bool IsInterrupted(long generation, string text)
    {
        if (Interlocked.Read(ref _interruptGeneration) == generation)
            return false;

        var preview = text.Length > 20 ? text.Substring(0, 20) + "..." : text;
        TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 请求已被中断，放弃播放: {preview}");
        return true;
    }

    /// <summary>
    /// 等待当前播放完成
    /// </summary>
    private async Task WaitForCurrentPlaybackAsync()
    {
        try
        {
            // 如果当前正在播放，等待播放完成
            if (_audioPlaybackService.IsPlaying)
            {
                TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 检测到正在播放，等待播放完成...");
                
                int maxWaitTime = 30000; // 最多等待30秒
                int checkInterval = 100;  // 每100ms检查一次
                int elapsedTime = 0;

                while (_audioPlaybackService.IsPlaying && elapsedTime < maxWaitTime)
                {
                    await Task.Delay(checkInterval);
                    elapsedTime += checkInterval;
                }

                if (elapsedTime >= maxWaitTime)
                {
                    TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 等待播放完成超时");
                }
                else
                {
                    TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 播放已完成，等待时间: {elapsedTime}ms");
                }
            }
        }
        catch (Exception ex)
        {
            TTSLogger.Log($"[TTSProcessingService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 等待播放完成失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查缓存（预加载 + 常规缓存）
    /// </summary>
    public async Task<string> CheckCacheAsync(string text)
    {
        try
        {
            // 1. 检查预加载缓存
            if (_preloadService is not null && _preloadService.IsPreloaded(text))
            {
                var preloadedPath = _preloadService.GetPreloadedPath(text);
                if (!string.IsNullOrEmpty(preloadedPath) && File.Exists(preloadedPath))
                {
                    return preloadedPath;
                }
            }

            // 2. 检查常规缓存
            if (_settings.EnableCache)
            {
                var cacheKey = Sub.GetHashCode(text + _settings.Provider).ToString("X");
                var cachedPath = _cacheManager.GetCachePath(cacheKey);

                if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
                {
                    return cachedPath;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 生成音频并缓存
    /// </summary>
    public async Task<string> GenerateAndCacheAudioAsync(string text)
    {
        try
        {
            // 更新状态：开始处理
            _stateManager?.SetProcessingState(true, text, _settings.Provider);
            _stateManager?.SetDownloadingState(true, 0);

            // 1. 生成音频数据
            var audioData = await GenerateAudioAsync(text);

            // 更新状态：下载完成
            _stateManager?.SetDownloadingState(false, 1);

            if (audioData is null || audioData.Length == 0)
            {
                _stateManager?.SetProcessingState(false);
                throw new InvalidOperationException("生成的音频数据为空");
            }

            // 2. 保存到缓存
            string cachedPath;
            if (_settings.EnableCache)
            {
                var cacheKey = Sub.GetHashCode(text + _settings.Provider).ToString("X");
                await _cacheManager.SaveToCacheAsync(cacheKey, audioData);
                cachedPath = Path.Combine(GraphCore.CachePath, "tts", $"{cacheKey}.mp3");
            }
            else
            {
                // 不使用缓存时，创建临时文件
                cachedPath = AudioPathHelper.GenerateSafeTempAudioPath(".mp3");
                await File.WriteAllBytesAsync(cachedPath, audioData);

                // 延迟删除临时文件
                AudioPathHelper.CleanupTempAudioFile(cachedPath, TimeSpan.FromSeconds(10));
            }

            // 更新状态：处理完成
            _stateManager?.SetProcessingState(false);

            return cachedPath;
        }
        catch (Exception ex)
        {
            _stateManager?.SetProcessingState(false);
            _stateManager?.SetError($"生成并缓存音频失败: {ex.Message}", ex, TTSOperationStage.Processing);
            throw;
        }
    }

    /// <summary>
    /// 生成音频数据
    /// </summary>
    public async Task<byte[]> GenerateAudioAsync(string text)
    {
        try
        {
            var audioData = await _ttsManager.GenerateAudioAsync(text);
            return audioData;
        }
        catch (Exception ex)
        {
            _stateManager?.SetError($"生成音频失败: {ex.Message}", ex, TTSOperationStage.Processing);
            throw;
        }
    }
}
