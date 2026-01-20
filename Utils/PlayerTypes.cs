namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 播放器状态
    /// </summary>
    public class PlayerStatus
    {
        public PlayerType Type { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsPlaying { get; set; }
        public string LastError { get; set; } = "";
        public DateTime LastErrorTime { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// 播放器变化事件参数
    /// </summary>
    public class PlayerChangedEventArgs : EventArgs
    {
        public PlayerType OldPlayerType { get; set; }
        public PlayerType NewPlayerType { get; set; }
        public string Reason { get; set; }
        public DateTime ChangeTime { get; set; } = DateTime.Now;

        public PlayerChangedEventArgs(PlayerType oldType, PlayerType newType, string reason)
        {
            OldPlayerType = oldType;
            NewPlayerType = newType;
            Reason = reason;
        }
    }

    /// <summary>
    /// 播放器详细信息（供设置界面使用）
    /// </summary>
    public class PlayerDetailInfo
    {
        public PlayerType CurrentPlayerType { get; set; }
        public bool IsPlayerAvailable { get; set; }
        public string PlayerStatusSummary { get; set; } = "";

        // VPetLLM 相关信息
        public bool VPetLLMPluginExists { get; set; }
        public bool MpvPlayerAvailable { get; set; }
        public string MpvExePath { get; set; } = "";
        public string MpvVersion { get; set; } = "";
        public long MpvFileSize { get; set; }

        // 播放器状态
        public bool IsPlaying { get; set; }
        public string LastError { get; set; } = "";
        public DateTime LastErrorTime { get; set; }

        // 错误统计
        public int TotalErrors { get; set; }
        public int RecentErrorCount { get; set; }
        public List<string> InitializationErrors { get; set; } = new List<string>();
    }

    /// <summary>
    /// 系统测试结果
    /// </summary>
    public class SystemTestResult
    {
        public bool OverallPassed { get; set; }
        public DateTime TestStartTime { get; set; }
        public TimeSpan TestDuration { get; set; }

        // 各项测试结果
        public bool PlayerDetectionPassed { get; set; }
        public bool PathProcessingPassed { get; set; }
        public bool ErrorHandlingPassed { get; set; }
        public bool StateManagementPassed { get; set; }
        public bool PlayerSwitchingPassed { get; set; }

        // 检测到的播放器类型
        public PlayerType DetectedPlayerType { get; set; }

        // 测试错误列表
        public List<string> TestErrors { get; set; } = new List<string>();

        /// <summary>
        /// 获取测试结果摘要
        /// </summary>
        public string GetSummary()
        {
            var summary = new StringBuilder();
            summary.AppendLine($"系统测试结果: {(OverallPassed ? "通过" : "失败")}");
            summary.AppendLine($"测试时间: {TestStartTime:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine($"测试耗时: {TestDuration.TotalMilliseconds:F0} ms");
            summary.AppendLine($"检测到的播放器: {DetectedPlayerType}");
            summary.AppendLine();
            summary.AppendLine("详细结果:");
            summary.AppendLine($"  播放器检测: {(PlayerDetectionPassed ? "✓" : "✗")}");
            summary.AppendLine($"  路径处理: {(PathProcessingPassed ? "✓" : "✗")}");
            summary.AppendLine($"  错误处理: {(ErrorHandlingPassed ? "✓" : "✗")}");
            summary.AppendLine($"  状态管理: {(StateManagementPassed ? "✓" : "✗")}");
            summary.AppendLine($"  播放器切换: {(PlayerSwitchingPassed ? "✓" : "✗")}");

            if (TestErrors.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("错误详情:");
                foreach (var error in TestErrors)
                {
                    summary.AppendLine($"  - {error}");
                }
            }

            return summary.ToString();
        }
    }
}