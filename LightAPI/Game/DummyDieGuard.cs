using HarmonyLib;
using UnityEngine;

namespace LightInDark.Game;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Die))]
public static class DummyDieGuardPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        if (__instance == null || !__instance.isDummy) return true;

        try
        {
            if (__instance.Data != null) __instance.Data.IsDead = true;
            __instance.gameObject.layer = LayerMask.NameToLayer("Ghost");
        }
        catch { }

        return false;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Exiled))]
public static class DummyExiledGuardPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        if (__instance == null || !__instance.isDummy) return true;

        try
        {
            if (__instance.Data != null) __instance.Data.IsDead = true;
            __instance.gameObject.layer = LayerMask.NameToLayer("Ghost");
        }
        catch { }

        return false;
    }
}
