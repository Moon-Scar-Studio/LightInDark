using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Handshake;

/// <summary>
/// 玩家角色生成时（大厅/房间）触发握手流程。
/// [已禁用-握手系统] 为保可玩性，握手整体暂停（2026-09-26）。
/// 恢复时：取消本类 Postfix 内注释，并在 LightPlugin.Load 中恢复 Initialize()。
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
public static class HandshakeJoinPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        try
        {
            // [已禁用-握手系统]
            // if (__instance == PlayerControl.LocalPlayer) return; // 自己（房主或玩家本人）不在这里触发
            // if (AmongUsClient.Instance?.AmHost != true) return;  // 只有房主发挑战
            // HandshakeManager.OnPlayerJoined(__instance.PlayerId, __instance.Data?.PlayerName ?? "");
        }
        catch (System.Exception ex)
        {
            LightLogger.LogError("[HandshakeJoinPatch.Postfix]", ex);
        }
    }
}