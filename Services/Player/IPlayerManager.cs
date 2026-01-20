namespace Vpet.Plugin.CustomTTS.Services.Player;

/// <summary>
/// 播放器管理器接口
/// 负责播放器检测、初始化、切换和状态管理
/// </summary>
public interface IPlayerManager
{
    // ============================================================================
    // 属性
    // ============================================================================

    /// <summary>
    /// 当前播放器类型
    /// </summary>
    PlayerType CurrentPlayerType { get; }

    /// <summary>
    /// 是否使用 mpv 播放器
    /// </summary>
    bool UseMpvPlayer { get; }

    // ============================================================================
    // 初始化和检测
    // ============================================================================

    /// <summary>
    /// 初始化播放器管理器
    /// </summary>
    void Initialize();

    /// <summary>
    /// 刷新播放器检测
    /// </summary>
    void RefreshDetection();

    // ============================================================================
    // 播放器管理
    // ============================================================================

    /// <summary>
    /// 切换到备用播放器
    /// </summary>
    /// <param name="reason">切换原因</param>
    Task SwitchToFallbackPlayerAsync(string reason);

    /// <summary>
    /// 获取最佳可用播放器
    /// </summary>
    PlayerType GetBestAvailablePlayer();

    /// <summary>
    /// 检查播放器可用性
    /// </summary>
    Task CheckPlayerAvailabilityAsync();

    // ============================================================================
    // 状态查询
    // ============================================================================

    /// <summary>
    /// 获取播放器状态
    /// </summary>
    PlayerStatus GetPlayerStatus();

    /// <summary>
    /// 获取播放器详细信息
    /// </summary>
    PlayerDetailInfo GetPlayerDetailInfo();

    /// <summary>
    /// 获取播放器状态描述
    /// </summary>
    string GetPlayerStatusDescription();

    /// <summary>
    /// 获取播放器推荐信息
    /// </summary>
    string GetPlayerRecommendation();

    // ============================================================================
    // 音量管理
    // ============================================================================

    /// <summary>
    /// 更新播放器音量
    /// </summary>
    /// <param name="volume">音量值 (0.0 - 1.0)</param>
    void UpdateVolume(double volume);

    /// <summary>
    /// 同步音量设置到所有播放器
    /// </summary>
    void SyncVolumeSettings();

    // ============================================================================
    // 错误管理
    // ============================================================================

    /// <summary>
    /// 获取播放器错误统计
    /// </summary>
    PlayerErrorStatistics GetPlayerErrorStatistics();

    /// <summary>
    /// 获取最近的播放器错误记录
    /// </summary>
    /// <param name="count">返回的错误记录数量</param>
    List<PlayerErrorRecord> GetRecentPlayerErrors(int count = 10);

    /// <summary>
    /// 导出播放器错误报告
    /// </summary>
    string ExportPlayerErrorReport();

    /// <summary>
    /// 清除播放器错误历史
    /// </summary>
    void ClearPlayerErrorHistory();

    // ============================================================================
    // 事件
    // ============================================================================

    /// <summary>
    /// 播放器变化事件
    /// </summary>
    event EventHandler<PlayerChangedEventArgs> PlayerChanged;
}
