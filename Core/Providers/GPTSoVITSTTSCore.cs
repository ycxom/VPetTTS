using Newtonsoft.Json;
using System.Net.Http;

namespace Vpet.Plugin.CustomTTS.Core.Providers
{
    /// <summary>
    /// GPT-SoVITS TTS 实现
    /// 支持WebUI和API v2模式
    /// </summary>
    public class GPTSoVITSTTSCore : TTSCoreBase
    {
        public override string Name => "GPT-SoVITS";

        public GPTSoVITSTTSCore(Setting settings) : base(settings)
        {
        }

        public override async Task<byte[]> GenerateAudioAsync(string text)
        {
            try
            {
                if (Settings?.GPTSoVITS is null || string.IsNullOrWhiteSpace(Settings.GPTSoVITS.BaseUrl))
                {
                    OnAudioGenerationError("GPT-SoVITS BaseUrl 未配置");
                    return Array.Empty<byte>();
                }

                LogMessage($"TTS (GPT-SoVITS): 发送请求，文本长度: {text.Length}");

                if (Settings.GPTSoVITS.ApiMode == "ApiV2")
                {
                    return await GenerateAudioApiV2Async(text);
                }
                else
                {
                    return await GenerateAudioWebUIAsync(text);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TTS (GPT-SoVITS): 生成音频异常: {ex.Message}");
                OnAudioGenerationError($"GPT-SoVITS TTS 异常: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        private async Task<byte[]> GenerateAudioWebUIAsync(string text)
        {
            var (textLanguageCode, promptLanguageCode) = GetNormalizedLanguages();
            var requestBody = new
            {
                text = text,
                text_lang = ToWebUILanguage(textLanguageCode),
                ref_audio_path = Settings.GPTSoVITS.ReferWavPath,
                prompt_text = Settings.GPTSoVITS.PromptText,
                prompt_lang = ToWebUILanguage(promptLanguageCode),
                top_k = 15,
                top_p = 1.0,
                temperature = Settings.GPTSoVITS.Temperature,
                text_split_method = "按标点符号切",
                batch_size = 1,
                speed_factor = Settings.GPTSoVITS.Speed,
                split_bucket = true,
                return_fragment = false,
                fragment_interval = 0.3,
                seed = -1
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = CreateHttpClient();
            var response = await client.PostAsync($"{Settings.GPTSoVITS.BaseUrl}/tts", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogMessage($"TTS (GPT-SoVITS WebUI): API 错误: {response.StatusCode} - {errorContent}");
                OnAudioGenerationError($"GPT-SoVITS WebUI 错误: {response.StatusCode}");
                return Array.Empty<byte>();
            }

            var audioData = await response.Content.ReadAsByteArrayAsync();
            LogMessage($"TTS (GPT-SoVITS WebUI): 音频生成成功，大小: {audioData.Length} bytes");

            OnAudioGenerated(audioData);
            return audioData;
        }

        private async Task<byte[]> GenerateAudioApiV2Async(string text)
        {
            var (textLanguage, promptLanguage) = GetNormalizedLanguages();
            var requestBody = new
            {
                text = text,
                text_lang = textLanguage,
                ref_audio_path = Settings.GPTSoVITS.ReferWavPath,
                prompt_text = Settings.GPTSoVITS.PromptText,
                prompt_lang = promptLanguage,
                top_k = 15,
                top_p = 1.0,
                temperature = Settings.GPTSoVITS.Temperature,
                text_split_method = "cut5",
                batch_size = 1,
                speed_factor = Settings.GPTSoVITS.Speed,
                streaming_mode = 0,
                seed = -1,
                parallel_infer = true,
                repetition_penalty = 1.35
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var client = CreateHttpClient();
            var response = await client.PostAsync($"{Settings.GPTSoVITS.BaseUrl}/tts", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogMessage($"TTS (GPT-SoVITS API v2): API 错误: {response.StatusCode} - {errorContent}");
                OnAudioGenerationError($"GPT-SoVITS API v2 错误: {response.StatusCode}");
                return Array.Empty<byte>();
            }

            var audioData = await response.Content.ReadAsByteArrayAsync();
            LogMessage($"TTS (GPT-SoVITS API v2): 音频生成成功，大小: {audioData.Length} bytes");

            OnAudioGenerated(audioData);
            return audioData;
        }

        /// <summary>
        /// 旧版 WebUI 兼容接口使用界面显示名称；API v2 则直接使用 ISO 风格语言码。
        /// 设置中始终保存语言码，在请求适配层完成转换，避免语言切换时混用两种格式。
        /// </summary>
        private static string ToWebUILanguage(string language)
        {
            return language switch
            {
                TTSLanguage.Chinese => "中文",
                TTSLanguage.English => "英文",
                TTSLanguage.Japanese => "日文",
                TTSLanguage.Cantonese => "粤语",
                TTSLanguage.Korean => "韩文",
                _ => "多语种混合"
            };
        }

        private (string TextLanguage, string PromptLanguage) GetNormalizedLanguages()
        {
            var textLanguage = TTSLanguage.Normalize(
                Settings.GPTSoVITS.TextLanguage,
                TTSLanguage.Chinese);
            var promptLanguage = TTSLanguage.Normalize(
                Settings.GPTSoVITS.PromptLanguage,
                textLanguage);

            return (textLanguage, promptLanguage);
        }

        public override string GetAudioFormat()
        {
            return "wav";
        }

        protected override void LogMessage(string message)
        {
            TTSLogger.Log($"[GPTSoVITSTTSCore] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }
}
