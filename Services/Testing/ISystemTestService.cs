namespace Vpet.Plugin.CustomTTS.Services.Testing;

/// <summary>
/// 系统测试服务接口
/// 负责系统测试和诊断功能
/// </summary>
public interface ISystemTestService
{
    // ============================================================================
    // 测试方法
    // ============================================================================

    /// <summary>
    /// 运行综合系统测试
    /// </summary>
    Task<SystemTestResult> RunSystemTestAsync();

    /// <summary>
    /// 验证向后兼容性
    /// </summary>
    bool VerifyBackwardCompatibility();

    // ============================================================================
    // 组件测试
    // ============================================================================

    /// <summary>
    /// 测试播放器检测
    /// </summary>
    Task<bool> TestPlayerDetectionAsync();

    /// <summary>
    /// 测试音频路径处理
    /// </summary>
    Task<bool> TestAudioPathProcessingAsync();

    /// <summary>
    /// 测试错误处理
    /// </summary>
    Task<bool> TestErrorHandlingAsync();

    /// <summary>
    /// 测试状态管理
    /// </summary>
    Task<bool> TestStateManagementAsync();

    /// <summary>
    /// 测试播放器切换
    /// </summary>
    Task<bool> TestPlayerSwitchingAsync();
}
