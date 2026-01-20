namespace Vpet.Plugin.CustomTTS.Services.Plugin;

/// <summary>
/// 插件检测服务接口
/// 负责 VPetLLM 和其他 TTS 插件的检测
/// </summary>
public interface IPluginDetectionService
{
    // ============================================================================
    // 属性
    // ============================================================================

    /// <summary>
    /// 是否被软禁用（检测到其他 TTS 插件）
    /// </summary>
    bool IsSoftDisabled { get; }

    /// <summary>
    /// 检测到的其他 TTS 插件名称
    /// </summary>
    string DetectedOtherPluginNames { get; }

    // ============================================================================
    // 检测方法
    // ============================================================================

    /// <summary>
    /// 初始化插件检测服务
    /// </summary>
    void Initialize();

    /// <summary>
    /// 检测 VPetLLM 插件
    /// </summary>
    void DetectVPetLLMPlugin();

    /// <summary>
    /// 检测其他 TTS 插件
    /// </summary>
    void DetectOtherTTSPlugins();

    /// <summary>
    /// 实时检测是否应该跳过 TTS
    /// </summary>
    /// <returns>如果应该跳过 TTS 则返回 true</returns>
    bool ShouldSkipTTS();

    /// <summary>
    /// 刷新检测状态
    /// </summary>
    void RefreshDetection();

    // ============================================================================
    // 检测结果
    // ============================================================================

    /// <summary>
    /// 获取 VPetLLM 检测结果
    /// </summary>
    VPetLLMDetectionResult GetVPetLLMDetectionResult();

    /// <summary>
    /// 获取其他 TTS 插件检测结果
    /// </summary>
    OtherTTSPluginDetectionResult GetOtherTTSPluginDetectionResult();
}
