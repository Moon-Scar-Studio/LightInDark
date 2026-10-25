using System;
using System.Collections.Generic;
using System.Linq;
using Hazel;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.RPCs;

namespace LightInDark.Modifiers.Buffs
{
    /// <summary>
    /// **Buff 的网络同步**（2026-10-25 用户要求："顺便加上 Rpc 同步（可以同步给符合条件的玩家 GiveBuff 或者 Remove）"）。
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么不用 `[LidRPC]`】
    ///   `LidRPC` 的 Prefix 是**无条件广播给所有人**（`LightAPI\RPCs\LidRPC.cs` 的 `RpcPrefix`），
    ///   而用户要的是"**只发给符合条件的玩家**" → 用底层 `CustomRPC.SendTo` 逐个发 ✓
    ///   （参考 NOS 的做法：`PlayerModInfo` 里的状态也是**房主权威 + 定向 Rpc** ✓）
    ///
    /// 【权威模型】
    ///   · **房主**：本地先生效 → 再把同一条指令发给筛出来的客户端 ✓
    ///   · **非房主**：本地调用只影响自己（不广播）✓ —— 房主才是唯一真相 ✓
    ///     （非房主要求改别人身上的 Buff 时，请让房主代发 ✓）
    ///   · 接收端只做"落地"，**不再转发** ✗（防风暴 ✓）
    ///
    /// 【同步内容】
    ///   Give  ：(playerId, 类型 key, 时长, 实例 key)
    ///   Remove：(playerId, 类型 key, 实例 key, 模式)   模式 0=按类型 1=按实例键 2=全部
    ///   ⚠️ 类型必须**双方都注册过**（`BuffRegistry`，模组加载时注册 ✓），否则接收端只能打 warning 丢弃 ✗
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class BuffSync
    {
        private const string RpcGive = "LID.BuffSync.Give";
        private const string RpcRemove = "LID.BuffSync.Remove";

        private const byte ModeByType = 0;
        private const byte ModeByKey = 1;
        private const byte ModeAll = 2;

        private static bool _registered;

        /// <summary>
        /// 注册接收端（**每个客户端都要跑一次** ✓）。
        /// 由 <c>LidRpcRegistry.ScanAndPatch</c> 在模组加载时调用（`Light\LightPlugin.cs:90` / `LightAPI\LIDPlugin.cs:44`）✓
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;

            try
            {
                CustomRPC.Register(RpcGive, reader =>
                {
                    try
                    {
                        byte playerId = reader.ReadByte();
                        string defKey = reader.ReadString();
                        float duration = reader.ReadSingle();
                        string instanceKey = reader.ReadString();

                        var definition = BuffRegistry.Get(defKey);
                        if (definition == null)
                        {
                            LightLogger.LogWarning($"[BuffSync] 收到未知 Buff：{defKey}（本地未注册 → 丢弃）");
                            return;
                        }
                        var player = Player.Get(playerId);
                        if (player == null) return;

                        BuffManager.Add(player, definition, duration, instanceKey);
                    }
                    catch (Exception ex) { LightLogger.LogWarning($"[BuffSync] Give 落地失败：{ex.Message}"); }
                });

                CustomRPC.Register(RpcRemove, reader =>
                {
                    try
                    {
                        byte playerId = reader.ReadByte();
                        string defKey = reader.ReadString();
                        string instanceKey = reader.ReadString();
                        byte mode = reader.ReadByte();

                        var player = Player.Get(playerId);
                        if (player == null) return;

                        switch (mode)
                        {
                            case ModeByKey:
                                BuffManager.RemoveByKey(player, instanceKey);
                                break;
                            case ModeAll:
                                BuffManager.RemoveAll(player);
                                break;
                            default:
                                BuffManager.RemoveByType(player, BuffRegistry.Get(defKey));
                                break;
                        }
                    }
                    catch (Exception ex) { LightLogger.LogWarning($"[BuffSync] Remove 落地失败：{ex.Message}"); }
                });

                // ★ 2026-10-25：先把 `BuffStorage` 里的模板登记进 `BuffRegistry` ✓
                //   —— RPC 接收端是靠 key 反查定义的，没登记就只能丢弃 ✗
                BuffStorage.EnsureRegistered();

                LightLogger.Log("[BuffSync] 已注册 Buff 同步 RPC（Give / Remove，定向发送 ✓）");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[BuffSync.EnsureRegistered]", ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  发送端
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 给玩家上 Buff：**本地生效 + （房主时）定向同步** ✓
        /// </summary>
        /// <param name="player">目标玩家（谁身上生效 ✓）</param>
        /// <param name="definition">Buff 类型（必须在 `BuffRegistry` 注册过 ✓，否则接收端会丢弃 ✗）</param>
        /// <param name="duration">时长（秒）；**负数 = 用定义里的默认时长** ✓</param>
        /// <param name="instanceKey">实例键（同类型可并存；移除时推荐用它 ✓）</param>
        /// <param name="canReceive">
        /// **符合条件的玩家**筛选器：返回 true 的**客户端**才会收到这条同步 ✓
        /// （null = 除自己以外所有能收的客户端 ✓；接收端不知道筛选逻辑，只落地 ✓）
        /// </param>
        public static RuntimeBuff Give(Player player, BuffDefinition definition, float duration,
                                       string instanceKey = null, Func<Player, bool> canReceive = null)
        {
            EnsureRegistered();
            if (player == null || definition == null) return null;

            float resolved = duration;
            // -1 / 负数 = 用定义里的默认时长（跟 `BuffManager.Add` 的语义一致 ✓）
            if (resolved < 0f) resolved = definition.DefaultDuration;

            var buff = BuffManager.Add(player, definition, resolved, instanceKey);
            if (buff == null) return null;

            if (IsHost()) SendGive(player, definition, buff.Duration, buff.InstanceKey, canReceive);
            return buff;
        }

        /// <summary>按**类型**移除（该玩家身上这种 Buff 全部实例 ✓）</summary>
        /// <param name="player">目标玩家 ✓</param>
        /// <param name="definition">要移除的类型（null = 什么都不做 ✓）</param>
        /// <param name="canReceive">哪些客户端需要收到这条移除同步（null = 除自己外全部 ✓）</param>
        public static int RemoveByType(Player player, BuffDefinition definition, Func<Player, bool> canReceive = null)
        {
            EnsureRegistered();
            int n = BuffManager.RemoveByType(player, definition);
            if (n > 0 && IsHost()) SendRemove(player, definition?.Key ?? "", "", ModeByType, canReceive);
            return n;
        }

        /// <summary>按**实例键**移除（推荐 ✓，能精确到某一次施加）</summary>
        /// <param name="player">目标玩家 ✓</param>
        /// <param name="instanceKey">实例键（`Give` 时传的那个 `key` ✓）</param>
        /// <param name="canReceive">哪些客户端需要收到这条移除同步（null = 除自己外全部 ✓）</param>
        public static int RemoveByKey(Player player, string instanceKey, Func<Player, bool> canReceive = null)
        {
            EnsureRegistered();
            int n = BuffManager.RemoveByKey(player, instanceKey);
            if (n > 0 && IsHost()) SendRemove(player, "", instanceKey, ModeByKey, canReceive);
            return n;
        }

        /// <summary>移除该玩家身上**全部** Buff ✓</summary>
        /// <param name="player">目标玩家 ✓</param>
        /// <param name="canReceive">哪些客户端需要收到这条清空同步（null = 除自己外全部 ✓）</param>
        public static void RemoveAll(Player player, Func<Player, bool> canReceive = null)
        {
            EnsureRegistered();
            if (player == null) return;
            BuffManager.RemoveAll(player);
            if (IsHost()) SendRemove(player, "", "", ModeAll, canReceive);
        }

        // ─────────────────────────────────────────────────────────────────
        //  内部：逐个客户端发送
        // ─────────────────────────────────────────────────────────────────

        private static bool IsHost()
        {
            try { return AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost; }
            catch { return false; }
        }

        /// <summary>符合条件的客户端（= 除自己以外、且有角色对象、并且通过筛选器 ✓）</summary>
        private static IEnumerable<PlayerControl> Targets(Func<Player, bool> canReceive)
        {
            var client = AmongUsClient.Instance;
            if (client == null) yield break;

            var all = client.allClients;
            if (all == null) yield break;

            // ⚠️ 2026-10-25：`AmongUsClient` 没有 `Id` 属性（编译报错 ✗），
            //    直接用"本地 PlayerControl"比较最稳 ✓
            var local = PlayerControl.LocalPlayer;

            foreach (var c in all)
            {
                if (c == null) continue;
                var control = c.Character;
                if (control == null) continue;
                if (local != null && control == local) continue;   // 自己在本地已经生效 ✓

                if (canReceive != null)
                {
                    Player? p = null;
                    try { p = Player.Get(control.PlayerId); } catch { }
                    if (p == null) continue;                    // 拿不到 Player 包装 → 没法判断，跳过 ✓
                    bool ok;
                    try { ok = canReceive(p); } catch { ok = false; }
                    if (!ok) continue;
                }

                yield return control;
            }
        }

        private static void SendGive(Player player, BuffDefinition definition, float duration,
                                     string instanceKey, Func<Player, bool> canReceive)
        {
            try
            {
                byte playerId = player.Control.PlayerId;
                string defKey = definition.Key;
                string key = instanceKey ?? "";

                foreach (var target in Targets(canReceive))
                {
                    CustomRPC.SendTo(target, RpcGive, writer =>
                    {
                        writer.Write(playerId);
                        writer.Write(defKey);
                        writer.Write(duration);
                        writer.Write(key);
                    });
                }

                LightLogger.Log($"[BuffSync] {player.Name} +{defKey}{(key.Length > 0 ? "#" + key : "")} → 已同步（定向 ✓）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[BuffSync.SendGive] {ex.Message}");
            }
        }

        private static void SendRemove(Player player, string defKey, string instanceKey, byte mode,
                                       Func<Player, bool> canReceive)
        {
            try
            {
                byte playerId = player.Control.PlayerId;
                string key = instanceKey ?? "";

                foreach (var target in Targets(canReceive))
                {
                    CustomRPC.SendTo(target, RpcRemove, writer =>
                    {
                        writer.Write(playerId);
                        writer.Write(defKey ?? "");
                        writer.Write(key);
                        writer.Write(mode);
                    });
                }

                LightLogger.Log($"[BuffSync] {player.Name} -{defKey}{(key.Length > 0 ? "#" + key : "")}（模式 {mode}）→ 已同步（定向 ✓）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[BuffSync.SendRemove] {ex.Message}");
            }
        }
    }
}
