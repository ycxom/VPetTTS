using Panuon.WPF.UI;

namespace Vpet.Plugin.CustomTTS
{
    /// <summary>
    /// winBlockedPlugins.xaml 的交互逻辑
    /// 独立窗口用于管理插件 TTS 屏蔽列表
    /// 支持云端推荐屏蔽 + 本地手动屏蔽
    /// </summary>
    public partial class winBlockedPlugins : Window
    {
        private readonly VPetTTS vts;

        /// <summary>
        /// 云端推荐屏蔽的 mod ID 集合（用于区分勾选来源）
        /// </summary>
        private HashSet<string> _cloudBannedModIds;

        public winBlockedPlugins(VPetTTS vts)
        {
            InitializeComponent();
            Resources = Application.Current.Resources;
            this.vts = vts;

            LoadPluginListAsync();
        }

        /// <summary>
        /// 异步加载插件列表
        /// </summary>
        private async void LoadPluginListAsync()
        {
            try
            {
                // 优先使用启动时缓存的扫描结果
                var plugins = vts.CachedPluginScanResults;
                if (plugins == null || plugins.Count == 0)
                {
                    plugins = await Task.Run(() =>
                        PluginAssemblyScanner.ScanPlugins(vts.MW));
                }

                Dispatcher.Invoke(() => PopulatePluginList(plugins));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingText.Text = "扫描插件失败".Translate() + $": {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 填充插件复选框列表
        /// </summary>
        private void PopulatePluginList(List<PluginAssemblyScanner.PluginInfo> plugins)
        {
            PluginListPanel.Children.Clear();

            if (plugins == null || plugins.Count == 0)
            {
                PluginListPanel.Children.Add(new TextBlock
                {
                    Text = "未检测到其他插件".Translate(),
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(5, 8, 5, 8)
                });
                return;
            }

            // 本地手动屏蔽集合
            var localBlockedSet = new HashSet<string>(
                vts.Set.BlockedPlugins ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            // 云端推荐屏蔽的 mod ID 集合
            _cloudBannedModIds = new HashSet<string>(
                vts.CloudBan?.CloudBannedModIds ?? new List<string>());

            // 用户明确允许的云端 mod ID 集合
            var cloudAllowedSet = new HashSet<string>(
                vts.Set.CloudBanAllowedMods ?? new List<string>());

            // 排序: 云端推荐 > 可调用TTS > 其他
            var sorted = plugins
                .OrderByDescending(p => p.IsCloudBanned)
                .ThenByDescending(p => p.CanCallSay)
                .ThenBy(p => p.Name);

            foreach (var plugin in sorted)
            {
                bool isCloudBanned = plugin.IsCloudBanned;
                bool isUserAllowed = isCloudBanned
                    && !string.IsNullOrEmpty(plugin.ModId)
                    && cloudAllowedSet.Contains(plugin.ModId);

                // 勾选状态:
                // - 云端屏蔽且用户未放行 → 勾选
                // - 云端屏蔽但用户放行 → 不勾选
                // - 非云端但在本地屏蔽列表 → 勾选
                bool isChecked;
                if (isCloudBanned)
                    isChecked = !isUserAllowed;
                else
                    isChecked = localBlockedSet.Contains(plugin.Name);

                var cb = new CheckBox
                {
                    Tag = plugin,  // 存储完整 PluginInfo 以便保存时区分
                    IsChecked = isChecked,
                    Margin = new Thickness(5, 4, 5, 4),
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };

                sp.Children.Add(new TextBlock
                {
                    Text = plugin.Name,
                    VerticalAlignment = VerticalAlignment.Center
                });

                // 云端推荐屏蔽标签（蓝色）
                if (isCloudBanned)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "  " + "云端推荐屏蔽".Translate(),
                        Foreground = System.Windows.Media.Brushes.DodgerBlue,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                if (plugin.CanCallSay)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = "  " + "可调用TTS".Translate(),
                        Foreground = System.Windows.Media.Brushes.OrangeRed,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                cb.Content = sp;
                PluginListPanel.Children.Add(cb);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newLocalBlocked = new List<string>();
                var newCloudAllowed = new List<string>();

                foreach (var child in PluginListPanel.Children)
                {
                    if (child is not CheckBox cb || cb.Tag is not PluginAssemblyScanner.PluginInfo plugin)
                        continue;

                    bool isChecked = cb.IsChecked == true;
                    bool isCloudBanned = plugin.IsCloudBanned;

                    if (isCloudBanned)
                    {
                        // 云端推荐屏蔽的插件:
                        // 未勾选 = 用户放行 → 加入 CloudBanAllowedMods
                        if (!isChecked && !string.IsNullOrEmpty(plugin.ModId))
                        {
                            newCloudAllowed.Add(plugin.ModId);
                        }
                        // 勾选 = 保持云端屏蔽（不需要加入本地列表）
                    }
                    else
                    {
                        // 非云端推荐的插件:
                        // 勾选 → 加入本地手动屏蔽列表
                        if (isChecked)
                        {
                            newLocalBlocked.Add(plugin.Name);
                        }
                    }
                }

                // 更新设置
                vts.Set.BlockedPlugins = newLocalBlocked;
                vts.Set.CloudBanAllowedMods = newCloudAllowed;

                // 持久化
                vts.MW.Set["VPetTTS"] = LPSConvert.SerializeObject(vts.Set, "VPetTTS");

                // 运行时更新拦截器（内部会合并云端 + 本地）
                vts.UpdateBlockedPlugins(newLocalBlocked);

                Close();
            }
            catch (Exception ex)
            {
                MessageBoxX.Show($"保存失败: {ex.Message}".Translate(), "错误".Translate());
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
