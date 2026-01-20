using Newtonsoft.Json;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// TTS 缓存管理器
    /// 实现基于最后访问时间的自动清理策略
    /// </summary>
    public class TTSCacheManager : IDisposable
    {
        private readonly string _cacheDir;
        private readonly string _metadataFile;
        private Dictionary<string, CacheEntry> _cacheMetadata;
        private readonly object _lock = new object();
        private Timer _cleanupTimer;
        private bool _disposed = false;

        /// <summary>
        /// 缓存过期时间（默认7天）
        /// </summary>
        public TimeSpan ExpirationTime { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// 清理检查间隔（默认1小时）
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

        public TTSCacheManager(string cacheDir)
        {
            _cacheDir = cacheDir ?? throw new ArgumentNullException(nameof(cacheDir));
            _metadataFile = Path.Combine(_cacheDir, "cache_metadata.json");

            // 确保缓存目录存在
            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }

            // 加载元数据
            LoadMetadata();

            // 启动定时清理
            StartCleanupTimer();

            LogMessage("TTS 缓存管理器已初始化");
        }

        /// <summary>
        /// 获取缓存文件路径，如果存在则更新访问时间
        /// </summary>
        public string GetCachePath(string cacheKey)
        {
            var filePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");

            lock (_lock)
            {
                if (File.Exists(filePath))
                {
                    // 更新访问时间
                    UpdateAccessTime(cacheKey);
                    return filePath;
                }
            }

            return null;
        }

        /// <summary>
        /// 检查缓存是否存在
        /// </summary>
        public bool HasCache(string cacheKey)
        {
            var filePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");
            return File.Exists(filePath);
        }

        /// <summary>
        /// 保存到缓存并记录元数据
        /// </summary>
        public async Task SaveToCacheAsync(string cacheKey, byte[] audioData)
        {
            var filePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");

            await File.WriteAllBytesAsync(filePath, audioData);

            lock (_lock)
            {
                _cacheMetadata[cacheKey] = new CacheEntry
                {
                    CacheKey = cacheKey,
                    CreatedTime = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    FileSize = audioData.Length
                };
                SaveMetadata();
            }

            LogMessage($"缓存已保存: {cacheKey} ({audioData.Length / 1024:F1} KB)");
        }

        /// <summary>
        /// 访问缓存时更新访问时间
        /// </summary>
        public void UpdateAccessTime(string cacheKey)
        {
            lock (_lock)
            {
                if (_cacheMetadata.TryGetValue(cacheKey, out var entry))
                {
                    entry.LastAccessTime = DateTime.Now;
                    SaveMetadata();
                }
                else
                {
                    // 文件存在但没有元数据，创建元数据
                    var filePath = Path.Combine(_cacheDir, $"{cacheKey}.mp3");
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        _cacheMetadata[cacheKey] = new CacheEntry
                        {
                            CacheKey = cacheKey,
                            CreatedTime = fileInfo.CreationTime,
                            LastAccessTime = DateTime.Now,
                            FileSize = fileInfo.Length
                        };
                        SaveMetadata();
                    }
                }
            }
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        public int CleanupExpiredCache()
        {
            int deletedCount = 0;
            var now = DateTime.Now;

            lock (_lock)
            {
                var expiredKeys = _cacheMetadata
                    .Where(kvp => now - kvp.Value.LastAccessTime > ExpirationTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    var filePath = Path.Combine(_cacheDir, $"{key}.mp3");
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            deletedCount++;
                        }
                        _cacheMetadata.Remove(key);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"删除过期缓存失败 {key}: {ex.Message}");
                    }
                }

                // 同时清理没有元数据的孤立文件
                deletedCount += CleanupOrphanedFiles();

                if (deletedCount > 0)
                {
                    SaveMetadata();
                    LogMessage($"已清理 {deletedCount} 个过期缓存文件");
                }
            }

            return deletedCount;
        }

        /// <summary>
        /// 清理孤立文件（有文件但没有元数据，且文件较旧）
        /// </summary>
        private int CleanupOrphanedFiles()
        {
            int deletedCount = 0;
            var now = DateTime.Now;

            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.mp3");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (!_cacheMetadata.ContainsKey(fileName))
                    {
                        var fileInfo = new FileInfo(file);
                        // 如果文件最后写入时间超过过期时间，删除它
                        if (now - fileInfo.LastWriteTime > ExpirationTime)
                        {
                            try
                            {
                                File.Delete(file);
                                deletedCount++;
                            }
                            catch { }
                        }
                        else
                        {
                            // 为较新的孤立文件创建元数据
                            _cacheMetadata[fileName] = new CacheEntry
                            {
                                CacheKey = fileName,
                                CreatedTime = fileInfo.CreationTime,
                                LastAccessTime = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"清理孤立文件时出错: {ex.Message}");
            }

            return deletedCount;
        }

        /// <summary>
        /// 清理所有缓存
        /// </summary>
        public void ClearAllCache()
        {
            lock (_lock)
            {
                try
                {
                    var files = Directory.GetFiles(_cacheDir, "*.mp3");
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                    _cacheMetadata.Clear();
                    SaveMetadata();
                    LogMessage("所有 TTS 缓存已清理");
                }
                catch (Exception ex)
                {
                    LogMessage($"清理所有缓存失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var stats = new CacheStatistics
                {
                    TotalFiles = _cacheMetadata.Count,
                    TotalSize = _cacheMetadata.Values.Sum(e => e.FileSize),
                    ExpiredFiles = _cacheMetadata.Values.Count(e => now - e.LastAccessTime > ExpirationTime),
                    OldestAccess = _cacheMetadata.Values.Any()
                        ? _cacheMetadata.Values.Min(e => e.LastAccessTime)
                        : DateTime.Now,
                    NewestAccess = _cacheMetadata.Values.Any()
                        ? _cacheMetadata.Values.Max(e => e.LastAccessTime)
                        : DateTime.Now
                };
                return stats;
            }
        }

        private void LoadMetadata()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_metadataFile))
                    {
                        var json = File.ReadAllText(_metadataFile);
                        _cacheMetadata = JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(json)
                            ?? new Dictionary<string, CacheEntry>();
                    }
                    else
                    {
                        _cacheMetadata = new Dictionary<string, CacheEntry>();
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"加载缓存元数据失败: {ex.Message}");
                    _cacheMetadata = new Dictionary<string, CacheEntry>();
                }
            }
        }

        private void SaveMetadata()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_cacheMetadata, Formatting.Indented);
                File.WriteAllText(_metadataFile, json);
            }
            catch (Exception ex)
            {
                LogMessage($"保存缓存元数据失败: {ex.Message}");
            }
        }

        private void StartCleanupTimer()
        {
            _cleanupTimer = new Timer(
                _ => CleanupExpiredCache(),
                null,
                CleanupInterval,
                CleanupInterval
            );
        }

        private void LogMessage(string message)
        {
            Console.WriteLine($"[TTSCacheManager] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();

            lock (_lock)
            {
                SaveMetadata();
            }
        }
    }

    /// <summary>
    /// 缓存条目
    /// </summary>
    public class CacheEntry
    {
        public string CacheKey { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public long FileSize { get; set; }
    }

    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public class CacheStatistics
    {
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public int ExpiredFiles { get; set; }
        public DateTime OldestAccess { get; set; }
        public DateTime NewestAccess { get; set; }

        public string TotalSizeFormatted => TotalSize < 1024 * 1024
            ? $"{TotalSize / 1024:F1} KB"
            : $"{TotalSize / 1024 / 1024:F1} MB";
    }
}
