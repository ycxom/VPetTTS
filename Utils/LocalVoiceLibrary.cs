namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 本地语音库：用户自己维护的「文字 → 音频文件」对照表。
    ///
    /// 要朗读的文字在表里有对应文件时直接播放该文件，不请求任何 TTS 服务 ——
    /// 常用的问候、固定台词可以换成真人录音，也省掉一次网络往返。
    ///
    /// 对照表随改随生效：每次查询比对文件修改时间，变了才重新解析，
    /// 用户编辑保存后不用重启、也不用回设置窗口点保存。
    /// 音频文件在查询时才检查是否存在，先写表、后补文件也可以；
    /// 文件缺失时这一句照常走 TTS 服务，不会因为表写错就哑掉。
    /// </summary>
    public static class LocalVoiceLibrary
    {
        /// <summary>对照表文件名，放在语音目录根下</summary>
        public const string MapFileName = "voices.txt";

        /// <summary>默认语音目录：文档\VPetLLM\TTS\LocalVoice（用户数据，与设置放在一起）</summary>
        public static string DefaultDirectory { get; } = Path.Combine(TTSDataPaths.DataRoot, "LocalVoice");

        // 文字和文件名之间的分隔符。按最后一次出现切分，文字里带 -> 也不影响
        private static readonly string[] Separators = { "->", "-》", "→", "=>" };

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

        private static readonly object Gate = new object();
        private static string _loadedFile;
        private static DateTime _loadedStamp;
        private static bool _loadedMatchPunctuation;
        // 发布后只读，替换而不修改，查询方拿到引用后可以在锁外读
        private static Dictionary<string, string> _table = new Dictionary<string, string>();

        /// <summary>设置里的目录为空时用默认目录</summary>
        public static string ResolveDirectory(LocalVoiceSetting setting)
        {
            var dir = setting?.Directory;
            return string.IsNullOrWhiteSpace(dir) ? DefaultDirectory : Environment.ExpandEnvironmentVariables(dir.Trim());
        }

        /// <summary>
        /// 查找文字对应的本地音频文件。未启用、表里没有、文件不存在都返回 null，交给 TTS 服务。
        /// </summary>
        public static string Find(string text, LocalVoiceSetting setting)
        {
            if (setting is null || !setting.Enable || string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                var table = GetTable(setting);
                if (table.Count == 0)
                    return null;

                if (!table.TryGetValue(Normalize(text, setting.MatchPunctuation), out var path))
                    return null;

                if (File.Exists(path))
                    return path;

                Log($"对照表命中但音频文件不存在，本句改走 TTS 服务: {path}");
                return null;
            }
            catch (Exception ex)
            {
                // 对照表正被编辑器写入、目录没权限等：这一句走 TTS 服务，下一句再试
                Log($"读取本地语音对照表失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 建好语音目录，并在没有对照表时写一份带说明的模板。返回目录路径。
        /// </summary>
        public static string EnsureCreated(LocalVoiceSetting setting)
        {
            var dir = ResolveDirectory(setting);
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, MapFileName);
            if (!File.Exists(file))
            {
                // 带 BOM：老版本记事本按 ANSI 打开无 BOM 文件会把中文说明显示成乱码
                File.WriteAllText(file, Template, new UTF8Encoding(true));
                Log($"已创建本地语音对照表模板: {file}");
            }
            return dir;
        }

        /// <summary>
        /// 比较用的规范形式：全半角统一、英文不分大小写、忽略空白；
        /// 不匹配标点时再去掉标点和符号，「你好，主人！」与「你好主人」视为同一句。
        /// </summary>
        public static string Normalize(string text, bool matchPunctuation)
        {
            var s = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c))
                    continue;
                if (!matchPunctuation && (char.IsPunctuation(c) || char.IsSymbol(c)))
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> GetTable(LocalVoiceSetting setting)
        {
            var dir = ResolveDirectory(setting);
            var file = Path.Combine(dir, MapFileName);

            lock (Gate)
            {
                var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
                if (file == _loadedFile && stamp == _loadedStamp && setting.MatchPunctuation == _loadedMatchPunctuation)
                    return _table;

                // 先解析成功再记下版本：读失败（编辑器正在写）时下次查询会重试
                var table = stamp == DateTime.MinValue
                    ? new Dictionary<string, string>()
                    : Parse(ReadLines(file), dir, setting.MatchPunctuation);

                _table = table;
                _loadedFile = file;
                _loadedStamp = stamp;
                _loadedMatchPunctuation = setting.MatchPunctuation;

                if (stamp == DateTime.MinValue)
                    Log($"本地语音对照表不存在: {file}");
                else
                    Log($"已加载本地语音对照表: {table.Count} 条（{(setting.MatchPunctuation ? "标点必须一致" : "忽略标点")}）{file}");
                return table;
            }
        }

        private static Dictionary<string, string> Parse(string[] lines, string dir, bool matchPunctuation)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var lineNo = i + 1;
                if (!TrySplit(line, out var text, out var audio))
                {
                    Log($"对照表第 {lineNo} 行格式不对（应为 文字 -> 音频文件），已忽略: {line}");
                    continue;
                }

                var key = Normalize(text, matchPunctuation);
                if (key.Length == 0)
                {
                    Log($"对照表第 {lineNo} 行去掉标点后文字为空，已忽略: {line}");
                    continue;
                }

                if (!AudioPathHelper.IsSupportedAudioFormat(Path.GetExtension(audio)))
                {
                    Log($"对照表第 {lineNo} 行音频格式不支持（mp3/wav/ogg/flac/aac/m4a/wma），已忽略: {audio}");
                    continue;
                }

                string path;
                try
                {
                    // 相对路径以语音目录为基准；写了绝对路径时 Combine 直接取它
                    path = Path.GetFullPath(Path.Combine(dir, audio));
                }
                catch (Exception ex)
                {
                    Log($"对照表第 {lineNo} 行音频路径无效，已忽略: {audio}（{ex.Message}）");
                    continue;
                }

                if (table.ContainsKey(key))
                    Log($"对照表第 {lineNo} 行与前面的条目是同一句，以这一行为准: {text}");
                table[key] = path;
            }
            return table;
        }

        private static bool TrySplit(string line, out string text, out string audio)
        {
            text = audio = null;

            var index = -1;
            string separator = null;
            foreach (var candidate in Separators)
            {
                var i = line.LastIndexOf(candidate, StringComparison.Ordinal);
                if (i > index)
                {
                    index = i;
                    separator = candidate;
                }
            }
            if (index <= 0)
                return false;

            text = line.Substring(0, index).Trim();
            audio = line.Substring(index + separator.Length).Trim().Trim('"');
            return text.Length > 0 && audio.Length > 0;
        }

        /// <summary>
        /// 按 BOM 认 UTF-8/UTF-16；没有 BOM 先按 UTF-8 严格解码，
        /// 解不开说明是记事本「ANSI」存的，退回 GBK
        /// </summary>
        private static string[] ReadLines(string file)
        {
            var bytes = File.ReadAllBytes(file);
            string content;
            try
            {
                using var reader = new StreamReader(new MemoryStream(bytes), StrictUtf8, detectEncodingFromByteOrderMarks: true);
                content = reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                var gbk = CodePagesEncodingProvider.Instance.GetEncoding(936) ?? Encoding.Default;
                content = gbk.GetString(bytes);
            }
            return content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static void Log(string message)
        {
            TTSLogger.Log($"[LocalVoice] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        private const string Template =
@"# VPetTTS 本地语音对照表
#
# 每行一条：文字 -> 音频文件
# 要朗读的文字在这里能找到时，直接播放对应的音频文件，不请求 TTS 服务。
#
# · 音频文件写文件名（相对本目录）或完整路径都可以，子目录也行：greetings/morning.mp3
# · 支持 mp3 / wav / ogg / flac / aac / m4a / wma
# · 分隔符 -> 也可以写成 → 或 =>
# · 以 # 开头的行是注释，空行会被忽略
# · 改完保存即生效，不用重启
# · 比较时忽略空格、全半角和英文大小写
# · 设置里「标点匹配」关闭时还会忽略标点：「你好，主人！」和「你好主人」算同一句；
#   打开后标点也必须一致，「你好！」和「你好？」可以配不同的录音
# · 同一句写了多行时以最后一行为准
# · 表里有、文件却不在时，这一句照常走 TTS 服务
#
# 示例（去掉行首的 # 即可启用）：
# 你好，主人 -> 你好主人.mp3
# 早上好 -> greetings/morning.wav
";
    }
}
