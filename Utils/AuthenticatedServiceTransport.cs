using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 官方服务鉴权通道的可用状态。
    /// </summary>
    public enum ServiceTransportState
    {
        /// <summary>本 MOD 自带的原生组件已就位，可以发送官方服务请求。</summary>
        Ready,

        /// <summary>本 MOD 缺少自带的原生鉴权组件（部署不完整）。</summary>
        NativeMissing,

        /// <summary>原生组件在，但加载失败（架构不匹配或文件损坏）。</summary>
        NativeLoadFailed,
    }

    /// <summary>
    /// 官方内置服务的鉴权通道：插件只交出原始业务请求，身份采集、鉴权头和
    /// 应用层加解密由本 MOD 自带的 VPetLLM.SecureCommunication.dll 完成。
    ///
    /// 为什么自带而不是找 VPetLLM 宿主代发：原生模块的 mod_id 取自它自己所在的
    /// MOD 目录，借宿主的 DLL 会把身份报成 VPetLLM，服务端的 mod_id 白名单就分不清
    /// 是哪个 MOD 在调用。自带副本还让本插件不依赖用户是否装了 VPetLLM——
    /// 官方服务是可选功能，其余渠道（DIY / OpenAI / GPT-SoVITS / URL 等自备凭证）
    /// 任何时候都不受影响。
    ///
    /// 插件不得自己生成 X-Cache-Token / X-Request-Signature / X-Check-Key /
    /// X-Trace-Id：这些只在原生模块里产生，托管层只做搬运。
    ///
    /// 只用于官方服务；用户自填地址/凭证的第三方服务仍走各自的 HttpClient。
    /// </summary>
    public static class AuthenticatedServiceTransport
    {
        private const string NativeLibraryName = "VPetLLM.SecureCommunication.dll";

        private static readonly object Gate = new object();
        private static bool _probed;
        private static ServiceTransportState _state;
        private static string _nativePath;
        private static int _notified;

        /// <summary>当前状态。首次读取时探测原生组件是否在位。</summary>
        public static ServiceTransportState State
        {
            get { EnsureProbed(); lock (Gate) return _state; }
        }

        /// <summary>
        /// 鉴权通道是否可用。不可用时调用方应给出 <see cref="StatusMessage"/> 里的提示，
        /// 而不是继续发送没有鉴权的请求。
        /// </summary>
        public static bool IsAvailable
        {
            get { return State == ServiceTransportState.Ready; }
        }

        /// <summary>
        /// 可直接展示给用户的一句话：为什么不可用、下一步该做什么。
        /// </summary>
        public static string StatusMessage
        {
            get
            {
                EnsureProbed();
                lock (Gate)
                {
                    if (_state == ServiceTransportState.Ready)
                        return "官方服务鉴权通道已就绪（本 MOD 自带原生组件）";
                    if (_state == ServiceTransportState.NativeLoadFailed)
                        return $"原生鉴权组件加载失败：{_nativePath}（架构不匹配或文件损坏）。" +
                               "请重新安装或更新本 MOD。";
                    return $"本 MOD 缺少原生鉴权组件：{_nativePath}。" +
                           "插件包不完整（缺少 runtimes 目录），请重新安装或更新本 MOD。";
                }
            }
        }

        /// <summary>启动时调一次：探测原生组件并把结论写进日志。</summary>
        public static void Initialize()
        {
            lock (Gate)
            {
                _probed = false;
                _state = ServiceTransportState.Ready;
                _nativePath = null;
                _notified = 0;
            }
            EnsureProbed();
        }

        /// <summary>
        /// 把"官方服务用不了"这件事一次性告诉用户。首次触发弹一次提示框，之后只走日志。
        ///
        /// 为什么必须让用户看见：这是插件包缺文件（没装全/被杀软删了），静默失败会让人
        /// 以为功能坏了。为什么用独立对话框而不是桌宠气泡：气泡是"覆盖当前内容"的语义，
        /// 会把正在念的回复顶掉——宿主的独占守卫正是为此而存在，插件不该去抢。
        /// </summary>
        public static void NotifyUnavailableOnce(string feature)
        {
            // 先占位再去弹，避免同时几个请求各弹一次；弹不出来就放回去，
            // 否则一次环境异常会把后续所有提醒都锁死。
            if (Interlocked.CompareExchange(ref _notified, 1, 0) != 0)
                return;

            var shown = false;
            try
            {
                var prefix = string.IsNullOrEmpty(feature) ? "" : feature + "：";
                var text = prefix + StatusMessage;
                var dispatcher = Application.Current == null ? null : Application.Current.Dispatcher;
                if (dispatcher == null)
                    return;
                dispatcher.Invoke(() =>
                {
                    MessageBox.Show(text, "VPetTTS 官方服务", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                shown = true;
            }
            catch
            {
                // 提示失败不影响主流程；原因仍会随异常信息和日志给出
            }
            finally
            {
                if (!shown)
                    Volatile.Write(ref _notified, 0);
            }
        }

        /// <summary>
        /// 发送官方服务请求。返回的响应正文是逐帧验签后才解密的流，
        /// 调用方照常读取即可（不得复用同一个 HttpRequestMessage 重放）。
        /// </summary>
        public static async Task<HttpResponseMessage> SendAsync(
            HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (State != ServiceTransportState.Ready)
            {
                var reason = StatusMessage;
                NotifyUnavailableOnce(null);
                throw new InvalidOperationException(reason);
            }

            try
            {
                return await SecureCommunicationTransport.SendAsync(client, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNativeLoadFailure(ex))
            {
                lock (Gate)
                    _state = ServiceTransportState.NativeLoadFailed;
                NotifyUnavailableOnce(null);
                throw new InvalidOperationException(StatusMessage, ex);
            }
        }

        private static void EnsureProbed()
        {
            lock (Gate)
            {
                if (_probed)
                    return;
                _probed = true;
                _nativePath = ExpectedNativePath();
                _state = File.Exists(_nativePath)
                    ? ServiceTransportState.Ready
                    : ServiceTransportState.NativeMissing;
            }
        }

        /// <summary>
        /// 原生模块该在的位置，和解析器（<see cref="SecureCommunicationTransport"/>）用的是同一套规则。
        /// 探测结果只用来把话说清楚：文件在位却仍加载失败时，异常会自己说明原因。
        /// </summary>
        private static string ExpectedNativePath()
        {
            string directory;
            try
            {
                directory = Path.GetDirectoryName(typeof(AuthenticatedServiceTransport).Assembly.Location);
            }
            catch (Exception)
            {
                directory = null;
            }
            var runtime = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            return string.IsNullOrEmpty(directory)
                ? NativeLibraryName
                : Path.Combine(directory, "runtimes", runtime, "native", NativeLibraryName);
        }

        private static bool IsNativeLoadFailure(Exception ex)
        {
            return ex is DllNotFoundException
                || ex is BadImageFormatException
                || ex is EntryPointNotFoundException
                || ex is FileNotFoundException
                || ex is TypeInitializationException;
        }
    }
}
