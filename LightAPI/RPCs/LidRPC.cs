using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Hazel;
using LightInDark.Core;
using UnityEngine;

namespace LightInDark.RPCs
{
    /// <summary>标记一个静态方法为 RPC。定义即用。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LidRPCAttribute : Attribute
    {
        public bool OnlyHost { get; set; } = false;
        public bool Reliable { get; set; } = true;
        /// <summary>是否在发送端本地执行（默认 true）</summary>
        public bool LocalExecute { get; set; } = true;
    }

    /// <summary>
    /// RPC 自动注册器。
    /// 扫描 [LidRPC] 方法，
    /// Harmony Prefix 拦截调用，序列化参数发送，
    /// 同时在本地执行（Before 模式）。
    /// </summary>
    public static class LidRpcRegistry
    {
        private static bool _initialized;

        private class RpcEntry
        {
            public string HashStr;
            public int Hash;
            public MethodInfo Method;
            public ParameterInfo[] Parameters;
            public bool OnlyHost;
            public bool Reliable;
            public bool LocalExecute;
        }

        private static readonly Dictionary<int, RpcEntry> _entries = new();

        /// <summary>
        /// **已注册的 RPC 清单**（给调试工具用 ✓ 2026-10-25 用户要求"RPC 触发器"）
        /// <para>按名字排序 ✓，含参数类型（调试面板靠它显示 + 生成默认参数 ✓）</para>
        /// </summary>
        public static List<LidRpcInfo> GetRpcs()
        {
            var list = new List<LidRpcInfo>(_entries.Count);
            foreach (var entry in _entries.Values)
            {
                try
                {
                    list.Add(new LidRpcInfo(entry.HashStr, entry.Hash, entry.OnlyHost, entry.Method, entry.Parameters));
                }
                catch { }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>**触发**一条已注册的 RPC（调试用 ✓）：参数按类型填默认值（byte → 本地玩家 id ✓）</summary>
        /// <param name="info"><see cref="GetRpcs"/> 里的一项 ✓</param>
        /// <param name="overrides">可选：按下标覆盖参数值 ✓（null 的项自动填默认 ✓）</param>
        /// <returns>是否真的调用了 ✓</returns>
        public static bool Invoke(LidRpcInfo info, object[] overrides = null)
        {
            if (info == null) return false;

            try
            {
                var args = new object[info.Parameters.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    if (overrides != null && i < overrides.Length && overrides[i] != null)
                    {
                        args[i] = overrides[i];
                        continue;
                    }
                    args[i] = DefaultArg(info.Parameters[i].ParameterType);
                }

                // 走正常调用（会被 Prefix 拦截 → 广播 + 本地执行 ✓，跟业务代码调它一模一样 ✓）
                info.Method.Invoke(null, args);
                LightLogger.Log($"[RpcTrigger] 已触发 {info.Name}（{(info.OnlyHost ? "OnlyHost" : "广播")}）参数：{Describe(args)}");
                return true;
            }
            catch (Exception ex)
            {
                LightLogger.LogError($"[RpcTrigger] 触发 {info.Name} 失败", ex);
                return false;
            }
        }

        private static object DefaultArg(Type type)
        {
            try
            {
                if (type == typeof(byte))
                    return PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.PlayerId : (byte)0;
                if (type == typeof(string)) return "";
                if (type == typeof(bool)) return false;
                if (type == typeof(float)) return 0f;
                if (type == typeof(int)) return 0;
                if (type == typeof(PlayerControl)) return PlayerControl.LocalPlayer;
                if (type == typeof(byte[])) return Array.Empty<byte>();
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
            catch { return null; }
        }

        private static string Describe(object[] args)
        {
            if (args == null || args.Length == 0) return "无";
            var parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
                parts[i] = args[i] == null ? "null" : args[i].ToString();
            return string.Join(", ", parts);
        }

        public static void ScanAndPatch(Harmony harmony)
        {
            try
            {
                if (_initialized) return;
                _initialized = true;

                var assembly = Assembly.GetExecutingAssembly();

                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        var attr = method.GetCustomAttribute<LidRPCAttribute>();
                        if (attr == null) continue;

                        var hashStr = $"{type.FullName}.{method.Name}";
                        var hash = hashStr.ComputeConstantHash();

                        var entry = new RpcEntry
                        {
                            HashStr = hashStr,
                            Hash = hash,
                            Method = method,
                            Parameters = method.GetParameters(),
                            OnlyHost = attr.OnlyHost,
                            Reliable = attr.Reliable,
                            LocalExecute = attr.LocalExecute,
                        };
                        _entries[hash] = entry;

                        // 注册接收处理器（接收端执行）
                        var localEntry = entry;
                        CustomRPC.Register(hashStr, reader =>
                        {
                            if (localEntry.OnlyHost && !SenderIsHost())
                            {
                                LightLogger.LogWarning($"[LidRPC] 拒绝非房主消息: {localEntry.HashStr}");
                                return;
                            }
                            var args = DeserializeArgs(reader, localEntry.Parameters);
                            InvokeLocally(localEntry.Method, args);
                        });

                        // Harmony Prefix：拦截调用，发送 RPC
                        var prefix = new HarmonyMethod(typeof(LidRpcRegistry), nameof(RpcPrefix));
                        harmony.Patch(method, prefix: prefix);

                        LightLogger.Log($"[LidRPC] 注册: {hashStr} (hash={hash}, {entry.Parameters.Length} params, local={entry.LocalExecute})");
                    }
                }

                LightLogger.Log($"[LidRPC] 扫描完成，共 {_entries.Count} 个 RPC");

                // ★ 2026-10-25：Buff 的定向同步 RPC（不走 LidRPC 的"全广播"，见 BuffSync 的注释 ✓）
                //    每个客户端都会跑到这里（Light\LightPlugin.cs:90 / LightAPI\LIDPlugin.cs:44）✓
                Modifiers.Buffs.BuffSync.EnsureRegistered();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.ScanAndPatch", ex);
            }
        }

        // 防止无限递归的标志
        // Bypass only the method being delivered. Nested RPCs still need to be broadcast.
        private static readonly HashSet<MethodBase> _localExecutingMethods = new();
        private static void InvokeLocally(MethodInfo method, object[] args)
        {
            bool added = _localExecutingMethods.Add(method);
            try { method.Invoke(null, args); }
            finally { if (added) _localExecutingMethods.Remove(method); }
        }
        private static bool SenderIsHost()
        {
            var client = AmongUsClient.Instance;
            if (client == null || CustomRPC.CurrentSender == byte.MaxValue) return false;
            var host = client.GetClient(client.HostId);
            return host != null && host.Character != null && host.Character.PlayerId == CustomRPC.CurrentSender;
        }

        /// <summary>
        /// Harmony Prefix — 拦截 [LidRPC] 方法调用。
        /// 发送 RPC + 本地执行。
        /// </summary>
        internal static bool RpcPrefix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                // 本地执行中：放行，执行方法体
                if (_localExecutingMethods.Contains(__originalMethod))
                    return true;

                var hashStr = $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}";
                var hash = hashStr.ComputeConstantHash();

