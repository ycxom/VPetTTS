namespace Vpet.Plugin.CustomTTS.Services.Lifecycle;

/// <summary>
/// 资源清理服务接口
/// 负责资源的释放和清理
/// </summary>
public interface IResourceCleanupService
{
    // ============================================================================
    // 清理方法
    // ============================================================================

    /// <summary>
    /// 清理播放器资源
    /// </summary>
    void CleanupPlayerResources();

    /// <summary>
    /// 系统关闭时的资源释放
    /// </summary>
    void OnSystemShutdown();

    /// <summary>
    /// 清理临时文件
    /// </summary>
    void CleanupTempFiles();

    /// <summary>
    /// 释放所有资源
    /// </summary>
    void Dispose();
}
