using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace Vpet.Plugin.CustomTTS.Core.Providers
{
    /// <summary>
    /// Free TTS 实现 (POST API 格式)
    /// 配置从服务器动态获取
    /// </summary>
    public class FreeTTSCore : TTSCoreBase
    {
        public override string Name => "Free";

        private string _apiKey;
        private string _apiUrl;
        private string _model;
        private string _textLanguage;

        public FreeTTSCore(Setting settings) : base(settings)
        {
            LoadConfig();
            _textLanguage = settings.Free?.TextLanguage ?? "auto";
        }

        /// <summary>
        /// 更新语言设置
        /// </summary>
        public void UpdateLanguage(string language)
        {
            _textLanguage = language ?? "auto";
        }

        /// <summary>
        /// 重新加载配置。
        /// 配置文件由后台异步下载（不阻塞插件启动），下载完成后调用此方法热更新，
        /// 避免首次安装时需要重启才能使用 Free TTS。
        /// </summary>
        public void ReloadConfig()
        {
            LoadConfig();
        }

        private bool HasConfig => !string.IsNullOrEmpty(_apiUrl) && !string.IsNullOrEmpty(_apiKey);

        /// <summary>请求时配置还没下好，最多等这么久。语音要和气泡对齐，不能等太长。</summary>
        private static readonly TimeSpan ConfigDownloadWait = TimeSpan.FromSeconds(10);

        private void LoadConfig()
        {
            // 每句语音前都会调，只在内容变化时记日志
            var hadConfig = HasConfig;
            var oldKey = _apiKey;
            var oldUrl = _apiUrl;
            try
            {
                var config = FreeConfigManager.GetTTSConfig();
                if (config is not null)
                {
                    _apiKey = DecodeString(config["API_KEY"]?.ToString() ?? "");
                    _apiUrl = DecodeString(config["API_URL"]?.ToString() ?? "");
                    _model = config["Model"]?.ToString() ?? "";
                    if (_apiKey != oldKey || _apiUrl != oldUrl)
                        LogMessage(hadConfig ? "FreeTTSCore: 配置已更新" : "FreeTTSCore: 配置加载成功");
                }
                else
                {
                    if (hadConfig || oldKey is null)
                        LogMessage("FreeTTSCore: 配置文件不存在（可能仍在下载，发请求时会再读一次）");
                    _apiKey = "";
                    _apiUrl = "";
                    _model = "";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"FreeTTSCore: 加载配置失败: {ex.Message}");
                _apiKey = "";
                _apiUrl = "";
                _model = "";
            }
        }

        /// <summary>
        /// 每句语音前从磁盘重读配置：VPetLLM 每 5 分钟会刷新同一目录下的这份配置（服务端换密钥时），
        /// 以前这里只在构造和启动下载完成时各读一次，之后的变化都要重启才生效。
        /// 磁盘上没有就发起（或复用）一次下载，等它结束再读。
        /// </summary>
        private async Task<bool> EnsureConfigLoadedAsync()
        {
            LoadConfig();
            if (HasConfig) return true;

            var download = FreeConfigManager.RequestDownload();
            if (!download.IsCompleted)
            {
                LogMessage("TTS (Free): 配置仍在下载，等待完成");
                await Task.WhenAny(download, Task.Delay(ConfigDownloadWait)).ConfigureAwait(false);
            }

            LoadConfig();
            return HasConfig;
        }

        public override async Task<byte[]> GenerateAudioAsync(string text)
        {
            try
            {
                if (!await EnsureConfigLoadedAsync().ConfigureAwait(false))
                {
                    LogMessage("TTS (Free): 配置未加载，TTS功能不可用");
                    OnAudioGenerationError("Free TTS 配置下载失败，请检查网络连接后再试");
                    return Array.Empty<byte>();
                }

                // 先探一次 /health（结果 120 秒内复用）。服务器下线时这里几百毫秒就返回，
                // 而不是让下面那个 30 秒超时的请求把说话动画一起拖住。
                var (healthy, reason) = await FreeServiceHealthCheck
                    .IsHealthyAsync(_apiUrl, GetProxy(), m => LogMessage($"TTS (Free): {m}"))
                    .ConfigureAwait(false);
                if (!healthy)
                {
                    LogMessage($"TTS (Free): 预检判定服务不可用（{reason}），跳过本次请求");
                    OnAudioGenerationError($"Free TTS {reason}，已跳过本次语音");
                    return Array.Empty<byte>();
                }

                LogMessage($"TTS (Free): 发送请求，文本长度: {text.Length}");

                var requestBody = new
                {
                    text = text,
                    text_lang = _textLanguage,
                    api_key = _apiKey,
                };

                LogMessage($"TTS (Free): 使用语言: {_textLanguage}");

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var startTime = DateTime.Now;
                using var client = CreateHttpClient();

                // 业务请求原样发出，鉴权头由本 MOD 自带的原生组件补齐
                using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
                {
                    Content = content
                };

                // 官方服务请求：Steam 身份、鉴权头、应用层加解密都由
                // 自带的 VPetLLM.SecureCommunication.dll 处理。
                if (!AuthenticatedServiceTransport.IsAvailable)
                {
                    var unavailable = AuthenticatedServiceTransport.StatusMessage;
                    LogMessage($"TTS (Free): 鉴权通道不可用 - {unavailable}");
                    AuthenticatedServiceTransport.NotifyUnavailableOnce("Free TTS");
                    OnAudioGenerationError($"Free TTS 不可用：{unavailable}");
                    return Array.Empty<byte>();
                }

                using var response = await AuthenticatedServiceTransport.SendAsync(client, request);
                var elapsed = (DateTime.Now - startTime).TotalSeconds;

                LogMessage($"TTS (Free): 响应接收完成，耗时 {elapsed:F2} 秒, 状态: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogMessage($"TTS (Free): API 错误: {response.StatusCode} - {errorContent}");

                    try
                    {
                        var errorObj = JObject.Parse(errorContent);
                        var errorMessage = errorObj["message"]?.ToString() ?? "未知错误";
                        OnAudioGenerationError($"Free TTS 服务错误: {errorMessage}");
                    }
                    catch
                    {
                        OnAudioGenerationError($"Free TTS 服务错误: {response.StatusCode}");
                    }

                    return Array.Empty<byte>();
                }

                var audioData = await response.Content.ReadAsByteArrayAsync();
                LogMessage($"TTS (Free): 音频生成成功，大小: {audioData.Length} bytes");

                // 真实请求成功是最强的可用性证据，立刻抹掉可能存在的误判。
                FreeServiceHealthCheck.ReportOutcome(_apiUrl, true, "服务正常");

                OnAudioGenerated(audioData);
                return audioData;
            }
            catch (TaskCanceledException ex)
            {
                LogMessage($"TTS (Free): 请求超时: {ex.Message}");
                // 回填结论：接下来 120 秒内的请求直接跳过，不必再各等 30 秒。
                FreeServiceHealthCheck.ReportOutcome(_apiUrl, false, "服务无响应");
                OnAudioGenerationError("请求超时，请检查网络连接");
                return Array.Empty<byte>();
            }
            catch (HttpRequestException ex)
            {
                LogMessage($"TTS (Free): 网络错误: {ex.Message}");
                FreeServiceHealthCheck.ReportOutcome(_apiUrl, false, "无法连接到服务");
                OnAudioGenerationError($"网络错误: {ex.Message}");
                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                LogMessage($"TTS (Free): 生成音频异常: {ex.Message}");
                OnAudioGenerationError($"生成音频异常: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public override string GetAudioFormat()
        {
            return "wav";
        }

        private string DecodeString(string encodedString)
        {
            try
            {
                if (string.IsNullOrEmpty(encodedString))
                {
                    return "";
                }

                var hexBytes = new byte[encodedString.Length / 2];
                for (int i = 0; i < hexBytes.Length; i++)
                {
                    hexBytes[i] = Convert.ToByte(encodedString.Substring(i * 2, 2), 16);
                }

                var base64String = Encoding.UTF8.GetString(hexBytes);
                var finalBytes = Convert.FromBase64String(base64String);
                var result = Encoding.UTF8.GetString(finalBytes);

                return result;
            }
            catch (Exception)
            {
                return "";
            }
        }

        protected override void LogMessage(string message)
        {
            TTSLogger.Log($"[FreeTTSCore] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }
}
