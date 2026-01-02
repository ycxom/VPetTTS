using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 基于 mpv.exe 的媒体播放器
    /// 支持高码率音频播放
    /// </summary>
    public class MpvPlayer : IDisposable
    {
        private Process _process;
        private readonly object _lock = new object();
        private bool _isPlaying = false;
        private readonly string _mpvExePath;
        private double _volume = 100.0;

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _isPlaying;
                }
            }
        }

        public MpvPlayer(string mpvExePath)
        {
            _mpvExePath = mpvExePath;

            if (!File.Exists(_mpvExePath))
            {
                throw new FileNotFoundException($"mpv.exe 未找到: {_mpvExePath}");
            }

            Console.WriteLine($"[VPetTTS] mpv 播放器初始化成功: {_mpvExePath}");
        }

        /// <summary>
        /// 播放音频文件
        /// </summary>
        public async Task PlayAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine($"[VPetTTS] mpv: 文件不存在: {filePath}");
                return;
            }

            try
            {
                lock (_lock)
                {
                    _isPlaying = true;
                }

                // 构建命令行参数
                var args = $"--no-video --volume={_volume} \"{filePath}\"";

                // 创建进程启动信息
                var startInfo = new ProcessStartInfo
                {
                    FileName = _mpvExePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _process = new Process { StartInfo = startInfo };
                _process.Start();

                // 等待进程结束
                await _process.WaitForExitAsync();

                lock (_lock)
                {
                    _isPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] mpv 播放错误: {ex.Message}");
                lock (_lock)
                {
                    _isPlaying = false;
                }
            }
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(1000);
                }

                lock (_lock)
                {
                    _isPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] mpv 停止播放错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置音量
        /// </summary>
        public void SetVolume(double volume)
        {
            _volume = Math.Clamp(volume, 0.0, 100.0);
        }

        public void Dispose()
        {
            try
            {
                Stop();
                _process?.Dispose();
                _process = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VPetTTS] mpv 释放资源错误: {ex.Message}");
            }
        }
    }
}
