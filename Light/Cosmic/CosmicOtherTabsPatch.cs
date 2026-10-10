using System;
using AmongUs.Data;
using HarmonyLib;
using LightInDark.Core;

namespace Light.Cosmic;

/// <summary>
/// **四个装扮页签的 Postfix**：注册 + 分类筛选行（方案 B ✓）。
///
/// ⚠️ 只做这两件事 ✓ —— **不再自己造 chip** ✗：
///   我们的数据已注册进 `HatManager` ✓，**原版 `OnEnable` 自己就会给它们造 chip** ✓
///   （位置 / 滚动 / `Tag` / 点击监听 / `SelectionHighlight` 全都是原版的 ✓ 天然正确 ✓）
///   我们要做的是把"**画什么**"喂对 ✓ → 由 `CosmicSetPreviewPatch`（帽子/皮肤/面罩 ✓）
///   与 `CosmicTabAppender.FixNameplateImages`（名牌 ✓）完成 ✓
///
/// ⚠️ 时机也在这里（`CosmicRegistry.Build()` 幂等 ✓）——
///   于是**主菜单 / 大厅 / 游戏内衣柜打开装扮页时都会注册** ✓✓
///   （用户提醒过：**换装扮不止在大厅** ✓ → 所以不依赖 `LobbyBehaviour` ✓；
///     更早的那次在 `CosmicPatch.HatManagerInit_Postfix` ✓ = 主注册点 ✓）
/// </summary>
[HarmonyPatch(typeof(HatsTab), nameof(HatsTab.OnEnable))]
internal static class CosmicHatsTabPatch
{
    public static void Postfix(HatsTab __instance)
    {
        try
        {
            CosmicRegistry.Build();
            if (!CosmicRegistry.HasAnyHat) return;
            CosmicTabAppender.Build(__instance, CosmicTabAppender.BuildGroups("Hats"));
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicHatsTab] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(SkinsTab), nameof(SkinsTab.OnEnable))]
internal static class CosmicSkinsTabPatch
{
    public static void Postfix(SkinsTab __instance)
    {
        try
        {
            CosmicRegistry.Build();
            if (!CosmicRegistry.HasAnySkin) return;
            CosmicTabAppender.Build(__instance, CosmicTabAppender.BuildGroups("Skins"));
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicSkinsTab] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(VisorsTab), nameof(VisorsTab.OnEnable))]
internal static class CosmicVisorsTabPatch
{
    public static void Postfix(VisorsTab __instance)
    {
        try
        {
            CosmicRegistry.Build();
            if (!CosmicRegistry.HasAnyVisor) return;
            CosmicTabAppender.Build(__instance, CosmicTabAppender.BuildGroups("Visors"));
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicVisorsTab] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(NameplatesTab), nameof(NameplatesTab.OnEnable))]
internal static class CosmicNameplatesTabPatch
{
    public static void Postfix(NameplatesTab __instance)
    {
        try
        {
            CosmicRegistry.Build();
            if (!CosmicRegistry.HasAnyPlate) return;
            CosmicTabAppender.Build(__instance, CosmicTabAppender.BuildGroups("NamePlates"));
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicNameplatesTab] {ex.Message}"); }
    }
}

// ═══════════════════════════ 游戏内渲染 ═══════════════════════════

/// <summary>
/// 游戏内穿戴**皮肤**：`SkinLayer.SetSkin(SkinViewData, int color, bool isLeft)` ✓（public ✓）
/// → 贴我们的 `IdleFrame` ✓ + `DefaultShader`（不染色 ✓，颜色已算进图里 ✓）
/// </summary>
[HarmonyPatch(typeof(SkinLayer), nameof(SkinLayer.SetSkin), typeof(SkinViewData), typeof(int), typeof(bool))]
internal static class CosmicSkinLayerPatch
{
    public static bool Prefix(SkinLayer __instance, SkinViewData skin)
    {
        try
        {
            if (__instance == null || skin == null) return true;

            // 我们给自己的 view data 起的 `name` 就是全局 id ✓ → 用它认领 ✓
            var item = CosmicRegistry.SkinItem(skin.name);
            if (item == null || skin.IdleFrame == null) return true;

            if (__instance.layer != null)
            {
                __instance.layer.sharedMaterial = HatManager.Instance.DefaultShader;
                CosmicTabAppender.ApplyItemVisual(__instance.layer, item, CosmicRegistry.LocalColor());
            }
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicSkinLayer] {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// 游戏内穿戴**面罩**：`VisorLayer.SetVisor(VisorData data, int color)` ✓（public ✓）
/// ⚠️ `VisorLayer.Image` 在原版里是 **private** ✗（`VisorLayer.cs` L298 ✓）——
///    interop 会把原版 private 字段暴露成属性 ✓（编译通过即证明可用 ✓）
/// </summary>
[HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.SetVisor), typeof(VisorData), typeof(int))]
internal static class CosmicVisorLayerPatch
{
    public static bool Prefix(VisorLayer __instance, VisorData data)
    {
        try
        {
            if (__instance == null || data == null) return true;
            var item = CosmicRegistry.VisorItem(data.ProductId);
            if (item == null) return true;

            var view = CosmicRegistry.VisorViewOf(item);
            if (view == null || view.IdleFrame == null) return true;

            var img = __instance.Image;
            if (img == null) return true;

            img.sharedMaterial = HatManager.Instance.DefaultShader;
            CosmicTabAppender.ApplyItemVisual(img, item, CosmicRegistry.LocalColor());
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicVisorLayer] {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// 进大厅时**也**建一次注册表 ✓（`Build()` 幂等 ✓）。
/// 主注册点其实是 `CosmicPatch.HatManagerInit_Postfix`（`HatManager.Initialize` ✓ = 最早最稳 ✓）；
/// 这里只是保险 ✓ —— 让"别人穿着自定义装扮"时也能正确渲染 ✓
///
/// ⚠️ 挂点 `LobbyBehaviour.Start`（我们自己的复盘按钮 patch 在用同一个方法 ✓ = 实证可 patch ✓）；
///    **别挂 `HatManager.Awake`** ✗ —— 运行期 undefined ✗ → `PatchAll` 抛异常 → **整个模组没加载** ✗✗
/// </summary>
[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
internal static class CosmicRegistryLobbyReadyPatch
{
    public static void Postfix()
    {
        try { CosmicRegistry.Build(); }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicRegistryLobbyReady] {ex.Message}"); }
    }
}
