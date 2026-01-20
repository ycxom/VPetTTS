namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 预加载事件参数
    /// 包含预加载事件的详细信息
    /// </summary>
    public class PreloadEventArgs : EventArgs
    {
        /// <summary>
        /// 请求标识符
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// 原始文本内容
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 当前预加载状态
        /// </summary>
        public PreloadStatus Status { get; set; }

        /// <summary>
        /// 缓存文件路径（如果适用）
        /// </summary>
        public string? CachePath { get; set; }

        /// <summary>
        /// 错误信息（如果发生错误）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 事件发生时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 预加载操作耗时（如果适用）
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// 是否命中缓存（如果适用）
        /// </summary>
        public bool? WasCached { get; set; }

        /// <summary>
        /// 创建一个新的预加载事件参数
        /// </summary>
        public PreloadEventArgs()
        {
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 创建一个新的预加载事件参数
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">原始文本</param>
        /// <param name="status">预加载状态</param>
        public PreloadEventArgs(string requestId, string text, PreloadStatus status) : this()
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Status = status;
        }

        /// <summary>
        /// 从预加载结果创建事件参数
        /// </summary>
        /// <param name="result">预加载结果</param>
        /// <param name="status">事件状态</param>
        /// <returns>预加载事件参数</returns>
        public static PreloadEventArgs FromResult(PreloadResult result, PreloadStatus status)
        {
            if (result is null)
                throw new ArgumentNullException(nameof(result));

            return new PreloadEventArgs(result.RequestId, result.Text, status)
            {
                CachePath = result.CachePath,
                ErrorMessage = result.ErrorMessage,
                Duration = result.Duration,
                WasCached = result.WasCached
            };
        }

        /// <summary>
        /// 创建预加载开始事件参数
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">文本内容</param>
        /// <returns>预加载开始事件参数</returns>
        public static PreloadEventArgs CreateStarted(string requestId, string text)
        {
            return new PreloadEventArgs(requestId, text, PreloadStatus.InProgress);
        }

        /// <summary>
        /// 创建预加载完成事件参数
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">文本内容</param>
        /// <param name="cachePath">缓存路径</param>
        /// <param name="duration">耗时</param>
        /// <param name="wasCached">是否命中缓存</param>
        /// <returns>预加载完成事件参数</returns>
        public static PreloadEventArgs CreateCompleted(string requestId, string text, string cachePath, TimeSpan duration, bool wasCached)
        {
            return new PreloadEventArgs(requestId, text, PreloadStatus.Completed)
            {
                CachePath = cachePath,
                Duration = duration,
                WasCached = wasCached
            };
        }

        /// <summary>
        /// 创建预加载失败事件参数
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">文本内容</param>
        /// <param name="errorMessage">错误信息</param>
        /// <param name="duration">耗时</param>
        /// <returns>预加载失败事件参数</returns>
        public static PreloadEventArgs CreateFailed(string requestId, string text, string errorMessage, TimeSpan? duration = null)
        {
            return new PreloadEventArgs(requestId, text, PreloadStatus.Failed)
            {
                ErrorMessage = errorMessage,
                Duration = duration
            };
        }

        /// <summary>
        /// 创建预加载取消事件参数
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">文本内容</param>
        /// <param name="duration">耗时</param>
        /// <returns>预加载取消事件参数</returns>
        public static PreloadEventArgs CreateCancelled(string requestId, string text, TimeSpan? duration = null)
        {
            return new PreloadEventArgs(requestId, text, PreloadStatus.Cancelled)
            {
                Duration = duration
            };
        }

        /// <summary>
        /// 返回事件参数的字符串表示
        /// </summary>
        public override string ToString()
        {
            var statusName = Status.GetDisplayName();
            var timeInfo = Duration.HasValue ? $" ({Duration.Value.TotalMilliseconds:F0}ms)" : "";
            var cacheInfo = WasCached.HasValue && WasCached.Value ? " (Cached)" : "";
            var errorInfo = !string.IsNullOrEmpty(ErrorMessage) ? $" - {ErrorMessage}" : "";

            return $"PreloadEvent[{RequestId}]: {statusName}{cacheInfo}{timeInfo}{errorInfo}";
        }
    }
}