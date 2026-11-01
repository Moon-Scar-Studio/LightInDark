using System;
using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches;

[HarmonyPatch(typeof(ExileController), nameof(ExileController.Begin))]
internal static class ExileDiagBegin
{
    public static void Postfix(ExileController __instance)
    {
        try
        {
            var d = __instance.initData;
            if (d == null)
            {
                LightLogger.LogVerbose("[ExileDiag] Begin initData=null");
                return;
            }

            string name = d.networkedPlayer == null ? "null" : d.networkedPlayer.PlayerName;
            LightLogger.LogVerbose($"[ExileDiag] Begin type={__instance.GetType().Name} tie={d.voteTie} overruled={d.wasOverruled} player={name} imp={d.totalImpostorCount}/{d.remainingImpostorCount}");
        }
        catch (Exception ex) { LightLogger.LogWarning($"[ExileDiag.Begin] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(ExileController), "WrapUp")]
internal static class ExileDiagWrapUp
{
    public static void Prefix()
    {
        try { LightLogger.LogVerbose("[ExileDiag] WrapUp"); }
        catch { }
    }
}

[HarmonyPatch(typeof(ExileController), "ReEnableGameplay")]
internal static class ExileDiagReEnable
{
    public static void Prefix()
    {
        try
        {
            var flow = GameManager.Instance?.LogicFlow?.TryCast<LogicGameFlowNormal>();
            bool over = false;
            try { over = flow != null && flow.IsGameOverDueToDeath(); } catch { }
            LightLogger.LogVerbose($"[ExileDiag] ReEnableGameplay overDueToDeath={over} allowAutoEnd={LightInDark.Game.GameModeManager.AllowAutomaticEnd}");
        }
        catch (Exception ex) { LightLogger.LogWarning($"[ExileDiag.ReEnable] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive), typeof(bool))]
internal static class ExileDiagHudActive
{
    public static void Prefix(bool isActive)
    {
        try
        {
            if (ExileController.Instance == null && MeetingHud.Instance == null) return;
            LightLogger.LogVerbose($"[ExileDiag] SetHudActive({isActive})");
        }
        catch { }
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame))]
internal static class ExileDiagEndGame
{
    public static void Postfix(GameOverReason endReason)
    {
        try
        {
            LightLogger.LogVerbose($"[ExileDiag] RpcEndGame reason={endReason} allowAutoEnd={LightInDark.Game.GameModeManager.AllowAutomaticEnd} inExile={ExileController.Instance != null}");
        }
        catch { }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
internal static class ExileDiagWatchdog
{
    private static float _elapsed;
    private static bool _logged;

    public static void Postfix()
    {
        try
        {
            if (ExileController.Instance == null || MeetingHud.Instance != null)
            {
                _elapsed = 0f;
                _logged = false;
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < 10f || _logged) return;
            _logged = true;

            int aliveImp = 0, aliveCrew = 0, dead = 0, dc = 0;
            foreach (var info in GameData.Instance.AllPlayers)
            {
                if (info == null) continue;
                if (info.Disconnected) { dc++; continue; }
                if (info.IsDead) { dead++; continue; }
                if (info.Role != null && info.Role.IsImpostor) aliveImp++;
                else aliveCrew++;
            }

            var hud = HudManager.Instance;
            var cam = Camera.main;
            var local = PlayerControl.LocalPlayer;
            var flow = GameManager.Instance?.LogicFlow?.TryCast<LogicGameFlowNormal>();
            bool over = false;
            try { over = flow != null && flow.IsGameOverDueToDeath(); } catch { }

            string fs = hud?.FullScreen == null ? "null" : hud.FullScreen.color.ToString();
            string camPos = cam == null ? "null" : cam.transform.position.ToString();
            string playerPos = local == null ? "null" : local.transform.position.ToString();

            LightLogger.LogVerbose($"[ExileDiag] stuck10s impAlive={aliveImp} crewAlive={aliveCrew} dead={dead} dc={dc} overDueToDeath={over} " +
                            $"shouldCheck={GameManager.Instance?.ShouldCheckForGameEnd} gameState={AmongUsClient.Instance?.GameState} " +
                            $"hudActive={hud != null && hud.gameObject.activeInHierarchy} fullScreen={fs} cam={camPos} player={playerPos}");
        }
        catch (Exception ex) { LightLogger.LogWarning($"[ExileDiag.Watchdog] {ex.Message}"); }
    }
}
