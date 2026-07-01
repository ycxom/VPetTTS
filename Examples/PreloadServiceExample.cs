namespace Vpet.Plugin.CustomTTS.Examples
{
    /// <summary>
    /// 预加载服务使用示例
    /// 展示如何使用预加载服务的各种功能
    /// </summary>
    public static class PreloadServiceExample
    {
        /// <summary>
        /// 基本使用示例
        /// </summary>
        public static async Task BasicUsageExample(IPreloadService preloadService)
        {
            TTSLogger.Log("=== 基本使用示例 ===");

            try
            {
                // 1. 预加载单个音频
                TTSLogger.Log("1. 预加载单个音频");
                var result = await preloadService.PreloadAudioAsync("你好，主人！", "request-001");

                if (result.Success)
                {
                    TTSLogger.Log($"   ✓ 预加载成功: {result.CachePath}");
                    TTSLogger.Log($"   耗时: {result.Duration.TotalMilliseconds:F0}ms");
                    TTSLogger.Log($"   命中缓存: {result.WasCached}");
                }
                else
                {
                    TTSLogger.Log($"   ✗ 预加载失败: {result.ErrorMessage}");
                }

                // 2. 检查是否已预加载
                TTSLogger.Log("\n2. 检查预加载状态");
                var isPreloaded = preloadService.IsPreloaded("你好，主人！");
                TTSLogger.Log($"   文本是否已预加载: {isPreloaded}");

                if (isPreloaded)
                {
                    var path = preloadService.GetPreloadedPath("你好，主人！");
                    TTSLogger.Log($"   缓存路径: {path}");
                }

                // 3. 查询请求状态
                TTSLogger.Log("\n3. 查询请求状态");
                var status = preloadService.GetPreloadStatus("request-001");
                TTSLogger.Log($"   请求状态: {status.GetDisplayName()}");
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"基本使用示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量预加载示例
        /// </summary>
        public static async Task BatchPreloadExample(IPreloadService preloadService)
        {
            TTSLogger.Log("\n=== 批量预加载示例 ===");

            try
            {
                // 创建批量请求
                var requests = new List<PreloadRequest>
                {
                    new PreloadRequest("早上好！", "batch-001"),
                    new PreloadRequest("中午好！", "batch-002"),
                    new PreloadRequest("晚上好！", "batch-003"),
                    new PreloadRequest("晚安！", "batch-004")
                };

                TTSLogger.Log($"开始批量预加载 {requests.Count} 个请求...");

                // 执行批量预加载
                var results = await preloadService.PreloadBatchAsync(requests, maxConcurrency: 2);

                // 统计结果
                var successCount = 0;
                var failureCount = 0;
                var cachedCount = 0;

                foreach (var result in results)
                {
                    if (result.Success)
                    {
                        successCount++;
                        if (result.WasCached)
                            cachedCount++;

                        TTSLogger.Log($"   ✓ {result.RequestId}: 成功 ({result.Duration.TotalMilliseconds:F0}ms)");
                    }
                    else
                    {
                        failureCount++;
                        TTSLogger.Log($"   ✗ {result.RequestId}: 失败 - {result.ErrorMessage}");
                    }
                }

                TTSLogger.Log($"\n批量预加载完成:");
                TTSLogger.Log($"   成功: {successCount}, 失败: {failureCount}, 缓存命中: {cachedCount}");
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"批量预加载示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 事件监听示例
        /// </summary>
        public static void EventListeningExample(IPreloadService preloadService)
        {
            TTSLogger.Log("\n=== 事件监听示例 ===");

            try
            {
                // 订阅事件
                preloadService.PreloadStarted += (sender, e) =>
                {
                    TTSLogger.Log($"   🔄 预加载开始: {e.RequestId} - {e.Text}");
                };

                preloadService.PreloadCompleted += (sender, e) =>
                {
                    var cacheInfo = e.WasCached == true ? " (缓存)" : "";
                    var duration = e.Duration?.TotalMilliseconds.ToString("F0") ?? "?";
                    TTSLogger.Log($"   ✅ 预加载完成: {e.RequestId}{cacheInfo} ({duration}ms)");
                };

                preloadService.PreloadFailed += (sender, e) =>
                {
                    TTSLogger.Log($"   ❌ 预加载失败: {e.RequestId} - {e.ErrorMessage}");
                };

                preloadService.PreloadCancelled += (sender, e) =>
                {
                    TTSLogger.Log($"   🚫 预加载取消: {e.RequestId}");
                };

                TTSLogger.Log("事件监听器已设置");
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"事件监听示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消操作示例
        /// </summary>
        public static async Task CancellationExample(IPreloadService preloadService)
        {
            TTSLogger.Log("\n=== 取消操作示例 ===");

            try
            {
                // 启动一个预加载任务
                var task = preloadService.PreloadAudioAsync("这是一个测试文本", "cancel-test");

                // 等待一小段时间
                await Task.Delay(100);

                // 尝试取消
                var cancelled = preloadService.CancelPreload("cancel-test");
                TTSLogger.Log($"取消操作结果: {cancelled}");

                // 等待任务完成
                var result = await task;
                TTSLogger.Log($"任务最终状态: {(result.Success ? "成功" : "失败")} - {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"取消操作示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 完整示例
        /// </summary>
        public static async Task RunCompleteExample(IPreloadService preloadService)
        {
            TTSLogger.Log("=== VPetTTS 预加载服务完整示例 ===\n");

            // 设置事件监听
            EventListeningExample(preloadService);

            // 基本使用
            await BasicUsageExample(preloadService);

            // 批量预加载
            await BatchPreloadExample(preloadService);

            // 取消操作
            await CancellationExample(preloadService);

            // 显示统计信息
            TTSLogger.Log($"\n=== 统计信息 ===");
            TTSLogger.Log($"活动任务数: {preloadService.ActiveTaskCount}");
            TTSLogger.Log($"总请求数: {preloadService.TotalRequestCount}");

            TTSLogger.Log("\n=== 示例完成 ===");
        }
    }
}