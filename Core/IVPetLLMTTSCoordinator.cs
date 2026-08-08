namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// VPetLLM TTS 协调接口
    /// 供 VPetLLM 插件使用，用于协调 TTS 功能的使用
    /// </summary>
    public interface IVPetLLMTTSCoordinator
    {
        /// <summary>
        /// 检查 VPetTTS 是否可用且启用
        /// </summary>
        /// <returns>如果 VPetTTS 可用且启用返回 true</returns>
        bool IsVPetTTSAvailable();

        /// <summary>
        /// 检查 VPetTTS 是否可以接受新的请求
        /// </summary>
        /// <returns>如果可以接受新请求返回 true</returns>
        bool CanAcceptNewRequests();

        /// <summary>
        /// 获取当前 TTS 状态
        /// </summary>
        /// <returns>TTS 状态接口</returns>
        ITTSState GetTTSState();

        /// <summary>
        /// 请求使用 VPetTTS（返回是否成功获得使用权）
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">要处理的文本</param>
        /// <returns>是否成功获得使用权</returns>
        Task<bool> RequestTTSUsageAsync(string requestId, string text);

        /// <summary>
        /// 释放 TTS 使用权
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        void ReleaseTTSUsage(string requestId);

        /// <summary>
        /// 开始监听 VPetTTS 状态变化
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// 停止监听 VPetTTS 状态变化
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// VPetTTS 可用性变化事件
        /// </summary>
        event EventHandler<TTSAvailabilityEventArgs> VPetTTSAvailabilityChanged;

        /// <summary>
        /// VPetTTS 状态变化事件
        /// </summary>
        event EventHandler<TTSStateChangedEventArgs> VPetTTSStateChanged;

        // ==================== 独占会话管理 ====================

        /// <summary>
        /// 启动独占会话
        /// </summary>
        /// <param name="callerId">调用者 ID</param>
        /// <returns>会话 ID</returns>
        Task<string> StartExclusiveSessionAsync(string callerId);

        /// <summary>
        /// 结束独占会话
        /// </summary>
        /// <param name="callerId">调用者 ID</param>
        /// <param name="sessionId">会话 ID</param>
        Task EndExclusiveSessionAsync(string callerId, string sessionId);

        /// <summary>
        /// 检查是否处于独占会话
        /// </summary>
        bool IsExclusiveSession();

        /// <summary>
        /// 获取独占会话所有者
        /// </summary>
        string? GetExclusiveOwner();

        /// <summary>
        /// 获取当前会话 ID
        /// </summary>
        string? GetCurrentSessionId();

        /// <summary>
        /// 获取当前活跃请求数
        /// </summary>
        int GetActiveRequestCount();

        /// <summary>
        /// 强制清理会话（用于处理会话冲突）
        /// </summary>
        void ForceCleanupSession();

        /// <summary>
        /// 提交 TTS 请求（在会话中）
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="sessionId">会话 ID</param>
        /// <returns>请求 ID</returns>
        Task<string> SubmitTTSAsync(string text, string sessionId);

        /// <summary>
        /// 验证请求
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="sessionId">会话 ID</param>
        bool ValidateRequest(string requestId, string sessionId);

        /// <summary>
        /// 检查请求是否完成
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        Task<bool> IsRequestCompleteAsync(string requestId);

        /// <summary>
        /// 等待某个请求的音频真正开始播放。
        ///
        /// 这是气泡与语音对齐用的锚点：SubmitTTSAsync 返回时音频还没出声（可能还在合成、
        /// 或在排队等前一句播完），调用方若此时就显示气泡，字会比声音早上几百毫秒到几秒。
        /// 等到本方法返回再显示气泡，两者就对齐了。
        /// </summary>
        /// <param name="requestId">请求 ID</param>
        /// <param name="timeoutMs">最长等待时间（毫秒）</param>
        /// <returns>
        /// 起播成功返回音频时长（毫秒，未知为 0），调用方可据此决定气泡的打字速度和停留时长；
        /// 超时、请求不存在、或请求已结束却从未出声（合成失败/被中断）返回 -1，
        /// 此时调用方应当按文本长度估算，照常把气泡放出来。
        /// </returns>
        Task<long> WaitForPlaybackStartAsync(string requestId, int timeoutMs);

        /// <summary>
        /// 检查是否正在处理
        /// </summary>
        bool IsProcessing();

        /// <summary>
        /// 预加载文本（在会话中）
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="sessionId">会话 ID</param>
        Task<bool> PreloadAsync(string text, string sessionId);

        /// <summary>
        /// 检查文本是否已预加载
        /// </summary>
        /// <param name="text">文本</param>
        bool IsPreloaded(string text);

        /// <summary>
        /// 中断当前语音：立即停止播放，作废已提交但还没播出来的请求。
        ///
        /// 用户在 VPetLLM 侧点"中断"时调用。会话本身不结束 —— 调用方通常紧接着
        /// 走正常的 EndExclusiveSessionAsync 收尾。
        /// </summary>
        /// <param name="callerId">调用者 ID（仅用于日志）</param>
        Task InterruptAsync(string callerId);
    }

    /// <summary>
    /// VPetLLM TTS 协调器实现
    /// 用于 VPetLLM 插件与 VPetTTS 插件之间的协调
    /// </summary>
    public class VPetLLMTTSCoordinator : IVPetLLMTTSCoordinator
    {
        private readonly ITTSState _ttsState;
        private readonly ExclusiveSessionManager _sessionManager;
        private readonly IPreloadService _preloadService;
        private ITTSProcessingService _ttsProcessingService;
        private bool _isMonitoring;
        private readonly object _lockObject = new object();

        public event EventHandler<TTSAvailabilityEventArgs> VPetTTSAvailabilityChanged;
        public event EventHandler<TTSStateChangedEventArgs> VPetTTSStateChanged;

        public VPetLLMTTSCoordinator(
            ITTSState ttsState,
            ExclusiveSessionManager sessionManager,
            IPreloadService preloadService,
            ITTSProcessingService ttsProcessingService)
        {
            _ttsState = ttsState ?? throw new ArgumentNullException(nameof(ttsState));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _preloadService = preloadService ?? throw new ArgumentNullException(nameof(preloadService));
            _ttsProcessingService = ttsProcessingService;
        }

        /// <summary>
        /// 设置 TTS 处理服务（用于延迟初始化）
        /// </summary>
        public void SetTTSProcessingService(ITTSProcessingService ttsProcessingService)
        {
            _ttsProcessingService = ttsProcessingService;
            LogMessage("TTS 处理服务已设置");
        }

        public bool IsVPetTTSAvailable()
        {
            return _ttsState is not null && _ttsState.IsEnabled;
        }

        public bool CanAcceptNewRequests()
        {
            return _ttsState is not null && _ttsState.CanAcceptNewRequests;
        }

        public ITTSState GetTTSState()
        {
            return _ttsState;
        }

        public async Task<bool> RequestTTSUsageAsync(string requestId, string text)
        {
            if (!IsVPetTTSAvailable())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 不可用");
                return false;
            }

            if (!CanAcceptNewRequests())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 正忙");
                return false;
            }

            // 等待一小段时间确保状态稳定
            await Task.Delay(10);

            // 再次检查状态
            if (!CanAcceptNewRequests())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 状态已变化");
                return false;
            }

            LogMessage($"请求 {requestId} 成功：可以使用 VPetTTS");
            return true;
        }

        public void ReleaseTTSUsage(string requestId)
        {
            LogMessage($"请求 {requestId} 已释放 TTS 使用权");
        }

        public void StartMonitoring()
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;

                _isMonitoring = true;

                // 订阅状态变化事件
                _ttsState.StateChanged += OnTTSStateChanged;
                _ttsState.AvailabilityChanged += OnTTSAvailabilityChanged;

                LogMessage("开始监听 VPetTTS 状态变化");
            }
        }

        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;

                _isMonitoring = false;

                // 取消订阅状态变化事件
                _ttsState.StateChanged -= OnTTSStateChanged;
                _ttsState.AvailabilityChanged -= OnTTSAvailabilityChanged;

                LogMessage("停止监听 VPetTTS 状态变化");
            }
        }

        private void OnTTSStateChanged(object sender, TTSStateChangedEventArgs e)
        {
            try
            {
                VPetTTSStateChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"处理状态变化事件时发生错误: {ex.Message}");
            }
        }

        private void OnTTSAvailabilityChanged(object sender, TTSAvailabilityEventArgs e)
        {
            try
            {
                VPetTTSAvailabilityChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"处理可用性变化事件时发生错误: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            TTSLogger.Log($"[VPetLLMTTSCoordinator] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        // ==================== 独占会话管理实现 ====================

        public async Task<string> StartExclusiveSessionAsync(string callerId)
        {
            try
            {
                var sessionId = _sessionManager.StartSession(callerId);
                _sessionManager.DisableTextCapture();
                LogMessage($"启动独占会话: {sessionId}，调用者: {callerId}");
                return await Task.FromResult(sessionId);
            }
            catch (Exception ex)
            {
                LogMessage($"启动独占会话失败: {ex.Message}");
                throw;
            }
        }

        public async Task EndExclusiveSessionAsync(string callerId, string sessionId)
        {
            try
            {
                if (_sessionManager.EndSession(callerId, sessionId))
                {
                    _sessionManager.EnableTextCapture();
                    LogMessage($"结束独占会话: {sessionId}");
                }
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogMessage($"结束独占会话失败: {ex.Message}");
                throw;
            }
        }

        public bool IsExclusiveSession()
        {
            return _sessionManager.IsSessionActive();
        }

        public string? GetExclusiveOwner()
        {
            return _sessionManager.GetCurrentOwner();
        }

        public string? GetCurrentSessionId()
        {
            return _sessionManager.GetCurrentSessionId();
        }

        public int GetActiveRequestCount()
        {
            return _sessionManager.GetActiveRequestCount();
        }

        public void ForceCleanupSession()
        {
            try
            {
                var currentSessionId = _sessionManager.GetCurrentSessionId();
                var currentOwner = _sessionManager.GetCurrentOwner();
                
                if (currentSessionId != null)
                {
                    LogMessage($"强制清理会话: {currentSessionId}，所有者: {currentOwner}");
                    
                    // 直接清理会话管理器的状态
                    _sessionManager.CheckAndCleanupTimedOutSession();
                    
                    // 如果还有会话，强制结束
                    if (_sessionManager.IsSessionActive())
                    {
                        LogMessage($"会话仍然活跃，尝试强制结束");
                        // 使用反射访问私有字段进行强制清理
                        var sessionManagerType = _sessionManager.GetType();
                        var currentSessionIdField = sessionManagerType.GetField("_currentSessionId", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var currentOwnerIdField = sessionManagerType.GetField("_currentOwnerId", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var requestMapField = sessionManagerType.GetField("_requestMap", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (currentSessionIdField != null)
                            currentSessionIdField.SetValue(_sessionManager, null);
                        if (currentOwnerIdField != null)
                            currentOwnerIdField.SetValue(_sessionManager, null);
                        if (requestMapField != null)
                        {
                            var requestMap = requestMapField.GetValue(_sessionManager) as System.Collections.IDictionary;
                            requestMap?.Clear();
                        }
                        
                        _sessionManager.EnableTextCapture();
                        LogMessage($"强制清理完成");
                    }
                }
                else
                {
                    LogMessage($"没有活跃会话，无需清理");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"强制清理会话失败: {ex.Message}");
            }
        }

        public async Task<string> SubmitTTSAsync(string text, string sessionId)
        {
            try
            {
                // 注册请求
                var requestId = _sessionManager.RegisterRequest(sessionId, text);
                LogMessage($"提交 TTS 请求: {requestId}，会话: {sessionId}");

                // 处理 TTS（如果有处理服务）
                if (_ttsProcessingService != null)
                {
                    LogMessage($"TTS 处理服务可用，开始异步处理请求: {requestId}");
                    
                    // 异步处理，不等待完成
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            LogMessage($"[Task.Run] 开始处理 TTS 请求: {requestId}");
                            await _ttsProcessingService.ProcessTTSRequestAsync(
                                text,
                                durationMs => _sessionManager.MarkRequestPlaybackStarted(requestId, durationMs));
                            LogMessage($"[Task.Run] TTS 请求处理完成: {requestId}");
                            _sessionManager.MarkRequestComplete(requestId);
                            LogMessage($"[Task.Run] 请求已标记完成: {requestId}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"[Task.Run] 处理 TTS 请求失败: {requestId}, 错误: {ex.Message}");
                            LogMessage($"[Task.Run] 异常堆栈: {ex.StackTrace}");
                            _sessionManager.MarkRequestComplete(requestId);
                        }
                    });
                }
                else
                {
                    LogMessage($"警告: _ttsProcessingService 为 null，无法处理请求 {requestId}");
                    // 标记请求完成，避免永久等待
                    _sessionManager.MarkRequestComplete(requestId);
                }

                return await Task.FromResult(requestId);
            }
            catch (Exception ex)
            {
                LogMessage($"提交 TTS 请求失败: {ex.Message}");
                throw;
            }
        }

        public bool ValidateRequest(string requestId, string sessionId)
        {
            return _sessionManager.ValidateRequest(requestId, sessionId);
        }

        public async Task<bool> IsRequestCompleteAsync(string requestId)
        {
            return await Task.FromResult(_sessionManager.IsRequestComplete(requestId));
        }

        public async Task<long> WaitForPlaybackStartAsync(string requestId, int timeoutMs)
        {
            try
            {
                var durationMs = await _sessionManager.WaitForPlaybackStartAsync(requestId, timeoutMs);

                LogMessage(durationMs >= 0
                    ? $"请求 {requestId} 已起播，音频时长: {durationMs}ms"
                    : $"请求 {requestId} 未能起播（超时/合成失败/已中断）");

                return durationMs;
            }
            catch (Exception ex)
            {
                LogMessage($"等待请求 {requestId} 起播失败: {ex.Message}");
                return ExclusiveSessionManager.PlaybackNeverStarted;
            }
        }

        public bool IsProcessing()
        {
            return _ttsState?.IsProcessing ?? false;
        }

        public async Task<bool> PreloadAsync(string text, string sessionId)
        {
            try
            {
                // 验证会话
                if (_sessionManager.GetCurrentSessionId() != sessionId)
                {
                    LogMessage($"预加载失败：会话 ID 不匹配");
                    return false;
                }

                // 调用预加载服务
                if (_preloadService != null)
                {
                    var requestId = Guid.NewGuid().ToString();
                    var result = await _preloadService.PreloadAudioAsync(text, requestId);
                    
                    if (result.Success)
                    {
                        LogMessage($"预加载成功：{text.Substring(0, Math.Min(20, text.Length))}...");
                        return true;
                    }
                    else
                    {
                        LogMessage($"预加载失败：{result.ErrorMessage}");
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"预加载失败: {ex.Message}");
                return false;
            }
        }

        public bool IsPreloaded(string text)
        {
            try
            {
                return _preloadService?.IsPreloaded(text) ?? false;
            }
            catch
            {
                return false;
            }
        }

        public async Task InterruptAsync(string callerId)
        {
            LogMessage($"收到中断请求，调用者: {callerId}");

            try
            {
                if (_ttsProcessingService != null)
                {
                    await _ttsProcessingService.InterruptAsync();
                }
                else
                {
                    LogMessage("TTS 处理服务未设置，无法停止播放");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"中断播放失败: {ex.Message}");
            }

            // 把在途请求全部标记完成：这些请求已经不会再播出来了，
            // 不标记的话调用方的 IsRequestCompleteAsync 会一直等到超时
            try
            {
                var activeRequests = _sessionManager.GetActiveRequests();
                foreach (var requestId in activeRequests)
                {
                    _sessionManager.MarkRequestComplete(requestId);
                }

                if (activeRequests.Count > 0)
                {
                    LogMessage($"已作废 {activeRequests.Count} 个在途请求");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"作废在途请求失败: {ex.Message}");
            }
        }
    }
}
