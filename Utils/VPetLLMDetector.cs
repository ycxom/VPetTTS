using System.Diagnostics;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 播放器类型枚举
    /// </summary>
    public enum PlayerType
    {
        None,
        MpvPlayer,
        VPetBuiltIn
    }

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

        /// <summary>
        /// 检测过程中的错误信息
        /// </summary>
        public List<string> DetectionErrors { get; set; } = new List<string>();

        /// <summary>
        /// 推荐的播放器类型
        /// </summary>
        public PlayerType RecommendedPlayer => CanUseMpvPlayer ? PlayerType.MpvPlayer : PlayerType.VPetBuiltIn;

        /// <summary>
        /// 检测时间
        /// </summary>
        public DateTime DetectionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 检测是否成功（无错误）
        /// </summary>
        public bool DetectionSuccessful => DetectionErrors.Count == 0;

        /// <summary>
        /// mpv 文件大小（如果找到）
        /// </summary>
        public long MpvFileSize { get; set; }

        /// <summary>
        /// mpv 文件版本信息（如果可获取）
        /// </summary>
        public string MpvVersion { get; set; } = "";
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
            if (!forceRefresh && _cachedResult is not null && DateTime.Now - _lastDetectionTime < CacheDuration)
            {
                // 不让日志和瀑布一样
                // LogMessage($"使用缓存的检测结果 (缓存时间: {_lastDetectionTime:HH:mm:ss})");
                return _cachedResult;
            }

            var result = new VPetLLMDetectionResult();
            LogMessage("开始检测 VPetLLM 插件...");

            try
            {
                if (mainWindow?.Plugins is null)
                {
                    var error = "主窗口或插件列表为空";
                    result.DetectionErrors.Add(error);
                    LogMessage($"检测失败: {error}");
                    return result;
                }

                LogMessage($"开始遍历 {mainWindow.Plugins.Count} 个已加载的插件");

                // 遍历所有已加载的插件
                foreach (var plugin in mainWindow.Plugins)
                {
                    try
                    {
                        var pluginName = plugin.PluginName;
                        LogMessage($"检查插件: {pluginName}");

                        if (string.Equals(pluginName, VPETLLM_PLUGIN_NAME, StringComparison.OrdinalIgnoreCase))
                        {
                            result.PluginExists = true;
                            LogMessage($"✓ 检测到 {VPETLLM_PLUGIN_NAME} 插件");

                            // 获取 VPetLLM 插件的 DLL 路径
                            var pluginAssembly = plugin.GetType().Assembly;
                            var pluginDllPath = pluginAssembly.Location;
                            var pluginDir = Path.GetDirectoryName(pluginDllPath);

                            LogMessage($"插件目录: {pluginDir}");

                            if (string.IsNullOrEmpty(pluginDir))
                            {
                                var error = "无法获取插件目录路径";
                                result.DetectionErrors.Add(error);
                                LogMessage($"警告: {error}");
                                break;
                            }

                            // mpv 目录在插件目录下的 mpv 文件夹中
                            var mpvDir = Path.Combine(pluginDir, "mpv");
                            var mpvExePath = Path.Combine(mpvDir, "mpv.exe");

                            LogMessage($"检查 mpv 路径: {mpvExePath}");

                            if (File.Exists(mpvExePath))
                            {
                                result.MpvExePath = mpvExePath;

                                // 获取文件信息
                                try
                                {
                                    var fileInfo = new FileInfo(mpvExePath);
                                    result.MpvFileSize = fileInfo.Length;

                                    // 尝试获取版本信息
                                    var versionInfo = FileVersionInfo.GetVersionInfo(mpvExePath);
                                    result.MpvVersion = versionInfo.FileVersion ?? "未知版本";

                                    LogMessage($"✓ 找到 mpv 播放器: {mpvExePath}");
                                    LogMessage($"  文件大小: {result.MpvFileSize / 1024 / 1024:F1} MB");
                                    LogMessage($"  版本信息: {result.MpvVersion}");
                                }
                                catch (Exception ex)
                                {
                                    var error = $"获取 mpv 文件信息失败: {ex.Message}";
                                    result.DetectionErrors.Add(error);
                                    LogMessage($"警告: {error}");
                                }
                            }
                            else
                            {
                                var error = $"mpv 播放器未找到: {mpvExePath}";
                                result.DetectionErrors.Add(error);
                                LogMessage($"✗ {error}");

                                // 检查 mpv 目录是否存在
                                if (!Directory.Exists(mpvDir))
                                {
                                    var dirError = $"mpv 目录不存在: {mpvDir}";
                                    result.DetectionErrors.Add(dirError);
                                    LogMessage($"✗ {dirError}");
                                }
                                else
                                {
                                    // 列出 mpv 目录中的文件
                                    try
                                    {
                                        var files = Directory.GetFiles(mpvDir);
                                        LogMessage($"mpv 目录中的文件: {string.Join(", ", files.Select(Path.GetFileName))}");
                                    }
                                    catch (Exception ex)
                                    {
                                        LogMessage($"无法列出 mpv 目录文件: {ex.Message}");
                                    }
                                }
                            }

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = $"检查插件 '{plugin?.PluginName ?? "未知"}' 时发生错误: {ex.Message}";
                        result.DetectionErrors.Add(error);
                        LogMessage($"错误: {error}");
                    }
                }

                if (!result.PluginExists)
                {
                    LogMessage($"✗ 未检测到 {VPETLLM_PLUGIN_NAME} 插件");
                }
            }
            catch (Exception ex)
            {
                var error = $"检测 VPetLLM 插件时发生严重错误: {ex.Message}";
                result.DetectionErrors.Add(error);
                LogMessage($"严重错误: {error}");
                LogMessage($"堆栈跟踪: {ex.StackTrace}");
            }

            // 记录检测结果摘要
            LogMessage($"检测完成 - 插件存在: {result.PluginExists}, mpv 可用: {result.CanUseMpvPlayer}, 推荐播放器: {result.RecommendedPlayer}");
            if (result.DetectionErrors.Count > 0)
            {
                LogMessage($"检测过程中发现 {result.DetectionErrors.Count} 个问题");
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
            LogMessage("检测缓存已清除");
        }

        /// <summary>
        /// 获取播放器状态摘要
        /// </summary>
        public static string GetPlayerStatusSummary(IMainWindow mainWindow)
        {
            try
            {
                var result = DetectVPetLLM(mainWindow);

                if (result.CanUseMpvPlayer)
                {
                    return $"mpv 播放器可用 (版本: {result.MpvVersion})";
                }
                else if (result.PluginExists)
                {
                    return "VPetLLM 插件已安装，但 mpv 不可用，使用 VPet 内置播放器";
                }
                else
                {
                    return "VPetLLM 插件未安装，使用 VPet 内置播放器";
                }
            }
            catch (Exception ex)
            {
                return $"播放器状态检测失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 验证 mpv 播放器是否可执行
        /// </summary>
        public static bool ValidateMpvPlayer(string mpvPath)
        {
            if (string.IsNullOrEmpty(mpvPath) || !File.Exists(mpvPath))
            {
                return false;
            }

            try
            {
                // 尝试运行 mpv --version 来验证可执行性
                var startInfo = new ProcessStartInfo
                {
                    FileName = mpvPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process is not null)
                    {
                        process.WaitForExit(5000); // 5秒超时
                        return process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"验证 mpv 播放器失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private static void LogMessage(string message)
        {
            TTSLogger.Log($"[VPetLLMDetector] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }
}
