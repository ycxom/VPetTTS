using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LinePutScript.Converter;
using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;

namespace VPet.Plugin.VPetTTS
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
        }

        private void LoadSettings()
        {
            // 基本设置
            SwitchOn.IsChecked = vts.Set.Enable;
            VolumeSilder.Value = vts.Set.Volume;
            SpeedSilder.Value = vts.Set.Speed;
            EnableCache.IsChecked = vts.Set.EnableCache;

            // 提供商选择
            foreach (ComboBoxItem item in CombProvider.Items)
            {
                if (item.Tag?.ToString() == vts.Set.Provider)
                {
                    CombProvider.SelectedItem = item;
                    break;
                }
            }
            if (CombProvider.SelectedItem == null && CombProvider.Items.Count > 0)
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
            if (ProxyProtocol.SelectedItem == null && ProxyProtocol.Items.Count > 0)
                ProxyProtocol.SelectedIndex = 0;

            UpdateProviderConfig();
            UpdateSpeedText();
        }

        private void SetupEventHandlers()
        {
            SpeedSilder.ValueChanged += (s, e) => UpdateSpeedText();
            CombProvider.SelectionChanged += (s, e) => UpdateProviderConfig();
        }

        private void UpdateSpeedText()
        {
            SpeedText.Text = $"{SpeedSilder.Value:F1}x";
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
            if (langCombo.SelectedItem == null && langCombo.Items.Count > 0)
                langCombo.SelectedIndex = 0;
            
            ProviderConfigPanel.Children.Add(langCombo);
        }

        private void AddOpenAIConfig()
        {
            AddConfigLabel("API Key");
            AddTextBox("OpenAI_ApiKey", vts.Set.OpenAI.ApiKey);

            AddConfigLabel("Base URL");
            AddTextBox("OpenAI_BaseUrl", vts.Set.OpenAI.BaseUrl);

            AddConfigLabel("Model");
            AddTextBox("OpenAI_Model", vts.Set.OpenAI.Model);

            AddConfigLabel("Voice");
            AddTextBox("OpenAI_Voice", vts.Set.OpenAI.Voice);
        }

        private void AddGPTSoVITSConfig()
        {
            AddConfigLabel("Base URL");
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

            AddConfigLabel("参考音频路径".Translate());
            AddTextBox("GPTSoVITS_ReferWavPath", vts.Set.GPTSoVITS.ReferWavPath);

            AddConfigLabel("提示文本".Translate());
            AddTextBox("GPTSoVITS_PromptText", vts.Set.GPTSoVITS.PromptText);
        }

        private void AddURLConfig()
        {
            AddConfigLabel("Base URL");
            AddTextBox("URL_BaseUrl", vts.Set.URL.BaseUrl);

            AddConfigLabel("Voice ID");
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
            AddConfigLabel("Base URL");
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

            AddConfigLabel("Content-Type");
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
                vts.Set.EnableCache = EnableCache.IsChecked.Value;

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
                MessageBoxX.Show($"保存设置失败: {ex.Message}".Translate(), "错误".Translate());
            }
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            Test.IsEnabled = false;
            try
            {
                // 临时应用当前设置进行测试
                SaveProviderConfig();
                vts.Set.Volume = VolumeSilder.Value;
                vts.Set.Speed = SpeedSilder.Value;

                var success = await vts.TestTTSAsync();
                if (!success)
                {
                    MessageBoxX.Show("TTS 测试失败，请检查配置".Translate(), "测试失败".Translate());
                }
            }
            catch (Exception ex)
            {
                MessageBoxX.Show($"测试失败: {ex.Message}".Translate(), "错误".Translate());
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
                vts.ClearCache();
                MessageBoxX.Show("缓存已清理".Translate(), "提示".Translate());
            }
            catch (Exception ex)
            {
                MessageBoxX.Show($"清理缓存失败: {ex.Message}".Translate(), "错误".Translate());
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            vts.winSetting = null;
        }
    }
}
