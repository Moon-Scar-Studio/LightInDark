using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Hazel;
using InnerNet;
using LightInDark.Core;
using UnityEngine;

namespace LightInDark.RPCs
{
    // =====================================================================
    // 自定义 RPC 系统
    //
    // 使用 callId = 200
    // 注意：不能使用 byte.MaxValue(255)！
    //   游戏服务器会把 255 识别为 Reactor 框架的自定义 RPC 通道，
    //   未完成 Reactor 握手就发送会触发“你在完成 Reactor 握手之前发送了
    //   Reactor 自定义 RPC”被服务器踢出/拒绝，自定义 RPC 完全收不到。
    //   官方 RpcCalls 枚举只用到 0~66，所以 67~254 都是安全区。
    // 通过 Harmony patch 在 InnerNetObject.HandleRpc 层面拦截
    // 发送时同时执行本地逻辑
    // =====================================================================

    /// <summary>
    /// 自定义 RPC 管理器。
    /// callId = 200（避开官方 0~66 与 Reactor 的 255）。
    /// </summary>
    public static class CustomRPC
    {
        /// <summary>自定义 RPC 的 callId（使用 200，勿改回 255）</summary>
        public const byte RpcCallId = 200;

        private static readonly Dictionary<int, Action<MessageReader>> _handlers = new();

        public static void Register(string hash, Action<MessageReader> handler)
        {
            try
            {
                _handlers[hash.ComputeConstantHash()] = handler;
                LightLogger.Log($"[CustomRPC] 注册: {hash} (hash={hash.ComputeConstantHash()})");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("CustomRPC.Register", ex);
            }
        }

        /// <summary>
        /// 发送 RPC 到所有客户端，并在本地立即执行。
        /// </summary>
        public static void Send(string hash, Action<MessageWriter> writer, bool reliable = true)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null) return;

                var player = PlayerControl.LocalPlayer;
                if (player == null) return;

                int hashValue = hash.ComputeConstantHash();

