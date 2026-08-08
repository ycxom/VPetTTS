namespace Vpet.Plugin.CustomTTS.Core;

/// <summary>
/// 独占会话管理器
/// 管理持久化独占会话状态和会话 ID，支持会话中的多个连续请求
/// </summary>
public class ExclusiveSessionManager
{
    private string? _currentSessionId;
    private string? _currentOwnerId;
    private DateTime _lastActivityTime;
    private readonly Dictionary<string, SessionRequestInfo> _requestMap = new();
    private readonly object _lockObject = new();
    private bool _textCaptureEnabled = true;

    /// <summary>
    /// 启动独占会话
    /// </summary>
    /// <param name="callerId">调用者 ID</param>
    /// <returns>会话 ID (GUID)</returns>
    public string StartSession(string callerId)
    {
        lock (_lockObject)
        {
            // 检查是否有超时会话，如果有则自动清理
            if (_currentSessionId != null)
            {
                if (IsSessionTimedOut())
                {
                    TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 检测到超时会话 {_currentSessionId}，自动清理");
                    _currentSessionId = null;
                    _currentOwnerId = null;
                    ClearRequests();
                    EnableTextCapture();
                }
                else
                {
                    throw new InvalidOperationException($"会话已存在，当前所有者: {_currentOwnerId}");
                }
            }

            _currentSessionId = Guid.NewGuid().ToString();
            _currentOwnerId = callerId;
            _lastActivityTime = DateTime.Now;
            ClearRequests();

            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动会话 {_currentSessionId}，所有者: {callerId}");
            return _currentSessionId;
        }
    }

    /// <summary>
    /// 结束独占会话
    /// </summary>
    /// <param name="callerId">调用者 ID</param>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>是否成功结束</returns>
    public bool EndSession(string callerId, string sessionId)
    {
        lock (_lockObject)
        {
            if (_currentSessionId == null)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 没有活跃会话，无法结束");
                return false;
            }

            if (_currentOwnerId != callerId)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 调用者 {callerId} 不是会话所有者 {_currentOwnerId}");
                return false;
            }

