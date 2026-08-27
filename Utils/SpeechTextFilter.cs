using System.Text.RegularExpressions;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 朗读文本过滤器：把括号包裹的动作描写从「要念出来的文本」里剔除。
    ///
    /// 气泡显示的是宿主原文，这里只加工送进 TTS 的副本，
    /// 所以效果是「动作描写看得到、听不到」。
    ///
    /// 过滤是幂等的（过滤后的文本再过一次不会变化），
    /// 因此允许在多个下游入口各自调用而不会互相破坏缓存键。
    /// </summary>
    public static class SpeechTextFilter
    {
        /// <summary>
        /// 一组成对的括号。开闭字符各自可以有多个变体，半角全角混着写也能配上。
        /// </summary>
        private readonly struct BracketGroup
        {
            public BracketGroup(string open, string close)
            {
                Open = open;
                Close = close;
            }

            public string Open { get; }
            public string Close { get; }
        }

        private static readonly BracketGroup RoundGroup = new BracketGroup("(（﹙", ")）﹚");
        private static readonly BracketGroup SquareGroup = new BracketGroup("[【〔［", "]】〕］");
        private static readonly BracketGroup CurlyGroup = new BracketGroup("{｛", "}｝");
        private static readonly BracketGroup AngleGroup = new BracketGroup("<〈《＜", ">〉》＞");

        /// <summary>
        /// 成对星号包裹的动作描写：*摸摸头* / **摸摸头**。
        ///
        /// 沿用 Markdown 强调的判定：星号内侧不能紧挨空白，
        /// 这样「3 * 4 * 5」这类乘法算式就不会被当成动作描写吃掉。
        /// </summary>
        private static readonly Regex AsteriskPattern =
            new Regex(@"\*\*(?=\S)[^*]+(?<=\S)\*\*|\*(?=\S)[^*]+(?<=\S)\*", RegexOptions.Compiled);

        /// <summary>行内连续空白</summary>
        private static readonly Regex InlineWhitespacePattern =
            new Regex("[ \t　]+", RegexOptions.Compiled);

        /// <summary>剔除括号后留在句首的孤立标点</summary>
        private static readonly Regex LeadingPunctuationPattern =
            new Regex(@"^[\s,，、。．.;；:：!！?？~～…—\-]+", RegexOptions.Compiled);

        /// <summary>剔除括号后挤在一起的重复标点，保留最后一个</summary>
        private static readonly Regex DuplicatePunctuationPattern =
            new Regex(@"[,，、。．.;；:：!！?？]\s*(?=[,，、。．.;；:：!！?？])", RegexOptions.Compiled);

        /// <summary>
        /// 按设置过滤待朗读文本。
        /// </summary>
        /// <returns>
        /// 过滤后的文本；整句都是动作描写时返回空字符串，调用方据此跳过本次 TTS。
        /// 过滤未启用时原样返回。
        /// </returns>
        public static string Apply(string text, TextFilterSetting setting)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            if (setting is null || !setting.Enable)
                return text;

            var groups = BuildGroups(setting);
            if (groups.Count == 0 && !setting.Asterisk)
                return text;

            var result = text;
            if (groups.Count > 0)
                result = RemoveBracketedSpans(result, groups);
            if (setting.Asterisk)
                result = AsteriskPattern.Replace(result, "");

            result = Normalize(result);

            // 只剩标点和空白，说明整句都是动作描写，直接不朗读
            return HasSpeakableContent(result) ? result : string.Empty;
        }

        /// <summary>
        /// 过滤是否真的改变了文本，仅用于日志，避免刷屏
        /// </summary>
        public static bool Changed(string original, string filtered)
            => !string.Equals(original, filtered, StringComparison.Ordinal);

        private static List<BracketGroup> BuildGroups(TextFilterSetting setting)
        {
            var groups = new List<BracketGroup>(6);
            if (setting.RoundBracket) groups.Add(RoundGroup);
            if (setting.SquareBracket) groups.Add(SquareGroup);
            if (setting.CurlyBracket) groups.Add(CurlyGroup);
            if (setting.AngleBracket) groups.Add(AngleGroup);
            groups.AddRange(ParseCustomPairs(setting.CustomPairs));
            return groups;
        }

        /// <summary>
        /// 解析自定义括号对。写法是「开闭两个字符为一组」，组间可用空格或逗号分隔。
        /// </summary>
        private static List<BracketGroup> ParseCustomPairs(string custom)
        {
            var groups = new List<BracketGroup>();
            if (string.IsNullOrWhiteSpace(custom))
                return groups;

            var chars = new List<char>(custom.Length);
            foreach (var c in custom)
            {
                if (char.IsWhiteSpace(c) || c == ',' || c == '，' || c == '、' || c == ';' || c == '；')
                    continue;
                chars.Add(c);
            }

            for (var i = 0; i + 1 < chars.Count; i += 2)
            {
                var open = chars[i];
                var close = chars[i + 1];
                if (open == close)
                    continue; // 同一个字符不成对，这类对称标记交给星号规则处理
                groups.Add(new BracketGroup(open.ToString(), close.ToString()));
            }

            return groups;
        }

        /// <summary>
        /// 扫描并删除确实闭合了的括号区间（含括号本身）。
        ///
        /// 用栈记录未闭合的开括号，只有遇到匹配的闭括号才落成一个待删区间；
        /// 扫到结尾仍留在栈里的开括号视为普通文字保留下来，
        /// 否则一个落单的左括号会把后面整句话都吞掉。
        /// 嵌套时只记录最外层区间，内层随外层一并删除。
        /// </summary>
        private static string RemoveBracketedSpans(string text, List<BracketGroup> groups)
        {
            var openStack = new List<(int GroupIndex, int Position)>();
            var spans = new List<(int Start, int End)>();

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                var closeGroup = IndexOfGroupByClose(groups, c);
                if (closeGroup >= 0 && TryPopMatchingOpen(openStack, closeGroup, out var openPosition))
                {
                    // 栈空说明刚闭合的是最外层，记录整段
                    if (openStack.Count == 0)
                        spans.Add((openPosition, i));
                    continue;
                }

                var openGroup = IndexOfGroupByOpen(groups, c);
                if (openGroup >= 0)
                    openStack.Add((openGroup, i));
            }

            if (spans.Count == 0)
                return text;

            var builder = new StringBuilder(text.Length);
            var cursor = 0;
            foreach (var (start, end) in spans)
            {
                if (start > cursor)
                    builder.Append(text, cursor, start - cursor);
                cursor = end + 1;
            }
            if (cursor < text.Length)
                builder.Append(text, cursor, text.Length - cursor);

            return builder.ToString();
        }

        /// <summary>
        /// 自栈顶向下找同组的开括号。找到就连同其上的未闭合项一起弹出，
        /// 那些是交错写法里的残次品，跟着一起删掉。
        /// </summary>
        private static bool TryPopMatchingOpen(
            List<(int GroupIndex, int Position)> openStack,
            int groupIndex,
            out int openPosition)
        {
            for (var i = openStack.Count - 1; i >= 0; i--)
            {
                if (openStack[i].GroupIndex != groupIndex)
                    continue;

                openPosition = openStack[i].Position;
                openStack.RemoveRange(i, openStack.Count - i);
                return true;
            }

            openPosition = -1;
            return false;
        }

        private static int IndexOfGroupByOpen(List<BracketGroup> groups, char c)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Open.IndexOf(c) >= 0)
                    return i;
            }
            return -1;
        }

        private static int IndexOfGroupByClose(List<BracketGroup> groups, char c)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Close.IndexOf(c) >= 0)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 收拾删除后留下的空白和孤立标点，避免 TTS 念出奇怪的停顿
        /// </summary>
        private static string Normalize(string text)
        {
            text = InlineWhitespacePattern.Replace(text, " ");
            text = DuplicatePunctuationPattern.Replace(text, "");

            var lines = text.Split('\n');
            var kept = new List<string>(lines.Length);
            foreach (var raw in lines)
            {
                var line = LeadingPunctuationPattern.Replace(raw.Trim(), "").Trim();
                if (line.Length > 0)
                    kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        /// <summary>
        /// 是否还有值得念的内容，有字母或数字即算
        /// </summary>
        private static bool HasSpeakableContent(string text)
        {
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                    return true;
            }
            return false;
        }
    }
}
