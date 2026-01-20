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
        _plugin.GetType().GetField("_stateManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(_plugin, _stateManager);

        LogMessage("TTS 状态管理器已初始化");

        // 初始化 VPetLLM TTS 协调器
        _ttsCoordinator = new VPetLLMTTSCoordinator(_stateManager);

        // 将协调器设置到插件中
        _plugin.GetType().GetField("_ttsCoordinator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(_plugin, _ttsCoordinator);

        LogMessage("VPetLLM TTS 协调器已初始化");
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
        _plugin.GetType().GetField("_cacheManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(_plugin, _cacheManager);

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
        _plugin.GetType().GetField("_preloadService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(_plugin, _preloadService);

        LogMessage("音频预加载服务已初始化");
    }

    /// <summary>
    /// 记录日志消息
    /// </summary>
    private void LogMessage(string message)
    {
        // 使用插件的日志方法
        var logMethod = _plugin.GetType().GetMethod("LogMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        logMethod?.Invoke(_plugin, new object[] { message });
    }
}
