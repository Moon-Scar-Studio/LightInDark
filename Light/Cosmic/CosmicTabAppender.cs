using System;
using System.Collections.Generic;
using AmongUs.Data;
using Light.UI.Window;
using LightInDark.Core;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.Cosmic;

/// <summary>
/// **装扮页签的"分类筛选行"（方案 B ✓）+ 名牌贴图补正** —— 四个页签共用 ✓。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【架构（2026-10-10 最终确定 ✓ —— 前几版全在这上面栽跟头）】
///
///   我们的数据已经**注册进 `HatManager`**（`allHats` / `allSkins` / `allVisors` / `allNamePlates` ✓）
///   → **原版自己的 `OnEnable` 循环就会给它们造 chip** ✓：
///     位置（`XRange/YStart/YOffset/NumPerRow` ✓）、`scroller` 边界（`SetScrollerBounds` ✓）、
///     `Tag` / `SelectionHighlight` / `SetUnavailable` / 点击监听（`ClickEquip`+`SelectXxx` ✓）**全都是对的** ✓✓
///
///   → 所以**不要自己再造一遍 chip** ✗✗（我前几版就是这么干的 ✓ → 重复 + 叠在 (0,0,0) + "歪" + "遮罩没了" ✓）
///   → 我们只需要做**两件**事：
///       ① 把"**画什么**"喂对 ✓ —— `CosmeticData.SetPreview` 的 Prefix（见 `CosmicPatches.cs` ✓）
///          名牌例外：它走 `CoLoadAssetAsync`（AssetReference ✗ 我们造不出来 ✓）→ 由本文件的补正负责 ✓
///       ② **加一排分类按钮** ✓（`原版` / 各插件名 ✓ 点了只显示/隐藏对应 chip ✓ 不重建 ✓）
///
/// 【分类怎么认】原版 chip 的归属：帽子用 `Tag`（= `HatData` ✓ `HatsTab.cs` L48 ✓）、
///   皮肤/面罩/名牌用 `ProductId`（✓ L44 / L48 / L57 ✓）→ 两种都看 ✓
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
internal static class CosmicTabAppender
{
    /// <summary>一个分类（"原版" + 各插件 ✓）</summary>
    internal sealed class Group
    {
        public string Name = "";
        /// <summary>这个分类里的装扮 id（全局 id ✓）</summary>
        public HashSet<string> Ids = new(StringComparer.Ordinal);
    }

    private static readonly List<(ColorChip chip, int group)> _chips = new();
    private static bool _titleNudged;

    /// <summary>
    /// 建**分类选择器**：`◄ [原版/插件名] ►` ✓（用户 2026-10-10 指定的样子 ✓ —— 就放在页签标题下面 ✓）。
    /// 位置照 TORV 的导航按钮（`HatsTabOnEnablePatch.cs` L186-204 ✓）：
    /// 左箭头 (-1.05, -0.18) ✓、标签 (0.85, -0.18) ✓、右箭头 (2.75, -0.18) ✓、z = -55 ✓
    /// </summary>
    internal static void Build(InventoryTab tab, List<Group> groups)
    {
        if (tab == null || groups == null || groups.Count <= 1) return;

        _groups = groups;
        _tab = tab;
        _index = 0;

        _chips.Clear();
        foreach (var c in tab.ColorChips)
        {
            if (c == null) continue;
            _chips.Add((c, GroupOf(c, groups)));
        }

        FixNameplateImages(tab);
        BuildSelector(tab);

        LightLogger.Log($"[CosmicTab] {tab.GetType().Name}：共 {tab.ColorChips.Count} 个格子，" +
                        $"分类 {groups.Count} 个（{string.Join(" / ", groups.ConvertAll(g => g.Name))} ✓）");
        Diagnose(tab);      // ★ 一次性诊断（把"看不见"的原因打出来 ✓）
    }

    private static List<Group>? _groups;
    private static InventoryTab? _tab;
    private static int _index;
    private static bool _diagnosed;

