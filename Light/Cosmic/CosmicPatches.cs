using System;
using System.Collections.Generic;
using System.Reflection;
using AmongUs.Data;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LightInDark.Core;
using UnityEngine;

namespace Light.Cosmic;

/// <summary>
/// **把 CosmicAddons 的装扮注册进游戏**（第 2 步 ✓）。
///
/// 四类都注册（帽子 / 皮肤 / 面罩 / 名牌 ✓），做法统一：
/// <code>
///   ① 造 `XxxData`（ScriptableObject ✓）→ ProductId = 我们的全局 id ✓、Free、NotInStore
///   ② 造 `XxxViewData` → 填贴图字段（HatViewData.MainImage / SkinViewData.IdleFrame /
///      VisorViewData.IdleFrame / NamePlateViewData.Image ✓）
///   ③ 塞进 `HatManager` 的对应列表（装扮页签才列得出来 ✓）
/// </code>
///
/// ⚠️ 所有 `XxxViewData.MatchPlayerColor` 一律 **false** ✓ —— 因为 `player` 模式的颜色
///    **已经由 `CosmicSpriteFactory` 逐像素算进图里**了 ✓，再让原版染一次会把图弄脏 ✗
///    （同理 `CosmeticData.PreviewCrewmateColor = false` ✓ → 原版 `UpdateMaterials` 会选
///      `DefaultShader`（不染色）✓ 而不是 `PlayerMaterial`（整张乘色）✗）
///
/// ⚠️ 踩过的坑（都实测过 ✓）：
///   · `CosmeticsCache.GetHat(string)` 返回 **`HatViewData`** ✗ 不是 `HatData` ✗（写错 → PatchAll 崩 → 整模组没加载 ✓）
///   · `HatManager.Awake` 运行期 **undefined** ✗（别挂它 ✓）
///   · Il2Cpp 里 `UnityAction` 是**类** ✗ → 监听器一律用 `(Action)` ✓（原版自己就是这么写的 ✓）
///   · `DataManager` 是**静态类** ✓（`DataManager.Player...` ✓ 需要 `using AmongUs.Data;` ✓）
/// </summary>
internal static class CosmicRegistry
{
    // ── 帽子 ──
    public static readonly Dictionary<string, CosmicItem> HatItems = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, HatData> HatDataById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HatViewData> HatViewById = new(StringComparer.Ordinal);

    // ── 皮肤 ──
    public static readonly Dictionary<string, CosmicItem> SkinItems = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, SkinData> SkinDataById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SkinViewData> SkinViewById = new(StringComparer.Ordinal);

    // ── 面罩 ──
    public static readonly Dictionary<string, CosmicItem> VisorItems = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, VisorData> VisorDataById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, VisorViewData> VisorViewById = new(StringComparer.Ordinal);

    // ── 名牌 ──
    public static readonly Dictionary<string, CosmicItem> PlateItems = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, NamePlateData> PlateDataById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, NamePlateViewData> PlateViewById = new(StringComparer.Ordinal);

    private static bool _built;

    public static bool HasAnyHat => HatItems.Count > 0;
    public static bool HasAnySkin => SkinItems.Count > 0;
    public static bool HasAnyVisor => VisorItems.Count > 0;
    public static bool HasAnyPlate => PlateItems.Count > 0;

    /// <summary>进大厅时建表 ✓（幂等 ✓；`HatManager` 没就绪会自动重试 ✓）</summary>
    public static void Build()
    {
        if (_built) return;

        try
        {
            var manager = HatManager.Instance;
            if (manager == null)
            {
                LightLogger.LogWarning("[CosmicRegistry] HatManager 还不存在 → 推迟注册（进大厅会自动重试）✓");
                return;                     // 不置 _built ✓
            }

            foreach (var addon in CosmicAddonLoader.Addons)
            {
                BuildCategory(addon, "Hats", manager);
                BuildCategory(addon, "Skins", manager);
                BuildCategory(addon, "Visors", manager);
                BuildCategory(addon, "NamePlates", manager);
            }

            _built = true;
            LightLogger.Log($"[CosmicRegistry] 已注册自定义装扮：帽子 {HatItems.Count} / 皮肤 {SkinItems.Count} / " +
                            $"面罩 {VisorItems.Count} / 名牌 {PlateItems.Count} ✓");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[CosmicRegistry.Build]", ex);
        }
    }

