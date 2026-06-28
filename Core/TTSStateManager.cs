namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// TTS 状态管理器，实现 ITTSState 接口
    /// 提供线程安全的状态管理和事件通知
    /// </summary>
    public class TTSStateManager : ITTSState
    {
        #region 私有字段

        private readonly object _lockObject = new object();
        private readonly Setting _settings;

        // 基本状态
        private volatile bool _isProcessing;
        private volatile bool _isDownloading;
        private volatile bool _isPlaying;

        // 当前信息
        private string _currentProvider = "";
        private string _currentText = "";
        private double _progress;

        // 播放进度信息（供 VPetLLM 读取）
        private string _currentAudioPath = "";
        private double _playbackProgress;
        private long _playbackPositionMs;
        private long _audioDurationMs = -1;
        private DateTime _playbackStartTime = DateTime.MinValue;
        private DateTime _estimatedPlaybackEndTime = DateTime.MinValue;
        private volatile bool _isPlaybackComplete = true;
        private DateTime _lastHeartbeatTime = DateTime.MinValue;

        // 错误状态
        private bool _hasError;
        private string _lastError = "";
        private DateTime _lastErrorTime = DateTime.MinValue;

        // 统计信息
        private int _totalProcessed;
        private long _totalProcessingTimeMs;
        private int _totalErrors;

        // 操作跟踪
        private DateTime _currentOperationStartTime;

        // 版本信息
        private const string PLUGIN_VERSION = "1.0.0";

        // 持久化文件路径
        private string _stateFilePath;

        // 持久化节流：避免每条说话完成都同步写磁盘（低状态高频说话时会持续阻塞调用线程）
        private static readonly TimeSpan _saveThrottle = TimeSpan.FromSeconds(3);
        private DateTime _lastSaveTime = DateTime.MinValue;
        private readonly object _saveLock = new object();

        #endregion

        #region 构造函数

        public TTSStateManager(Setting settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // 设置持久化文件路径
            _stateFilePath = Path.Combine(GraphCore.CachePath, "tts_state.lps");

            // 加载持久化状态
            LoadState();
        }

        #endregion

        #region ITTSState 属性实现

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (_isProcessing != value)
                {
                    var oldValue = _isProcessing;
                    _isProcessing = value;
                    OnStateChanged(nameof(IsProcessing), oldValue, value);
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (_isDownloading != value)
                {
                    var oldValue = _isDownloading;
                    _isDownloading = value;
                    OnStateChanged(nameof(IsDownloading), oldValue, value);
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying != value)
                {
                    var oldValue = _isPlaying;
                    _isPlaying = value;
                    OnStateChanged(nameof(IsPlaying), oldValue, value);
                }
            }
        }

        public bool IsEnabled => _settings?.Enable ?? false;

        public string CurrentProvider
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentProvider;
                }
            }
            private set
            {
                string oldValue = null;
                bool changed = false;
                lock (_lockObject)
                {
                    if (_currentProvider != value)
                    {
                        oldValue = _currentProvider;
                        _currentProvider = value ?? "";
                        changed = true;
                    }
                }
                // 在锁外触发事件，避免订阅者回调与读属性互相阻塞
                if (changed)
                {
                    OnStateChanged(nameof(CurrentProvider), oldValue, value);
                }
            }
        }

        public string CurrentText
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentText;
                }
            }
            private set
            {
                string oldValue = null;
                bool changed = false;
                lock (_lockObject)
                {
                    if (_currentText != value)
                    {
                        oldValue = _currentText;
                        _currentText = value ?? "";
                        changed = true;
                    }
                }
                if (changed)
                {
                    OnStateChanged(nameof(CurrentText), oldValue, value);
                }
            }
        }

        public double Progress
        {
            get
            {
                lock (_lockObject)
                {
                    return _progress;
                }
            }
            private set
            {
                double oldValue = 0;
                double clampedValue;
                bool changed = false;
                lock (_lockObject)
                {
                    clampedValue = Math.Max(0, Math.Min(1, value));
                    if (Math.Abs(_progress - clampedValue) > 0.001)
                    {
                        oldValue = _progress;
                        _progress = clampedValue;
                        changed = true;
                    }
                }
                if (changed)
                {
                    OnStateChanged(nameof(Progress), oldValue, clampedValue);
                }
            }
        }

        #region 播放进度属性实现（供 VPetLLM 读取）

        public string CurrentAudioPath
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentAudioPath;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _currentAudioPath = value ?? "";
                }
            }
        }

        public double PlaybackProgress
        {
            get
            {
                lock (_lockObject)
                {
                    return _playbackProgress;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _playbackProgress = Math.Max(0, Math.Min(1, value));
                }
            }
        }

        public long PlaybackPositionMs
        {
            get
            {
                lock (_lockObject)
                {
                    return _playbackPositionMs;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _playbackPositionMs = Math.Max(0, value);
                }
            }
        }

        public long AudioDurationMs
        {
            get
            {
                lock (_lockObject)
                {
                    return _audioDurationMs;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _audioDurationMs = value;
                }
            }
        }

        public DateTime PlaybackStartTime
        {
            get
            {
                lock (_lockObject)
                {
                    return _playbackStartTime;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _playbackStartTime = value;
                }
            }
        }

        public DateTime EstimatedPlaybackEndTime
        {
            get
            {
                lock (_lockObject)
                {
                    return _estimatedPlaybackEndTime;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _estimatedPlaybackEndTime = value;
                }
            }
        }

        public bool IsPlaybackComplete
        {
            get => _isPlaybackComplete;
            private set
            {
                if (_isPlaybackComplete != value)
                {
                    _isPlaybackComplete = value;
                    OnStateChanged(nameof(IsPlaybackComplete), !value, value);
                }
            }
        }

        public DateTime LastHeartbeatTime
        {
            get
            {
                lock (_lockObject)
                {
                    return _lastHeartbeatTime;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _lastHeartbeatTime = value;
                }
            }
        }

        #endregion

        public bool HasError
        {
            get
            {
                lock (_lockObject)
                {
                    return _hasError;
                }
            }
            private set
            {
                bool oldValue = false;
                bool changed = false;
                lock (_lockObject)
                {
                    if (_hasError != value)
                    {
                        oldValue = _hasError;
                        _hasError = value;
                        changed = true;
                    }
                }
                if (changed)
                {
                    OnStateChanged(nameof(HasError), oldValue, value);
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_lockObject)
                {
                    return _lastError;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _lastError = value ?? "";
                }
            }
        }

        public DateTime LastErrorTime
        {
            get
            {
                lock (_lockObject)
                {
                    return _lastErrorTime;
                }
            }
            private set
            {
                lock (_lockObject)
                {
                    _lastErrorTime = value;
                }
            }
        }

        public int TotalProcessed
        {
            get
            {
                lock (_lockObject)
                {
                    return _totalProcessed;
                }
            }
        }

        public TimeSpan TotalProcessingTime
        {
            get
            {
                lock (_lockObject)
                {
                    return TimeSpan.FromMilliseconds(_totalProcessingTimeMs);
                }
            }
        }

        public bool CanAcceptNewRequests => IsEnabled && !IsProcessing && !IsDownloading && !IsPlaying;

        public string PluginVersion => PLUGIN_VERSION;

        #endregion

        #region 事件

        public event EventHandler<TTSStateChangedEventArgs> StateChanged;
        public event EventHandler<TTSProcessingEventArgs> ProcessingStarted;
        public event EventHandler<TTSProcessingEventArgs> ProcessingCompleted;
        public event EventHandler<TTSDownloadEventArgs> DownloadStarted;
        public event EventHandler<TTSDownloadEventArgs> DownloadCompleted;
        public event EventHandler<TTSPlaybackEventArgs> PlaybackStarted;
        public event EventHandler<TTSPlaybackEventArgs> PlaybackCompleted;
        public event EventHandler<TTSErrorEventArgs> ErrorOccurred;
        public event EventHandler<TTSAvailabilityEventArgs> AvailabilityChanged;

        #endregion

        #region 状态更新方法

        /// <summary>
        /// 设置处理状态
        /// </summary>
        public void SetProcessingState(bool isProcessing, string text = null, string provider = null)
        {
            if (isProcessing)
            {
                // 开始处理时清除之前的错误状态
                ClearError();

                _currentOperationStartTime = DateTime.Now;
                CurrentText = text;
                CurrentProvider = provider ?? _settings?.Provider ?? "";
                Progress = 0;
                IsProcessing = true;

                OnProcessingStarted(new TTSProcessingEventArgs(text, CurrentProvider));
            }
            else
            {
                var duration = DateTime.Now - _currentOperationStartTime;

                lock (_lockObject)
                {
                    _totalProcessed++;
                    _totalProcessingTimeMs += (long)duration.TotalMilliseconds;
                }

                Progress = 1;
                IsProcessing = false;

                OnProcessingCompleted(new TTSProcessingEventArgs(CurrentText, CurrentProvider)
                {
                    Duration = duration,
                    IsSuccess = !HasError
                });

                // 重置当前信息
                CurrentText = "";

                // 保存状态（每次处理完成后）
                SaveState();
            }
        }

        /// <summary>
        /// 设置下载状态
        /// </summary>
        public void SetDownloadingState(bool isDownloading, double progress = 0, string url = null)
        {
            if (isDownloading)
            {
                Progress = progress;
                IsDownloading = true;

                OnDownloadStarted(new TTSDownloadEventArgs(url, progress));
            }
            else
            {
                Progress = 1;
                IsDownloading = false;

                OnDownloadCompleted(new TTSDownloadEventArgs(url, 1));
            }
        }

        /// <summary>
        /// 更新下载进度
        /// </summary>
        public void UpdateDownloadProgress(double progress, long bytesDownloaded = 0, long totalBytes = 0)
        {
            Progress = progress;
        }

        /// <summary>
        /// 设置播放状态
        /// </summary>
        public void SetPlayingState(bool isPlaying, string audioPath = null, string text = null)
        {
            if (isPlaying)
            {
                CurrentAudioPath = audioPath;
                PlaybackStartTime = DateTime.Now;
                PlaybackProgress = 0;
                PlaybackPositionMs = 0;
                IsPlaybackComplete = false;
                LastHeartbeatTime = DateTime.Now;
                IsPlaying = true;

                OnPlaybackStarted(new TTSPlaybackEventArgs(audioPath, text));
            }
            else
            {
                PlaybackProgress = 1;
                IsPlaybackComplete = true;
                LastHeartbeatTime = DateTime.Now;
                IsPlaying = false;

                OnPlaybackCompleted(new TTSPlaybackEventArgs(audioPath, text));
            }
        }

        /// <summary>
        /// 设置播放状态（带音频时长信息）
        /// </summary>
        public void SetPlayingState(bool isPlaying, string audioPath, string text, long audioDurationMs)
        {
            if (isPlaying)
            {
                CurrentAudioPath = audioPath;
                AudioDurationMs = audioDurationMs;
                PlaybackStartTime = DateTime.Now;
                PlaybackProgress = 0;
                PlaybackPositionMs = 0;
                IsPlaybackComplete = false;
                LastHeartbeatTime = DateTime.Now;

                // 计算预计结束时间
                if (audioDurationMs > 0)
                {
                    EstimatedPlaybackEndTime = DateTime.Now.AddMilliseconds(audioDurationMs);
                }
                else
                {
                    EstimatedPlaybackEndTime = DateTime.MinValue;
                }

                IsPlaying = true;

                OnPlaybackStarted(new TTSPlaybackEventArgs(audioPath, text));
            }
            else
            {
                PlaybackProgress = 1;
                IsPlaybackComplete = true;
                LastHeartbeatTime = DateTime.Now;
                IsPlaying = false;

                OnPlaybackCompleted(new TTSPlaybackEventArgs(audioPath, text));
            }
        }

        /// <summary>
        /// 更新播放进度（供播放器调用）
        /// </summary>
        public void UpdatePlaybackProgress(long positionMs, long durationMs = -1)
        {
            PlaybackPositionMs = positionMs;
            LastHeartbeatTime = DateTime.Now;

            if (durationMs > 0)
            {
                AudioDurationMs = durationMs;
                PlaybackProgress = (double)positionMs / durationMs;

                // 更新预计结束时间
                var remainingMs = durationMs - positionMs;
                if (remainingMs > 0)
                {
                    EstimatedPlaybackEndTime = DateTime.Now.AddMilliseconds(remainingMs);
                }
            }
            else if (AudioDurationMs > 0)
            {
                PlaybackProgress = (double)positionMs / AudioDurationMs;
            }
        }

        /// <summary>
        /// 更新心跳时间（表示播放器仍在正常运行）
        /// </summary>
        public void UpdateHeartbeat()
        {
            LastHeartbeatTime = DateTime.Now;
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        public void SetError(string error, Exception exception = null, TTSOperationStage stage = TTSOperationStage.Error)
        {
            lock (_lockObject)
            {
                _hasError = true;
                _lastError = error ?? "";
                _lastErrorTime = DateTime.Now;
                _totalErrors++;
            }

            // 重置操作状态
            IsProcessing = false;
            IsDownloading = false;
            IsPlaying = false;

            OnErrorOccurred(new TTSErrorEventArgs(error, exception)
            {
                Stage = stage,
                RelatedText = CurrentText
            });

            OnStateChanged(nameof(HasError), false, true);

            // 保存状态
            SaveState();
        }

        /// <summary>
        /// 清除错误状态
        /// </summary>
        public void ClearError()
        {
            bool changed = false;
            lock (_lockObject)
            {
                if (_hasError)
                {
                    _hasError = false;
                    changed = true;
                }
            }
            if (changed)
            {
                OnStateChanged(nameof(HasError), true, false);
            }
        }

        /// <summary>
        /// 通知可用性变化
        /// </summary>
        public void NotifyAvailabilityChanged(string reason = null)
        {
            OnAvailabilityChanged(new TTSAvailabilityEventArgs(CanAcceptNewRequests, IsEnabled, reason));
        }

        #endregion

        #region 事件触发方法

        protected virtual void OnStateChanged(string propertyName, object oldValue, object newValue)
        {
            try
            {
                StateChanged?.Invoke(this, new TTSStateChangedEventArgs(propertyName, oldValue, newValue));
            }
            catch (Exception ex)
            {
                LogMessage($"StateChanged 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnProcessingStarted(TTSProcessingEventArgs e)
        {
            try
            {
                ProcessingStarted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"ProcessingStarted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnProcessingCompleted(TTSProcessingEventArgs e)
        {
            try
            {
                ProcessingCompleted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"ProcessingCompleted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnDownloadStarted(TTSDownloadEventArgs e)
        {
            try
            {
                DownloadStarted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"DownloadStarted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnDownloadCompleted(TTSDownloadEventArgs e)
        {
            try
            {
                DownloadCompleted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"DownloadCompleted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnPlaybackStarted(TTSPlaybackEventArgs e)
        {
            try
            {
                PlaybackStarted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"PlaybackStarted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnPlaybackCompleted(TTSPlaybackEventArgs e)
        {
            try
            {
                PlaybackCompleted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"PlaybackCompleted 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnErrorOccurred(TTSErrorEventArgs e)
        {
            try
            {
                ErrorOccurred?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"ErrorOccurred 事件处理异常: {ex.Message}");
            }
        }

        protected virtual void OnAvailabilityChanged(TTSAvailabilityEventArgs e)
        {
            try
            {
                AvailabilityChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"AvailabilityChanged 事件处理异常: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private void LogMessage(string message)
        {
            Console.WriteLine($"[TTSStateManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        #endregion

        #region 持久化方法

        /// <summary>
        /// 保存状态到文件
        /// </summary>
        /// <param name="force">true 时忽略节流并同步写入（用于关闭时确保落盘）</param>
        public void SaveState(bool force = false)
        {
            // 节流：非强制写入时，距上次落盘不足间隔则跳过
            // 仅持久化统计/错误信息，丢失最近一次更新无实质影响
            lock (_saveLock)
            {
                if (!force && DateTime.Now - _lastSaveTime < _saveThrottle)
                {
                    return;
                }
                _lastSaveTime = DateTime.Now;
            }

            // 在调用线程上做快照（廉价），实际磁盘写入放到后台线程，避免阻塞说话/播放线程
            string content;
            try
            {
                var stateData = new TTSStateData
                {
                    TotalProcessed = _totalProcessed,
                    TotalProcessingTimeMs = _totalProcessingTimeMs,
                    LastProvider = _currentProvider,
                    LastActivity = DateTime.Now,
                    WasProcessingOnShutdown = _isProcessing || _isDownloading || _isPlaying,
                    TotalErrors = _totalErrors,
                    LastError = _lastError,
                    LastErrorTime = _lastErrorTime,
                    DataVersion = 1
                };

                content = LPSConvert.SerializeObject(stateData, "TTSState").ToString();
            }
            catch (Exception ex)
            {
                LogMessage($"序列化状态失败: {ex.Message}");
                return;
            }

            if (force)
            {
                WriteStateToDisk(content);
            }
            else
            {
                Task.Run(() => WriteStateToDisk(content));
            }
        }

        private void WriteStateToDisk(string content)
        {
            try
            {
                File.WriteAllText(_stateFilePath, content);
            }
            catch (Exception ex)
            {
                LogMessage($"保存状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载状态
        /// </summary>
        public void LoadState()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    LogMessage("状态文件不存在，使用默认值");
                    return;
                }

                var content = File.ReadAllText(_stateFilePath);
                var lps = new LpsDocument(content);
                var stateData = LPSConvert.DeserializeObject<TTSStateData>(lps["TTSState"]);

                if (stateData is not null)
                {
                    stateData.Validate();

                    lock (_lockObject)
                    {
                        _totalProcessed = stateData.TotalProcessed;
                        _totalProcessingTimeMs = stateData.TotalProcessingTimeMs;
                        _totalErrors = stateData.TotalErrors;

                        // 如果上次关闭时正在处理，记录警告
                        if (stateData.WasProcessingOnShutdown)
                        {
                            LogMessage("警告：上次关闭时 TTS 正在处理中");
                        }
                    }

                    LogMessage($"状态已从文件加载 (总处理: {_totalProcessed}, 总时间: {_totalProcessingTimeMs}ms)");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"加载状态失败: {ex.Message}，使用默认值");
                // 使用默认值
                var defaultData = TTSStateData.CreateDefault();
                lock (_lockObject)
                {
                    _totalProcessed = defaultData.TotalProcessed;
                    _totalProcessingTimeMs = defaultData.TotalProcessingTimeMs;
                    _totalErrors = defaultData.TotalErrors;
                }
            }
        }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void ResetStatistics()
        {
            lock (_lockObject)
            {
                _totalProcessed = 0;
                _totalProcessingTimeMs = 0;
                _totalErrors = 0;
            }

            SaveState(force: true);
            LogMessage("统计信息已重置");
        }

        #endregion
    }
}
