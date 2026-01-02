using System;
using System.IO;
using System.Reflection;
using VPet_Simulator.Windows.Interface;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// VPetLLM 插件检测结果
    /// </summary>
    public class VPetLLMDetectionResult
    {
        /// <summary>
        /// 插件是否存在
        /// </summary>
        public bool PluginExists { get; set; }

        /// <summary>
        /// mpv.exe 路径（如果找到）
        /// </summary>
        public string MpvExePath { get; set; } = "";

        /// <summary>
        /// 是否可以使用 mpv 播放器
        /// </summary>
        public bool CanUseMpvPlayer => PluginExists && !string.IsNullOrEmpty(MpvExePath) && File.Exists(MpvExePath);
    }

    /// <summary>
    /// VPetLLM 插件检测器
    /// 用于检测 VPetLLM 插件是否已安装，并获取其 mpv 播放器路径
    /// </summary>
    public static class VPetLLMDetector
    {
        private const string VPETLLM_PLUGIN_NAME = "VPetLLM";
        private static VPetLLMDetectionResult _cachedResult = null;
        private static DateTime _lastDetectionTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 检测 VPetLLM 插件
        /// </summary>
        public static VPetLLMDetectionResult DetectVPetLLM(IMainWindow mainWindow, bool forceRefresh = false)
        {
            // 使用缓存避免频繁检测
            if (!forceRefresh && _cachedResult != null && DateTime.Now - _lastDetectionTime < CacheDuration)
            {
                return _cachedResult;
            }

            var result = new VPetLLMDetectionResult();

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
                        if (string.Equals(pluginName, VPETLLM_PLUGIN_NAME, StringComparison.OrdinalIgnoreCase))
                        {
                            result.PluginExists = true;
                            Console.WriteLine($"[VPetTTS] 检测到 {VPETLLM_PLUGIN_NAME} 插件");

                            // 获取 VPetLLM 插件的 DLL 路径
                            var pluginAssembly = plugin.GetType().Assembly;
                            var pluginDllPath = pluginAssembly.Location;
                            var pluginDir = Path.GetDirectoryName(pluginDllPath);

                            // mpv 目录在插件目录下的 mpv 文件夹中
                            var mpvDir = Path.Combine(pluginDir, "mpv");
                            var mpvExePath = Path.Combine(mpvDir, "mpv.exe");

                            if (File.Exists(mpvExePath))
                            {
                                result.MpvExePath = mpvExePath;
                                Console.WriteLine($"[VPetTTS] 找到 mpv 播放器: {mpvExePath}");
                            }
                            else
                            {
                                Console.WriteLine($"[VPetTTS] mpv 播放器未找到: {mpvExePath}");
                            }

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VPetTTS] 检查插件时发生错误: {ex.Message}");
                    }
                }

                if (!result.PluginExists)
                {
                    Console.WriteLine($"[VPetTTS] 未检测到 {VPETLLM_PLUGIN_NAME} 插件");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] 检测 VPetLLM 插件时发生错误: {ex.Message}");
            }

            _cachedResult = result;
            _lastDetectionTime = DateTime.Now;
            return result;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public static void ClearCache()
        {
            _cachedResult = null;
            _lastDetectionTime = DateTime.MinValue;
        }
    }
}
