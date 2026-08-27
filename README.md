# VPetTTS

桌宠说话的时候附带语音，支持多种 TTS 服务。

## 目录

- [功能特性](#功能特性)
- [支持的 TTS 提供商](#支持的-tts-提供商)
- [快速开始](#快速开始)
- [API 接口文档](#api-接口文档)
  - [主插件入口 (VPetTTS)](#主插件入口-vpettts)
  - [TTS 状态接口 (ITTSState)](#tts-状态接口-ittsstate)
  - [VPetLLM 协调器接口 (IVPetLLMTTSCoordinator)](#vpetllm-协调器接口-ivpetllmttscoordinator)
  - [预加载服务接口 (IPreloadService)](#预加载服务接口-ipreloadservice)
  - [播放器管理接口 (IPlayerManager)](#播放器管理接口-iplayermanager)
  - [音频播放服务接口 (IAudioPlaybackService)](#音频播放服务接口-iaudioplaybackservice)
  - [TTS 处理服务接口 (ITTSProcessingService)](#tts-处理服务接口-ittsprocessingservice)
  - [TTS 管理器 (TTSManager)](#tts-管理器-ttsmanager)
  - [缓存管理器 (TTSCacheManager)](#缓存管理器-ttscachemanager)
- [配置说明](#配置说明)
- [事件参数](#事件参数)

## 功能特性

- 支持多种 TTS 服务提供商
- 音频缓存机制，减少重复请求
- 音频预加载功能，提升响应速度
- 独占会话管理，支持多插件协调
- 自动播放器检测与切换
- 括号过滤：括号内的动作描写只在气泡显示，不参与朗读
- 代理支持
- 云端屏蔽插件列表
- 日语界面本地化与日语语音合成

## 支持的 TTS 提供商

| 提供商 | 说明 |
|--------|------|
| Free | 免费 TTS 服务，支持多语言 |
| OpenAI | OpenAI TTS API (tts-1/tts-1-hd) |
| GPT-SoVITS | 本地 GPT-SoVITS TTS 服务 |
| URL | 自定义 URL TTS 服务 |
| DIY | 自定义 DIY TTS 服务 |

## 快速开始

1. 将插件放入 VPet 的 mod 目录
2. 启动 VPet，在 MOD 配置菜单中找到 VPetTTS 设置
3. 选择 TTS 提供商并配置相关参数
4. 启用 TTS 功能

### 日语支持

- 界面：当 VPet 使用日语界面时，插件会自动加载 `1102_VPetTTS/lang/ja.lps`。
- Free TTS：将 `TextLanguage` 设为 `"ja"`（或在设置界面选择“日语”）；`"auto"` 仍可自动检测文本语言。
- GPT-SoVITS：将 `TextLanguage` 设为 `"ja"` 可合成日语文本。`PromptLanguage` 表示参考音频提示文本的语言；日语提示文本使用 `"ja"`，若提示文本是其他语言，则应填写对应语言码。

## API 接口文档

### 主插件入口 (VPetTTS)

主插件类，提供所有公开接口的访问入口。

#### 公开属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `TTSState` | `ITTSState` | TTS 状态接口，供外部 mod 如 VPetLLM 访问 |
| `TTSCoordinator` | `IVPetLLMTTSCoordinator` | VPetLLM TTS 协调器，供 VPetLLM 插件使用 |
| `PreloadService` | `IPreloadService` | 音频预加载服务，供外部程序使用 |
| `UseMpvPlayer` | `bool` | 是否使用 mpv 播放器 |
| `CurrentPlayerType` | `PlayerType` | 当前播放器类型 |
| `IsSoftDisabled` | `bool` | 获取软禁用状态 |
| `DetectedOtherTTSPluginNames` | `string` | 检测到的其他 TTS 插件名称 |

#### 公开方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetPlayerStatus()` | `PlayerStatus` | 获取播放器状态 |
| `RefreshSoftDisableStatus()` | `void` | 刷新软禁用状态 |
| `UpdateBlockedPlugins(List<string>)` | `void` | 更新屏蔽插件列表 |

#### 公开事件

| 事件 | 事件参数类型 | 说明 |
|------|--------------|------|
| `StateChanged` | `TTSStateChangedEventArgs` | TTS 状态变化事件 |
| `PlayerChanged` | `PlayerChangedEventArgs` | 播放器状态变化事件 |

---

### TTS 状态接口 (ITTSState)

提供 TTS 状态信息，供外部 mod（如 VPetLLM）获取 TTS 状态。

#### 基本状态属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsProcessing` | `bool` | 是否正在处理 TTS 请求 |
| `IsDownloading` | `bool` | 是否正在下载音频 |
| `IsPlaying` | `bool` | 是否正在播放音频 |
| `IsEnabled` | `bool` | TTS 功能是否启用 |

#### 当前信息属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentProvider` | `string` | 当前使用的 TTS 提供商 |
| `CurrentText` | `string` | 当前正在处理的文本 |
| `Progress` | `double` | 当前操作进度 (0.0 - 1.0) |

#### 播放进度属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentAudioPath` | `string` | 当前播放的音频文件路径 |
| `PlaybackProgress` | `double` | 当前播放进度 (0.0 - 1.0) |
| `PlaybackPositionMs` | `long` | 当前播放位置（毫秒） |
| `AudioDurationMs` | `long` | 音频总时长（毫秒），-1 表示未知 |
| `PlaybackStartTime` | `DateTime` | 播放开始时间 |
| `EstimatedPlaybackEndTime` | `DateTime` | 预计播放结束时间 |
| `IsPlaybackComplete` | `bool` | 播放是否已完成 |
| `LastHeartbeatTime` | `DateTime` | 最后一次心跳时间 |

#### 错误状态属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `HasError` | `bool` | 是否存在错误 |
| `LastError` | `string` | 最后一次错误信息 |
| `LastErrorTime` | `DateTime` | 最后一次错误发生时间 |

#### 统计信息属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `TotalProcessed` | `int` | 总处理次数 |
| `TotalProcessingTime` | `TimeSpan` | 总处理时间 |

#### VPetLLM 协调相关属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `CanAcceptNewRequests` | `bool` | 是否可以接受新的 TTS 请求 |
| `PluginVersion` | `string` | 插件版本信息 |

#### 事件

| 事件 | 事件参数类型 | 说明 |
|------|--------------|------|
| `StateChanged` | `TTSStateChangedEventArgs` | 状态变化事件 |
| `ProcessingStarted` | `TTSProcessingEventArgs` | TTS 处理开始事件 |
| `ProcessingCompleted` | `TTSProcessingEventArgs` | TTS 处理完成事件 |
| `DownloadStarted` | `TTSDownloadEventArgs` | 音频下载开始事件 |
| `DownloadCompleted` | `TTSDownloadEventArgs` | 音频下载完成事件 |
| `PlaybackStarted` | `TTSPlaybackEventArgs` | 音频播放开始事件 |
| `PlaybackCompleted` | `TTSPlaybackEventArgs` | 音频播放完成事件 |
| `ErrorOccurred` | `TTSErrorEventArgs` | 错误发生事件 |
| `AvailabilityChanged` | `TTSAvailabilityEventArgs` | TTS 可用性变化事件 |

---

### VPetLLM 协调器接口 (IVPetLLMTTSCoordinator)

供 VPetLLM 插件使用，用于协调 TTS 功能的使用。

#### 可用性检查方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `IsVPetTTSAvailable()` | `bool` | 检查 VPetTTS 是否可用且启用 |
| `CanAcceptNewRequests()` | `bool` | 检查是否可以接受新的请求 |
| `GetTTSState()` | `ITTSState` | 获取当前 TTS 状态 |

#### 请求管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `RequestTTSUsageAsync(string requestId, string text)` | `Task<bool>` | 请求使用 VPetTTS |
| `ReleaseTTSUsage(string requestId)` | `void` | 释放 TTS 使用权 |

#### 监控方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `StartMonitoring()` | `void` | 开始监听 VPetTTS 状态变化 |
| `StopMonitoring()` | `void` | 停止监听 VPetTTS 状态变化 |

#### 独占会话管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `StartExclusiveSessionAsync(string callerId)` | `Task<string>` | 启动独占会话，返回会话 ID |
| `EndExclusiveSessionAsync(string callerId, string sessionId)` | `Task` | 结束独占会话 |
| `IsExclusiveSession()` | `bool` | 检查是否处于独占会话 |
| `GetExclusiveOwner()` | `string?` | 获取独占会话所有者 |
| `GetCurrentSessionId()` | `string?` | 获取当前会话 ID |
| `GetActiveRequestCount()` | `int` | 获取当前活跃请求数 |
| `ForceCleanupSession()` | `void` | 强制清理会话 |

#### TTS 请求方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `SubmitTTSAsync(string text, string sessionId)` | `Task<string>` | 提交 TTS 请求，返回请求 ID |
| `ValidateRequest(string requestId, string sessionId)` | `bool` | 验证请求有效性 |
| `IsRequestCompleteAsync(string requestId)` | `Task<bool>` | 检查请求是否完成 |
| `IsProcessing()` | `bool` | 检查是否正在处理 |

#### 预加载方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `PreloadAsync(string text, string sessionId)` | `Task<bool>` | 预加载文本 |
| `IsPreloaded(string text)` | `bool` | 检查文本是否已预加载 |

#### 事件

| 事件 | 事件参数类型 | 说明 |
|------|--------------|------|
| `VPetTTSAvailabilityChanged` | `TTSAvailabilityEventArgs` | VPetTTS 可用性变化事件 |
| `VPetTTSStateChanged` | `TTSStateChangedEventArgs` | VPetTTS 状态变化事件 |

---

### 预加载服务接口 (IPreloadService)

提供音频预加载功能，允许外部程序提前下载并缓存音频文件。

#### 预加载方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `PreloadAudioAsync(string text, string requestId, CancellationToken)` | `Task<PreloadResult>` | 异步预加载单个音频 |
| `PreloadBatchAsync(IEnumerable<PreloadRequest>, int maxConcurrency, CancellationToken)` | `Task<IEnumerable<PreloadResult>>` | 批量预加载音频 |

#### 查询方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `IsPreloaded(string text)` | `bool` | 检查文本是否已预加载 |
| `GetPreloadStatus(string requestId)` | `PreloadStatus` | 获取预加载状态 |
| `GetPreloadedPath(string text)` | `string?` | 获取已预加载音频的缓存路径 |

#### 取消方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `CancelPreload(string requestId)` | `bool` | 取消指定预加载请求 |
| `CancelAllPreloads()` | `void` | 取消所有预加载请求 |

#### 统计和管理属性/方法

| 成员 | 类型 | 说明 |
|------|------|------|
| `ActiveTaskCount` | `int` | 当前活动的预加载任务数量 |
| `TotalRequestCount` | `int` | 总的预加载请求数量 |
| `CleanupCompletedTasks()` | `void` | 清理已完成的任务记录 |

#### 事件

| 事件 | 事件参数类型 | 说明 |
|------|--------------|------|
| `PreloadStarted` | `PreloadEventArgs` | 预加载开始事件 |
| `PreloadCompleted` | `PreloadEventArgs` | 预加载完成事件 |
| `PreloadFailed` | `PreloadEventArgs` | 预加载失败事件 |
| `PreloadCancelled` | `PreloadEventArgs` | 预加载取消事件 |

---

### 播放器管理接口 (IPlayerManager)

负责播放器检测、初始化、切换和状态管理。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentPlayerType` | `PlayerType` | 当前播放器类型 |
| `UseMpvPlayer` | `bool` | 是否使用 mpv 播放器 |

#### 初始化和检测方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `Initialize()` | `void` | 初始化播放器管理器 |
| `RefreshDetection()` | `void` | 刷新播放器检测 |

#### 播放器管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `SwitchToFallbackPlayerAsync(string reason)` | `Task` | 切换到备用播放器 |
| `GetBestAvailablePlayer()` | `PlayerType` | 获取最佳可用播放器 |
| `CheckPlayerAvailabilityAsync()` | `Task` | 检查播放器可用性 |

#### 状态查询方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetPlayerStatus()` | `PlayerStatus` | 获取播放器状态 |
| `GetPlayerDetailInfo()` | `PlayerDetailInfo` | 获取播放器详细信息 |
| `GetPlayerStatusDescription()` | `string` | 获取播放器状态描述 |
| `GetPlayerRecommendation()` | `string` | 获取播放器推荐信息 |

#### 音量管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `UpdateVolume(double volume)` | `void` | 更新播放器音量 (0.0 - 1.0) |
| `SyncVolumeSettings()` | `void` | 同步音量设置到所有播放器 |

#### 错误管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetPlayerErrorStatistics()` | `PlayerErrorStatistics` | 获取播放器错误统计 |
| `GetRecentPlayerErrors(int count)` | `List<PlayerErrorRecord>` | 获取最近的播放器错误记录 |
| `ExportPlayerErrorReport()` | `string` | 导出播放器错误报告 |
| `ClearPlayerErrorHistory()` | `void` | 清除播放器错误历史 |

#### 事件

| 事件 | 事件参数类型 | 说明 |
|------|--------------|------|
| `PlayerChanged` | `PlayerChangedEventArgs` | 播放器变化事件 |

---

### 音频播放服务接口 (IAudioPlaybackService)

负责音频文件验证、播放和状态跟踪。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsPlaying` | `bool` | 是否正在播放 |

#### 方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `PlayAudioAsync(string path)` | `Task` | 播放音频文件 |
| `StopAsync()` | `Task` | 停止当前播放 |

---

### TTS 处理服务接口 (ITTSProcessingService)

负责 TTS 请求处理、缓存管理和音频生成。

#### TTS 处理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `ProcessTTSRequestAsync(string text)` | `Task` | 处理 TTS 请求 |

#### 缓存管理方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `CheckCacheAsync(string text)` | `Task<string>` | 检查缓存，返回缓存路径或 null |
| `GenerateAndCacheAudioAsync(string text)` | `Task<string>` | 生成音频并缓存，返回音频文件路径 |

#### 音频生成方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GenerateAudioAsync(string text)` | `Task<byte[]>` | 生成音频数据 |

---

### TTS 管理器 (TTSManager)

TTS 管理器，负责管理 TTS 提供商和音频生成。

#### 方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GenerateAudioAsync(string text)` | `Task<byte[]?>` | 生成音频 |
| `GetAvailableProviders()` | `List<string>` | 获取可用提供商列表 |
| `SwitchProvider(string providerName)` | `void` | 切换提供商 |
| `RefreshSettings()` | `void` | 刷新设置 |

---

### 缓存管理器 (TTSCacheManager)

TTS 缓存管理器，实现基于最后访问时间的自动清理策略。

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `ExpirationTime` | `TimeSpan` | 缓存过期时间（默认7天） |
| `CleanupInterval` | `TimeSpan` | 清理检查间隔（默认1小时） |

#### 方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetCachePath(string cacheKey)` | `string` | 获取缓存文件路径 |
| `HasCache(string cacheKey)` | `bool` | 检查缓存是否存在 |
| `SaveToCacheAsync(string cacheKey, byte[] audioData)` | `Task` | 保存到缓存 |
| `UpdateAccessTime(string cacheKey)` | `void` | 更新访问时间 |
| `CleanupExpiredCache()` | `int` | 清理过期缓存，返回删除数量 |

---

## 配置说明

### Setting 类

主配置类，包含所有 TTS 相关设置。

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enable` | `bool` | `true` | 启用 TTS |
| `Provider` | `string` | `"Free"` | 当前 TTS 提供商 |
| `Volume` | `double` | `100.0` | 音量 (0-200) |
| `Speed` | `double` | `1.0` | 语速 (0.1-3.0) |
| `EnableCache` | `bool` | `true` | 启用缓存 |
| `RequestTimeout` | `int` | `30` | 请求超时时间（秒） |
| `Proxy` | `ProxySetting` | - | 代理设置 |
| `Free` | `FreeTTSSetting` | - | Free TTS 设置 |
| `OpenAI` | `OpenAITTSSetting` | - | OpenAI TTS 设置 |
| `GPTSoVITS` | `GPTSoVITSTTSSetting` | - | GPT-SoVITS 设置 |
| `URL` | `URLTTSSetting` | - | URL TTS 设置 |
| `DIY` | `DIYTTSSetting` | - | DIY TTS 设置 |
| `BlockedPlugins` | `List<string>` | - | 屏蔽的插件名称列表 |
| `CloudBanAllowedMods` | `List<string>` | - | 用户允许的云端屏蔽 mod ID |

### ProxySetting 代理设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `IsEnabled` | `bool` | `false` | 是否启用代理 |
| `FollowSystemProxy` | `bool` | `false` | 是否跟随系统代理 |
| `Protocol` | `string` | `"http"` | 代理协议 |
| `Address` | `string` | `"127.0.0.1:8080"` | 代理地址 |
| `ForAllAPI` | `bool` | `false` | 对所有 API 使用代理 |
| `ForTTS` | `bool` | `true` | 对 TTS 使用代理 |

### FreeTTSSetting Free TTS 设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `TextLanguage` | `string` | `"auto"` | 文本语言代码 (`auto`/`zh`/`en`/`ja`/`yue`/`ko`)，日语使用 `ja` |

### OpenAITTSSetting OpenAI TTS 设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ApiKey` | `string` | `""` | API Key |
| `BaseUrl` | `string` | `"https://api.openai.com/v1"` | API Base URL |
| `Model` | `string` | `"tts-1"` | 模型名称 |
| `Voice` | `string` | `"alloy"` | 语音 |
| `Format` | `string` | `"mp3"` | 音频格式 |

### GPTSoVITSTTSSetting GPT-SoVITS 设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BaseUrl` | `string` | `"http://127.0.0.1:9880"` | 服务地址 |
| `ApiMode` | `string` | `"WebUI"` | API 模式 (WebUI/ApiV2) |
| `ModelName` | `string` | `""` | 模型名称 |
| `ReferWavPath` | `string` | `""` | 参考音频路径 |
| `PromptText` | `string` | `""` | 提示文本 |
| `TextLanguage` | `string` | `"zh"` | 待合成文本的语言代码，日语使用 `ja` |
| `PromptLanguage` | `string` | `"zh"` | 参考音频提示文本的语言代码，日语使用 `ja` |
| `Temperature` | `double` | `1.0` | 温度 |
| `Speed` | `double` | `1.0` | 语速 |

### URLTTSSetting URL TTS 设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BaseUrl` | `string` | `""` | 基础 URL |
| `Voice` | `string` | `"36"` | 语音 ID |
| `Method` | `string` | `"GET"` | HTTP 方法 |

### DIYTTSSetting DIY TTS 设置

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BaseUrl` | `string` | `""` | 基础 URL |
| `Method` | `string` | `"POST"` | HTTP 方法 |
| `ContentType` | `string` | `"application/json"` | Content-Type |
| `RequestBody` | `string` | `""` | 请求体模板 |
| `CustomHeaders` | `List<CustomHeader>` | - | 自定义请求头 |
| `ResponseFormat` | `string` | `"mp3"` | 响应格式 |

---

## 事件参数

### TTSStateChangedEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `PropertyName` | `string` | 变化的属性名称 |
| `OldValue` | `object` | 旧值 |
| `NewValue` | `object` | 新值 |
| `Timestamp` | `DateTime` | 时间戳 |

### TTSProcessingEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Text` | `string` | 处理的文本内容 |
| `Provider` | `string` | 使用的 TTS 提供商 |
| `Timestamp` | `DateTime` | 时间戳 |
| `Duration` | `TimeSpan?` | 处理耗时 |
| `IsSuccess` | `bool` | 是否成功 |

### TTSDownloadEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Url` | `string` | 下载 URL |
| `Progress` | `double` | 下载进度 (0.0 - 1.0) |
| `Timestamp` | `DateTime` | 时间戳 |
| `BytesDownloaded` | `long` | 已下载字节数 |
| `TotalBytes` | `long` | 总字节数 |

### TTSPlaybackEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `AudioPath` | `string` | 音频文件路径 |
| `Duration` | `TimeSpan` | 音频时长 |
| `Timestamp` | `DateTime` | 时间戳 |
| `Text` | `string` | 播放的文本内容 |

### TTSErrorEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Error` | `string` | 错误信息 |
| `Exception` | `Exception` | 异常对象 |
| `Timestamp` | `DateTime` | 时间戳 |
| `Stage` | `TTSOperationStage` | 发生错误时的操作阶段 |
| `RelatedText` | `string` | 相关的文本内容 |

### TTSAvailabilityEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsAvailable` | `bool` | 是否可用 |
| `Reason` | `string` | 变化原因 |
| `Timestamp` | `DateTime` | 时间戳 |

### PlayerChangedEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `OldPlayerType` | `PlayerType` | 旧播放器类型 |
| `NewPlayerType` | `PlayerType` | 新播放器类型 |
| `Reason` | `string` | 切换原因 |
| `ChangeTime` | `DateTime` | 切换时间 |

### PreloadResult

| 属性 | 类型 | 说明 |
|------|------|------|
| `RequestId` | `string` | 请求标识符 |
| `Text` | `string` | 原始文本内容 |
| `Success` | `bool` | 预加载是否成功 |
| `CachePath` | `string?` | 缓存文件路径 |
| `ErrorMessage` | `string?` | 错误信息 |
| `Duration` | `TimeSpan` | 预加载操作耗时 |
| `WasCached` | `bool` | 是否命中缓存 |
| `StartTime` | `DateTime` | 预加载开始时间 |
| `EndTime` | `DateTime` | 预加载完成时间 |

### PlayerStatus

| 属性 | 类型 | 说明 |
|------|------|------|
| `Type` | `PlayerType` | 播放器类型 |
| `IsAvailable` | `bool` | 是否可用 |
| `IsPlaying` | `bool` | 是否正在播放 |
| `LastError` | `string` | 最后错误信息 |
| `LastErrorTime` | `DateTime` | 最后错误时间 |

### PlayerDetailInfo

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentPlayerType` | `PlayerType` | 当前播放器类型 |
| `IsPlayerAvailable` | `bool` | 播放器是否可用 |
| `PlayerStatusSummary` | `string` | 播放器状态摘要 |
| `VPetLLMPluginExists` | `bool` | VPetLLM 插件是否存在 |
| `MpvPlayerAvailable` | `bool` | mpv 播放器是否可用 |
| `MpvExePath` | `string` | mpv 可执行文件路径 |
| `MpvVersion` | `string` | mpv 版本 |
| `IsPlaying` | `bool` | 是否正在播放 |
| `LastError` | `string` | 最后错误信息 |
| `TotalErrors` | `int` | 总错误数 |

---

## 使用示例

### 获取 TTS 状态

```csharp
// 获取 VPetTTS 插件实例
var vpetTTS = MW.Main.Plugins.OfType<VPetTTS>().FirstOrDefault();

if (vpetTTS != null)
{
    var state = vpetTTS.TTSState;
    Console.WriteLine($"TTS 启用: {state.IsEnabled}");
    Console.WriteLine($"正在播放: {state.IsPlaying}");
    Console.WriteLine($"当前提供商: {state.CurrentProvider}");
}
```

### 订阅状态变化事件

```csharp
vpetTTS.StateChanged += (sender, e) =>
{
    Console.WriteLine($"属性 {e.PropertyName} 从 {e.OldValue} 变为 {e.NewValue}");
};
```

### 使用预加载服务

```csharp
var preloadService = vpetTTS.PreloadService;

// 预加载单个音频
var result = await preloadService.PreloadAudioAsync("你好世界", "request-001");
if (result.Success)
{
    Console.WriteLine($"预加载成功: {result.CachePath}");
}

// 检查是否已预加载
if (preloadService.IsPreloaded("你好世界"))
{
    var path = preloadService.GetPreloadedPath("你好世界");
}
```

### 使用协调器进行独占会话

```csharp
var coordinator = vpetTTS.TTSCoordinator;

// 启动独占会话
var sessionId = await coordinator.StartExclusiveSessionAsync("MyPlugin");

try
{
    // 提交 TTS 请求
    var requestId = await coordinator.SubmitTTSAsync("你好", sessionId);
    
    // 等待完成
    while (!await coordinator.IsRequestCompleteAsync(requestId))
    {
        await Task.Delay(100);
    }
}
finally
{
    // 结束会话
    await coordinator.EndExclusiveSessionAsync("MyPlugin", sessionId);
}
```

## 许可证

请参阅 [LICENSE](LICENSE) 文件。
