namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 内部预加载任务跟踪
    /// 用于管理单个预加载任务的状态和生命周期
    /// </summary>
    internal class PreloadTask
    {
        /// <summary>
        /// 请求标识符
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// 要转换的文本
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 当前状态
        /// </summary>
        public PreloadStatus Status { get; set; }

        /// <summary>
        /// 取消令牌源
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();

        /// <summary>
        /// 任务开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 任务结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 执行任务
        /// </summary>
        public Task<PreloadResult>? Task { get; set; }

        /// <summary>
        /// 缓存路径（如果成功）
        /// </summary>
        public string? CachePath { get; set; }

        /// <summary>
        /// 错误信息（如果失败）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 是否命中缓存
        /// </summary>
        public bool WasCached { get; set; }

        /// <summary>
        /// 附加数据
        /// </summary>
        public object? Tag { get; set; }

        /// <summary>
        /// 创建一个新的预加载任务
        /// </summary>
        public PreloadTask()
        {
            StartTime = DateTime.Now;
            Status = PreloadStatus.Pending;
        }

        /// <summary>
        /// 创建一个新的预加载任务
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">文本内容</param>
        public PreloadTask(string requestId, string text) : this()
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>
        /// 获取任务耗时
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                var endTime = EndTime ?? DateTime.Now;
                return endTime - StartTime;
            }
        }

        /// <summary>
        /// 检查任务是否已完成（成功、失败或取消）
        /// </summary>
        public bool IsCompleted => Status.IsTerminal();

        /// <summary>
        /// 检查任务是否正在运行
        /// </summary>
        public bool IsActive => Status.IsActive();

        /// <summary>
        /// 标记任务为进行中
        /// </summary>
        public void MarkInProgress()
        {
            Status = PreloadStatus.InProgress;
        }

        /// <summary>
        /// 标记任务为成功完成
        /// </summary>
        /// <param name="cachePath">缓存路径</param>
        /// <param name="wasCached">是否命中缓存</param>
        public void MarkCompleted(string cachePath, bool wasCached)
        {
            Status = PreloadStatus.Completed;
            CachePath = cachePath;
            WasCached = wasCached;
            EndTime = DateTime.Now;
        }

        /// <summary>
        /// 标记任务为失败
        /// </summary>
        /// <param name="errorMessage">错误信息</param>
        public void MarkFailed(string errorMessage)
        {
            Status = PreloadStatus.Failed;
            ErrorMessage = errorMessage;
            EndTime = DateTime.Now;
        }

        /// <summary>
        /// 标记任务为取消
        /// </summary>
        public void MarkCancelled()
        {
            Status = PreloadStatus.Cancelled;
            EndTime = DateTime.Now;
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        public void Cancel()
        {
            if (!IsCompleted)
            {
                CancellationTokenSource?.Cancel();
                MarkCancelled();
            }
        }

        /// <summary>
        /// 创建预加载结果
        /// </summary>
        /// <returns>预加载结果</returns>
        public PreloadResult CreateResult()
        {
            var result = new PreloadResult(RequestId, Text)
            {
                Success = Status == PreloadStatus.Completed,
                CachePath = CachePath,
                ErrorMessage = ErrorMessage,
                Duration = Duration,
                WasCached = WasCached,
                StartTime = StartTime,
                EndTime = EndTime ?? DateTime.Now
            };

            return result;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            CancellationTokenSource?.Dispose();
        }

        /// <summary>
        /// 返回任务的字符串表示
        /// </summary>
        public override string ToString()
        {
            var statusName = Status.GetDisplayName();
            var durationInfo = $" ({Duration.TotalMilliseconds:F0}ms)";
            return $"PreloadTask[{RequestId}]: {statusName}{durationInfo}";
        }
    }
}