    private static void BuildCategory(CosmicAddon addon, string category, HatManager manager)
    {
        if (!addon.ItemsByCategory.TryGetValue(category, out var items) || items.Count == 0) return;

        foreach (var item in items)
        {
            try
            {
                switch (category)
                {
                    case "Hats":
                        if (HatItems.ContainsKey(item.FullId)) continue;
                        var hd = BuildHatData(item); if (hd == null) continue;
                        HatItems[item.FullId] = item;
                        HatDataById[item.FullId] = hd;
                        HatViewById[item.FullId] = BuildHatView(item);
                        AddToManager(manager, hd);
                        break;

                    case "Skins":
                        if (SkinItems.ContainsKey(item.FullId)) continue;
                        var sd = BuildSkinData(item); if (sd == null) continue;
                        SkinItems[item.FullId] = item;
                        SkinDataById[item.FullId] = sd;
                        SkinViewById[item.FullId] = BuildSkinView(item);
                        AddToManager(manager, sd);
                        break;

                    case "Visors":
                        if (VisorItems.ContainsKey(item.FullId)) continue;
                        var vd = BuildVisorData(item); if (vd == null) continue;
                        VisorItems[item.FullId] = item;
                        VisorDataById[item.FullId] = vd;
                        VisorViewById[item.FullId] = BuildVisorView(item);
                        AddToManager(manager, vd);
                        break;

                    case "NamePlates":
                        if (PlateItems.ContainsKey(item.FullId)) continue;
                        var pd = BuildPlateData(item); if (pd == null) continue;
                        PlateItems[item.FullId] = item;
                        PlateDataById[item.FullId] = pd;
                        PlateViewById[item.FullId] = BuildPlateView(item);
                        AddToManager(manager, pd);
                        break;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[CosmicRegistry] 注册 {item.FullId} 失败：{ex.Message}");
            }
        }
    }

    // ─────────────────────── 造数据（四类） ───────────────────────

    private static HatData? BuildHatData(CosmicItem item)
    {
        var d = ScriptableObject.CreateInstance<HatData>();
        if (d == null) return null;
        d.name = item.FullId;
        d.ProductId = item.FullId;
        d.Free = true;
        d.NotInStore = true;
        d.InFront = !item.BehindBody;          // JSON 的 behindBody = 画在身体后面 ✓
        d.NoBounce = false;
        d.BlocksVisors = false;
        d.StoreName = item.DisplayName;
        d.PreviewCrewmateColor = false;        // ★ 颜色已算进图里 → 用不染色的着色器 ✓
        return d;
    }

    private static SkinData? BuildSkinData(CosmicItem item)
    {
        var d = ScriptableObject.CreateInstance<SkinData>();
        if (d == null) return null;
        d.name = item.FullId;
        d.ProductId = item.FullId;
        d.Free = true;
        d.NotInStore = true;
        // ⚠️ `StoreName` 只有 `HatData` 有 ✗（`CosmeticData` 里没有 ✓）→ 这三类不设它 ✓
        d.ChipOffset = new Vector2(item.OffsetX, item.OffsetY);   // 用上了 JSON 的 offset ✓
        d.PreviewCrewmateColor = false;
        return d;
    }

    private static VisorData? BuildVisorData(CosmicItem item)
    {
        var d = ScriptableObject.CreateInstance<VisorData>();
        if (d == null) return null;
        d.name = item.FullId;
        d.ProductId = item.FullId;
        d.Free = true;
        d.NotInStore = true;
        // ⚠️ `StoreName` 只有 `HatData` 有 ✗（`CosmeticData` 里没有 ✓）→ 这三类不设它 ✓
        d.ChipOffset = new Vector2(item.OffsetX, item.OffsetY);   // 用上了 JSON 的 offset ✓
        d.PreviewCrewmateColor = false;
        return d;
    }

    private static NamePlateData? BuildPlateData(CosmicItem item)
    {
        var d = ScriptableObject.CreateInstance<NamePlateData>();
        if (d == null) return null;
        d.name = item.FullId;
        d.ProductId = item.FullId;
        d.Free = true;
        d.NotInStore = true;
        // ⚠️ `StoreName` 只有 `HatData` 有 ✗（`CosmeticData` 里没有 ✓）→ 这三类不设它 ✓
        d.ChipOffset = new Vector2(item.OffsetX, item.OffsetY);   // 用上了 JSON 的 offset ✓
        return d;
    }

    // ─────────────────────── 造"画什么"（四类） ───────────────────────

    private static HatViewData BuildHatView(CosmicItem item)
    {
        var v = ScriptableObject.CreateInstance<HatViewData>();
        v.name = item.FullId;
        v.MatchPlayerColor = false;
        var s = CosmicSpriteFactory.Get(item, LocalColor());
        if (s != null) { v.MainImage = s; v.BackImage = s; }
        return v;
    }

    private static SkinViewData BuildSkinView(CosmicItem item)
    {
        var v = ScriptableObject.CreateInstance<SkinViewData>();
        v.name = item.FullId;
        v.MatchPlayerColor = false;
        v.IdleFrame = CosmicSpriteFactory.Get(item, LocalColor());
        return v;
    }

    private static VisorViewData BuildVisorView(CosmicItem item)
    {
        var v = ScriptableObject.CreateInstance<VisorViewData>();
        v.name = item.FullId;
        v.MatchPlayerColor = false;
        var s = CosmicSpriteFactory.Get(item, LocalColor());
        v.IdleFrame = s;
        return v;
    }

    private static NamePlateViewData BuildPlateView(CosmicItem item)
    {
        var v = ScriptableObject.CreateInstance<NamePlateViewData>();
        v.name = item.FullId;
        v.Image = CosmicSpriteFactory.Get(item, LocalColor());
        return v;
    }

    /// <summary>取 view data（没有就现建 ✓）</summary>
    public static HatViewData? HatViewOf(CosmicItem item)
    {
        try
        {
            if (HatViewById.TryGetValue(item.FullId, out var v) && v != null) return v;
            v = BuildHatView(item); HatViewById[item.FullId] = v; return v;
        }
        catch { return null; }
    }

    public static SkinViewData? SkinViewOf(CosmicItem item)
    {
        try
        {
            if (SkinViewById.TryGetValue(item.FullId, out var v) && v != null) return v;
            v = BuildSkinView(item); SkinViewById[item.FullId] = v; return v;
        }
        catch { return null; }
    }

    public static VisorViewData? VisorViewOf(CosmicItem item)
    {
        try
        {
            if (VisorViewById.TryGetValue(item.FullId, out var v) && v != null) return v;
            v = BuildVisorView(item); VisorViewById[item.FullId] = v; return v;
        }
        catch { return null; }
    }

    public static NamePlateViewData? PlateViewOf(CosmicItem item)
    {
        try
        {
            if (PlateViewById.TryGetValue(item.FullId, out var v) && v != null) return v;
            v = BuildPlateView(item); PlateViewById[item.FullId] = v; return v;
        }
        catch { return null; }
    }

    /// <summary>本地玩家颜色（装扮栏预览用的就是它 ✓）</summary>
    public static Color LocalColor()
    {
        try
        {
            var pc = PlayerControl.LocalPlayer;
            if (pc != null && pc.Data != null)
            {
                int cid = pc.Data.DefaultOutfit.ColorId;
                return Palette.PlayerColors[Mathf.Clamp(cid, 0, Palette.PlayerColors.Length - 1)];
            }
        }
        catch { }
        return Color.white;
    }

    /// <summary>
    /// 把我们的数据塞进 `HatManager` 的对应容器 ✓。
    ///
    /// ⚠️⚠️ 2026-10-10 读原版源码确认的关键事实：
    ///   `HatManager` 里装数据的字段是**数组** ✗ 不是 `List` ✗：
    ///   <code>
    ///     private HatData[] allHats;      // HatManager.cs L344
    ///     private SkinData[] allSkins;    // L347 …同理 allVisors / allNamePlates
    ///   </code>
    ///   它们在 `Initialize` 里由 `GetSorted(Refdata.hats)` 一次性建好 ✓，
    ///   而 `GetUnlockedHats()` = `GetUnlocked&lt;HatData&gt;(allHats)` ✓
    ///   （过滤条件是 **`x.Free || 已购买`** ✓ → 我们给的数据 `Free = true` ✓ 所以能列出来 ✓）
    ///   → interop 里 `HatData[]` 就是 `Il2CppReferenceArray<HatData>` ✓，
    ///     所以要**重建一个更大的数组再回写字段** ✓（我重写时把这段删了 ✗ → 那就是"页签里没有我们"的另一个原因 ✓）
    /// </summary>
    private static void AddToManager<T>(HatManager manager, T data) where T : CosmeticData
    {
        try
        {
            // ⚠️⚠️ 保险：Il2CppInterop 对原版字段**可能只暴露成属性**（`get_allHats` / `set_allHats` ✓）
            //    而不是真正的 .NET 字段 ✗ —— 只走 `GetFields()` 的话可能**一条都找不到** ✓
            //    → 两条路都试 ✓（谁先成功算谁 ✓）
            bool done = TryViaFields(manager, data) || TryViaProperties(manager, data);

            if (!done)
                LightLogger.LogWarning($"[CosmicRegistry] ⚠️ 没能把 {data.ProductId} 塞进 HatManager 的任何容器" +
                                       "（页签里就不会出现它 ✓）—— 请把这条发我 ✓");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicRegistry.AddToManager] {ex.Message}");
        }
    }

