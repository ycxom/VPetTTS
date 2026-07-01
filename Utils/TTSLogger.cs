namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// VPetTTS 内部日志器。
    /// 所有日志仅保留在插件内部（内存缓冲 + 调试窗口），绝不写入 Console。
    /// 这样可以避免日志被 VPet 主程序捕获（VPet 会重定向 Console.Out）并输出到 VPet 日志。
    /// </summary>
    public static class TTSLogger
    {
        /// <summary>
        /// 内存缓冲区最大保留条目数（防止内存无限增长）。
        /// 从程序启动起即开始缓存日志，最多保留 1000 行，超出后丢弃最早的记录。
        /// </summary>
        private const int MaxEntries = 1000;

        private static readonly object _lock = new object();
        private static readonly Queue<string> _buffer = new Queue<string>(MaxEntries);

        /// <summary>
        /// 有新日志时触发（参数为原始日志消息，不含窗口时间戳）。
        /// 调试窗口订阅此事件以实时显示日志。
        /// </summary>
        public static event Action<string> OnLog;

        /// <summary>
        /// 记录一条日志。仅保留在插件内部，绝不写入 Console / VPet。
        /// </summary>
        public static void Log(string message)
        {
            message ??= string.Empty;

            lock (_lock)
            {
                _buffer.Enqueue(message);
                while (_buffer.Count > MaxEntries)
                    _buffer.Dequeue();
            }

            // 通知订阅者（如调试窗口）。异常隔离，避免影响调用方或产生递归。
            var handler = OnLog;
            if (handler != null)
            {
                try { handler(message); }
                catch { /* 忽略订阅者异常 */ }
            }
        }

        /// <summary>
        /// 获取当前缓冲区中的所有日志快照（用于调试窗口初始化时回填历史日志）。
        /// </summary>
        public static string[] GetSnapshot()
        {
            lock (_lock)
            {
                return _buffer.ToArray();
            }
        }

        /// <summary>
        /// 清空日志缓冲区。
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }
    }
}
