# VPetTTS 音频预加载服务 API 文档

## 概述

音频预加载服务允许外部程序（如 VPetLLM）提前请求 TTS 服务器生成并下载音频文件，将其缓存到本地，但不立即播放。当后续需要播放该音频时，可以直接从缓存读取，实现零延迟播放。

## 快速验证

### 验证安装

```csharp
// 检查预加载服务是否可用
var vpetTTS = mainWindow.Plugins.FirstOrDefault(p => p.PluginName == "VPetTTS") as VPetTTS;
if (vpetTTS?.PreloadService is not null)
{
    Console.WriteLine("✓ 预加载服务已安装并可用");
    Console.WriteLine($"活动任务数: {vpetTTS.PreloadService.ActiveTaskCount}");
}
else
{
    Console.WriteLine("✗ 预加载服务不可用");
}
```

### 基本功能测试

```csharp
// 运行基本功能测试
var testResult = await VPet.Plugin.CustomTTS.Tests.TestRunner.RunAllTestsAsync();
if (testResult)
{
    Console.WriteLine("✓ 所有基本功能测试通过");
}
else
{
    Console.WriteLine("✗ 部分测试失败，请检查日志");
}
```

## 快速开始

### 获取预加载服务

```csharp
// 从 VPetTTS 插件获取预加载服务
var vpetTTS = mainWindow.Plugins.FirstOrDefault(p => p.PluginName == "VPetTTS") as VPetTTS;
IPreloadService preloadService = vpetTTS?.PreloadService;
```

### 基本使用示例

```csharp
// 预加载单个音频
var result = await preloadService.PreloadAudioAsync("你好，主人！", "request-001");

if (result.Success)
{
    Console.WriteLine($"预加载成功，缓存路径: {result.CachePath}");
}
else
{
    Console.WriteLine($"预加载失败: {result.ErrorMessage}");
}
```

## 接口定义

### IPreloadService

```csharp
public interface IPreloadService
{
    // 异步预加载单个音频
    Task<PreloadResult> PreloadAudioAsync(string text, string requestId, CancellationToken cancellationToken = default);
    
    // 批量预加载音频
    Task<IEnumerable<PreloadResult>> PreloadBatchAsync(IEnumerable<PreloadRequest> requests, int maxConcurrency = 3, CancellationToken cancellationToken = default);
    
    // 检查文本是否已预加载
    bool IsPreloaded(string text);
    
    // 获取预加载状态
    PreloadStatus GetPreloadStatus(string requestId);
    
    // 获取已预加载音频的缓存路径
    string? GetPreloadedPath(string text);
    
    // 取消指定预加载请求
    bool CancelPreload(string requestId);
    
    // 取消所有预加载请求
    void CancelAllPreloads();
    
    // 事件
    event EventHandler<PreloadEventArgs> PreloadStarted;
    event EventHandler<PreloadEventArgs> PreloadCompleted;
    event EventHandler<PreloadEventArgs> PreloadFailed;
    event EventHandler<PreloadEventArgs> PreloadCancelled;
}
```

## 方法详解

### PreloadAudioAsync

异步预加载单个音频文件。

**参数：**
| 参数 | 类型 | 说明 |
|-----|------|-----|
| text | string | 要转换为语音的文本 |
| requestId | string | 请求的唯一标识符 |
| cancellationToken | CancellationToken | 可选的取消令牌 |

**返回值：** `Task<PreloadResult>`

**示例：**
```csharp
var result = await preloadService.PreloadAudioAsync(
    "欢迎回来！", 
    Guid.NewGuid().ToString()
);
```

### PreloadBatchAsync

批量预加载多个音频文件，支持并发控制。

**参数：**
| 参数 | 类型 | 说明 |
|-----|------|-----|
| requests | IEnumerable\<PreloadRequest\> | 预加载请求集合 |
| maxConcurrency | int | 最大并发数，默认为 3 |
| cancellationToken | CancellationToken | 可选的取消令牌 |

**返回值：** `Task<IEnumerable<PreloadResult>>`

**示例：**
```csharp
var requests = new List<PreloadRequest>
{
    new PreloadRequest { Text = "早上好！", RequestId = "req-1" },
    new PreloadRequest { Text = "中午好！", RequestId = "req-2" },
    new PreloadRequest { Text = "晚上好！", RequestId = "req-3" }
};

var results = await preloadService.PreloadBatchAsync(requests, maxConcurrency: 2);

foreach (var result in results)
{
    Console.WriteLine($"{result.RequestId}: {(result.Success ? "成功" : "失败")}");
}
```

### IsPreloaded

检查指定文本的音频是否已缓存。

**参数：**
| 参数 | 类型 | 说明 |
|-----|------|-----|
| text | string | 要检查的文本 |

**返回值：** `bool` - 如果已缓存返回 true

