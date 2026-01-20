using System.Text.RegularExpressions;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 音频文件路径处理助手
    /// </summary>
    public static class AudioPathHelper
    {
        /// <summary>
        /// 验证音频文件路径
        /// </summary>
        public static AudioPathValidationResult ValidateAudioPath(string path)
        {
            var result = new AudioPathValidationResult();

            if (string.IsNullOrEmpty(path))
            {
                result.IsValid = false;
                result.ErrorMessage = "音频文件路径不能为空";
                return result;
            }

            try
            {
                // 检查路径长度
                if (path.Length > 260)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "音频文件路径过长（超过260字符）";
                    return result;
                }

                // 检查非法字符
                var invalidChars = Path.GetInvalidPathChars();
                foreach (var invalidChar in invalidChars)
                {
                    if (path.Contains(invalidChar))
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"音频文件路径包含非法字符: '{invalidChar}'";
                        return result;
                    }
                }

                // 尝试获取绝对路径
                var absolutePath = Path.GetFullPath(path);
                result.NormalizedPath = absolutePath;

                // 检查文件是否存在
                if (!File.Exists(absolutePath))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"音频文件不存在: {absolutePath}";
                    return result;
                }

                // 检查文件扩展名
                var extension = Path.GetExtension(absolutePath).ToLowerInvariant();
                if (!IsSupportedAudioFormat(extension))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"不支持的音频格式: {extension}";
                    return result;
                }

                // 检查文件大小
                var fileInfo = new FileInfo(absolutePath);
                if (fileInfo.Length == 0)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "音频文件为空";
                    return result;
                }

                if (fileInfo.Length > 100 * 1024 * 1024) // 100MB
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"音频文件过大: {fileInfo.Length / 1024 / 1024:F1} MB";
                    return result;
                }

                result.IsValid = true;
                result.FileSize = fileInfo.Length;
                result.FileExtension = extension;

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"路径验证失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 规范化音频文件路径为 URI 格式
        /// </summary>
        public static string NormalizeToUri(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("路径不能为空", nameof(path));
            }

            try
            {
                // 获取绝对路径
                var absolutePath = Path.GetFullPath(path);

                // 处理特殊字符
                absolutePath = HandleSpecialCharacters(absolutePath);

                // 创建 URI
                var uri = new Uri(absolutePath);
                return uri.ToString();
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"路径规范化失败: {ex.Message}", nameof(path), ex);
            }
        }

        /// <summary>
        /// 处理路径中的特殊字符
        /// </summary>
        private static string HandleSpecialCharacters(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // 替换反斜杠为正斜杠（URI 标准）
            path = path.Replace('\\', '/');

            // 处理 Unicode 字符
            if (ContainsUnicodeCharacters(path))
            {
                // 对 Unicode 字符进行编码
                var bytes = Encoding.UTF8.GetBytes(path);
                var encodedPath = Encoding.UTF8.GetString(bytes);
                LogMessage($"检测到 Unicode 字符，路径已编码: {Path.GetFileName(path)}");
                return encodedPath;
            }

            return path;
        }

        /// <summary>
        /// 检查路径是否包含 Unicode 字符
        /// </summary>
        private static bool ContainsUnicodeCharacters(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (char c in path)
            {
                if (c > 127) // ASCII 范围之外的字符
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查是否为支持的音频格式
        /// </summary>
        private static bool IsSupportedAudioFormat(string extension)
        {
            var supportedFormats = new[]
            {
                ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma"
            };

            return Array.Exists(supportedFormats, format =>
                string.Equals(format, extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 生成安全的临时音频文件路径
        /// </summary>
        public static string GenerateSafeTempAudioPath(string extension = ".mp3")
        {
            try
            {
                var tempDir = Path.GetTempPath();
                var fileName = $"vpet_tts_{Guid.NewGuid():N}{extension}";
                var tempPath = Path.Combine(tempDir, fileName);

                LogMessage($"生成临时音频文件路径: {tempPath}");
                return tempPath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"生成临时文件路径失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 清理临时音频文件
        /// </summary>
        public static void CleanupTempAudioFile(string path, TimeSpan delay = default)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            if (delay == default)
                delay = TimeSpan.FromSeconds(10);

            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        LogMessage($"临时音频文件已清理: {Path.GetFileName(path)}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"清理临时音频文件失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 修复损坏的音频文件路径
        /// </summary>
        public static string RepairAudioPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                // 移除多余的引号
                path = path.Trim('"', '\'');

                // 修复双反斜杠
                path = Regex.Replace(path, @"\\+", @"\");

                // 修复混合的路径分隔符
                path = path.Replace('/', '\\');

                // 移除路径末尾的分隔符
                path = path.TrimEnd('\\', '/');

                LogMessage($"路径修复完成: {Path.GetFileName(path)}");
                return path;
            }
            catch (Exception ex)
            {
                LogMessage($"路径修复失败: {ex.Message}");
                return path;
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private static void LogMessage(string message)
        {
            Console.WriteLine($"[AudioPathHelper] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }

    /// <summary>
    /// 音频路径验证结果
    /// </summary>
    public class AudioPathValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string NormalizedPath { get; set; } = "";
        public long FileSize { get; set; }
        public string FileExtension { get; set; } = "";
    }
}