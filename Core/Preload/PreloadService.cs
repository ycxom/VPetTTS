using System.Collections.Concurrent;

namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 音频预加载服务实现
    /// 提供音频预加载功能，支持单个和批量预加载
    /// </summary>
    public class PreloadService : IPreloadService, IDisposable
    {
        #region 私有字段

        private readonly TTSManager _ttsManager;
        private readonly TTSCacheManager _cacheManager;
        private readonly Setting _settings;
        private readonly ConcurrentDictionary<string, PreloadTask> _activeTasks;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        #endregion

        #region 事件

        public event EventHandler<PreloadEventArgs>? PreloadStarted;
        public event EventHandler<PreloadEventArgs>? PreloadCompleted;
        public event EventHandler<PreloadEventArgs>? PreloadFailed;
        public event EventHandler<PreloadEventArgs>? PreloadCancelled;

        #endregion

        #region 属性

        /// <summary>
        /// 获取当前活动的预加载任务数量
        /// </summary>
        public int ActiveTaskCount => _activeTasks.Values.Count(t => t.IsActive);

        /// <summary>
        /// 获取总的预加载请求数量（包括已完成的）
        /// </summary>
        public int TotalRequestCount => _activeTasks.Count;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建预加载服务实例
        /// </summary>
        /// <param name="ttsManager">TTS管理器</param>
        /// <param name="cacheManager">缓存管理器</param>
        /// <param name="settings">设置</param>
        public PreloadService(TTSManager ttsManager, TTSCacheManager cacheManager, Setting settings)
        {
            _ttsManager = ttsManager ?? throw new ArgumentNullException(nameof(ttsManager));
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _activeTasks = new ConcurrentDictionary<string, PreloadTask>();

            LogMessage("预加载服务已初始化");
        }

        #endregion

        #region 预加载方法（待实现）

        /// <summary>
        /// 异步预加载单个音频
        /// </summary>
        public async Task<PreloadResult> PreloadAudioAsync(string text, string requestId, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.Now;

            try
            {
                // 验证输入参数
                if (string.IsNullOrWhiteSpace(text))
                {
                    var errorResult = PreloadResult.CreateFailure(requestId, text ?? "", "Text cannot be empty or whitespace");
                    LogMessage($"预加载失败 [{requestId}]: 文本为空");
                    return errorResult;
                }

                if (string.IsNullOrWhiteSpace(requestId))
                {
                    var errorResult = PreloadResult.CreateFailure(requestId ?? "", text, "RequestId cannot be empty or whitespace");
                    LogMessage($"预加载失败: RequestId 为空");
                    return errorResult;
                }

                // 检查是否已存在相同的请求
                if (_activeTasks.ContainsKey(requestId))
                {
                    var errorResult = PreloadResult.CreateFailure(requestId, text, "A request with the same RequestId is already in progress");
                    LogMessage($"预加载失败 [{requestId}]: 相同的请求ID已存在");
                    return errorResult;
                }

                // 生成缓存键
                var cacheKey = GenerateCacheKey(text);

                // 检查缓存是否已存在
                if (_cacheManager.HasCache(cacheKey))
                {
                    var cachePath = _cacheManager.GetCachePath(cacheKey);
                    if (!string.IsNullOrEmpty(cachePath))
                    {
                        var cachedResult = PreloadResult.CreateSuccess(requestId, text, cachePath, true);
                        cachedResult.StartTime = startTime;
                        cachedResult.Complete();

                        LogMessage($"预加载命中缓存 [{requestId}]: {text} -> {cachePath}");

                        // 触发完成事件
                        var completedEvent = PreloadEventArgs.CreateCompleted(requestId, text, cachePath, cachedResult.Duration, true);
                        OnPreloadCompleted(completedEvent);

                        return cachedResult;
                    }
                }

                // 创建预加载任务
                var preloadTask = new PreloadTask(requestId, text);
                if (!_activeTasks.TryAdd(requestId, preloadTask))
                {
                    var errorResult = PreloadResult.CreateFailure(requestId, text, "Failed to add task to active tasks");
                    LogMessage($"预加载失败 [{requestId}]: 无法添加到活动任务列表");
                    return errorResult;
                }

                try
                {
                    // 触发开始事件
                    var startedEvent = PreloadEventArgs.CreateStarted(requestId, text);
                    OnPreloadStarted(startedEvent);

                    // 标记任务为进行中
                    preloadTask.MarkInProgress();
                    LogMessage($"开始预加载 [{requestId}]: {text}");

                    // 创建组合取消令牌
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, preloadTask.CancellationTokenSource.Token);

                    // 执行 TTS 生成
                    var audioData = await _ttsManager.GenerateAudioAsync(text);

                    // 检查取消
                    combinedCts.Token.ThrowIfCancellationRequested();

                    if (audioData is null || audioData.Length == 0)
                    {
                        var errorMessage = "TTS service returned empty audio data";
                        preloadTask.MarkFailed(errorMessage);

                        var errorResult = PreloadResult.CreateFailure(requestId, text, errorMessage);
                        errorResult.StartTime = startTime;
                        errorResult.Complete();

                        LogMessage($"预加载失败 [{requestId}]: {errorMessage}");

                        // 触发失败事件
                        var failedEvent = PreloadEventArgs.CreateFailed(requestId, text, errorMessage, errorResult.Duration);
                        OnPreloadFailed(failedEvent);

                        return errorResult;
                    }

                    // 保存到缓存
                    await _cacheManager.SaveToCacheAsync(cacheKey, audioData);

                    // 检查取消
                    combinedCts.Token.ThrowIfCancellationRequested();

                    // 获取缓存路径
                    var finalCachePath = _cacheManager.GetCachePath(cacheKey);
                    if (string.IsNullOrEmpty(finalCachePath))
                    {
                        var errorMessage = "Failed to retrieve cache path after saving";
                        preloadTask.MarkFailed(errorMessage);

                        var errorResult = PreloadResult.CreateFailure(requestId, text, errorMessage);
                        errorResult.StartTime = startTime;
                        errorResult.Complete();

                        LogMessage($"预加载失败 [{requestId}]: {errorMessage}");

                        // 触发失败事件
                        var failedEvent = PreloadEventArgs.CreateFailed(requestId, text, errorMessage, errorResult.Duration);
                        OnPreloadFailed(failedEvent);

                        return errorResult;
                    }

                    // 标记任务完成
                    preloadTask.MarkCompleted(finalCachePath, false);

                    var successResult = PreloadResult.CreateSuccess(requestId, text, finalCachePath, false);
                    successResult.StartTime = startTime;
                    successResult.Complete();

                    LogMessage($"预加载完成 [{requestId}]: {text} -> {finalCachePath} ({successResult.Duration.TotalMilliseconds:F0}ms)");

                    // 触发完成事件
                    var completedEvent = PreloadEventArgs.CreateCompleted(requestId, text, finalCachePath, successResult.Duration, false);
                    OnPreloadCompleted(completedEvent);

                    return successResult;
                }
                catch (OperationCanceledException)
                {
                    preloadTask.MarkCancelled();

                    var cancelledResult = PreloadResult.CreateFailure(requestId, text, "Preload was cancelled");
                    cancelledResult.StartTime = startTime;
                    cancelledResult.Complete();

                    LogMessage($"预加载已取消 [{requestId}]: {text}");

                    // 触发取消事件
                    var cancelledEvent = PreloadEventArgs.CreateCancelled(requestId, text, cancelledResult.Duration);
                    OnPreloadCancelled(cancelledEvent);

                    return cancelledResult;
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Preload failed: {ex.Message}";
                    preloadTask.MarkFailed(errorMessage);

                    var errorResult = PreloadResult.CreateFailure(requestId, text, errorMessage);
                    errorResult.StartTime = startTime;
                    errorResult.Complete();

                    LogMessage($"预加载异常 [{requestId}]: {errorMessage}");

                    // 触发失败事件
                    var failedEvent = PreloadEventArgs.CreateFailed(requestId, text, errorMessage, errorResult.Duration);
                    OnPreloadFailed(failedEvent);

                    return errorResult;
                }
            }
            catch (Exception ex)
            {
                // 外层异常处理
                var errorMessage = $"Unexpected error during preload: {ex.Message}";
                var errorResult = PreloadResult.CreateFailure(requestId, text, errorMessage);
                errorResult.StartTime = startTime;
                errorResult.Complete();

                LogMessage($"预加载意外错误 [{requestId}]: {errorMessage}");

                return errorResult;
            }
        }

        /// <summary>
        /// 批量预加载音频
        /// </summary>
        public async Task<IEnumerable<PreloadResult>> PreloadBatchAsync(
            IEnumerable<PreloadRequest> requests,
            int maxConcurrency = 3,
            CancellationToken cancellationToken = default)
        {
            if (requests is null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            var requestList = requests.ToList();
            if (requestList.Count == 0)
            {
                return new List<PreloadResult>();
            }

            if (maxConcurrency <= 0)
            {
                maxConcurrency = 1;
            }

            LogMessage($"开始批量预加载: {requestList.Count} 个请求，最大并发数: {maxConcurrency}");

            var results = new List<PreloadResult>();
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task<PreloadResult>>();

            try
            {
                // 为每个请求创建预加载任务
                foreach (var request in requestList)
                {
                    if (request is null)
                    {
                        // 跳过空请求
                        var nullResult = PreloadResult.CreateFailure("", "", "Request is null");
                        results.Add(nullResult);
                        continue;
                    }

                    if (!request.IsValid())
                    {
                        // 跳过无效请求
                        var invalidResult = PreloadResult.CreateFailure(
                            request.RequestId ?? "",
                            request.Text ?? "",
                            "Request is invalid: Text and RequestId cannot be empty");
                        results.Add(invalidResult);
                        continue;
                    }

                    // 创建受信号量控制的任务
                    var task = CreateSemaphoreControlledTask(request, semaphore, cancellationToken);
                    tasks.Add(task);
                }

                // 等待所有任务完成
                var taskResults = await Task.WhenAll(tasks);
                results.AddRange(taskResults);

                // 统计结果
                var successCount = results.Count(r => r.Success);
                var failureCount = results.Count(r => !r.Success);
                var cachedCount = results.Count(r => r.Success && r.WasCached);
                var downloadedCount = results.Count(r => r.Success && !r.WasCached);

                LogMessage($"批量预加载完成: 总数 {results.Count}, 成功 {successCount} (缓存 {cachedCount}, 下载 {downloadedCount}), 失败 {failureCount}");

                return results;
            }
            catch (Exception ex)
            {
                LogMessage($"批量预加载发生异常: {ex.Message}");

                // 为未完成的请求创建失败结果
                var remainingRequests = requestList.Skip(results.Count);
                foreach (var request in remainingRequests)
                {
                    if (request is not null)
                    {
                        var errorResult = PreloadResult.CreateFailure(
                            request.RequestId ?? "",
                            request.Text ?? "",
                            $"Batch operation failed: {ex.Message}");
                        results.Add(errorResult);
                    }
                }

                return results;
            }
            finally
            {
                semaphore?.Dispose();
            }
        }

        /// <summary>
        /// 创建受信号量控制的预加载任务
        /// </summary>
        private async Task<PreloadResult> CreateSemaphoreControlledTask(
            PreloadRequest request,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                return await PreloadAudioAsync(request.Text, request.RequestId, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }

        #endregion

        #region 查询方法（待实现）

        /// <summary>
        /// 检查文本是否已预加载
        /// </summary>
        public bool IsPreloaded(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var cacheKey = GenerateCacheKey(text);
                return _cacheManager.HasCache(cacheKey);
            }
            catch (Exception ex)
            {
                LogMessage($"检查预加载状态时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取预加载状态
        /// </summary>
        public PreloadStatus GetPreloadStatus(string requestId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    return PreloadStatus.Unknown;
                }

                if (_activeTasks.TryGetValue(requestId, out var task))
                {
                    return task.Status;
                }

                return PreloadStatus.Unknown;
            }
            catch (Exception ex)
            {
                LogMessage($"获取预加载状态时发生错误: {ex.Message}");
                return PreloadStatus.Unknown;
            }
        }

        /// <summary>
        /// 获取已预加载音频的缓存路径
        /// </summary>
        public string? GetPreloadedPath(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                var cacheKey = GenerateCacheKey(text);
                return _cacheManager.GetCachePath(cacheKey);
            }
            catch (Exception ex)
            {
                LogMessage($"获取预加载路径时发生错误: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 取消方法（待实现）

        /// <summary>
        /// 取消指定预加载请求
        /// </summary>
        public bool CancelPreload(string requestId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    return false;
                }

                if (_activeTasks.TryGetValue(requestId, out var task))
                {
                    // 只能取消活动状态的任务
                    if (task.IsActive)
                    {
                        task.Cancel();
                        LogMessage($"已取消预加载请求 [{requestId}]");

                        // 触发取消事件
                        var cancelledEvent = PreloadEventArgs.CreateCancelled(requestId, task.Text, task.Duration);
                        OnPreloadCancelled(cancelledEvent);

                        return true;
                    }
                    else
                    {
                        LogMessage($"无法取消预加载请求 [{requestId}]: 任务状态为 {task.Status.GetDisplayName()}");
                        return false;
                    }
                }
                else
                {
                    LogMessage($"未找到预加载请求 [{requestId}]");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"取消预加载请求时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取消所有预加载请求
        /// </summary>
        public void CancelAllPreloads()
        {
            try
            {
                var activeTasks = _activeTasks.Values.Where(t => t.IsActive).ToList();

                if (activeTasks.Count == 0)
                {
                    LogMessage("没有活动的预加载任务需要取消");
                    return;
                }

                LogMessage($"开始取消 {activeTasks.Count} 个活动的预加载任务");

                var cancelledCount = 0;
                foreach (var task in activeTasks)
                {
                    try
                    {
                        if (task.IsActive)
                        {
                            task.Cancel();
                            cancelledCount++;

                            // 触发取消事件
                            var cancelledEvent = PreloadEventArgs.CreateCancelled(task.RequestId, task.Text, task.Duration);
                            OnPreloadCancelled(cancelledEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"取消任务 [{task.RequestId}] 时发生错误: {ex.Message}");
                    }
                }

                LogMessage($"已取消 {cancelledCount} 个预加载任务");
            }
            catch (Exception ex)
            {
                LogMessage($"取消所有预加载任务时发生错误: {ex.Message}");
            }
        }

        #endregion

        #region 管理方法

        /// <summary>
        /// 清理已完成的任务记录
        /// </summary>
        public void CleanupCompletedTasks()
        {
            lock (_lockObject)
            {
                var completedTasks = _activeTasks
                    .Where(kvp => kvp.Value.IsCompleted)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var taskId in completedTasks)
                {
                    if (_activeTasks.TryRemove(taskId, out var task))
                    {
                        task.Dispose();
                    }
                }

                if (completedTasks.Count > 0)
                {
                    LogMessage($"已清理 {completedTasks.Count} 个已完成的任务记录");
                }
            }
        }

        #endregion

        #region 事件触发方法

        /// <summary>
        /// 触发预加载开始事件
        /// </summary>
        protected virtual void OnPreloadStarted(PreloadEventArgs e)
        {
            try
            {
                PreloadStarted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"触发预加载开始事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 触发预加载完成事件
        /// </summary>
        protected virtual void OnPreloadCompleted(PreloadEventArgs e)
        {
            try
            {
                PreloadCompleted?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"触发预加载完成事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 触发预加载失败事件
        /// </summary>
        protected virtual void OnPreloadFailed(PreloadEventArgs e)
        {
            try
            {
                PreloadFailed?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"触发预加载失败事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 触发预加载取消事件
        /// </summary>
        protected virtual void OnPreloadCancelled(PreloadEventArgs e)
        {
            try
            {
                PreloadCancelled?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"触发预加载取消事件时发生错误: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 生成缓存键
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <returns>缓存键</returns>
        protected virtual string GenerateCacheKey(string text)
        {
            return CacheKeyGenerator.GenerateCacheKey(text, _settings);
        }

        /// <summary>
        /// 记录日志消息
        /// </summary>
        protected virtual void LogMessage(string message)
        {
            TTSLogger.Log($"[PreloadService] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        #endregion

        #region IDisposable 实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // 取消所有活动任务
            try
            {
                CancelAllPreloads();
            }
            catch (Exception ex)
            {
                LogMessage($"取消所有预加载任务时发生错误: {ex.Message}");
            }

            // 清理任务记录
            foreach (var task in _activeTasks.Values)
            {
                task.Dispose();
            }
            _activeTasks.Clear();

            LogMessage("预加载服务已释放资源");
        }

        #endregion
    }
}