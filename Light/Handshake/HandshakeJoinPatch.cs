using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Handshake;

/// <summary>
/// 房主侧握手入口：玩家角色生成时向其发起验证挑战。
/// 受 <see cref="HandshakeManager.IsEnabled"/> 约束。
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
public static class HandshakeJoinPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        try
        {
            if (__instance == PlayerControl.LocalPlayer) return;   // 自己（房主/本人）不在这里触发
            if (AmongUsClient.Instance?.AmHost != true) return;    // 只有房主发挑战
            if (!HandshakeManager.IsEnabled) return;               // 握手未启用则不介入
            HandshakeManager.OnPlayerJoined(__instance.PlayerId, __instance.Data?.PlayerName ?? "");
        }
        catch (System.Exception ex)
        {
            LightLogger.LogError("[HandshakeJoinPatch.Postfix]", ex);
        }
    }
}
