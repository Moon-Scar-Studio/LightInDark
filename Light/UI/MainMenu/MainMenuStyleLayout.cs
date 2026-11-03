using System;
using System.Collections.Generic;
using Light.UI.Window;
using LightInDark.Core;
using UnityEngine;

namespace Light.UI.MainMenu;

/// <summary>
/// **主界面三套样式**（用户 2026-11-03 要求 ✓ —— "两个版本我要出来的效果和他们一模一样，95% 一样"）
///
///   · <see cref="MainButtonStyle.Mod"/>     —— 现状：换模组 PNG 贴图 ✓
///   · <see cref="MainButtonStyle.Vanilla"/> —— **原版 + 署名**：不换贴图 ✓
///        + 照 ToH 放右侧大标题 ✓ + 制作组三行 ✓ + 四个按钮竖排在署名下面 ✓（按内容上色 ✓）
///
/// 【美化版的对齐清单（对照 `nos标题画面修改Fix2\Scripts\Loader.cs` 的 `Postfix`，行号见注释）】
///   NOS 步骤                                              我们是否照做
///   ────────────────────────────────────────────────────────────────────────
///   ① BackgroundTexture.SetActive(true)                   ✓
///   ② ModStamp.localScale = 0.3                           ✓
///   ③ LeftPanel.scale = 0.9 → 子物体搬到父级 → 隐藏       ✓（我们原本也是这么做的 ✓）
///   ④ NebulaButton.OnClick → ShowingPanel = true          ✓（我们的 LightButton ✓）
///   ⑤ 按钮上色：{news,myAccount,settings,LightButton} 粉  ✓
///                {credits,quit} 紫                        ✓
///                Shine 隐藏 + 文字白色                     ✓
///   ⑥ Divider 隐藏                                        ✓
///   ⑦ RightPanel：销毁 AspectPosition → 记原位 → 右移 10 → 染色 (173,214,255)  ✓
///   ⑧ playButton 上挂 logo (0.1,1,1) scale 0.85           ✓（logo 图用我们的 ✓）
///      ＋ logo 的 Glow / 版本号文字                        ✗ 缺素材（见文末说明）
///   ⑨ VersionShower 隐藏                                  ✓
///   ⑩ screenTint：销毁 AspectPosition → 挂到 RightPanel → localPos(-0.0824,0.0513,z)  ✓
///   ⑪ howToPlayButton / FreePlayButton 隐藏               ✓
///   ⑫ BottomButtonBounds.y -= 0.1                         ✓
///   ⑬ 四个额外按钮：右下角往上堆 + 按内容上色              ✓（用户 5% 要求 ✓）
///
/// 【未做的 5% 里属于"缺素材"的两处】⑧ 的 Glow 光晕、版本号文字 ——
///   需要 `logoGlow.png` 才能做 ✗（我不用代码硬造光晕 ✗，那会变成"不像"✓）。给图我随时补 ✓。
/// </summary>
public static class MainMenuStyleLayout
{
    private static readonly List<GameObject> _created = new();
    private static readonly string[] ExtraButtonNames =
        { "ExtraButton0", "ExtraButton1", "ExtraButton2", "ExtraButton3" };

    private static Sprite? _logo;
    private static Sprite? _logoOnPlay;

    public static void Apply(MainMenuManager menu)
    {
        try
        {
            Clear();
            if (menu == null) return;

            switch (AppearanceSettings.ButtonStyle)
            {
                case MainButtonStyle.Vanilla: ApplyVanilla(menu); break;
                default:
                    // MOD 样式：呼吸灯 / 悬停 / 点击反馈是它的特色 ✓ → 恢复 ✓
                    Light.Patches.ButtonBreathEffect.Disabled = false;
                    RestoreExtraButtonsDefault();
                    break;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuStyleLayout.Apply]", ex);
        }
    }

