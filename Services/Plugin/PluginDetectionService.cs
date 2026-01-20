namespace Vpet.Plugin.CustomTTS.Services.Plugin;

/// <summary>
/// 插件检测服务
/// 负责 VPetLLM 和其他 TTS 插件的检测
/// </summary>
public class PluginDetectionService : IPluginDetectionService
{
    private readonly IMainWindow _mainWindow;
    private readonly string _pluginName;

    // 检测结果
    private VPetLLMDetectionResult _vpetLLMDetectionResult;
    private OtherTTSPluginDetectionResult _otherTTSPluginDetectionResult;
    private bool _softDisabled = false;

    public PluginDetectionService(IMainWindow mainWindow, string pluginName)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _pluginName = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
    }

    /// <summary>
    /// 是否被软禁用（检测到其他 TTS 插件）
    /// </summary>
    public bool IsSoftDisabled => _softDisabled;

    /// <summary>
    /// 检测到的其他 TTS 插件名称
    /// </summary>
    public string DetectedOtherPluginNames => _otherTTSPluginDetectionResult?.PluginNames ?? "";

    /// <summary>
    /// 初始化插件检测服务
    /// </summary>
    public void Initialize()
    {
        DetectVPetLLMPlugin();
        DetectOtherTTSPlugins();
    }

    /// <summary>
    /// 检测 VPetLLM 插件
    /// </summary>
    public void DetectVPetLLMPlugin()
    {
        try
        {
            _vpetLLMDetectionResult = VPetLLMDetector.DetectVPetLLM(_mainWindow, forceRefresh: true);
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出异常
            // 创建一个空的检测结果，因为属性是只读的
            _vpetLLMDetectionResult = null;
        }
    }

    /// <summary>
    /// 检测其他 TTS 插件（不包括 VPetLLM 内置 TTS）
    /// VPetTTS 不再避让 VPetLLM，只检测真正的 TTS 插件冲突
    /// </summary>
    public void DetectOtherTTSPlugins()
    {
        try
        {
            _otherTTSPluginDetectionResult = OtherTTSPluginDetector.DetectOtherTTSPlugins(_mainWindow, _pluginName);

            if (_otherTTSPluginDetectionResult.HasOtherEnabledTTSPlugin)
            {
                // 软禁用：只设置标记，不修改用户设置
                _softDisabled = true;
            }
            else
            {
                _softDisabled = false;
            }
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出异常
            // 创建一个空的检测结果，因为属性是只读的
            _otherTTSPluginDetectionResult = null;
            _softDisabled = false;
        }
    }

    /// <summary>
    /// 实时检测是否应该跳过 TTS（软禁用检测）
    /// 注意：VPetTTS 不再避让 VPetLLM 内置 TTS，只检测其他真正的 TTS 插件冲突
    /// </summary>
    public bool ShouldSkipTTS()
    {
        try
        {
            // 重新检测其他 TTS 插件状态（不包括 VPetLLM）
            var result = OtherTTSPluginDetector.DetectOtherTTSPlugins(_mainWindow, _pluginName);
            var shouldSkip = result.HasOtherEnabledTTSPlugin;

            // 更新软禁用状态
            if (shouldSkip != _softDisabled)
            {
                _softDisabled = shouldSkip;
            }

            return shouldSkip;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 刷新检测状态
    /// </summary>
    public void RefreshDetection()
    {
        DetectVPetLLMPlugin();
        DetectOtherTTSPlugins();
    }

    /// <summary>
    /// 获取 VPetLLM 检测结果
    /// </summary>
    public VPetLLMDetectionResult GetVPetLLMDetectionResult()
    {
        return _vpetLLMDetectionResult;
    }

    /// <summary>
    /// 获取其他 TTS 插件检测结果
    /// </summary>
    public OtherTTSPluginDetectionResult GetOtherTTSPluginDetectionResult()
    {
        return _otherTTSPluginDetectionResult;
    }
}
