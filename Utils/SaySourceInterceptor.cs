using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 通过 Harmony 补丁拦截 Main.Say/SayRnd 调用，在 Task.Run 之前捕获调用栈，
    /// 识别消息来源插件并标记被屏蔽的 SayInfo。
    /// </summary>
    public static class SaySourceInterceptor
    {
        private static Harmony _harmony;
        private static bool _initialized;

        /// <summary>
        /// 被屏蔽的 SayInfo → 来源插件名称
        /// 使用 object key 以避免跨程序集类型不匹配
        /// </summary>
        private static readonly ConcurrentDictionary<object, string> _blockedSayInfos = new();

        /// <summary>
        /// AsyncLocal 用于跨 Task.Run 传递来源插件名称
        /// </summary>
        private static readonly AsyncLocal<string> _asyncSourcePlugin = new();

        /// <summary>
        /// 屏蔽的插件程序集名称 → 插件名称
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> _blockedAssemblyMap = new();

        /// <summary>
        /// 所有已加载插件：程序集名称 → 插件名称
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> _pluginAssemblyMap = new();

        /// <summary>
        /// VPet 核心程序集名称前缀（跳过这些程序集）
        /// </summary>
        private static readonly string[] _skipAssemblyPrefixes = new[]
        {
            "VPet-Simulator",
            "LinePutScript",
            "System",
            "Microsoft",
            "mscorlib",
            "netstandard",
            "WindowsBase",
            "PresentationCore",
            "PresentationFramework",
            "Panuon",
        };

        /// <summary>
        /// VPetTTS 自身程序集名称
        /// </summary>
        private static string _selfAssemblyName;

        /// <summary>
        /// 日志回调
        /// </summary>
        private static Action<string> _logAction;

        /// <summary>
        /// 初始化拦截器，应用 Harmony 补丁
        /// </summary>
        public static void Initialize(IMainWindow mw, List<string> blockedPlugins, Action<string> logAction = null)
        {
            if (_initialized) return;

            _logAction = logAction;
            _selfAssemblyName = typeof(SaySourceInterceptor).Assembly.GetName().Name;

            BuildPluginAssemblyMap(mw);
            UpdateBlockedPlugins(blockedPlugins);
            ApplyPatches(mw);

            _initialized = true;
            Log($"SaySourceInterceptor 初始化完成，屏蔽插件数: {_blockedAssemblyMap.Count}");
        }

        /// <summary>
        /// 更新屏蔽的插件列表（运行时可调用）
        /// </summary>
        public static void UpdateBlockedPlugins(List<string> blockedPlugins)
        {
            _blockedAssemblyMap.Clear();

            if (blockedPlugins == null || blockedPlugins.Count == 0) return;

            var blockedSet = new HashSet<string>(blockedPlugins, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _pluginAssemblyMap)
            {
                if (blockedSet.Contains(kvp.Value))
                {
                    _blockedAssemblyMap[kvp.Key] = kvp.Value;
                }
            }

            Log($"屏蔽列表已更新: {string.Join(", ", _blockedAssemblyMap.Values.Distinct())}");
        }

        /// <summary>
        /// 检查 SayInfo 是否被屏蔽，并从跟踪字典中移除
        /// </summary>
        public static bool IsBlockedAndRemove(SayInfo sayInfo, out string sourcePlugin)
        {
            if (sayInfo == null || _blockedSayInfos.IsEmpty)
            {
                sourcePlugin = null;
                return false;
            }

            // 用 object key 查找，兼容跨程序集类型
            return _blockedSayInfos.TryRemove(sayInfo, out sourcePlugin);
        }

        /// <summary>
        /// 检查 SayInfo 是否被屏蔽（简化版本）
        /// </summary>
        public static bool IsBlockedAndRemove(SayInfo sayInfo)
        {
            return IsBlockedAndRemove(sayInfo, out _);
        }

        /// <summary>
        /// 获取当前被跟踪的屏蔽 SayInfo 数量（调试用）
        /// </summary>
        public static int TrackedBlockedCount => _blockedSayInfos.Count;

        /// <summary>
        /// 获取所有已加载插件信息（供设置界面使用）
        /// </summary>
        public static IReadOnlyDictionary<string, string> PluginAssemblyMap => _pluginAssemblyMap;

        /// <summary>
        /// 卸载 Harmony 补丁
        /// </summary>
        public static void Unpatch()
        {
            _harmony?.UnpatchAll(_harmony.Id);
            _initialized = false;
            _blockedSayInfos.Clear();
            _blockedAssemblyMap.Clear();
            Log("SaySourceInterceptor 已卸载");
        }

        #region Private Methods

        private static void BuildPluginAssemblyMap(IMainWindow mw)
        {
            _pluginAssemblyMap.Clear();

            if (mw?.Plugins == null) return;

            foreach (var plugin in mw.Plugins)
            {
                try
                {
                    var asmName = plugin.GetType().Assembly.GetName().Name;
                    var pluginName = plugin.PluginName;
                    _pluginAssemblyMap[asmName] = pluginName;
                    Log($"已注册插件: {pluginName} → 程序集 {asmName}");
                }
                catch (Exception ex)
                {
                    Log($"扫描插件时出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 通过方法名和参数类型名称查找方法（遍历继承链，避免跨程序集 Type 不匹配）
        /// </summary>
        private static MethodInfo FindMethod(Type targetType, string methodName, string[] paramTypeNames)
        {
            // 向上遍历继承链（mw.Main.GetType() 可能是 Main 的子类）
            var type = targetType;
            while (type != null && type != typeof(object))
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                {
                    if (m.Name != methodName) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != paramTypeNames.Length) continue;

                    bool match = true;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType.Name != paramTypeNames[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        Log($"FindMethod: 在 {type.Name} 上找到 {methodName}({string.Join(", ", paramTypeNames)})");
                        return m;
                    }
                }
                type = type.BaseType;
            }
            return null;
        }

        private static void ApplyPatches(IMainWindow mw)
        {
            _harmony = new Harmony("com.vpettts.saysourceinterceptor");

            var mainType = mw.Main.GetType();
            Log($"目标类型: {mainType.FullName} (Base: {mainType.BaseType?.FullName})");
            Log($"目标程序集: {mainType.Assembly.GetName().Name} v{mainType.Assembly.GetName().Version}");
            Log($"插件引用程序集: {typeof(SayInfoWithOutStream).Assembly.GetName().Name} v{typeof(SayInfoWithOutStream).Assembly.GetName().Version}");

            // 诊断：列出目标类型上所有 Say/SayRnd 方法
            var allMethods = mainType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Say" || m.Name == "SayRnd");
            foreach (var m in allMethods)
            {
                var pNames = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Log($"发现方法: {m.DeclaringType.Name}.{m.Name}({pNames})");
            }

            var patchType = typeof(SaySourceInterceptor);

            // Say(SayInfoWithOutStream)
            PatchByName(mainType, "Say", new[] { "SayInfoWithOutStream" },
                patchType.GetMethod(nameof(SayPrefix), BindingFlags.NonPublic | BindingFlags.Static), null);

            // Say(SayInfoWithStream)
            PatchByName(mainType, "Say", new[] { "SayInfoWithStream" },
                patchType.GetMethod(nameof(SayPrefix), BindingFlags.NonPublic | BindingFlags.Static), null);

            // SayRnd(String, Boolean, String)
            PatchByName(mainType, "SayRnd", new[] { "String", "Boolean", "String" },
                patchType.GetMethod(nameof(SayRndPrefix), BindingFlags.NonPublic | BindingFlags.Static),
                patchType.GetMethod(nameof(SayRndPostfix), BindingFlags.NonPublic | BindingFlags.Static));

            // SayRnd(SayInfoWithStream)
            PatchByName(mainType, "SayRnd", new[] { "SayInfoWithStream" },
                patchType.GetMethod(nameof(SayRndPrefix), BindingFlags.NonPublic | BindingFlags.Static),
                patchType.GetMethod(nameof(SayRndPostfix), BindingFlags.NonPublic | BindingFlags.Static));

            Log("Harmony 补丁应用完毕");
        }

        private static void PatchByName(Type targetType, string methodName, string[] paramTypeNames,
            MethodInfo prefixMethod, MethodInfo postfixMethod)
        {
            try
            {
                var original = FindMethod(targetType, methodName, paramTypeNames);
                if (original == null)
                {
                    Log($"警告：未找到方法 {targetType.Name}.{methodName}({string.Join(", ", paramTypeNames)})");
                    return;
                }

                var prefix = prefixMethod != null ? new HarmonyMethod(prefixMethod) : null;
                var postfix = postfixMethod != null ? new HarmonyMethod(postfixMethod) : null;

                _harmony.Patch(original, prefix: prefix, postfix: postfix);
                Log($"已补丁: {methodName}({string.Join(", ", paramTypeNames)})");
            }
            catch (Exception ex)
            {
                Log($"补丁失败 {methodName}({string.Join(", ", paramTypeNames)}): {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 从调用栈中识别来源插件
        /// </summary>
        private static string IdentifySourcePlugin()
        {
            // 先检查 AsyncLocal（来自 SayRnd 的跨 Task.Run 传播）
            var asyncSource = _asyncSourcePlugin.Value;
            if (!string.IsNullOrEmpty(asyncSource))
            {
                return asyncSource;
            }

            var stackTrace = new StackTrace(false);
            var frames = stackTrace.GetFrames();
            if (frames == null) return null;

            for (int i = 0; i < frames.Length; i++)
            {
                var method = frames[i].GetMethod();
                if (method == null) continue;

                var declaringType = method.DeclaringType;
                if (declaringType == null) continue;

                string asmName;
                try
                {
                    asmName = declaringType.Assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }

                if (string.Equals(asmName, _selfAssemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool skip = false;
                for (int j = 0; j < _skipAssemblyPrefixes.Length; j++)
                {
                    if (asmName.StartsWith(_skipAssemblyPrefixes[j], StringComparison.OrdinalIgnoreCase))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

                if (asmName.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) ||
                    asmName.StartsWith("HarmonyLib", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_pluginAssemblyMap.TryGetValue(asmName, out var pluginName))
                {
                    return pluginName;
                }
            }

            return null;
        }

        /// <summary>
        /// 检查来源插件是否被屏蔽，如是则标记
        /// </summary>
        private static void CheckAndMarkBlocked(object sayInfo)
        {
            if (sayInfo == null || _blockedAssemblyMap.IsEmpty) return;

            var sourcePlugin = IdentifySourcePlugin();
            if (sourcePlugin == null)
            {
                Log($"Prefix 触发，但未识别到来源插件");
                return;
            }

            Log($"Prefix 识别到来源插件: {sourcePlugin}");

            if (_blockedAssemblyMap.Values.Any(name =>
                string.Equals(name, sourcePlugin, StringComparison.OrdinalIgnoreCase)))
            {
                _blockedSayInfos[sayInfo] = sourcePlugin;
                Log($"已标记屏蔽 SayInfo (来源: {sourcePlugin})");
            }
        }

        #endregion

        #region Harmony Patch Methods (使用 object __0 避免类型不匹配)

        /// <summary>
        /// Say 方法通用 Prefix（两个 Say 重载共用）
        /// Harmony 通过 __0 注入第一个参数（object 类型，兼容任意程序集）
        /// </summary>
        private static void SayPrefix(object __0)
        {
            try
            {
                Log($">>> SayPrefix 触发! 参数类型={__0?.GetType().Name}");
                CheckAndMarkBlocked(__0);
            }
            catch (Exception ex)
            {
                Log($"SayPrefix 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// SayRnd 方法通用 Prefix
        /// </summary>
        private static void SayRndPrefix()
        {
            try
            {
                if (_blockedAssemblyMap.IsEmpty) return;

                var sourcePlugin = IdentifySourcePlugin();
                if (!string.IsNullOrEmpty(sourcePlugin))
                {
                    _asyncSourcePlugin.Value = sourcePlugin;
                    Log($"SayRnd Prefix 设置 AsyncLocal: {sourcePlugin}");
                }
            }
            catch (Exception ex)
            {
                Log($"SayRndPrefix 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// SayRnd 方法通用 Postfix
        /// </summary>
        private static void SayRndPostfix()
        {
            _asyncSourcePlugin.Value = null;
        }

        #endregion

        private static void Log(string message)
        {
            _logAction?.Invoke($"[SaySourceInterceptor] {message}");
        }
    }
}
