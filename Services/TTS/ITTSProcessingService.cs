namespace Vpet.Plugin.CustomTTS.Services.TTS;

/// <summary>
/// TTS 处理服务接口
/// 负责 TTS 请求处理、缓存管理和音频生成
/// </summary>
public interface ITTSProcessingService
{
    // ============================================================================
    // TTS 处理
    // ============================================================================

    /// <summary>
    /// 处理 TTS 请求
    /// </summary>
    /// <param name="text">要转换的文本</param>
    Task ProcessTTSRequestAsync(string text);

    // ============================================================================
    // 缓存管理
    // ============================================================================

    /// <summary>
    /// 检查缓存（预加载 + 常规缓存）
    /// </summary>
    /// <param name="text">要检查的文本</param>
    /// <returns>缓存的音频文件路径，如果不存在则返回 null</returns>
    Task<string> CheckCacheAsync(string text);

    /// <summary>
    /// 生成音频并缓存
    /// </summary>
    /// <param name="text">要转换的文本</param>
    /// <returns>生成的音频文件路径</returns>
    Task<string> GenerateAndCacheAudioAsync(string text);

    // ============================================================================
    // 音频生成
    // ============================================================================

    /// <summary>
    /// 生成音频数据
    /// </summary>
    /// <param name="text">要转换的文本</param>
    /// <returns>音频数据字节数组</returns>
    Task<byte[]> GenerateAudioAsync(string text);
}
