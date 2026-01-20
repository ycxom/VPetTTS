namespace Vpet.Plugin.CustomTTS.Services.Player;

/// <summary>
/// 音频播放服务接口
/// 负责音频文件验证、播放和状态跟踪
/// </summary>
public interface IAudioPlaybackService
{
    // ============================================================================
    // 属性
    // ============================================================================

    /// <summary>
    /// 是否正在播放
    /// </summary>
    bool IsPlaying { get; }

    // ============================================================================
    // 播放控制
    // ============================================================================

    /// <summary>
    /// 播放音频文件
    /// </summary>
    /// <param name="path">音频文件路径</param>
    Task PlayAudioAsync(string path);

    /// <summary>
    /// 停止当前播放
    /// </summary>
    Task StopAsync();
}
