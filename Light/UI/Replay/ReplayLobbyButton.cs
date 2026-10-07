using System;
using HarmonyLib;
using Light.UI.Window;
using LightInDark.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Light.UI.Replay;

/// <summary>
/// **大厅左上角的"复盘信息"按钮**（用户 2026-10-06 要求 ✓）：
///   · 克隆一个**原版按钮**（优先 `HudManager.SettingsButton` 齿轮 ✓，兜底 `GameStartManager.InviteFriendsButton` ✓）
///   · 放在**屏幕左上角** ✓
///   · **鼠标悬停** → 弹详情框「复盘信息」 ✓（走工程统一的 `DetailPopup` ✓）
///   · **点击** → 打开 <see cref="ReplayWindow"/> ✓
///
/// ⚠️ 只在**有上一局复盘数据**时出现 ✓（= "游戏结束后在大厅时" ✓；第一次进大厅还没有数据就不显示 ✓）
/// ⚠️ 克隆原版按钮的规矩（AGENTS §4.5）：**必须整体替换 `OnClick`** ✗ 否则会连原版功能一起触发
///    （那个齿轮按钮点下去会打开设置菜单 ✗）；`OnMouseOver/OnMouseOut` 是**追加**监听即可 ✓
/// </summary>
internal static class ReplayLobbyButton
{
    private static GameObject? _btn;
    private static Transform? _tplParent;      // 模板所在的父物体（用它保证坐标系/缩放一致 ✓）
    private static float _tplZ = -50f;         // 模板的 z（已验证能显示的深度 ✓）

    /// <summary>进入大厅时创建（幂等 ✓）</summary>
    public static void Ensure()
    {
        try
        {
            if (_btn != null) return;                                  // 已经建过 ✓

            // 没有复盘数据就不显示（= 还没打完一局 ✓）
            if (string.IsNullOrWhiteSpace(SafeReplayText())) return;

            var tpl = FindTemplate(out var template);
            if (tpl == null) { LightLogger.LogWarning("[ReplayLobbyButton] 找不到可克隆的原版按钮"); return; }

            _btn = UnityEngine.Object.Instantiate(template, null);
            _btn.name = "LightReplayButton";

            // ⚠️⚠️ 2026-10-06 用户报「复盘按钮和设置按钮重叠了，位置压根没变」——
            //    根因：克隆体留在**原版按钮的父物体**下 ✗，而那里有 `AspectPosition`
            //    每帧 `AdjustPosition()` 把位置算回去（AGENTS §4.4 第一条 ✓）
            //    → 抄 Nebula 的做法（`NebulaManager.cs:131`）：
            //      **挂到 UI 相机下面** ✓ —— 没有任何原版布局组件会再来动它 ✓✓
            //    同时把克隆体上可能带的 AspectPosition / AspectScaler 一并干掉（双保险 ✓）
            DestroyLayoutDrivers(_btn);

            var cam = FindUiCamera(template);
            if (cam != null)
            {
                _btn.transform.SetParent(cam.transform, false);
                _btn.transform.localScale = Vector3.one;

                // 屏幕坐标 → 世界坐标（左上角：x 约 6% 处、y 约 93% 处 ✓）
                float sx = Screen.width * 0.06f;
                float sy = Screen.height * 0.93f;
                var world = cam.ScreenToWorldPoint(new Vector3(sx, sy, Mathf.Abs(cam.transform.position.z) > 0.01f ? Mathf.Abs(cam.transform.position.z) : 10f));
                world.z = 0f;   // 与相机同平面（z 交给 sorting ✓）
                _btn.transform.position = world;
            }
            else
            {
                LightLogger.LogWarning("[ReplayLobbyButton] 没找到 UI 相机 → 退回原父物体定位（可能被 AspectPosition 覆盖 ✗）");
                _btn.transform.SetParent(template.transform.parent, false);
                float edgeX = 3f * Screen.width / Mathf.Max(1f, Screen.height) - 0.8f;
                _btn.transform.localPosition = new Vector3(-edgeX + 0.55f, 2.05f, template.transform.localPosition.z);
            }

            // 文字：原版按钮自带 TMP → 改成"复盘"
            var label = _btn.GetComponentInChildren<TMPro.TextMeshPro>(true);
            if (label != null)
            {
                label.text = "复盘";
                label.enableAutoSizing = false;
                var tr = label.GetComponent<TextTranslatorTMP>();
                if (tr != null) tr.enabled = false;      // 别让翻译器改回去 ✗（AGENTS §13.3 同款坑）
            }

            // ★ 必须**整体替换** OnClick（AGENTS §4.5）—— 否则点它会连"设置菜单"一起打开 ✗
            var passive = _btn.GetComponent<PassiveButton>();
            if (passive != null)
            {
                passive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                passive.OnClick.AddListener((UnityAction)(() =>
                {
                    try { ReplayWindow.Toggle(); }
                    catch (Exception ex) { LightLogger.LogError("[ReplayLobbyButton] 打开复盘失败", ex); }
                }));

                // 悬停提示：**追加**监听 ✓（原版的视觉反馈保留 ✓）
                passive.OnMouseOver.AddListener((UnityAction)(() =>
                {
                    try { DetailPopup.Show("复盘信息", true, _btn!.transform); } catch { }
                }));
                passive.OnMouseOut.AddListener((UnityAction)(() =>
                {
                    try { DetailPopup.Hide(); } catch { }
                }));
            }

            _btn.SetActive(true);
            LightLogger.Log($"[ReplayLobbyButton] 已创建（模板={template.name} 父={(cam != null ? cam.name : "原父物体")} " +
                            $"屏幕位置=({Screen.width * 0.06f:0},{Screen.height * 0.93f:0})）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ReplayLobbyButton.Ensure]", ex);
        }
    }

