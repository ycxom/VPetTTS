namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 预加载请求
    /// 包含要预加载的文本内容和请求标识符
    /// </summary>
    public class PreloadRequest
    {
        /// <summary>
        /// 要转换为语音的文本内容
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 请求的唯一标识符
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// 可选的附加数据，用于存储调用方的自定义信息
        /// </summary>
        public object? Tag { get; set; }

        /// <summary>
        /// 创建一个新的预加载请求
        /// </summary>
        public PreloadRequest()
        {
        }

        /// <summary>
        /// 创建一个新的预加载请求
        /// </summary>
        /// <param name="text">要转换的文本</param>
        /// <param name="requestId">请求标识符</param>
        public PreloadRequest(string text, string requestId)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        }

        /// <summary>
        /// 创建一个新的预加载请求
        /// </summary>
        /// <param name="text">要转换的文本</param>
        /// <param name="requestId">请求标识符</param>
        /// <param name="tag">附加数据</param>
        public PreloadRequest(string text, string requestId, object? tag) : this(text, requestId)
        {
            Tag = tag;
        }

        /// <summary>
        /// 验证请求是否有效
        /// </summary>
        /// <returns>如果请求有效返回 true</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Text) && !string.IsNullOrWhiteSpace(RequestId);
        }

        /// <summary>
        /// 返回请求的字符串表示
        /// </summary>
        public override string ToString()
        {
            return $"PreloadRequest[{RequestId}]: \"{Text}\"";
        }
    }
}