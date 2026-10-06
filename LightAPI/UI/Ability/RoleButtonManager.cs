using System;
using System.Collections.Generic;
using HarmonyLib;
using LightInDark.Core;
using LightInDark.Roles;
using UnityEngine;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 按钮管理器：统一注册/创建/更新/清理所有角色按钮（普通、持续、会议目标、会议右下角）。
    /// 通过 Harmony patch 自动驱动：
    ///  - HudManager.Start → 创建 HUD 按钮（普通/持续）
    ///  - HudManager.SetHudActive → 全局可见性
    ///  - PlayerControl.FixedUpdate → 每帧更新
    ///  - MeetingHud.Start / OnDestroy → 会议按钮生命周期
    /// </summary>
    public static class RoleButtonManager
    {
        private static readonly List<RoleButtonBase> _buttons = new();

        /// <summary>上次的 HUD 可见性（只在变化时打日志）。</summary>
        private static bool _lastVisible = true;

        /// <summary>当前注册的按钮数（调试用）。</summary>
        public static int Count => _buttons.Count;

        /// <summary>注册一个按钮（由各按钮 Create 调用）。HUD 就绪则立即创建，否则入队等待。</summary>
        public static void Register(RoleButtonBase button)
        {
            try
            {
                if (button == null) return;
                _buttons.Add(button);

                // 会议按钮由 MeetingHud.Start 创建（OnMeetingStart），此处不创建
                if (button is MeetingTargetButton || button is MeetingAbilityButton)
                {
                    LightLogger.Log($"[RoleButtonManager] 注册 {button.GetType().Name}（{button.Role?.CodeName ?? "?"}）→ 会议类，等会议开始再建");
                    return;
                }

                bool hudReady = HudManager.Instance != null && HudManager.Instance.AbilityButton != null;
                LightLogger.Log($"[RoleButtonManager] 注册 {button.GetType().Name}（{button.Role?.CodeName ?? "?"}）→ " +
                                $"{(hudReady ? "HUD 就绪，立即创建" : "HUD 未就绪，入队等待")}（累计 {_buttons.Count} 个）");

                if (hudReady)
                {
                    HudGrid.Ensure();
                    button.Create();
                }
                else
                {
                    // HUD 未就绪：加入等待队列（由 HudManagerStartPatch 触发补建）
                    _pendingHud.Add(button);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.Register]", ex);
            }
        }

        private static readonly List<RoleButtonBase> _pendingHud = new();

        /// <summary>HUD 就绪后补建普通/持续按钮。</summary>
        internal static void CreateAllHudButtons()
        {
            try
            {
                HudGrid.Ensure();
                LightLogger.Log($"[RoleButtonManager] HUD 就绪，补建 {_pendingHud.Count} 个待建按钮（累计 {_buttons.Count} 个）");
                foreach (var b in _pendingHud)
                {
                    if (b.IsDeadObject) continue;
                    b.Create();
                }
                _pendingHud.Clear();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.CreateAllHudButtons]", ex);
            }
        }

        /// <summary>每帧更新所有按钮。</summary>
        public static void UpdateAll()
        {
            try
            {
                for (int i = _buttons.Count - 1; i >= 0; i--)
                {
                    var b = _buttons[i];
                    if (b.IsDeadObject) { _buttons.RemoveAt(i); continue; }
                    b.Update();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonManager.UpdateAll] {ex.Message}");
            }
        }

        /// <summary>设置所有按钮的全局 HUD 激活状态（只在状态真的变化时打一条日志）。</summary>
        public static void SetAllVisible(bool visible)
        {
            try
            {
                if (_lastVisible != visible)
                {
                    _lastVisible = visible;
                    LightLogger.Log($"[RoleButtonManager] HUD 可见性 → {(visible ? "显示" : "隐藏")}（{_buttons.Count} 个按钮）");
                }

                foreach (var b in _buttons)
                    b.SetHudActive(visible);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.SetAllVisible]", ex);
            }
        }

        /// <summary>会议开始：通知会议按钮。</summary>
        public static void OnMeetingStart(MeetingHud meeting)
        {
            try
            {
                foreach (var b in _buttons)
                {
                    if (b is MeetingTargetButton mtb) mtb.OnMeetingStart(meeting);
                    else if (b is MeetingAbilityButton mab) mab.OnMeetingStart(meeting);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.OnMeetingStart]", ex);
            }
        }

        /// <summary>会议结束：清理会议按钮。</summary>
        public static void OnMeetingEnd()
        {
            try
            {
                foreach (var b in _buttons)
                {
                    if (b is MeetingTargetButton mtb) mtb.OnMeetingEnd();
                    else if (b is MeetingAbilityButton mab) mab.OnMeetingEnd();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.OnMeetingEnd]", ex);
            }
        }

        /// <summary>释放指定职业运行时实例的全部按钮（换职业时由 RuntimeRoleTemplate.Release 调用，防止按钮残留重复）。</summary>
        public static void ReleaseButtonsOf(RuntimeRoleTemplate role)
        {
            try
            {
                for (int i = _buttons.Count - 1; i >= 0; i--)
                {
                    var b = _buttons[i];
                    if (!ReferenceEquals(b.Role, role)) continue;
                    _buttons.RemoveAt(i);
                    _pendingHud.Remove(b);
                    b.Release();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.ReleaseButtonsOf]", ex);
            }
        }

        /// <summary>清空全部按钮（游戏结束/释放时）。</summary>
        public static void Clear()
        {
            try
            {
                foreach (var b in _buttons) b.Release();
                _buttons.Clear();
                _pendingHud.Clear();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonManager.Clear]", ex);
            }
        }
    }

    // =====================================================================
    // Harmony 补丁
    // =====================================================================

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Start))]
    public static class HudManagerStartPatch
    {
        public static void Postfix()
        {
            try
            {
                HudGrid.Ensure();
                RoleButtonManager.CreateAllHudButtons();
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive),
        typeof(PlayerControl), typeof(RoleBehaviour), typeof(bool))]
    public static class HudManagerSetHudActivePatch
    {
        public static void Postfix(bool isActive)
        {
            try
            {
                RoleButtonManager.SetAllVisible(isActive);
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    public static class PlayerControlFixedUpdatePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (!__instance.AmOwner) return;
                RoleButtonManager.UpdateAll();

                // ★ 顺手驱动 GameManager 的每帧清理（2026-10-06 审查 #15）：
                //   `GameManager.Update()`（摘掉死亡/断线玩家）**原本没有任何调用点** ——
                //   是一段"看着在跑其实从不执行"的死代码（AGENTS §4.2.0 式静默走空）✗
                //   挂在这里是因为它已经有"仅本人 + 每帧"的正确节流，代价只有一次列表遍历。
                try { LightInDark.Game.GameManager.Instance.Update(); } catch { }

                // ★ 补发挂起的系统消息（审查 B12）：开局阶段聊天框没就绪时发的提示会被挂起，
                //   在这里（每帧、仅本人）等它就绪后补发 —— 否则玩家永远看不到"预定已消耗"这类告知 ✗
                try { LightInDark.RPCs.RpcDefinitions.TickPendingMessages(); } catch { }
            }
            catch (System.Exception)
            {
            }
        }
    }

    /// <summary>
    /// 会议开始：创建会议按钮。
    ///
    /// ⚠️ **必须比"发会议事件"的那个 postfix 先跑**（2026-10-06 复核）：
    ///   `MeetingHud.Start` 上现在有**两个** postfix —— 本类（建按钮）与
    ///   `EventPatches.MeetingStartPatch`（发 `OnMeetingDiscussionStart` + 会议开始事件）。
    ///   两个都没标优先级时**执行顺序由加载顺序决定**（不确定）✗
    ///   → 一旦事件先发，职业在 `OnMeetingStarted` 里想配置/挂接自己的会议按钮就会看到"按钮还没建" ✗
    ///   Harmony 的规则是**优先级数值越大越先执行**，所以这里用 High 明确"先建按钮、再发事件"。
    /// </summary>
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    public static class MeetingHudStartPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            try
            {
                RoleButtonManager.OnMeetingStart(__instance);
            }
            catch (System.Exception ex)
            {
                // ⚠️ 原来是空 catch：会议按钮全部建不出来时一个字都不留（AGENTS §11.6）
                LightLogger.LogError("[MeetingHudStartPatch] 会议按钮创建失败", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    public static class MeetingHudOnDestroyPatch
    {
        public static void Postfix()
        {
            try
            {
                RoleButtonManager.OnMeetingEnd();
            }
            catch (System.Exception)
            {
            }
        }
    }
}