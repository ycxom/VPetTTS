using System;
using System.Threading.Tasks;

namespace Vpet.Plugin.CustomTTS.Core
{
    /// <summary>
    /// VPetLLM TTS 协调接口
    /// 供 VPetLLM 插件使用，用于协调 TTS 功能的使用
    /// </summary>
    public interface IVPetLLMTTSCoordinator
    {
        /// <summary>
        /// 检查 VPetTTS 是否可用且启用
        /// </summary>
        /// <returns>如果 VPetTTS 可用且启用返回 true</returns>
        bool IsVPetTTSAvailable();

        /// <summary>
        /// 检查 VPetTTS 是否可以接受新的请求
        /// </summary>
        /// <returns>如果可以接受新请求返回 true</returns>
        bool CanAcceptNewRequests();

        /// <summary>
        /// 获取当前 TTS 状态
        /// </summary>
        /// <returns>TTS 状态接口</returns>
        ITTSState GetTTSState();

        /// <summary>
        /// 请求使用 VPetTTS（返回是否成功获得使用权）
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        /// <param name="text">要处理的文本</param>
        /// <returns>是否成功获得使用权</returns>
        Task<bool> RequestTTSUsageAsync(string requestId, string text);

        /// <summary>
        /// 释放 TTS 使用权
        /// </summary>
        /// <param name="requestId">请求标识符</param>
        void ReleaseTTSUsage(string requestId);

        /// <summary>
        /// 开始监听 VPetTTS 状态变化
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// 停止监听 VPetTTS 状态变化
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// VPetTTS 可用性变化事件
        /// </summary>
        event EventHandler<TTSAvailabilityEventArgs> VPetTTSAvailabilityChanged;

        /// <summary>
        /// VPetTTS 状态变化事件
        /// </summary>
        event EventHandler<TTSStateChangedEventArgs> VPetTTSStateChanged;
    }

    /// <summary>
    /// VPetLLM TTS 协调器实现
    /// 用于 VPetLLM 插件与 VPetTTS 插件之间的协调
    /// </summary>
    public class VPetLLMTTSCoordinator : IVPetLLMTTSCoordinator
    {
        private readonly ITTSState _ttsState;
        private bool _isMonitoring;
        private readonly object _lockObject = new object();

        public event EventHandler<TTSAvailabilityEventArgs> VPetTTSAvailabilityChanged;
        public event EventHandler<TTSStateChangedEventArgs> VPetTTSStateChanged;

        public VPetLLMTTSCoordinator(ITTSState ttsState)
        {
            _ttsState = ttsState ?? throw new ArgumentNullException(nameof(ttsState));
        }

        public bool IsVPetTTSAvailable()
        {
            return _ttsState != null && _ttsState.IsEnabled;
        }

        public bool CanAcceptNewRequests()
        {
            return _ttsState != null && _ttsState.CanAcceptNewRequests;
        }

        public ITTSState GetTTSState()
        {
            return _ttsState;
        }

        public async Task<bool> RequestTTSUsageAsync(string requestId, string text)
        {
            if (!IsVPetTTSAvailable())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 不可用");
                return false;
            }

            if (!CanAcceptNewRequests())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 正忙");
                return false;
            }

            // 等待一小段时间确保状态稳定
            await Task.Delay(10);

            // 再次检查状态
            if (!CanAcceptNewRequests())
            {
                LogMessage($"请求 {requestId} 失败：VPetTTS 状态已变化");
                return false;
            }

            LogMessage($"请求 {requestId} 成功：可以使用 VPetTTS");
            return true;
        }

        public void ReleaseTTSUsage(string requestId)
        {
            LogMessage($"请求 {requestId} 已释放 TTS 使用权");
        }

        public void StartMonitoring()
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;

                _isMonitoring = true;

                // 订阅状态变化事件
                _ttsState.StateChanged += OnTTSStateChanged;
                _ttsState.AvailabilityChanged += OnTTSAvailabilityChanged;

                LogMessage("开始监听 VPetTTS 状态变化");
            }
        }

        public void StopMonitoring()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;

                _isMonitoring = false;

                // 取消订阅状态变化事件
                _ttsState.StateChanged -= OnTTSStateChanged;
                _ttsState.AvailabilityChanged -= OnTTSAvailabilityChanged;

                LogMessage("停止监听 VPetTTS 状态变化");
            }
        }

        private void OnTTSStateChanged(object sender, TTSStateChangedEventArgs e)
        {
            try
            {
                VPetTTSStateChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"处理状态变化事件时发生错误: {ex.Message}");
            }
        }

        private void OnTTSAvailabilityChanged(object sender, TTSAvailabilityEventArgs e)
        {
            try
            {
                VPetTTSAvailabilityChanged?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LogMessage($"处理可用性变化事件时发生错误: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            Console.WriteLine($"[VPetLLMTTSCoordinator] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
        }
    }
}
