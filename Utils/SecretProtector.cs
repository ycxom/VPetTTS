using System.ComponentModel;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace Vpet.Plugin.CustomTTS.Utils
{
    /// <summary>
    /// 设置文件里的密钥（API Key、认证头）加密落盘：Windows DPAPI，当前用户作用域。
    ///
    /// 落盘形态是 "dpapi1:" + Base64(密文)。内存里始终是明文，所以设置窗口、请求代码
    /// 都不用改——加解密只发生在 JSON 读写那一层（见 <see cref="ProtectedStringConverter"/>）。
    ///
    /// 防的是什么：窃密木马批量扫描、拷走 settings.json 这类“只搬文件”的攻击——
    /// 密文离开这台机器、这个 Windows 账户就解不开。
    /// 防不住什么：已经跑在同一账户下、并且专门针对本插件调用 DPAPI 的恶意程序
    /// （DPAPI 的设计边界，Chrome 的 App-Bound Encryption 也是为此才出现的）；
    /// 以及进程内存里的明文。这里的附加熵只是让“通用窃密脚本”解不开，不是对抗定向攻击的手段。
    ///
    /// 用 P/Invoke 而不是 System.Security.Cryptography.ProtectedData：后者是额外的 DLL，
    /// 而创意工坊会对 MOD 内每个 DLL 做哈希校验，少一个依赖少一个出问题的地方。
    /// </summary>
    public static class SecretProtector
    {
        /// <summary>密文前缀；也是区分“已加密”和“旧版明文”的唯一依据</summary>
        public const string Prefix = "dpapi1:";

        private const int CryptProtectUiForbidden = 0x1;

        // 附加熵：绑定到本插件，别的程序拿到密文也不能直接用 CryptUnprotectData(无熵) 解开
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VPetLLM.VPetTTS.Settings.Secret.v1");

        public static bool IsProtected(string value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>加密。空串原样返回；已是密文不重复加密</summary>
        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext) || IsProtected(plaintext))
                return plaintext ?? "";

            var cipher = Transform(Encoding.UTF8.GetBytes(plaintext), protect: true);
            return Prefix + Convert.ToBase64String(cipher);
        }

        /// <summary>
        /// 解密。没有前缀的值原样返回（手改 settings.json 写进去的明文 key 照常能用，
        /// 下次保存时自然变成密文）；
        /// 解不开（密文是别的账户/别的机器生成的）返回空串并记日志——
        /// 宁可让用户重填一次 key，也不能让整个设置读取失败。
        /// </summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return "";

            if (!IsProtected(stored))
                return stored;

            try
            {
                var cipher = Convert.FromBase64String(stored.Substring(Prefix.Length));
                return Encoding.UTF8.GetString(Transform(cipher, protect: false));
            }
            catch (Exception ex)
            {
                TTSLogger.Log($"SecretProtector: 密钥解密失败（可能是换了机器/账户），需要重新填写: {ex.Message}");
                return "";
            }
        }

        private static byte[] Transform(byte[] input, bool protect)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("密钥加密依赖 Windows DPAPI");

            var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            var inputBlob = new DataBlob { Size = input.Length, Data = inputHandle.AddrOfPinnedObject() };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
            DataBlob outputBlob = default;
            IntPtr description = IntPtr.Zero;
            try
            {
                var ok = protect
                    ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                    : CryptUnprotectData(ref inputBlob, out description, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var result = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (outputBlob.Data != IntPtr.Zero)
                {
                    // 解密输出是明文：用完先清零再释放
                    Marshal.Copy(new byte[outputBlob.Size], 0, outputBlob.Data, outputBlob.Size);
                    _ = LocalFree(outputBlob.Data);
                }
                if (description != IntPtr.Zero) _ = LocalFree(description);
                entropyHandle.Free();
                inputHandle.Free();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn, string description, ref DataBlob optionalEntropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn, out IntPtr description, ref DataBlob optionalEntropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }

    /// <summary>
    /// 标在密钥属性上：写 JSON 时加密、读 JSON 时解密（兼容旧版明文）。
    /// 对 LPS 序列化无影响（LPS 走自己的 [Line] 特性，会忽略这个）。
    /// </summary>
    public sealed class ProtectedStringConverter : JsonConverter<string>
    {
        public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
        {
            writer.WriteValue(SecretProtector.Protect(value));
        }

        public override string ReadJson(JsonReader reader, Type objectType, string existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return "";

            return SecretProtector.Unprotect(reader.Value?.ToString());
        }
    }
}
