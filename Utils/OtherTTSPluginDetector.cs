using System;
using System.Collections.Generic;
using VPet_Simulator.Windows.Interface;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 其他 TTS 插件检测结果
    /// </summary>
    public class OtherTTSPluginDetectionResult
    {
        /// <summary>
        /// 检测到的其他 TTS 插件列表
        /// </summary>
        public List<string> DetectedPlugins { get; set; } = new List<string>();

        /// <summary>
        /// 是否检测到其他已启用的 TTS 插件
        /// </summary>
        public bool HasOtherEnabledTTSPlugin => DetectedPlugins.Count > 0;

        /// <summary>
        /// 检测到的插件名称（用于日志）
        /// </summary>
        public string PluginNames => string.Join(", ", DetectedPlugins);
    }

    /// <summary>
    /// 其他 TTS 插件检测器
    /// 用于检测其他 TTS 插件（如 EdgeTTS）是否已启用，防止多个 TTS 插件同时运行
    /// </summary>
    public static class OtherTTSPluginDetector
    {
        /// <summary>
        /// 已知的其他 TTS 插件名称列表
        /// </summary>
        private static readonly string[] KNOWN_TTS_PLUGINS = new[]
        {
            "EdgeTTS",      // VPet.Plugin.EdgeTTS
            "VPetLLM",      // VPetLLM 内置 TTS
            // 可以在这里添加更多已知的 TTS 插件名称
        };

        /// <summary>
        /// 检测其他 TTS 插件
        /// </summary>
        public static OtherTTSPluginDetectionResult DetectOtherTTSPlugins(IMainWindow mainWindow, string currentPluginName = "VPetTTS")
        {
            var result = new OtherTTSPluginDetectionResult();

            try
            {
                if (mainWindow?.Plugins == null)
                {
                    return result;
                }

                // 遍历所有已加载的插件
                foreach (var plugin in mainWindow.Plugins)
                {
                    try
                    {
                        var pluginName = plugin.PluginName;

                        // 跳过当前插件自己
                        if (string.Equals(pluginName, currentPluginName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // 检查是否是已知的 TTS 插件
                        foreach (var knownPlugin in KNOWN_TTS_PLUGINS)
                        {
                            if (string.Equals(pluginName, knownPlugin, StringComparison.OrdinalIgnoreCase))
                            {
                                // 特殊处理 VPetLLM：检查其 TTS 功能是否启用
                                if (string.Equals(pluginName, "VPetLLM", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (CheckVPetLLMTTSEnabled(plugin))
                                    {
                                        result.DetectedPlugins.Add("VPetLLM (内置TTS)");
                                        Console.WriteLine($"[VPetTTS] 检测到 VPetLLM 内置 TTS 已启用");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[VPetTTS] VPetLLM 存在但内置 TTS 未启用");
                                    }
                                }
                                else
                                {
                                    // 其他 TTS 插件：检查插件是否启用
                                    if (CheckPluginEnabled(plugin))
                                    {
                                        result.DetectedPlugins.Add(pluginName);
                                        Console.WriteLine($"[VPetTTS] 检测到其他已启用的 TTS 插件: {pluginName}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[VPetTTS] 检测到其他 TTS 插件但未启用: {pluginName}");
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VPetTTS] 检查插件时发生错误: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] 检测其他 TTS 插件时发生错误: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 检查 VPetLLM 的 TTS 功能是否启用
        /// 通过反射访问 Settings.TTS.IsEnabled 属性
        /// </summary>
        private static bool CheckVPetLLMTTSEnabled(MainPlugin plugin)
        {
            try
            {
                // 获取 Settings 属性
                var settingsProperty = plugin.GetType().GetProperty("Settings");
                if (settingsProperty == null)
                {
                    Console.WriteLine("[VPetTTS] VPetLLM 没有 Settings 属性");
                    return false;
                }

                var settings = settingsProperty.GetValue(plugin);
                if (settings == null)
                {
                    Console.WriteLine("[VPetTTS] VPetLLM Settings 为 null");
                    return false;
                }

                // 获取 TTS 属性
                var ttsProperty = settings.GetType().GetProperty("TTS");
                if (ttsProperty == null)
                {
                    Console.WriteLine("[VPetTTS] VPetLLM Settings 没有 TTS 属性");
                    return false;
                }

                var tts = ttsProperty.GetValue(settings);
                if (tts == null)
                {
                    Console.WriteLine("[VPetTTS] VPetLLM TTS 设置为 null");
                    return false;
                }

                // 获取 IsEnabled 属性
                var isEnabledProperty = tts.GetType().GetProperty("IsEnabled");
                if (isEnabledProperty == null)
                {
                    Console.WriteLine("[VPetTTS] VPetLLM TTS 没有 IsEnabled 属性");
                    return false;
                }

                var isEnabled = isEnabledProperty.GetValue(tts);
                if (isEnabled is bool enabled)
                {
                    return enabled;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] 检查 VPetLLM TTS 状态时发生错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查插件的 Enable 属性
        /// 通过反射访问插件的 Set.Enable 属性
        /// </summary>
        private static bool CheckPluginEnabled(MainPlugin plugin)
        {
            try
            {
                object setObject = null;

                // 首先尝试获取 Set 属性
                var setProperty = plugin.GetType().GetProperty("Set");
                if (setProperty != null)
                {
                    setObject = setProperty.GetValue(plugin);
                }

                // 如果属性不存在，尝试获取 Set 字段
                if (setObject == null)
                {
                    var setField = plugin.GetType().GetField("Set");
                    if (setField != null)
                    {
                        setObject = setField.GetValue(plugin);
                    }
                }

                if (setObject != null)
                {
                    // 尝试获取 Enable 属性
                    var enableProperty = setObject.GetType().GetProperty("Enable");
                    if (enableProperty != null)
                    {
                        var enableValue = enableProperty.GetValue(setObject);
                        if (enableValue is bool enabled)
                        {
                            return enabled;
                        }
                    }
                }

                // 如果无法获取 Enable 属性，假设插件未启用（保守策略）
                Console.WriteLine("[VPetTTS] 无法获取 Enable 属性，假设插件未启用");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] 检查插件启用状态时发生错误: {ex.Message}");
                // 出错时假设插件未启用（保守策略）
                return false;
            }
        }
    }
}