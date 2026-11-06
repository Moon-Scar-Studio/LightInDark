using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Light.Cosmic;

/// <summary>Retains NOS front/back, facing and animation-state images on a vanilla wardrobe/player.</summary>
public sealed class NosCosmeticDriver : MonoBehaviour
{
    static NosCosmeticDriver() => ClassInjector.RegisterTypeInIl2Cpp<NosCosmeticDriver>();
    public NosCosmeticDriver(IntPtr pointer) : base(pointer) { }

    private CosmeticsLayer? _layer;
    private PlayerAnimations? _animations;
    private SpriteRenderer? _visorBack;

    internal static void Attach(Component component)
    {
        var layer = component.GetComponentInParent<CosmeticsLayer>();
        if (layer == null) return;
        var driver = layer.GetComponent<NosCosmeticDriver>();
        if (driver == null) driver = layer.gameObject.AddComponent<NosCosmeticDriver>();
        driver._layer = layer;
        driver._animations = layer.GetComponent<PlayerAnimations>();
        // Initialize both hat renderers once. Later visibility changes (stealth,
        // meetings, pets) remain under vanilla control rather than being reset.
        var hat = layer.hat;
        if (hat != null && hat.Hat != null && CosmicRegistry.HatItem(hat.Hat.ProductId)?.NosImages.Count > 0)
        {
            if (hat.FrontLayer != null) hat.FrontLayer.enabled = layer.Visible;
            if (hat.BackLayer != null) hat.BackLayer.enabled = layer.Visible;
        }
        driver.Refresh();
    }

    private void LateUpdate() => Refresh();

    private NosAnimation Animation()
    {
        if (_animations == null || _animations.group == null || _animations.Animator == null)
            return NosAnimation.Idle;
        var current = _animations.Animator.m_currAnim;
        var group = _animations.group;
        if (current == group.ClimbUpAnim) return NosAnimation.ClimbUp;
        if (current == group.ClimbDownAnim) return NosAnimation.ClimbDown;
        if (current == group.RunAnim) return NosAnimation.Run;
        if (current == group.EnterVentAnim) return NosAnimation.EnterVent;
        if (current == group.ExitVentAnim) return NosAnimation.ExitVent;
        return NosAnimation.Idle;
    }

    private void Refresh()
    {
        if (_layer == null || HatManager.Instance == null) return;
        var animation = Animation();
        var left = _layer.FlipX;
        var colorId = _layer.ColorId;
        var hat = _layer.hat;
        var hatItem = hat != null && hat.Hat != null ? CosmicRegistry.HatItem(hat.Hat.ProductId) : null;
        if (hat != null && hatItem != null && hatItem.NosImages.Count > 0)
        {
            var images = NosCosmeticState.Select(hatItem, animation, left);
            Render(hat.FrontLayer, images.Front, colorId, left);
            Render(hat.BackLayer, images.Back, colorId, left);
        }

        var visor = _layer.visor;
        var visorItem = visor != null && visor.visorData != null ? CosmicRegistry.VisorItem(visor.visorData.ProductId) : null;
        if (visor == null || visor.Image == null || visorItem == null || visorItem.NosImages.Count == 0)
        {
            if (_visorBack != null) _visorBack.gameObject.SetActive(false);
            return;
        }
        var selected = NosCosmeticState.Select(visorItem, animation, left);
        Render(visor.Image, selected.Front, colorId, left);
        if (selected.Back != null && _visorBack == null)
        {
            var backObject = new GameObject("NosVisorBack");
            backObject.transform.SetParent(visor.Image.transform.parent, false);
            _visorBack = backObject.AddComponent<SpriteRenderer>();
        }
        if (_visorBack != null)
        {
            _visorBack.gameObject.SetActive(selected.Back != null && _layer.Visible && visor.Image.gameObject.activeInHierarchy);
            _visorBack.enabled = visor.Image.enabled;
            _visorBack.color = visor.Image.color;
            _visorBack.gameObject.layer = visor.Image.gameObject.layer;
            _visorBack.maskInteraction = visor.Image.maskInteraction;
            _visorBack.sortingLayerID = visor.Image.sortingLayerID;
            // NOS puts visor backs behind the body, but in front of hat backs.
            _visorBack.sortingOrder = hat != null && hat.BackLayer != null
                ? hat.BackLayer.sortingOrder + (visorItem.BackmostBack ? -1 : 1)
                : visor.Image.sortingOrder - 1;
            var position = visor.Image.transform.localPosition;
            position.z += _layer.zIndexSpacing * ((visorItem.BehindHat ? 2f : 4f) +
                (visorItem.BackmostBack ? 1.5f : 0.5f));
            _visorBack.transform.localPosition = position;
            _visorBack.transform.localScale = visor.Image.transform.localScale;
            Render(_visorBack, selected.Back, colorId, left);
        }
    }

    internal static void Render(SpriteRenderer? renderer, CosmicItem? image, int colorId, bool left)
    {
        if (renderer == null) return;
        renderer.sprite = image != null ? CosmicSpriteFactory.Get(image, Color.white) : null;
        // Left images are drawn in the same flipped coordinate space in NOS.
        renderer.flipX = left;
        if (image == null) return;
        ApplyMaterial(renderer, image, colorId);
    }

    internal static void ApplyMaterial(SpriteRenderer renderer, CosmicItem item, int colorId)
    {
        var manager = HatManager.Instance;
        bool adaptive = item.ColorMode == CosmicColorMode.Adaptive;
        bool masked = renderer.maskInteraction != SpriteMaskInteraction.None;
        var material = adaptive
            ? (masked ? manager.MaskedPlayerMaterial : manager.PlayerMaterial)
            : (masked ? manager.MaskedMaterial : manager.DefaultShader);
        if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        if (adaptive) PlayerMaterial.SetColors(colorId, renderer);
    }
}

[HarmonyPatch(typeof(HatParent), nameof(HatParent.LateUpdate))]
internal static class NosHatLateUpdatePatch
{
    public static bool Prefix(HatParent __instance)
    {
        var item = __instance.Hat != null ? CosmicRegistry.HatItem(__instance.Hat.ProductId) : null;
        return item == null || item.NosImages.Count == 0;
    }
}
