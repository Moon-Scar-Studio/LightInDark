using BepInEx;
using LightInDark.Core;
using System;
using System.IO;
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
        /// <summary>验证服务器公钥（PEM）。已嵌入真实公钥（2026-09-26 重新生成，旧密钥曾入 git 历史故更换）。</summary>
        public const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAESYcReDUo3QHkY7Q05gd5JfNXk9l6\n" +
            "DdbF3HW5z4lygUYthh7VHde8GFswBGey6JRPaFK5PTHfiZbycMpPXFlWiA==\n" +
            "-----END PUBLIC KEY-----";

        /// <summary>版本号，与服务器 official.json 的 version 对应。</summary>
        public const string ProtocolVersion = "1.0.0";

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
        /// 返回 (apiHash, modHash)。找不到文件时返回 (0,0)。
        /// </summary>
        public static (int apiHash, int modHash) ComputeLocalHashes()
        {
            int api = 0, mod = 0;
            try
            {
                string pluginDir = Paths.PluginPath;
                string apiPath = Path.Combine(pluginDir, "LightInDark.dll");
                string modPath = Path.Combine(pluginDir, "Light.dll");
                if (File.Exists(apiPath)) api = ComputeFileHash(apiPath);
                if (File.Exists(modPath)) mod = ComputeFileHash(modPath);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HandshakeCrypto.ComputeLocalHashes]", ex);
            }
            return (api, mod);
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

                if (version != ProtocolVersion) return false;
                if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false; // 过期

                int payloadLen = o;
                int sigLen = raw.Length - payloadLen;
                // 服务器 ECDsa.SignData 默认为 IEEE P1363 格式（r‖s，各 32 字节 = 64 字节）
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