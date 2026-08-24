namespace Vpet.Plugin.CustomTTS.Services.Initialization;

/// <summary>
/// 初始化服务
/// 负责插件各组件的初始化
/// </summary>
public class InitializationService : IInitializationService
{
    private readonly IMainWindow _mainWindow;
    private readonly Setting _settings;
    private readonly VPetTTS _plugin;

    // 组件引用（由 VPetTTS 传入或创建）
    private TTSStateManager _stateManager;
    private ExclusiveSessionManager _sessionManager;
    private VPetLLMTTSCoordinator _ttsCoordinator;
    private TTSCacheManager _cacheManager;
    private TTSManager _ttsManager;
    private PreloadService _preloadService;

    public InitializationService(IMainWindow mainWindow, Setting settings, VPetTTS plugin)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
    }

    /// <summary>
    /// 初始化所有组件
    /// </summary>
    public async Task InitializeAllAsync()
    {
        // 1. 初始化认证提供者
        InitializeAuthProviders();

        // 2. 初始化状态管理器
        InitializeStateManager();

        // 3. 初始化缓存管理器
        InitializeCacheManager();

        // 4. 异步初始化 Free TTS 配置
        await InitializeFreeTTSConfigAsync();

        // 5. 初始化 TTS 管理器
        InitializeTTSManager();

        // 6. 初始化预加载服务
        InitializePreloadService();
    }

    /// <summary>
    /// 初始化认证提供者
    /// </summary>
    public void InitializeAuthProviders()
    {
        try
        {
            Func<ulong> getSteamId = () =>
            {
                try { return _mainWindow?.SteamID ?? 0; } catch { return 0; }
            };

            Func<Task<int>> getAuthKey = async () =>
            {
                try { return _mainWindow is not null ? await _mainWindow.GenerateAuthKey() : 0; } catch { return 0; }
            };

            Func<string> getModId = () =>
            {
                try
                {
                    var dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (string.IsNullOrEmpty(dllPath)) return "";

                    foreach (var mod in _mainWindow.OnModInfo)
                    {
                        if (mod.Path is not null && dllPath.StartsWith(mod.Path.FullName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (mod.ItemID > 0)
                                return mod.ItemID.ToString();
                        }
                    }
                    return "";
                }
                catch { return ""; }
            };

            RequestSignatureHelper.Init(getSteamId, getAuthKey, getModId);
            LogMessage("认证签名助手初始化完成");
        }
        catch (Exception ex)
        {
            LogMessage($"初始化认证签名助手失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 初始化状态管理器
    /// </summary>
    public void InitializeStateManager()
    {
        _stateManager = new TTSStateManager(_settings);

        // 将状态管理器设置到插件中
        SetPluginField("_stateManager", _stateManager);

        LogMessage("TTS 状态管理器已初始化");

        // 初始化独占会话管理器
        _sessionManager = new ExclusiveSessionManager();

        // 将会话管理器设置到插件中
        SetPluginField("_sessionManager", _sessionManager);

        LogMessage("独占会话管理器已初始化");

        // 注意：VPetLLM TTS 协调器需要在预加载服务和 TTS 处理服务初始化后创建
        // 所以我们在 InitializePreloadService 中创建
    }

    /// <summary>
    /// 初始化缓存管理器
    /// </summary>
    public void InitializeCacheManager()
    {
        // 创建缓存目录
        var cachePath = GraphCore.CachePath + @"\tts";
        if (!Directory.Exists(cachePath))
            Directory.CreateDirectory(cachePath);

        // 初始化缓存管理器（7天过期策略）
        _cacheManager = new TTSCacheManager(cachePath);

        // 将缓存管理器设置到插件中
        SetPluginField("_cacheManager", _cacheManager);

        LogMessage("TTS 缓存管理器已初始化（7天过期策略）");
    }

    /// <summary>
    /// 异步初始化 Free TTS 配置
    /// </summary>
    public async Task InitializeFreeTTSConfigAsync()
    {
        try
        {
            await FreeConfigManager.InitializeTTSConfigAsync();
            LogMessage("Free TTS 配置初始化完成");
        }
        catch (Exception ex)
        {
            LogMessage($"Free TTS 配置初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 初始化 TTS 管理器
    /// </summary>
    public void InitializeTTSManager()
    {
        _ttsManager = new TTSManager(_settings);

        // 将 TTS 管理器设置到插件中
        _plugin.ttsManager = _ttsManager;

        LogMessage("TTS 管理器已初始化");
    }

    /// <summary>
    /// 初始化预加载服务
    /// </summary>
    public void InitializePreloadService()
    {
        _preloadService = new PreloadService(_ttsManager, _cacheManager, _settings);

        // 将预加载服务设置到插件中
        SetPluginField("_preloadService", _preloadService);

        LogMessage("音频预加载服务已初始化");

        // 现在初始化 VPetLLM TTS 协调器（需要所有依赖）
        // 注意：TTS 处理服务可能还未创建，所以传 null
        _ttsCoordinator = new VPetLLMTTSCoordinator(_stateManager, _sessionManager, _preloadService, null);

        // 将协调器设置到插件中
        SetPluginField("_ttsCoordinator", _ttsCoordinator);

        LogMessage("VPetLLM TTS 协调器已初始化");
    }

    /// <summary>
    /// 记录日志消息
    /// </summary>
    private void LogMessage(string message)
    {
        // 直接调用，不再走反射：VPetTTS.LogMessage 是 public，
        // 旧代码用 NonPublic 查找始终返回 null，本服务的日志从未输出过。
        _plugin.LogMessage(message);
    }

    /// <summary>
    /// 通过反射把组件回填到宿主插件的私有字段。
    /// 字段找不到时必须报错——静默 no-op 会让整条初始化链在下游以
    /// "依赖组件未初始化" 的形式失败，且无从追溯。
    /// </summary>
    private void SetPluginField(string fieldName, object value)
    {
        var field = _plugin.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (field is null)
            throw new InvalidOperationException(
                $"回填字段失败：在 {_plugin.GetType().FullName} 上找不到字段 '{fieldName}'（字段可能已被重命名）");

        field.SetValue(_plugin, value);
    }
}
