namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// TTS 插件用户数据的统一存放位置：文档\VPetLLM\TTS。
    ///
    /// 过去音频缓存和状态文件都放在 GraphCore.CachePath（VPet 安装目录\cache）下，
    /// 那里是宿主的图形缓存区：任一 MOD 的 info.lps cachedate 前进时，宿主会把整个
    /// cache 目录清空，几百 MB 的语音缓存说没就没。挪到文档目录后不再受宿主清缓存
    /// 影响，与 VPetLLM（FreeConfig）、LLM表情包（Emotion）的存放策略一致。
    /// </summary>
    public static class TTSDataPaths
    {
        /// <summary>TTS 插件数据目录：文档\VPetLLM\TTS</summary>
        public static string DataRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VPetLLM", "TTS");

        /// <summary>TTS 音频缓存目录：文档\VPetLLM\TTS\Cache</summary>
        public static string CacheDir { get; } = Path.Combine(DataRoot, "Cache");

        /// <summary>状态持久化文件：文档\VPetLLM\TTS\tts_state.lps</summary>
        public static string StateFile { get; } = Path.Combine(DataRoot, "tts_state.lps");

        static TTSDataPaths()
        {
            try
            {
                Directory.CreateDirectory(DataRoot);
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"TTSDataPaths: 创建数据目录失败: {ex.Message}");
            }

            MigrateLegacyCacheData();
        }

        // 宿主缓存区里的旧位置
        private static string LegacyCacheDir => GraphCore.CachePath + @"\tts";
        private static string LegacyStateFile => Path.Combine(GraphCore.CachePath, "tts_state.lps");

        /// <summary>
        /// 一次性迁移：旧位置的缓存目录和状态文件整体挪到文档下。
        /// 首次访问本类时执行 —— TTSStateManager 的构造先于 CacheManager 初始化，
        /// 所以迁移完成后 LoadState 读到的就已经是新位置的文件。
        ///
        /// 任何失败都只记日志不抛出：缓存有 7 天过期策略，留在原地最多少用一次；
        /// 状态文件丢了也只是统计归零，都不值得为它阻塞插件启动。
        /// </summary>
        private static void MigrateLegacyCacheData()
        {
            try
            {
                // 状态文件：小文件，Move 失败（跨卷等）退回 Copy
                if (File.Exists(LegacyStateFile) && !File.Exists(StateFile))
                {
                    try
                    {
                        File.Move(LegacyStateFile, StateFile);
                        TTSLogger.Log("TTSDataPaths: tts_state.lps 已迁移到文档目录");
                    }
                    catch (Exception ex)
                    {
                        File.Copy(LegacyStateFile, StateFile);
                        TTSLogger.Log($"TTSDataPaths: tts_state.lps 跨卷复制到文档目录（Move 失败: {ex.Message}）");
                    }
                }

                // 音频缓存：整目录搬。失败（跨卷、文件被 mpv 占用、杀软扫描）就不搬，
                // 不做跨卷复制 —— 几百 MB 的缓存不值得在启动时同步拷
                if (Directory.Exists(LegacyCacheDir) && !Directory.Exists(CacheDir))
                {
                    try
                    {
                        Directory.Move(LegacyCacheDir, CacheDir);
                        TTSLogger.Log("TTSDataPaths: TTS 音频缓存已迁移到文档目录");
                    }
                    catch (Exception ex)
                    {
                        TTSLogger.Log($"TTSDataPaths: 缓存目录迁移失败（留在原地，之后自然过期）: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"TTSDataPaths: 迁移旧数据时出现意外错误: {ex.Message}");
            }
        }
    }
}
