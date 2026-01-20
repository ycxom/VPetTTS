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
    public async Task ProcessTTSRequestAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            // 1. 检查缓存（预加载 + 常规缓存）
            var cachedPath = await CheckCacheAsync(text);

            if (!string.IsNullOrEmpty(cachedPath))
            {
                // 缓存命中，直接播放
                await _audioPlaybackService.PlayAudioAsync(cachedPath);
                return;
            }

            // 2. 生成音频并缓存
            var audioPath = await GenerateAndCacheAudioAsync(text);

            // 3. 播放音频
            if (!string.IsNullOrEmpty(audioPath))
            {
                await _audioPlaybackService.PlayAudioAsync(audioPath);
            }
        }
        catch (Exception ex)
        {
            _stateManager?.SetError($"TTS 处理失败: {ex.Message}", ex, TTSOperationStage.Processing);
            throw;
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
