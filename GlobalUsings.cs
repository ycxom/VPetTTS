// Global using statements for VPetTTS
// 这个文件定义了项目中常用的命名空间，减少每个文件中的 using 语句

// ============================================================================
// System namespaces - 系统命名空间
// ============================================================================
// ============================================================================
// Third-party libraries - 第三方库
// ============================================================================
global using LinePutScript;
global using LinePutScript.Converter;
global using LinePutScript.Localization.WPF;
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Threading;
// ============================================================================
// VPetTTS Core - 核心功能命名空间
// ============================================================================
global using Vpet.Plugin.CustomTTS.Core;
global using Vpet.Plugin.CustomTTS.Core.Preload;
global using Vpet.Plugin.CustomTTS.Core.Providers;
// ============================================================================
// VPetTTS Services - 服务层命名空间
// ============================================================================

// Player services - 播放器服务
global using Vpet.Plugin.CustomTTS.Services.Player;

// TTS services - TTS 服务
global using Vpet.Plugin.CustomTTS.Services.TTS;

// Plugin services - 插件服务
global using Vpet.Plugin.CustomTTS.Services.Plugin;

// Initialization services - 初始化服务
global using Vpet.Plugin.CustomTTS.Services.Initialization;

// Testing services - 测试服务
global using Vpet.Plugin.CustomTTS.Services.Testing;

// Lifecycle services - 生命周期服务
global using Vpet.Plugin.CustomTTS.Services.Lifecycle;

// ============================================================================
// VPetTTS Utils - 工具类命名空间
// ============================================================================
global using Vpet.Plugin.CustomTTS.Utils;
global using VPet_Simulator.Core;
// ============================================================================
// VPet Core - VPet 核心命名空间
// ============================================================================
global using VPet_Simulator.Windows.Interface;
// 使用别名避免命名冲突
global using AudioPathHelper = Vpet.Plugin.CustomTTS.Utils.AudioPathHelper;
// Cache statistics
global using CacheStatistics = Vpet.Plugin.CustomTTS.Utils.CacheStatistics;
global using FreeConfigManager = Vpet.Plugin.CustomTTS.Utils.FreeConfigManager;
// Preload service
global using IPreloadService = Vpet.Plugin.CustomTTS.Core.Preload.IPreloadService;
// TTS state and events
global using ITTSState = Vpet.Plugin.CustomTTS.Core.ITTSState;
global using IVPetLLMTTSCoordinator = Vpet.Plugin.CustomTTS.Core.IVPetLLMTTSCoordinator;
global using MpvPlayer = Vpet.Plugin.CustomTTS.Utils.MpvPlayer;
global using OtherTTSPluginDetectionResult = Vpet.Plugin.CustomTTS.Utils.OtherTTSPluginDetectionResult;
global using OtherTTSPluginDetector = Vpet.Plugin.CustomTTS.Utils.OtherTTSPluginDetector;
global using PlayerChangedEventArgs = Vpet.Plugin.CustomTTS.Utils.PlayerChangedEventArgs;
global using PlayerDetailInfo = Vpet.Plugin.CustomTTS.Utils.PlayerDetailInfo;
global using PlayerErrorHandler = Vpet.Plugin.CustomTTS.Utils.PlayerErrorHandler;
global using PlayerErrorRecord = Vpet.Plugin.CustomTTS.Utils.PlayerErrorRecord;
global using PlayerErrorStatistics = Vpet.Plugin.CustomTTS.Utils.PlayerErrorStatistics;
global using PlayerStatus = Vpet.Plugin.CustomTTS.Utils.PlayerStatus;
// ============================================================================
// VPetTTS Models - 模型和数据类型
// ============================================================================

// Player types and status
global using PlayerType = Vpet.Plugin.CustomTTS.Utils.PlayerType;
global using PreloadService = Vpet.Plugin.CustomTTS.Core.Preload.PreloadService;
global using ProcessStatus = Vpet.Plugin.CustomTTS.Utils.ProcessStatus;
global using RequestSignatureHelper = Vpet.Plugin.CustomTTS.Utils.RequestSignatureHelper;
// Test results
global using SystemTestResult = Vpet.Plugin.CustomTTS.Utils.SystemTestResult;
global using TTSCacheManager = Vpet.Plugin.CustomTTS.Utils.TTSCacheManager;
global using TTSOperationStage = Vpet.Plugin.CustomTTS.Core.TTSOperationStage;
global using TTSStateChangedEventArgs = Vpet.Plugin.CustomTTS.Core.TTSStateChangedEventArgs;
// Detection results
global using VPetLLMDetectionResult = Vpet.Plugin.CustomTTS.Utils.VPetLLMDetectionResult;
global using VPetLLMDetector = Vpet.Plugin.CustomTTS.Utils.VPetLLMDetector;
global using VPetLLMTTSCoordinator = Vpet.Plugin.CustomTTS.Core.VPetLLMTTSCoordinator;

// ============================================================================
// Notes - 注意事项
// ============================================================================
// 1. 此文件使用 C# 10+ 的 global using 特性
// 2. 需要在 .csproj 中设置 <LangVersion>10.0</LangVersion> 或更高
// 3. 所有项目文件都会自动包含这些命名空间
// 4. 使用别名（如 IPlayerManager = ...）可以避免命名冲突
// 5. 按功能分组组织，便于维护和查找
