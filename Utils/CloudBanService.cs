using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 云端预屏蔽列表服务
    /// 从云端 API 获取推荐屏蔽的 mod 列表（按 Steam Workshop mod ID），
    /// 与本地手动屏蔽列表合并，计算最终有效屏蔽插件名称列表。
    /// </summary>
    public class CloudBanService
    {
        private const string CloudBanUrl = "https://vpetllm.ycxom.com/api/TTS_Ban_Mod_Text.json";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private readonly Action<string> _logAction;

        /// <summary>
        /// 云端获取的原始 mod ID 列表（缓存）
        /// </summary>
        public List<string> CloudBannedModIds { get; private set; } = new List<string>();

        /// <summary>
        /// 云端是否成功获取过数据
        /// </summary>
        public bool IsFetched { get; private set; }

        public CloudBanService(Action<string> logAction = null)
        {
            _logAction = logAction;
        }

        /// <summary>
        /// 异步获取云端屏蔽列表
        /// </summary>
        /// <returns>云端推荐屏蔽的 mod ID 列表</returns>
        public async Task<List<string>> FetchCloudBanListAsync()
        {
            try
            {
                Log("开始获取云端屏蔽列表...");
                var response = await _httpClient.GetStringAsync(CloudBanUrl).ConfigureAwait(false);

                var json = JObject.Parse(response);
                var modIds = json["mod_id"]?.ToObject<List<string>>() ?? new List<string>();

                CloudBannedModIds = modIds;
                IsFetched = true;

                Log($"云端屏蔽列表获取成功，共 {modIds.Count} 个 mod: {string.Join(", ", modIds)}");
                return modIds;
            }
            catch (Exception ex)
            {
                Log($"获取云端屏蔽列表失败: {ex.Message}");
                CloudBannedModIds = new List<string>();
                IsFetched = false;
                return new List<string>();
            }
        }

        /// <summary>
        /// 计算有效屏蔽插件名称列表
        /// (cloudBannedIds - userAllowedIds) → 映射为插件名 + localBlockedNames
        /// </summary>
        /// <param name="cloudBannedModIds">云端推荐屏蔽的 mod ID 列表</param>
        /// <param name="userAllowedModIds">用户明确允许的 mod ID 列表（从 Setting.CloudBanAllowedMods）</param>
        /// <param name="localBlockedPlugins">本地手动屏蔽的插件名称列表（从 Setting.BlockedPlugins）</param>
        /// <param name="modInfos">所有已加载 mod 的信息（从 MW.ModInfo）</param>
        /// <returns>最终有效屏蔽的插件名称列表</returns>
        public static List<string> ComputeEffectiveBlockedPlugins(
            List<string> cloudBannedModIds,
            List<string> userAllowedModIds,
            List<string> localBlockedPlugins,
            IEnumerable<IModInfo> modInfos)
        {
            var effectiveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 云端屏蔽列表减去用户允许列表 → 有效云端屏蔽 mod IDs
            var allowedSet = new HashSet<string>(userAllowedModIds ?? new List<string>());
            var effectiveCloudIds = new HashSet<string>(
                (cloudBannedModIds ?? new List<string>()).Where(id => !allowedSet.Contains(id)));

            // 2. 将有效云端 mod IDs 映射为插件名称
            if (modInfos != null && effectiveCloudIds.Count > 0)
            {
                foreach (var mod in modInfos)
                {
                    try
                    {
                        if (mod.ItemID > 0 && effectiveCloudIds.Contains(mod.ItemID.ToString()))
                        {
                            if (!string.IsNullOrEmpty(mod.Name))
                            {
                                effectiveSet.Add(mod.Name);
                            }
                        }
                    }
                    catch
                    {
                        // 跳过无法读取的 mod
                    }
                }
            }

            // 3. 合并本地手动屏蔽列表
            if (localBlockedPlugins != null)
            {
                foreach (var name in localBlockedPlugins)
                {
                    effectiveSet.Add(name);
                }
            }

            return effectiveSet.ToList();
        }

        /// <summary>
        /// 判断指定 mod ID 是否在云端屏蔽列表中
        /// </summary>
        public bool IsCloudBanned(string modId)
        {
            if (string.IsNullOrEmpty(modId) || CloudBannedModIds == null)
                return false;
            return CloudBannedModIds.Contains(modId);
        }

        private void Log(string message)
        {
            _logAction?.Invoke($"[CloudBanService] {message}");
        }
    }
}
