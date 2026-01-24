namespace Vpet.Plugin.CustomTTS.Services.Lifecycle;

/// <summary>
/// 资源清理服务
/// 负责资源的释放和清理
/// </summary>
public class ResourceCleanupService : IResourceCleanupService
{
    private readonly IPlayerManager _playerManager;
    private readonly TTSCacheManager _cacheManager;
    private readonly PreloadService _preloadService;
    private readonly PlayerErrorHandler _errorHandler;

    public ResourceCleanupService(
        IPlayerManager playerManager,
        TTSCacheManager cacheManager,
        PreloadService preloadService,
        PlayerErrorHandler errorHandler)
    {
        // 允许 null 参数，因为在清理时这些服务可能还未初始化或已被释放
        _playerManager = playerManager;
        _cacheManager = cacheManager;
        _preloadService = preloadService;
        _errorHandler = errorHandler;
    }

    /// <summary>
    /// 清理播放器资源
    /// </summary>
    public void CleanupPlayerResources()
    {
        try
        {
            // 获取 mpv 播放器实例并清理
            if (_playerManager is PlayerManager playerManager)
            {
                var mpvPlayer = playerManager.GetMpvPlayer();
                if (mpvPlayer is not null)
                {
                    try
                    {
                        mpvPlayer.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 检查服务是否已初始化
    /// </summary>
    public bool IsInitialized => _playerManager is not null && _cacheManager is not null;

    /// <summary>
    /// 系统关闭时的资源释放
    /// </summary>
    public void OnSystemShutdown()
    {
        try
        {
            // 1. 清理播放器资源
            CleanupPlayerResources();

            // 2. 清理错误处理器
            try
            {
                _errorHandler?.ClearErrorHistory();
            }
            catch { }

            // 3. 释放预加载服务
            try
            {
                _preloadService?.Dispose();
            }
            catch { }

            // 4. 释放缓存管理器
            try
            {
                _cacheManager?.Dispose();
            }
            catch { }
        }
        catch { }
    }

    /// <summary>
    /// 清理临时文件
    /// </summary>
    public void CleanupTempFiles()
    {
        try
        {
            // 清理缓存管理器中的过期文件
            _cacheManager?.CleanupExpiredCache();
        }
        catch { }
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public void Dispose()
    {
        OnSystemShutdown();
    }
}
