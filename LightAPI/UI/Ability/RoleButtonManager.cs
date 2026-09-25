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
                if (button is MeetingTargetButton || button is MeetingAbilityButton) return;

                if (HudManager.Instance != null && HudManager.Instance.AbilityButton != null)
                {
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

        /// <summary>设置所有按钮的全局 HUD 激活状态。</summary>
        public static void SetAllVisible(bool visible)
        {
            try
            {
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
            }
            catch (System.Exception)
            {
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    public static class MeetingHudStartPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            try
            {
                RoleButtonManager.OnMeetingStart(__instance);
            }
            catch (System.Exception)
            {
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