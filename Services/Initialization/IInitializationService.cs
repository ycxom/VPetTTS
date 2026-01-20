namespace Vpet.Plugin.CustomTTS.Services.Initialization;

/// <summary>
/// 初始化服务接口
/// 负责插件各组件的初始化
/// </summary>
public interface IInitializationService
{
    // ============================================================================
    // 初始化方法
    // ============================================================================

    /// <summary>
    /// 初始化所有组件
    /// </summary>
    Task InitializeAllAsync();

    /// <summary>
    /// 初始化认证提供者
    /// </summary>
    void InitializeAuthProviders();

    /// <summary>
    /// 初始化状态管理器
    /// </summary>
    void InitializeStateManager();

    /// <summary>
    /// 初始化缓存管理器
    /// </summary>
    void InitializeCacheManager();

    /// <summary>
    /// 异步初始化 Free TTS 配置
    /// </summary>
    Task InitializeFreeTTSConfigAsync();

    /// <summary>
    /// 初始化 TTS 管理器
    /// </summary>
    void InitializeTTSManager();

    /// <summary>
    /// 初始化预加载服务
    /// </summary>
    void InitializePreloadService();
}
