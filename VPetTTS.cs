namespace Vpet.Plugin.CustomTTS
{
    public class VPetTTS : MainPlugin
    {
        public override string PluginName => "VPetTTS";

        public Setting Set;
        public TTSManager ttsManager;
        public winSetting winSetting;

        /// <summary>
        /// TTS 状态管理器
        /// </summary>
        private TTSStateManager _stateManager;

        /// <summary>
        /// 独占会话管理器
        /// </summary>
        private ExclusiveSessionManager _sessionManager;

        /// <summary>
        /// VPetLLM TTS 协调器
        /// </summary>
        private VPetLLMTTSCoordinator _ttsCoordinator;

        /// <summary>
        /// TTS 状态接口（供外部 mod 如 VPetLLM 访问）
        /// </summary>
        public ITTSState TTSState => _stateManager;

        /// <summary>
        /// VPetLLM TTS 协调器（供 VPetLLM 插件使用）
        /// </summary>
        public IVPetLLMTTSCoordinator TTSCoordinator => _ttsCoordinator;

        /// <summary>
        /// 音频预加载服务（供外部程序使用）
        /// </summary>
        public IPreloadService PreloadService => _preloadService;

        /// <summary>
        /// 状态变化事件（供外部 mod 订阅）
        /// </summary>
        public event EventHandler<TTSStateChangedEventArgs> StateChanged
        {
            add => _stateManager.StateChanged += value;
            remove => _stateManager.StateChanged -= value;
        }

        /// <summary>
        /// TTS 缓存管理器
        /// </summary>
        private TTSCacheManager _cacheManager;

        /// <summary>
        /// 音频预加载服务
        /// </summary>
        private PreloadService _preloadService;

        /// <summary>
        /// 定时刷新器（用于调试窗口等）
        /// </summary>
        private DispatcherTimer _refreshTimer;

        // ==================== 服务层字段 ====================
        /// <summary>
        /// 初始化服务
        /// </summary>
        private IInitializationService _initializationService;

        /// <summary>
        /// 插件检测服务
        /// </summary>
        private IPluginDetectionService _pluginDetectionService;

        /// <summary>
        /// 播放器管理器
        /// </summary>
        private IPlayerManager _playerManager;

        /// <summary>
        /// 音频播放服务
        /// </summary>
        private IAudioPlaybackService _audioPlaybackService;

        /// <summary>
        /// TTS 处理服务
        /// </summary>
        private ITTSProcessingService _ttsProcessingService;

        /// <summary>
        /// 系统测试服务
        /// </summary>
        private ISystemTestService _systemTestService;

        /// <summary>
        /// 资源清理服务
        /// </summary>
        private IResourceCleanupService _resourceCleanupService;

        /// <summary>
        /// 启动时异步扫描的插件信息缓存（供设置界面使用）
        /// </summary>
        public List<PluginAssemblyScanner.PluginInfo> CachedPluginScanResults { get; private set; }

        /// <summary>
        /// 云端屏蔽列表服务
        /// </summary>
        public CloudBanService CloudBan { get; private set; }

        /// <summary>
        /// 是否使用 mpv 播放器
        /// </summary>
        public bool UseMpvPlayer => _playerManager?.UseMpvPlayer ?? false;

        /// <summary>
        /// 当前播放器类型
        /// </summary>
        public PlayerType CurrentPlayerType => _playerManager?.CurrentPlayerType ?? PlayerType.None;

        /// <summary>
        /// 获取播放器状态
        /// </summary>
        public PlayerStatus GetPlayerStatus()
        {
            return _playerManager?.GetPlayerStatus() ?? new PlayerStatus { Type = PlayerType.None };
        }

        /// <summary>
        /// 播放器状态变化事件
        /// </summary>
        public event EventHandler<PlayerChangedEventArgs> PlayerChanged;

        /// <summary>
        /// 获取软禁用状态（供设置窗口使用）
        /// </summary>
        public bool IsSoftDisabled => _pluginDetectionService?.IsSoftDisabled ?? false;

        /// <summary>
        /// 获取检测到的其他 TTS 插件名称（供设置窗口使用）
        /// </summary>
        public string DetectedOtherTTSPluginNames => _pluginDetectionService?.DetectedOtherPluginNames ?? "";

        /// <summary>
        /// 刷新软禁用状态（供设置窗口调用）
        /// </summary>
        public void RefreshSoftDisableStatus()
        {
            _pluginDetectionService?.RefreshDetection();
        }

        /// <summary>
        /// 更新屏蔽插件列表（供设置窗口保存时调用）
        /// 合并云端屏蔽 + 本地手动屏蔽，计算有效列表后更新拦截器
        /// </summary>
        public void UpdateBlockedPlugins(List<string> localBlockedPlugins)
        {
            // 计算有效屏蔽列表（合并云端 + 本地）
            var effectiveBlocked = CloudBanService.ComputeEffectiveBlockedPlugins(
                CloudBan?.CloudBannedModIds,
                Set.CloudBanAllowedMods,
                localBlockedPlugins,
                MW.ModInfo);

            SaySourceInterceptor.UpdateBlockedPlugins(effectiveBlocked);
            LogMessage($"屏蔽插件列表已更新: {(effectiveBlocked.Count > 0 ? string.Join(", ", effectiveBlocked) : "(空)")}");
        }

        public VPetTTS(IMainWindow mainwin) : base(mainwin)
        {
        }

        public override void LoadPlugin()
        {
            // 加载设置
            Set = LPSConvert.DeserializeObject<Setting>(MW.Set["VPetTTS"]);
            Set?.Validate();

            // ==================== 创建并初始化服务 ====================

            // 1. 初始化服务（负责所有组件的初始化）
            // 注意：InitializationService 会通过反射设置 _stateManager, _ttsCoordinator, _cacheManager, _preloadService
            _initializationService = new InitializationService(MW, Set, this);
            
            // 修复：使用后台线程初始化，但等待完成后再继续
            // 使用 ManualResetEventSlim 避免死锁
            var initCompleted = new System.Threading.ManualResetEventSlim(false);
            Exception initException = null;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await _initializationService.InitializeAllAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    initException = ex;
                }
                finally
                {
                    initCompleted.Set();
                }
            });
            
            // 等待初始化完成（最多等待 10 秒）
            if (!initCompleted.Wait(TimeSpan.FromSeconds(10)))
            {
                LogMessage("警告：初始化服务超时（10秒），继续加载插件");
            }
            else if (initException != null)
            {
                LogMessage($"初始化服务失败: {initException.Message}");
            }
            else
            {
                LogMessage("初始化服务完成");
            }

            // 2. 插件检测服务
            _pluginDetectionService = new PluginDetectionService(MW, PluginName);
            _pluginDetectionService.Initialize();

            // 3. 播放器管理器（需要 _stateManager，由 InitializationService 设置）
            _playerManager = new PlayerManager(MW, Set, _stateManager, new PlayerErrorHandler());
            _playerManager.Initialize();
            _playerManager.PlayerChanged += (sender, e) => PlayerChanged?.Invoke(this, e);

            // 4. 音频播放服务
            _audioPlaybackService = new AudioPlaybackService(_playerManager, _stateManager, MW, new PlayerErrorHandler());

            // 5. TTS 处理服务（需要 ttsManager, _cacheManager, _preloadService，由 InitializationService 设置）
            // 修复：同步创建 TTS 处理服务，确保在 LoadPlugin 返回前完成
            if (ttsManager != null && _cacheManager != null && _preloadService != null)
            {
                _ttsProcessingService = new TTSProcessingService(
                    ttsManager,
                    _stateManager,
                    _cacheManager,
                    _preloadService,
                    _audioPlaybackService,
                    Set);
                LogMessage("TTS 处理服务初始化完成");
                
                // 更新协调器中的 TTS 处理服务引用
                if (_ttsCoordinator is VPetLLMTTSCoordinator coordinator)
                {
                    coordinator.SetTTSProcessingService(_ttsProcessingService);
                    LogMessage("协调器的 TTS 处理服务引用已更新");
                }
            }
            else
            {
                LogMessage("TTS 处理服务初始化失败：依赖组件未初始化");
                LogMessage($"ttsManager: {ttsManager != null}, _cacheManager: {_cacheManager != null}, _preloadService: {_preloadService != null}");
            }

            // 6. 系统测试服务
            _systemTestService = new SystemTestService(_playerManager, _audioPlaybackService, _pluginDetectionService);

            // 7. 资源清理服务
            _resourceCleanupService = new ResourceCleanupService(_playerManager, _cacheManager, _preloadService, new PlayerErrorHandler());
            LogMessage("资源清理服务初始化完成");

            // 如果启用TTS，注册SayProcess事件
            // 软禁用模式：即使检测到其他插件也注册事件，在运行时检测并跳过
            if (Set.Enable)
                MW.Main.SayProcess.Add(Main_OnSay);

            // 添加到MOD配置菜单
            MenuItem modset = MW.Main.ToolBar.MenuMODConfig;
            modset.Visibility = Visibility.Visible;
            var menuItem = new MenuItem()
            {
                Header = "VPetTTS".Translate(),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            menuItem.Click += (s, e) => { Setting(); };
            modset.Items.Add(menuItem);

            // 记录软禁用状态
            if (_pluginDetectionService.IsSoftDisabled)
            {
                LogMessage($"检测到其他已启用的 TTS 插件 ({_pluginDetectionService.DetectedOtherPluginNames})，VPetTTS 将在运行时自动跳过（不包括 VPetLLM 内置 TTS）");
            }

            // 初始化 Say 来源拦截器（始终初始化，屏蔽列表为空时补丁不生效）
            try
            {
                SaySourceInterceptor.Initialize(MW, Set.BlockedPlugins ?? new List<string>(), LogMessage);
                if (Set.BlockedPlugins != null && Set.BlockedPlugins.Count > 0)
                {
                    LogMessage($"Say 来源拦截器已启用，屏蔽插件: {string.Join(", ", Set.BlockedPlugins)}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Say 来源拦截器初始化失败: {ex.Message}");
            }

            // 异步获取云端屏蔽列表 + 扫描已安装插件（不阻塞启动）
            CloudBan = new CloudBanService(LogMessage);
            _ = Task.Run(async () =>
            {
                try
                {
                    // 1. 扫描插件
                    CachedPluginScanResults = PluginAssemblyScanner.ScanPlugins(MW);
                    LogMessage($"插件扫描完成，发现 {CachedPluginScanResults.Count} 个其他插件");

                    // 2. 获取云端屏蔽列表
                    var cloudBannedIds = await CloudBan.FetchCloudBanListAsync().ConfigureAwait(false);

                    if (cloudBannedIds.Count > 0)
                    {
                        // 3. 标记插件的 IsCloudBanned
                        foreach (var pluginInfo in CachedPluginScanResults)
                        {
                            if (!string.IsNullOrEmpty(pluginInfo.ModId) && CloudBan.IsCloudBanned(pluginInfo.ModId))
                            {
                                pluginInfo.IsCloudBanned = true;
                            }
                        }

                        // 4. 计算有效屏蔽列表（合并云端 + 本地）
                        var effectiveBlocked = CloudBanService.ComputeEffectiveBlockedPlugins(
                            cloudBannedIds,
                            Set.CloudBanAllowedMods,
                            Set.BlockedPlugins,
                            MW.ModInfo);

                        // 5. 更新拦截器
                        SaySourceInterceptor.UpdateBlockedPlugins(effectiveBlocked);
                        LogMessage($"云端屏蔽列表已合并，有效屏蔽插件: {(effectiveBlocked.Count > 0 ? string.Join(", ", effectiveBlocked) : "(空)")}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"云端屏蔽列表获取/合并失败: {ex.Message}");
                }
            });

            // 通知可用性状态
            _stateManager?.NotifyAvailabilityChanged("插件加载完成");

            // 启动定时器，定期清理超时会话（每 30 秒检查一次）
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _refreshTimer.Tick += (s, e) =>
            {
                try
                {
                    _sessionManager?.CheckAndCleanupTimedOutSession();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VPetTTS] 清理超时会话失败: {ex.Message}");
                }
            };
            _refreshTimer.Start();
            Console.WriteLine("[VPetTTS] 启动会话超时清理定时器（30秒间隔）");

            // 注册应用程序退出事件
            Application.Current.Exit += OnApplicationExit;
        }

        /// <summary>
        /// 应用程序退出事件处理
        /// </summary>
        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            // 停止定时器
            _refreshTimer?.Stop();

            // 关闭时强制落盘最新状态（节流可能跳过了最近一次写入）
            _stateManager?.SaveState(force: true);

            _resourceCleanupService?.OnSystemShutdown();
        }



        /// <summary>
        /// 刷新播放器检测
        /// </summary>
        public void RefreshPlayerDetection()
        {
            _playerManager?.RefreshDetection();
        }

        /// <summary>
        /// 更新播放器音量设置
        /// </summary>
        public void UpdatePlayerVolume(double volume)
        {
            _playerManager?.UpdateVolume(volume);
        }

        /// <summary>
        /// 检查播放器可用性并自动切换
        /// </summary>
        public async Task CheckPlayerAvailabilityAsync()
        {
            if (_playerManager is not null)
                await _playerManager.CheckPlayerAvailabilityAsync();
        }

        /// <summary>
        /// 获取播放器详细信息（供设置界面使用）
        /// </summary>
        public PlayerDetailInfo GetPlayerDetailInfo()
        {
            return _playerManager?.GetPlayerDetailInfo() ?? new PlayerDetailInfo
            {
                CurrentPlayerType = PlayerType.None,
                IsPlayerAvailable = false,
                PlayerStatusSummary = "播放器管理器未初始化"
            };
        }

        /// <summary>
        /// 获取播放器状态描述（供设置界面使用）
        /// </summary>
        public string GetPlayerStatusDescription()
        {
            return _playerManager?.GetPlayerStatusDescription() ?? "播放器管理器未初始化";
        }

        /// <summary>
        /// 获取播放器推荐信息（供设置界面使用）
        /// </summary>
        public string GetPlayerRecommendation()
        {
            return _playerManager?.GetPlayerRecommendation() ?? "播放器管理器未初始化";
        }

        /// <summary>
        /// 获取播放器错误统计
        /// </summary>
        public PlayerErrorStatistics GetPlayerErrorStatistics()
        {
            return _playerManager?.GetPlayerErrorStatistics() ?? new PlayerErrorStatistics();
        }

        /// <summary>
        /// 获取最近的播放器错误
        /// </summary>
        public List<PlayerErrorRecord> GetRecentPlayerErrors(int count = 10)
        {
            return _playerManager?.GetRecentPlayerErrors(count) ?? new List<PlayerErrorRecord>();
        }

        /// <summary>
        /// 导出播放器错误报告
        /// </summary>
        public string ExportPlayerErrorReport()
        {
            return _playerManager?.ExportPlayerErrorReport() ?? "播放器管理器未初始化";
        }

        /// <summary>
        /// 清除播放器错误历史
        /// </summary>
        public void ClearPlayerErrorHistory()
        {
            _playerManager?.ClearPlayerErrorHistory();
        }

        /// <summary>
        /// 综合系统测试（验证所有组件集成）
        /// </summary>
        public async Task<SystemTestResult> RunSystemTestAsync()
        {
            if (_systemTestService is not null)
                return await _systemTestService.RunSystemTestAsync();

            return new SystemTestResult
            {
                OverallPassed = false,
                TestErrors = new List<string> { "系统测试服务未初始化" }
            };
        }

        /// <summary>
        /// 验证向后兼容性
        /// </summary>
        public bool VerifyBackwardCompatibility()
        {
            return _systemTestService?.VerifyBackwardCompatibility() ?? false;
        }

        /// <summary>
        /// 获取播放器状态摘要
        /// </summary>
        public string GetPlayerStatusSummary()
        {
            return VPetLLMDetector.GetPlayerStatusSummary(MW);
        }





        /// <summary>
        /// 处理说话事件
        /// 委托给 TTS 处理服务
        /// </summary>
        public async void Main_OnSay(VPet_Simulator.Core.SayInfo sayInfo)
        {
            try
            {
                // 添加日志：收到 OnSay 事件
                LogMessage("Main_OnSay: 收到 OnSay 事件");

                if (!Set.Enable)
                {
                    LogMessage("Main_OnSay: 插件未启用，跳过");
                    return;
                }

                // 检查是否处于独占会话（文本获取屏蔽）
                if (_sessionManager != null && !_sessionManager.IsTextCaptureEnabled())
                {
                    LogMessage("Main_OnSay: 独占会话期间，屏蔽原有文本获取");
                    return;
                }

                // 实时检测是否应该跳过 TTS（软禁用）
                var shouldSkip = _pluginDetectionService?.ShouldSkipTTS() == true;
                if (shouldSkip)
                {
                    LogMessage($"Main_OnSay: 软禁用检测 - 检测到其他 TTS 插件已启用 ({_pluginDetectionService?.DetectedOtherPluginNames})，跳过本次 TTS");
                    return;
                }
                else
                {
                    LogMessage("Main_OnSay: 软禁用检测通过，继续处理");
                }

                // 检查消息来源是否被屏蔽
                if (SaySourceInterceptor.IsBlockedAndRemove(sayInfo, out var blockedSource))
                {
                    LogMessage($"Main_OnSay: 消息来自被屏蔽的插件 ({blockedSource})，跳过 TTS");
                    return;
                }

                // 获取文本
                var saythings = await sayInfo.GetSayText().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(saythings))
                {
                    LogMessage("Main_OnSay: 文本为空，跳过");
                    return;
                }

                LogMessage($"Main_OnSay: 处理文本: {saythings.Substring(0, Math.Min(30, saythings.Length))}...");

                // 委托给 TTS 处理服务
                if (_ttsProcessingService is not null)
                {
                    LogMessage("Main_OnSay: 调用 TTS 处理服务");
                    await _ttsProcessingService.ProcessTTSRequestAsync(saythings);
                    LogMessage("Main_OnSay: TTS 处理服务调用完成");
                }
                else
                {
                    // TTS 处理服务未初始化（不应该发生，因为现在是同步初始化）
                    LogMessage($"Main_OnSay: 错误 - TTS 处理服务未初始化");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Main_OnSay: TTS处理失败: {ex.Message}");
                LogMessage($"Main_OnSay: 堆栈跟踪: {ex.StackTrace}");
                _stateManager?.SetError($"TTS处理失败: {ex.Message}", ex, TTSOperationStage.Processing);
            }
        }

        public override void Setting()
        {
            if (winSetting is null || !winSetting.IsLoaded)
            {
                winSetting = new winSetting(this);
                winSetting.Show();
            }
            else
            {
                winSetting.Activate();
                winSetting.Topmost = true;
                winSetting.Topmost = false;
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        public void LogMessage(string message)
        {
            Console.WriteLine($"[VPetTTS] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        /// <summary>
        /// 测试TTS功能
        /// </summary>
        public async Task<bool> TestTTSAsync(string text = null)
        {
            try
            {
                text = text ?? "你好，主人。现在是".Translate() + DateTime.Now.ToString("HH:mm");

                LogMessage($"开始 TTS 测试: {text}");

                // 确保使用最佳播放器
                await CheckPlayerAvailabilityAsync();
                LogMessage($"测试将使用播放器: {CurrentPlayerType}");

                // 更新状态：开始处理
                _stateManager.SetProcessingState(true, text, Set.Provider);
                _stateManager.SetDownloadingState(true, 0);

                var audioData = await ttsManager.GenerateAudioAsync(text);

                // 更新状态：下载完成
                _stateManager.SetDownloadingState(false, 1);

                if (audioData is not null && audioData.Length > 0)
                {
                    var tempPath = AudioPathHelper.GenerateSafeTempAudioPath(".mp3");
                    await File.WriteAllBytesAsync(tempPath, audioData);

                    LogMessage($"测试音频文件已生成: {Path.GetFileName(tempPath)} ({audioData.Length / 1024:F1} KB)");

                    // 更新状态：处理完成
                    _stateManager.SetProcessingState(false);

                    // 更新状态：开始播放
                    _stateManager.SetPlayingState(true, tempPath, text);

                    // 使用音频播放服务
                    if (_audioPlaybackService is not null)
                    {
                        await _audioPlaybackService.PlayAudioAsync(tempPath);
                    }

                    // 更新状态：播放完成
                    _stateManager.SetPlayingState(false, tempPath, text);

                    // 延迟删除临时文件
                    AudioPathHelper.CleanupTempAudioFile(tempPath, TimeSpan.FromSeconds(10));

                    LogMessage("TTS 测试成功完成");
                    return true;
                }
                else
                {
                    // 更新状态：处理完成（无音频数据）
                    _stateManager.SetProcessingState(false);
                    LogMessage("TTS 测试失败: 未生成音频数据");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TTS测试失败: {ex.Message}");
                // 更新状态：发生错误
                _stateManager.SetError($"TTS测试失败: {ex.Message}", ex, TTSOperationStage.Processing);
            }
            return false;
        }

        /// <summary>
        /// 清理缓存
        /// </summary>
        public void ClearCache()
        {
            try
            {
                _cacheManager?.ClearAllCache();
                LogMessage("TTS缓存已清理");
            }
            catch (Exception ex)
            {
                LogMessage($"清理缓存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            return _cacheManager?.GetStatistics();
        }

        /// <summary>
        /// 手动清理过期缓存
        /// </summary>
        public int CleanupExpiredCache()
        {
            return _cacheManager?.CleanupExpiredCache() ?? 0;
        }

        /// <summary>
        /// 打开调试窗口
        /// </summary>
        public void OpenDebugWindow()
        {
            try
            {
                var debugWindow = new winDebugLog(this);
                debugWindow.Show();
                LogMessage("调试窗口已打开");
            }
            catch (Exception ex)
            {
                LogMessage($"打开调试窗口失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取音频文件时长（毫秒）
        /// </summary>
        /// <param name="audioPath">音频文件路径</param>
        /// <returns>音频时长（毫秒），-1 表示无法获取</returns>
        private long GetAudioDurationMs(string audioPath)
        {
            try
            {
                if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
                {
                    return -1;
                }

                var extension = Path.GetExtension(audioPath).ToLowerInvariant();

                // 对于 MP3 文件，尝试从文件头读取时长
                if (extension == ".mp3")
                {
                    return GetMp3DurationMs(audioPath);
                }

                // 对于其他格式，根据文件大小和比特率估算
                // 假设平均比特率为 128kbps
                var fileInfo = new FileInfo(audioPath);
                var fileSizeBytes = fileInfo.Length;
                var estimatedDurationMs = (long)(fileSizeBytes * 8.0 / 128.0); // 128kbps

                return estimatedDurationMs;
            }
            catch (Exception ex)
            {
                LogMessage($"获取音频时长失败: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 获取 MP3 文件时长（毫秒）
        /// 通过读取 MP3 帧头信息计算
        /// </summary>
        private long GetMp3DurationMs(string mp3Path)
        {
            try
            {
                using (var fs = new FileStream(mp3Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // 跳过 ID3v2 标签（如果存在）
                    var header = new byte[10];
                    if (fs.Read(header, 0, 10) == 10)
                    {
                        if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                        {
                            // ID3v2 标签存在，计算标签大小
                            var tagSize = ((header[6] & 0x7F) << 21) |
                                         ((header[7] & 0x7F) << 14) |
                                         ((header[8] & 0x7F) << 7) |
                                         (header[9] & 0x7F);
                            fs.Seek(tagSize, SeekOrigin.Current);
                        }
                        else
                        {
                            fs.Seek(0, SeekOrigin.Begin);
                        }
                    }

                    // 查找第一个有效的 MP3 帧
                    var frameHeader = new byte[4];
                    while (fs.Position < fs.Length - 4)
                    {
                        if (fs.Read(frameHeader, 0, 4) != 4)
                            break;

                        // 检查帧同步字 (0xFF 0xFB, 0xFF 0xFA, 0xFF 0xF3, 0xFF 0xF2)
                        if (frameHeader[0] == 0xFF && (frameHeader[1] & 0xE0) == 0xE0)
                        {
                            // 解析帧头
                            var version = (frameHeader[1] >> 3) & 0x03;
                            var layer = (frameHeader[1] >> 1) & 0x03;
                            var bitrateIndex = (frameHeader[2] >> 4) & 0x0F;
                            var sampleRateIndex = (frameHeader[2] >> 2) & 0x03;

                            // 获取比特率（kbps）
                            var bitrate = GetMp3Bitrate(version, layer, bitrateIndex);
                            if (bitrate > 0)
                            {
                                // 计算音频数据大小（排除标签）
                                var audioDataSize = fs.Length - fs.Position + 4;
                                // 计算时长：(文件大小 * 8) / 比特率
                                var durationMs = (long)(audioDataSize * 8.0 / bitrate);
                                return durationMs;
                            }
                        }

                        // 回退一个字节继续搜索
                        fs.Seek(-3, SeekOrigin.Current);
                    }
                }

                // 如果无法解析，使用文件大小估算（假设 128kbps）
                var fileInfo = new FileInfo(mp3Path);
                return (long)(fileInfo.Length * 8.0 / 128.0);
            }
            catch
            {
                // 出错时使用文件大小估算
                var fileInfo = new FileInfo(mp3Path);
                return (long)(fileInfo.Length * 8.0 / 128.0);
            }
        }

        /// <summary>
        /// 获取 MP3 比特率（kbps）
        /// </summary>
        private int GetMp3Bitrate(int version, int layer, int bitrateIndex)
        {
            // MPEG 1 Layer 3 比特率表
            int[] bitratesV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            // MPEG 2/2.5 Layer 3 比特率表
            int[] bitratesV2L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };

            if (bitrateIndex < 0 || bitrateIndex > 15)
                return 0;

            if (version == 3) // MPEG 1
            {
                if (layer == 1) // Layer 3
                    return bitratesV1L3[bitrateIndex];
            }
            else // MPEG 2 or 2.5
            {
                if (layer == 1) // Layer 3
                    return bitratesV2L3[bitrateIndex];
            }

            return 128; // 默认值
        }
    }
}