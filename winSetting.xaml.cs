using Panuon.WPF.UI;

namespace Vpet.Plugin.CustomTTS
{
    /// <summary>
    /// winSetting.xaml 的交互逻辑
    /// </summary>
    public partial class winSetting : Window
    {
        VPetTTS vts;

        public winSetting(VPetTTS vts)
        {
            InitializeComponent();
            Resources = Application.Current.Resources;
            this.vts = vts;

            LoadSettings();
            SetupEventHandlers();

            vts.StateChanged += OnTTSStateChanged;
            Closed += (s, e) => vts.StateChanged -= OnTTSStateChanged;
        }

        private void LoadSettings()
        {
            // 基本设置
            SwitchOn.IsChecked = vts.Set.Enable;
            VolumeSilder.Value = vts.Set.Volume;
            SpeedSilder.Value = vts.Set.Speed;
            EnableCache.IsChecked = vts.Set.EnableCache;
            PreferBuiltInPlayer.IsChecked = vts.Set.PreferVPetBuiltInPlayer;
            EnableTextFilter.IsChecked = vts.Set.TextFilter.Enable;

            // 请求超时
            TimeoutSlider.Value = vts.Set.RequestTimeout;

            // 提供商选择
            foreach (ComboBoxItem item in CombProvider.Items)
            {
                if (item.Tag?.ToString() == vts.Set.Provider)
                {
                    CombProvider.SelectedItem = item;
                    break;
                }
            }
            if (CombProvider.SelectedItem is null && CombProvider.Items.Count > 0)
                CombProvider.SelectedIndex = 0;

            // 代理设置
            EnableProxy.IsChecked = vts.Set.Proxy.IsEnabled;
            FollowSystemProxy.IsChecked = vts.Set.Proxy.FollowSystemProxy;
            ProxyAddress.Text = vts.Set.Proxy.Address;

            foreach (ComboBoxItem item in ProxyProtocol.Items)
            {
                if (item.Tag?.ToString() == vts.Set.Proxy.Protocol)
                {
                    ProxyProtocol.SelectedItem = item;
                    break;
                }
            }
            if (ProxyProtocol.SelectedItem is null && ProxyProtocol.Items.Count > 0)
                ProxyProtocol.SelectedIndex = 0;

            UpdateProviderConfig();

            UpdateSoftDisableStatus();

            UpdateServiceUnavailableStatus();
        }