    private static bool TryViaFields<T>(HatManager manager, T data) where T : CosmeticData
    {
        bool done = false;
        foreach (var f in manager.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var ft = f.FieldType;
            if (!ft.IsGenericType || ft.GetGenericArguments()[0] != typeof(T)) continue;

            var def = ft.GetGenericTypeDefinition();
            if (def == typeof(Il2CppReferenceArray<>))
            {
                if (f.GetValue(manager) is Il2CppReferenceArray<T> arr)
                {
                    f.SetValue(manager, GrowArray(arr, data));
                    done = true;
                }
            }
            else if (def == typeof(List<>))
            {
                if (f.GetValue(manager) is List<T> list && !list.Contains(data)) { list.Add(data); done = true; }
            }
        }
        return done;
    }

    private static bool TryViaProperties<T>(HatManager manager, T data) where T : CosmeticData
    {
        bool done = false;
        foreach (var p in manager.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var pt = p.PropertyType;
            if (!pt.IsGenericType || pt.GetGenericArguments()[0] != typeof(T)) continue;

            var def = pt.GetGenericTypeDefinition();
            if (def == typeof(Il2CppReferenceArray<>))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (p.GetValue(manager) is Il2CppReferenceArray<T> arr)
                {
                    p.SetValue(manager, GrowArray(arr, data));
                    done = true;
                }
            }
            else if (def == typeof(List<>))
            {
                if (!p.CanRead) continue;
                if (p.GetValue(manager) is List<T> list && !list.Contains(data)) { list.Add(data); done = true; }
            }
        }
        return done;
    }

    /// <summary>`HatData[]` 在 interop 里是 `Il2CppReferenceArray<HatData>` ✓ → 重建一个更大的再回写 ✓</summary>
    private static Il2CppReferenceArray<T> GrowArray<T>(Il2CppReferenceArray<T> arr, T data) where T : CosmeticData
    {
        var bigger = new Il2CppReferenceArray<T>(arr.Length + 1);
        for (int i = 0; i < arr.Length; i++) bigger[i] = arr[i];
        bigger[arr.Length] = data;
        return bigger;
    }

    // ── 查表（给 patch 用 ✓）──
    public static CosmicItem? HatItem(string? id) => id != null && HatItems.TryGetValue(id, out var i) ? i : null;
    public static CosmicItem? SkinItem(string? id) => id != null && SkinItems.TryGetValue(id, out var i) ? i : null;
    public static CosmicItem? VisorItem(string? id) => id != null && VisorItems.TryGetValue(id, out var i) ? i : null;
    public static CosmicItem? PlateItem(string? id) => id != null && PlateItems.TryGetValue(id, out var i) ? i : null;

    public static HatData? HatDataOf(string? id) => id != null && HatDataById.TryGetValue(id, out var d) ? d : null;
}

