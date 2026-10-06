using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using Light.Roles.Assignment;
using UnityEngine;

namespace Light.Patches
{
    /// <summary>
    /// 原版逻辑拦截 + 事件注入。
    /// 阻挡原版绝大多数逻辑，用 Harmony 注入新逻辑。
    /// </summary>

    // =====================================================================
    // 游戏流程
    // =====================================================================

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
    public static class GameManagerStartPatch
    {
        public static void Postfix(GameManager __instance)
        {
            try
            {
                // 保持原版结束检查开启（ShouldCheckForGameEnd = true 由原版 StartGame 设置）。
                // 若强制置 false，原版 CheckEndCriteria 永不触发 RpcEndGame -> 游戏无法正常结束。
                // 自定义胜负在 RpcEndGame / AmongUsClient.OnGameEnd 层拦截处理。
                // 玩家数据已在 SelectRoles 阶段初始化（此时玩家已生成、角色已分配）。
                EventTriggers.OnIntroEnd();
                EventTriggers.OnGameStart(PlayerControl.AllPlayerControls.Count);
                LightInDark.Game.EndGameManager.OnNewGameStart();
                LightInDark.Modifiers.ModifierManager.ClearAll();
                LightLogger.Log("[Patch] 游戏开始，已保持原版结束检查开启，自定义胜负在 RpcEndGame 层处理");
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckTaskCompletion))]
    public static class BlockTaskCompletionPatch
    {
        public static bool Prefix(ref bool __result)
        {
            try
            {
                __result = false;
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckEndGameViaTasks))]
    public static class BlockEndGameViaTasksPatch
    {
        public static bool Prefix(GameManager __instance)
        {
            try
            {
                var ev = new GameTryEndEvent { CrewmatesWin = true, Reason = "task" };
                EventSystem.RunEvent(ev);
                // 放行原版：全部任务完成时原版 CheckEndGameViaTasks 会调用 RpcEndGame 结束游戏。
                // 只有自定义逻辑明确取消时才拦下。
                return !ev.IsCanceled;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.IsGameOverDueToDeath))]
    public static class BlockGameOverDueToDeathPatch
    {
        public static bool Prefix(ref bool __result)
        {
            try
            {
                __result = false;
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    // =====================================================================
    // 角色分配
    // =====================================================================

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    public static class RoleSelectPatch
    {
        /// <summary>
        /// 原版跑完之后再做模组分配（同 TORV：Postfix + 读原版已经选出的内鬼）。
        /// RoleType、任务表、开场流程都交给原版，模组只在上面叠加自定义职业。
        /// </summary>
        public static void Postfix()
        {
            try
            {
                LightInDark.Game.LightPlayerDataManager.Initialize();
                LightInDark.Game.GameManager.Instance.Initialize();

                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

                EventTriggers.OnRoleSelectionBegin(PlayerControl.AllPlayerControls?.Count ?? 0);

                var impostors = new List<byte>();
                var others = new List<byte>();

                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc?.Data?.Role == null) continue;
                    if (pc.isDummy) continue;
                    if (pc.Data.Role.IsImpostor) impostors.Add(pc.PlayerId);
                    else others.Add(pc.PlayerId);
                }

                new StandardRoleAllocator().Assign(impostors, others);
            }
            catch (System.Exception ex)
            {
                LightLogger.LogError("[RoleSelectPatch.Postfix]", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.AssignRoleOnDeath))]
    public static class BlockGhostRolePatch
    {
        public static bool Prefix()
        {
            try
            {
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(RoleBehaviour), nameof(RoleBehaviour.Initialize))]
    public static class BlockRoleInitializePatch
    {
        public static void Postfix(RoleBehaviour __instance)
        {
            try
            {
                if (HudManager.Instance != null && HudManager.Instance.AbilityButton != null)
                    HudManager.Instance.AbilityButton.gameObject.SetActive(false);
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(RoleBehaviour), nameof(RoleBehaviour.InitializeAbilityButton))]
    public static class BlockAbilityButtonPatch
    {
        public static bool Prefix()
        {
            try
            {
                if (HudManager.Instance != null && HudManager.Instance.AbilityButton != null)
                    HudManager.Instance.AbilityButton.gameObject.SetActive(false);
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    // =====================================================================
    // 玩家死亡/击杀
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Die))]
    public static class PlayerDeathPatch
    {
        public static void Postfix(PlayerControl __instance, DeathReason reason)
        {
            try
            {
                EventTriggers.OnPlayerDeath(__instance, reason);
                // 死亡状态已由 RpcDefinitions.Suicide/MurderPlayer 记录到 LightPlayerDataManager
                // 此处仅在尚未记录时补充（如原版直接触发的死亡）
                var existing = LightPlayerDataManager.GetData(__instance.PlayerId);
                if (existing != null && !existing.IsDead)
                {
                    // 根据 vanilla DeathReason 映射到 PlayerState
                    // 0=Kill, 1=Exile, 2=Disconnect
                    PlayerState state = ((int)reason == 1) ? PlayerState.Exile : PlayerState.Dead;
                    LightPlayerDataManager.SetDeath(
                        __instance.PlayerId, state, null, LightPlayerDataManager.CurrentMeetingNumber);
                }
            }
            catch (System.Exception)
            {
            }
        }
    }

    // =====================================================================
    // 管道 — AU 方法签名不同，暂时跳过
    // =====================================================================

    // =====================================================================
    // 任务完成
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcCompleteTask))]
    public static class TaskCompletePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                int completed = 0, total = 0;
                if (__instance.Data?.Tasks != null)
                {
                    total = __instance.Data.Tasks.Count;
                    foreach (var task in __instance.Data.Tasks)
                        if (task != null && task.Complete) completed++;
                }
                EventTriggers.OnTaskComplete(__instance, completed, total);
                