    /// <summary>
    /// 干掉克隆体上**每帧会写位置/缩放**的原版组件 ✓（AGENTS §4.4）——
    /// 不干掉的话，我们设完位置下一帧就被它算回去 ✗（用户报的"位置压根没变"就是这个 ✓）
    /// </summary>
    private static void DestroyLayoutDrivers(GameObject go)
    {
        try
        {
            var ap = go.GetComponent<AspectPosition>();
            if (ap != null) { ap.enabled = false; UnityEngine.Object.Destroy(ap); }

            var apParent = go.GetComponentInParent<AspectPosition>();
            if (apParent != null) apParent.enabled = false;   // 父物体上的也先关掉（我们已换父，双保险 ✓）

            // ⚠️ 原版没有 `AspectScaler`（那是 nebula 的叫法 ✗）—— 原版是 `AspectSize`（AGENTS §4.4 第三类）
            var size = go.GetComponent<AspectSize>();
            if (size != null) { size.enabled = false; UnityEngine.Object.Destroy(size); }

            LightLogger.Log("[ReplayLobbyButton] 已移除克隆体上的布局驱动组件（AspectPosition/AspectSize）✓");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ReplayLobbyButton.DestroyLayoutDrivers] {ex.Message}");
        }
    }

    /// <summary>找渲染这个按钮的**UI 相机**（按 layer 的 cullingMask 判定 ✓，见 AGENTS §4.3）</summary>
    private static Camera? FindUiCamera(GameObject sample)
    {
        try
        {
            int layerMaskBit = 1 << sample.layer;
            Camera? best = null;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null) continue;
                if ((cam.cullingMask & layerMaskBit) == 0) continue;      // 这台相机不画这个层 ✗
                if (best == null || cam.depth > best.depth) best = cam;   // 取最靠后的那台（UI 相机 depth 更高 ✓）
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>离开大厅/换局时清掉（避免跨局残留 ✓）</summary>
    public static void Destroy()
    {
        try
        {
            if (_btn != null) UnityEngine.Object.Destroy(_btn);
        }
        catch { }
        finally
        {
            _btn = null;
        }
    }

    /// <summary>
    /// 找可克隆的原版按钮：
    ///   ① `HudManager.SettingsButton`（齿轮图标，最像"角落小按钮" ✓）
    ///   ② `GameStartManager.InviteFriendsButton`（大厅的"邀请好友" ✓）
    ///   ③ `HudManager.MapButton`（兜底 ✓）
    /// </summary>
    private static GameObject? FindTemplate(out GameObject template)
    {
        template = null!;

        try
        {
            var hud = HudManager.Instance;
            if (hud != null)
            {
                if (hud.SettingsButton != null) { template = hud.SettingsButton; return hud.SettingsButton; }
                if (hud.MapButton != null && hud.MapButton.gameObject != null)
                {
                    template = hud.MapButton.gameObject;
                    return template;
                }
            }
        }
        catch { }

        try
        {
            var gsm = GameStartManager.Instance;
            if (gsm != null && gsm.InviteFriendsButton != null)
            {
                template = gsm.InviteFriendsButton;
                return template;
            }
        }
        catch { }

        return null;
    }

    private static string SafeReplayText()
    {
        try { return LightInDark.Game.LightPlayerDataManager.BuildReplayText() ?? ""; }
        catch { return ""; }
    }
}

/// <summary>
/// 进入大厅时创建按钮（`LobbyBehaviour.Start` 每次进大厅都会跑 ✓）。
/// ⚠️ 类级 `[HarmonyPatch]` 必须写 —— 否则整个类被 Harmony 静默跳过 ✗（AGENTS §4.1）
///
/// ⚠️ **不再额外 patch `LobbyBehaviour.OnDestroy` 去清理** ✗：
///    那个方法在 IL2CPP 里不一定存在（patch 不到会白报一条错 ✗），而按钮挂在原版按钮的父物体下，
///    会**跟着大厅一起销毁** ✓；`Ensure()` 里的 `if (_btn != null) return;` 用的是 Unity 的 `==` 重载
///    （AGENTS §4.6.1）→ 已销毁对象会被识别成假 null ✓ → 下次进大厅自动重建 ✓（自愈 ✓）
/// </summary>
[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
internal static class ReplayLobbyButtonPatch
{
    public static void Postfix()
    {
        try { ReplayLobbyButton.Ensure(); }
        catch (Exception ex) { LightLogger.LogWarning($"[ReplayLobbyButtonPatch] {ex.Message}"); }
    }
}