// ═══════════════════════════ 注入 patch ═══════════════════════════

/// <summary>`CosmeticsCache.GetHat(id)` → 我们的 id 返回我们的 `HatViewData` ✓（返回类型是 `HatViewData` ✗ 不是 `HatData` ✓）</summary>
[HarmonyPatch(typeof(CosmeticsCache), "GetHat")]
internal static class CosmicCacheGetHatPatch
{
    public static bool Prefix(string id, ref HatViewData __result)
    {
        try
        {
            var item = CosmicRegistry.HatItem(id);
            if (item == null) return true;
            var view = CosmicRegistry.HatViewOf(item);
            if (view == null) return true;
            __result = view;
            return false;
        }
        catch { return true; }
    }
}

/// <summary>`CosmeticsCache.GetSkin(id)` → 我们的 `SkinViewData` ✓</summary>
[HarmonyPatch(typeof(CosmeticsCache), "GetSkin")]
internal static class CosmicCacheGetSkinPatch
{
    public static bool Prefix(string id, ref SkinViewData __result)
    {
        try
        {
            var item = CosmicRegistry.SkinItem(id);
            if (item == null) return true;
            var view = CosmicRegistry.SkinViewOf(item);
            if (view == null) return true;
            __result = view;
            return false;
        }
        catch { return true; }
    }
}

