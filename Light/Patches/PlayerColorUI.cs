using System;
using System.Collections.Generic;
using HarmonyLib;
using LightInDark.Core;
using Light.UI.HudUI;
using LightInDark.UI.Window;
using Light.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Light.Patches;

/// <summary>
/// 完全接管玩家换装-颜色选择 UI（半架空原版，参考 Nebula DynamicColors 思路）：
///  - 打开换装（PlayerTab.OnEnable）后，禁用原版全部 ColorChip（按钮、点击、悬停）；
///  - 在原版色块相同位置自绘一层色块（HudUIAssets 素材），点击立即发送 CmdCheckColor，
///    不受"颜色已被占用"限制（允许重复选色）；
///  - 玩家自身已装备色块高亮显示选中框。
/// 所有素材来自项目内 GUI 资源；不使用反射私有字段。
/// </summary>
[HarmonyPatch(typeof(PlayerTab))]
public static class PlayerColorUI
{
    private static GameObject? _layer;
    private static readonly List<GameObject> _tiles = new();
    private static readonly Dictionary<GameObject, int> _tileColors = new();
    private static PlayerTab? _current;

    // 每格渲染（底块 tint 玩家色）
    private const float TileSize = 0.55f;

    private static Sprite _frameSprite;
    private static Sprite _frameSelectedSprite;
    private static Sprite _baseSprite;

    private static void EnsureSprites()
    {
        try
        {
            if (_baseSprite == null) _baseSprite = HudUIAssets.WhiteSprite;
            if (_frameSprite == null) _frameSprite = HudUIAssets.ColorButtonSprite;
            if (_frameSelectedSprite == null) _frameSelectedSprite = HudUIAssets.ColorButtonSelectedSprite;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PlayerColorUI.EnsureSprites]", ex);
        }
    }

    private static void ClearLayer()
    {
        try
        {
            if (_layer != null) { Object.Destroy(_layer); _layer = null; }
            _tiles.Clear();
            _tileColors.Clear();
        }
        catch { }
    }

    /// <summary>颜色选择面板是否处于接管状态（换装菜单开着）。</summary>
    public static bool IsActive => _layer != null;

    [HarmonyPatch("OnEnable")]
    [HarmonyPostfix]
    public static void OnEnablePostfix(PlayerTab __instance)
    {
        try
        {
            _current = __instance;
            ClearLayer();
            EnsureSprites();

            // 布局容器 = 原版色块的父（ColorTabArea）
            Transform? parent = null;
            foreach (var chip in __instance.ColorChips)
            {
                if (chip != null && chip.transform != null && chip.transform.parent != null)
                {
                    parent = chip.transform.parent;
                    break;
                }
            }
            if (parent == null) return;

            _layer = new GameObject("LightColorLayer");
            _layer.transform.SetParent(parent, false);
            _layer.transform.localPosition = Vector3.zero;
            _layer.transform.localScale = Vector3.one;

            // 禁用原版 chips 交互并读取其位置作为自绘布局参照
            foreach (var chip in __instance.ColorChips)
            {
                try
                {
                    if (chip == null) continue;
                    // 只处理本次激活的色块（原版每次 OnEnable 会累积旧的 inactive chip）
                    if (!chip.gameObject.activeSelf) continue;
                    if (chip.Button != null)
                    {
                        chip.Button.enabled = false;
                        chip.Button.OnClick.RemoveAllListeners();
                        chip.Button.OnMouseOver.RemoveAllListeners();
                        chip.Button.OnMouseOut.RemoveAllListeners();
                    }
                    // 取位置参照
                    var pos = chip.transform.localPosition;
                    var tag = 0;
                    try { tag = System.Convert.ToInt32(chip.Tag); } catch { continue; }
                    if (tag < 0 || tag >= Palette.PlayerColors.Length) continue;
                    BuildTile(pos, tag);
                    // 隐藏原版块（保留占位不销毁以免原版 Update 引用空对象）
                    chip.gameObject.SetActive(false);
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("[PlayerColorUI] chip loop", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PlayerColorUI.OnEnablePostfix]", ex);
        }
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    public static void UpdatePostfix(PlayerTab __instance)
    {
        try
        {
            if (_current != __instance) return;
            if (_layer == null) return;
            // 高亮当前已装备颜色
            int equipped = __instance.CurrentColorId();
            for (int i = 0; i < _tiles.Count; i++)
            {
                var go = _tiles[i];
                if (go == null) continue;
                int tileColor = GetTileColor(go);
                SetTileSelected(go, tileColor == equipped);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PlayerColorUI.UpdatePostfix]", ex);
        }
    }

    private static void BuildTile(Vector3 pos, int colorId)
    {
        try
        {
            var tile = new GameObject($"LightColorTile{colorId}");
            tile.layer = LayerExpansion.GetUILayer();
            tile.transform.SetParent(_layer!.transform, false);
            tile.transform.localPosition = pos;
            tile.transform.localScale = Vector3.one * TileSize;

            // 底：纯色块 tint 玩家色
            var baseSr = tile.AddComponent<SpriteRenderer>();
            baseSr.sprite = _baseSprite;
            baseSr.color = Palette.PlayerColors[colorId];

            // 框（常态 / 选中态切换）
            var frameObj = new GameObject("Frame");
            frameObj.layer = LayerExpansion.GetUILayer();
            frameObj.transform.SetParent(tile.transform, false);
            frameObj.transform.localScale = Vector3.one * 1.08f;
            frameObj.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            var frameSr = frameObj.AddComponent<SpriteRenderer>();
            frameSr.sprite = _frameSprite;

            var selObj = new GameObject("Selected");
            selObj.layer = LayerExpansion.GetUILayer();
            selObj.transform.SetParent(tile.transform, false);
            selObj.transform.localScale = Vector3.one * 1.16f;
            selObj.transform.localPosition = new Vector3(0f, 0f, -0.2f);
            var selSr = selObj.AddComponent<SpriteRenderer>();
            selSr.sprite = _frameSelectedSprite;
            selSr.color = UnityEngine.Color.white;
            selObj.SetActive(false);

            // 交互
            var col = tile.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = Vector2.one;
            var pb = tile.SetUpButton(true, null, playSound: true);
            var hoverColor = UnityEngine.Color.Lerp(Palette.PlayerColors[colorId], UnityEngine.Color.white, 0.3f);
            pb.OnMouseOver.AddListener((UnityAction)(() => baseSr.color = hoverColor));
            pb.OnMouseOut.AddListener((UnityAction)(() => baseSr.color = Palette.PlayerColors[colorId]));
            pb.OnClick.AddListener((UnityAction)(() =>
            {
                try
                {
                    var local = PlayerControl.LocalPlayer;
                    if (local != null) local.CmdCheckColor((byte)colorId);
                }
                catch { }
            }));

            _tileColors[tile] = colorId;
            _tiles.Add(tile);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PlayerColorUI.BuildTile]", ex);
        }
    }

    private static int GetTileColor(GameObject go)
    {
        return _tileColors.TryGetValue(go, out int id) ? id : -1;
    }

    private static void SetTileSelected(GameObject go, bool selected)
    {
        try
        {
            var sel = go.transform.FindChild("Selected");
            if (sel != null) sel.gameObject.SetActive(selected);
        }
        catch { }
    }

}
