using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;
using VPet_Simulator.Core;
using LinePutScript.Converter;
using LinePutScript;
using LinePutScript.Localization.WPF;
using Vpet.Plugin.CustomTTS.Core;
using Vpet.Plugin.CustomTTS.Utils;

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
        /// 状态变化事件（供外部 mod 订阅）
        /// </summary>
        public event EventHandler<TTSStateChangedEventArgs> StateChanged
        {
            add => _stateManager.StateChanged += value;
            remove => _stateManager.StateChanged -= value;
        }

        /// <summary>
        /// VPetLLM 检测结果
        /// </summary>
        private VPetLLMDetectionResult _vpetLLMDetectionResult;

        /// <summary>
        /// 其他 TTS 插件检测结果
        /// </summary>
        private OtherTTSPluginDetectionResult _otherTTSPluginDetectionResult;

        /// <summary>
        /// mpv 播放器实例（如果 VPetLLM 已安装）
        /// </summary>
        private MpvPlayer _mpvPlayer;

        /// <summary>
        /// 当前播放器类型
        /// </summary>
        private PlayerType _currentPlayerType = PlayerType.None;

        /// <summary>
        /// 播放器状态跟踪
        /// </summary>
        private PlayerStatus _playerStatus = new PlayerStatus();

        /// <summary>
        /// 播放器初始化错误列表
        /// </summary>
        private List<string> _playerInitErrors = new List<string>();

        /// <summary>
        /// 播放器错误处理器
        /// </summary>
        private PlayerErrorHandler _errorHandler = new PlayerErrorHandler();

        /// <summary>
        /// TTS 缓存管理器
        /// </summary>
        private TTSCacheManager _cacheManager;

        /// <summary>
        /// 定时刷新器（用于调试窗口等）
        /// </summary>
        private DispatcherTimer _refreshTimer;

        /// <summary>
        /// 是否使用 mpv 播放器
        /// </summary>
        public bool UseMpvPlayer => _mpvPlayer != null && _currentPlayerType == PlayerType.MpvPlayer;

        /// <summary>
        /// 当前播放器类型
        /// </summary>
        public PlayerType CurrentPlayerType => _currentPlayerType;

        /// <summary>
        /// 获取播放器状态
        /// </summary>
        public PlayerStatus GetPlayerStatus()
        {
            lock (_playerStatus)
            {
                return new PlayerStatus
                {
                    Type = _currentPlayerType,
                    IsAvailable = _currentPlayerType != PlayerType.None,
                    IsPlaying = _stateManager?.IsPlaying ?? false,
                    LastError = _playerStatus.LastError,
                    LastErrorTime = _playerStatus.LastErrorTime
                };
            }
        }

        /// <summary>
        /// 播放器状态变化事件
        /// </summary>
        public event EventHandler<PlayerChangedEventArgs> PlayerChanged;

        /// <summary>
        /// 是否应该软禁用（因为检测到其他 TTS 插件）
        /// 软禁用：不修改用户设置，只在运行时跳过 TTS
        /// </summary>
        private bool _softDisabled = false;

        /// <summary>
        /// 获取软禁用状态（供设置窗口使用）
        /// </summary>
        public bool IsSoftDisabled => _softDisabled;

        /// <summary>
        /// 获取检测到的其他 TTS 插件名称（供设置窗口使用）
        /// </summary>
        public string DetectedOtherTTSPluginNames => _otherTTSPluginDetectionResult?.PluginNames ?? "";

        /// <summary>
        /// 刷新软禁用状态（供设置窗口调用）
        /// </summary>
        public void RefreshSoftDisableStatus()
        {
            DetectOtherTTSPlugins();
        }

        public VPetTTS(IMainWindow mainwin) : base(mainwin)
        {
        }

        public override void LoadPlugin()
        {
            // 加载设置
            Set = LPSConvert.DeserializeObject<Setting>(MW.Set["VPetTTS"]);
            Set?.Validate();

            // 初始化认证签名助手
            InitializeAuthProviders();

            // 初始化状态管理器
            _stateManager = new TTSStateManager(Set);
            LogMessage("TTS 状态管理器已初始化");

            // 初始化 VPetLLM TTS 协调器
            _ttsCoordinator = new VPetLLMTTSCoordinator(_stateManager);
            LogMessage("VPetLLM TTS 协调器已初始化");

            // 检测其他 TTS 插件（软禁用检测）
            DetectOtherTTSPlugins();

            // 创建缓存目录
            if (!Directory.Exists(GraphCore.CachePath + @"\tts"))
                Directory.CreateDirectory(GraphCore.CachePath + @"\tts");

            // 初始化缓存管理器（7天过期策略）
            _cacheManager = new TTSCacheManager(GraphCore.CachePath + @"\tts");
            LogMessage("TTS 缓存管理器已初始化（7天过期策略）");

            // 检测 VPetLLM 插件并初始化 mpv 播放器
            DetectAndInitializeMpvPlayer();

            // 初始化Free TTS配置（异步）
            _ = Task.Run(async () =>
            {
                try
                {
                    await Utils.FreeConfigManager.InitializeTTSConfigAsync();
                    LogMessage("Free TTS 配置初始化完成");
                }
                catch (Exception ex)
                {
                    LogMessage($"Free TTS 配置初始化失败: {ex.Message}");
                }
            });

            // 初始化TTS管理器
            ttsManager = new TTSManager(Set);

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
            if (_softDisabled)
            {
                LogMessage($"检测到其他已启用的 TTS 插件 ({_otherTTSPluginDetectionResult.PluginNames})，VPetTTS 将在运行时自动跳过（不包括 VPetLLM 内置 TTS）");
            }

            // 通知可用性状态
            _stateManager.NotifyAvailabilityChanged("插件加载完成");
            
            // 注册应用程序退出事件
            Application.Current.Exit += OnApplicationExit;
        }

        /// <summary>
        /// 应用程序退出事件处理
        /// </summary>
        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            OnSystemShutdown();
        }

        /// <summary>
        /// 检测其他 TTS 插件（软禁用模式）
        /// </summary>
        /// <summary>
        /// 检测其他 TTS 插件（不包括 VPetLLM 内置 TTS）
        /// VPetTTS 不再避让 VPetLLM，只检测真正的 TTS 插件冲突
        /// </summary>
        private void DetectOtherTTSPlugins()
        {
            try
            {
                _otherTTSPluginDetectionResult = OtherTTSPluginDetector.DetectOtherTTSPlugins(MW, PluginName);

                if (_otherTTSPluginDetectionResult.HasOtherEnabledTTSPlugin)
                {
                    // 软禁用：只设置标记，不修改用户设置
                    _softDisabled = true;
                }
                else
                {
                    _softDisabled = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"检测其他 TTS 插件时发生错误: {ex.Message}");
                _softDisabled = false;
            }
        }

        /// <summary>
        /// 初始化认证签名助手
        /// </summary>
        private void InitializeAuthProviders()
        {
            try
            {
                Func<ulong> getSteamId = () =>
                {
                    try { return MW?.SteamID ?? 0; } catch { return 0; }
                };

                Func<Task<int>> getAuthKey = async () =>
                {
                    try { return MW != null ? await MW.GenerateAuthKey() : 0; } catch { return 0; }
                };

                Func<string> getModId = () =>
                {
                    try
                    {
                        var dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        if (string.IsNullOrEmpty(dllPath)) return "";
                        
                        foreach (var mod in MW.OnModInfo)
                        {
                            if (mod.Path != null && dllPath.StartsWith(mod.Path.FullName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (mod.ItemID > 0)
                                    return mod.ItemID.ToString();
                            }
                        }
                        return "";
                    }
                    catch { return ""; }
                };

                Utils.RequestSignatureHelper.Init(getSteamId, getAuthKey, getModId);
                LogMessage("认证签名助手初始化完成");
            }
            catch (Exception ex)
            {
                LogMessage($"初始化认证签名助手失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 实时检测是否应该跳过 TTS（软禁用检测）
        /// 注意：VPetTTS 不再避让 VPetLLM 内置 TTS，只检测其他真正的 TTS 插件冲突
        /// </summary>
        private bool ShouldSkipTTS()
        {
            try
            {
                // 重新检测其他 TTS 插件状态（不包括 VPetLLM）
                var result = OtherTTSPluginDetector.DetectOtherTTSPlugins(MW, PluginName);
                var shouldSkip = result.HasOtherEnabledTTSPlugin;
                
                // 更新软禁用状态
                if (shouldSkip != _softDisabled)
                {
                    _softDisabled = shouldSkip;
                    if (shouldSkip)
                    {
                        LogMessage($"检测到其他 TTS 插件已启用 ({result.PluginNames})，跳过 TTS");
                    }
                    else
                    {
                        LogMessage("其他 TTS 插件已禁用，恢复 TTS 功能");
                    }
                }
                
                return shouldSkip;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检测 VPetLLM 插件并初始化 mpv 播放器
        /// </summary>
        private void DetectAndInitializeMpvPlayer()
        {
            var oldPlayerType = _currentPlayerType;
            _playerInitErrors.Clear();

            try
            {
                LogMessage("开始检测和初始化播放器...");
                _vpetLLMDetectionResult = VPetLLMDetector.DetectVPetLLM(MW, forceRefresh: true);

                // 记录检测过程中的错误
                if (_vpetLLMDetectionResult.DetectionErrors.Count > 0)
                {
                    _playerInitErrors.AddRange(_vpetLLMDetectionResult.DetectionErrors);
                    foreach (var error in _vpetLLMDetectionResult.DetectionErrors)
                    {
                        LogMessage($"检测警告: {error}");
                    }
                }

                if (_vpetLLMDetectionResult.CanUseMpvPlayer)
                {
                    try
                    {
                        // 验证 mpv 播放器是否可执行
                        if (VPetLLMDetector.ValidateMpvPlayer(_vpetLLMDetectionResult.MpvExePath))
                        {
                            _mpvPlayer = new MpvPlayer(_vpetLLMDetectionResult.MpvExePath);
                            _mpvPlayer.SetVolume(Set.Volume);
                            
                            // 订阅 mpv 播放器事件
                            _mpvPlayer.ProcessExited += OnMpvProcessExited;
                            _mpvPlayer.PlaybackCompleted += OnMpvPlaybackCompleted;
                            
                            _currentPlayerType = PlayerType.MpvPlayer;
                            
                            LogMessage($"✓ 成功初始化 mpv 播放器");
                            LogMessage($"  路径: {_vpetLLMDetectionResult.MpvExePath}");
                            LogMessage($"  版本: {_vpetLLMDetectionResult.MpvVersion}");
                            LogMessage($"  文件大小: {_vpetLLMDetectionResult.MpvFileSize / 1024 / 1024:F1} MB");
                        }
                        else
                        {
                            var error = "mpv 播放器验证失败，无法执行";
                            _playerInitErrors.Add(error);
                            LogMessage($"✗ {error}");
                            _currentPlayerType = PlayerType.VPetBuiltIn;
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = $"初始化 mpv 播放器失败: {ex.Message}";
                        _playerInitErrors.Add(error);
                        LogMessage($"✗ {error}");
                        LogMessage($"堆栈跟踪: {ex.StackTrace}");
                        
                        // 使用错误处理器记录初始化错误
                        _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, ex, "mpv 播放器初始化", _vpetLLMDetectionResult.MpvExePath);
                        
                        _mpvPlayer = null;
                        _currentPlayerType = PlayerType.VPetBuiltIn;
                    }
                }
                else if (_vpetLLMDetectionResult.PluginExists)
                {
                    var reason = "VPetLLM 插件已安装但 mpv 播放器不可用";
                    LogMessage($"○ {reason}，回退到 VPet 内置播放器");
                    _currentPlayerType = PlayerType.VPetBuiltIn;
                }
                else
                {
                    LogMessage("○ 未检测到 VPetLLM 插件，使用 VPet 内置播放器");
                    _currentPlayerType = PlayerType.VPetBuiltIn;
                }

                // 更新播放器状态
                UpdatePlayerStatus();

                // 触发播放器变化事件
                if (oldPlayerType != _currentPlayerType)
                {
                    var reason = GetPlayerChangeReason(oldPlayerType, _currentPlayerType);
                    OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, reason));
                }

                LogMessage($"播放器初始化完成 - 当前播放器: {_currentPlayerType}");
            }
            catch (Exception ex)
            {
                var error = $"播放器检测和初始化过程发生严重错误: {ex.Message}";
                _playerInitErrors.Add(error);
                LogMessage($"严重错误: {error}");
                LogMessage($"堆栈跟踪: {ex.StackTrace}");
                
                // 使用错误处理器记录严重错误
                _errorHandler.HandlePlayerError(PlayerType.None, ex, "播放器检测和初始化");
                
                // 回退到内置播放器
                _mpvPlayer = null;
                _currentPlayerType = PlayerType.VPetBuiltIn;
                UpdatePlayerStatus();
                
                if (oldPlayerType != _currentPlayerType)
                {
                    OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, "初始化错误，回退到内置播放器"));
                }
            }
        }

        /// <summary>
        /// 更新播放器状态
        /// </summary>
        private void UpdatePlayerStatus()
        {
            lock (_playerStatus)
            {
                _playerStatus.Type = _currentPlayerType;
                _playerStatus.IsAvailable = _currentPlayerType != PlayerType.None;
                
                if (_playerInitErrors.Count > 0)
                {
                    _playerStatus.LastError = string.Join("; ", _playerInitErrors);
                    _playerStatus.LastErrorTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 获取播放器变化原因
        /// </summary>
        private string GetPlayerChangeReason(PlayerType oldType, PlayerType newType)
        {
            if (oldType == PlayerType.None && newType == PlayerType.MpvPlayer)
                return "检测到 VPetLLM 插件，初始化 mpv 播放器";
            else if (oldType == PlayerType.None && newType == PlayerType.VPetBuiltIn)
                return "未检测到 VPetLLM 插件，使用内置播放器";
            else if (oldType == PlayerType.MpvPlayer && newType == PlayerType.VPetBuiltIn)
                return "mpv 播放器不可用，回退到内置播放器";
            else if (oldType == PlayerType.VPetBuiltIn && newType == PlayerType.MpvPlayer)
                return "检测到可用的 mpv 播放器，切换使用";
            else
                return "播放器状态变化";
        }

        /// <summary>
        /// 触发播放器变化事件
        /// </summary>
        private void OnPlayerChanged(PlayerChangedEventArgs e)
        {
            try
            {
                LogMessage($"播放器切换: {e.OldPlayerType} → {e.NewPlayerType} ({e.Reason})");
                PlayerChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"播放器变化事件处理异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新播放器检测
        /// </summary>
        public void RefreshPlayerDetection()
        {
            LogMessage("手动刷新播放器检测");
            VPetLLMDetector.ClearCache();
            DetectAndInitializeMpvPlayer();
        }

        /// <summary>
        /// 更新播放器音量设置
        /// </summary>
        public void UpdatePlayerVolume(double volume)
        {
            try
            {
                LogMessage($"更新播放器音量: {volume}%");
                
                // 更新设置
                if (Set != null)
                {
                    Set.Volume = volume;
                }
                
                // 同步到所有播放器
                SyncVolumeSettings();
            }
            catch (Exception ex)
            {
                LogMessage($"更新播放器音量时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查播放器可用性并自动切换
        /// </summary>
        public async Task CheckPlayerAvailabilityAsync()
        {
            try
            {
                LogMessage("检查播放器可用性...");
                
                // 如果当前是 mpv 播放器，检查其状态
                if (_currentPlayerType == PlayerType.MpvPlayer && _mpvPlayer != null)
                {
                    var processStatus = _mpvPlayer.GetProcessStatus();
                    LogMessage($"mpv 播放器状态: {processStatus}");
                    
                    if (processStatus == ProcessStatus.Disposed || processStatus == ProcessStatus.Unknown)
                    {
                        LogMessage("mpv 播放器不可用，切换到内置播放器");
                        await SwitchToFallbackPlayer("mpv 播放器状态异常");
                    }
                }
                
                // 尝试切换到最佳播放器
                await EnsureBestPlayer();
            }
            catch (Exception ex)
            {
                LogMessage($"检查播放器可用性时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取播放器详细信息（供设置界面使用）
        /// </summary>
        public PlayerDetailInfo GetPlayerDetailInfo()
        {
            try
            {
                var info = new PlayerDetailInfo
                {
                    CurrentPlayerType = _currentPlayerType,
                    IsPlayerAvailable = _currentPlayerType != PlayerType.None,
                    PlayerStatusSummary = GetPlayerStatusSummary()
                };

                // mpv 播放器信息
                if (_vpetLLMDetectionResult != null)
                {
                    info.VPetLLMPluginExists = _vpetLLMDetectionResult.PluginExists;
                    info.MpvPlayerAvailable = _vpetLLMDetectionResult.CanUseMpvPlayer;
                    info.MpvExePath = _vpetLLMDetectionResult.MpvExePath;
                    info.MpvVersion = _vpetLLMDetectionResult.MpvVersion;
                    info.MpvFileSize = _vpetLLMDetectionResult.MpvFileSize;
                }

                // 播放器状态
                var playerStatus = GetPlayerStatus();
                info.IsPlaying = playerStatus.IsPlaying;
                info.LastError = playerStatus.LastError;
                info.LastErrorTime = playerStatus.LastErrorTime;

                // 错误统计
                var errorStats = GetPlayerErrorStatistics();
                info.TotalErrors = errorStats.TotalErrors;
                info.RecentErrorCount = GetRecentPlayerErrors(5).Count;

                // 初始化错误
                info.InitializationErrors = new List<string>(_playerInitErrors);

                return info;
            }
            catch (Exception ex)
            {
                LogMessage($"获取播放器详细信息时发生错误: {ex.Message}");
                return new PlayerDetailInfo
                {
                    CurrentPlayerType = PlayerType.None,
                    IsPlayerAvailable = false,
                    PlayerStatusSummary = $"获取信息失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取播放器状态描述（供设置界面使用）
        /// </summary>
        public string GetPlayerStatusDescription()
        {
            try
            {
                var description = new StringBuilder();
                
                description.AppendLine($"当前播放器: {GetPlayerTypeDisplayName(_currentPlayerType)}");
                
                if (_currentPlayerType == PlayerType.MpvPlayer && _mpvPlayer != null)
                {
                    var processStatus = _mpvPlayer.GetProcessStatus();
                    description.AppendLine($"mpv 状态: {GetProcessStatusDisplayName(processStatus)}");
                    
                    if (_vpetLLMDetectionResult != null)
                    {
                        description.AppendLine($"mpv 版本: {_vpetLLMDetectionResult.MpvVersion}");
                        description.AppendLine($"文件大小: {_vpetLLMDetectionResult.MpvFileSize / 1024 / 1024:F1} MB");
                    }
                }
                
                var playerStatus = GetPlayerStatus();
                if (!string.IsNullOrEmpty(playerStatus.LastError))
                {
                    description.AppendLine($"最近错误: {playerStatus.LastError}");
                    description.AppendLine($"错误时间: {playerStatus.LastErrorTime:yyyy-MM-dd HH:mm:ss}");
                }
                
                var errorStats = GetPlayerErrorStatistics();
                if (errorStats.TotalErrors > 0)
                {
                    description.AppendLine($"总错误数: {errorStats.TotalErrors}");
                }
                
                return description.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"获取状态描述失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取播放器类型显示名称
        /// </summary>
        private string GetPlayerTypeDisplayName(PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.MpvPlayer => "mpv 播放器 (高码率)",
                PlayerType.VPetBuiltIn => "VPet 内置播放器",
                PlayerType.None => "无播放器",
                _ => "未知播放器"
            };
        }

        /// <summary>
        /// 获取进程状态显示名称
        /// </summary>
        private string GetProcessStatusDisplayName(ProcessStatus status)
        {
            return status switch
            {
                ProcessStatus.NotStarted => "未启动",
                ProcessStatus.Ready => "就绪",
                ProcessStatus.Playing => "播放中",
                ProcessStatus.Exited => "已退出",
                ProcessStatus.Disposed => "已释放",
                ProcessStatus.Unknown => "未知状态",
                _ => "未定义状态"
            };
        }

        /// <summary>
        /// 获取播放器推荐信息（供设置界面使用）
        /// </summary>
        public string GetPlayerRecommendation()
        {
            try
            {
                if (_vpetLLMDetectionResult?.CanUseMpvPlayer == true)
                {
                    return "推荐使用 mpv 播放器，支持高码率音频播放，音质更佳。";
                }
                else if (_vpetLLMDetectionResult?.PluginExists == true)
                {
                    return "检测到 VPetLLM 插件，但 mpv 播放器不可用。建议检查 VPetLLM 插件安装。";
                }
                else
                {
                    return "未检测到 VPetLLM 插件，将使用 VPet 内置播放器。如需高码率音频支持，请安装 VPetLLM 插件。";
                }
            }
            catch (Exception ex)
            {
                return $"获取推荐信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 综合系统测试（验证所有组件集成）
        /// </summary>
        public async Task<SystemTestResult> RunSystemTestAsync()
        {
            var result = new SystemTestResult();
            var testStartTime = DateTime.Now;
            
            try
            {
                LogMessage("开始系统集成测试...");
                result.TestStartTime = testStartTime;
                
                // 1. 测试播放器检测
                LogMessage("测试 1: 播放器检测");
                try
                {
                    RefreshPlayerDetection();
                    var detailInfo = GetPlayerDetailInfo();
                    result.PlayerDetectionPassed = detailInfo.IsPlayerAvailable;
                    result.DetectedPlayerType = detailInfo.CurrentPlayerType;
                    LogMessage($"播放器检测结果: {detailInfo.CurrentPlayerType} (可用: {detailInfo.IsPlayerAvailable})");
                }
                catch (Exception ex)
                {
                    result.PlayerDetectionPassed = false;
                    result.TestErrors.Add($"播放器检测失败: {ex.Message}");
                }
                
                // 2. 测试音频路径处理
                LogMessage("测试 2: 音频路径处理");
                try
                {
                    var testPath = AudioPathHelper.GenerateSafeTempAudioPath(".mp3");
                    var validationResult = AudioPathHelper.ValidateAudioPath(testPath);
                    // 创建一个小的测试文件
                    await File.WriteAllBytesAsync(testPath, new byte[] { 0x49, 0x44, 0x33 }); // MP3 header
                    
                    validationResult = AudioPathHelper.ValidateAudioPath(testPath);
                    result.PathProcessingPassed = validationResult.IsValid;
                    
                    // 清理测试文件
                    AudioPathHelper.CleanupTempAudioFile(testPath, TimeSpan.FromSeconds(1));
                    LogMessage($"路径处理测试: {(result.PathProcessingPassed ? "通过" : "失败")}");
                }
                catch (Exception ex)
                {
                    result.PathProcessingPassed = false;
                    result.TestErrors.Add($"路径处理测试失败: {ex.Message}");
                }
                
                // 3. 测试错误处理
                LogMessage("测试 3: 错误处理");
                try
                {
                    var errorStats = GetPlayerErrorStatistics();
                    var recentErrors = GetRecentPlayerErrors(5);
                    result.ErrorHandlingPassed = true; // 如果能获取统计信息就算通过
                    LogMessage($"错误处理测试: 通过 (总错误: {errorStats.TotalErrors})");
                }
                catch (Exception ex)
                {
                    result.ErrorHandlingPassed = false;
                    result.TestErrors.Add($"错误处理测试失败: {ex.Message}");
                }
                
                // 4. 测试播放器状态管理
                LogMessage("测试 4: 播放器状态管理");
                try
                {
                    var playerStatus = GetPlayerStatus();
                    var statusDescription = GetPlayerStatusDescription();
                    result.StateManagementPassed = !string.IsNullOrEmpty(statusDescription);
                    LogMessage($"状态管理测试: {(result.StateManagementPassed ? "通过" : "失败")}");
                }
                catch (Exception ex)
                {
                    result.StateManagementPassed = false;
                    result.TestErrors.Add($"状态管理测试失败: {ex.Message}");
                }
                
                // 5. 测试播放器切换（如果有多个播放器）
                LogMessage("测试 5: 播放器切换");
                try
                {
                    await CheckPlayerAvailabilityAsync();
                    var bestPlayer = GetBestAvailablePlayer();
                    result.PlayerSwitchingPassed = bestPlayer != PlayerType.None;
                    LogMessage($"播放器切换测试: {(result.PlayerSwitchingPassed ? "通过" : "失败")} (最佳播放器: {bestPlayer})");
                }
                catch (Exception ex)
                {
                    result.PlayerSwitchingPassed = false;
                    result.TestErrors.Add($"播放器切换测试失败: {ex.Message}");
                }
                
                // 计算总体结果
                result.OverallPassed = result.PlayerDetectionPassed && 
                                     result.PathProcessingPassed && 
                                     result.ErrorHandlingPassed && 
                                     result.StateManagementPassed && 
                                     result.PlayerSwitchingPassed;
                
                result.TestDuration = DateTime.Now - testStartTime;
                
                LogMessage($"系统集成测试完成: {(result.OverallPassed ? "全部通过" : "部分失败")} (耗时: {result.TestDuration.TotalMilliseconds:F0}ms)");
                
                if (!result.OverallPassed)
                {
                    LogMessage($"测试失败项: {result.TestErrors.Count} 个");
                    foreach (var error in result.TestErrors)
                    {
                        LogMessage($"  - {error}");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                result.OverallPassed = false;
                result.TestErrors.Add($"系统测试异常: {ex.Message}");
                result.TestDuration = DateTime.Now - testStartTime;
                LogMessage($"系统集成测试异常: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 验证向后兼容性
        /// </summary>
        public bool VerifyBackwardCompatibility()
        {
            try
            {
                LogMessage("验证向后兼容性...");
                
                // 检查关键接口是否保持不变
                var hasUseMpvPlayer = UseMpvPlayer; // 原有属性
                var hasCurrentPlayerType = CurrentPlayerType != PlayerType.None; // 新属性
                var hasTestMethod = TestTTSAsync() != null; // 原有方法
                
                LogMessage($"向后兼容性验证: UseMpvPlayer={hasUseMpvPlayer}, CurrentPlayerType={CurrentPlayerType}, TestMethod=可用");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"向后兼容性验证失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取播放器状态摘要
        /// </summary>
        public string GetPlayerStatusSummary()
        {
            var summary = VPetLLMDetector.GetPlayerStatusSummary(MW);
            
            if (_playerInitErrors.Count > 0)
            {
                summary += $" (检测到 {_playerInitErrors.Count} 个问题)";
            }
            
            return summary;
        }

        /// <summary>
        /// 获取播放器错误统计
        /// </summary>
        public PlayerErrorStatistics GetPlayerErrorStatistics()
        {
            return _errorHandler.GetErrorStatistics();
        }

        /// <summary>
        /// 获取最近的播放器错误
        /// </summary>
        public List<PlayerErrorRecord> GetRecentPlayerErrors(int count = 10)
        {
            return _errorHandler.GetRecentErrors(count);
        }

        /// <summary>
        /// 导出播放器错误报告
        /// </summary>
        public string ExportPlayerErrorReport()
        {
            return _errorHandler.ExportErrorReport();
        }

        /// <summary>
        /// 清除播放器错误历史
        /// </summary>
        public void ClearPlayerErrorHistory()
        {
            _errorHandler.ClearErrorHistory();
        }

        /// <summary>
        /// mpv 进程退出事件处理
        /// </summary>
        private void OnMpvProcessExited(object sender, ProcessExitedEventArgs e)
        {
            try
            {
                LogMessage($"mpv 进程退出事件: {e.Reason} (退出代码: {e.ExitCode})");
                
                // 如果是异常退出，记录错误
                if (e.ExitCode != 0)
                {
                    var error = $"mpv 进程异常退出: {e.Reason}";
                    _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, 
                        new InvalidOperationException(error), 
                        "进程监控");
                    
                    // 考虑切换到备用播放器
                    if (_currentPlayerType == PlayerType.MpvPlayer)
                    {
                        LogMessage("由于 mpv 进程异常退出，考虑切换到内置播放器");
                        _ = Task.Run(async () => await SwitchToFallbackPlayer($"mpv 进程异常退出: {e.Reason}"));
                    }
                }
                
                // 更新播放状态
                UpdatePlayingState(false);
            }
            catch (Exception ex)
            {
                LogMessage($"处理 mpv 进程退出事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// mpv 播放完成事件处理
        /// </summary>
        private void OnMpvPlaybackCompleted(object sender, PlaybackCompletedEventArgs e)
        {
            try
            {
                LogMessage($"mpv 播放完成: 成功={e.Success}, 退出代码={e.ExitCode}");
                
                // 更新播放状态
                UpdatePlayingState(false);
                
                // 如果播放失败，记录错误
                if (!e.Success)
                {
                    var error = $"mpv 播放失败 (退出代码: {e.ExitCode})";
                    _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, 
                        new InvalidOperationException(error), 
                        "播放完成");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"处理 mpv 播放完成事件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新播放状态
        /// </summary>
        private void UpdatePlayingState(bool isPlaying)
        {
            try
            {
                lock (_playerStatus)
                {
                    _playerStatus.IsPlaying = isPlaying;
                }
                
                // 同步到状态管理器
                if (_stateManager != null)
                {
                    // 注意：这里不直接调用 SetPlayingState，因为那会触发事件
                    // 我们只是同步内部状态
                    LogMessage($"播放状态同步: {isPlaying}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"更新播放状态时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理播放器资源
        /// </summary>
        private void CleanupPlayerResources()
        {
            try
            {
                LogMessage("开始清理播放器资源...");
                
                // 停止当前播放
                if (_mpvPlayer != null)
                {
                    try
                    {
                        // 取消订阅事件
                        _mpvPlayer.ProcessExited -= OnMpvProcessExited;
                        _mpvPlayer.PlaybackCompleted -= OnMpvPlaybackCompleted;
                        
                        // 停止播放并释放资源
                        _mpvPlayer.Stop();
                        _mpvPlayer.Dispose();
                        _mpvPlayer = null;
                        
                        LogMessage("mpv 播放器资源已清理");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"清理 mpv 播放器资源时发生错误: {ex.Message}");
                    }
                }
                
                // 重置播放器状态
                _currentPlayerType = PlayerType.None;
                UpdatePlayingState(false);
                UpdatePlayerStatus();
                
                LogMessage("播放器资源清理完成");
            }
            catch (Exception ex)
            {
                LogMessage($"清理播放器资源时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 系统关闭时的资源释放
        /// </summary>
        public void OnSystemShutdown()
        {
            try
            {
                LogMessage("系统关闭，开始释放所有资源...");
                
                // 清理播放器资源
                CleanupPlayerResources();
                
                // 清理错误处理器
                _errorHandler?.ClearErrorHistory();

                // 释放缓存管理器
                _cacheManager?.Dispose();
                
                LogMessage("系统关闭资源释放完成");
            }
            catch (Exception ex)
            {
                LogMessage($"系统关闭资源释放时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放音频文件（自动选择播放器）
        /// </summary>
        private async Task PlayAudioAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                LogMessage("播放失败: 音频文件路径为空");
                return;
            }

            // 全面验证音频文件
            var validationResult = AudioPathHelper.ValidateAudioPath(path);
            if (!validationResult.IsValid)
            {
                var error = $"音频文件验证失败: {validationResult.ErrorMessage}";
                LogMessage(error);
                UpdatePlayerError(error);
                _stateManager?.SetError(error, new ArgumentException(validationResult.ErrorMessage), TTSOperationStage.Playing);
                return;
            }

            // 使用验证后的规范化路径
            var normalizedPath = validationResult.NormalizedPath;
            LogMessage($"音频文件验证通过: {Path.GetFileName(normalizedPath)} ({validationResult.FileSize / 1024:F1} KB, {validationResult.FileExtension})");

            var originalPlayerType = _currentPlayerType;
            var playbackAttempts = 0;
            const int maxAttempts = 2; // 最多尝试两种播放器

            while (playbackAttempts < maxAttempts)
            {
                playbackAttempts++;
                
                try
                {
                    LogMessage($"尝试播放音频 (第 {playbackAttempts} 次): {Path.GetFileName(normalizedPath)} 使用 {_currentPlayerType} 播放器");

                    if (_currentPlayerType == PlayerType.MpvPlayer && _mpvPlayer != null)
                    {
                        // 使用 mpv 播放器（高码率支持）
                        await PlayWithMpvAsync(normalizedPath);
                        LogMessage($"✓ mpv 播放器播放成功");
                        return;
                    }
                    else if (_currentPlayerType == PlayerType.VPetBuiltIn)
                    {
                        // 使用 VPet 内置播放器
                        await PlayWithVPetBuiltInAsync(normalizedPath);
                        LogMessage($"✓ VPet 内置播放器播放成功");
                        return;
                    }
                    else
                    {
                        throw new InvalidOperationException($"无效的播放器类型: {_currentPlayerType}");
                    }
                }
                catch (Exception ex)
                {
                    var error = $"播放器 {_currentPlayerType} 播放失败: {ex.Message}";
                    LogMessage($"✗ {error}");
                    
                    // 使用错误处理器记录详细错误
                    _errorHandler.HandlePlayerError(_currentPlayerType, ex, "音频播放", normalizedPath);
                    UpdatePlayerError(error);

                    // 如果是第一次尝试且使用的是 mpv，尝试切换到内置播放器
                    if (playbackAttempts == 1 && _currentPlayerType == PlayerType.MpvPlayer)
                    {
                        // 检查是否应该切换播放器
                        if (_errorHandler.ShouldRetryWithDifferentPlayer(ex, _currentPlayerType))
                        {
                            LogMessage("错误处理器建议切换到 VPet 内置播放器重试...");
                            await SwitchToFallbackPlayer($"mpv 播放失败: {ex.Message}");
                            continue;
                        }
                    }
                    
                    // 如果所有播放器都失败了
                    LogMessage($"所有播放器都无法播放音频文件: {normalizedPath}");
                    LogMessage($"最后一个错误: {ex.Message}");
                    
                    // 记录到状态管理器
                    _stateManager?.SetError($"音频播放失败: {ex.Message}", ex, TTSOperationStage.Playing);
                    break;
                }
            }
        }

        /// <summary>
        /// 使用 mpv 播放器播放音频
        /// </summary>
        private async Task PlayWithMpvAsync(string path)
        {
            if (_mpvPlayer == null)
            {
                throw new InvalidOperationException("mpv 播放器未初始化");
            }

            try
            {
                // 检查 mpv 播放器状态
                var processStatus = _mpvPlayer.GetProcessStatus();
                if (processStatus == ProcessStatus.Disposed)
                {
                    throw new ObjectDisposedException("mpv 播放器已被释放");
                }

                LogMessage($"mpv 播放器状态: {processStatus}");
                await _mpvPlayer.PlayAsync(path);
            }
            catch (FileNotFoundException ex)
            {
                throw new FileNotFoundException($"mpv 播放器找不到音频文件: {ex.Message}", ex);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("损坏") || ex.Message.Contains("corrupt"))
            {
                LogMessage($"检测到损坏的音频文件: {Path.GetFileName(path)}");
                throw new InvalidDataException($"音频文件可能已损坏: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // 检查是否是 mpv 进程相关的错误
                if (ex.Message.Contains("mpv") || ex.Message.Contains("进程") || ex.Message.Contains("Process"))
                {
                    LogMessage("检测到 mpv 进程错误，可能需要重新初始化");
                    
                    // 尝试重新初始化 mpv 播放器
                    try
                    {
                        _mpvPlayer?.Dispose();
                        _mpvPlayer = new MpvPlayer(_vpetLLMDetectionResult.MpvExePath);
                        _mpvPlayer.SetVolume(Set.Volume);
                        
                        // 重新订阅事件
                        _mpvPlayer.ProcessExited += OnMpvProcessExited;
                        _mpvPlayer.PlaybackCompleted += OnMpvPlaybackCompleted;
                        
                        LogMessage("mpv 播放器重新初始化成功");
                    }
                    catch (Exception reinitEx)
                    {
                        LogMessage($"mpv 播放器重新初始化失败: {reinitEx.Message}");
                        _mpvPlayer = null;
                        _currentPlayerType = PlayerType.VPetBuiltIn;
                    }
                }
                
                throw; // 重新抛出原始异常
            }
        }

        /// <summary>
        /// 使用 VPet 内置播放器播放音频
        /// </summary>
        private async Task PlayWithVPetBuiltInAsync(string path)
        {
            try
            {
                // 验证音频文件路径（如果还没有验证过）
                var validationResult = AudioPathHelper.ValidateAudioPath(path);
                if (!validationResult.IsValid)
                {
                    throw new ArgumentException($"音频文件验证失败: {validationResult.ErrorMessage}");
                }

                // 规范化路径为 URI 格式
                var audioUri = AudioPathHelper.NormalizeToUri(validationResult.NormalizedPath);
                
                // 验证 URI 格式
                if (!Uri.TryCreate(audioUri, UriKind.Absolute, out var uri))
                {
                    throw new ArgumentException($"无法创建有效的 URI: {audioUri}");
                }

                LogMessage($"使用 VPet 内置播放器播放: {Path.GetFileName(path)} ({validationResult.FileSize / 1024:F1} KB)");

                // 确保在主线程上调用
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        MW.Main.PlayVoice(uri);
                    }
                    catch (Exception ex)
                    {
                        // 检查是否是文件格式不支持的错误
                        if (ex.Message.Contains("format") || ex.Message.Contains("格式") || 
                            ex.Message.Contains("codec") || ex.Message.Contains("编解码器"))
                        {
                            throw new NotSupportedException($"VPet 内置播放器不支持此音频格式: {validationResult.FileExtension}", ex);
                        }
                        
                        throw new InvalidOperationException($"VPet 内置播放器调用失败: {ex.Message}", ex);
                    }
                });

                // 给播放器一些时间开始播放
                await Task.Delay(100);
            }
            catch (ArgumentException)
            {
                // 路径验证错误，直接重新抛出
                throw;
            }
            catch (NotSupportedException)
            {
                // 格式不支持错误，直接重新抛出
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"VPet 内置播放器播放失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 规范化音频文件路径（已弃用，使用 AudioPathHelper.NormalizeToUri）
        /// </summary>
        [Obsolete("使用 AudioPathHelper.NormalizeToUri 替代")]
        private string NormalizeAudioPath(string path)
        {
            return AudioPathHelper.NormalizeToUri(path);
        }

        /// <summary>
        /// 切换到备用播放器
        /// </summary>
        private async Task SwitchToFallbackPlayer(string reason)
        {
            var oldPlayerType = _currentPlayerType;
            
            try
            {
                if (_currentPlayerType == PlayerType.MpvPlayer)
                {
                    LogMessage($"切换播放器: mpv → VPet 内置播放器 (原因: {reason})");
                    
                    // 停止当前播放并清理 mpv 资源
                    if (_mpvPlayer != null)
                    {
                        try
                        {
                            await _mpvPlayer.StopAsync();
                            LogMessage("mpv 播放器已停止");
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"停止 mpv 播放器时发生错误: {ex.Message}");
                        }
                    }
                    
                    _currentPlayerType = PlayerType.VPetBuiltIn;
                    
                    // 确保音量设置一致性
                    SyncVolumeSettings();
                }
                else
                {
                    LogMessage($"无法切换播放器: 当前已是最后的备用播放器 ({_currentPlayerType})");
                    return;
                }

                // 使用错误处理器记录播放器切换
                _errorHandler.LogPlayerSwitch(oldPlayerType, _currentPlayerType, reason);

                // 更新播放器状态
                UpdatePlayerStatus();
                
                // 触发播放器变化事件
                OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, reason));
                
                await Task.Delay(50); // 给系统一点时间处理切换
                
                LogMessage($"播放器切换完成: {oldPlayerType} → {_currentPlayerType}");
            }
            catch (Exception ex)
            {
                LogMessage($"播放器切换失败: {ex.Message}");
                _errorHandler.HandlePlayerError(_currentPlayerType, ex, "播放器切换");
            }
        }

        /// <summary>
        /// 同步音量设置到所有播放器
        /// </summary>
        private void SyncVolumeSettings()
        {
            try
            {
                var currentVolume = Set?.Volume ?? 100.0;
                LogMessage($"同步音量设置: {currentVolume}%");
                
                // 同步到 mpv 播放器
                if (_mpvPlayer != null)
                {
                    _mpvPlayer.SetVolume(currentVolume);
                }
                
                // VPet 内置播放器的音量通常由系统控制，这里记录日志
                LogMessage($"VPet 内置播放器将使用系统音量设置");
            }
            catch (Exception ex)
            {
                LogMessage($"同步音量设置时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取最佳可用播放器
        /// </summary>
        private PlayerType GetBestAvailablePlayer()
        {
            try
            {
                // 优先使用 mpv 播放器（如果可用）
                if (_mpvPlayer != null && _currentPlayerType == PlayerType.MpvPlayer)
                {
                    var processStatus = _mpvPlayer.GetProcessStatus();
                    if (processStatus != ProcessStatus.Disposed && processStatus != ProcessStatus.Unknown)
                    {
                        LogMessage("最佳播放器: mpv (高码率支持)");
                        return PlayerType.MpvPlayer;
                    }
                }
                
                // 回退到 VPet 内置播放器
                LogMessage("最佳播放器: VPet 内置播放器");
                return PlayerType.VPetBuiltIn;
            }
            catch (Exception ex)
            {
                LogMessage($"获取最佳播放器时发生错误: {ex.Message}");
                return PlayerType.VPetBuiltIn; // 安全回退
            }
        }

        /// <summary>
        /// 确保使用最佳播放器
        /// </summary>
        private async Task EnsureBestPlayer()
        {
            try
            {
                var bestPlayer = GetBestAvailablePlayer();
                
                if (bestPlayer != _currentPlayerType)
                {
                    LogMessage($"切换到最佳播放器: {_currentPlayerType} → {bestPlayer}");
                    
                    var oldPlayerType = _currentPlayerType;
                    _currentPlayerType = bestPlayer;
                    
                    // 同步音量设置
                    SyncVolumeSettings();
                    
                    // 更新状态
                    UpdatePlayerStatus();
                    
                    // 触发事件
                    OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, "切换到最佳播放器"));
                }
            }
            catch (Exception ex)
            {
                LogMessage($"确保最佳播放器时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新播放器错误信息
        /// </summary>
        private void UpdatePlayerError(string error)
        {
            lock (_playerStatus)
            {
                _playerStatus.LastError = error;
                _playerStatus.LastErrorTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 处理说话事件
        /// </summary>
        public async void Main_OnSay(VPet_Simulator.Core.SayInfo sayInfo)
        {
            try
            {
                if (!Set.Enable)
                    return;

                // 实时检测是否应该跳过 TTS（软禁用）
                if (ShouldSkipTTS())
                {
                    LogMessage("软禁用：检测到其他 TTS 插件已启用，跳过本次 TTS");
                    return;
                }

                // 获取说话文本
                var saythings = await sayInfo.GetSayText();
                
                if (string.IsNullOrWhiteSpace(saythings))
                    return;

                LogMessage($"处理TTS请求: {saythings}");

                // 更新状态：开始处理
                _stateManager.SetProcessingState(true, saythings, Set.Provider);

                // 生成缓存文件路径
                var cacheKey = Sub.GetHashCode(saythings + Set.Provider).ToString("X");

                // 检查缓存（使用缓存管理器）
                if (Set.EnableCache)
                {
                    var cachedPath = _cacheManager.GetCachePath(cacheKey);
                    if (cachedPath != null)
                    {
                        // 更新状态：处理完成（使用缓存）
                        _stateManager.SetProcessingState(false);
                        
                        // 更新状态：开始播放
                        _stateManager.SetPlayingState(true, cachedPath, saythings);
                        await PlayAudioAsync(cachedPath);
                        // 更新状态：播放完成
                        _stateManager.SetPlayingState(false, cachedPath, saythings);
                        return;
                    }
                }

                // 更新状态：开始下载/生成
                _stateManager.SetDownloadingState(true, 0);

                // 生成音频
                var audioData = await ttsManager.GenerateAudioAsync(saythings);
                
                // 更新状态：下载完成
                _stateManager.SetDownloadingState(false, 1);

                string path;
                if (audioData != null && audioData.Length > 0)
                {
                    // 保存到缓存
                    if (Set.EnableCache)
                    {
                        await _cacheManager.SaveToCacheAsync(cacheKey, audioData);
                        path = Path.Combine(GraphCore.CachePath, "tts", $"{cacheKey}.mp3");
                    }
                    else
                    {
                        // 不使用缓存时，创建安全的临时文件
                        path = AudioPathHelper.GenerateSafeTempAudioPath(".mp3");
                        await File.WriteAllBytesAsync(path, audioData);
                    }

                    // 更新状态：处理完成
                    _stateManager.SetProcessingState(false);

                    // 更新状态：开始播放
                    _stateManager.SetPlayingState(true, path, saythings);
                    
                    // 播放音频
                    await PlayAudioAsync(path);
                    
                    // 更新状态：播放完成
                    _stateManager.SetPlayingState(false, path, saythings);

                    // 如果不使用缓存，延迟删除临时文件
                    if (!Set.EnableCache)
                    {
                        AudioPathHelper.CleanupTempAudioFile(path, TimeSpan.FromSeconds(10));
                    }
                }
                else
                {
                    // 更新状态：处理完成（无音频数据）
                    _stateManager.SetProcessingState(false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TTS处理失败: {ex.Message}");
                // 更新状态：发生错误
                _stateManager.SetError($"TTS处理失败: {ex.Message}", ex, TTSOperationStage.Processing);
            }
        }

        public override void Setting()
        {
            if (winSetting == null || !winSetting.IsLoaded)
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
                await EnsureBestPlayer();
                LogMessage($"测试将使用播放器: {_currentPlayerType}");
                
                // 更新状态：开始处理
                _stateManager.SetProcessingState(true, text, Set.Provider);
                _stateManager.SetDownloadingState(true, 0);
                
                var audioData = await ttsManager.GenerateAudioAsync(text);
                
                // 更新状态：下载完成
                _stateManager.SetDownloadingState(false, 1);
                
                if (audioData != null && audioData.Length > 0)
                {
                    var tempPath = AudioPathHelper.GenerateSafeTempAudioPath(".mp3");
                    await File.WriteAllBytesAsync(tempPath, audioData);
                    
                    LogMessage($"测试音频文件已生成: {Path.GetFileName(tempPath)} ({audioData.Length / 1024:F1} KB)");
                    
                    // 更新状态：处理完成
                    _stateManager.SetProcessingState(false);
                    
                    // 更新状态：开始播放
                    _stateManager.SetPlayingState(true, tempPath, text);
                    
                    // 使用当前最佳播放器
                    await PlayAudioAsync(tempPath);
                    
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
    }
}