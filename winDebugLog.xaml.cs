using Microsoft.Win32;

namespace Vpet.Plugin.CustomTTS
{
    /// <summary>
    /// VPetTTS 调试日志窗口
    /// </summary>
    public partial class winDebugLog : Window
    {
        private readonly VPetTTS _plugin;
        private readonly DispatcherTimer _refreshTimer;
        private readonly StringBuilder _logBuffer;
        private readonly object _logLock = new object();

        public winDebugLog(VPetTTS plugin)
        {
            InitializeComponent();
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logBuffer = new StringBuilder();

            // 设置定时刷新
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 初始化界面
            InitializeDebugInterface();

            // 开始监听日志
            StartLogMonitoring();

            // 启动定时器
            _refreshTimer.Start();
        }

        /// <summary>
        /// 初始化调试界面
        /// </summary>
        private void InitializeDebugInterface()
        {
            try
            {
                // 立即刷新播放器状态
                RefreshPlayerStatus();

                // 添加初始日志
                AppendLog("=== VPetTTS 调试日志启动 ===");
                AppendLog($"调试界面已启动 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                AppendLog($"插件版本: {_plugin.PluginName}");
                AppendLog("");
            }
            catch (Exception ex)
            {
                AppendLog($"初始化调试界面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始监听日志
        /// </summary>
        private void StartLogMonitoring()
        {
            try
            {
                // 重定向控制台输出到我们的日志
                var originalOut = Console.Out;
                Console.SetOut(new DebugLogWriter(this, originalOut));

                AppendLog("日志监听已启动");
            }
            catch (Exception ex)
            {
                AppendLog($"启动日志监听失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 定时器刷新事件
        /// </summary>
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                RefreshPlayerStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"定时刷新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新播放器状态
        /// </summary>
        private void RefreshPlayerStatus()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 获取当前播放器信息
                    var currentPlayerType = _plugin.CurrentPlayerType;
                    var playerDetailInfo = _plugin.GetPlayerDetailInfo();

                    // 更新当前播放器显示
                    txtCurrentPlayer.Text = GetPlayerTypeDisplayName(currentPlayerType);
                    txtCurrentPlayer.Foreground = currentPlayerType == PlayerType.MpvPlayer ?
                        System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange;

                    // 更新 VPetLLM 插件状态
                    txtVPetLLMStatus.Text = playerDetailInfo.VPetLLMPluginExists ? "✓ 已安装" : "✗ 未安装";
                    txtVPetLLMStatus.Foreground = playerDetailInfo.VPetLLMPluginExists ?
                        System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;

                    // 更新 mpv 播放器状态
                    if (playerDetailInfo.VPetLLMPluginExists)
                    {
                        if (playerDetailInfo.MpvPlayerAvailable)
                        {
                            txtMpvStatus.Text = $"✓ 可用 ({playerDetailInfo.MpvVersion})";
                            txtMpvStatus.Foreground = System.Windows.Media.Brushes.Green;
                        }
                        else
                        {
                            txtMpvStatus.Text = "✗ 不可用";
                            txtMpvStatus.Foreground = System.Windows.Media.Brushes.Red;
                        }
                    }
                    else
                    {
                        txtMpvStatus.Text = "- VPetLLM 未安装";
                        txtMpvStatus.Foreground = System.Windows.Media.Brushes.Gray;
                    }

                    // 更新推荐播放器
                    var recommendedPlayer = playerDetailInfo.MpvPlayerAvailable ? PlayerType.MpvPlayer : PlayerType.VPetBuiltIn;
                    txtRecommendedPlayer.Text = GetPlayerTypeDisplayName(recommendedPlayer);
                    txtRecommendedPlayer.Foreground = recommendedPlayer == PlayerType.MpvPlayer ?
                        System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange;

                    // 如果推荐播放器与当前播放器不一致，显示警告
                    if (recommendedPlayer != currentPlayerType)
                    {
                        txtRecommendedPlayer.Text += " ⚠️";
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog($"刷新播放器状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取播放器类型显示名称
        /// </summary>
        private string GetPlayerTypeDisplayName(PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.MpvPlayer => "mpv 播放器 (高码率)",
                PlayerType.VPetBuiltIn => "VPet 内置播放器",
                PlayerType.None => "无播放器",
                _ => "未知播放器"
            };
        }

        /// <summary>
        /// 添加日志条目
        /// </summary>
        public void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            lock (_logLock)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] {message}";

                _logBuffer.AppendLine(logEntry);

                // 限制日志缓冲区大小
                if (_logBuffer.Length > 100000) // 100KB
                {
                    var content = _logBuffer.ToString();
                    var lines = content.Split('\n');
                    if (lines.Length > 1000)
                    {
                        _logBuffer.Clear();
                        for (int i = lines.Length - 800; i < lines.Length; i++)
                        {
                            if (i >= 0 && !string.IsNullOrEmpty(lines[i]))
                                _logBuffer.AppendLine(lines[i]);
                        }
                    }
                }
            }

            // 更新 UI
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    lock (_logLock)
                    {
                        txtLog.Text = _logBuffer.ToString();
                    }

                    // 自动滚动到底部
                    if (chkAutoScroll.IsChecked == true)
                    {
                        logScrollViewer.ScrollToEnd();
                    }
                }
                catch (Exception ex)
                {
                    // 避免递归日志错误
                    System.Diagnostics.Debug.WriteLine($"更新日志UI失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 刷新播放器按钮点击事件
        /// </summary>
        private void BtnRefreshPlayer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("=== 手动刷新播放器检测 ===");
                _plugin.RefreshPlayerDetection();
                RefreshPlayerStatus();
                AppendLog("播放器检测刷新完成");
            }
            catch (Exception ex)
            {
                AppendLog($"刷新播放器检测失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清空日志按钮点击事件
        /// </summary>
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            lock (_logLock)
            {
                _logBuffer.Clear();
                txtLog.Text = "";
            }
            AppendLog("=== 日志已清空 ===");
        }

        /// <summary>
        /// 测试播放器按钮点击事件
        /// </summary>
        private async void BtnTestPlayer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("=== 开始播放器测试 ===");
                btnTestPlayer.IsEnabled = false;
                btnTestPlayer.Content = "测试中...";

                // 运行系统测试
                var testResult = await _plugin.RunSystemTestAsync();

                AppendLog($"系统测试结果: {(testResult.OverallPassed ? "✓ 全部通过" : "✗ 部分失败")}");
                AppendLog($"测试耗时: {testResult.TestDuration.TotalMilliseconds:F0}ms");

                if (testResult.TestErrors.Count > 0)
                {
                    AppendLog("测试错误:");
                    foreach (var error in testResult.TestErrors)
                    {
                        AppendLog($"  - {error}");
                    }
                }

                AppendLog("=== 播放器测试完成 ===");
            }
            catch (Exception ex)
            {
                AppendLog($"播放器测试失败: {ex.Message}");
            }
            finally
            {
                btnTestPlayer.IsEnabled = true;
                btnTestPlayer.Content = "测试播放器";
            }
        }

        /// <summary>
        /// 运行诊断按钮点击事件
        /// </summary>
        private async void BtnRunDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("=== 开始系统诊断 ===");
                btnRunDiagnostic.IsEnabled = false;
                btnRunDiagnostic.Content = "诊断中...";

                await RunComprehensiveDiagnostic();

                AppendLog("=== 系统诊断完成 ===");
            }
            catch (Exception ex)
            {
                AppendLog($"系统诊断失败: {ex.Message}");
            }
            finally
            {
                btnRunDiagnostic.IsEnabled = true;
                btnRunDiagnostic.Content = "运行诊断";
            }
        }

        /// <summary>
        /// 运行综合诊断
        /// </summary>
        private async Task RunComprehensiveDiagnostic()
        {
            try
            {
                // 1. 检查插件环境
                AppendLog("1. 检查插件环境...");
                var pluginInfo = _plugin.GetPlayerDetailInfo();
                AppendLog($"   当前播放器: {pluginInfo.CurrentPlayerType}");
                AppendLog($"   播放器可用: {pluginInfo.IsPlayerAvailable}");

                // 2. 检查 VPetLLM 插件
                AppendLog("2. 检查 VPetLLM 插件...");
                AppendLog($"   插件存在: {pluginInfo.VPetLLMPluginExists}");
                if (pluginInfo.VPetLLMPluginExists)
                {
                    AppendLog($"   mpv 可用: {pluginInfo.MpvPlayerAvailable}");
                    AppendLog($"   mpv 路径: {pluginInfo.MpvExePath}");
                    AppendLog($"   mpv 版本: {pluginInfo.MpvVersion}");
                    AppendLog($"   文件大小: {pluginInfo.MpvFileSize / 1024 / 1024:F1} MB");
                }

                // 3. 检查初始化错误
                AppendLog("3. 检查初始化错误...");
                if (pluginInfo.InitializationErrors.Count > 0)
                {
                    AppendLog($"   发现 {pluginInfo.InitializationErrors.Count} 个初始化错误:");
                    foreach (var error in pluginInfo.InitializationErrors)
                    {
                        AppendLog($"     - {error}");
                    }
                }
                else
                {
                    AppendLog("   无初始化错误");
                }

                // 4. 检查播放器状态
                AppendLog("4. 检查播放器状态...");
                var playerStatus = _plugin.GetPlayerStatus();
                AppendLog($"   播放器类型: {playerStatus.Type}");
                AppendLog($"   是否可用: {playerStatus.IsAvailable}");
                AppendLog($"   是否播放中: {playerStatus.IsPlaying}");
                if (!string.IsNullOrEmpty(playerStatus.LastError))
                {
                    AppendLog($"   最近错误: {playerStatus.LastError}");
                    AppendLog($"   错误时间: {playerStatus.LastErrorTime:yyyy-MM-dd HH:mm:ss}");
                }

                // 5. 检查播放器推荐
                AppendLog("5. 检查播放器推荐...");
                var recommendation = _plugin.GetPlayerRecommendation();
                AppendLog($"   推荐信息: {recommendation}");

                // 6. 运行播放器可用性检查
                AppendLog("6. 运行播放器可用性检查...");
                await _plugin.CheckPlayerAvailabilityAsync();
                AppendLog("   播放器可用性检查完成");

                // 7. 生成诊断建议
                AppendLog("7. 生成诊断建议...");
                GenerateDiagnosticRecommendations(pluginInfo);
            }
            catch (Exception ex)
            {
                AppendLog($"诊断过程中发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成诊断建议
        /// </summary>
        private void GenerateDiagnosticRecommendations(PlayerDetailInfo info)
        {
            AppendLog("=== 诊断建议 ===");

            if (!info.VPetLLMPluginExists)
            {
                AppendLog("建议: 安装 VPetLLM 插件以获得 mpv 高码率音频支持");
            }
            else if (!info.MpvPlayerAvailable)
            {
                AppendLog("建议: VPetLLM 插件已安装但 mpv 不可用，请检查:");
                AppendLog("  1. VPetLLM 插件是否完整安装");
                AppendLog("  2. mpv.exe 文件是否存在于插件目录");
                AppendLog("  3. 是否有足够的文件访问权限");
            }
            else if (info.CurrentPlayerType != PlayerType.MpvPlayer)
            {
                AppendLog("建议: mpv 播放器可用但未被选择，可能原因:");
                AppendLog("  1. mpv 播放器初始化失败");
                AppendLog("  2. 播放器验证失败");
                AppendLog("  3. 存在其他配置问题");
                AppendLog("  请查看上方的初始化错误信息");
            }
            else
            {
                AppendLog("状态: mpv 播放器工作正常 ✓");
            }

            if (info.InitializationErrors.Count > 0)
            {
                AppendLog("注意: 发现初始化错误，这可能是播放器选择问题的根本原因");
            }
        }

        /// <summary>
        /// 保存日志按钮点击事件
        /// </summary>
        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "保存调试日志",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = $"VPetTTS_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    lock (_logLock)
                    {
                        File.WriteAllText(saveDialog.FileName, _logBuffer.ToString(), Encoding.UTF8);
                    }

                    AppendLog($"日志已保存到: {saveDialog.FileName}");
                    MessageBox.Show($"日志已保存到:\n{saveDialog.FileName}", "保存成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"保存日志失败: {ex.Message}");
                MessageBox.Show($"保存日志失败:\n{ex.Message}", "保存失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _refreshTimer?.Stop();
                AppendLog("调试窗口已关闭");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"关闭调试窗口时发生错误: {ex.Message}");
            }

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// 调试日志写入器
    /// </summary>
    public class DebugLogWriter : TextWriter
    {
        private readonly winDebugLog _debugWindow;
        private readonly TextWriter _originalWriter;

        public DebugLogWriter(winDebugLog debugWindow, TextWriter originalWriter)
        {
            _debugWindow = debugWindow;
            _originalWriter = originalWriter;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string value)
        {
            try
            {
                // 写入到原始输出
                _originalWriter?.WriteLine(value);

                // 写入到调试窗口
                if (!string.IsNullOrEmpty(value) && value.Contains("[VPetTTS]") || value.Contains("[VPetLLMDetector]") || value.Contains("[MpvPlayer]"))
                {
                    _debugWindow?.AppendLog(value);
                }
            }
            catch
            {
                // 忽略日志写入错误，避免递归
            }
        }

        public override void Write(string value)
        {
            _originalWriter?.Write(value);
        }
    }
}