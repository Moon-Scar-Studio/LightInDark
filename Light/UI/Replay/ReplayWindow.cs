using System;
using Light.UI.HudUI;                     // HudUIWindow / HudUIAssets（和 PresetWindow 同一个 using ✓）
using LightInDark.Core;
using LightInDark.Game;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.UI.Replay;

/// <summary>
/// **复盘窗口**（用户 2026-10-06 要求：游戏结束后在大厅点左上角按钮打开 ✓）。
///
/// ⚠️ 本轮只把**窗口做出来** ✓ —— 文字布局/分页/筛选这些按用户说的"到时候再说" ✗
///    现在：标题 + 一段可滚动的复盘文本（`LightPlayerDataManager.BuildReplayText()` ✓）+ 关闭按钮 ✓
///
/// ⚠️ 实现要点（照抄工程里已验证的 `PresetSaveWindow` 写法 ✓）：
///   · `HudUIWindow.Create(title, size, parent)` —— parent 挂 `HudManager.transform` ✓
///     （AGENTS §6：HUD 内窗口挂 HudManager、z ≈ -50 最稳 ✓）
///   · `AddText` / `AddButton` 都是**先建父物体再挂真正的 TMP/按钮** ✓
///   · 建完统一 `ApplyCjkFont()` ✓（否则中文可能不显示 ✗）
///   · 关闭用 `Close()` ✓；再点一次按钮 = 关掉（开关语义 ✓）
/// </summary>
public static class ReplayWindow
{
    private static HudUIWindow? _w;
    private static TextMeshPro? _body;

    private static readonly Vector2 WinSize = new(7.4f, 4.6f);

    public static bool IsOpen => _w != null && _w.GameObject != null;

    /// <summary>开关：没开就开，开着就关 ✓</summary>
    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public static void Open()
    {
        try
        {
            if (IsOpen) return;

            // 父物体：与其它 HUD 窗口一致挂 HudManager ✓
            var hud = HudManager.Instance;
            if (hud == null) { LightLogger.LogWarning("[ReplayWindow] HudManager 不存在，无法开窗"); return; }

            _w = HudUIWindow.Create("复盘信息", WinSize, hud.transform);
            if (_w == null || _w.Screen == null)
            {
                _w = null;
                LightLogger.LogWarning("[ReplayWindow] 窗口创建失败");
                return;
            }

            var root = _w.Screen.transform;

            // 标题
            var title = _w.AddText("复盘信息", 2.0f, TextAlignmentOptions.Center);
            if (title != null) Place(title.transform, root, new Vector3(0f, WinSize.y * 0.5f - 0.45f, -2f));

            // 正文（复盘文本；先直接放完整内容，滚动/分页等你定 ✓）
            string text;
            try { text = LightPlayerDataManager.BuildReplayText() ?? ""; }
            catch (Exception ex) { text = ""; LightLogger.LogWarning($"[ReplayWindow] 取复盘文本失败：{ex.Message}"); }
            if (string.IsNullOrWhiteSpace(text)) text = "（本局没有可显示的复盘数据）";

            _body = _w.AddText(text, 1.15f, TextAlignmentOptions.TopLeft);
            if (_body != null)
            {
                _body.enableAutoSizing = false;
                _body.fontSize = 1.15f;
                _body.enableWordWrapping = true;
                _body.overflowMode = TextOverflowModes.Truncate;   // 内容超出先截断（滚动条以后再上 ✓）
                Place(_body.transform, root, new Vector3(0f, -0.15f, -2f));

                var rect = _body.rectTransform;
                if (rect != null) rect.sizeDelta = new Vector2(WinSize.x - 0.7f, WinSize.y - 1.5f);
            }

            // 关闭
            var close = _w.AddButton("关闭", Close, new Vector2(1.7f, 0.56f));
            if (close != null && close.GameObject != null)
                close.SetPosition(new Vector3(0f, -(WinSize.y * 0.5f) + 0.45f, -2f));

            try { _w.ApplyCjkFont(); } catch { }

            // AGENTS §6：HUD 内窗口挂 HudManager、z ≈ -50 ✓
            // ⚠️ `HudUIWindow` 自身没有 SetPosition（那是 `HudUIButton` 的方法 ✗）→ 直接改它的 GameObject ✓
            if (_w.GameObject != null) _w.GameObject.transform.localPosition = new Vector3(0f, 0f, -50f);

            LightLogger.Log($"[ReplayWindow] 已打开（正文 {(_body != null ? _body.text.Length : 0)} 字）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ReplayWindow.Open]", ex);
        }
    }

    public static void Close()
    {
        try
        {
            _w?.Close();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ReplayWindow.Close] {ex.Message}");
        }
        finally
        {
            _w = null;
            _body = null;
        }
    }

    /// <summary>把子物体放到 root 的局部坐标（窗口内布局 ✓）</summary>
    private static void Place(Transform? child, Transform root, Vector3 localPos)
    {
        try
        {
            if (child == null) return;
            child.SetParent(root, false);
            child.localPosition = localPos;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ReplayWindow.Place] {ex.Message}");
        }
    }
}
