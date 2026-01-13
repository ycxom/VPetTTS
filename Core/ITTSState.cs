using System;

namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// TTS 状态接口，供外部 mod（如 VPetLLM）获取 TTS 状态信息
    /// </summary>
    public interface ITTSState
    {
        #region 基本状态属性

        /// <summary>
        /// 是否正在处理 TTS 请求
        /// </summary>
        bool IsProcessing { get; }

        /// <summary>
        /// 是否正在下载音频
        /// </summary>
        bool IsDownloading { get; }

        /// <summary>
        /// 是否正在播放音频
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// TTS 功能是否启用
        /// </summary>
        bool IsEnabled { get; }

        #endregion

        #region 当前信息属性

        /// <summary>
        /// 当前使用的 TTS 提供商
        /// </summary>
        string CurrentProvider { get; }

        /// <summary>
        /// 当前正在处理的文本
        /// </summary>
        string CurrentText { get; }

        /// <summary>
        /// 当前操作进度 (0.0 - 1.0)
        /// </summary>
        double Progress { get; }

        #endregion

        #region 播放进度属性（供 VPetLLM 读取）

        /// <summary>
        /// 当前播放的音频文件路径
        /// </summary>
        string CurrentAudioPath { get; }

        /// <summary>
        /// 当前播放进度 (0.0 - 1.0)
        /// </summary>
        double PlaybackProgress { get; }

        /// <summary>
        /// 当前播放位置（毫秒）
        /// </summary>
        long PlaybackPositionMs { get; }

        /// <summary>
        /// 音频总时长（毫秒），-1 表示未知
        /// </summary>
        long AudioDurationMs { get; }

        /// <summary>
        /// 播放开始时间
        /// </summary>
        DateTime PlaybackStartTime { get; }

        /// <summary>
        /// 预计播放结束时间（基于音频时长计算）
        /// </summary>
        DateTime EstimatedPlaybackEndTime { get; }

        /// <summary>
        /// 播放是否已完成（用于 VPetLLM 判断是否可以发送下一个请求）
        /// </summary>
        bool IsPlaybackComplete { get; }

        /// <summary>
        /// 最后一次心跳时间（用于检测播放器是否正常运行）
        /// </summary>
        DateTime LastHeartbeatTime { get; }

        #endregion

        #region 错误状态属性

        /// <summary>
        /// 是否存在错误
        /// </summary>
        bool HasError { get; }

        /// <summary>
        /// 最后一次错误信息
        /// </summary>
        string LastError { get; }

        /// <summary>
        /// 最后一次错误发生时间
        /// </summary>
        DateTime LastErrorTime { get; }

        #endregion

        #region 统计信息属性

        /// <summary>
        /// 总处理次数
        /// </summary>
        int TotalProcessed { get; }

        /// <summary>
        /// 总处理时间
        /// </summary>
        TimeSpan TotalProcessingTime { get; }

        #endregion

        #region VPetLLM 协调相关属性

        /// <summary>
        /// 是否可以接受新的 TTS 请求
        /// </summary>
        bool CanAcceptNewRequests { get; }

        /// <summary>
        /// 插件版本信息
        /// </summary>
        string PluginVersion { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 状态变化事件
        /// </summary>
        event EventHandler<TTSStateChangedEventArgs> StateChanged;

        /// <summary>
        /// TTS 处理开始事件
        /// </summary>
        event EventHandler<TTSProcessingEventArgs> ProcessingStarted;

        /// <summary>
        /// TTS 处理完成事件
        /// </summary>
        event EventHandler<TTSProcessingEventArgs> ProcessingCompleted;

        /// <summary>
        /// 音频下载开始事件
        /// </summary>
        event EventHandler<TTSDownloadEventArgs> DownloadStarted;

        /// <summary>
        /// 音频下载完成事件
        /// </summary>
        event EventHandler<TTSDownloadEventArgs> DownloadCompleted;

        /// <summary>
        /// 音频播放开始事件
        /// </summary>
        event EventHandler<TTSPlaybackEventArgs> PlaybackStarted;

        /// <summary>
        /// 音频播放完成事件
        /// </summary>
        event EventHandler<TTSPlaybackEventArgs> PlaybackCompleted;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        event EventHandler<TTSErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// TTS 可用性变化事件
        /// </summary>
        event EventHandler<TTSAvailabilityEventArgs> AvailabilityChanged;

        #endregion
    }
}