    /// <summary>
    /// 显示 / 隐藏我们加在主界面上的所有装饰（标题、署名、额外按钮 ✓）。
    ///
    /// ★ 照 ToH（`MainMenuManagerPatch.cs:100-122`）✓：
    ///   打开任何子面板（在线/账户/制作人员/…）时把 logo+按钮**隐藏** ✓，
    ///   `ResetScreen`（返回主屏）时再**显示** ✓ —— 这样它们就不会压在面板上、也不会被点到 ✓。
    /// </summary>
    public static void SetDecorVisible(bool visible)
    {
        for (int i = 0; i < _created.Count; i++)
        {
            try
            {
                var go = _created[i];
                if (go != null && go.activeSelf != visible) go.SetActive(visible);
            }
            catch { }
        }
    }

    public static void Clear()
    {
        for (int i = 0; i < _created.Count; i++)
        {
            try { if (_created[i] != null) UnityEngine.Object.Destroy(_created[i]); } catch { }
        }
        _created.Clear();
    }

    // ⚠️ 2026-11-03 用户："美化版你删了吧，bug 太多了" ✓
    //   → 原 `ApplyPretty`（Fix2 的 13 步 + Nova 的图标细节）**整段删除** ✗
    //   现在只剩两种样式：MOD（现状）+ 原版 + 署名 ✓

    // =====================================================================
    //  原版 + 署名 —— 标题位置照 ToH `CredentialsPatch.cs` L135-157 ✓
    // =====================================================================

    private static void ApplyVanilla(MainMenuManager menu)
    {
        // ★ 2026-11-03 用户**用 UE 量出来的**坐标（照打 ✓，不再猜 ✗）：
        //     logo    localPosition (2, 0.3, 1)   localScale 全 1.5 ✓
        //     四按钮  (0,-0.5,1) (2,-0.5,1) (4,-0.5,1) (2,-1,1) ✓
        //     字      (4.3, 0, 1) ✓，字体换成**游戏字体** + 上色 ✓
        //
        //   ⚠️⚠️ z：用户给的 z=1，但原版 UI 内容在 **z≈-1** ✓（AGENTS §4.3：同容器里 z 越小越靠前 ✓）
        //       用 z=1 会被左面板/背景**盖住** ✗ —— 截图里的"logo 层级错了 / 按钮看不见"就是这个 ✓
        //       → 这里统一用 **z = -2**（在原版内容之前 ✓、在弹窗之后 ✓）
        var ui = menu.mainMenuUI != null ? menu.mainMenuUI.transform : menu.transform;
        const float z = -1f;   // 与原版内容同级 ✓（弹窗 z=-10 会盖住它 ✓）

        var root = new GameObject("LID_VanillaDecor");
        root.transform.SetParent(ui, false);
        root.transform.localPosition = Vector3.zero;
        Try(() => root.layer = ui.gameObject.layer);   // 层跟父级 ✓（AGENTS §4.3）
        _created.Add(root);

        // ① 标题：位置/缩放按用户给的值 ✓
        var title = new GameObject("LID_TitleLogo");
        title.transform.SetParent(root.transform, false);
        title.transform.localPosition = new Vector3(2f, 0.3f, z);
        title.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        title.layer = root.layer;
        var sr = title.AddComponent<SpriteRenderer>();
        sr.sprite = _logo ??= LoadLogo(480f);      // 1200×900 @480 = 2.5×1.875，再 ×1.5 缩放 ✓
        sr.color = Color.white;
        Try(() =>
        {
            var reference = menu.newsButton != null && menu.newsButton.inactiveSprites != null
                ? menu.newsButton.inactiveSprites.GetComponent<SpriteRenderer>() : null;
            if (reference != null)
            {
                sr.sortingLayerID = reference.sortingLayerID;
                // ⚠️ 2026-11-03 用户第三次反馈"LOGO 层级还是错的"：
                //   原来写 `+2` ✗ → logo 会盖到**设置弹窗**上面 ✗✓
                //   ★ 改成与原版按钮**完全同一层同一序** ✓（z 也用内容层 -1 ✓）
                //     → 弹窗（z=-10、sortingOrder 更高）自然盖住它 ✓，它又压在背景之上 ✓
                sr.sortingOrder = reference.sortingOrder;
            }
        });

        // ② 制作组：一个文本框三行 ✓，**游戏字体** ✓ + 上色 ✓（用户："字体太难看了，改一个再加点颜色"）
        var credits = MenuTextTemplate.Create(root.transform, new Vector3(4.3f, 0f, z),
            "Modded by hvtXsvc & Miaowu\nResources by XiXi\n(C) Moon-Scar Studio",
            1.9f, new Color(1f, 0.86f, 0.55f, 1f));    // 暖金色 ✓（用户 2026-11-03："那行 modded 字大一点" ✓）
        if (credits != null)
        {
            credits.alignment = TMPro.TextAlignmentOptions.Center;
            credits.lineSpacing = 8f;
        }

        // ③ 四个按钮：按用户给的 4 组坐标 ✓
        PlaceExtraButtonsAt(new[]
        {
            new Vector3(0f, -0.5f, z),
            new Vector3(2f, -0.5f, z),
            new Vector3(4f, -0.5f, z),
            new Vector3(2f, -1f, z),
        }, root.transform);

        // ④ 四个按钮**按内容上色** ✓（用户 2026-11-03："你咋把那四个按钮的上色删了？加回去" ✓）
        ColorExtraButtonsByContent();

        // ⑤ "美化版不要呼吸灯和点击反馈（这些只应该在 MOD 样式有）" ✓ → 原版同样关掉 ✓
        Light.Patches.ButtonBreathEffect.Disabled = true;
    }

