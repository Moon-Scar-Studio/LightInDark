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
        /// 在原版 SelectRoles 之后执行：以原版实际选出的内鬼为准做自定义职业分配。
        /// 这样自定义内鬼职业必然落在原版内鬼身上，阵营展示/击杀按钮/队友列表都正确，
        /// 也避免原版随后把职业的底色职业覆盖掉（旧写法在 Prefix 里分配会被原版覆盖）。
        /// </summary>
        public static void Postfix()
        {
            try
            {
                // 初始化玩家数据（所有执行 SelectRoles 的客户端都要初始化）
                LightInDark.Game.LightPlayerDataManager.Initialize();
                LightInDark.Game.GameManager.Instance.Initialize();

                if (!AmongUsClient.Instance.AmHost) return;   // 分配只在主机做，经 RPC 同步

                // ⚠️ 只处理**普通模式**（2026-10-06 审查）：`SelectRoles` 在隐藏者模式（HnS）等
                //   其它模式同样会跑，我们的后置原来**无条件**分配自定义职业 →
                //   HnS 里会凭空出现内鬼/船员职业（本该没有）。
                try
                {
                    var mode = GameOptionsManager.Instance.CurrentGameOptions.GameMode;
                    if (mode != GameModes.Normal)
                    {
                        LightLogger.Log($"[Patch] 当前模式 {mode} 不是普通模式 → 跳过自定义职业分配");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[Patch] 取游戏模式失败，按普通模式继续：{ex.Message}");
                }

                LightLogger.Log("[Patch] 原版分配完成，开始自定义职业分配");
                EventTriggers.OnRoleSelectionBegin(PlayerControl.AllPlayerControls?.Count ?? 0);

                var impostors = new List<byte>();
                var others = new List<byte>();
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc == null) continue;

                    // ⚠️ 与原版取材保持一致：排除**已断线**的玩家（原版 RoleManager 里就滤了 Disconnected），
                    //    否则断线残留条目会白占职业名额（AGENTS 审查 #14）
                    try { if (pc.Data != null && pc.Data.Disconnected) continue; } catch { }

                    if (pc.Data?.Role != null && pc.Data.Role.IsImpostor)
                        impostors.Add(pc.PlayerId);
                    else
                        others.Add(pc.PlayerId);
                }

                if (impostors.Count + others.Count == 0)
                {
                    LightLogger.LogWarning("[Patch] 没有任何可分配玩家（名单为空）→ 跳过分配");
                    return;
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

                // ★ 鬼魂职业分配（2026-10-06，对齐 Nebula 的 GhostRoleAssignmentPatch）：
                //   以前死亡 = 职业终止；现在若注册了 RoleCategory.Ghost 的职业，
                //   房主会给死者分配一个（**没有鬼魂职业时这一步什么都不做** → 行为与改动前一致）
                try
                {
                    if (__instance != null) Roles.Assignment.GhostRoleAllocator.TryAssign(__instance.PlayerId);
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[PlayerDeathPatch] 鬼魂职业分配失败：{ex.Message}");
                }
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

                // ★ 把"被放逐的人"带进事件（2026-10-06 审查 #9）：
                //   原来恒发 `OnPlayerExile(null)` → `PlayerExileEvent.Exiled` **永远为空** ✗
                //   任何想知道"谁被放逐了"的职业都拿不到数据（而 `ExileController.initData.networkedPlayer`
                //   上就有这个信息）。
                PlayerControl? exiled = null;
                try
                {
                    var info = __instance != null ? __instance.initData.networkedPlayer : null;
                    if (info != null)
                    {
                        foreach (var pc in PlayerControl.AllPlayerControls)
                        {
                            if (pc == null) continue;
                            if (pc.Data == info) { exiled = pc; break; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[Patch] 取被放逐玩家失败：{ex.Message}");
                }

                if (exiled != null) LightLogger.Log($"[Patch] 被放逐：{exiled.name}");
                EventTriggers.OnPlayerExile(exiled);
            }
            catch (Exception ex)
            {
                // ⚠️ 原来是空 catch（AGENTS §11.6：静默是排查的敌人）
                LightLogger.LogError("[ExileBeginPatch.Postfix]", ex);
            }
        }
    }

    // =====================================================================
    // 紧急按钮
    // =====================================================================

    /// <summary>
    /// **任意来源的复活都发事件**（2026-10-06 审查 #9）。
    ///
    /// 原来只在自家 `RpcDefinitions.RevivePlayer` 里发 `OnPlayerRevive` →
    ///   原版路径 / 其它模组 / 直接调 `PlayerControl.Revive` 都不会触发，
    ///   职业写在复活钩子里的"恢复状态"逻辑会**静默不跑** ✗
    /// 现在统一挂在这里（自家 RPC 里那一次已撤掉，避免同一次复活发两遍）。
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Revive))]
    public static class RevivePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (__instance == null) return;
                EventTriggers.OnPlayerRevive(__instance);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RevivePatch] 复活事件派发失败：{ex.Message}");
            }
        }
    }

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
