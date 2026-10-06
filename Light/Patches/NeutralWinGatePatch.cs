using System.Linq;
using HarmonyLib;
using LightInDark.Roles;

namespace Light.Patches;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame))]
public static class NeutralWinGatePatch
{
    public static bool Prefix(GameOverReason endReason)
    {
        if (LightInDark.Game.EndGameManager.GetCurrentReason() != LightInDark.Game.GameEndReason.None)
            return true;

        if (!IsNumericWin(endReason)) return true;

        return !RoleUtils.AliveEvilNeutrals().Any();
    }

    private static bool IsNumericWin(GameOverReason reason)
        => reason == GameOverReason.CrewmatesByVote
           || reason == GameOverReason.ImpostorsByKill
           || reason == GameOverReason.ImpostorsByVote;
}
