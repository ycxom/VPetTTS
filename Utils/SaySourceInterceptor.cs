using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 通过 Harmony 补丁拦截 Main.Say/SayRnd 调用，在 Task.Run 之前捕获调用栈，
    /// 识别消息来源插件并标记被屏蔽的 SayInfo。
    ///
    /// 来源识别按"被屏蔽 MOD 所在目录里的程序集"进行匹配（而不是按插件显示名做字符串匹配），
    /// 这样可以同时兼容：
    ///   1. 多程序集 MOD（主插件 DLL 与实际调用 Say 的 DLL 不是同一个）
    ///   2. 云端按 Steam ItemID 屏蔽（IModInfo.Name 与 PluginName 不一致也能命中）
    ///   3. 插件显示名与 MOD 名不一致的情况
    /// </summary>
    public static class SaySourceInterceptor
    {
        private static Harmony _harmony;
        private static bool _initialized;

        /// <summary>
        /// 主窗体引用（用于在更新屏蔽列表时实时解析插件 → 程序集 → MOD 目录）
        /// </summary>
        private static IMainWindow _mw;

        /// <summary>
        /// 被屏蔽的 SayInfo → 来源显示名称
        /// 使用 object key 以避免跨程序集类型不匹配
        /// </summary>
        private static readonly ConcurrentDictionary<object, string> _blockedSayInfos = new();

        /// <summary>
        /// AsyncLocal 用于跨 Task.Run 传递来源显示名称（SayRnd 内部会在 Task.Run 中再次调用 Say，
        /// 此时原始调用栈已丢失，需要靠 AsyncLocal 传播）
        /// </summary>
        private static readonly AsyncLocal<string> _asyncSourcePlugin = new();

        /// <summary>
        /// 被屏蔽 MOD 的程序集简单名 → 屏蔽来源显示名（用于命中后记录日志）。
        /// 这是真正用于拦截判定的数据，由 <see cref="UpdateBlockedPlugins"/> 直接从实时插件/MOD 信息构建，
        /// 不依赖任何预先缓存的名称映射，避免"映射为空/名称对不上"导致整体失效。
        /// volatile 引用整体替换，读侧无需加锁。
        /// </summary>
        private static volatile Dictionary<string, string> _blockedAssemblies = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 所有已加载插件：程序集名称 → 插件名称（仅供设置界面展示用）
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> _pluginAssemblyMap = new();

        /// <summary>
        /// VPetTTS 自身程序集名称
        /// </summary>
        private static string _selfAssemblyName;

        /// <summary>
        /// 已成功应用的补丁数量（诊断用）
        /// </summary>
        public static int PatchedMethodCount { get; private set; }

        /// <summary>
        /// 日志回调
        /// </summary>
        private static Action<string> _logAction;

        /// <summary>
        /// 初始化拦截器，应用 Harmony 补丁
        /// </summary>
        public static void Initialize(IMainWindow mw, List<string> blockedPlugins, Action<string> logAction = null)
        {
            _logAction = logAction;
            _mw = mw;
            _selfAssemblyName = typeof(SaySourceInterceptor).Assembly.GetName().Name;

            // 即使重复调用（多开/重新加载），也刷新插件映射与屏蔽列表，避免静态状态过期
            BuildPluginAssemblyMap(mw);
            UpdateBlockedPlugins(blockedPlugins);

            if (!_initialized)
            {
                ApplyPatches(mw);
                _initialized = true;
            }

            Log($"SaySourceInterceptor 初始化完成，已打补丁方法: {PatchedMethodCount}，屏蔽程序集数: {_blockedAssemblies.Count}");
        }

        /// <summary>
        /// 更新屏蔽列表（运行时可调用）。
        /// 入参为屏蔽项的"名称"集合：可能是 PluginName、IModInfo.Name，或 Steam ItemID（字符串）。
        /// 本方法会把这些名称解析为"被屏蔽 MOD 目录下的所有已加载程序集"。
        /// </summary>
        public static void UpdateBlockedPlugins(List<string> blockedPlugins)
        {
            var newBlocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var blockedSet = new HashSet<string>(blockedPlugins ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (blockedSet.Count == 0 || _mw?.Plugins == null)
            {
                _blockedAssemblies = newBlocked;
                Log("屏蔽列表已更新: (空)");
                return;
            }

            foreach (var plugin in _mw.Plugins)
            {
                string pluginName;
                Assembly pluginAsm;
                try
                {
                    pluginName = plugin.PluginName;
                    pluginAsm = plugin.GetType().Assembly;
                }
                catch
                {
                    continue;
                }

                // 不能屏蔽自己
                if (string.Equals(pluginAsm.GetName().Name, _selfAssemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var mod = FindModForAssembly(pluginAsm);

                // 命中条件：屏蔽集合里包含 该插件的 PluginName / 所属 MOD 名 / 所属 Steam ItemID
                bool isBlocked = blockedSet.Contains(pluginName);
                string displayName = pluginName;
                if (!isBlocked && mod != null)
                {
                    if (!string.IsNullOrEmpty(mod.Name) && blockedSet.Contains(mod.Name))
                    {
                        isBlocked = true;
                        displayName = mod.Name;
                    }
                    else if (mod.ItemID > 0 && blockedSet.Contains(mod.ItemID.ToString()))
                    {
                        isBlocked = true;
                        displayName = string.IsNullOrEmpty(mod.Name) ? pluginName : mod.Name;
                    }
                }

                if (!isBlocked) continue;

                // 加入该插件自身程序集
                newBlocked[pluginAsm.GetName().Name] = displayName;
                // 多程序集 MOD：把该 MOD 目录下所有已加载程序集都算作屏蔽来源
                AddAssembliesUnderMod(mod, displayName, newBlocked);
            }

            _blockedAssemblies = newBlocked;
            Log($"屏蔽列表已更新: 名称[{string.Join(", ", blockedSet)}] -> 程序集[{string.Join(", ", newBlocked.Keys)}]");
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
        /// 当前生效的被屏蔽程序集（诊断用）：程序集名 → 来源显示名
        /// </summary>
        public static IReadOnlyDictionary<string, string> BlockedAssemblies => _blockedAssemblies;

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
            PatchedMethodCount = 0;
            _blockedSayInfos.Clear();
            _blockedAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        /// 根据程序集所在文件路径，定位它属于哪个 MOD（IModInfo.Path 包含该 DLL）
        /// </summary>
        private static IModInfo FindModForAssembly(Assembly asm)
        {
            try
            {
                var loc = SafeGetLocation(asm);
                if (string.IsNullOrEmpty(loc) || _mw?.ModInfo == null) return null;

                foreach (var mod in _mw.ModInfo)
                {
                    try
                    {
                        if (IsUnderDirectory(loc, mod?.Path?.FullName))
                            return mod;
                    }
                    catch { /* 跳过无法读取的 mod */ }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        /// <summary>
        /// 将指定 MOD 目录下所有已加载程序集加入屏蔽集合（兼容多 DLL 的 MOD）
        /// </summary>
        private static void AddAssembliesUnderMod(IModInfo mod, string displayName, Dictionary<string, string> target)
        {
            var root = mod?.Path?.FullName;
            if (string.IsNullOrEmpty(root)) return;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var loc = SafeGetLocation(asm);
                    if (!IsUnderDirectory(loc, root)) continue;

                    var name = asm.GetName().Name;
                    if (string.Equals(name, _selfAssemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                    target[name] = displayName;
                }
                catch { /* 动态程序集没有 Location，跳过 */ }
            }
        }

        /// <summary>
        /// 判断文件路径 <paramref name="filePath"/> 是否位于目录 <paramref name="dir"/> 之内
        /// （按目录边界匹配，避免 "...\VPetLLM" 误命中 "...\VPetLLM2"）
        /// </summary>
        private static bool IsUnderDirectory(string filePath, string dir)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(dir)) return false;
            var root = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeGetLocation(Assembly asm)
        {
            try
            {
                if (asm.IsDynamic) return null;
                return asm.Location;
            }
            catch
            {
                return null;
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
            PatchedMethodCount = 0;

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

            if (PatchedMethodCount == 0)
                Log("严重警告：没有任何 Say/SayRnd 方法被成功打补丁，来源屏蔽将完全失效！");
            else
                Log($"Harmony 补丁应用完毕，成功 {PatchedMethodCount} 个");
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
                PatchedMethodCount++;
                Log($"已补丁: {methodName}({string.Join(", ", paramTypeNames)})");
            }
            catch (Exception ex)
            {
                Log($"补丁失败 {methodName}({string.Join(", ", paramTypeNames)}): {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 从调用栈中识别来源是否为被屏蔽的 MOD。
        /// 返回非空的屏蔽来源显示名表示命中。
        /// </summary>
        private static string IdentifyBlockedSource()
        {
            // 先检查 AsyncLocal（来自 SayRnd 的跨 Task.Run 传播）
            var asyncSource = _asyncSourcePlugin.Value;
            if (!string.IsNullOrEmpty(asyncSource))
            {
                return asyncSource;
            }

            var blocked = _blockedAssemblies;
            if (blocked.Count == 0) return null;

            var stackTrace = new StackTrace(false);
            var frames = stackTrace.GetFrames();
            if (frames == null) return null;

            for (int i = 0; i < frames.Length; i++)
            {
                var method = frames[i].GetMethod();
                var declaringType = method?.DeclaringType;
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

                // 只要调用链中出现被屏蔽 MOD 的任意程序集，即判定为该来源
                if (blocked.TryGetValue(asmName, out var displayName))
                {
                    return displayName;
                }
            }

            return null;
        }

        /// <summary>
        /// 检查来源是否被屏蔽，如是则标记 SayInfo
        /// </summary>
        private static void CheckAndMarkBlocked(object sayInfo)
        {
            if (sayInfo == null || _blockedAssemblies.Count == 0) return;

            var sourcePlugin = IdentifyBlockedSource();
            if (sourcePlugin == null)
            {
                return;
            }

            _blockedSayInfos[sayInfo] = sourcePlugin;
            Log($"已标记屏蔽 SayInfo (来源: {sourcePlugin})");
        }

        #endregion

        #region Harmony Patch Methods (使用 object __0 避免类型不匹配)

        /// <summary>
        /// Say 方法通用 Prefix（两个 Say 重载共用）
        /// Harmony 通过 __0 注入第一个参数（object 类型，兼容任意程序集）
        /// </summary>
        private static void SayPrefix(object __0)
        {
            // 热路径：没有任何屏蔽程序集时立即返回，避免每次说话都做堆栈分析
            // （该补丁挂在核心 Say 上，会被所有插件的每一次说话触发）
            if (_blockedAssemblies.Count == 0) return;

            try
            {
                CheckAndMarkBlocked(__0);
            }
            catch (Exception ex)
            {
                Log($"SayPrefix 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// SayRnd 方法通用 Prefix
        /// SayRnd 内部会在 Task.Run 中再次调用 Say，原始调用栈届时已丢失，
        /// 因此在此处提前识别来源并写入 AsyncLocal，随 ExecutionContext 传播到内部 Say。
        /// </summary>
        private static void SayRndPrefix()
        {
            try
            {
                if (_blockedAssemblies.Count == 0) return;

                var sourcePlugin = IdentifyBlockedSource();
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
