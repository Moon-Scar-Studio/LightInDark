using BepInEx;
using LightInDark.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LightInDark.Handshake
{
    /// <summary>
    /// 握手协议层（LightAPI）。
    /// 客户端把 (version, apiHash, modHash, accountId) 交给验证服务器，
    /// 服务器用 ECDSA P-256 私钥签名生成票据；房主用内置公钥验签 + 对比本地 hash，
    /// 从而确认对方客户端未被篡改。
    /// </summary>
    public static class HandshakeCrypto
    {
        /// <summary>验证服务器公钥（PEM），取自线上 GET /pubkey。</summary>
        public const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEz9PVNNRRieHiMwD5g6mOjjcBHLkb\n" +
            "AzBtgX7o9w3d1vB6M5qclqsXZ5dMNiUiFRNlEMbFJNVnlj5007wj/mVLFw==\n" +
            "-----END PUBLIC KEY-----";

        /// <summary>握手验证版本号，与模组版本及服务器 official.json 的 version 一致。</summary>
        public const string VerifyVersion = "0.0.1";

        /// <summary>
        /// 计算文件 hash（SHA256 前 4 字节 → int32）。
        /// 与服务器 official.json 中记录的 apiHash/modHash 一致。
        /// </summary>
        public static int ComputeFileHash(string path)
        {
            try
            {
                using var sha = SHA256.Create();
                byte[] bytes = File.ReadAllBytes(path);
                byte[] digest = sha.ComputeHash(bytes);
                return BitConverter.ToInt32(digest, 0);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeCrypto.ComputeFileHash]", ex);
                return 0;
            }
        }

        /// <summary>
        /// 计算本地两个插件的 hash：LightInDark.dll（API）与 Light.dll（模组）。
        /// 返回 (apiHash, modHash)。定位失败时对应值为 0 并记录告警。
        /// </summary>
        public static (int apiHash, int modHash) ComputeLocalHashes()
        {
            int api = ResolvePluginHash("LightInDark.dll", GetAssemblyLocation(typeof(LIDPlugin).Assembly));
            int mod = ResolvePluginHash("Light.dll", GetModAssemblyLocation());
            return (api, mod);
        }

        // 优先用已加载程序集的实际位置，兜底在插件目录递归查找；都失败则告警并返回 0
        private static int ResolvePluginHash(string fileName, string assemblyLocation)
        {
            var tried = new List<string>();
            if (assemblyLocation != null)
            {
                tried.Add($"程序集位置 {assemblyLocation}");
                if (File.Exists(assemblyLocation)) return ComputeFileHash(assemblyLocation);
            }
            try
            {
                string dir = Paths.PluginPath;
                tried.Add($"递归查找 {dir}");
                string found = FindFileRecursive(dir, fileName);
                if (found != null) return ComputeFileHash(found);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeCrypto.ComputeLocalHashes]", ex);
            }
            LightLogger.LogWarning($"[HandshakeCrypto] 未能定位 {fileName}，该 hash 将上报 0（会导致验证失败）；已尝试：{string.Join("；", tried)}");
            return 0;
        }

        // 取程序集所在路径；动态/内存程序集无 Location，安全返回 null
        private static string GetAssemblyLocation(Assembly asm)
        {
            try { return string.IsNullOrEmpty(asm?.Location) ? null : asm.Location; }
            catch { return null; }
        }

        // LightAPI 不引用 Light，只能用程序集名从已加载程序集里找模组本体
        private static string GetModAssemblyLocation()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Light") continue;
                string loc = GetAssemblyLocation(asm);
                if (loc != null) return loc;
            }
            return null;
        }

        // 递归查找文件；目录不存在或无权访问时安全跳过
        private static string FindFileRecursive(string dir, string fileName)
        {
            try
            {
                string direct = Path.Combine(dir, fileName);
                if (File.Exists(direct)) return direct;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string found = FindFileRecursive(sub, fileName);
                    if (found != null) return found;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeCrypto.FindFileRecursive]", ex);
            }
            return null;
        }

        /// <summary>
        /// 解析并验证票据（签名 + 版本 + 过期），返回票据内嵌的信息。
        /// 不做 hash 比对（由调用方决定比对基准）。
        /// </summary>
        /// <returns>true 表示票据由官方服务器签发且未过期。</returns>
        public static bool TryParse(string ticketBase64,
            out string accountId, out long exp,
            out int apiHash, out int modHash, out string version,
            out int nonce)
        {
            accountId = "";
            exp = 0;
            apiHash = 0;
            modHash = 0;
            version = "";
            nonce = 0;
            try
            {
                if (string.IsNullOrEmpty(ticketBase64)) return false;

                byte[] raw = Convert.FromBase64String(ticketBase64);
                if (raw.Length < 8 + 1 + 4 + 4 + 8) return false;

                int o = 0;
                int accLen = raw[o++];
                if (accLen > 64) return false;
                accountId = Encoding.UTF8.GetString(raw, o, accLen); o += accLen;

                int verLen = raw[o++];
                if (verLen > 32 || o + verLen > raw.Length - (4 + 4 + 8 + 1)) return false;
                version = Encoding.UTF8.GetString(raw, o, verLen); o += verLen;

                nonce = ReadInt32(raw, ref o);
                apiHash = ReadInt32(raw, ref o);
                modHash = ReadInt32(raw, ref o);
                exp = ReadInt64(raw, ref o);

                if (version != VerifyVersion) return false;
                if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false; // 过期

                int payloadLen = o;
                int sigLen = raw.Length - payloadLen;
                // 服务器 ECDsa.SignData 默认输出 IEEE P1363 格式（r‖s，各 32 字节，共 64 字节；非 DER）
                if (sigLen != 64) return false;

                byte[] payload = new byte[payloadLen];
                Array.Copy(raw, 0, payload, 0, payloadLen);
                byte[] sig = new byte[sigLen];
                Array.Copy(raw, payloadLen, sig, 0, sigLen);

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(PublicKeyPem);
                return ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeCrypto.TryParse]", ex);
                return false;
            }
        }

        private static int ReadInt32(byte[] buf, ref int o)
        {
            int v = buf[o] | (buf[o + 1] << 8) | (buf[o + 2] << 16) | (buf[o + 3] << 24);
            o += 4;
            return v;
        }

        private static long ReadInt64(byte[] buf, ref int o)
        {
            long v = 0;
            for (int i = 0; i < 8; i++)
                v |= (long)buf[o + i] << (8 * i);
            o += 8;
            return v;
        }
    }
}