**示例：**
```csharp
if (preloadService.IsPreloaded("你好"))
{
    Console.WriteLine("音频已缓存，可以即时播放");
}
```

### GetPreloadStatus

获取指定请求的预加载状态。

**参数：**
| 参数 | 类型 | 说明 |
|-----|------|-----|
| requestId | string | 请求标识符 |

**返回值：** `PreloadStatus` 枚举值

**示例：**
```csharp
var status = preloadService.GetPreloadStatus("request-001");
switch (status)
{
    case PreloadStatus.InProgress:
        Console.WriteLine("正在下载...");
        break;
    case PreloadStatus.Completed:
        Console.WriteLine("下载完成");
        break;
    case PreloadStatus.Failed:
        Console.WriteLine("下载失败");
        break;
}
```

### CancelPreload / CancelAllPreloads

取消预加载请求。

**示例：**
```csharp
// 取消单个请求
bool cancelled = preloadService.CancelPreload("request-001");

// 取消所有请求
preloadService.CancelAllPreloads();
```

## 数据模型

### PreloadRequest

```csharp
public class PreloadRequest
{
    public string Text { get; set; }      // 要转换的文本
    public string RequestId { get; set; } // 请求唯一标识符
    public object? Tag { get; set; }      // 可选的附加数据
}
```

### PreloadResult

```csharp
public class PreloadResult
{
    public string RequestId { get; set; }    // 请求标识符
    public string Text { get; set; }         // 原始文本
    public bool Success { get; set; }        // 是否成功
    public string? CachePath { get; set; }   // 缓存文件路径
    public string? ErrorMessage { get; set; } // 错误信息
    public TimeSpan Duration { get; set; }   // 耗时
    public bool WasCached { get; set; }      // 是否命中缓存
}
```

### PreloadStatus

```csharp
public enum PreloadStatus
{
    Unknown,      // 未知（未找到该请求）
    Pending,      // 等待中
    InProgress,   // 进行中
    Completed,    // 已完成
    Failed,       // 失败
    Cancelled     // 已取消
}
```

### PreloadEventArgs

```csharp
public class PreloadEventArgs : EventArgs
{
    public string RequestId { get; set; }
    public string Text { get; set; }
    public PreloadStatus Status { get; set; }
    public string? CachePath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
}
```

## 事件使用

### 订阅事件

```csharp
preloadService.PreloadStarted += (sender, e) =>
{
    Console.WriteLine($"开始预加载: {e.RequestId}");
};

preloadService.PreloadCompleted += (sender, e) =>
{
    Console.WriteLine($"预加载完成: {e.RequestId}, 路径: {e.CachePath}");
};

preloadService.PreloadFailed += (sender, e) =>
{
    Console.WriteLine($"预加载失败: {e.RequestId}, 错误: {e.ErrorMessage}");
};

preloadService.PreloadCancelled += (sender, e) =>
{
    Console.WriteLine($"预加载已取消: {e.RequestId}");
};
```

## 最佳实践

### 1. 提前预加载

在用户可能需要播放音频之前提前预加载：

```csharp
// 例如：在 LLM 生成回复时，提前预加载可能的语音
async Task OnLLMResponseReceived(string response)
{
    // 异步预加载，不阻塞主流程
    _ = preloadService.PreloadAudioAsync(response, Guid.NewGuid().ToString());
}
```

### 2. 检查缓存避免重复请求

```csharp
async Task<string?> EnsureAudioPreloaded(string text)
{
    // 先检查是否已缓存
    if (preloadService.IsPreloaded(text))
    {
        return preloadService.GetPreloadedPath(text);
    }
    
    // 未缓存则预加载
    var result = await preloadService.PreloadAudioAsync(text, Guid.NewGuid().ToString());
    return result.Success ? result.CachePath : null;
}
```

### 3. 使用取消令牌

```csharp
var cts = new CancellationTokenSource();

// 设置超时
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    var result = await preloadService.PreloadAudioAsync(text, requestId, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("预加载超时或被取消");
}
```

### 4. 批量预加载对话

```csharp
// 预加载一系列可能的对话
var greetings = new[] { "早上好", "中午好", "晚上好", "晚安" };
var requests = greetings.Select((text, i) => new PreloadRequest
{
    Text = text,
    RequestId = $"greeting-{i}"
});

await preloadService.PreloadBatchAsync(requests, maxConcurrency: 2);
```

## 注意事项

1. **缓存有效期**：预加载的音频遵循 VPetTTS 的缓存策略（默认 7 天过期）
2. **缓存键**：缓存键基于文本内容和 TTS 设置生成，更改 TTS 设置后需要重新预加载
3. **并发限制**：批量预加载时建议设置合理的并发数，避免对 TTS 服务器造成过大压力
4. **内存管理**：预加载服务不会在内存中保留音频数据，仅保存到磁盘缓存
