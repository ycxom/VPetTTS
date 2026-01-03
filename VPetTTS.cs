using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        /// 是否使用 mpv 播放器
        /// </summary>
        public bool UseMpvPlayer => _mpvPlayer != null;

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
                LogMessage($"检测到其他已启用的 TTS 插件 ({_otherTTSPluginDetectionResult.PluginNames})，VPetTTS 将在运行时自动跳过");
            }

            // 通知可用性状态
            _stateManager.NotifyAvailabilityChanged("插件加载完成");
        }

        /// <summary>
        /// 检测其他 TTS 插件（软禁用模式）
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
        /// 实时检测是否应该跳过 TTS（软禁用检测）
        /// </summary>
        private bool ShouldSkipTTS()
        {
            try
            {
                // 重新检测其他 TTS 插件状态
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
            try
            {
                _vpetLLMDetectionResult = VPetLLMDetector.DetectVPetLLM(MW);

                if (_vpetLLMDetectionResult.CanUseMpvPlayer)
                {
                    _mpvPlayer = new MpvPlayer(_vpetLLMDetectionResult.MpvExePath);
                    _mpvPlayer.SetVolume(Set.Volume);
                    LogMessage($"已检测到 VPetLLM 插件，将使用 mpv 播放器实现高码率音频播放");
                }
                else if (_vpetLLMDetectionResult.PluginExists)
                {
                    LogMessage("已检测到 VPetLLM 插件，但 mpv 播放器不可用，将使用 VPet 内置播放器");
                }
                else
                {
                    LogMessage("未检测到 VPetLLM 插件，将使用 VPet 内置播放器");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"检测 VPetLLM 插件时发生错误: {ex.Message}");
                _mpvPlayer = null;
            }
        }

        /// <summary>
        /// 播放音频文件（自动选择播放器）
        /// </summary>
        private async Task PlayAudioAsync(string path)
        {
            if (_mpvPlayer != null)
            {
                // 使用 mpv 播放器（高码率支持）
                await _mpvPlayer.PlayAsync(path);
            }
            else
            {
                // 使用 VPet 内置播放器
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MW.Main.PlayVoice(new Uri(path));
                });
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
                var path = GraphCore.CachePath + $"\\tts\\{cacheKey}.mp3";

                // 检查缓存
                if (Set.EnableCache && File.Exists(path))
                {
                    // 更新状态：处理完成（使用缓存）
                    _stateManager.SetProcessingState(false);
                    
                    // 更新状态：开始播放
                    _stateManager.SetPlayingState(true, path, saythings);
                    await PlayAudioAsync(path);
                    // 更新状态：播放完成
                    _stateManager.SetPlayingState(false, path, saythings);
                    return;
                }

                // 更新状态：开始下载/生成
                _stateManager.SetDownloadingState(true, 0);

                // 生成音频
                var audioData = await ttsManager.GenerateAudioAsync(saythings);
                
                // 更新状态：下载完成
                _stateManager.SetDownloadingState(false, 1);

                if (audioData != null && audioData.Length > 0)
                {
                    // 保存到缓存
                    if (Set.EnableCache)
                    {
                        await File.WriteAllBytesAsync(path, audioData);
                    }
                    else
                    {
                        // 不使用缓存时，创建临时文件
                        path = Path.GetTempFileName();
                        path = Path.ChangeExtension(path, "mp3");
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
                        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
                        {
                            try
                            {
                                if (File.Exists(path))
                                    File.Delete(path);
                            }
                            catch { }
                        });
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
                
                // 更新状态：开始处理
                _stateManager.SetProcessingState(true, text, Set.Provider);
                _stateManager.SetDownloadingState(true, 0);
                
                var audioData = await ttsManager.GenerateAudioAsync(text);
                
                // 更新状态：下载完成
                _stateManager.SetDownloadingState(false, 1);
                
                if (audioData != null && audioData.Length > 0)
                {
                    var tempPath = Path.GetTempFileName();
                    tempPath = Path.ChangeExtension(tempPath, "mp3");
                    await File.WriteAllBytesAsync(tempPath, audioData);
                    
                    // 更新状态：处理完成
                    _stateManager.SetProcessingState(false);
                    
                    // 更新状态：开始播放
                    _stateManager.SetPlayingState(true, tempPath, text);
                    
                    // 使用自动选择的播放器
                    await PlayAudioAsync(tempPath);
                    
                    // 更新状态：播放完成
                    _stateManager.SetPlayingState(false, tempPath, text);

                    // 延迟删除临时文件
                    _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
                    {
                        try
                        {
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                        }
                        catch { }
                    });

                    return true;
                }
                else
                {
                    // 更新状态：处理完成（无音频数据）
                    _stateManager.SetProcessingState(false);
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
                var cacheDir = GraphCore.CachePath + @"\tts";
                if (Directory.Exists(cacheDir))
                {
                    foreach (var file in Directory.GetFiles(cacheDir))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
                LogMessage("TTS缓存已清理");
            }
            catch (Exception ex)
            {
                LogMessage($"清理缓存失败: {ex.Message}");
            }
        }
    }
}