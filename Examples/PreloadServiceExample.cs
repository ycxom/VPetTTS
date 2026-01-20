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
            Console.WriteLine("=== 基本使用示例 ===");

            try
            {
                // 1. 预加载单个音频
                Console.WriteLine("1. 预加载单个音频");
                var result = await preloadService.PreloadAudioAsync("你好，主人！", "request-001");

                if (result.Success)
                {
                    Console.WriteLine($"   ✓ 预加载成功: {result.CachePath}");
                    Console.WriteLine($"   耗时: {result.Duration.TotalMilliseconds:F0}ms");
                    Console.WriteLine($"   命中缓存: {result.WasCached}");
                }
                else
                {
                    Console.WriteLine($"   ✗ 预加载失败: {result.ErrorMessage}");
                }

                // 2. 检查是否已预加载
                Console.WriteLine("\n2. 检查预加载状态");
                var isPreloaded = preloadService.IsPreloaded("你好，主人！");
                Console.WriteLine($"   文本是否已预加载: {isPreloaded}");

                if (isPreloaded)
                {
                    var path = preloadService.GetPreloadedPath("你好，主人！");
                    Console.WriteLine($"   缓存路径: {path}");
                }

                // 3. 查询请求状态
                Console.WriteLine("\n3. 查询请求状态");
                var status = preloadService.GetPreloadStatus("request-001");
                Console.WriteLine($"   请求状态: {status.GetDisplayName()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"基本使用示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量预加载示例
        /// </summary>
        public static async Task BatchPreloadExample(IPreloadService preloadService)
        {
            Console.WriteLine("\n=== 批量预加载示例 ===");

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

                Console.WriteLine($"开始批量预加载 {requests.Count} 个请求...");

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

                        Console.WriteLine($"   ✓ {result.RequestId}: 成功 ({result.Duration.TotalMilliseconds:F0}ms)");
                    }
                    else
                    {
                        failureCount++;
                        Console.WriteLine($"   ✗ {result.RequestId}: 失败 - {result.ErrorMessage}");
                    }
                }

                Console.WriteLine($"\n批量预加载完成:");
                Console.WriteLine($"   成功: {successCount}, 失败: {failureCount}, 缓存命中: {cachedCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"批量预加载示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 事件监听示例
        /// </summary>
        public static void EventListeningExample(IPreloadService preloadService)
        {
            Console.WriteLine("\n=== 事件监听示例 ===");

            try
            {
                // 订阅事件
                preloadService.PreloadStarted += (sender, e) =>
                {
                    Console.WriteLine($"   🔄 预加载开始: {e.RequestId} - {e.Text}");
                };

                preloadService.PreloadCompleted += (sender, e) =>
                {
                    var cacheInfo = e.WasCached == true ? " (缓存)" : "";
                    var duration = e.Duration?.TotalMilliseconds.ToString("F0") ?? "?";
                    Console.WriteLine($"   ✅ 预加载完成: {e.RequestId}{cacheInfo} ({duration}ms)");
                };

                preloadService.PreloadFailed += (sender, e) =>
                {
                    Console.WriteLine($"   ❌ 预加载失败: {e.RequestId} - {e.ErrorMessage}");
                };

                preloadService.PreloadCancelled += (sender, e) =>
                {
                    Console.WriteLine($"   🚫 预加载取消: {e.RequestId}");
                };

                Console.WriteLine("事件监听器已设置");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"事件监听示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消操作示例
        /// </summary>
        public static async Task CancellationExample(IPreloadService preloadService)
        {
            Console.WriteLine("\n=== 取消操作示例 ===");

            try
            {
                // 启动一个预加载任务
                var task = preloadService.PreloadAudioAsync("这是一个测试文本", "cancel-test");

                // 等待一小段时间
                await Task.Delay(100);

                // 尝试取消
                var cancelled = preloadService.CancelPreload("cancel-test");
                Console.WriteLine($"取消操作结果: {cancelled}");

                // 等待任务完成
                var result = await task;
                Console.WriteLine($"任务最终状态: {(result.Success ? "成功" : "失败")} - {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"取消操作示例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 完整示例
        /// </summary>
        public static async Task RunCompleteExample(IPreloadService preloadService)
        {
            Console.WriteLine("=== VPetTTS 预加载服务完整示例 ===\n");

            // 设置事件监听
            EventListeningExample(preloadService);

            // 基本使用
            await BasicUsageExample(preloadService);

            // 批量预加载
            await BatchPreloadExample(preloadService);

            // 取消操作
            await CancellationExample(preloadService);

            // 显示统计信息
            Console.WriteLine($"\n=== 统计信息 ===");
            Console.WriteLine($"活动任务数: {preloadService.ActiveTaskCount}");
            Console.WriteLine($"总请求数: {preloadService.TotalRequestCount}");

            Console.WriteLine("\n=== 示例完成 ===");
        }
    }
}