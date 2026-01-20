namespace Vpet.Plugin.CustomTTS.Services.Player;

/// <summary>
/// 播放器管理器
/// 负责播放器检测、初始化、切换和状态管理
/// </summary>
public class PlayerManager : IPlayerManager
{
    private readonly IMainWindow _mainWindow;
    private readonly Setting _settings;
    private readonly TTSStateManager _stateManager;
    private readonly PlayerErrorHandler _errorHandler;

    // 播放器实例和状态
    private MpvPlayer _mpvPlayer;
    private PlayerType _currentPlayerType = PlayerType.None;
    private PlayerStatus _playerStatus = new PlayerStatus();
    private List<string> _playerInitErrors = new List<string>();
    private VPetLLMDetectionResult _vpetLLMDetectionResult;

    /// <summary>
    /// 播放器变化事件
    /// </summary>
    public event EventHandler<PlayerChangedEventArgs> PlayerChanged;

    public PlayerManager(
        IMainWindow mainWindow,
        Setting settings,
        TTSStateManager stateManager,
        PlayerErrorHandler errorHandler)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    }

    /// <summary>
    /// 当前播放器类型
    /// </summary>
    public PlayerType CurrentPlayerType => _currentPlayerType;

    /// <summary>
    /// 是否使用 mpv 播放器
    /// </summary>
    public bool UseMpvPlayer => _mpvPlayer is not null && _currentPlayerType == PlayerType.MpvPlayer;

    /// <summary>
    /// 获取 mpv 播放器实例（供内部使用）
    /// </summary>
    internal MpvPlayer GetMpvPlayer() => _mpvPlayer;

    /// <summary>
    /// 初始化播放器管理器
    /// </summary>
    public void Initialize()
    {
        DetectAndInitializePlayers();
    }

    /// <summary>
    /// 检测并初始化所有可用播放器
    /// </summary>
    private void DetectAndInitializePlayers()
    {
        var oldPlayerType = _currentPlayerType;
        _playerInitErrors.Clear();

        try
        {
            _vpetLLMDetectionResult = VPetLLMDetector.DetectVPetLLM(_mainWindow, forceRefresh: true);

            // 记录检测过程中的错误
            if (_vpetLLMDetectionResult.DetectionErrors.Count > 0)
            {
                _playerInitErrors.AddRange(_vpetLLMDetectionResult.DetectionErrors);
            }

            if (_vpetLLMDetectionResult.CanUseMpvPlayer)
            {
                try
                {
                    // 验证 mpv 播放器是否可执行
                    if (VPetLLMDetector.ValidateMpvPlayer(_vpetLLMDetectionResult.MpvExePath))
                    {
                        _mpvPlayer = new MpvPlayer(_vpetLLMDetectionResult.MpvExePath);
                        _mpvPlayer.SetVolume(_settings.Volume);

                        // 订阅 mpv 播放器事件
                        _mpvPlayer.ProcessExited += OnMpvProcessExited;
                        _mpvPlayer.PlaybackCompleted += OnMpvPlaybackCompleted;

                        _currentPlayerType = PlayerType.MpvPlayer;
                    }
                    else
                    {
                        _playerInitErrors.Add("mpv 播放器验证失败，无法执行");
                        _currentPlayerType = PlayerType.VPetBuiltIn;
                    }
                }
                catch (Exception ex)
                {
                    _playerInitErrors.Add($"初始化 mpv 播放器失败: {ex.Message}");
                    _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, ex, "mpv 播放器初始化", _vpetLLMDetectionResult.MpvExePath);
                    _mpvPlayer = null;
                    _currentPlayerType = PlayerType.VPetBuiltIn;
                }
            }
            else
            {
                _currentPlayerType = PlayerType.VPetBuiltIn;
            }

            // 更新播放器状态
            UpdatePlayerStatus();

            // 触发播放器变化事件
            if (oldPlayerType != _currentPlayerType)
            {
                var reason = GetPlayerChangeReason(oldPlayerType, _currentPlayerType);
                OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, reason));
            }
        }
        catch (Exception ex)
        {
            _playerInitErrors.Add($"播放器检测和初始化过程发生严重错误: {ex.Message}");
            _errorHandler.HandlePlayerError(PlayerType.None, ex, "播放器检测和初始化");

            // 回退到内置播放器
            _mpvPlayer = null;
            _currentPlayerType = PlayerType.VPetBuiltIn;
            UpdatePlayerStatus();

            if (oldPlayerType != _currentPlayerType)
            {
                OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, "初始化错误，回退到内置播放器"));
            }
        }
    }

    /// <summary>
    /// 刷新播放器检测
    /// </summary>
    public void RefreshDetection()
    {
        VPetLLMDetector.ClearCache();
        DetectAndInitializePlayers();
    }

    /// <summary>
    /// 切换到备用播放器
    /// </summary>
    public async Task SwitchToFallbackPlayerAsync(string reason)
    {
        var oldPlayerType = _currentPlayerType;

        try
        {
            // 如果当前是 mpv 播放器，切换到内置播放器
            if (_currentPlayerType == PlayerType.MpvPlayer)
            {
                // 停止 mpv 播放器
                if (_mpvPlayer is not null)
                {
                    try
                    {
                        await _mpvPlayer.StopAsync();
                    }
                    catch { }
                }

                _currentPlayerType = PlayerType.VPetBuiltIn;

                // 同步音量设置
                SyncVolumeSettings();

                // 更新播放器状态
                UpdatePlayerStatus();

                // 触发播放器变化事件
                OnPlayerChanged(new PlayerChangedEventArgs(oldPlayerType, _currentPlayerType, reason));
            }
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(_currentPlayerType, ex, "切换到备用播放器");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 获取最佳可用播放器
    /// </summary>
    public PlayerType GetBestAvailablePlayer()
    {
        try
        {
            // 如果 mpv 播放器可用，优先使用
            if (_mpvPlayer is not null)
            {
                var status = _mpvPlayer.GetProcessStatus();
                if (status != ProcessStatus.Disposed && status != ProcessStatus.Unknown)
                {
                    return PlayerType.MpvPlayer;
                }
            }

            // 否则使用内置播放器
            return PlayerType.VPetBuiltIn;
        }
        catch
        {
            return PlayerType.VPetBuiltIn;
        }
    }

    /// <summary>
    /// 检查播放器可用性
    /// </summary>
    public async Task CheckPlayerAvailabilityAsync()
    {
        try
        {
            // 如果当前是 mpv 播放器，检查其状态
            if (_currentPlayerType == PlayerType.MpvPlayer && _mpvPlayer is not null)
            {
                var processStatus = _mpvPlayer.GetProcessStatus();

                if (processStatus == ProcessStatus.Disposed || processStatus == ProcessStatus.Unknown)
                {
                    await SwitchToFallbackPlayerAsync("mpv 播放器状态异常");
                }
            }
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(_currentPlayerType, ex, "检查播放器可用性");
        }
    }

    /// <summary>
    /// 获取播放器状态
    /// </summary>
    public PlayerStatus GetPlayerStatus()
    {
        lock (_playerStatus)
        {
            return new PlayerStatus
            {
                Type = _currentPlayerType,
                IsAvailable = _currentPlayerType != PlayerType.None,
                IsPlaying = _stateManager?.IsPlaying ?? false,
                LastError = _playerStatus.LastError,
                LastErrorTime = _playerStatus.LastErrorTime
            };
        }
    }

    /// <summary>
    /// 获取播放器详细信息
    /// </summary>
    public PlayerDetailInfo GetPlayerDetailInfo()
    {
        try
        {
            var info = new PlayerDetailInfo
            {
                CurrentPlayerType = _currentPlayerType,
                IsPlayerAvailable = _currentPlayerType != PlayerType.None,
                PlayerStatusSummary = GetPlayerStatusSummary()
            };

            // mpv 播放器信息
            if (_vpetLLMDetectionResult is not null)
            {
                info.VPetLLMPluginExists = _vpetLLMDetectionResult.PluginExists;
                info.MpvPlayerAvailable = _vpetLLMDetectionResult.CanUseMpvPlayer;
                info.MpvExePath = _vpetLLMDetectionResult.MpvExePath;
                info.MpvVersion = _vpetLLMDetectionResult.MpvVersion;
                info.MpvFileSize = _vpetLLMDetectionResult.MpvFileSize;
            }

            // 播放器状态
            info.IsPlaying = _stateManager?.IsPlaying ?? false;
            info.LastError = _playerStatus.LastError;
            info.LastErrorTime = _playerStatus.LastErrorTime;

            // 错误统计
            var errorStats = _errorHandler.GetErrorStatistics();
            info.TotalErrors = errorStats.TotalErrors;
            info.RecentErrorCount = _errorHandler.GetRecentErrors(10).Count;
            info.InitializationErrors = new List<string>(_playerInitErrors);

            return info;
        }
        catch (Exception ex)
        {
            return new PlayerDetailInfo
            {
                CurrentPlayerType = _currentPlayerType,
                IsPlayerAvailable = false,
                PlayerStatusSummary = $"获取播放器信息失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 获取播放器状态描述
    /// </summary>
    public string GetPlayerStatusDescription()
    {
        return GetPlayerStatusSummary();
    }

    /// <summary>
    /// 获取播放器推荐信息
    /// </summary>
    public string GetPlayerRecommendation()
    {
        if (_currentPlayerType == PlayerType.VPetBuiltIn && _vpetLLMDetectionResult?.PluginExists == false)
        {
            return "建议安装 VPetLLM 插件以使用 mpv 播放器，获得更好的音频播放体验";
        }
        return "";
    }

    /// <summary>
    /// 更新播放器音量
    /// </summary>
    public void UpdateVolume(double volume)
    {
        try
        {
            // 更新设置
            if (_settings is not null)
            {
                _settings.Volume = volume;
            }

            // 同步到所有播放器
            SyncVolumeSettings();
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(_currentPlayerType, ex, "更新播放器音量");
        }
    }

    /// <summary>
    /// 同步音量设置到所有播放器
    /// </summary>
    public void SyncVolumeSettings()
    {
        try
        {
            if (_mpvPlayer is not null)
            {
                _mpvPlayer.SetVolume(_settings.Volume);
            }
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, ex, "同步音量设置");
        }
    }

    /// <summary>
    /// 获取播放器错误统计
    /// </summary>
    public PlayerErrorStatistics GetPlayerErrorStatistics()
    {
        return _errorHandler.GetErrorStatistics();
    }

    /// <summary>
    /// 获取最近的播放器错误记录
    /// </summary>
    public List<PlayerErrorRecord> GetRecentPlayerErrors(int count = 10)
    {
        return _errorHandler.GetRecentErrors(count);
    }

    /// <summary>
    /// 导出播放器错误报告
    /// </summary>
    public string ExportPlayerErrorReport()
    {
        return _errorHandler.ExportErrorReport();
    }

    /// <summary>
    /// 清除播放器错误历史
    /// </summary>
    public void ClearPlayerErrorHistory()
    {
        _errorHandler.ClearErrorHistory();
    }

    // ============================================================================
    // 私有辅助方法
    // ============================================================================

    /// <summary>
    /// 更新播放器状态
    /// </summary>
    private void UpdatePlayerStatus()
    {
        lock (_playerStatus)
        {
            _playerStatus.Type = _currentPlayerType;
            _playerStatus.IsAvailable = _currentPlayerType != PlayerType.None;

            if (_playerInitErrors.Count > 0)
            {
                _playerStatus.LastError = string.Join("; ", _playerInitErrors);
                _playerStatus.LastErrorTime = DateTime.Now;
            }
        }
    }

    /// <summary>
    /// 获取播放器变化原因
    /// </summary>
    private string GetPlayerChangeReason(PlayerType oldType, PlayerType newType)
    {
        if (oldType == PlayerType.None && newType == PlayerType.MpvPlayer)
            return "检测到 VPetLLM 插件，初始化 mpv 播放器";
        else if (oldType == PlayerType.None && newType == PlayerType.VPetBuiltIn)
            return "未检测到 VPetLLM 插件，使用内置播放器";
        else if (oldType == PlayerType.MpvPlayer && newType == PlayerType.VPetBuiltIn)
            return "mpv 播放器不可用，回退到内置播放器";
        else if (oldType == PlayerType.VPetBuiltIn && newType == PlayerType.MpvPlayer)
            return "检测到可用的 mpv 播放器，切换使用";
        else
            return "播放器状态变化";
    }

    /// <summary>
    /// 触发播放器变化事件
    /// </summary>
    private void OnPlayerChanged(PlayerChangedEventArgs e)
    {
        try
        {
            PlayerChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(_currentPlayerType, ex, "播放器变化事件处理");
        }
    }

    /// <summary>
    /// 获取播放器状态摘要
    /// </summary>
    private string GetPlayerStatusSummary()
    {
        if (_currentPlayerType == PlayerType.MpvPlayer)
        {
            return "使用 mpv 播放器（高质量音频播放）";
        }
        else if (_currentPlayerType == PlayerType.VPetBuiltIn)
        {
            return "使用 VPet 内置播放器";
        }
        else
        {
            return "播放器未初始化";
        }
    }

    // ============================================================================
    // 事件处理方法
    // ============================================================================

    /// <summary>
    /// mpv 进程退出事件处理
    /// </summary>
    private void OnMpvProcessExited(object sender, ProcessExitedEventArgs e)
    {
        try
        {
            _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, null, "mpv 进程退出", e.Reason);

            // 如果是异常退出，考虑切换到内置播放器
            if (e.ExitCode != 0)
            {
                _ = Task.Run(async () => await SwitchToFallbackPlayerAsync($"mpv 进程异常退出: {e.Reason}"));
            }
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, ex, "处理 mpv 进程退出事件");
        }
    }

    /// <summary>
    /// mpv 播放完成事件处理
    /// </summary>
    private void OnMpvPlaybackCompleted(object sender, PlaybackCompletedEventArgs e)
    {
        try
        {
            // 播放完成，更新状态
            _stateManager?.SetPlayingState(false);
        }
        catch (Exception ex)
        {
            _errorHandler.HandlePlayerError(PlayerType.MpvPlayer, ex, "处理 mpv 播放完成事件");
        }
    }
}