                // 网络发送
                if (client.AmClient && client.GameState == InnerNetClient.GameStates.Started)
                {
                    try
                    {
                        var msgWriter = client.StartRpcImmediately(
                            player.NetId, RpcCallId,
                            reliable ? SendOption.Reliable : SendOption.None, -1);

                        msgWriter.Write(hashValue);
                        writer?.Invoke(msgWriter);

                        client.FinishRpcImmediately(msgWriter);
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"[CustomRPC] 发送失败: {hash}: {ex.Message}");
                    }
                }

                // 本地执行
                // 在发送端也执行方法体
                // 不需要序列化/反序列化——直接用一个单独的 Action 调用
                // handler 接收 MessageReader，但我们无法轻松地本地构造它
                // 所以：LocalExecute 由 [LidRPC] 的 Prefix 处理（直接 Invoke 方法）
                // 这里只负责网络发送
                // LidRPC.Prefix 已经处理了本地执行
            }
            catch (Exception ex)
            {
                LightLogger.LogError("CustomRPC.Send", ex);
            }
        }

        /// <summary>
        /// 发送 RPC 到指定玩家（不本地执行）
        /// </summary>
        public static void SendTo(PlayerControl target, string hash, Action<MessageWriter> writer, bool reliable = true)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null) return;

                var player = PlayerControl.LocalPlayer;
                if (player == null) return;

                int hashValue = hash.ComputeConstantHash();

                try
                {
                    var msgWriter = client.StartRpcImmediately(
                        player.NetId, RpcCallId,
                        reliable ? SendOption.Reliable : SendOption.None,
                        target.OwnerId);

                    msgWriter.Write(hashValue);
                    writer?.Invoke(msgWriter);

                    client.FinishRpcImmediately(msgWriter);
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[CustomRPC] SendTo 失败: {hash}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("CustomRPC.SendTo", ex);
            }
        }

        /// <summary>
        /// 仅发送 RPC（不本地执行）
        /// </summary>
        public static void SendOnly(string hash, Action<MessageWriter> writer, bool reliable = true)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmClient) return;

                var player = PlayerControl.LocalPlayer;
                if (player == null) return;

                int hashValue = hash.ComputeConstantHash();

                try
                {
                    var msgWriter = client.StartRpcImmediately(
                        player.NetId, RpcCallId,
                        reliable ? SendOption.Reliable : SendOption.None, -1);

                    msgWriter.Write(hashValue);
                    writer?.Invoke(msgWriter);

                    client.FinishRpcImmediately(msgWriter);
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[CustomRPC] SendOnly 失败: {hash}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("CustomRPC.SendOnly", ex);
            }
        }

        /// <summary>
        /// 处理收到的自定义 RPC。
        /// 从 reader 读取 hash，查找 handler，执行。
        /// </summary>
        internal static void HandleRpc(MessageReader reader)
        {
            try
            {
                int hash = reader.ReadInt32();
                if (_handlers.TryGetValue(hash, out var handler))
                {
                    handler.Invoke(reader);
                }
                else
                {
                    LightLogger.LogWarning($"[CustomRPC] 未知 RPC hash: {hash}");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[CustomRPC] 处理失败: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    // =====================================================================
    // Harmony Patch — 在 InnerNetObject 层面拦截 HandleRpc
    // =====================================================================

    /// <summary>
    /// 拦截所有 InnerNetObject 的 HandleRpc。
    /// </summary>
    [HarmonyPatch(typeof(InnerNetObject), nameof(InnerNetObject.HandleRpc))]
    public static class CustomRpcHandlePatch
    {
        public static bool Prefix(InnerNetObject __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (callId != CustomRPC.RpcCallId) return true; // 不是我们的 RPC，正常处理

                CustomRPC.HandleRpc(reader);
                return false; // 阻止原版处理
            }
            catch (Exception ex)
            {
                LightLogger.LogError("CustomRpcHandlePatch.Prefix", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// PlayerControl override 了 HandleRpc（虚方法分派可能不走基类），
    /// 这里同样拦截，确保自定义 RPC 无论经基类还是子类都能处理。
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    public static class PlayerControlRpcPatch
    {
        public static bool Prefix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (callId != CustomRPC.RpcCallId) return true;

                CustomRPC.HandleRpc(reader);
                return false;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerControlRpcPatch.Prefix", ex);
                return true;
            }
        }
    }

    // =====================================================================
    // 扩展方法
    // =====================================================================

    public static class MessageWriterExtensions
    {
        public static void WritePlayer(this MessageWriter writer, PlayerControl player)
        {
            try
            {
                writer.Write(player?.PlayerId ?? byte.MaxValue);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageWriterExtensions.WritePlayer", ex);
            }
        }

        public static void WriteVector2(this MessageWriter writer, Vector2 value)
        {
            try
            {
                writer.Write(value.x);
                writer.Write(value.y);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageWriterExtensions.WriteVector2", ex);
            }
        }

        public static void WriteVector3(this MessageWriter writer, Vector3 value)
        {
            try
            {
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageWriterExtensions.WriteVector3", ex);
            }
        }
    }

    public static class MessageReaderExtensions
    {
        public static PlayerControl ReadPlayer(this MessageReader reader)
        {
            try
            {
                byte playerId = reader.ReadByte();
                if (playerId == byte.MaxValue) return null;
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId) return pc;
                return null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageReaderExtensions.ReadPlayer", ex);
                return null;
            }
        }

        public static Vector2 ReadVector2(this MessageReader reader)
        {
            try
            {
                return new(reader.ReadSingle(), reader.ReadSingle());
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageReaderExtensions.ReadVector2", ex);
                return default;
            }
        }

        public static Vector3 ReadVector3(this MessageReader reader)
        {
            try
            {
                return new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            catch (Exception ex)
            {
                LightLogger.LogError("MessageReaderExtensions.ReadVector3", ex);
                return default;
            }
        }
    }

    internal static class HashHelper
    {
        public static int ComputeConstantHash(this string str)
        {
            try
            {
                int hash = 0;
                foreach (char c in str)
                    hash = (hash * 31) + c;
                return hash;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("HashHelper.ComputeConstantHash", ex);
                return default;
            }
        }
    }
}
