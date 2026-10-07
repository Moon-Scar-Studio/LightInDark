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

                // ★★ 把原版随机出来的**技能职业一律降级为底色职业**（用户 2026-10-06 要求：
                //    "原版职业完全不分配，全权交给我们接管"）—— 见方法注释里的完整理由。
                //    ⚠️ 必须放在下面"分阵营"之前/之后都行（它**保持 IsImpostor 不变** ✓），
                //       这里放在前面，让后续所有逻辑看到的都是干净的 Crewmate/Impostor ✓
                DowngradeVanillaSpecialRoles();

                EventTriggers.OnRoleSelectionBegin(PlayerControl.AllPlayerControls?.Count ?? 0);

                var impostors = new List<byte>();
                var others = new List<byte>();

                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc == null) continue;
                    // §4.6.1：别用 `pc?.Data?.Role` 判 Unity 对象（假 null 挡不住）
                    if (pc.Data == null || pc.Data.Role == null) continue;

                    // PR 带来的过滤：排除假人（否则假人会白占职业名额）
                    if (pc.isDummy) continue;

                    // 与原版取材保持一致：排除**已断线**的玩家（原版 RoleManager 里就滤了 Disconnected），
                    // 否则断线残留条目会白占职业名额（AGENTS 审查 #14）
                    try { if (pc.Data.Disconnected) continue; } catch { }

                    if (pc.Data.Role.IsImpostor)
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

        /// <summary>
        /// 把原版随机出来的**技能职业**全部降级成底色职业（Crewmate / Impostor），**阵营保持不变**。
        ///
        /// ═══════════════════════════════════════════════════════════════════════
        /// 【为什么需要】（用户 2026-10-06："原版职业完全不分配，全权交给我们接管"）
        ///
        ///   原版这些技能职业**每帧驱动 `HudManager.Instance.AbilityButton`**：
        ///     EngineerRole.cs:119/145  SetCoolDown        :131/153  SetFillUp(ventTime)
        ///     TrackerRole.cs:174/222   SetFillUp           ← "追踪器"
        ///     Phantom / Shapeshifter / Scientist / Detective / GuardianAngel / SpiritGuide 同理
        ///   而 `HudManager.SetHudActive` 还会 `AbilityButton.ToggleVisible(isActive)` 把它重新显示（HudManager.cs:175/179）
        ///   → 只要有人身上还挂着原版技能职业，那套能力就会继续跑、继续画自己的冷却
        ///     （`SetFillUp` 的表现正是「平时不显示 → 最后 3 秒才出现 → 很快填满」）
        ///     → 与我们的按钮形成**两套计时** = 用户报的「装填动画错误 / 管道里能走 CD / 追踪器不显示」✗
        ///
        ///   我们自己的框架已经**完全覆盖**了这些能力：
        ///     击杀 = `CanKill` + `AbilityButtonFactory.CreateKill`
        ///     钻管道 = `CanUseVents` + `RoleVentPatch`
        ///     其它技能 = 各自的 `RoleButtonConfig`
        ///   → 所以原版技能职业对本模组**没有任何用处，只有副作用** ✓
        ///
        /// 【为什么用"覆写"而不是"Prefix return false 不跑原版"】
        ///   `RoleManager.SelectRoles`（19.0 L70-92）虽然只有 20 行，但它负责
        ///   收集名单（过滤断线/死亡 + 假人）→ `GetAdjustedNumImpostors` → 两次 `AssignRolesForTeam`
        ///   （后者内部会 **广播 `RpcSetRole`**、按人数配比内鬼）。
        ///   完全不跑 = 这些都要自己补（人数配比/广播/任务表），风险明显更大 ✗
        ///   而本方法**保留原版整套流程**，只把"随机出来的具体职业"抹平成底色职业 ✓
        ///   → 效果与用户的诉求一致（原版技能职业一个都不会存在），风险却低得多 ✓
        ///
        /// ⚠️ `IsImpostor`（= `Data.Role.Role` 是否内鬼方）**必须保持不变** ✓：
        ///    内鬼方的技能职业（Shapeshifter/Phantom）降级为 `RoleTypes.Impostor`，
        ///    船員方的（Engineer/Tracker/Scientist/Detective/Noisemaker…）降级为 `RoleTypes.Crewmate`
        ///    → 击杀按钮/破坏/胜负/名字颜色全部不受影响 ✓
        /// ⚠️ 必须在**主机**执行（只在这里跑 ✓），并且用 `RpcSetRole` 广播给所有客户端 ✓
        /// ═══════════════════════════════════════════════════════════════════════
        /// </summary>
        private static void DowngradeVanillaSpecialRoles()
        {
            int changed = 0;
            try
            {
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    try
                    {
                        if (pc == null) continue;
                        // §4.6.1：Unity 对象用显式判空，别用 ?.
                        if (pc.Data == null) continue;
                        var role = pc.Data.Role;
                        if (role == null) continue;

                        var current = role.Role;                 // 原版 RoleTypes（Engineer/Tracker/…）
                        bool impostorSide = role.IsImpostor;     // 阵营：必须保持

                        var target = impostorSide ? RoleTypes.Impostor : RoleTypes.Crewmate;
                        if (current == target) continue;         // 已经是底色职业 → 不动

                        pc.RpcSetRole(target, false);            // 广播（主机本地也会生效）
                        changed++;
                        LightLogger.Log($"[Patch] 原版职业降级：{pc.name} {current} → {target}" +
                                        $"（阵营={(impostorSide ? "内鬼" : "船员")} 保持不变）");
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"[Patch] 降级原版职业失败：{ex.Message}");
                    }
                }

                if (changed > 0)
                    LightLogger.Log($"[Patch] 已把 {changed} 个原版技能职业降级为底色职业 → 原版能力不再干扰我们的按钮 ✓");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[Patch] DowngradeVanillaSpecialRoles", ex);
            }
        }
    }

    /// <summary>
    /// **完全接管原版职业分配**（方案 B，用户 2026-10-06 要求"原版职业完全不分配，全权交给我们接管"）。
    ///
    /// 做法：`RoleManager.SelectRoles` 的 **Prefix 返回 false** → 原版那 20 行（19.0 L70-92）整个不跑，
    /// 我们自己按原版同样的规则发**底色职业**（只有 Impostor / Crewmate 两种），
    /// 然后 `RoleSelectPatch.Postfix` 照常叠我们的自定义职业 ✓
    /// （Harmony：Prefix 返回 false 只跳过原方法，**Postfix 仍会执行** ✓ 所以不用改那边的结构）
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么要这样写 —— 两个从原版源码里挖出来的坑】
    ///
    /// ① **不能直接用 `PlayerControl.RpcSetRole` 做第一次赋值** ✗
    ///    `RpcSetRole`(PlayerControl.cs:2449-2458) 第一行就是 `this.Data.Role.OnRoleSet()` ——
    ///    直接解引用 `Data.Role`。而跳过 `SelectRoles` 之后 `Data.Role` **还是 null** → **NRE** ✗
    ///    → 所以先调 `RoleManager.Instance.SetRole(pc, type)`：它内部是
    ///      `if (data.Role) { Deinitialize + Destroy }` → `Instantiate(AllRoles.First(...))` → `Initialize`
    ///      → **能处理 null，且同步生效** ✓（RoleManager.cs:32-55）
    ///
    /// ② **本地状态不是立刻生效的** ✗
    ///    `RpcSetRole` 的本地路径是 `StartCoroutine(CoSetRole(...))`（异步协程）✗
    ///    而我们的 `RoleSelectPatch.Postfix` 紧接着就要读 `IsImpostor` 来分阵营
    ///    → 若只调 `RpcSetRole`，Postfix 会读到**旧值**，阵营分配直接错 ✗
    ///    → 所以 `RoleManager.SetRole`（同步 ✓）负责本地，`RpcSetRole` 只负责**广播**给其它客户端 ✓
    ///      （重复应用同一个职业是无害的：就是销毁再建一个同类型的 RoleBehaviour ✓）
    ///
    /// 【为什么任务不受影响】✓
    ///    任务是在 `ShipStatus.Begin` 里发的（`ShipStatus.cs:421  networkedPlayerInfo.RpcSetTasks(array)`），
    ///    与 `SelectRoles` **完全无关** ✓ → 跳过它不会让任何人没任务 ✓
    ///
    /// ⚠️ 非普通模式（HnS 等）**不拦**，交给原版 ✓（与我们 Postfix 里的模式闸门一致 ✓）
    /// ⚠️ 任何异常都 `return true` **退回原版** ✓（安全方向：宁可原版跑，也别开不出局 ✗）
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    public static class VanillaSelectRolesBlockPatch
    {
        private static readonly System.Random Rng = new();

        /// <summary>返回 false = 不跑原版 SelectRoles（我们自己发底色职业）。</summary>
        public static bool Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return true;

                // 只在普通模式接管（HnS 等让原版自己来）
                try
                {
                    var mode = GameOptionsManager.Instance.CurrentGameOptions.GameMode;
                    if (mode != GameModes.Normal) return true;
                }
                catch { return true; }

                AssignBaseRoles();
                return false;      // ★ 跳过原版；Postfix（我们的自定义分配）仍会执行 ✓
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[VanillaSelectRolesBlockPatch] 接管失败 → 退回原版", ex);
                return true;
            }
        }

        /// <summary>
        /// 按原版同样的规则发**底色职业**：收集名单（滤断线/死亡 + 假人）→ 内鬼数量 → 随机挑人。
        /// 只有 `RoleTypes.Impostor` / `RoleTypes.Crewmate` 两种，**绝不发技能职业** ✓
        /// </summary>
        private static void AssignBaseRoles()
        {
            // ── 1. 名单（照抄 RoleManager.SelectRoles L72-86 的取法，避免漏人）──
            var players = new List<PlayerControl>();
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null) continue;
                if (pc.Data.Disconnected || pc.Data.IsDead) continue;
                players.Add(pc);
            }
            players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));     // 原版按 ClientData.Id 排序，这里等价地保证确定性

            // 假人（原版会额外从 GameData.AllPlayers 里补 dummy）
            try
            {
                foreach (var info in GameData.Instance.AllPlayers)
                {
                    var obj = info?.Object;
                    if (obj == null || !obj.isDummy) continue;
                    if (players.Contains(obj)) continue;
                    players.Add(obj);
                }
            }
            catch { }

            if (players.Count == 0)
            {
                LightLogger.LogWarning("[Patch] 接管分配：名单为空 → 让原版处理");
                throw new InvalidOperationException("名单为空");
            }

            // ── 2. 内鬼数量：用原版同一个入口（含人数配比与房主设置）──
            int wanted = GameOptionsManager.Instance.CurrentGameOptions.GetAdjustedNumImpostors(players.Count);
            if (wanted < 0) wanted = 0;
            if (wanted > players.Count) wanted = players.Count;

            // ── 3. 随机挑内鬼（原版也是随机）──
            var order = players.OrderBy(_ => Rng.Next()).ToList();
            var impostors = new HashSet<byte>();
            for (int i = 0; i < wanted; i++) impostors.Add(order[i].PlayerId);

            // ── 4. 落地：先同步写本地，再广播 ──
            int imp = 0, crew = 0;
            foreach (var pc in order)
            {
                var type = impostors.Contains(pc.PlayerId)
                    ? AmongUs.GameOptions.RoleTypes.Impostor
                    : AmongUs.GameOptions.RoleTypes.Crewmate;

                try { RoleManager.Instance.SetRole(pc, type); }        // ★ 同步（能处理 Data.Role == null）
                catch (Exception ex) { LightLogger.LogWarning($"[Patch] SetRole 失败 {pc.name}: {ex.Message}"); }

                try { pc.RpcSetRole(type, false); }                    // ★ 广播（此时 Data.Role 已存在 → 不会 NRE）
                catch (Exception ex) { LightLogger.LogWarning($"[Patch] RpcSetRole 失败 {pc.name}: {ex.Message}"); }

                if (type == AmongUs.GameOptions.RoleTypes.Impostor) imp++; else crew++;
            }

            LightLogger.Log($"[Patch] 已接管原版分配：共 {order.Count} 人 → 内鬼 {imp} / 船员 {crew}" +
                            $"（**只发底色职业，原版技能职业一个都不会出现** ✓）");
        }
    }

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.AssignRoleOnDeath))]
    public static class BlockGhostRolePatch    {
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
