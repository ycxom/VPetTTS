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
        /// 注意：VPetTTS 不再避让 VPetLLM 内置 TTS，VPetLLM 内置 TTS 会自动避让 VPetTTS
        /// </summary>
        private static readonly string[] KNOWN_TTS_PLUGINS = new[]
        {
            "EdgeTTS",      // VPet.Plugin.EdgeTTS
            // VPetLLM 已移除 - VPetTTS 不再检测和避让 VPetLLM 内置 TTS
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
                if (mainWindow?.Plugins is null)
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
                                // 检查插件是否启用
                                if (CheckPluginEnabled(plugin))
                                {
                                    result.DetectedPlugins.Add(pluginName);
                                    TTSLogger.Log($"[VPetTTS] 检测到其他已启用的 TTS 插件: {pluginName}");
                                }
                                else
                                {
                                    TTSLogger.Log($"[VPetTTS] 检测到其他 TTS 插件但未启用: {pluginName}");
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TTSLogger.Log($"[VPetTTS] 检查插件时发生错误: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"[VPetTTS] 检测其他 TTS 插件时发生错误: {ex.Message}");
            }

            return result;
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
                if (setProperty is not null)
                {
                    setObject = setProperty.GetValue(plugin);
                }

                // 如果属性不存在，尝试获取 Set 字段
                if (setObject is null)
                {
                    var setField = plugin.GetType().GetField("Set");
                    if (setField is not null)
                    {
                        setObject = setField.GetValue(plugin);
                    }
                }

                if (setObject is not null)
                {
                    // 尝试获取 Enable 属性
                    var enableProperty = setObject.GetType().GetProperty("Enable");
                    if (enableProperty is not null)
                    {
                        var enableValue = enableProperty.GetValue(setObject);
                        if (enableValue is bool enabled)
                        {
                            return enabled;
                        }
                    }
                }

                // 如果无法获取 Enable 属性，假设插件未启用（保守策略）
                TTSLogger.Log("[VPetTTS] 无法获取 Enable 属性，假设插件未启用");
                return false;
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"[VPetTTS] 检查插件启用状态时发生错误: {ex.Message}");
                // 出错时假设插件未启用（保守策略）
                return false;
            }
        }
    }
}