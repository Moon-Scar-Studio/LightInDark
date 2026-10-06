using System.Collections.Generic;
using HarmonyLib;
using InnerNet;
using LightInDark;
using LightInDark.RPCs;
using LightInDark.Roles;
using UnityEngine;

namespace Light.Patches;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class ForceEndGameKeyPatch
{
    public static void Postfix()
    {
        if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;
        if (!Input.GetKeyDown(KeyCode.F)) return;

        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost) return;
        if (client.GameState != InnerNetClient.GameStates.Started) return;

        RpcDefinitions.ForceEndGameInvalid();
    }
}

[HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
public static class InvalidGameEndScreenPatch
{
    private static bool IsInvalid
        => LightInDark.Game.EndGameManager.GetCurrentReason() == LightInDark.Game.GameEndReason.Invalid;

    public static void Prefix()
    {
        if (!IsInvalid) return;
        EndGameResult.CachedWinners?.Clear();
    }

    public static void Postfix(EndGameManager __instance)
    {
        if (!IsInvalid || __instance == null) return;

        if (__instance.WinText != null)
            __instance.WinText.text = LightInDark.Language.Language.GetStringOrKey("gameEnd.invalid", "无效游戏，无人胜利");

        var bar = __instance.BackgroundBar;
        if (bar != null && bar.material != null)
            bar.material.SetColor("_Color", new UnityEngine.Color(0.35f, 0.35f, 0.35f, 1f));
    }
}

[HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
public static class CustomWinEndScreenPatch
{
    private static string WinnerCode()
        => LightInDark.Game.EndGameManager.GetCurrentReason() == LightInDark.Game.GameEndReason.CustomWin
            ? LightInDark.Game.EndGameManager.CustomWinnerCode
            : null;

    public static void Prefix()
    {
        var code = WinnerCode();
        if (string.IsNullOrEmpty(code)) return;

        var winners = new Il2CppSystem.Collections.Generic.List<CachedPlayerData>();
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc?.Data == null) continue;
            var player = LightInDark.Game.GameManager.Instance?.GetPlayer(pc.PlayerId);
            if (player != null && player.IsWinner) winners.Add(new CachedPlayerData(pc.Data));
        }

        EndGameResult.CachedWinners = winners;
    }

    public static void Postfix(EndGameManager __instance)
    {
        var code = WinnerCode();
        if (string.IsNullOrEmpty(code) || __instance == null) return;

        var role = RoleRegistry.GetByName(code);
        var color = (role?.Color ?? LightInDark.Color.White).ToUnityColor();

        if (__instance.WinText != null)
        {
            __instance.WinText.text = LightInDark.Language.Language.GetStringOrKey($"gameEnd.{code}Win", $"{code} Win");
            __instance.WinText.color = color;
        }

        var bar = __instance.BackgroundBar;
        if (bar != null && bar.material != null)
            bar.material.SetColor("_Color", color);
    }
}