                if (!_entries.TryGetValue(hash, out var entry))
                    return true;

                // 房主检查
                if (entry.OnlyHost && !AmongUsClient.Instance.AmHost)
                    return false;

                // 序列化参数用于网络发送
                var payloadWriter = MessageWriter.Get(SendOption.Reliable);
                SerializeArgs(payloadWriter, __args, entry.Parameters);
                var payloadBytes = payloadWriter.ToByteArray(false);

                // 发送到网络
                CustomRPC.SendOnly(entry.HashStr, writer =>
                {
                    writer.Write(payloadBytes, 0, payloadBytes.Length);
                }, entry.Reliable);

                // 本地执行
                if (entry.LocalExecute)
                {
                    try
                    {
                        InvokeLocally(entry.Method, __args);
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"[LidRPC] 本地执行失败: {hashStr}: {ex.Message}");
                    }
                }

                // 跳过原方法体（发送端不重复执行）
                return false;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.RpcPrefix", ex);
                return true;
            }
        }

        // ---- 序列化 ----

        private static void SerializeArgs(MessageWriter writer, object[] args, ParameterInfo[] parameters)
        {
            try
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    SerializeArg(writer, args[i], parameters[i].ParameterType);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.SerializeArgs", ex);
            }
        }

        private static void SerializeArg(MessageWriter writer, object value, Type type)
        {
            try
            {
                if (type.IsEnum) writer.Write(Convert.ToInt32(value));
                else if (type == typeof(byte)) writer.Write((byte)value);
                else if (type == typeof(int)) writer.Write((int)value);
                else if (type == typeof(float)) writer.Write((float)value);
                else if (type == typeof(bool)) writer.Write((bool)value);
                else if (type == typeof(string)) writer.Write((string?)value ?? "");
                else if (type == typeof(PlayerControl)) writer.WritePlayer((PlayerControl?)value);
                else if (type == typeof(Vector2)) writer.WriteVector2((Vector2)value);
                else if (type == typeof(Vector3)) writer.WriteVector3((Vector3)value);
                else if (type == typeof(byte[])) _WriteByteArray(writer, (byte[]?)value);
                else if (type == typeof(int[])) _WriteIntArray(writer, (int[]?)value);
                else if (type == typeof(string[])) _WriteStringArray(writer, (string[]?)value);
                else throw new NotSupportedException($"[LidRPC] 不支持的类型: {type.Name}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.SerializeArg", ex);
            }
        }

        private static object?[] DeserializeArgs(MessageReader reader, ParameterInfo[] parameters)
        {
            try
            {
                var args = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    args[i] = DeserializeArg(reader, parameters[i].ParameterType);
                }
                return args;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.DeserializeArgs", ex);
                return default;
            }
        }

        private static object? DeserializeArg(MessageReader reader, Type type)
        {
            try
            {
                if (type.IsEnum) return System.Enum.ToObject(type, reader.ReadInt32());
                if (type == typeof(byte)) return reader.ReadByte();
                if (type == typeof(int)) return reader.ReadInt32();
                if (type == typeof(float)) return reader.ReadSingle();
                if (type == typeof(bool)) return reader.ReadBoolean();
                if (type == typeof(string)) return reader.ReadString();
                if (type == typeof(PlayerControl)) return reader.ReadPlayer();
                if (type == typeof(Vector2)) return reader.ReadVector2();
                if (type == typeof(Vector3)) return reader.ReadVector3();
                if (type == typeof(byte[])) return _ReadByteArray(reader);
                if (type == typeof(int[])) return _ReadIntArray(reader);
                if (type == typeof(string[])) return _ReadStringArray(reader);
                throw new NotSupportedException($"[LidRPC] 不支持的类型: {type.Name}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LidRpcRegistry.DeserializeArg", ex);
                return default;
            }
        }

        // ---- 数组序列化辅助 ----

        private static void _WriteByteArray(MessageWriter writer, byte[]? arr)
        {
            if (arr == null) { writer.Write(0); return; }
            writer.Write(arr.Length);
            foreach (var v in arr) writer.Write(v);
        }

        private static void _WriteIntArray(MessageWriter writer, int[]? arr)
        {
            if (arr == null) { writer.Write(0); return; }
            writer.Write(arr.Length);
            foreach (var v in arr) writer.Write(v);
        }

        private static void _WriteStringArray(MessageWriter writer, string[]? arr)
        {
            if (arr == null) { writer.Write(0); return; }
            writer.Write(arr.Length);
            foreach (var v in arr) writer.Write(v ?? "");
        }

        private static byte[] _ReadByteArray(MessageReader reader)
        {
            int len = reader.ReadInt32();
            if (len <= 0 || len > 1024) return Array.Empty<byte>();
            var arr = new byte[len];
            for (int i = 0; i < len; i++) arr[i] = reader.ReadByte();
            return arr;
        }

        private static int[] _ReadIntArray(MessageReader reader)
        {
            int len = reader.ReadInt32();
            if (len <= 0 || len > 1024) return Array.Empty<int>();
            var arr = new int[len];
            for (int i = 0; i < len; i++) arr[i] = reader.ReadInt32();
            return arr;
        }

        private static string[] _ReadStringArray(MessageReader reader)
        {
            int len = reader.ReadInt32();
            if (len <= 0 || len > 1024) return Array.Empty<string>();
            var arr = new string[len];
            for (int i = 0; i < len; i++) arr[i] = reader.ReadString();
            return arr;
        }
    }
