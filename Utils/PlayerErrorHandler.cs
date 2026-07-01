namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 播放器错误处理器
    /// </summary>
    public class PlayerErrorHandler
    {
        private readonly List<PlayerErrorRecord> _errorHistory = new List<PlayerErrorRecord>();
        private readonly object _lockObject = new object();
        private readonly int _maxErrorHistory = 100;

        /// <summary>
        /// 处理播放器错误
        /// </summary>
        public void HandlePlayerError(PlayerType playerType, Exception exception, string context, string audioPath = null)
        {
            var errorRecord = new PlayerErrorRecord
            {
                Timestamp = DateTime.Now,
                PlayerType = playerType,
                ErrorMessage = exception?.Message ?? "未知错误",
                StackTrace = exception?.StackTrace ?? "",
                Context = context ?? "",
                AudioPath = audioPath ?? "",
                ExceptionType = exception?.GetType().Name ?? "Unknown"
            };

            lock (_lockObject)
            {
                _errorHistory.Add(errorRecord);

                // 保持错误历史记录在限制范围内
                if (_errorHistory.Count > _maxErrorHistory)
                {
                    _errorHistory.RemoveAt(0);
                }
            }

            // 记录详细的错误日志
            LogDetailedError(errorRecord);
        }

        /// <summary>
        /// 记录播放器切换
        /// </summary>
        public void LogPlayerSwitch(PlayerType fromPlayer, PlayerType toPlayer, string reason)
        {
            var switchRecord = new PlayerSwitchRecord
            {
                Timestamp = DateTime.Now,
                FromPlayer = fromPlayer,
                ToPlayer = toPlayer,
                Reason = reason ?? "未指定原因"
            };

            LogMessage($"播放器切换记录:");
            LogMessage($"  时间: {switchRecord.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            LogMessage($"  从: {fromPlayer} → 到: {toPlayer}");
            LogMessage($"  原因: {reason}");
            LogMessage($"  切换ID: {switchRecord.SwitchId}");
        }

        /// <summary>
        /// 判断是否应该使用不同播放器重试
        /// </summary>
        public bool ShouldRetryWithDifferentPlayer(Exception exception, PlayerType currentPlayer)
        {
            if (exception is null)
                return false;

            var errorMessage = exception.Message.ToLowerInvariant();

            // mpv 相关错误，应该切换到内置播放器
            if (currentPlayer == PlayerType.MpvPlayer)
            {
                var mpvErrors = new[]
                {
                    "mpv", "process", "进程", "executable", "可执行文件",
                    "file not found", "文件未找到", "access denied", "访问被拒绝",
                    "timeout", "超时", "killed", "terminated"
                };

                foreach (var errorKeyword in mpvErrors)
                {
                    if (errorMessage.Contains(errorKeyword))
                    {
                        LogMessage($"检测到 mpv 相关错误，建议切换播放器: {errorKeyword}");
                        return true;
                    }
                }
            }

            // 内置播放器错误，通常不需要切换（已经是最后的选择）
            if (currentPlayer == PlayerType.VPetBuiltIn)
            {
                LogMessage("VPet 内置播放器错误，无其他播放器可切换");
                return false;
            }

            return false;
        }

        /// <summary>
        /// 获取错误统计信息
        /// </summary>
        public PlayerErrorStatistics GetErrorStatistics()
        {
            lock (_lockObject)
            {
                var stats = new PlayerErrorStatistics();

                foreach (var error in _errorHistory)
                {
                    stats.TotalErrors++;

                    if (!stats.ErrorsByPlayer.ContainsKey(error.PlayerType))
                        stats.ErrorsByPlayer[error.PlayerType] = 0;
                    stats.ErrorsByPlayer[error.PlayerType]++;

                    if (!stats.ErrorsByType.ContainsKey(error.ExceptionType))
                        stats.ErrorsByType[error.ExceptionType] = 0;
                    stats.ErrorsByType[error.ExceptionType]++;
                }

                if (_errorHistory.Count > 0)
                {
                    stats.FirstErrorTime = _errorHistory[0].Timestamp;
                    stats.LastErrorTime = _errorHistory[_errorHistory.Count - 1].Timestamp;
                }

                return stats;
            }
        }

        /// <summary>
        /// 获取最近的错误记录
        /// </summary>
        public List<PlayerErrorRecord> GetRecentErrors(int count = 10)
        {
            lock (_lockObject)
            {
                var recentErrors = new List<PlayerErrorRecord>();
                var startIndex = Math.Max(0, _errorHistory.Count - count);

                for (int i = startIndex; i < _errorHistory.Count; i++)
                {
                    recentErrors.Add(_errorHistory[i]);
                }

                return recentErrors;
            }
        }

        /// <summary>
        /// 清除错误历史
        /// </summary>
        public void ClearErrorHistory()
        {
            lock (_lockObject)
            {
                _errorHistory.Clear();
            }
            LogMessage("播放器错误历史已清除");
        }

        /// <summary>
        /// 导出错误报告
        /// </summary>
        public string ExportErrorReport()
        {
            var report = new StringBuilder();
            report.AppendLine("VPetTTS 播放器错误报告");
            report.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            var stats = GetErrorStatistics();
            report.AppendLine("错误统计:");
            report.AppendLine($"  总错误数: {stats.TotalErrors}");

            if (stats.TotalErrors > 0)
            {
                report.AppendLine($"  首次错误: {stats.FirstErrorTime:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"  最近错误: {stats.LastErrorTime:yyyy-MM-dd HH:mm:ss}");

                report.AppendLine("  按播放器分类:");
                foreach (var kvp in stats.ErrorsByPlayer)
                {
                    report.AppendLine($"    {kvp.Key}: {kvp.Value} 次");
                }

                report.AppendLine("  按错误类型分类:");
                foreach (var kvp in stats.ErrorsByType)
                {
                    report.AppendLine($"    {kvp.Key}: {kvp.Value} 次");
                }
            }

            report.AppendLine();
            report.AppendLine("最近错误详情:");

            var recentErrors = GetRecentErrors(20);
            foreach (var error in recentErrors)
            {
                report.AppendLine($"[{error.Timestamp:yyyy-MM-dd HH:mm:ss}] {error.PlayerType}");
                report.AppendLine($"  错误: {error.ErrorMessage}");
                report.AppendLine($"  上下文: {error.Context}");
                if (!string.IsNullOrEmpty(error.AudioPath))
                {
                    report.AppendLine($"  文件: {Path.GetFileName(error.AudioPath)}");
                }
                report.AppendLine();
            }

            return report.ToString();
        }

        /// <summary>
        /// 记录详细错误信息
        /// </summary>
        private void LogDetailedError(PlayerErrorRecord error)
        {
            LogMessage($"播放器错误详情:");
            LogMessage($"  时间: {error.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            LogMessage($"  播放器: {error.PlayerType}");
            LogMessage($"  错误类型: {error.ExceptionType}");
            LogMessage($"  错误信息: {error.ErrorMessage}");
            LogMessage($"  上下文: {error.Context}");

            if (!string.IsNullOrEmpty(error.AudioPath))
            {
                LogMessage($"  音频文件: {Path.GetFileName(error.AudioPath)}");
            }

            if (!string.IsNullOrEmpty(error.StackTrace))
            {
                LogMessage($"  堆栈跟踪: {error.StackTrace.Split('\n')[0]}"); // 只显示第一行
            }

            LogMessage($"  错误ID: {error.ErrorId}");
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        private void LogMessage(string message)
        {
            TTSLogger.Log($"[PlayerErrorHandler] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }

    /// <summary>
    /// 播放器错误记录
    /// </summary>
    public class PlayerErrorRecord
    {
        public string ErrorId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime Timestamp { get; set; }
        public PlayerType PlayerType { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string Context { get; set; } = "";
        public string AudioPath { get; set; } = "";
        public string ExceptionType { get; set; } = "";
    }

    /// <summary>
    /// 播放器切换记录
    /// </summary>
    public class PlayerSwitchRecord
    {
        public string SwitchId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime Timestamp { get; set; }
        public PlayerType FromPlayer { get; set; }
        public PlayerType ToPlayer { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// 播放器错误统计
    /// </summary>
    public class PlayerErrorStatistics
    {
        public int TotalErrors { get; set; }
        public DateTime FirstErrorTime { get; set; }
        public DateTime LastErrorTime { get; set; }
        public Dictionary<PlayerType, int> ErrorsByPlayer { get; set; } = new Dictionary<PlayerType, int>();
        public Dictionary<string, int> ErrorsByType { get; set; } = new Dictionary<string, int>();
    }
}