namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 预加载状态枚举
    /// 表示预加载请求的当前状态
    /// </summary>
    public enum PreloadStatus
    {
        /// <summary>
        /// 未知状态（未找到该请求或状态异常）
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 等待中（请求已提交但尚未开始处理）
        /// </summary>
        Pending = 1,

        /// <summary>
        /// 进行中（正在下载或处理音频）
        /// </summary>
        InProgress = 2,

        /// <summary>
        /// 已完成（预加载成功完成）
        /// </summary>
        Completed = 3,

        /// <summary>
        /// 失败（预加载过程中发生错误）
        /// </summary>
        Failed = 4,

        /// <summary>
        /// 已取消（请求被用户或系统取消）
        /// </summary>
        Cancelled = 5
    }

    /// <summary>
    /// PreloadStatus 扩展方法
    /// </summary>
    public static class PreloadStatusExtensions
    {
        /// <summary>
        /// 检查状态是否为终端状态（已完成、失败或取消）
        /// </summary>
        /// <param name="status">预加载状态</param>
        /// <returns>如果是终端状态返回 true</returns>
        public static bool IsTerminal(this PreloadStatus status)
        {
            return status == PreloadStatus.Completed ||
                   status == PreloadStatus.Failed ||
                   status == PreloadStatus.Cancelled;
        }

        /// <summary>
        /// 检查状态是否为活动状态（等待中或进行中）
        /// </summary>
        /// <param name="status">预加载状态</param>
        /// <returns>如果是活动状态返回 true</returns>
        public static bool IsActive(this PreloadStatus status)
        {
            return status == PreloadStatus.Pending ||
                   status == PreloadStatus.InProgress;
        }

        /// <summary>
        /// 检查状态是否为成功状态
        /// </summary>
        /// <param name="status">预加载状态</param>
        /// <returns>如果是成功状态返回 true</returns>
        public static bool IsSuccess(this PreloadStatus status)
        {
            return status == PreloadStatus.Completed;
        }

        /// <summary>
        /// 获取状态的显示名称
        /// </summary>
        /// <param name="status">预加载状态</param>
        /// <returns>状态的中文显示名称</returns>
        public static string GetDisplayName(this PreloadStatus status)
        {
            return status switch
            {
                PreloadStatus.Unknown => "未知",
                PreloadStatus.Pending => "等待中",
                PreloadStatus.InProgress => "进行中",
                PreloadStatus.Completed => "已完成",
                PreloadStatus.Failed => "失败",
                PreloadStatus.Cancelled => "已取消",
                _ => "未定义"
            };
        }

        /// <summary>
        /// 获取状态的英文名称
        /// </summary>
        /// <param name="status">预加载状态</param>
        /// <returns>状态的英文名称</returns>
        public static string GetEnglishName(this PreloadStatus status)
        {
            return status switch
            {
                PreloadStatus.Unknown => "Unknown",
                PreloadStatus.Pending => "Pending",
                PreloadStatus.InProgress => "In Progress",
                PreloadStatus.Completed => "Completed",
                PreloadStatus.Failed => "Failed",
                PreloadStatus.Cancelled => "Cancelled",
                _ => "Undefined"
            };
        }
    }
}