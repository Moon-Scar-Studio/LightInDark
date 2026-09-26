using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Handshake;

/// <summary>
/// 房主侧大厅闸门（红名 + 阻止开始）。
/// [已禁用-握手系统] 为保可玩性，握手整体暂停（2026-09-26）。
/// 恢复时：取消本类各 patch 方法体内注释，并在 LightPlugin.Load 中恢复 Initialize()。
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
            // [已禁用-握手系统]
            // if (AmongUsClient.Instance?.AmHost != true) return;
            // if (string.IsNullOrEmpty(LightPlugin.LightSettingsData.VerifyServerUrl)) return;
            //
            // RefreshUnverifiedNames();
            //
            // bool blocked = HandshakeManager.HasUnverified();
            // if (!blocked) return;
            //
            // __instance.startState = GameStartManager.StartingStates.NotStarting;
            // if (__instance.StartButton != null)
            // {
            //     __instance.StartButton.SetButtonEnableState(false);
            //     __instance.StartButton.ChangeButtonText("正在等待玩家");
            // }
            // if (__instance.GameStartText != null)
            // {
            //     __instance.GameStartText.text = "正在等待玩家";
            // }
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
            // [已禁用-握手系统]
            // if (AmongUsClient.Instance?.AmHost != true) return true;
            // if (string.IsNullOrEmpty(LightPlugin.LightSettingsData.VerifyServerUrl)) return true;
            // if (!HandshakeManager.HasUnverified()) return true;
            //
            // __instance.startState = GameStartManager.StartingStates.NotStarting;
            // if (__instance.StartButton != null)
            // {
            //     __instance.StartButton.SetButtonEnableState(false);
            //     __instance.StartButton.ChangeButtonText("正在等待玩家");
            // }
            // LightLogger.LogWarning("[Handshake] 存在未验证玩家，阻止开始游戏");
            // return false;
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning("[HandshakeLobbyGatePatch.BeginGame] " + ex.Message);
        }
        return true; // 不拦截
    }

    /// <summary>每帧把未验证玩家名字刷红、验证通过/离开的恢复原色。[已禁用]</summary>
    private static void RefreshUnverifiedNames()
    {
        try
        {
            // [已禁用-握手系统]
            // foreach (var pc in PlayerControl.AllPlayerControls)
            // {
            //     if (pc == null) continue;
            //     if (HandshakeManager.IsUnverified(pc.PlayerId))
            //         HandshakeManager.MarkNameRed(pc.PlayerId);
            //     else
            //         HandshakeManager.RestoreNameColor(pc.PlayerId);
            // }
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning("[HandshakeLobbyGatePatch.Refresh] " + ex.Message);
        }
    }
}

/// <summary>玩家角色销毁时清理握手状态（红名记录等）。[已禁用]</summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.OnDestroy))]
public static class HandshakePlayerDestroyPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        try
        {
            // [已禁用-握手系统]
            // if (__instance == null) return;
            // HandshakeManager.CleanupPlayer(__instance.PlayerId);
        }
        catch (System.Exception ex)
        {
            LightLogger.LogError("[HandshakePlayerDestroyPatch.Postfix]", ex);
        }
    }
}