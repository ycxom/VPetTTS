namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 音频预加载服务接口
    /// 提供音频预加载功能，允许外部程序提前下载并缓存音频文件
    /// </summary>
    public interface IPreloadService
    {
        #region 预加载方法

        /// <summary>
        /// 异步预加载单个音频
        /// </summary>
        /// <param name="text">要转换的文本</param>
        /// <param name="requestId">请求唯一标识符</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>预加载结果</returns>
        Task<PreloadResult> PreloadAudioAsync(string text, string requestId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量预加载音频
        /// </summary>
        /// <param name="requests">预加载请求集合</param>
        /// <param name="maxConcurrency">最大并发数，默认为3</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>预加载结果集合</returns>
        Task<IEnumerable<PreloadResult>> PreloadBatchAsync(
            IEnumerable<PreloadRequest> requests,
            int maxConcurrency = 3,
            CancellationToken cancellationToken = default);

        #endregion

        #region 查询方法

        /// <summary>
        /// 检查文本是否已预加载
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <returns>是否已缓存</returns>
        bool IsPreloaded(string text);

        /// <summary>
        /// 获取预加载状态
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <returns>预加载状态</returns>
        PreloadStatus GetPreloadStatus(string requestId);

        /// <summary>
        /// 获取已预加载音频的缓存路径
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <returns>缓存路径，如果未缓存则返回 null</returns>
        string? GetPreloadedPath(string text);

        #endregion

        #region 取消方法

        /// <summary>
        /// 取消指定预加载请求
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <returns>是否成功取消</returns>
        bool CancelPreload(string requestId);

        /// <summary>
        /// 取消所有预加载请求
        /// </summary>
        void CancelAllPreloads();

        #endregion

        #region 事件

        /// <summary>
        /// 预加载开始事件
        /// 当预加载操作开始时触发
        /// </summary>
        event EventHandler<PreloadEventArgs> PreloadStarted;

        /// <summary>
        /// 预加载完成事件
        /// 当预加载操作成功完成时触发
        /// </summary>
        event EventHandler<PreloadEventArgs> PreloadCompleted;

        /// <summary>
        /// 预加载失败事件
        /// 当预加载操作失败时触发
        /// </summary>
        event EventHandler<PreloadEventArgs> PreloadFailed;

        /// <summary>
        /// 预加载取消事件
        /// 当预加载操作被取消时触发
        /// </summary>
        event EventHandler<PreloadEventArgs> PreloadCancelled;

        #endregion

        #region 统计和管理

        /// <summary>
        /// 获取当前活动的预加载任务数量
        /// </summary>
        int ActiveTaskCount { get; }

        /// <summary>
        /// 获取总的预加载请求数量（包括已完成的）
        /// </summary>
        int TotalRequestCount { get; }

        /// <summary>
        /// 清理已完成的任务记录
        /// </summary>
        void CleanupCompletedTasks();

        #endregion
    }
}