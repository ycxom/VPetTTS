using System.Reflection;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 扫描已加载插件的程序集引用，识别哪些插件引用了 VPet-Simulator.Core
    /// （即具备调用 Say 的能力），辅助用户构建屏蔽列表。
    /// </summary>
    public static class PluginAssemblyScanner
    {
        /// <summary>
        /// 插件信息
        /// </summary>
        public class PluginInfo
        {
            /// <summary>
            /// 插件名称（PluginName）
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// 插件程序集名称
            /// </summary>
            public string AssemblyName { get; set; }

            /// <summary>
            /// 是否引用了 VPet-Simulator.Core（具备调用 Say 的能力）
            /// </summary>
            public bool CanCallSay { get; set; }

            /// <summary>
            /// Steam Workshop mod ID（来自 IModInfo.ItemID）
            /// </summary>
            public string ModId { get; set; }

            /// <summary>
            /// 是否被云端推荐屏蔽
            /// </summary>
            public bool IsCloudBanned { get; set; }
        }

        /// <summary>
        /// 扫描所有已加载插件，返回插件信息列表
        /// </summary>
        /// <param name="mw">主窗体引用</param>
        /// <param name="excludeSelf">排除 VPetTTS 自身的程序集名称</param>
        /// <returns>插件信息列表</returns>
        public static List<PluginInfo> ScanPlugins(IMainWindow mw, string excludeSelf = null)
        {
            var result = new List<PluginInfo>();
            if (mw?.Plugins == null) return result;

            var selfAsmName = excludeSelf ?? typeof(PluginAssemblyScanner).Assembly.GetName().Name;

            // 构建插件名称 → mod ID 映射（通过 MW.ModInfo）
            var pluginNameToModId = BuildPluginNameToModIdMap(mw);

            foreach (var plugin in mw.Plugins)
            {
                try
                {
                    var asm = plugin.GetType().Assembly;
                    var asmName = asm.GetName().Name;

                    // 排除自身
                    if (string.Equals(asmName, selfAsmName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var info = new PluginInfo
                    {
                        Name = plugin.PluginName,
                        AssemblyName = asmName,
                        CanCallSay = ReferencesVPetCore(asm)
                    };

                    // 填充 ModId
                    if (pluginNameToModId.TryGetValue(plugin.PluginName, out var modId))
                    {
                        info.ModId = modId;
                    }

                    result.Add(info);
                }
                catch
                {
                    // 跳过无法扫描的插件
                }
            }

            return result;
        }

        /// <summary>
        /// 通过 MW.ModInfo 构建插件名称 → Steam Workshop mod ID 的映射
        /// IModInfo.Name 对应 MainPlugin.PluginName，IModInfo.ItemID 为 Steam Workshop ID
        /// </summary>
        private static Dictionary<string, string> BuildPluginNameToModIdMap(IMainWindow mw)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (mw.ModInfo == null) return map;

                foreach (var mod in mw.ModInfo)
                {
                    try
                    {
                        if (mod.ItemID > 0 && !string.IsNullOrEmpty(mod.Name))
                        {
                            map[mod.Name] = mod.ItemID.ToString();
                        }
                    }
                    catch
                    {
                        // 跳过无法读取的 mod
                    }
                }
            }
            catch
            {
                // ModInfo 不可用
            }

            return map;
        }

        /// <summary>
        /// 检查程序集是否引用了 VPet-Simulator.Core
        /// </summary>
        private static bool ReferencesVPetCore(Assembly assembly)
        {
            try
            {
                var refs = assembly.GetReferencedAssemblies();
                return refs.Any(r =>
                    r.Name.StartsWith("VPet-Simulator.Core", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
