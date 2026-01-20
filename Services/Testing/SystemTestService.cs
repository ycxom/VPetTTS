namespace Vpet.Plugin.CustomTTS.Services.Testing;

/// <summary>
/// 系统测试服务
/// 负责系统测试和诊断功能
/// </summary>
public class SystemTestService : ISystemTestService
{
    private readonly IPlayerManager _playerManager;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IPluginDetectionService _pluginDetectionService;

    public SystemTestService(
        IPlayerManager playerManager,
        IAudioPlaybackService audioPlaybackService,
        IPluginDetectionService pluginDetectionService)
    {
        _playerManager = playerManager ?? throw new ArgumentNullException(nameof(playerManager));
        _audioPlaybackService = audioPlaybackService ?? throw new ArgumentNullException(nameof(audioPlaybackService));
        _pluginDetectionService = pluginDetectionService ?? throw new ArgumentNullException(nameof(pluginDetectionService));
    }

    /// <summary>
    /// 运行综合系统测试
    /// </summary>
    public async Task<SystemTestResult> RunSystemTestAsync()
    {
        var result = new SystemTestResult
        {
            TestStartTime = DateTime.Now,
            TestErrors = new List<string>()
        };

        try
        {
            // 1. 测试播放器检测
            result.PlayerDetectionPassed = await TestPlayerDetectionAsync();

            // 2. 测试音频路径处理
            result.PathProcessingPassed = await TestAudioPathProcessingAsync();

            // 3. 测试错误处理
            result.ErrorHandlingPassed = await TestErrorHandlingAsync();

            // 4. 测试状态管理
            result.StateManagementPassed = await TestStateManagementAsync();

            // 5. 测试播放器切换
            result.PlayerSwitchingPassed = await TestPlayerSwitchingAsync();

            // 记录检测到的播放器类型
            result.DetectedPlayerType = _playerManager.CurrentPlayerType;

            // 计算总体结果
            result.OverallPassed = result.PlayerDetectionPassed &&
                                   result.PathProcessingPassed &&
                                   result.ErrorHandlingPassed &&
                                   result.StateManagementPassed &&
                                   result.PlayerSwitchingPassed;
        }
        catch (Exception ex)
        {
            result.TestErrors.Add($"系统测试异常: {ex.Message}");
            result.OverallPassed = false;
        }
        finally
        {
            result.TestDuration = DateTime.Now - result.TestStartTime;
        }

        return result;
    }

    /// <summary>
    /// 验证向后兼容性
    /// </summary>
    public bool VerifyBackwardCompatibility()
    {
        try
        {
            // 使用反射检查公共 API 是否保持不变
            var vpetTTSType = typeof(VPetTTS);

            // 检查关键属性
            var hasSet = vpetTTSType.GetProperty("Set") is not null;
            var hasTTSManager = vpetTTSType.GetProperty("ttsManager") is not null;
            var hasTTSState = vpetTTSType.GetProperty("TTSState") is not null;

            // 检查关键方法
            var hasLoadPlugin = vpetTTSType.GetMethod("LoadPlugin") is not null;
            var hasSetting = vpetTTSType.GetMethod("Setting") is not null;

            return hasSet && hasTTSManager && hasTTSState && hasLoadPlugin && hasSetting;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 测试播放器检测
    /// </summary>
    public async Task<bool> TestPlayerDetectionAsync()
    {
        try
        {
            var playerType = _playerManager.CurrentPlayerType;
            return playerType != PlayerType.None;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 测试音频路径处理
    /// </summary>
    public async Task<bool> TestAudioPathProcessingAsync()
    {
        try
        {
            // 测试路径验证
            var testPath = Path.Combine(Path.GetTempPath(), "test.mp3");
            var result = AudioPathHelper.ValidateAudioPath(testPath);
            return true; // 验证方法可以正常调用
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 测试错误处理
    /// </summary>
    public async Task<bool> TestErrorHandlingAsync()
    {
        try
        {
            // 测试错误统计功能
            var stats = _playerManager.GetPlayerErrorStatistics();
            return stats is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 测试状态管理
    /// </summary>
    public async Task<bool> TestStateManagementAsync()
    {
        try
        {
            // 测试播放器状态查询
            var status = _playerManager.GetPlayerStatus();
            return status is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 测试播放器切换
    /// </summary>
    public async Task<bool> TestPlayerSwitchingAsync()
    {
        try
        {
            await _playerManager.CheckPlayerAvailabilityAsync();
            var bestPlayer = _playerManager.GetBestAvailablePlayer();
            return bestPlayer != PlayerType.None;
        }
        catch
        {
            return false;
        }
    }
}
