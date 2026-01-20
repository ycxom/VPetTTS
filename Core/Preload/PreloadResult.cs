namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 预加载结果
    /// 包含预加载操作的结果信息
    /// </summary>
    public class PreloadResult
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
        /// 预加载是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 缓存文件路径，成功时包含实际路径
        /// </summary>
        public string? CachePath { get; set; }

        /// <summary>
        /// 错误信息，失败时包含详细错误描述
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 预加载操作耗时
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 是否命中缓存（true 表示从缓存获取，false 表示新下载）
        /// </summary>
        public bool WasCached { get; set; }

        /// <summary>
        /// 预加载开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 预加载完成时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 创建一个新的预加载结果
        /// </summary>
        public PreloadResult()
        {
            StartTime = DateTime.Now;
        }

        /// <summary>
        /// 创建一个新的预加载结果
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">原始文本</param>
        public PreloadResult(string requestId, string text) : this()
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>
        /// 创建一个成功的预加载结果
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">原始文本</param>
        /// <param name="cachePath">缓存路径</param>
        /// <param name="wasCached">是否命中缓存</param>
        /// <returns>成功的预加载结果</returns>
        public static PreloadResult CreateSuccess(string requestId, string text, string cachePath, bool wasCached)
        {
            return new PreloadResult(requestId, text)
            {
                Success = true,
                CachePath = cachePath,
                WasCached = wasCached,
                EndTime = DateTime.Now
            };
        }

        /// <summary>
        /// 创建一个失败的预加载结果
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">原始文本</param>
        /// <param name="errorMessage">错误信息</param>
        /// <returns>失败的预加载结果</returns>
        public static PreloadResult CreateFailure(string requestId, string text, string errorMessage)
        {
            return new PreloadResult(requestId, text)
            {
                Success = false,
                ErrorMessage = errorMessage,
                EndTime = DateTime.Now
            };
        }

        /// <summary>
        /// 完成预加载结果（设置结束时间和耗时）
        /// </summary>
        public void Complete()
        {
            EndTime = DateTime.Now;
            Duration = EndTime - StartTime;
        }

        /// <summary>
        /// 返回结果的字符串表示
        /// </summary>
        public override string ToString()
        {
            var status = Success ? "Success" : "Failed";
            var cacheInfo = WasCached ? " (Cached)" : "";
            var error = !Success && !string.IsNullOrEmpty(ErrorMessage) ? $" - {ErrorMessage}" : "";

            return $"PreloadResult[{RequestId}]: {status}{cacheInfo} ({Duration.TotalMilliseconds:F0}ms){error}";
        }
    }
}