/// <summary>`CosmeticsCache.GetVisor(id)` → 我们的 `VisorViewData` ✓</summary>
[HarmonyPatch(typeof(CosmeticsCache), "GetVisor")]
internal static class CosmicCacheGetVisorPatch
{
    public static bool Prefix(string id, ref VisorViewData __result)
    {
        try
        {
            var item = CosmicRegistry.VisorItem(id);
            if (item == null) return true;
            var view = CosmicRegistry.VisorViewOf(item);
            if (view == null) return true;
            __result = view;
            return false;
        }
        catch { return true; }
    }
}

/// <summary>`CosmeticsCache.GetNameplate(id)` → 我们的 `NamePlateViewData` ✓</summary>
[HarmonyPatch(typeof(CosmeticsCache), "GetNameplate")]
internal static class CosmicCacheGetPlatePatch
{
    public static bool Prefix(string id, ref NamePlateViewData __result)
    {
        try
        {
            var item = CosmicRegistry.PlateItem(id);
            if (item == null) return true;
            var view = CosmicRegistry.PlateViewOf(item);
            if (view == null) return true;
            __result = view;
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// **接管 `CosmeticData.SetPreview`** ✓✓ —— 这是"没页签、滚动条也没了"的真凶 ✓。
///
/// 日志实证（2026-10-10）：
/// <code>
///   NullReferenceException
///     at CosmeticData.SetPreview (SpriteRenderer renderer, Int32 color)
///     at HatsTab.OnEnable ()          ← **原版自己的循环**在我们造的 HatData 上炸了 ✗
/// </code>
/// 追进原版（`CosmeticData.cs` L119 → L103）：
/// <code>
///   public void SetPreview(SpriteRenderer renderer, int color) => this.CoLoadPreview(...);
///   public void CoLoadPreview(Action&lt;Sprite, AddressableAsset&gt; onLoaded)
///   {
///       Sprite sprite = null;
///       if (this.PreviewData.RuntimeKeyIsValid())   // ★ 我们的 PreviewData 是 null ✗ → NRE ✗✗
///   }
/// </code>
/// `PreviewData` 是 **`AssetReference`**（Unity 资源引用 ✓ 运行时造不出来 ✗），
/// 而原版 `HatsTab.OnEnable` 对**每一顶**解锁帽子都会调 `hat.SetPreview(...)` ✓
/// → 我们只要把数据放进 `allHats` ✓，原版的循环就必炸 ✗ → **整个 `OnEnable` 中断** ✓
/// → 用户看到的就是「**没页签、滚动条也没了**」✓✓
///
/// 修法：我们的数据**直接贴自己算好的图** ✓，根本不走 `CoLoadPreview` ✓（返回 false 跳过原版 ✓）
/// ⚠️ 顺带**不做** `PlayerMaterial.SetColors`（原版那步是"整张乘色" ✗）—— 颜色已经算进图里了 ✓
/// </summary>
[HarmonyPatch(typeof(CosmeticData), nameof(CosmeticData.SetPreview))]
internal static class CosmicSetPreviewPatch
{
    public static bool Prefix(CosmeticData __instance, SpriteRenderer renderer)
    {
        try
        {
            if (__instance == null) return true;

            var id = __instance.ProductId;
            var item = CosmicRegistry.HatItem(id) ?? CosmicRegistry.SkinItem(id)
                       ?? CosmicRegistry.VisorItem(id) ?? CosmicRegistry.PlateItem(id);
            if (item == null) return true;                  // 原版的数据照旧 ✓

            if (renderer == null) return false;             // 没渲染器就什么都不做 ✓（也别让原版去解引用 null ✓）

            renderer.sharedMaterial = HatManager.Instance.DefaultShader;   // 不染色 ✓
            CosmicTabAppender.ApplyItemVisual(renderer, item, CosmicRegistry.LocalColor());
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicSetPreview] {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// `CosmeticData.GetItemName()` → **显示名 + 作者名** ✓
/// 作者名照抄 NOS（Nebula）`MoreCosmic.cs` L504/L659/L756/L1523 的写法 ✓：
/// <code>名字 + "\n&lt;size=1.6&gt;by " + 作者 + "&lt;/size&gt;"</code>
/// </summary>
[HarmonyPatch(typeof(CosmeticData), nameof(CosmeticData.GetItemName))]
internal static class CosmicItemNamePatch
{
    public static bool Prefix(CosmeticData __instance, ref string __result)
    {
        try
        {
            if (__instance == null) return true;
            var id = __instance.ProductId;
            var item = CosmicRegistry.HatItem(id) ?? CosmicRegistry.SkinItem(id)
                       ?? CosmicRegistry.VisorItem(id) ?? CosmicRegistry.PlateItem(id);
            if (item == null) return true;

            __result = string.IsNullOrWhiteSpace(item.Author)
                ? item.DisplayName
                : item.DisplayName + "\n<size=1.6>by " + item.Author + "</size>";
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 游戏内穿戴**帽子**：`HatParent.SetHat(int color)` → 直接贴**算好颜色**的图 ✓
/// （`CosmicSpriteFactory` 已把玩家色算进图里 ✓，这里再乘色会把图弄脏 ✗）
/// </summary>
[HarmonyPatch(typeof(HatParent), nameof(HatParent.SetHat), typeof(int))]
internal static class CosmicHatParentSetHatPatch
{
    public static bool Prefix(HatParent __instance, int color)
    {
        try
        {
            var data = __instance != null ? __instance.Hat : null;
            if (data == null) return true;
            var item = CosmicRegistry.HatItem(data.ProductId);
            if (item == null) return true;

            Color c;
            try { c = Palette.PlayerColors[Mathf.Clamp(color, 0, Palette.PlayerColors.Length - 1)]; }
            catch { c = Color.white; }

            var sprite = CosmicSpriteFactory.Get(item, c);
            if (sprite == null) return true;

            __instance.UnloadAsset();
            CosmicHatParentPopulatePatch.ApplyHatLayers(__instance, item, c);
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicHatParentSetHatPatch] {ex.Message}");
            return true;
        }
    }
}

/// <summary>装扮栏**预览**走这条 ✓（抄 TORV `HatLoad.PopulateFromViewData_Patch` ✓）</summary>
[HarmonyPatch(typeof(HatParent), nameof(HatParent.PopulateFromViewData))]
internal static class CosmicHatParentPopulatePatch
{
    public static bool Prefix(HatParent __instance)
    {
        try
        {
            var data = __instance != null ? __instance.Hat : null;
            if (data == null) return true;
            var item = CosmicRegistry.HatItem(data.ProductId);
            if (item == null) return true;

            var sprite = CosmicSpriteFactory.Get(item, CosmicRegistry.LocalColor());
            if (sprite == null) return true;

            ApplyHatLayers(__instance, item, CosmicRegistry.LocalColor());
            return false;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicHatParentPopulatePatch] {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 给前后层上皮图 ✓（`front` 由 JSON 的 `behindBody` 决定 ✓）。
    /// 贴图/`flipX`/`offset`/`scale`/逐帧动画**统一走** <see cref="CosmicTabAppender.ApplyItemVisual"/> ✓
    /// （页签和游戏内共用同一个入口 ✓，免得两边各写一遍漏东西 ✗）
    /// </summary>
    internal static void ApplyHatLayers(HatParent p, CosmicItem item, Color color)
    {
        bool front = !item.BehindBody;
        if (p.FrontLayer != null)
        {
            p.FrontLayer.enabled = front;
            p.FrontLayer.sharedMaterial = HatManager.Instance.DefaultShader;   // 不染色 ✓（颜色已算进图里 ✓）
            if (front) CosmicTabAppender.ApplyItemVisual(p.FrontLayer, item, color);
            else p.FrontLayer.sprite = null;
        }
        if (p.BackLayer != null)
        {
            p.BackLayer.enabled = !front;
            p.BackLayer.sharedMaterial = HatManager.Instance.DefaultShader;
            if (!front) CosmicTabAppender.ApplyItemVisual(p.BackLayer, item, color);
            else p.BackLayer.sprite = null;
        }
    }
}
