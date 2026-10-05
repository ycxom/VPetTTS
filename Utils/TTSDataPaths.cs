namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// TTS 插件用户数据的存放位置。
    ///
    /// 设置/状态放 文档\VPetLLM\TTS：那是用户数据，要与 VPetLLM（FreeConfig）、
    /// LLM表情包（Emotion）的存放策略一致。
    ///
    /// 音频缓存放 %TEMP%\VPetTTS\Cache：缓存不是用户数据，放文档目录的问题是
    /// 用户卸载插件后几百 MB 残留没人清；%Temp% 由系统管理生命周期（磁盘清理/
    /// 存储感知会回收），丢了也只是重新下载，何况缓存本身就有 7 天过期策略。
    ///
    /// 已发布版本把缓存和状态都放在 GraphCore.CachePath（VPet 安装目录\cache）——
    /// 宿主的图形缓存区，任一 MOD 的 info.lps cachedate 前进时整个目录会被清空，说没就没。
    /// 首次启动时从那里一次性迁出。
    /// </summary>
    public static class TTSDataPaths
    {
        /// <summary>TTS 插件数据目录：文档\VPetLLM\TTS</summary>
        public static string DataRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VPetLLM", "TTS");

        /// <summary>TTS 音频缓存目录：%TEMP%\VPetTTS\Cache</summary>
        public static string CacheDir { get; } = Path.Combine(Path.GetTempPath(), "VPetTTS", "Cache");

        /// <summary>状态持久化文件：文档\VPetLLM\TTS\tts_state.lps</summary>
        public static string StateFile { get; } = Path.Combine(DataRoot, "tts_state.lps");

        /// <summary>设置文件（JSON，密钥字段加密）：文档\VPetLLM\TTS\settings.json</summary>
        public static string SettingsFile { get; } = Path.Combine(DataRoot, "settings.json");

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

        // 已发布版本的位置：宿主图形缓存区
        private static string LegacyCacheDir => GraphCore.CachePath + @"\tts";
        private static string LegacyStateFile => Path.Combine(GraphCore.CachePath, "tts_state.lps");

        /// <summary>
        /// 一次性迁移：状态文件挪到文档下，音频缓存挪到 %Temp% 下。
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

                // 音频缓存：整目录搬。Directory.Move 要求目标父目录已存在
                // （%TEMP%\VPetTTS 没人建过），先补上父目录再搬。失败（跨卷、文件被 mpv 占用、
                // 杀软扫描）就不搬，不做跨卷复制 —— 几百 MB 的缓存不值得在启动时同步拷
                if (!Directory.Exists(CacheDir) && Directory.Exists(LegacyCacheDir))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(CacheDir));
                        Directory.Move(LegacyCacheDir, CacheDir);
                        TTSLogger.Log($"TTSDataPaths: TTS 音频缓存已迁移到 {CacheDir}");
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
