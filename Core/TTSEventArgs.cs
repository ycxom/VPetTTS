using System;

namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// TTS 状态变化事件参数
    /// </summary>
    public class TTSStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 变化的属性名称
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// 旧值
        /// </summary>
        public object OldValue { get; set; }

        /// <summary>
        /// 新值
        /// </summary>
        public object NewValue { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public TTSStateChangedEventArgs() { }

        public TTSStateChangedEventArgs(string propertyName, object oldValue, object newValue)
        {
            PropertyName = propertyName;
            OldValue = oldValue;
            NewValue = newValue;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 处理事件参数
    /// </summary>
    public class TTSProcessingEventArgs : EventArgs
    {
        /// <summary>
        /// 处理的文本内容
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 使用的 TTS 提供商
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 处理耗时（仅在完成时有效）
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; } = true;

        public TTSProcessingEventArgs() { }

        public TTSProcessingEventArgs(string text, string provider)
        {
            Text = text;
            Provider = provider;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 下载事件参数
    /// </summary>
    public class TTSDownloadEventArgs : EventArgs
    {
        /// <summary>
        /// 下载 URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 下载进度 (0.0 - 1.0)
        /// </summary>
        public double Progress { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 下载的字节数
        /// </summary>
        public long BytesDownloaded { get; set; }

        /// <summary>
        /// 总字节数
        /// </summary>
        public long TotalBytes { get; set; }

        public TTSDownloadEventArgs() { }

        public TTSDownloadEventArgs(string url, double progress)
        {
            Url = url;
            Progress = progress;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 播放事件参数
    /// </summary>
    public class TTSPlaybackEventArgs : EventArgs
    {
        /// <summary>
        /// 音频文件路径
        /// </summary>
        public string AudioPath { get; set; }

        /// <summary>
        /// 音频时长
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 播放的文本内容
        /// </summary>
        public string Text { get; set; }

        public TTSPlaybackEventArgs() { }

        public TTSPlaybackEventArgs(string audioPath, string text = null)
        {
            AudioPath = audioPath;
            Text = text;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 错误事件参数
    /// </summary>
    public class TTSErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 发生错误时的操作阶段
        /// </summary>
        public TTSOperationStage Stage { get; set; }

        /// <summary>
        /// 相关的文本内容
        /// </summary>
        public string RelatedText { get; set; }

        public TTSErrorEventArgs() { }

        public TTSErrorEventArgs(string error, Exception exception = null)
        {
            Error = error;
            Exception = exception;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 可用性变化事件参数
    /// </summary>
    public class TTSAvailabilityEventArgs : EventArgs
    {
        /// <summary>
        /// 是否可用
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 变化原因
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public TTSAvailabilityEventArgs() { }

        public TTSAvailabilityEventArgs(bool isAvailable, bool isEnabled, string reason = null)
        {
            IsAvailable = isAvailable;
            IsEnabled = isEnabled;
            Reason = reason;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TTS 操作阶段枚举
    /// </summary>
    public enum TTSOperationStage
    {
        /// <summary>
        /// 空闲状态
        /// </summary>
        Idle,

        /// <summary>
        /// 正在处理
        /// </summary>
        Processing,

        /// <summary>
        /// 正在下载
        /// </summary>
        Downloading,

        /// <summary>
        /// 正在播放
        /// </summary>
        Playing,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 发生错误
        /// </summary>
        Error
    }
}