    /// <summary>
    /// ⚠️ AGENTS §4.8：排查"看不见"就该**一次把关键状态打全** ✓，别猜 ✓。
    /// 2026-10-10 的现象：原版 585 个格子都建好了 ✓ 但屏幕上一片空 ✗ → 这行诊断给出答案 ✓
    /// </summary>
    private static void Diagnose(InventoryTab tab)
    {
        if (_diagnosed) return;
        _diagnosed = true;
        try
        {
            var inner = tab.scroller != null ? tab.scroller.Inner : null;
            LightLogger.Log($"[CosmicDiag] 页签 active={tab.gameObject.activeInHierarchy} " +
                            $"scroller={(tab.scroller != null)} inner={(inner != null)} " +
                            $"innerLocal={(inner != null ? inner.localPosition.ToString() : "-")} " +
                            $"innerWorld={(inner != null ? inner.position.ToString() : "-")} " +
                            $"contentY=[{tab.scroller?.ContentYBounds.min:0.##},{tab.scroller?.ContentYBounds.max:0.##}] " +
                            $"YStart={tab.YStart:0.##} YOffset={tab.YOffset:0.##} NumPerRow={tab.NumPerRow} " +
                            $"XRange=[{tab.XRange.min:0.##},{tab.XRange.max:0.##}]");

            int shown = 0;
            foreach (var c in tab.ColorChips)
            {
                if (c == null || shown >= 3) break;
                var fl = c.Inner != null ? c.Inner.FrontLayer : null;
                LightLogger.Log($"[CosmicDiag] chip[{shown}] id={c.ProductId} " +
                                $"active={c.gameObject.activeInHierarchy} local={c.transform.localPosition} " +
                                $"world={c.transform.position} scale={c.transform.lossyScale} " +
                                $"innerLocal={(c.Inner != null ? c.Inner.transform.localPosition.ToString() : "-")} " +
                                $"sr={(fl != null)} srEnabled={(fl != null && fl.enabled)} " +
                                $"sprite={(fl != null && fl.sprite != null ? fl.sprite.name : "无")} " +
                                $"mat={(fl != null && fl.sharedMaterial != null ? fl.sharedMaterial.name : "无")} " +
                                $"layer={c.gameObject.layer} sortOrder={(fl != null ? fl.sortingOrder : -999)}");
                shown++;
            }
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicDiag] {ex.Message}"); }
    }

