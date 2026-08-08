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
    /// <param name="onPlaybackStarted">
    /// 音频真正开始播放时回调，参数为音频时长（毫秒，未知为 -1）。
    /// 合成失败或被中断时不会触发 —— 调用方据此知道"这句没播出来"。
    /// </param>
    Task ProcessTTSRequestAsync(string text, Action<long>? onPlaybackStarted = null);

    /// <summary>
    /// 中断：停掉正在播放的音频，并让已经在途（正在合成/排队等播放）的请求放弃播放。
    /// 供 VPetLLM 等调用方在用户点"中断"时调用。
    /// </summary>
    Task InterruptAsync();

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
