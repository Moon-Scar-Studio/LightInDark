using System;
using HarmonyLib;
using Light.UI.DebugTools;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Modifiers.Buffs;
using LightInDark.Visual;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// Buff 的每帧驱动：
///   · HudManager.Update → 计时 / 视野与体型聚合 / 小黑人外观低频兜底
///   · PlayerPhysics.FixedUpdate → 速度倍率（必须在原版算完速度之后写）
/// </summary>
[HarmonyPatch]
public static class BuffPatches
{
    private const int AnonymousCheckEvery = 20;
    private static int _frame;

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    [HarmonyPostfix]
    public static void HudUpdatePostfix()
    {
        try
        {
            BuffManager.Tick(Time.deltaTime);
            BuffManager.ApplyAttributes();
            BuffDebugPanel.Tick();
            Light.UI.DebugTools.RpcTriggerPanel.Tick();          // RPC 触发器：自动关闭
            Light.UI.DebugTools.RpcTriggerPanel.HandleInput();   // RPC 触发器：搜索框吃键

            if (++_frame % AnonymousCheckEvery == 0) EnsureAnonymousLooks();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BuffPatches.HudUpdate] {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    [HarmonyPostfix]
    public static void PhysicsFixedUpdatePostfix(PlayerPhysics __instance)
    {
        try
        {
            var control = __instance.myPlayer;
            if (control != null) BuffManager.ApplySpeed(control);
        }
        catch
        {
        }
    }

    private static void EnsureAnonymousLooks()
    {
        try
        {
            if (AmongUsClient.Instance == null || AmongUsClient.Instance.GameState != AmongUsClient.GameStates.Started) return;

            foreach (var player in Player.AllPlayers)
                PlayerAppearance.EnsureApplied(player);
        }
        catch
        {
        }
    }
}
