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
                    Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 检测到超时会话 {_currentSessionId}，自动清理");
                    _currentSessionId = null;
                    _currentOwnerId = null;
                    _requestMap.Clear();
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
            _requestMap.Clear();

            Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动会话 {_currentSessionId}，所有者: {callerId}");
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
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 没有活跃会话，无法结束");
                return false;
            }

            if (_currentOwnerId != callerId)
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 调用者 {callerId} 不是会话所有者 {_currentOwnerId}");
                return false;
            }

            if (_currentSessionId != sessionId)
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 会话 ID 不匹配，期望: {_currentSessionId}，实际: {sessionId}");
                return false;
            }

            Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 结束会话 {_currentSessionId}，清理 {_requestMap.Count} 个请求");
            _currentSessionId = null;
            _currentOwnerId = null;
            _requestMap.Clear();

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

            Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 注册请求 {requestId}，会话: {sessionId}，文本: {text.Substring(0, Math.Min(20, text.Length))}...");
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
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，没有活跃会话");
                return false;
            }

            if (_currentSessionId != sessionId)
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，会话 ID 不匹配");
                return false;
            }

            if (!_requestMap.ContainsKey(requestId))
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，请求 ID {requestId} 不存在");
                return false;
            }

            var requestInfo = _requestMap[requestId];
            if (requestInfo.SessionId != sessionId)
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 验证失败，请求不属于当前会话");
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
        lock (_lockObject)
        {
            if (_requestMap.TryGetValue(requestId, out var requestInfo))
            {
                requestInfo.IsComplete = true;
                requestInfo.CompletedTime = DateTime.Now;
                _lastActivityTime = DateTime.Now;

                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 标记请求 {requestId} 完成");
            }
        }
    }

    /// <summary>
    /// 检查请求是否完成
    /// </summary>
    public bool IsRequestComplete(string requestId)
    {
        lock (_lockObject)
        {
            if (_requestMap.TryGetValue(requestId, out var requestInfo))
            {
                return requestInfo.IsComplete;
            }
            return false;
        }
    }

    /// <summary>
    /// 注销请求
    /// </summary>
    public void UnregisterRequest(string requestId)
    {
        lock (_lockObject)
        {
            if (_requestMap.Remove(requestId))
            {
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 注销请求 {requestId}");
            }
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
                Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 会话 {_currentSessionId} 超时，自动清理");
                _currentSessionId = null;
                _currentOwnerId = null;
                _requestMap.Clear();
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
            Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 禁用文本获取");
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
            Console.WriteLine($"[ExclusiveSessionManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启用文本获取");
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
}