        /// <summary>
        /// 更新软禁用状态显示
        /// </summary>
        private void UpdateSoftDisableStatus()
        {
            try
            {
                vts.RefreshSoftDisableStatus();

                var warningText = this.FindName("SoftDisableWarning") as TextBlock;

                if (vts.IsSoftDisabled)
                {
                    var pluginNames = vts.DetectedOtherTTSPluginNames.Translate();
                    var template = "⚠ 检测到 {0} 插件已启用，TTS 将在运行时自动跳过".Translate();
                    var message = string.Format(template, pluginNames);

                    if (warningText is not null)
                    {
                        warningText.Text = message;
                        warningText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        TTSLogger.Log($"[VPetTTS] {message}");
                    }
                }
                else
                {
                    if (warningText is not null)
                    {
                        warningText.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"[VPetTTS] 更新软禁用状态显示时发生错误: {ex.Message}");
            }
        }

        private void UpdateServiceUnavailableStatus()
        {
            try
            {
                var warningText = this.FindName("ServiceUnavailableWarning") as TextBlock;
                if (warningText is null) return;

                if (vts.TTSState?.HasError == true)
                {
                    var isFreeProvider = vts.Set.Provider == "Free";
                    if (isFreeProvider)
                    {
                        warningText.Text = "服务不可用，请检查网络是否正常，可能在维护中".Translate();
                    }
                    else
                    {
                        warningText.Text = "服务不可用，请检查网络是否正常".Translate();
                    }
                    warningText.Visibility = Visibility.Visible;
                }
                else
                {
                    warningText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"[VPetTTS] 更新服务不可用状态显示时发生错误: {ex.Message}");
            }
        }

        private void OnTTSStateChanged(object? sender, TTSStateChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == "HasError")
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateServiceUnavailableStatus();
                    }));
                }
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"[VPetTTS] OnTTSStateChanged 处理失败: {ex.Message}");
            }
        }

        private void SetupEventHandlers()
        {
            CombProvider.SelectionChanged += (s, e) =>
            {
                var warningText = this.FindName("ServiceUnavailableWarning") as TextBlock;
                if (warningText is not null)
                    warningText.Visibility = Visibility.Collapsed;
                UpdateProviderConfig();
            };
        }

        private void UpdateProviderConfig()
        {
            ProviderConfigPanel.Children.Clear();

            if (CombProvider.SelectedItem is ComboBoxItem selectedItem)
            {
                var provider = selectedItem.Tag?.ToString();

                switch (provider)
                {
                    case "Free":
                        AddFreeConfig();
                        break;
                    case "OpenAI":
                        AddOpenAIConfig();
                        break;
                    case "GPT-SoVITS":
                        AddGPTSoVITSConfig();
                        break;
                    case "URL":
                        AddURLConfig();
                        break;
                    case "DIY":
                        AddDIYConfig();
                        break;
                }
            }
        }

        private void AddFreeConfig()
        {
            var infoText = new TextBlock
            {
                Text = "Free TTS 使用免费在线服务，无需配置".Translate(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ProviderConfigPanel.Children.Add(infoText);

            AddConfigLabel("语言设置".Translate());
            var langCombo = new ComboBox
            {
                Name = "Free_TextLanguage",
                Margin = new Thickness(0, 0, 0, 8)
            };
            langCombo.SetResourceReference(StyleProperty, "StandardComboBoxStyle");

            foreach (var lang in FreeTTSSetting.SupportedLanguages)
            {
                var item = new ComboBoxItem { Content = lang.Value.Translate(), Tag = lang.Key };
                langCombo.Items.Add(item);
                if (lang.Key == vts.Set.Free.TextLanguage)
                    langCombo.SelectedItem = item;
            }
            if (langCombo.SelectedItem is null && langCombo.Items.Count > 0)
                langCombo.SelectedIndex = 0;

            ProviderConfigPanel.Children.Add(langCombo);
        }

        private ComboBox CreateLanguageCombo(string name, string selectedLanguage, string fallbackLanguage)
        {
            var combo = new ComboBox
            {
                Name = name,
                Margin = new Thickness(0, 0, 0, 8)
            };
            combo.SetResourceReference(StyleProperty, "StandardComboBoxStyle");

            var normalizedLanguage = TTSLanguage.Normalize(selectedLanguage, fallbackLanguage);
            foreach (var language in TTSLanguage.SupportedLanguages)
            {
                var item = new ComboBoxItem
                {
                    Content = language.Value.Translate(),
                    Tag = language.Key
                };
                combo.Items.Add(item);

                if (language.Key == normalizedLanguage)
                    combo.SelectedItem = item;
            }

            if (combo.SelectedItem is null && combo.Items.Count > 0)
                combo.SelectedIndex = 0;

            return combo;
        }

        private void AddOpenAIConfig()
        {
            AddConfigLabel("API Key".Translate());
            AddTextBox("OpenAI_ApiKey", vts.Set.OpenAI.ApiKey);

            AddConfigLabel("Base URL".Translate());
            AddTextBox("OpenAI_BaseUrl", vts.Set.OpenAI.BaseUrl);

            AddConfigLabel("Model".Translate());
            AddTextBox("OpenAI_Model", vts.Set.OpenAI.Model);

            AddConfigLabel("Voice".Translate());
            AddTextBox("OpenAI_Voice", vts.Set.OpenAI.Voice);
        }

        private void AddGPTSoVITSConfig()
        {
            AddConfigLabel("Base URL".Translate());
            AddTextBox("GPTSoVITS_BaseUrl", vts.Set.GPTSoVITS.BaseUrl);

            AddConfigLabel("API 模式".Translate());
            var apiModeCombo = new ComboBox { Name = "GPTSoVITS_ApiMode", Margin = new Thickness(0, 0, 0, 8) };
            apiModeCombo.SetResourceReference(StyleProperty, "StandardComboBoxStyle");
            apiModeCombo.Items.Add(new ComboBoxItem { Content = "WebUI", Tag = "WebUI" });
            apiModeCombo.Items.Add(new ComboBoxItem { Content = "API v2", Tag = "ApiV2" });
            foreach (ComboBoxItem item in apiModeCombo.Items)
            {
                if (item.Tag?.ToString() == vts.Set.GPTSoVITS.ApiMode)
                {
                    apiModeCombo.SelectedItem = item;
                    break;
                }
            }
            ProviderConfigPanel.Children.Add(apiModeCombo);

            AddConfigLabel("文本语言".Translate());
            ProviderConfigPanel.Children.Add(CreateLanguageCombo(
                "GPTSoVITS_TextLanguage",
                vts.Set.GPTSoVITS.TextLanguage,
                "auto"));

            AddConfigLabel("参考音频路径".Translate());
            AddTextBox("GPTSoVITS_ReferWavPath", vts.Set.GPTSoVITS.ReferWavPath);

            AddConfigLabel("提示文本".Translate());
            AddTextBox("GPTSoVITS_PromptText", vts.Set.GPTSoVITS.PromptText);

            AddConfigLabel("提示语言".Translate());
            ProviderConfigPanel.Children.Add(CreateLanguageCombo(
                "GPTSoVITS_PromptLanguage",
                vts.Set.GPTSoVITS.PromptLanguage,
                "zh"));
        }

        private void AddURLConfig()
        {
            AddConfigLabel("Base URL".Translate());
            AddTextBox("URL_BaseUrl", vts.Set.URL.BaseUrl);

            AddConfigLabel("Voice ID".Translate());
            AddTextBox("URL_Voice", vts.Set.URL.Voice);

            AddConfigLabel("HTTP 方法".Translate());
            var methodCombo = new ComboBox { Name = "URL_Method", Margin = new Thickness(0, 0, 0, 8) };
            methodCombo.SetResourceReference(StyleProperty, "StandardComboBoxStyle");
            methodCombo.Items.Add(new ComboBoxItem { Content = "GET", Tag = "GET" });
            methodCombo.Items.Add(new ComboBoxItem { Content = "POST", Tag = "POST" });
            foreach (ComboBoxItem item in methodCombo.Items)
            {
                if (item.Tag?.ToString() == vts.Set.URL.Method)
                {
                    methodCombo.SelectedItem = item;
                    break;
                }
            }
            ProviderConfigPanel.Children.Add(methodCombo);
        }

        private void AddDIYConfig()
        {
            AddConfigLabel("Base URL".Translate());
            AddTextBox("DIY_BaseUrl", vts.Set.DIY.BaseUrl);

            AddConfigLabel("HTTP 方法".Translate());
            var methodCombo = new ComboBox { Name = "DIY_Method", Margin = new Thickness(0, 0, 0, 8) };
            methodCombo.SetResourceReference(StyleProperty, "StandardComboBoxStyle");
            methodCombo.Items.Add(new ComboBoxItem { Content = "GET", Tag = "GET" });
            methodCombo.Items.Add(new ComboBoxItem { Content = "POST", Tag = "POST" });
            foreach (ComboBoxItem item in methodCombo.Items)
            {
                if (item.Tag?.ToString() == vts.Set.DIY.Method)
                {
                    methodCombo.SelectedItem = item;
                    break;
                }
            }
            ProviderConfigPanel.Children.Add(methodCombo);

            AddConfigLabel("Content-Type".Translate());
            AddTextBox("DIY_ContentType", vts.Set.DIY.ContentType);

            AddConfigLabel("请求体 (使用 {text} 作为文本占位符)".Translate());
            var requestBodyBox = new TextBox
            {
                Name = "DIY_RequestBody",
                Text = vts.Set.DIY.RequestBody,
                AcceptsReturn = true,
                Height = 60,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            ProviderConfigPanel.Children.Add(requestBodyBox);
        }

        private void AddConfigLabel(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 0, 0, 4)
            };
            ProviderConfigPanel.Children.Add(label);
        }

        private void AddTextBox(string name, string text)
        {
            var textBox = new TextBox
            {
                Name = name,
                Text = text ?? "",
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(5, 3, 5, 3)
            };
            ProviderConfigPanel.Children.Add(textBox);
        }

        private void SaveProviderConfig()
        {
            if (CombProvider.SelectedItem is ComboBoxItem selectedItem)
            {
                var provider = selectedItem.Tag?.ToString();
                vts.Set.Provider = provider ?? "Free";

                switch (provider)
                {
                    case "Free":
                        SaveFreeConfig();
                        break;
                    case "OpenAI":
                        SaveOpenAIConfig();
                        break;
                    case "GPT-SoVITS":
                        SaveGPTSoVITSConfig();
                        break;
                    case "URL":
                        SaveURLConfig();
                        break;
                    case "DIY":
                        SaveDIYConfig();
                        break;
                }
            }
        }

        private void SaveFreeConfig()
        {
            var langCombo = FindControl<ComboBox>("Free_TextLanguage");
            if (langCombo?.SelectedItem is ComboBoxItem item)
                vts.Set.Free.TextLanguage = item.Tag?.ToString() ?? "auto";
        }

        private void SaveOpenAIConfig()
        {
            vts.Set.OpenAI.ApiKey = FindControl<TextBox>("OpenAI_ApiKey")?.Text ?? "";
            vts.Set.OpenAI.BaseUrl = FindControl<TextBox>("OpenAI_BaseUrl")?.Text ?? "";
            vts.Set.OpenAI.Model = FindControl<TextBox>("OpenAI_Model")?.Text ?? "";
            vts.Set.OpenAI.Voice = FindControl<TextBox>("OpenAI_Voice")?.Text ?? "";
        }

        private void SaveGPTSoVITSConfig()
        {
            vts.Set.GPTSoVITS.BaseUrl = FindControl<TextBox>("GPTSoVITS_BaseUrl")?.Text ?? "";
            vts.Set.GPTSoVITS.ReferWavPath = FindControl<TextBox>("GPTSoVITS_ReferWavPath")?.Text ?? "";
            vts.Set.GPTSoVITS.PromptText = FindControl<TextBox>("GPTSoVITS_PromptText")?.Text ?? "";

            var apiModeCombo = FindControl<ComboBox>("GPTSoVITS_ApiMode");
            if (apiModeCombo?.SelectedItem is ComboBoxItem item)
                vts.Set.GPTSoVITS.ApiMode = item.Tag?.ToString() ?? "WebUI";

            var textLanguageCombo = FindControl<ComboBox>("GPTSoVITS_TextLanguage");
            if (textLanguageCombo?.SelectedItem is ComboBoxItem textLanguage)
                vts.Set.GPTSoVITS.TextLanguage = TTSLanguage.Normalize(
                    textLanguage.Tag?.ToString(),
                    "auto");

            var promptLanguageCombo = FindControl<ComboBox>("GPTSoVITS_PromptLanguage");
            if (promptLanguageCombo?.SelectedItem is ComboBoxItem promptLanguage)
                vts.Set.GPTSoVITS.PromptLanguage = TTSLanguage.Normalize(
                    promptLanguage.Tag?.ToString(),
                    "zh");
        }

        private void SaveURLConfig()
        {
            vts.Set.URL.BaseUrl = FindControl<TextBox>("URL_BaseUrl")?.Text ?? "";
            vts.Set.URL.Voice = FindControl<TextBox>("URL_Voice")?.Text ?? "";

            var methodCombo = FindControl<ComboBox>("URL_Method");
            if (methodCombo?.SelectedItem is ComboBoxItem item)
                vts.Set.URL.Method = item.Tag?.ToString() ?? "GET";
        }

        private void SaveDIYConfig()
        {
            vts.Set.DIY.BaseUrl = FindControl<TextBox>("DIY_BaseUrl")?.Text ?? "";
            vts.Set.DIY.ContentType = FindControl<TextBox>("DIY_ContentType")?.Text ?? "";
            vts.Set.DIY.RequestBody = FindControl<TextBox>("DIY_RequestBody")?.Text ?? "";

            var methodCombo = FindControl<ComboBox>("DIY_Method");
            if (methodCombo?.SelectedItem is ComboBoxItem item)
                vts.Set.DIY.Method = item.Tag?.ToString() ?? "POST";
        }

        private T FindControl<T>(string name) where T : FrameworkElement
        {
            foreach (var child in ProviderConfigPanel.Children)
            {
                if (child is T control && control.Name == name)
                    return control;
            }
            return null;
        }

        private void BlockedPlugins_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new winBlockedPlugins(vts);
                win.Owner = this;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                var message = string.Format("打开插件屏蔽设置失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        /// <summary>
        /// 打开括号类型选择。确定后立即落盘，
        /// 与插件屏蔽设置的行为保持一致（不依赖主窗口再点一次保存）。
        /// </summary>
        private void TextFilterConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                vts.Set.TextFilter ??= new TextFilterSetting();

                var win = new winTextFilter(vts.Set.TextFilter);
                win.Owner = this;
                win.ShowDialog();

                if (win.Confirmed)
                {
                    vts.Set.Validate();
                    vts.MW.Set["VPetTTS"] = LPSConvert.SerializeObject(vts.Set, "VPetTTS");
                }
            }
            catch (Exception ex)
            {
                var message = string.Format("打开括号过滤设置失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 处理启用/禁用状态变更
                if (vts.Set.Enable != SwitchOn.IsChecked.Value)
                {
                    if (SwitchOn.IsChecked.Value)
                        vts.MW.Main.SayProcess.Add(vts.Main_OnSay);
                    else
                        vts.MW.Main.SayProcess.Remove(vts.Main_OnSay);
                    vts.Set.Enable = SwitchOn.IsChecked.Value;
                }

                // 保存基本设置
                vts.Set.Volume = VolumeSilder.Value;
                vts.Set.Speed = SpeedSilder.Value;
                vts.Set.RequestTimeout = (int)TimeoutSlider.Value;
                vts.Set.EnableCache = EnableCache.IsChecked.Value;
                vts.Set.TextFilter.Enable = EnableTextFilter.IsChecked.Value;

                // 播放器偏好变化时刷新播放器选择
                var preferBuiltIn = PreferBuiltInPlayer.IsChecked.Value;
                var playerPreferenceChanged = vts.Set.PreferVPetBuiltInPlayer != preferBuiltIn;
                vts.Set.PreferVPetBuiltInPlayer = preferBuiltIn;
                if (playerPreferenceChanged)
                {
                    vts.RefreshPlayerDetection();
                }

                // 保存代理设置
                vts.Set.Proxy.IsEnabled = EnableProxy.IsChecked.Value;
                vts.Set.Proxy.FollowSystemProxy = FollowSystemProxy.IsChecked.Value;
                vts.Set.Proxy.Address = ProxyAddress.Text;
                if (ProxyProtocol.SelectedItem is ComboBoxItem protocolItem)
                    vts.Set.Proxy.Protocol = protocolItem.Tag?.ToString() ?? "http";

                // 保存提供商配置
                SaveProviderConfig();

                // 验证并保存设置
                vts.Set.Validate();
                vts.MW.Set["VPetTTS"] = LPSConvert.SerializeObject(vts.Set, "VPetTTS");

                // 刷新 TTS 管理器设置
                vts.ttsManager?.RefreshSettings();

                // 关闭窗口
                Close();
            }
            catch (Exception ex)
            {
                var message = string.Format("保存设置失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            Test.IsEnabled = false;
            try
            {
                SaveProviderConfig();
                vts.Set.Volume = VolumeSilder.Value;
                vts.Set.Speed = SpeedSilder.Value;

                // 测试应立即使用刚在界面中选择的语言，而不是已缓存的旧设置。
                vts.Set.Validate();
                vts.ttsManager?.RefreshSettings();

                var success = await vts.TestTTSAsync();
                if (!success)
                {
                    UpdateServiceUnavailableStatus();
                    MessageBoxX.Show("TTS 测试失败，请检查配置".Translate(), "测试失败".Translate());
                }
                else
                {
                    UpdateServiceUnavailableStatus();
                }
            }
            catch (Exception ex)
            {
                UpdateServiceUnavailableStatus();
                var message = string.Format("测试失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
            finally
            {
                Test.IsEnabled = true;
            }
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 先显示缓存统计
                var stats = vts.GetCacheStatistics();
                string statsInfo = stats is not null
                    ? string.Format(
                        "当前缓存: {0} 个文件, {1}\n过期文件: {2} 个\n\n确定要清理所有缓存吗？".Translate(),
                        stats.TotalFiles,
                        stats.TotalSizeFormatted,
                        stats.ExpiredFiles)
                    : "确定要清理所有缓存吗？".Translate();

                var result = MessageBoxX.Show(statsInfo, "清理缓存".Translate(), MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    vts.ClearCache();
                    MessageBoxX.Show("缓存已清理".Translate(), "提示".Translate());
                }
            }
            catch (Exception ex)
            {
                var message = string.Format("清理缓存失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        private void CleanupExpiredCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var deletedCount = vts.CleanupExpiredCache();
                var message = string.Format(
                    "已清理 {0} 个过期缓存文件（超过7天未使用）".Translate(),
                    deletedCount);
                MessageBoxX.Show(message, "提示".Translate());
            }
            catch (Exception ex)
            {
                var message = string.Format("清理过期缓存失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        private void Debug_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                vts.OpenDebugWindow();
            }
            catch (Exception ex)
            {
                var message = string.Format("打开调试窗口失败: {0}".Translate(), ex.Message);
                MessageBoxX.Show(message, "错误".Translate());
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            vts.winSetting = null;
        }
    }
}
