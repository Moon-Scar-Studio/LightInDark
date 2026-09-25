using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using System;
using UnityEngine;

namespace LightInDark.RPCs
{
    /// <summary>
    /// RPC 定义文件。使用 [LidRPC] 属性，定义即用。
    /// </summary>
    public static class RpcDefinitions
    {
        // ============ 角色同步 ============

        [LidRPC]
        public static void SetRole(byte playerId, int roleId, int[] arguments)
        {
            try
            {
                var definedRole = RoleRegistry.GetById(roleId);
                if (definedRole == null) { LightLogger.LogWarning($"[RPC] 未知角色Id: {roleId}"); return; }
                var gamePlayer = Game.GameManager.Instance.GetPlayer(playerId);
                if (gamePlayer == null) return;
                gamePlayer.SetRoleLocal(definedRole, arguments);
                Game.LightPlayerDataManager.SetRole(playerId, definedRole.Name);
                EventTriggers.OnPlayerRoleSet(gamePlayer.Control, gamePlayer.Role);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.SetRole", ex);
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
                        EventTriggers.OnPlayerRevive(pc);
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
                PlayerControl.LocalPlayer?.RpcStartMeeting(null);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RpcDefinitions.RpcStartMeeting", ex);
            }
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
        public static void AssignRole(Game.Player player, Role role)
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
