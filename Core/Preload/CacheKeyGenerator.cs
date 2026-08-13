using System.Security.Cryptography;

namespace Vpet.Plugin.CustomTTS.Core.Preload
{
    /// <summary>
    /// 缓存键生成器
    /// 负责为预加载音频生成唯一的缓存键
    /// </summary>
    public static class CacheKeyGenerator
    {
        /// <summary>
        /// 生成缓存键
        /// 基于文本内容和当前 TTS 设置生成唯一键
        /// </summary>
        /// <param name="text">要转换的文本</param>
        /// <param name="settings">TTS 设置</param>
        /// <returns>32位十六进制缓存键</returns>
        public static string GenerateCacheKey(string text, Setting settings)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Text cannot be null or empty", nameof(text));

            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            // 构建键源字符串，包含影响音频生成的所有参数
            var keySource = BuildKeySource(text, settings);

            // 使用 SHA256 生成哈希
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keySource));

            // 转换为32位十六进制字符串（取前16字节）
            return Convert.ToHexString(hashBytes).Substring(0, 32).ToLower();
        }

        /// <summary>
        /// 构建键源字符串
        /// 包含所有影响音频生成的参数
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="settings">TTS 设置</param>
        /// <returns>键源字符串</returns>
        private static string BuildKeySource(string text, Setting settings)
        {
            var keyBuilder = new StringBuilder();

            // 基本文本内容
            keyBuilder.Append(text);
            keyBuilder.Append('|');

            // TTS 提供商
            keyBuilder.Append(settings.Provider ?? "");
            keyBuilder.Append('|');

            // 根据不同提供商添加相关设置
            switch (settings.Provider?.ToLower())
            {
                case "free":
                    AppendFreeSettings(keyBuilder, settings);
                    break;
                case "openai":
                    AppendOpenAISettings(keyBuilder, settings);
                    break;
                case "gpt-sovits":
                    AppendGPTSoVITSSettings(keyBuilder, settings);
                    break;
                case "url":
                    AppendURLSettings(keyBuilder, settings);
                    break;
                case "diy":
                    AppendDIYSettings(keyBuilder, settings);
                    break;
                default:
                    // 未知提供商，添加通用设置
                    keyBuilder.Append("unknown");
                    break;
            }

            return keyBuilder.ToString();
        }

        /// <summary>
        /// 添加 Free TTS 相关设置
        /// </summary>
        private static void AppendFreeSettings(StringBuilder keyBuilder, Setting settings)
        {
            if (settings.Free is not null)
            {
                keyBuilder.Append(settings.Free.TextLanguage ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.Speed);
            }
        }

        /// <summary>
        /// 添加 OpenAI TTS 相关设置
        /// </summary>
        private static void AppendOpenAISettings(StringBuilder keyBuilder, Setting settings)
        {
            if (settings.OpenAI is not null)
            {
                keyBuilder.Append(settings.OpenAI.Voice ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.OpenAI.Model ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.Speed);
            }
        }

        /// <summary>
        /// 添加 GPT-SoVITS 相关设置
        /// </summary>
        private static void AppendGPTSoVITSSettings(StringBuilder keyBuilder, Setting settings)
        {
            if (settings.GPTSoVITS is not null)
            {
                var textLanguage = TTSLanguage.Normalize(
                    settings.GPTSoVITS.TextLanguage,
                    TTSLanguage.Chinese);
                var promptLanguage = TTSLanguage.Normalize(
                    settings.GPTSoVITS.PromptLanguage,
                    textLanguage);

                keyBuilder.Append(settings.GPTSoVITS.BaseUrl ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.GPTSoVITS.ApiMode ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.GPTSoVITS.PromptText ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.GPTSoVITS.ReferWavPath ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(textLanguage);
                keyBuilder.Append('|');
                keyBuilder.Append(promptLanguage);
                keyBuilder.Append('|');
                keyBuilder.Append(settings.GPTSoVITS.Temperature);
                keyBuilder.Append('|');
                keyBuilder.Append(settings.GPTSoVITS.Speed);
            }
        }

        /// <summary>
        /// 添加 URL TTS 相关设置
        /// </summary>
        private static void AppendURLSettings(StringBuilder keyBuilder, Setting settings)
        {
            if (settings.URL is not null)
            {
                keyBuilder.Append(settings.URL.BaseUrl ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.URL.Voice ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.Speed);
            }
        }

        /// <summary>
        /// 添加 DIY TTS 相关设置
        /// </summary>
        private static void AppendDIYSettings(StringBuilder keyBuilder, Setting settings)
        {
            if (settings.DIY is not null)
            {
                keyBuilder.Append(settings.DIY.BaseUrl ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.DIY.RequestBody ?? "");
                keyBuilder.Append('|');
                keyBuilder.Append(settings.DIY.Method ?? "");
            }
        }

        /// <summary>
        /// 验证缓存键格式
        /// </summary>
        /// <param name="cacheKey">缓存键</param>
        /// <returns>是否为有效格式</returns>
        public static bool IsValidCacheKey(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
                return false;

            if (cacheKey.Length != 32)
                return false;

            // 检查是否为有效的十六进制字符串
            foreach (char c in cacheKey)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 生成测试用的缓存键（用于单元测试）
        /// </summary>
        /// <param name="seed">种子值</param>
        /// <returns>测试缓存键</returns>
        internal static string GenerateTestCacheKey(string seed)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return Convert.ToHexString(hashBytes).Substring(0, 32).ToLower();
        }
    }
}
