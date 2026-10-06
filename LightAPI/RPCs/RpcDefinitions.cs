using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightInDark.RPCs
{
    /// <summary>
    /// RPC 定义文件。使用 [LidRPC] 属性，定义即用。
    /// </summary>
    public static class RpcDefinitions
    {
        // ============ 角色同步 ============

        /// <summary>
        /// **早到的职业 RPC 挂起表**（playerId → (roleId, arguments)）。
        ///
        /// ⚠️ 为什么需要：房主在自己的 `RoleManager.SelectRoles` 里就把 N 条 `SetRole` 发出来了，
        ///    而客户端那一刻**可能还没建 Player 表** → 原来 `gamePlayer == null` 直接 `return`，
        ///    **静默丢弃且不重试** → 客户端整局没有职业，而房主日志一切正常 ✗
        ///    （AGENTS §4.2.3 记的"时机不对就晚点做"同款问题）
        ///    现在改成挂起，等 `GameManager.Initialize()` 建好表后由 <see cref="FlushPendingRoles"/> 重放。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<byte, (int RoleId, int[] Arguments)> PendingRoles = new();

        /// <summary>
        /// 房主下发职业。
        ///
        /// ⚠️ `OnlyHost = true`（2026-10-06 审查）：不加的话**任何客户端都能广播"我是召集者"** ——
        ///   接收端原来只按 hash 执行、不校验来源（`LidRPC.cs:122` 的 `OnlyHost` 检查是现成的闸门，白不用）。
        ///   这不只是"能作弊"，也让排查变得无从下手（日志里看起来是合法 RPC）。
        /// </summary>
        [LidRPC(OnlyHost = true)]
        public static void SetRole(byte playerId, int roleId, int[] arguments)
        {
            try
            {
                var definedRole = RoleRegistry.GetById(roleId);
                if (definedRole == null)
                {
                    // 原来是 LogWarning + return，太轻了：这等于"这个玩家的职业没了"
                    LightLogger.LogError($"[RPC] SetRole 收到未知角色 Id={roleId}（playerId={playerId}）");
                    return;
                }

                var gamePlayer = Game.GameManager.Instance.GetPlayer(playerId);
                if (gamePlayer == null)
                {
                    PendingRoles[playerId] = (roleId, arguments);
                    LightLogger.Log($"[RPC] SetRole({playerId},{definedRole.CodeName}) 早到 → 挂起，" +
                                    $"等 Player 表就绪后重放（当前挂起 {PendingRoles.Count} 条）");
                    return;
                }

                ApplyRole(gamePlayer, definedRole, arguments);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SetRole", ex);
            }
        }

        /// <summary>真正落到玩家身上（SetRole 与重放共用）。</summary>
        private static void ApplyRole(Game.Player gamePlayer, RoleTemplate definedRole, int[] arguments)
        {
            gamePlayer.SetRoleLocal(definedRole, arguments);
            Game.LightPlayerDataManager.SetRole(gamePlayer.Control.PlayerId, definedRole.Name);

            // ★ 中立职业的任务不计入进度（PR 带来的逻辑）。
            //   ⚠️ 刻意放在这个**共用落地函数**里，而不是只写在 SetRole 里：
            //      早到的职业 RPC 是走 FlushPendingRoles → ApplyRole 重放的，
            //      写在上面就会让"重放的那批玩家"漏掉这一步。
            if (definedRole.RoleCategory == Configuration.RoleCategory.Neutral
                && gamePlayer.Control?.Data?.Role != null)
                gamePlayer.Control.Data.Role.TasksCountTowardProgress = false;

            EventTriggers.OnPlayerRoleSet(gamePlayer.Control, gamePlayer.Role);
        }

        /// <summary>
        /// 重放挂起的职业 RPC —— **由 `GameManager.Initialize()` 在玩家表建好后调用**。
        /// 没有这一步，早到的职业就永久丢了（客户端表现为"整局没职业"）。
        /// </summary>
        public static void FlushPendingRoles()
        {
            if (PendingRoles.Count == 0) return;
            try
            {
                var pending = new List<KeyValuePair<byte, (int RoleId, int[] Arguments)>>(PendingRoles);
                PendingRoles.Clear();

                int ok = 0, lost = 0;
                foreach (var kv in pending)
                {
                    var role = RoleRegistry.GetById(kv.Value.RoleId);
                    var player = Game.GameManager.Instance.GetPlayer(kv.Key);
                    if (role == null || player == null) { lost++; continue; }
                    ApplyRole(player, role, kv.Value.Arguments);
                    ok++;
                }

                LightLogger.Log($"[RPC] 重放挂起的职业 RPC：成功 {ok} 条" + (lost > 0 ? $"，{lost} 条找不到玩家/职业（已丢弃）" : ""));
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.FlushPendingRoles", ex);
            }
        }

        // ============ 玩家操作 ============

        [LidRPC]
        public static void Suicide(PlayerControl player, bool needLog = true, string state = "suicide", PlayerState playerState = PlayerState.Suicide)
        {
            try
            {
                if (player == null || player.Data.IsDead) return;
                player.RpcMurderPlayer(player, true);
                EventTriggers.OnPlayerSuicide(player, state, playerState, needLog);
                LightPlayerDataManager.SetDeath(player.PlayerId, playerState, player.PlayerId, LightPlayerDataManager.CurrentMeetingNumber);
                if (needLog) LightLogger.Log($"[RPC] {player.name} suicide. state:{state}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.Suicide", ex);
            }
        }

        [LidRPC]
        public static void MurderPlayer(PlayerControl killer, PlayerControl victim, PlayerState state = PlayerState.BeKilled)
        {
            try
            {
                if (killer == null || victim == null) return;
                killer.RpcMurderPlayer(victim, true);
                EventTriggers.OnPlayerMurder(killer, victim, state);
                LightPlayerDataManager.SetDeath(victim.PlayerId, state, killer.PlayerId, LightPlayerDataManager.CurrentMeetingNumber);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.MurderPlayer", ex);
            }
        }

        /// <summary>
        /// 同步**职业数据位**（对齐 Nebula 的 roleData 同步；房主权威）。
        /// 职业状态不用再自己写 RPC —— 用 <see cref="RoleData.Set"/>，框架走这条。
        /// </summary>
        [LidRPC(OnlyHost = true)]
        public static void SetRoleData(byte playerId, int dataId, int value)
        {
            try
            {
                RoleData.Apply(playerId, dataId, value);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SetRoleData", ex);
            }
        }

        /// <summary>恢复玩家（取消死亡状态）</summary>
        [LidRPC(OnlyHost = true)]
        public static void RevivePlayer(byte playerId)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId && pc.Data.IsDead)
                    {
                        pc.Revive();
                        // ⚠️ 这里**不再手动发** OnPlayerRevive（2026-10-06 审查 #9）：
                        //    复活事件改由 `PlayerControl.Revive` 的补丁统一派发（见 RoleInterceptPatch.RevivePatch）→
                        //    原版路径 / 其它模组的复活也能触发；这里再发一次就是同一次复活发两遍。
                        LightLogger.Log($"[RPC] {pc.name} 已复活");
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.RevivePlayer", ex);
            }
        }

        /// <summary>设置玩家可见性</summary>
        [LidRPC]
        public static void SetPlayerInvisible(byte playerId, bool invisible)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId)
                    {
                        pc.SetInvisibility(invisible);
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SetPlayerInvisible", ex);
            }
        }

        /// <summary>传送玩家到指定位置</summary>
        [LidRPC(OnlyHost = true)]
        public static void TeleportPlayer(byte playerId, Vector2 position)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId)
                    {
                        var pos = new UnityEngine.Vector3(position.x, position.y, pc.transform.position.z);
                        pc.NetTransform.SnapTo(pos);
                        LightLogger.Log($"[RPC] {pc.name} 传送到 {position}");
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.TeleportPlayer", ex);
            }
        }

        // ============ 会议 ============

        [LidRPC]
        public static void StartMeeting(byte reporterId, byte reportedId)
        {
            try
            {
                if (MeetingHud.Instance != null) return;
                PlayerControl reporter = null;
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == reporterId) { reporter = pc; break; }

                if (reporter == null) return;

                NetworkedPlayerInfo reportedBody = reportedId == byte.MaxValue ? null : null; // TODO
                reporter.RpcStartMeeting(reportedBody);
                LightLogger.Log($"[RPC] 会议开始: reporter={reporter.name}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.StartMeeting", ex);
            }
        }

        [LidRPC]
        public static void RpcStartMeeting()
        {
            try
            {
                var local = PlayerControl.LocalPlayer;
                if (local == null) return;
                if (MeetingHud.Instance != null) return;
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

                MeetingRoomManager.Instance.AssignSelf(local, null);
                HudManager.Instance.OpenMeetingRoom(local);
                local.RpcStartMeeting(null);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.RpcStartMeeting", ex);
            }
        }

        [LidRPC]
        public static void ForceEndGameInvalid()
        {
            LightInDark.Game.EndGameManager.MarkInvalid();

            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;

            LightInDark.Game.EndGameManager.TryEndGame(LightInDark.Game.GameEndReason.Invalid);
        }

        [LidRPC(OnlyHost = true)]
        public static void ForceEndMeeting()
        {
            try
            {
                if (MeetingHud.Instance == null) return;
                // 简单关闭会议
                MeetingHud.Instance.gameObject.SetActive(false);
                LightLogger.Log("[RPC] 强制结束会议");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.ForceEndMeeting", ex);
            }
        }

        [LidRPC(OnlyHost = true)]
        public static void BreakEmergencyButton()
        {
            try
            {
                if (ShipStatus.Instance != null)
                {
                    ShipStatus.Instance.BreakEmergencyButton();
                    EventTriggers.OnEmergencyButtonBroken();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.BreakEmergencyButton", ex);
            }
        }

        // ============ 聊天 ============

        /// <summary>免费聊天显示状态变更（由主插件订阅实现）</summary>
        public static event Action<bool> OnFreeChatStateChanged;

        [LidRPC]
        public static void ShowChat()
        {
            try
            {
                OnFreeChatStateChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.ShowChat", ex);
            }
        }

        [LidRPC]
        public static void HideChat()
        {
            try
            {
                OnFreeChatStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.HideChat", ex);
            }
        }

        [LidRPC]
        public static void SendChatMessage(byte playerId, string message)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId)
                    {
                        HudManager.Instance?.Chat?.AddChat(pc, message, false);
                        EventTriggers.OnChatMessage(pc, message);
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SendChatMessage", ex);
            }
        }

        // ============ 踢出 ============

        [LidRPC(OnlyHost = true)]
        public static void KickPlayer(byte playerId)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId)
                    {
                        var client = Utilities.LightUtils.GetClient(pc);
                        if (client != null) AmongUsClient.Instance.KickPlayer(client.Id, false);
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.KickPlayer", ex);
            }
        }

        [LidRPC]
        public static void SetKickReason(byte targetPlayerId, string reason)
        {
            try
            {
                if (PlayerControl.LocalPlayer?.PlayerId == targetPlayerId)
                {
                    Utilities.LightUtils.KickManager.kickReason = reason;
                    Utilities.LightUtils.KickManager.kickReasonWaitUntil = Time.realtimeSinceStartup + 30f;
                    Utilities.LightUtils.KickManager.kickReasonConsumeUntil = 0f;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SetKickReason", ex);
            }
        }

        // ============ 职业预定（/up） ============

        /// <summary>玩家预定下一局强制分配的职业（发送者 → 房主处理）</summary>
        [LidRPC]
        public static void RequestPinnedRole(byte senderPlayerId, string roleName)
        {
            try
            {
                if (!AmongUsClient.Instance.AmHost) return;
                Roles.Assignment.RolePinManager.HandleRequest(senderPlayerId, roleName);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.RequestPinnedRole", ex);
            }
        }

        /// <summary>取消职业预定（发送者 → 房主处理）</summary>
        [LidRPC]
        public static void CancelPinnedRole(byte senderPlayerId)
        {
            try
            {
                if (!AmongUsClient.Instance.AmHost) return;
                Roles.Assignment.RolePinManager.HandleCancel(senderPlayerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.CancelPinnedRole", ex);
            }
        }

        /// <summary>
        /// 向指定玩家聊天框显示一条系统消息（仅目标玩家本地显示）。
        ///
        /// ⚠️ 2026-10-06 审查 B12：`SelectRoles` 阶段（`HudManager.Chat` 还没就绪）发出的提示
        ///   原来**直接 return 丢弃** → 玩家永远看不到，而对应状态（比如"预定已被消耗"）早已生效 ✗
        ///   现在改成**挂起 + 每帧补发**（AGENTS §4.2.3 的"时机不对就晚点做"）。
        /// ⚠️ 顺带修掉 `HudManager.Instance?.Chat`：`HudManager` 是 Unity 对象，假 null 时 `?.` 挡不住（§4.6.1）✗
        /// </summary>
        [LidRPC]
        public static void ShowSystemMessage(byte targetPlayerId, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message)) return;

                switch (TryShowSystemMessage(targetPlayerId, message))
                {
                    case ShowResult.Shown:
                    case ShowResult.NotForMe:      // 不是给我的 → 直接丢（绝不能排队，否则会越堆越多）
                        return;

                    case ShowResult.NotReady:
                        if (!_pendingMessages.Contains((targetPlayerId, message)))
                        {
                            _pendingMessages.Add((targetPlayerId, message));
                            LightLogger.Log($"[RPC] 系统消息挂起（聊天框未就绪）：{message}（待发 {_pendingMessages.Count} 条）");
                        }
                        return;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.ShowSystemMessage", ex);
            }
        }

        private enum ShowResult { Shown, NotForMe, NotReady }

        private static readonly List<(byte PlayerId, string Message)> _pendingMessages = new();

        /// <summary>尝试立刻显示：`NotForMe` = 目标不是我；`NotReady` = 聊天框还没就绪（可延后重试）。</summary>
        private static ShowResult TryShowSystemMessage(byte targetPlayerId, string message)
        {
            var pc = PlayerControl.LocalPlayer;
            if (pc == null || pc.PlayerId != targetPlayerId) return ShowResult.NotForMe;

            var hud = HudManager.Instance;
            if (hud == null) return ShowResult.NotReady;
            var chat = hud.Chat;
            if (chat == null) return ShowResult.NotReady;

            string orig = pc.name;
            pc.SetName("System");
            chat.AddChat(pc, message, false);
            pc.SetName(orig);
            return ShowResult.Shown;
        }

        /// <summary>每帧补发挂起的系统消息（由 `PlayerControl.FixedUpdate` 补丁驱动）。</summary>
        internal static void TickPendingMessages()
        {
            if (_pendingMessages.Count == 0) return;

            try
            {
                for (int i = _pendingMessages.Count - 1; i >= 0; i--)
                {
                    var (pid, msg) = _pendingMessages[i];
                    if (TryShowSystemMessage(pid, msg) != ShowResult.Shown) continue;   // 还没就绪 → 留到下帧
                    _pendingMessages.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.TickPendingMessages", ex);
            }
        }

        [LidRPC(OnlyHost = true)]
        public static void KickPlayerWithReason(byte playerId, string reason)
        {
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                    if (pc.PlayerId == playerId)
                    {
                        if (PlayerControl.LocalPlayer.PlayerId == playerId)
                        {
                            Utilities.LightUtils.KickManager.kickReason = reason;
                            Utilities.LightUtils.KickManager.kickReasonConsumeUntil = Time.realtimeSinceStartup + 5f;
                        }
                        var client = Utilities.LightUtils.GetClient(pc);
                        if (client != null) AmongUsClient.Instance.KickPlayer(client.Id, false);
                        break;
                    }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.KickPlayerWithReason", ex);
            }
        }
    }

    // =====================================================================
    // 可复用的 static 方法
    // =====================================================================

    /// <summary>
    /// 游戏操作工具。提供常用操作的静态方法。
    /// </summary>
    public static class GameActions
    {
        /// <summary>分配角色给玩家（同步）</summary>
        public static void AssignRole(Game.Player player, RoleTemplate role)
        {
            try
            {
                player.SetRole(role, role.DefaultArguments);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.AssignRole", ex);
            }
        }

        /// <summary>让玩家自杀</summary>
        public static void KillSelf(PlayerControl player, string reason = "suicide")
        {
            try
            {
                RpcDefinitions.Suicide(player, true, reason);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.KillSelf", ex);
            }
        }

        /// <summary>击杀玩家</summary>
        public static void Murder(PlayerControl killer, PlayerControl victim)
        {
            try
            {
                RpcDefinitions.MurderPlayer(killer, victim);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.Murder", ex);
            }
        }

        /// <summary>复活玩家（仅房主）</summary>
        public static void Revive(PlayerControl player)
        {
            try
            {
                RpcDefinitions.RevivePlayer(player.PlayerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.Revive", ex);
            }
        }

        /// <summary>传送玩家</summary>
        public static void Teleport(PlayerControl player, Vector2 position)
        {
            try
            {
                RpcDefinitions.TeleportPlayer(player.PlayerId, position);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.Teleport", ex);
            }
        }

        /// <summary>隐身/显形</summary>
        public static void SetInvisible(PlayerControl player, bool invisible)
        {
            try
            {
                RpcDefinitions.SetPlayerInvisible(player.PlayerId, invisible);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.SetInvisible", ex);
            }
        }

        /// <summary>召开紧急会议</summary>
        public static void StartMeeting()
        {
            try
            {
                RpcDefinitions.RpcStartMeeting();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.StartMeeting", ex);
            }
        }

        /// <summary>强制结束会议</summary>
        public static void ForceEndMeeting()
        {
            try
            {
                RpcDefinitions.ForceEndMeeting();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.ForceEndMeeting", ex);
            }
        }

        /// <summary>破坏紧急按钮</summary>
        public static void BreakEmergencyButton()
        {
            try
            {
                RpcDefinitions.BreakEmergencyButton();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.BreakEmergencyButton", ex);
            }
        }

        /// <summary>显示聊天</summary>
        public static void ShowChat()
        {
            try
            {
                RpcDefinitions.ShowChat();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.ShowChat", ex);
            }
        }

        /// <summary>隐藏聊天</summary>
        public static void HideChat()
        {
            try
            {
                RpcDefinitions.HideChat();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.HideChat", ex);
            }
        }

        /// <summary>发送聊天消息</summary>
        public static void SendMessage(PlayerControl player, string message)
        {
            try
            {
                RpcDefinitions.SendChatMessage(player.PlayerId, message);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.SendMessage", ex);
            }
        }

        /// <summary>踢出玩家</summary>
        public static void Kick(PlayerControl player, string reason = "")
        {
            try
            {
                if (!string.IsNullOrEmpty(reason))
                    RpcDefinitions.SetKickReason(player.PlayerId, reason);
                RpcDefinitions.KickPlayer(player.PlayerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.Kick", ex);
            }
        }

        /// <summary>获取所有存活玩家</summary>
        public static System.Collections.Generic.IEnumerable<PlayerControl> AlivePlayers()
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc != null && !pc.Data.IsDead) yield return pc;
        }

        /// <summary>获取所有内鬼</summary>
        public static System.Collections.Generic.IEnumerable<PlayerControl> Impostors()
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc?.Data?.Role?.IsImpostor == true) yield return pc;
        }

        /// <summary>获取最近玩家</summary>
        public static PlayerControl GetClosestPlayer(PlayerControl source, float maxDistance = 2f)
        {
            try
            {
                PlayerControl closest = null;
                float closestDist = maxDistance;
                foreach (var pc in AlivePlayers())
                {
                    if (pc == source) continue;
                    float dist = Vector2.Distance(source.transform.position, pc.transform.position);
                    if (dist < closestDist) { closestDist = dist; closest = pc; }
                }
                return closest;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("GameActions.GetClosestPlayer", ex);
                return null;
            }
        }

        /// <summary>获取本地玩家</summary>
        public static Game.Player LocalPlayer => Game.GameManager.Instance?.LocalPlayer;

        /// <summary>是否是房主</summary>
        public static bool IsHost => AmongUsClient.Instance?.AmHost ?? false;

        /// <summary>是否在游戏中</summary>
        public static bool InGame => AmongUsClient.Instance?.GameState == InnerNet.InnerNetClient.GameStates.Started;
    }
}