/// <summary>
    /// **一条已注册 RPC 的元信息**（调试工具用 ✓ 2026-10-25）
    /// </summary>
    public sealed class LidRpcInfo
    {
        /// <summary>完整名（`命名空间.类.方法` ✓，调试面板搜名字用得到 ✓）</summary>
        public string Name { get; }

        /// <summary>短名（只有方法名 ✓）</summary>
        public string ShortName { get; }

        /// <summary>常量哈希（= 网络里的 id ✓，搜 id 就用它 ✓）</summary>
        public int Hash { get; }

        /// <summary>是否只接受房主 ✓</summary>
        public bool OnlyHost { get; }

        /// <summary>被 patch 的静态方法 ✓</summary>
        public System.Reflection.MethodInfo Method { get; }

        /// <summary>形参（含名字 ✓）</summary>
        public System.Reflection.ParameterInfo[] Parameters { get; }

        /// <summary>显示用签名，如 `StartMeeting(byte reporterId, byte reportedId)` ✓</summary>
        public string Signature { get; }

        public LidRpcInfo(string name, int hash, bool onlyHost,
                          System.Reflection.MethodInfo method,
                          System.Reflection.ParameterInfo[] parameters)
        {
            Name = name ?? "";
            Hash = hash;
            OnlyHost = onlyHost;
            Method = method;
            Parameters = parameters ?? System.Array.Empty<System.Reflection.ParameterInfo>();

            int dot = Name.LastIndexOf('.');
            ShortName = dot >= 0 && dot + 1 < Name.Length ? Name.Substring(dot + 1) : Name;

            var parts = new string[Parameters.Length];
            for (int i = 0; i < Parameters.Length; i++)
                parts[i] = Parameters[i].ParameterType.Name + " " + Parameters[i].Name;
            Signature = ShortName + "(" + string.Join(", ", parts) + ")";
        }

        public override string ToString() => Signature;
    }
}