    /// <summary>这个 chip 属于哪个分类 ✓（0 = 原版 ✓）</summary>
    private static int GroupOf(ColorChip chip, List<Group> groups)
    {
        try
        {
            // 帽子：原版把 `HatData` 放在 `Tag` 里 ✓；其它三类放在 `ProductId` 里 ✓ → 两种都看 ✓
            string? id = null;

            var tag = chip.Tag;
            if (tag != null)
            {
                try { id = tag.TryCast<CosmeticData>()?.ProductId; } catch { }
                if (string.IsNullOrEmpty(id))
                {
                    try { id = tag.ToString(); } catch { }      // 有些页签放的是字符串 ✓
                }
            }
            if (string.IsNullOrEmpty(id)) id = chip.ProductId;
            if (string.IsNullOrEmpty(id)) return 0;

            for (int g = 1; g < groups.Count; g++)
                if (groups[g].Ids.Contains(id!)) return g;
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// **名牌贴图补正** ✓：名牌走 `CoLoadAssetAsync&lt;NamePlateViewData&gt;(plate, …)` ✓
    /// （内部 `NamePlateData.CreateAddressableAsset()` → `new AddressableAsset&lt;NamePlateViewData&gt;(ViewDataRef)` ✗
    ///  而 `ViewDataRef` 是 `AssetReference` ✗ 运行时造不出来 ✓）→ 原版那条路拿不到我们的图 ✓
    /// 我们手里有现成的 ✓ → 直接补到 `NameplateChip.image` 上 ✓
    /// </summary>
    private static void FixNameplateImages(InventoryTab tab)
    {
        try
        {
            foreach (var chip in tab.ColorChips)
            {
                if (chip == null) continue;
                var item = CosmicRegistry.PlateItem(chip.ProductId);
                if (item == null) continue;

                var plateChip = chip.TryCast<NameplateChip>();
                if (plateChip == null || plateChip.image == null) continue;

                plateChip.image.sharedMaterial = HatManager.Instance.DefaultShader;
                ApplyItemVisual(plateChip.image, item, CosmicRegistry.LocalColor());
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicTab] 名牌贴图补正失败：{ex.Message}");
        }
    }

    /// <summary>
    /// **把一件装扮的视觉全套装上一个渲染器** ✓：贴图（第 0 帧）✓、`flipX` ✓、
    /// `offset`/`scale` ✓、多帧则挂 <see cref="CosmicAnimDriver"/> ✓
    /// （`SetPreview` 的 Prefix 与页签补正**共用这一个入口** ✓）
    /// </summary>
    internal static void ApplyItemVisual(SpriteRenderer? sr, CosmicItem item, Color color)
    {
        try
        {
            if (sr == null || item == null) return;

            var frames = CosmicSpriteFactory.GetFrames(item, color);
            if (frames != null && frames.Length > 0) sr.sprite = frames[0];

            sr.flipX = item.FlipX;                                          // JSON 的 flipX ✓
            var t = sr.transform;
            if (item.OffsetX != 0f || item.OffsetY != 0f)                   // JSON 的 offset ✓
                t.localPosition = new Vector3(item.OffsetX, item.OffsetY, t.localPosition.z);
            if (item.Scale > 0f && Mathf.Abs(item.Scale - 1f) > 0.0001f)    // JSON 的 scale ✓
                t.localScale = Vector3.one * item.Scale;

            if (frames != null && frames.Length > 1)                        // JSON 的 frames / frameDelay ✓
                CosmicAnimDriver.Attach(sr, frames, item.FrameDelay);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicTab] ApplyItemVisual 失败：{ex.Message}");
        }
    }

    // ─────────────── 分类选择器 `◄ [原版/插件名] ►`（用户 2026-10-10 指定的样式 ✓） ───────────────
    //
    //  位置照 TORV 的导航按钮（`HatsTabOnEnablePatch.cs` L186-204 ✓）：
    //    左箭头 (-1.05, -0.18, -55) ✓  标签 (0.85, -0.18, -55) ✓  右箭头 (2.75, -0.18, -55) ✓
    //  —— 正好落在页签标题"帽子"的下面 ✓（用户图里画的就是这个位置 ✓）
    //  ⚠️ 我上一版做成了"一排按钮" ✗（原版/插件名并排 ✓）—— 用户明确否决 ✓，改成这个选择器 ✓

    private static readonly List<GameObject> _selectorObjs = new();
    private static TextMeshPro? _labelTmp;

    private static void BuildSelector(InventoryTab tab)
    {
        foreach (var old in _selectorObjs)
            if (old != null) Object.Destroy(old);
        _selectorObjs.Clear();
        _labelTmp = null;

        var left = MakeArrow(tab, "◄", new Vector3(-1.05f, -0.18f, -55f), -1);
        if (left != null) _selectorObjs.Add(left);

        var label = MakeLabel(tab, new Vector3(0.85f, -0.18f, -55f));
        if (label != null) _selectorObjs.Add(label);

        var right = MakeArrow(tab, "►", new Vector3(2.75f, -0.18f, -55f), +1);
        if (right != null) _selectorObjs.Add(right);

        NudgeTitleOnce(tab);      // 标题往右让一点（TORV L183 同款 ✓），免得和左箭头打架 ✓

        UpdateLabel();
        ApplyFilter(tab, 0);
    }

    /// <summary>
    /// 把页签标题往右挪 0.6 ✓（TORV `HatsTabOnEnablePatch.cs` L179-184 就是这么干的 ✓）。
    /// ⚠️ TORV 每次 `OnEnable` 都挪 ✗ → 会越挪越远 ✓；我们**只挪一次** ✓（静态标记 ✓）
    /// </summary>
    private static void NudgeTitleOnce(InventoryTab tab)
    {
        if (_titleNudged) return;
        try
        {
            var t = tab.transform.Find("Text");
            if (t != null)
            {
                var p = t.localPosition;
                t.localPosition = new Vector3(p.x + 0.6f, p.y, p.z);
                _titleNudged = true;
            }
        }
        catch { }
    }

    private static void Cycle(int delta)
    {
        try
        {
            if (_groups == null || _tab == null || _groups.Count == 0) return;
            _index = ((_index + delta) % _groups.Count + _groups.Count) % _groups.Count;
            UpdateLabel();
            ApplyFilter(_tab, _index);
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicTab] 切换分类失败：{ex.Message}"); }
    }

    private static void UpdateLabel()
    {
        try
        {
            if (_labelTmp != null && _groups != null && _index >= 0 && _index < _groups.Count)
                _labelTmp.text = _groups[_index].Name;
        }
        catch { }
    }

    /// <summary>箭头按钮 ✓（只有一个字形 ✓ 没底板 —— 和用户画的一致 ✓）</summary>
    private static GameObject? MakeArrow(InventoryTab tab, string glyph, Vector3 pos, int delta)
    {
        var go = NewRoot(tab, "CosmicArrow_" + glyph, pos);
        if (go == null) return null;

        var tmp = CloneText(tab, go.transform, glyph, 3.2f, new Color(1f, 0.95f, 0.85f), Vector3.zero);
        if (tmp != null) tmp.fontStyle = FontStyles.Bold;

        AddClick(go, new Vector2(0.5f, 0.5f), () => Cycle(delta));
        return go;
    }

    /// <summary>中间那个名字框 ✓（金边底 + 居中的名字 ✓）</summary>
    private static GameObject? MakeLabel(InventoryTab tab, Vector3 pos)
    {
        var go = NewRoot(tab, "CosmicGroupLabel", pos);
        if (go == null) return null;

        var bg = new GameObject("Bg");
        bg.transform.SetParent(go.transform, false);
        bg.layer = go.layer;
        bg.transform.localPosition = new Vector3(0f, 0f, 1f);      // 底在文字后面 ✓
        var sr = bg.AddComponent<SpriteRenderer>();
        ApplyButtonSprite(sr, true);
        sr.sortingOrder = 10;

        _labelTmp = CloneText(tab, go.transform, "原版", 2.6f, new Color(1f, 0.92f, 0.62f), Vector3.zero);
        return go;
    }

    /// <summary>点分类：只**显示/隐藏**已有 chip ✓（不重建、不销毁 ✓ 最稳 ✓）</summary>
    private static void ApplyFilter(InventoryTab tab, int group)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            var (chip, g) = _chips[i];
            if (chip == null) continue;
            try { chip.gameObject.SetActive(g == group); } catch { }
        }
        SetScrollerBounds(tab);
    }

    private static void SetScrollerBounds(InventoryTab tab)
    {
        try
        {
            var m = AccessTools.Method(typeof(InventoryTab), "SetScrollerBounds");
            m?.Invoke(tab, null);
        }
        catch { }
    }

    private static GameObject? NewRoot(InventoryTab tab, string name, Vector3 pos)
    {
        try
        {
            var go = new GameObject(name);
            go.transform.SetParent(tab.transform, false);
            go.transform.localPosition = pos;
            go.layer = tab.gameObject.layer;      // ★ 跟随页签的层 ✓（不然相机不渲染 ✗）
            return go;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicTab] 建 {name} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>克隆页签自己的 TMP ✓（字体统一 ✓；AGENTS §12.1 Bold、§12.2 关掉 autoSizing ✓）</summary>
    private static TextMeshPro? CloneText(InventoryTab tab, Transform parent, string text, float size, Color color, Vector3 pos)
    {
        try
        {
            var template = tab.GetComponentInChildren<TextMeshPro>(true);
            if (template == null) return null;

            var tmp = Object.Instantiate(template, parent);
            tmp.gameObject.SetActive(true);
            tmp.transform.localPosition = pos;
            tmp.transform.localScale = Vector3.one;

            var tr = tmp.GetComponent<TextTranslatorTMP>();
            if (tr != null) Object.Destroy(tr);

            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = 0f;
            tmp.enableAutoSizing = false;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            return tmp;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicTab] 克隆文字失败：{ex.Message}");
            return null;
        }
    }

    private static void AddClick(GameObject go, Vector2 size, Action onClick)
    {
        try
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            var pb = go.AddComponent<PassiveButton>();
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();   // AGENTS §4.5：必须替换 ✓
            pb.OnMouseOut = new UnityEngine.Events.UnityEvent();
            pb.OnMouseOver = new UnityEngine.Events.UnityEvent();
            pb.Colliders = new Collider2D[] { col };
            pb.OnClick.AddListener(onClick);                               // `Action` ✓（Il2Cpp 的 UnityAction 是类 ✗）
        }
        catch (Exception ex) { LightLogger.LogWarning($"[CosmicTab] 绑定点击失败：{ex.Message}"); }
    }

    private static void ApplyButtonSprite(SpriteRenderer sr, bool selected)
    {
        try
        {
            sr.sprite = RoundedPanelSprite.Get(
                selected ? new Color(0.30f, 0.28f, 0.20f, 1f) : new Color(0.16f, 0.16f, 0.18f, 1f),
                selected ? new Color(1f, 0.85f, 0.35f, 1f) : new Color(1f, 1f, 1f, 0.25f),
                10, 3);
            sr.drawMode = SpriteDrawMode.Sliced;                  // AGENTS §4.3.3 ✓
            sr.size = new Vector2(1.9f, 0.5f);
        }
        catch { }
    }

    /// <summary>把某个类别按插件分组 ✓（`groups[0]` 固定是"原版" ✓ 空集合 ✓）</summary>
    internal static List<Group> BuildGroups(string category)
    {
        var groups = new List<Group> { new() { Name = "原版" } };

        foreach (var addon in CosmicAddonLoader.Addons)
        {
            if (!addon.ItemsByCategory.TryGetValue(category, out var items) || items.Count == 0) continue;
            var g = new Group { Name = addon.Name };
            foreach (var it in items) g.Ids.Add(it.FullId);
            if (g.Ids.Count > 0) groups.Add(g);
        }
        return groups;
    }
}
