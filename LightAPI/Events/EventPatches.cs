using HarmonyLib;
using InnerNet;
using LightInDark.Core;
using System;
using UnityEngine;

namespace LightInDark.Events
{
    // =====================================================================
    //  游戏帧更新事件
    // =====================================================================

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class GameUpdatePatch
    {
        public static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance?.GameState != InnerNetClient.GameStates.Started) return;
                EventTriggers.OnGameUpdate(Time.deltaTime);
                EventTriggers.OnGameHudUpdate(Time.deltaTime);
                EventTriggers.OnGameLateUpdate(Time.deltaTime);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] GameUpdatePatch", ex); }
        }
    }

    // =====================================================================
    //  HUD 激活状态
    // =====================================================================

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive),
        typeof(PlayerControl), typeof(RoleBehaviour), typeof(bool))]
    public static class HudActivePatch
    {
        public static void Postfix(bool isActive)
        {
            try { EventTriggers.OnHudActiveChange(isActive); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] HudActivePatch", ex); }
        }
    }

    // =====================================================================
    //  玩家移动 / 击杀
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    public static class PlayerMovePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (__instance == null || __instance.Data == null || __instance.Data.IsDead) return;
                EventTriggers.OnPlayerMove(__instance, __instance.transform.position);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerMovePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    public static class PlayerMurderPatch
    {
        public static bool Prefix(PlayerControl __instance, PlayerControl target)
        {
            try
            {
                if (target == null) return true;
                var canKill = EventTriggers.OnPlayerCheckCanKill(__instance, target);
                if (!canKill.CanKill)
                {
                    EventTriggers.OnPlayerGuard(target, __instance);
                    return false;
                }
                if (!EventTriggers.OnPlayerTryVanillaKill(__instance, target))
                    return false;
                return true;
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerMurderPatch.Prefix", ex); return true; }
        }

        public static void Postfix(PlayerControl __instance, PlayerControl target)
        {
            try
            {
                if (target == null) return;
                EventTriggers.OnPlayerTryMurder(__instance, target);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerMurderPatch.Postfix", ex); }
        }
    }

    /// <summary>击杀冷却重置。</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetKillTimer))]
    public static class ResetKillCooldownPatch
    {
        public static void Prefix(PlayerControl __instance, ref float time)
        {
            try
            {
                if (__instance != PlayerControl.LocalPlayer) return;

                var ev = EventTriggers.OnResetKillCooldown(__instance);
                if (!ev.UseDefaultCooldown && ev.FixedCooldown.HasValue)
                    time = ev.FixedCooldown.Value;
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ResetKillCooldownPatch", ex); }
        }
    }

    // =====================================================================
    //  踢出
    // =====================================================================

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.KickPlayer))]
    public static class PlayerKickPatch
    {
        public static void Prefix(int clientId, bool ban)
        {
            try
            {
                var player = AmongUsClient.Instance?.GetClient(clientId)?.Character;
                if (player == null) return;
                EventTriggers.OnPlayerKick(player, null, ban ? "banned" : "kicked");
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerKickPatch", ex); }
        }
    }

    // =====================================================================
    //  小游戏 / 控制台
    // =====================================================================

    [HarmonyPatch(typeof(Console), nameof(Console.Use))]
    public static class ConsoleUsePatch
    {
        public static void Postfix(Console __instance)
        {
            try
            {
                var pc = PlayerControl.LocalPlayer;
                if (pc == null) return;
                EventTriggers.OnPlayerBeginMinigameByConsole(pc, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ConsoleUsePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(DoorConsole), nameof(DoorConsole.Use))]
    public static class DoorConsoleUsePatch
    {
        public static void Postfix(DoorConsole __instance)
        {
            try
            {
                var pc = PlayerControl.LocalPlayer;
                if (pc == null) return;
                EventTriggers.OnPlayerBeginMinigameByDoor(pc, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] DoorConsoleUsePatch", ex); }
        }
    }

    // =====================================================================
    //  任务
    // =====================================================================

    [HarmonyPatch(typeof(NormalPlayerTask), nameof(NormalPlayerTask.Initialize))]
    public static class TaskInitializePatch
    {
        public static void Postfix(NormalPlayerTask __instance)
        {
            try
            {
                if (__instance.Owner == null) return;
                EventTriggers.OnPlayerGetTask(__instance.Owner, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] TaskInitializePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerTask), nameof(PlayerTask.Complete))]
    public static class TaskCompleteUpdatePatch
    {
        public static void Postfix(PlayerTask __instance)
        {
            try
            {
                if (__instance.Owner == null) return;
                EventTriggers.OnTaskUpdate(__instance.Owner);

                int completed = 0, total = 0;
                if (__instance.Owner.Data?.Tasks != null)
                {
                    total = __instance.Owner.Data.Tasks.Count;
                    foreach (var t in __instance.Owner.Data.Tasks)
                        if (t != null && t.Complete) completed++;
                }
                if (total > 0 && completed >= total)
                    EventTriggers.OnAllTasksComplete(__instance.Owner);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] TaskCompleteUpdatePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerTask), nameof(PlayerTask.OnRemove))]
    public static class PlayerTaskRemovePatch
    {
        public static void Postfix(PlayerTask __instance)
        {
            try
            {
                var owner = __instance.Owner;
                if (owner == null) return;
                EventTriggers.OnPlayerTaskRemove(owner, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerTaskRemovePatch", ex); }
        }
    }

    // =====================================================================
    //  会议
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
    public static class ReportDeadBodyEventPatch
    {
        public static bool Prefix(PlayerControl __instance, NetworkedPlayerInfo target)
        {
            try
            {
                bool isEmergency = target == null;
                if (isEmergency)
                {
                    var check = EventTriggers.OnCheckCanPushEmergencyButton();
                    if (!check.CanPushButton) return false;
                }
                if (!EventTriggers.OnMeetingTryStart(__instance, target, isEmergency))
                    return false;
                return true;
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ReportDeadBodyEventPatch.Prefix", ex); return true; }
        }

        public static void Postfix(PlayerControl __instance, NetworkedPlayerInfo target)
        {
            try
            {
                bool isEmergency = target == null;
                PlayerControl reported = null;
                if (target != null)
                {
                    foreach (var pc in PlayerControl.AllPlayerControls)
                        if (pc.PlayerId == target.PlayerId) { reported = pc; break; }
                }
                EventTriggers.OnMeetingPreStart(__instance, reported);
                if (isEmergency)
                    EventTriggers.OnCalledEmergencyMeeting(__instance);
                else
                    EventTriggers.OnReportDeadBody(__instance, reported);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ReportDeadBodyEventPatch.Postfix", ex); }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    public static class MeetingStartPatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMeetingDiscussionStart(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingStartPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Confirm))]
    public static class MeetingVoteCastPatch
    {
        public static void Postfix(byte suspectStateIdx)
        {
            try
            {
                PlayerControl voteFor = null;
                if (suspectStateIdx < 254)
                {
                    foreach (var pc in PlayerControl.AllPlayerControls)
                        if (pc.PlayerId == suspectStateIdx) { voteFor = pc; break; }
                }
                EventTriggers.OnPlayerVoteCast(PlayerControl.LocalPlayer, voteFor);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingVoteCastPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
    public static class MeetingClosePatch
    {
        public static bool Prefix()
        {
            if (!EventTriggers.OnMeetingTryEnd()) return false;
            EventTriggers.OnMeetingPreEnd();
            return true;
        }

        public static void Postfix(MeetingHud __instance)
        {
            try
            {
                byte exiledId = byte.MaxValue;
                bool wasTie = true;
                if (__instance.exiledPlayer != null)
                {
                    exiledId = __instance.exiledPlayer.PlayerId;
                    wasTie = false;
                }
                EventTriggers.OnMeetingEnd(exiledId, wasTie);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingClosePatch.Postfix", ex); }
        }
    }

    /// <summary>投票阶段开始。</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.ServerStart))]
    public static class MeetingVotingStartPatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMeetingVotingStart(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingVotingStartPatch", ex); }
        }
    }

    /// <summary>投票结算判定前。</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
    public static class MeetingTryEndVotingPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            try
            {
                byte exiledId = byte.MaxValue;
                bool isTie = __instance.exiledPlayer == null;
                if (__instance.exiledPlayer != null)
                    exiledId = __instance.exiledPlayer.PlayerId;

                EventTriggers.OnMeetingTryEndVoting(exiledId, isTie);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingTryEndVotingPatch", ex); }
        }
    }

    /// <summary>投票结果公布。</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
    public static class MeetingVoteCompletePatch
    {
        public static void Postfix(MeetingHud.VoterState[] states)
        {
            try
            {
                EventTriggers.OnMeetingVoteEnd(states);
                EventTriggers.OnMeetingVoteDisclosed(states);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MeetingVoteCompletePatch", ex); }
        }
    }

    /// <summary>有玩家投出票。</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
    public static class PlayerVotedPatch
    {
        public static void Postfix(PlayerId srcPlayerId, PlayerId suspectPlayerId)
        {
            try
            {
                if (AmongUsClient.Instance?.AmHost != true) return;

                var voted = FindPlayer((byte)suspectPlayerId);
                if (voted == null) return;

                var voters = new System.Collections.Generic.List<PlayerControl>();
                var voter = FindPlayer((byte)srcPlayerId);
                if (voter != null) voters.Add(voter);

                EventTriggers.OnPlayerVoted(voted, voters);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerVotedPatch", ex); }
        }

        private static PlayerControl FindPlayer(byte playerId)
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc != null && pc.PlayerId == playerId) return pc;
            return null;
        }
    }

    // =====================================================================
    //  放逐
    // =====================================================================

    [HarmonyPatch(typeof(ExileController), nameof(ExileController.Begin))]
    public static class ExileBeginEventPatch
    {
        public static void Prefix(ExileController __instance)
        {
            try
            {
                EventTriggers.OnExileScenePreStart(new System.Collections.Generic.List<PlayerControl>());

                var info = __instance.initData?.networkedPlayer;
                byte exiledId = byte.MaxValue;
                if (info != null) exiledId = info.PlayerId;
                EventTriggers.OnPlayerTryExile(exiledId, __instance.initData?.voteTie ?? false);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ExileBeginEventPatch.Prefix", ex); }
        }

        public static void Postfix()
        {
            try
            {
                var exiled = new System.Collections.Generic.List<PlayerControl>();
                EventTriggers.OnExileSceneStart(exiled);
                EventTriggers.OnFixExileText(exiled);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ExileBeginEventPatch.Postfix", ex); }
        }
    }

    // =====================================================================
    //  管道
    // =====================================================================

    [HarmonyPatch(typeof(Vent), nameof(Vent.Use))]
    public static class VentUsePatch
    {
        public static void Postfix(Vent __instance)
        {
            try
            {
                var pc = PlayerControl.LocalPlayer;
                if (pc == null) return;
                EventTriggers.OnVentUsed(pc, __instance.Id);
                if (pc.inVent)
                    EventTriggers.OnPlayerVentEnter(pc, __instance);
                else
                    EventTriggers.OnPlayerVentExit(pc, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] VentUsePatch", ex); }
        }
    }

    // =====================================================================
    //  地图
    // =====================================================================

    [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowNormalMap))]
    public static class MapOpenNormalPatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMapOpenNormal(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MapOpenNormalPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowSabotageMap))]
    public static class MapOpenSabotagePatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMapOpenSabotage(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MapOpenSabotagePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowCountOverlay))]
    public static class MapOpenAdminPatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMapOpenAdmin(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MapOpenAdminPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.Close))]
    public static class MapClosePatch
    {
        public static void Postfix()
        {
            try { EventTriggers.OnMapClose(); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] MapClosePatch", ex); }
        }
    }

    // =====================================================================
    //  破坏系统
    // =====================================================================

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem),
        typeof(SystemTypes), typeof(PlayerControl), typeof(byte))]
    public static class ShipSystemUpdatePatch
    {
        // 阿蒙古斯的破坏系统是由服务器端发包控制的，客户端只负责显示。所以这辈子也抓不到破坏者喵。
        public static void Postfix(SystemTypes systemType, PlayerControl player, byte amount)
        {
            try
            {
                if (systemType == SystemTypes.Doors)
                {
                    if (AmongUsClient.Instance?.AmHost != true || player == null) return;
                    EventSystem.RunEvent(new PlayerTryOpenDoorHostEvent(player, null));
                    return;
                }

                if (!IsCriticalSabotage(systemType)) return;
                if ((amount & 0x80) != 0)
                    EventTriggers.OnSabotageEnd(systemType);
                else
                    EventTriggers.OnSabotageStart(systemType);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] ShipSystemUpdatePatch", ex); }
        }

        private static bool IsCriticalSabotage(SystemTypes type)
            => type == SystemTypes.Reactor
            || type == SystemTypes.LifeSupp
            || type == SystemTypes.Comms
            || type == SystemTypes.Electrical
            || type == SystemTypes.MushroomMixupSabotage
            || type == SystemTypes.HeliSabotage;
    }

    // =====================================================================
    //  门
    // =====================================================================

    [HarmonyPatch(typeof(AutoOpenDoor), nameof(AutoOpenDoor.SetDoorway))]
    public static class DoorwayPatch
    {
        public static bool Prefix(AutoOpenDoor __instance, bool open)
        {
            try
            {
                if (!open) return true;
                var player = PlayerControl.LocalPlayer;
                if (player == null) return true;

                var ev = new PlayerTryOpenDoorLocalEvent(player, __instance);
                EventSystem.RunEvent(ev);
                return !ev.IsCanceled;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[EventPatch] DoorwayPatch.Prefix", ex);
                return true;
            }
        }

        public static void Postfix(AutoOpenDoor __instance, bool open)
        {
            try
            {
                if (!open) return;
                var player = PlayerControl.LocalPlayer;
                if (player == null) return;
                EventTriggers.OnPlayerOpenDoor(player, __instance);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] DoorwayPatch.Postfix", ex); }
        }
    }

    // =====================================================================
    //  玩家视觉（每帧）
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    public static class PlayerVisualPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (__instance == null || __instance.Data == null) return;

                var newVis = __instance.Data.IsDead
                    ? PlayerUpdateVisibilityEvent.VisibilityLevel.SemiTransparent
                    : PlayerUpdateVisibilityEvent.VisibilityLevel.Visible;
                EventTriggers.OnPlayerUpdateVisibility(__instance, newVis, PlayerUpdateVisibilityEvent.VisibilityLevel.Visible);

                var rend = __instance.cosmetics?.currentBodySprite?.BodySprite;
                float alpha = rend != null ? rend.color.a : 1f;
                EventTriggers.OnPlayerAlphaUpdate(__instance, alpha);

                if (__instance == PlayerControl.LocalPlayer)
                {
                    EventTriggers.OnPlayerUpdateVentState(__instance);
                    EventTriggers.OnPlayerCheckPlayFootSound(__instance);
                }
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerVisualPatch", ex); }
        }
    }

    /// <summary>名字装饰。可改显示名与颜色。</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RawSetName))]
    public static class PlayerDecorateNamePatch
    {
        public static void Prefix(PlayerControl __instance, ref string name)
        {
            try
            {
                if (__instance == null) return;

                var ev = EventTriggers.OnPlayerDecorateName(__instance, name);
                if (!string.IsNullOrEmpty(ev.Name)) name = ev.Name;

                if (ev.NameColor.HasValue && __instance.cosmetics?.nameText != null)
                    __instance.cosmetics.nameText.color = ColorHelper.ToUnityColor(ev.NameColor.Value);
            }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] PlayerDecorateNamePatch", ex); }
        }
    }

    // =====================================================================
    //  本地任务完成
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcCompleteTask))]
    public static class LocalTaskCompletePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try { EventTriggers.OnTaskCompleteLocal(__instance); }
            catch (Exception ex) { LightLogger.LogError("[EventPatch] LocalTaskCompletePatch", ex); }
        }
    }
}
