using System.Reflection;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 异常详情格式化。
    /// 只记 ex.Message 会丢掉定位问题所需的全部信息——尤其是程序集绑定失败
    /// （FileLoadException / FileNotFoundException），其 Message 只说"加载不了"，
    /// 真正指明"被谁挡住"的是 FusionLog 和内部异常链。
    /// </summary>
    public static class ExceptionDetail
    {
        /// <summary>
        /// 生成单行摘要：异常类型 + 消息（+ 内部异常类型/消息）。
        /// 用于日志首行，便于快速扫读。
        /// </summary>
        public static string Brief(Exception ex)
        {
            if (ex is null) return "(null exception)";

            var sb = new StringBuilder();
            sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);

            var inner = ex.InnerException;
            int depth = 0;
            while (inner is not null && depth < 5)
            {
                sb.Append(" ---> ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
                inner = inner.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 生成完整多行详情：异常链 + 程序集绑定日志（FusionLog）+ 堆栈。
        /// </summary>
        public static string Full(Exception ex)
        {
            if (ex is null) return "(null exception)";

            var sb = new StringBuilder();
            AppendOne(sb, ex, 0);
            return sb.ToString();
        }

        private static void AppendOne(StringBuilder sb, Exception ex, int depth)
        {
            if (ex is null || depth > 5) return;

            var indent = depth == 0 ? "" : new string(' ', depth * 2) + "└─ ";
            sb.Append(indent).Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);

            // 程序集绑定失败：FusionLog 会指出运行时探测了哪些路径、为什么拒绝。
            // 需要在 runtimeconfig 中开启绑定日志才有内容，没有时保持静默。
            switch (ex)
            {
                case FileLoadException fle when !string.IsNullOrEmpty(fle.FusionLog):
                    sb.Append(indent).Append("  FileName: ").AppendLine(fle.FileName);
                    sb.Append(indent).Append("  FusionLog: ").AppendLine(fle.FusionLog);
                    break;
                case FileLoadException fle:
                    sb.Append(indent).Append("  FileName: ").AppendLine(fle.FileName);
                    sb.Append(indent).AppendLine("  FusionLog: (空，未启用程序集绑定日志)");
                    break;
                case FileNotFoundException fnf when !string.IsNullOrEmpty(fnf.FusionLog):
                    sb.Append(indent).Append("  FileName: ").AppendLine(fnf.FileName);
                    sb.Append(indent).Append("  FusionLog: ").AppendLine(fnf.FusionLog);
                    break;
                case FileNotFoundException fnf:
                    sb.Append(indent).Append("  FileName: ").AppendLine(fnf.FileName);
                    sb.Append(indent).AppendLine("  FusionLog: (空，未启用程序集绑定日志)");
                    break;
                case TypeLoadException tle:
                    sb.Append(indent).Append("  TypeName: ").AppendLine(tle.TypeName);
                    break;
                case ReflectionTypeLoadException rtle:
                    foreach (var le in rtle.LoaderExceptions)
                    {
                        if (le is not null)
                            sb.Append(indent).Append("  LoaderException: ").AppendLine(Brief(le));
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.Append(indent).AppendLine("  StackTrace:");
                sb.AppendLine(ex.StackTrace);
            }

            AppendOne(sb, ex.InnerException, depth + 1);
        }

        /// <summary>
        /// 从异常链里找出加载失败的程序集简单名（如 "Newtonsoft.Json"）。
        /// 找不到时返回 null。
        /// </summary>
        public static string TryGetFailedAssemblyName(Exception ex)
        {
            int depth = 0;
            while (ex is not null && depth < 6)
            {
                string fileName = ex switch
                {
                    FileLoadException fle => fle.FileName,
                    FileNotFoundException fnf => fnf.FileName,
                    BadImageFormatException bif => bif.FileName,
                    _ => null
                };

                if (!string.IsNullOrEmpty(fileName))
                {
                    // FileName 形如 "Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=..."
                    var comma = fileName.IndexOf(',');
                    var simple = comma > 0 ? fileName.Substring(0, comma) : fileName;
                    simple = simple.Trim();
                    if (simple.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        simple = simple.Substring(0, simple.Length - 4);
                    return simple;
                }

                ex = ex.InnerException;
                depth++;
            }
            return null;
        }

        /// <summary>
        /// 当前 AppDomain 已加载的、与指定简单名匹配的程序集清单。
        /// 排查"同名不同版本/不同路径"冲突时非常关键。
        /// </summary>
        public static string LoadedAssemblies(string simpleName)
        {
            try
            {
                var matches = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    .Select(a =>
                    {
                        string loc;
                        try { loc = string.IsNullOrEmpty(a.Location) ? "(动态/内存)" : a.Location; }
                        catch { loc = "(位置不可用)"; }
                        return $"{a.GetName().FullName} @ {loc}";
                    })
                    .ToList();

                return matches.Count == 0
                    ? $"当前 AppDomain 未加载任何名为 '{simpleName}' 的程序集"
                    : $"当前 AppDomain 已加载 {matches.Count} 个 '{simpleName}': {string.Join(" | ", matches)}";
            }
            catch (Exception ex)
            {
                return $"枚举已加载程序集失败: {ex.Message}";
            }
        }
    }
}
