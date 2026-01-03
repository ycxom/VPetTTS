using System;
using LinePutScript.Converter;

namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// TTS 状态持久化数据模型
    /// 只保存必要的统计信息，不保存运行时状态
    /// </summary>
    public class TTSStateData
    {
        /// <summary>
        /// 总处理次数
        /// </summary>
        [Line]
        public int TotalProcessed { get; set; } = 0;

        /// <summary>
        /// 总处理时间（毫秒）
        /// </summary>
        [Line]
        public long TotalProcessingTimeMs { get; set; } = 0;

        /// <summary>
        /// 最后使用的 TTS 提供商
        /// </summary>
        [Line]
        public string LastProvider { get; set; } = "";

        /// <summary>
        /// 最后活动时间
        /// </summary>
        [Line]
        public DateTime LastActivity { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 关闭时是否正在处理
        /// </summary>
        [Line]
        public bool WasProcessingOnShutdown { get; set; } = false;

        /// <summary>
        /// 总错误次数
        /// </summary>
        [Line]
        public int TotalErrors { get; set; } = 0;

        /// <summary>
        /// 最后一次错误信息
        /// </summary>
        [Line]
        public string LastError { get; set; } = "";

        /// <summary>
        /// 最后一次错误时间
        /// </summary>
        [Line]
        public DateTime LastErrorTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 数据版本（用于未来兼容性）
        /// </summary>
        [Line]
        public int DataVersion { get; set; } = 1;

        /// <summary>
        /// 验证数据有效性
        /// </summary>
        public void Validate()
        {
            if (TotalProcessed < 0) TotalProcessed = 0;
            if (TotalProcessingTimeMs < 0) TotalProcessingTimeMs = 0;
            if (TotalErrors < 0) TotalErrors = 0;
            if (string.IsNullOrEmpty(LastProvider)) LastProvider = "";
            if (string.IsNullOrEmpty(LastError)) LastError = "";
        }

        /// <summary>
        /// 创建默认状态数据
        /// </summary>
        public static TTSStateData CreateDefault()
        {
            return new TTSStateData
            {
                TotalProcessed = 0,
                TotalProcessingTimeMs = 0,
                LastProvider = "",
                LastActivity = DateTime.MinValue,
                WasProcessingOnShutdown = false,
                TotalErrors = 0,
                LastError = "",
                LastErrorTime = DateTime.MinValue,
                DataVersion = 1
            };
        }
    }
}
