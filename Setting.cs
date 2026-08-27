namespace Vpet.Plugin.CustomTTS
{
    public class Setting
    {
        /// <summary>
        /// 启用TTS
        /// </summary>
        [Line]
        public bool Enable { get; set; } = true;

        /// <summary>
        /// 当前TTS提供商
        /// </summary>
        [Line]
        public string Provider { get; set; } = "Free";

        /// <summary>
        /// 音量 (0-200)
        /// </summary>
        [Line]
        public double Volume { get; set; } = 100.0;

        /// <summary>
        /// 语速 (0.1-3.0)
        /// </summary>
        [Line]
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// 启用缓存
        /// </summary>
        [Line]
        public bool EnableCache { get; set; } = true;

        /// <summary>
        /// 偏好 VPet 内置播放器（Main.PlayVoice）。
        /// 宿主会在语音播放期间自动保持说话动画和气泡（与 EdgeTTS 插件同机制）；
        /// false = mpv 可用时优先 mpv（高码率支持），mpv 不可用时仍回退内置播放器。
        /// </summary>
        [Line]
        public bool PreferVPetBuiltInPlayer { get; set; } = false;

        /// <summary>
        /// 请求超时时间（秒）
        /// </summary>
        [Line]
        public int RequestTimeout { get; set; } = 30;

        /// <summary>
        /// 代理设置
        /// </summary>
        [Line]
        public ProxySetting Proxy { get; set; } = new ProxySetting();

        /// <summary>
        /// Free TTS设置
        /// </summary>
        [Line]
        public FreeTTSSetting Free { get; set; } = new FreeTTSSetting();

        /// <summary>
        /// OpenAI TTS设置
        /// </summary>
        [Line]
        public OpenAITTSSetting OpenAI { get; set; } = new OpenAITTSSetting();

        /// <summary>
        /// GPT-SoVITS设置
        /// </summary>
        [Line]
        public GPTSoVITSTTSSetting GPTSoVITS { get; set; } = new GPTSoVITSTTSSetting();

        /// <summary>
        /// URL TTS设置
        /// </summary>
        [Line]
        public URLTTSSetting URL { get; set; } = new URLTTSSetting();

        /// <summary>
        /// DIY TTS设置
        /// </summary>
        [Line]
        public DIYTTSSetting DIY { get; set; } = new DIYTTSSetting();

        /// <summary>
        /// 朗读文本过滤（括号内的动作描写只显示不朗读）
        /// </summary>
        [Line]
        public TextFilterSetting TextFilter { get; set; } = new TextFilterSetting();

        /// <summary>
        /// 屏蔽的插件名称列表（这些插件触发的 Say 不会生成 TTS）
        /// </summary>
        [Line]
        public List<string> BlockedPlugins { get; set; } = new List<string>();

        /// <summary>
        /// 用户明确允许的云端屏蔽 mod ID 列表
        /// （云端推荐屏蔽但用户选择放行的 mod）
        /// </summary>
        [Line]
        public List<string> CloudBanAllowedMods { get; set; } = new List<string>();

        /// <summary>
        /// 验证设置
        /// </summary>
        public void Validate()
        {
            if (Volume < 0) Volume = 0;
            if (Volume > 200) Volume = 200;
            if (Speed < 0.1) Speed = 0.1;
            if (Speed > 3.0) Speed = 3.0;

            if (RequestTimeout < 5) RequestTimeout = 5;
            if (RequestTimeout > 300) RequestTimeout = 300;

            if (string.IsNullOrWhiteSpace(Provider))
                Provider = "Free";

            Free ??= new FreeTTSSetting();
            Free.TextLanguage = TTSLanguage.Normalize(Free.TextLanguage);

            GPTSoVITS ??= new GPTSoVITSTTSSetting();
            GPTSoVITS.TextLanguage = TTSLanguage.Normalize(GPTSoVITS.TextLanguage, TTSLanguage.Chinese);
            GPTSoVITS.PromptLanguage = TTSLanguage.Normalize(
                GPTSoVITS.PromptLanguage,
                GPTSoVITS.TextLanguage);

            TextFilter ??= new TextFilterSetting();
            TextFilter.CustomPairs ??= "";
        }
    }

    /// <summary>
    /// 朗读文本过滤设置。
    ///
    /// 命中的片段只是不送进 TTS，气泡里的原文不受影响，
    /// 也就是「动作描写看得到、听不到」。
    /// </summary>
    public class TextFilterSetting
    {
        /// <summary>
        /// 总开关。关闭时文本原样朗读（旧行为）
        /// </summary>
        [Line]
        public bool Enable { get; set; } = false;

        /// <summary>圆括号 ( ) （ ）</summary>
        [Line]
        public bool RoundBracket { get; set; } = true;

        /// <summary>方括号 [ ] 【 】 〔 〕</summary>
        [Line]
        public bool SquareBracket { get; set; } = true;

        /// <summary>花括号 { } ｛ ｝</summary>
        [Line]
        public bool CurlyBracket { get; set; } = false;

        /// <summary>
        /// 尖括号 &lt; &gt; 〈 〉 《 》。
        /// 默认关闭：书名号和数学比较符号都会被误伤
        /// </summary>
        [Line]
        public bool AngleBracket { get; set; } = false;

        /// <summary>成对星号包裹的动作描写 *摸摸头* / **摸摸头**</summary>
        [Line]
        public bool Asterisk { get; set; } = true;

        /// <summary>
        /// 自定义括号对，开闭两个字符为一组，组间可用空格或逗号分隔。
        /// 例如「」『』
        /// </summary>
        [Line]
        public string CustomPairs { get; set; } = "";
    }

    /// <summary>
    /// TTS 内部统一使用的语言代码及旧配置兼容转换。
    /// 提供商如需不同格式，应在各自的请求适配层中转换。
    /// </summary>
    public static class TTSLanguage
    {
        public const string Auto = "auto";
        public const string Chinese = "zh";
        public const string English = "en";
        public const string Japanese = "ja";
        public const string Cantonese = "yue";
        public const string Korean = "ko";

        private static readonly Dictionary<string, string> LanguageOptions = new()
        {
            { Auto, "自动检测" },
            { Chinese, "中文" },
            { English, "英语" },
            { Japanese, "日语" },
            { Cantonese, "粤语" },
            { Korean, "韩语" }
        };

        /// <summary>
        /// 可供设置界面和提供商复用的语言选项。
        /// </summary>
        public static IReadOnlyDictionary<string, string> SupportedLanguages => LanguageOptions;

        /// <summary>
        /// 将旧版显示名称、常见别名和区域语言代码归一化为内部语言代码。
        /// 未知值回退到 <paramref name="fallback"/>，回退值无效时使用 auto。
        /// </summary>
        public static string Normalize(string language, string fallback = Auto)
        {
            var normalizedFallback = NormalizeKnownLanguage(fallback) ?? Auto;
            return NormalizeKnownLanguage(language) ?? normalizedFallback;
        }

        private static string NormalizeKnownLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return null;

            var value = language.Trim().Replace('_', '-').ToLowerInvariant();
            var exactMatch = value switch
            {
                "auto" or "auto-detect" or "autodetect" or "自动" or "自动检测" or "自動" or "自動檢測" or "多语种混合" or "多語種混合" => Auto,
                "zh" or "cn" or "all-zh" or "all_zh" or "chinese" or "中文" or "汉语" or "漢語" or "普通话" or "普通話" or "中英混合" => Chinese,
                "en" or "english" or "英文" or "英语" or "英語" => English,
                "ja" or "jp" or "all-ja" or "all_ja" or "japanese" or "日文" or "日语" or "日語" or "日本語" or "日英混合" => Japanese,
                "yue" or "zh-yue" or "all-yue" or "all_yue" or "cantonese" or "粤语" or "粵語" or "粤英混合" or "粵英混合" => Cantonese,
                "ko" or "kr" or "all-ko" or "all_ko" or "korean" or "韩语" or "韓語" or "한국어" or "韩英混合" or "韓英混合" => Korean,
                _ => null
            };

            if (exactMatch is not null)
                return exactMatch;

            if (value.StartsWith("ja-", StringComparison.Ordinal)) return Japanese;
            if (value.StartsWith("en-", StringComparison.Ordinal)) return English;
            if (value.StartsWith("ko-", StringComparison.Ordinal)) return Korean;
            if (value.StartsWith("yue-", StringComparison.Ordinal)) return Cantonese;
            if (value.StartsWith("zh-", StringComparison.Ordinal)) return Chinese;

            return null;
        }
    }

    public class ProxySetting
    {
        [Line]
        public bool IsEnabled { get; set; } = false;
        [Line]
        public bool FollowSystemProxy { get; set; } = false;
        [Line]
        public string Protocol { get; set; } = "http";
        [Line]
        public string Address { get; set; } = "127.0.0.1:8080";
        [Line]
        public bool ForAllAPI { get; set; } = false;
        [Line]
        public bool ForTTS { get; set; } = true;
    }

    public class FreeTTSSetting
    {
        /// <summary>
        /// 文本语言设置
        /// auto=自动检测, zh=中文, en=英语, ja=日语, yue=粤语, ko=韩语
        /// </summary>
        [Line]
        public string TextLanguage { get; set; } = "auto";

        /// <summary>
        /// 获取支持的语言列表
        /// </summary>
        public static Dictionary<string, string> SupportedLanguages =>
            TTSLanguage.SupportedLanguages.ToDictionary(option => option.Key, option => option.Value);
    }

    public class OpenAITTSSetting
    {
        [Line]
        public string ApiKey { get; set; } = "";
        [Line]
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        [Line]
        public string Model { get; set; } = "tts-1";
        [Line]
        public string Voice { get; set; } = "alloy";
        [Line]
        public string Format { get; set; } = "mp3";
    }

    public class GPTSoVITSTTSSetting
    {
        [Line]
        public string BaseUrl { get; set; } = "http://127.0.0.1:9880";
        [Line]
        public string ApiMode { get; set; } = "WebUI"; // WebUI or ApiV2
        [Line]
        public string ModelName { get; set; } = "";
        [Line]
        public string ReferWavPath { get; set; } = "";
        [Line]
        public string PromptText { get; set; } = "";
        [Line]
        public string TextLanguage { get; set; } = TTSLanguage.Chinese;
        [Line]
        public string PromptLanguage { get; set; } = ""; // 旧配置缺少此字段时跟随 TextLanguage
        [Line]
        public double Temperature { get; set; } = 1.0;
        [Line]
        public double Speed { get; set; } = 1.0;
    }

    public class URLTTSSetting
    {
        [Line]
        public string BaseUrl { get; set; } = "";
        [Line]
        public string Voice { get; set; } = "36";
        [Line]
        public string Method { get; set; } = "GET";
    }

    public class DIYTTSSetting
    {
        [Line]
        public string BaseUrl { get; set; } = "";
        [Line]
        public string Method { get; set; } = "POST";
        [Line]
        public string ContentType { get; set; } = "application/json";
        [Line]
        public string RequestBody { get; set; } = "";
        [Line]
        public List<CustomHeader> CustomHeaders { get; set; } = new();
        [Line]
        public string ResponseFormat { get; set; } = "mp3";
    }

    public class CustomHeader
    {
        [Line]
        public string Key { get; set; } = "";
        [Line]
        public string Value { get; set; } = "";
        [Line]
        public bool IsEnabled { get; set; } = true;
    }
}