            if (_currentSessionId != sessionId)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 会话 ID 不匹配，期望: {_currentSessionId}，实际: {sessionId}");
                return false;
            }

            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 结束会话 {_currentSessionId}，清理 {_requestMap.Count} 个请求");
            _currentSessionId = null;
            _currentOwnerId = null;
            ClearRequests();

            return true;
        }
    }

    /// <summary>
    /// 检查会话是否活跃
    /// </summary>
    public bool IsSessionActive()
    {
        lock (_lockObject)
        {
            return _currentSessionId != null;
        }
    }

    /// <summary>
    /// 获取当前会话所有者
    /// </summary>
    public string? GetCurrentOwner()
    {
        lock (_lockObject)
        {
            return _currentOwnerId;
        }
    }

    /// <summary>
    /// 获取当前会话 ID
    /// </summary>
    public string? GetCurrentSessionId()
    {
        lock (_lockObject)
        {
            return _currentSessionId;
        }
    }

    /// <summary>
    /// 获取当前活跃请求数
    /// </summary>
    public int GetActiveRequestCount()
    {
        lock (_lockObject)
        {
            return _requestMap.Count(r => !r.Value.IsComplete);
        }
    }

    /// <summary>
    /// 获取最后活动时间
    /// </summary>
    public DateTime GetLastActivityTime()
    {
        lock (_lockObject)
        {
            return _lastActivityTime;
        }
    }

    /// <summary>
    /// 注册请求
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="text">请求文本</param>
    /// <returns>请求 ID (GUID)</returns>
    public string RegisterRequest(string sessionId, string text)
    {
        lock (_lockObject)
        {
            if (_currentSessionId == null)
            {
                throw new InvalidOperationException("没有活跃会话");
            }

            if (_currentSessionId != sessionId)
            {
                throw new InvalidOperationException($"会话 ID 不匹配，期望: {_currentSessionId}，实际: {sessionId}");
            }

            var requestId = Guid.NewGuid().ToString();
            var requestInfo = new SessionRequestInfo
            {
                RequestId = requestId,
                SessionId = sessionId,
                Text = text,
                CreatedTime = DateTime.Now,
                IsComplete = false
            };

            _requestMap[requestId] = requestInfo;
            _lastActivityTime = DateTime.Now;

            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 注册请求 {requestId}，会话: {sessionId}，文本: {text.Substring(0, Math.Min(20, text.Length))}...");
            return requestId;
        }
    }

    /// <summary>
    /// 验证请求
    /// </summary>
    /// <param name="requestId">请求 ID</param>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>是否有效</returns>
    public bool ValidateRequest(string requestId, string sessionId)
    {
        lock (_lockObject)
        {
            if (_currentSessionId == null)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，没有活跃会话");
                return false;
            }

            if (_currentSessionId != sessionId)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，会话 ID 不匹配");
                return false;
            }

            if (!_requestMap.ContainsKey(requestId))
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，请求 ID {requestId} 不存在");
                return false;
            }

            var requestInfo = _requestMap[requestId];
            if (requestInfo.SessionId != sessionId)
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，请求不属于当前会话");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 标记请求完成
    /// </summary>
    public void MarkRequestComplete(string requestId)
    {
        SessionRequestInfo? requestInfo;

        lock (_lockObject)
        {
            if (!_requestMap.TryGetValue(requestId, out requestInfo))
            {
                return;
            }

            requestInfo.IsComplete = true;
            requestInfo.CompletedTime = DateTime.Now;
            _lastActivityTime = DateTime.Now;

            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 标记请求 {requestId} 完成");
        }

        // 请求走完了却从没起播（合成失败、被中断、缓存缺失），说明音频不会来了。
        // 必须把起播等待者放掉，否则 VPetLLM 会一直等到超时才肯把气泡放出来 —— 表现为"没声音也没字"。
        // 在锁外触发：TrySetResult 会同步跑续体，持锁调用可能与调用方的其它会话操作互等。
        requestInfo.PlaybackStartedSource.TrySetResult(PlaybackNeverStarted);
    }

    /// <summary>
    /// 标记请求的音频已经真正开始播放，并带上音频时长。
    ///
    /// 这是气泡与语音对齐的锚点：VPetLLM 等到这个信号才显示气泡，
    /// 并用 <paramref name="audioDurationMs"/> 决定气泡的打字速度和停留时长。
    /// </summary>
    /// <param name="requestId">请求 ID</param>
    /// <param name="audioDurationMs">音频时长（毫秒），未知时传 0</param>
    public void MarkRequestPlaybackStarted(string requestId, long audioDurationMs)
    {
        SessionRequestInfo? requestInfo;

        lock (_lockObject)
        {
            if (!_requestMap.TryGetValue(requestId, out requestInfo))
            {
                return;
            }

            requestInfo.PlaybackStartTime = DateTime.Now;
            requestInfo.AudioDurationMs = audioDurationMs;
            _lastActivityTime = DateTime.Now;

            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 请求 {requestId} 音频起播，时长: {audioDurationMs}ms");
        }

        requestInfo.PlaybackStartedSource.TrySetResult(Math.Max(0, audioDurationMs));
    }

    /// <summary>
    /// 等待某个请求的音频真正起播。
    /// </summary>
    /// <param name="requestId">请求 ID</param>
    /// <param name="timeoutMs">最长等待时间（毫秒）</param>
    /// <returns>
    /// 起播成功返回音频时长（毫秒，未知为 0）；
    /// 请求不存在、超时、或请求已结束却从未起播，返回 <see cref="PlaybackNeverStarted"/>（-1）。
    /// </returns>
    public async Task<long> WaitForPlaybackStartAsync(string requestId, int timeoutMs)
    {
        SessionRequestInfo? requestInfo;

        lock (_lockObject)
        {
            if (!_requestMap.TryGetValue(requestId, out requestInfo))
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 等待起播失败：请求 {requestId} 不存在");
                return PlaybackNeverStarted;
            }
        }

        var startedTask = requestInfo.PlaybackStartedSource.Task;
        if (startedTask.IsCompleted)
        {
            return await startedTask;
        }

        var completed = await Task.WhenAny(startedTask, Task.Delay(timeoutMs));
        if (completed != startedTask)
        {
            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 等待请求 {requestId} 起播超时 ({timeoutMs}ms)");
            return PlaybackNeverStarted;
        }

        return await startedTask;
    }

    /// <summary>
    /// <see cref="WaitForPlaybackStartAsync"/> 的哨兵值：音频始终没有播出来。
    /// </summary>
    public const long PlaybackNeverStarted = -1;

    /// <summary>
    /// 检查请求是否完成。
    ///
    /// 请求不在表里一律算完成：要么从没注册过，要么已经随会话结束/超时清理被抹掉了 ——
    /// 两种情况都不会再有人来标记它完成。返回 false 的话调用方会一直轮询到自己的超时
    /// （默认 60 秒），期间桌宠卡在那一句上不动。
    /// </summary>
    public bool IsRequestComplete(string requestId)
    {
        lock (_lockObject)
        {
            if (_requestMap.TryGetValue(requestId, out var requestInfo))
            {
                return requestInfo.IsComplete;
            }
            return true;
        }
    }

    /// <summary>
    /// 注销请求
    /// </summary>
    public void UnregisterRequest(string requestId)
    {
        SessionRequestInfo? removed = null;

        lock (_lockObject)
        {
            if (_requestMap.TryGetValue(requestId, out removed))
            {
                _requestMap.Remove(requestId);
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 注销请求 {requestId}");
            }
        }

        removed?.PlaybackStartedSource.TrySetResult(PlaybackNeverStarted);
    }

    /// <summary>
    /// 清空请求表，并放掉所有还挂在起播信号上的等待者。
    /// 请求随会话一起没了，音频自然不会再来 —— 不放的话调用方要空等到超时。
    /// 调用方需持有 <see cref="_lockObject"/>。
    /// </summary>
    private void ClearRequests()
    {
        var pending = _requestMap.Values.ToList();
        _requestMap.Clear();

        foreach (var request in pending)
        {
            request.PlaybackStartedSource.TrySetResult(PlaybackNeverStarted);
        }
    }

    /// <summary>
    /// 获取所有活跃请求 ID
    /// </summary>
    public List<string> GetActiveRequests()
    {
        lock (_lockObject)
        {
            return _requestMap
                .Where(r => !r.Value.IsComplete)
                .Select(r => r.Key)
                .ToList();
        }
    }

    /// <summary>
    /// 检查会话是否超时
    /// </summary>
    /// <param name="timeoutMs">超时时间（毫秒），默认 60 秒</param>
    public bool IsSessionTimedOut(int timeoutMs = 60000)
    {
        lock (_lockObject)
        {
            if (_currentSessionId == null)
            {
                return false;
            }

            var elapsed = (DateTime.Now - _lastActivityTime).TotalMilliseconds;
            return elapsed > timeoutMs;
        }
    }

    /// <summary>
    /// 更新活动时间
    /// </summary>
    public void UpdateActivity()
    {
        lock (_lockObject)
        {
            _lastActivityTime = DateTime.Now;
        }
    }

    /// <summary>
    /// 检查并清理超时会话
    /// </summary>
    public void CheckAndCleanupTimedOutSession()
    {
        lock (_lockObject)
        {
            if (_currentSessionId != null && IsSessionTimedOut())
            {
                TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 会话 {_currentSessionId} 超时，自动清理");
                _currentSessionId = null;
                _currentOwnerId = null;
                ClearRequests();
                EnableTextCapture();
            }
        }
    }

    /// <summary>
    /// 禁用文本获取
    /// </summary>
    public void DisableTextCapture()
    {
        lock (_lockObject)
        {
            _textCaptureEnabled = false;
            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 禁用文本获取");
        }
    }

    /// <summary>
    /// 启用文本获取
    /// </summary>
    public void EnableTextCapture()
    {
        lock (_lockObject)
        {
            _textCaptureEnabled = true;
            TTSLogger.Log($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启用文本获取");
        }
    }

    /// <summary>
    /// 检查文本获取是否启用
    /// </summary>
    public bool IsTextCaptureEnabled()
    {
        lock (_lockObject)
        {
            return _textCaptureEnabled;
        }
    }
}

/// <summary>
/// 会话请求信息
/// </summary>
public class SessionRequestInfo
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public bool IsComplete { get; set; }

    /// <summary>
    /// 音频真正开始播放的时刻；从未起播时为 null
    /// </summary>
    public DateTime? PlaybackStartTime { get; set; }

    /// <summary>
    /// 本次请求音频的时长（毫秒），未知为 0
    /// </summary>
    public long AudioDurationMs { get; set; }

    /// <summary>
    /// 起播信号：起播时置为音频时长，请求结束却没播出来时置为
    /// <see cref="ExclusiveSessionManager.PlaybackNeverStarted"/>。
    ///
    /// 用 RunContinuationsAsynchronously：完成这个源的是播放线程，
    /// 让等待方的续体在自己的线程上跑，别把气泡显示的开销压回播放路径。
    /// </summary>
    public TaskCompletionSource<long> PlaybackStartedSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