                LightPlayerDataManager.UpdateTaskProgress(__instance.PlayerId, completed, total);
            }
            catch (System.Exception)
            {
            }
        }
    }

    // =====================================================================
    // 会议
    // =====================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
    public static class ReportDeadBodyPatch
    {
        public static void Postfix(PlayerControl __instance, NetworkedPlayerInfo target)
        {
            try
            {
                bool isEmergency = target == null;
                EventTriggers.OnMeetingStart(__instance, target, isEmergency);
                LightLogger.Log($"[Patch] {(isEmergency ? "紧急会议" : "尸体报告")} by {__instance.name}");
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Confirm))]
    public static class MeetingVotePatch
    {
        public static void Postfix(MeetingHud __instance, byte suspectStateIdx)
        {
            try
            {
                EventTriggers.OnPlayerVote(PlayerControl.LocalPlayer, suspectStateIdx);
            }
            catch (System.Exception)
            {
            }
        }
    }

    // =====================================================================
    // 断线
    // =====================================================================

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    public static class PlayerLeftPatch
    {
        public static void Postfix(AmongUsClient __instance, ClientData data)
        {
            try
            {
                if (data?.Character != null)
                {
                    EventTriggers.OnPlayerDisconnect(data.Character);
                    LightPlayerDataManager.SetDisconnected(data.Character.PlayerId);
                }
            }
            catch (System.Exception)
            {
            }
        }
    }

    // =====================================================================
    // 放逐
    // =====================================================================

    [HarmonyPatch(typeof(ExileController), nameof(ExileController.Begin))]
    public static class ExileBeginPatch
    {
        public static void Postfix(ExileController __instance)
        {
            try
            {
                LightLogger.Log("[Patch] 放逐动画开始");
                LightPlayerDataManager.CurrentMeetingNumber++;
                EventTriggers.OnPlayerExile(null);
            }
            catch (System.Exception)
            {
            }
        }
    }

    // =====================================================================
    // 紧急按钮
    // =====================================================================

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.BreakEmergencyButton))]
    public static class EmergencyButtonBrokenPatch
    {
        public static void Postfix()
        {
            try
            {
                EventTriggers.OnEmergencyButtonBroken();
            }
            catch (System.Exception)
            {
            }
        }
    }
    [HarmonyPatch(typeof(PlayerControl),nameof(PlayerControl.RpcMurderPlayer))]
    public static class PlayerTryMurderPatch
    {
        public static bool Prefix()
        {
            bool needCanceled = false;
            return true;
        }
    }
}
