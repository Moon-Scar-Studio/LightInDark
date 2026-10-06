using HarmonyLib;
using LightInDark.Core;
using LightInDark.Events;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// 通风管放行：职业模板覆写 CanUseVents 返回 true 即可钻通风管。
/// </summary>
internal static class RoleVent
{
    public static bool CanVent(byte playerId)
        => LightInDark.Game.GameManager.Instance?.GetPlayer(playerId)?.Role?.Role?.CanUseVents == true;
}

[HarmonyPatch(typeof(Vent), nameof(Vent.CanUse))]
public static class RoleVentCanUsePatch
{
    public static void Postfix(Vent __instance, [HarmonyArgument(0)] NetworkedPlayerInfo pc, ref bool canUse, ref bool couldUse)
    {
        if (canUse || pc?.Object == null) return;
        if (!RoleVent.CanVent(pc.PlayerId)) return;

        var player = pc.Object;
        couldUse = !pc.IsDead && (player.CanMove || player.inVent);
        if (!couldUse) return;

        Vector3 center = player.Collider.bounds.center;
        Vector3 position = __instance.transform.position;
        canUse = Vector2.Distance(center, position) <= __instance.UsableDistance
            && !PhysicsHelpers.AnythingBetween(player.Collider, center, position, Constants.ShipOnlyMask, false);
    }
}

[HarmonyPatch(typeof(RoleBehaviour), nameof(RoleBehaviour.CanVent), MethodType.Getter)]
public static class RoleCanVentPatch
{
    public static void Postfix(RoleBehaviour __instance, ref bool __result)
    {
        if (__result || __instance?.Player == null) return;
        if (RoleVent.CanVent(__instance.Player.PlayerId)) __result = true;
    }
}

internal static class RoleVentButtonTicker
{
    public static void OnHudUpdate(GameHudUpdateEvent ev)
    {
        try
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || !RoleVent.CanVent(local.PlayerId)) return;

            var button = HudManager.Instance?.ImpostorVentButton;
            if (button == null) return;
            if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);

            var vent = FindNearestVent(local, out float nearest, out Vent nearestVent);
            button.SetTarget(vent);

            if (vent != null) button.SetEnabled();
            else button.SetDisabled();

            var me = local.transform.position;
            var cam = Camera.main == null ? Vector3.zero : Camera.main.transform.position;
            LightLogger.Log($"[Vent] t={Time.time:0} me={me.x:0.0},{me.y:0.0} cam={cam.x:0.0},{cam.y:0.0}"
                + $" nearest={nearest:0.00}"
                + $" currentVent={(Vent.currentVent == null ? "null" : Vent.currentVent.Id.ToString())}"
                + $" target={(vent == null ? "null" : vent.Id.ToString())}"
                + $" inVent={local.inVent}");
        }
        catch { }
    }

    private static Vent FindNearestVent(PlayerControl local, out float nearest, out Vent nearestVent)
    {
        nearest = float.MaxValue;
        nearestVent = null;

        var ship = ShipStatus.Instance;
        if (ship?.AllVents == null || local == null) return null;

        var pos = (Vector2)local.transform.position;

        foreach (var vent in ship.AllVents)
        {
            if (vent == null) continue;

            float distance = Vector2.Distance(pos, (Vector2)vent.transform.position);
            if (distance >= nearest) continue;

            nearest = distance;
            nearestVent = vent;
        }

        return nearestVent != null && nearest <= nearestVent.UsableDistance ? nearestVent : null;
    }
}
