using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Handshake;

/// <summary>
/// 房主侧大厅闸门：未通过验证的玩家红名；存在待验证/未通过者时阻止开始。
/// 受 <see cref="HandshakeManager.IsEnabled"/> 约束。
/// </summary>
[HarmonyPatch(typeof(GameStartManager))]
public static class HandshakeLobbyGatePatch
{
    [HarmonyPatch(nameof(GameStartManager.Update))]
    [HarmonyPostfix]
    public static void UpdatePostfix(GameStartManager __instance)
    {
        try
        {
            if (AmongUsClient.Instance?.AmHost != true) return;
            if (!HandshakeManager.IsEnabled) return;

            // 每帧刷新红名：未通过验证的玩家红名，其余恢复原色
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null) continue;
                if (HandshakeManager.IsUnverified(pc.PlayerId))
                    HandshakeManager.MarkNameRed(pc.PlayerId);
                else
                    HandshakeManager.RestoreNameColor(pc.PlayerId);
            }

            bool blocked = HandshakeManager.HasPending() || HandshakeManager.HasUnverified();
            if (blocked)
            {
                __instance.startState = GameStartManager.StartingStates.NotStarting;
                if (__instance.StartButton != null)
                {
                    __instance.StartButton.SetButtonEnableState(false);
                    __instance.StartButton.ChangeButtonText("正在等待玩家");
                }
                if (__instance.GameStartText != null)
                {
                    __instance.GameStartText.text = "正在等待玩家";
                }
            }
            else if (__instance.StartButton != null)
            {
                // 阻塞解除：恢复按钮可用（文案交由原版刷新，避免写错默认文案）
                __instance.StartButton.SetButtonEnableState(true);
            }
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning("[HandshakeLobbyGatePatch.Update] " + ex.Message);
        }
    }

    [HarmonyPatch(nameof(GameStartManager.BeginGame))]
    [HarmonyPrefix]
    public static bool BeginGamePrefix(GameStartManager __instance)
    {
        try
        {
            if (AmongUsClient.Instance?.AmHost != true) return true;
            if (!HandshakeManager.IsEnabled) return true;
            if (!HandshakeManager.HasPending() && !HandshakeManager.HasUnverified()) return true;

            __instance.startState = GameStartManager.StartingStates.NotStarting;
            if (__instance.StartButton != null)
            {
                __instance.StartButton.SetButtonEnableState(false);
                __instance.StartButton.ChangeButtonText("正在等待玩家");
            }
            if (__instance.GameStartText != null)
            {
                __instance.GameStartText.text = "正在等待玩家";
            }
            LightLogger.LogWarning("[Handshake] 存在待验证/未通过玩家，阻止开始游戏");
            return false;
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning("[HandshakeLobbyGatePatch.BeginGame] " + ex.Message);
        }
        return true; // 不拦截
    }
}

/// <summary>玩家角色销毁时恢复名字颜色并清理握手状态。</summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.OnDestroy))]
public static class HandshakePlayerDestroyPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        try
        {
            if (__instance == null) return;
            HandshakeManager.RestoreNameColor(__instance.PlayerId);
            HandshakeManager.CleanupPlayer(__instance.PlayerId);
        }
        catch (System.Exception ex)
        {
            LightLogger.LogError("[HandshakePlayerDestroyPatch.Postfix]", ex);
        }
    }
}
