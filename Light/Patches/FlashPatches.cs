using HarmonyLib;
using Light.Modifiers;
using Light.Utilities;
using LightInDark.Modifiers;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.Patches;

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
public static class FlashSpeedPatch
{
    public static void Postfix(PlayerPhysics __instance)
    {
        if (__instance == null || !__instance.AmOwner || __instance.myPlayer == null) return;

        var player = LightInDark.Game.Player.Get(__instance.myPlayer.PlayerId);
        float multiplier = Flash.SpeedOf(player);
        if (multiplier == 1f || !__instance.myPlayer.CanMove) return;

        __instance.body.velocity *= multiplier;
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class FlashNamePatch
{
    private static GameObject? _symbol;
    private static Sprite? _sprite;

    public static void Postfix()
    {
        var control = PlayerControl.LocalPlayer;
        if (control == null)
        {
            DestroySymbol();
            return;
        }

        var nameText = control.cosmetics?.nameText;
        if (nameText == null)
        {
            DestroySymbol();
            return;
        }

        var player = LightInDark.Game.Player.Get(control.PlayerId);
        if (player == null || !ModifierManager.HasModifier(player, Flash.Code))
        {
            DestroySymbol();
            return;
        }

        EnsureSymbol(nameText);
        UpdateSymbol(nameText);
    }

    private static void EnsureSymbol(TextMeshPro nameText)
    {
        if (_symbol != null) return;

        if (_sprite == null)
            _sprite = ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.FlashSymbol.png", 100f);
        if (_sprite == null) return;

        var parent = nameText.transform.parent;
        if (parent == null) return;

        _symbol = new GameObject("FlashSymbol");
        _symbol.layer = nameText.gameObject.layer;
        _symbol.transform.SetParent(parent, false);

        var renderer = _symbol.AddComponent<SpriteRenderer>();
        renderer.sprite = _sprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.color = Color.white;

        var nameRenderer = nameText.GetComponent<MeshRenderer>();
        if (nameRenderer != null)
        {
            renderer.sortingLayerID = nameRenderer.sortingLayerID;
            renderer.sortingOrder = nameRenderer.sortingOrder + 1;
        }
    }

    private static void UpdateSymbol(TextMeshPro nameText)
    {
        if (_symbol == null) return;

        var parent = nameText.transform.parent;
        if (parent == null) return;

        bool visible = nameText.gameObject.activeInHierarchy && nameText.enabled;
        if (_symbol.activeSelf != visible) _symbol.SetActive(visible);
        if (!visible) return;

        float scale = parent.lossyScale.x;
        if (Mathf.Abs(scale) < 0.0001f) scale = 1f;

        var bounds = nameText.textBounds;
        float height = Mathf.Max(0.05f, bounds.size.y / scale);
        float width = Mathf.Max(0.05f, bounds.size.x / scale);

        _symbol.transform.localPosition = nameText.transform.localPosition
            + new Vector3(width * 0.5f + height * 0.22f, 0f, -0.01f);

        var renderer = _symbol.GetComponent<SpriteRenderer>();
        if (renderer != null)
            renderer.size = new Vector2(height * 0.7f, height * 0.9f);
    }

    private static void DestroySymbol()
    {
        if (_symbol != null) Object.Destroy(_symbol);
        _symbol = null;
    }
}