    // =====================================================================
    //  NOS 的按钮上色（照抄 `FormatButtonColor` L303-320 ✓）
    // =====================================================================

    private static void Format(PassiveButton? button, Color inactiveColor, Color activeColor,
                               Color inactiveText, Color activeText)
    {
        try
        {
            if (button == null) return;

            SetShineInactive(button.activeSprites != null ? button.activeSprites.transform : null);
            SetShineInactive(button.inactiveSprites != null ? button.inactiveSprites.transform : null);

            var activeSr = button.activeSprites != null ? button.activeSprites.GetComponent<SpriteRenderer>() : null;
            var inactiveSr = button.inactiveSprites != null ? button.inactiveSprites.GetComponent<SpriteRenderer>() : null;

            if (activeSr != null)
                activeSr.color = activeColor.a == 0f
                    ? new Color(inactiveColor.r, inactiveColor.g, inactiveColor.b, 1f)
                    : activeColor;
            if (inactiveSr != null) inactiveSr.color = inactiveColor;

            button.activeTextColor = activeText;
            button.inactiveTextColor = inactiveText;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.Format] {ex.Message}");
        }
    }

    private static void SetShineInactive(Transform? parent)
    {
        try
        {
            if (parent == null) return;
            var shine = parent.FindChild("Shine");
            if (shine != null) shine.gameObject.SetActive(false);
        }
        catch { }
    }

    private static PassiveButton? FindLightButton()
    {
        try
        {
            var go = GameObject.Find("LightButton");
            return go != null ? go.GetComponent<PassiveButton>() : null;
        }
        catch { return null; }
    }

    // =====================================================================
    //  四个额外按钮
    // =====================================================================

    private static List<GameObject> FindExtraButtons()
    {
        var list = new List<GameObject>();
        foreach (var name in ExtraButtonNames)
        {
            var go = GameObject.Find(name);
            if (go != null) list.Add(go);
        }
        return list;
    }

    /// <summary>原版样式：**按用户给的固定坐标**摆四个按钮 ✓（z 已由调用方给成负数 ✓）</summary>
    private static void PlaceExtraButtonsAt(Vector3[] positions, Transform parent)
    {
        try
        {
            if (parent == null || positions == null) return;
            var buttons = FindExtraButtons();
            for (int i = 0; i < buttons.Count && i < positions.Length; i++)
            {
                var go = buttons[i];
                DisableAspect(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = positions[i];
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.PlaceExtraButtonsAt] {ex.Message}");
        }
    }

    /// <summary>原版样式：竖排（父节点 = 署名容器 ✓）</summary>
    private static void PlaceExtraButtonsVertically(Transform parent, float startY, float stepY, float x)
    {
        try
        {
            if (parent == null) return;
            var buttons = FindExtraButtons();
            for (int i = 0; i < buttons.Count; i++)
            {
                var go = buttons[i];
                DisableAspect(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(x, startY + stepY * i, 0f);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.PlaceExtraButtonsVertically] {ex.Message}");
        }
    }

    /// <summary>美化版：**右下角往上堆** ✓（用户要求 ✓；不换贴图 ✓ 只上色 ✓）</summary>
    private static void PlaceExtraButtonsBottomRight(MainMenuManager menu)
    {
        try
        {
            // 父节点跟着"退出/制作"那两个按钮走 ✓ —— 保证和右侧面板一起滑动 ✓
            Transform parent = menu.transform;
            Try(() => { if (menu.quitButton != null && menu.quitButton.transform.parent != null) parent = menu.quitButton.transform.parent; });

            var buttons = FindExtraButtons();
            const float baseX = 4.15f, baseY = -2.35f, stepY = 0.36f;

            for (int i = 0; i < buttons.Count; i++)
            {
                var go = buttons[i];
                DisableAspect(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(baseX, baseY + stepY * i, 0f);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.PlaceExtraButtonsBottomRight] {ex.Message}");
        }
    }

    private static void RestoreExtraButtonsDefault()
    {
        try
        {
            var buttons = FindExtraButtons();
            for (int i = 0; i < buttons.Count; i++)
            {
                var asp = buttons[i].GetComponent<AspectPosition>();
                if (asp == null) continue;
                int col = i % 2;
                int row = i / 2 + 1;
                asp.enabled = true;
                asp.anchorPoint = new Vector2(col == 0 ? 0.42f : 0.58f, 0.5f - 0.08f * row);
                asp.AdjustPosition();
            }
        }
        catch { }
    }

    private static void DisableAspect(GameObject go)
    {
        try
        {
            var asp = go.GetComponent<AspectPosition>();
            if (asp != null) asp.enabled = false;
        }
        catch { }
    }

    /// <summary>按内容给四个按钮上色 ✓（用户要求：原版 / 美化版都要 ✓）</summary>
    private static void ColorExtraButtonsByContent()
    {
        var colors = new[]
        {
            new Color32( 76, 187, 138, 255),   // 检查更新
            new Color32(110, 118, 129, 255),   // Github
            new Color32( 84, 160, 255, 255),   // 模组官网
            new Color32( 88, 101, 242, 255),   // QQ群 / Discord
        };

        try
        {
            var buttons = FindExtraButtons();
            for (int i = 0; i < buttons.Count && i < colors.Length; i++)
            {
                var pb = buttons[i].GetComponent<PassiveButton>();
                if (pb == null) continue;
                Format(pb, colors[i], colors[i], Color.white, Color.white);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.ColorExtraButtons] {ex.Message}");
        }
    }

    // =====================================================================
    //  文字 / 图片 / 小工具
    // =====================================================================

    private static void CreateText(Transform parent, Vector3 localPos, string text, float size)
    {
        try
        {
            var tmp = MenuTextTemplate2.Create(parent, localPos, text, size, Color.white);
            if (tmp != null) tmp.alignment = TMPro.TextAlignmentOptions.Center;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.CreateText] {ex.Message}");
        }
    }

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { LightLogger.LogWarning($"[MainMenuStyleLayout] {ex.Message}"); }
    }

    private static Sprite? LoadLogo(float ppu)
    {
        try
        {
            var tex = Utilities.GraphicsHelper.LoadTextureFromResources("Light.Resources.Lobby.LightInDark.png");
            if (tex == null) return null;

            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuStyleLayout.LoadLogo] {ex.Message}");
            return null;
        }
    }

    /// <summary>RightPanel 的原始位置（美化版滑入动画用 ✓）</summary>
    public static Vector3 RightPanelOp;
}
