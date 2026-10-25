using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Utilities;
using Light.UI.Window;
using Light.UI.MainMenu;
using LightInDark.Core;
using TMPro;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// 更换主界面左侧按钮样式：把六张自定义按钮图裁剪掉四周透明留白后替换原版贴图，
/// 并修正点击碰撞盒，保持按钮条位置与原始宽高比。
/// </summary>
[HarmonyPatch(typeof(MainMenuManager))]
public static class MainMenuButtonSpritePatch
{
    private const byte AlphaThreshold = 8;

    /// <summary>文件名与 MainMenuManager 字段选择器一一对应。</summary>
    private static readonly (string File, Func<MainMenuManager, PassiveButton> Pick)[] Buttons =
    [
        ("Buttons/MainMenu/1787250791214.png", m => m.playButton),
        ("Buttons/MainMenu/1787250799180.png", m => m.inventoryButton),
        ("Buttons/MainMenu/1787250815120.png", m => m.shopButton),
        ("Buttons/MainMenu/1787250837638.png", m => m.newsButton),
        ("Buttons/MainMenu/1787250844759.png", m => m.myAccountButton),
        ("Buttons/MainMenu/1787250853178.png", m => m.settingsButton),
        ("Buttons/MainMenu/1787149768081.png", m => m.creditsButton),
        ("Buttons/MainMenu/1787149768081.png", m => m.quitButton),
    ];

    [HarmonyPatch(nameof(MainMenuManager.Start))]
    [HarmonyPostfix]
    public static void StartPostfix(MainMenuManager __instance)
    {
        try
        {
            // ⚠️ 样式不是 MOD 时**整个跳过**。
            //    用户要的「原版按钮（一点贴图都不换）」只能靠"根本不替换"来实现 ——
            //    因为下面的 HideDecorations 会 **Destroy** 掉图标，销毁了没法还原，
            //    "先替换再撤回"这条路走不通。
            if (Suppress || StyleIsNotMod())
            {
                LightLogger.Log("[MainMenuButtonSprite] 当前不是 MOD 样式，跳过左侧按钮贴图替换");
                return;
            }

            foreach (var (file, pick) in Buttons)
            {
                try
                {
                    var btn = pick(__instance);
                    if (btn == null)
                    {
                        LightLogger.LogWarning($"[MainMenuButtonSprite] 按钮为空，跳过 {file}");
                        continue;
                    }
                    ReplaceButton(btn, file);
                }
                catch (Exception ex)
                {
                    LightLogger.LogError($"[MainMenuButtonSprite] 替换失败 {file}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.StartPostfix]", ex);
        }
    }

    #region 新增：右侧面板（点击"开始"后出现）按钮贴图

    /// <summary>
    /// 文件名 → 右侧面板按钮。
    /// 这些图是 1920x1080 大画布 + 透明留白，只画了按钮那一块；ReplaceButton 会自动裁掉透明边。
    /// 缩放用 fitInside：等比缩放到"完整放进原按钮框"，不会越界、不会顶到相邻按钮；
    /// 觉得太小/太大就调该条目的 Scale（1.2 = 放大 20%，0.9 = 缩小 10%）。
    /// </summary>
    private static readonly (string File, Func<MainMenuManager, PassiveButton?> Pick, float Scale)[] RightPanelButtons =
    [
        ("Buttons/MainMenu/Local.png",      m => m.playLocalButton,  1f),
        ("Buttons/MainMenu/OnLine.png",     m => m.PlayOnlineButton, 1f),
        ("Buttons/MainMenu/CreateGame.png", m => m.createGameButton, 1f),
        ("Buttons/MainMenu/EnterCode.png",  PickEnterCodeButton,     1f),
        ("Buttons/MainMenu/FindGame.png",   PickFindGame,            1f),
    ];

    /// <summary>
    /// 「输入代码」要找的是 OnlineButtons 里那张大卡片 "Enter Code Button"（3.78x2.64，
    /// 与"创建大厅/寻找游戏"同结构）。
    /// 注意：MainMenuManager.enterCodeButtons 指的是**点进去之后的整个面板**
    /// （里面有 JoinGame / FieldsContainer…），不是这张卡片 —— 所以先按名字精确找。
    /// </summary>
    private static PassiveButton? PickEnterCodeButton(MainMenuManager m)
    {
        try
        {
            // ★ 2026-10-25 性能修复：原来这里是 `FindObjectsOfType<PassiveButton>(true)` ✗
            //    —— 全场景枚举（含未激活对象），每次进主菜单跑一遍 → 卡一下 ✓
            //    按 AGENTS §5.1 记录的路径**限定在 Online Buttons 子树里找** ✓
            //    （`OnlineButtons/AspectSize/Scaler/{Create Lobby Button, Find Game Button, Enter Code Button, Line}`）
            Transform? onlineRoot = null;
            try
            {
                var ui = m.mainMenuUI;
                if (ui != null)
                    onlineRoot = ui.transform.FindChild("AspectScaler")?.FindChild("Online Buttons");
            }
            catch { }
            if (onlineRoot == null) onlineRoot = m.transform;      // 兜底：至少限定在主界面内 ✓

            foreach (var pb in onlineRoot.GetComponentsInChildren<PassiveButton>(true))
            {
                if (pb == null) continue;
                if (pb.gameObject.name == "Enter Code Button") return pb;
            }

            // ② 兜底（老行为）：enterCodeButtons 面板里的第一个按钮
            var go = m.enterCodeButtons;
            if (go != null)
            {
                var pb = go.GetComponent<PassiveButton>();
                if (pb == null) pb = go.GetComponentInChildren<PassiveButton>(true);
                if (pb != null) return pb;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuButtonSprite] 找 Enter Code Button 失败：{ex.Message}");
        }
        return null;
    }

    /// <summary>findGameButton 的类型是 FindGameButton（纯 MonoBehaviour），真正的按钮组件挂在同一个物体上。</summary>
    private static PassiveButton? PickFindGame(MainMenuManager m)
    {
        try
        {
            var fg = m.findGameButton;
            if (fg == null) return null;
            return fg.gameObject.GetComponent<PassiveButton>();
        }
        catch { return null; }
    }

    [HarmonyPatch(nameof(MainMenuManager.Start))]
    [HarmonyPostfix]
    public static void RightPanelPostfix(MainMenuManager __instance)
    {
        try
        {
            if (Suppress || StyleIsNotMod())
            {
                LightLogger.Log("[MainMenuButtonSprite] 当前不是 MOD 样式，跳过右侧面板贴图替换");
                return;
            }

            foreach (var (file, pick, scale) in RightPanelButtons)
            {
                try
                {
                    var btn = pick(__instance);
                    if (btn == null)
                    {
                        LightLogger.LogWarning($"[MainMenuButtonSprite] 右侧按钮为空，跳过 {file}");
                        continue;
                    }
                    ReplaceButton(btn, file, fitInside: true, scale: scale, hideExtraRenderers: true);
                }
                catch (Exception ex)
                {
                    LightLogger.LogError($"[MainMenuButtonSprite] 右侧替换失败 {file}", ex);
                }
            }

            if (_watch.Count > 0) _watchFrame = 0;   // 启用保图守卫
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.RightPanelPostfix]", ex);
        }
    }

    /// <summary>保图守卫的帧计数（-1 = 未启用）。</summary>
    private static int _watchFrame = -1;
    private static int _guardLogs;                    // 保图日志条数上限（避免刷屏）

    /// <summary>
    /// 保图守卫：**每帧**检查我们贴的图有没有被原版改回，发现就立刻补回来。
    ///
    /// 两个要点：
    ///  1. **每帧**检查。早期版本是"前 10 秒每帧、之后每 60 帧查一次" —— 那会让原版贴图整整显示几十帧。
    ///  2. <see cref="HarmonyPriority"/> = <c>Priority.Last</c>：让本 postfix 在同一个 LateUpdate 的
    ///     其它 postfix **之后**执行，保证"本帧最后写入者"是我们。否则就会出现两边每帧互相覆盖
    ///     （画面一帧原版一帧模组，即"打架"）。
    /// </summary>
    [HarmonyPatch("LateUpdate")]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void WatchLateUpdate()
    {
        if (_watchFrame < 0) return;
        _watchFrame++;

        GuardSprites();
    }

    /// <summary>
    /// 发现贴图被改回就立刻再贴一次。
    /// 只守**贴图**、不守颜色：颜色是原版的正常功能（鼠标悬浮/禁用时 ButtonRolloverHandler 会改颜色），
    /// 之前连颜色一起强写会把悬浮反馈吃掉，也是"打架"的一部分。
    /// </summary>
    private static void GuardSprites()
    {
        // ⚠️ 外部（MainMenuButtonStyler）切到「原版样式 / 亚克力样式」时会把这里置 true。
        //    那时必须停手，否则每帧把模组 PNG 补回来，用户选的样式根本显示不出来。
        if (Suppress || StyleIsNotMod()) return;

        foreach (var (label, sr, expected) in _watch)
        {
            try
            {
                if (sr == null || expected == null) continue;
                if (sr.sprite == expected) continue;          // 还是我们的图 → 什么都不做

                string was = sr.sprite != null ? sr.sprite.name : "(null)";
                sr.sprite = expected;

                if (_guardLogs < 20)
                {
                    _guardLogs++;
                    LightLogger.LogVerbose($"[MainMenuButtonSprite][保图] {label} 被改回（原为 {was}），已重新贴上 · 第 {_watchFrame} 帧");
                }
                else if (_guardLogs == 20)
                {
                    _guardLogs++;
                    LightLogger.LogVerbose("[MainMenuButtonSprite][保图] 日志已达 20 条上限，后续只补图不再记录");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[MainMenuButtonSprite][保图] {label}: {ex.Message}");
            }
        }
    }

    #endregion

    private static void ReplaceButton(PassiveButton btn, string relativePath,
        bool fitInside = false, float scale = 1f, bool hideExtraRenderers = false)
    {
        var texture = ResourceHelper.LoadTexture(relativePath);
        if (texture == null)
        {
            LightLogger.LogWarning($"[MainMenuButtonSprite] 加载纹理失败 {relativePath}");
            return;
        }

        if (!FindOpaqueBounds(texture, AlphaThreshold, out int minX, out int minY, out int width, out int height))
        {
            LightLogger.LogWarning($"[MainMenuButtonSprite] 未检测到非透明区域，使用整图 {relativePath}");
            minX = 0;
            minY = 0;
            width = texture.width;
            height = texture.height;
        }

        // ── 找"主渲染器" ────────────────────────────────────────────────
        // 左侧按钮：贴图挂在 activeSprites 上，按老逻辑找即可。
        // 右侧面板（fitInside = true）：结构五花八门，必须用启发式挑，否则会像上一版那样翻车 ——
        //   本地/在线：真正的卡片大图是 Scaler/Background（5.12x5.12，sprite=mainscreen1/onlinemode），
        //              而 Scaler/Label/Highlight|Inactive|Disabled 只是 0.90x0.81 的小标签层。
        //              上一版把这些小标签当主图 → 新图被塞进小槽里，看起来"被压得很狠"。
        //   输入代码：真正的底是 Background（0.48x0.48），但 Checkmark（0.60x0.48）面积更大，
        //              上一版按"第一个/最大"挑就挑成了勾选图标 → 输入框贴图根本没换。
        //   创建/搜索：三个状态图（Highlight/Inactive/Clicked）尺寸完全一致，都要换。
        // 规则：优先"名字像背景/FIELD 的图"里面积最大的；没有再退到"面积最大的"。
        var containers = new[]
        {
            btn.activeSprites, btn.inactiveSprites, btn.disabledSprites,
            btn.selectedSprites, btn.selectedInactiveSprites, btn.onClickSprites,
        };

        var mainSr = fitInside ? FindMainRendererSmart(btn) : FindMainRenderer(btn, containers);
        if (mainSr == null)
        {
            LightLogger.LogWarning($"[MainMenuButtonSprite] 找不到任何带贴图的 SpriteRenderer，跳过 {relativePath}");
            return;
        }

        var oldSpr = mainSr.sprite;
        float basePPU = oldSpr != null ? oldSpr.pixelsPerUnit : 100f;
        float oldMainArea = SpriteArea(oldSpr);        // 换图前先记下主图面积，用于判断"状态图是否全尺寸"

        // 沿用原按钮锚点语义：用原 sprite 归一化 pivot 作为新裁剪 sprite 的 pivot，
        // 使按钮条中心对准原按钮中心，避免整体偏移（原锚点默认在中心 0.5）
        var pivot = oldSpr != null && oldSpr.rect.width > 0f && oldSpr.rect.height > 0f
            ? new Vector2(oldSpr.pivot.x / oldSpr.rect.width, oldSpr.pivot.y / oldSpr.rect.height)
            : new Vector2(0.5f, 0.5f);

        // 换算 PPU：
        //   fitInside = false（左侧按钮沿用原行为）—— 与原按钮同宽，高度按图片比例走
        //   fitInside = true （右侧面板）        —— 等比缩放，完整"放进"原按钮框内
        float ppu = basePPU;
        if (oldSpr != null && oldSpr.bounds.size.x > 0f && oldSpr.bounds.size.y > 0f)
        {
            float oldW = oldSpr.bounds.size.x;
            float oldH = oldSpr.bounds.size.y;

            if (fitInside)
            {
                float aspect = (float)width / height;                 // 新图宽高比
                float targetW = Mathf.Min(oldW, oldH * aspect);        // 受高度限制时按高度反推宽度
                ppu = width / targetW;
                LightLogger.Log($"[MainMenuButtonSprite] {relativePath} 适配：原框 {oldW:F2}x{oldH:F2}，新图比例 {aspect:F2}，目标宽 {targetW:F2}");
            }
            else
            {
                ppu = width / oldW;
            }

            if (scale > 0.01f) ppu /= scale;                           // scale > 1 → 变大
        }

        var sprite = Sprite.Create(texture, new Rect(minX, minY, width, height), pivot, ppu);
        try { sprite.name = "LID_" + System.IO.Path.GetFileNameWithoutExtension(relativePath); } catch { }   // 给运行时 sprite 起名，日志才看得清

        // ── 贴图赋值 ────────────────────────────────────────────────────
        // 每一次改动都登记一条"撤销动作"，供切换回原版样式时还原。
        var assigned = new HashSet<SpriteRenderer> { mainSr };
        Record(() => { if (mainSr != null) mainSr.sprite = oldSpr; });
        mainSr.sprite = sprite;

        // 全尺寸的状态贴图（>= 主图面积的 50%）也一并换成新图：
        // 创建/搜索的三个状态图就属于这种，换掉后悬浮/按下/禁用都不会闪回原版。
        // 小尺寸的状态贴图（本地/在线那两个 0.90x0.81 小标签）不换，交给下面统一关掉。
        foreach (var container in containers)
        {
            if (container == null) continue;
            var sr = PickInContainer(container);
            if (sr == null || assigned.Contains(sr)) continue;

            // 左侧按钮（fitInside=false）沿用老行为：所有状态容器一律换图，
            // 否则会出现"常态显示原版、鼠标悬浮才变模组"的错乱（状态图和主图尺寸差别大时会踩到）。
            // 右侧面板才用"面积 >= 主图 50% 才算全尺寸状态图"的规则。
            bool fullSize = !fitInside || (oldMainArea > 0f && SpriteArea(sr.sprite) >= oldMainArea * 0.5f);
            if (fullSize)
            {
                var srCaptured = sr;
                var oldSprState = sr.sprite;
                Record(() => { if (srCaptured != null) srCaptured.sprite = oldSprState; });
                sr.sprite = sprite;
                assigned.Add(sr);
            }
        }

        // ── 关掉多余渲染器 ──────────────────────────────────────────────
        // 原版卡片上还叠着"内嵌缩略图 / 小标签 / Shine 辉光"等层，会盖在新图上面。
        // 这里用【组件级】sr.enabled = false：PassiveButton 切状态是 SetActive 容器，
        // 组件级关闭不会被它重新打开（用 SetActive 关会被覆盖，原版贴图会再冒出来）。
        if (hideExtraRenderers)
        {
            int hidden = 0;
            foreach (var sr in btn.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || assigned.Contains(sr)) continue;
                if (!sr.enabled) continue;
                var srOff = sr;
                Record(() => { if (srOff != null) srOff.enabled = true; });
                sr.enabled = false;
                hidden++;
            }
            LightLogger.Log($"[MainMenuButtonSprite] {relativePath} 关闭多余渲染器 {hidden} 个");
        }

        LightLogger.Log($"[MainMenuButtonSprite] {relativePath} 裁剪 {minX},{minY} {width}x{height} PPU={ppu:F2} 世界尺寸={sprite.bounds.size.x:F2}x{sprite.bounds.size.y:F2} 主渲染器={RelPath(btn.transform, mainSr)}");

        // 登记保图：替换后每帧盯着这张图，被原版改回去就立刻补
        _watch.Add((relativePath, mainSr, sprite));

        FixCollider(btn, mainSr);
        HideDecorations(btn);
        SetButtonTextColor(btn);
    }

    /// <summary>替换过的渲染器，供保图守卫每帧检查（只守贴图，颜色交给原版）。</summary>
    private static readonly List<(string Label, SpriteRenderer Sr, Sprite Expected)> _watch = new();

    /// <summary>
    /// true = 暂停保图守卫。
    /// 由 <see cref="Light.UI.MainMenu.MainMenuButtonStyler"/> 控制：
    /// 选了「原版样式 / 亚克力样式」就把模组 PNG 让位给原版贴图，守卫必须停手。
    /// </summary>
    public static bool Suppress { get; set; }

    /// <summary>
    /// 当前设置是不是"非 MOD 样式"。
    /// 直接读设置而不是只看 Suppress 标志：MainMenuPatch 和本类的 postfix
    /// 执行顺序不确定，靠标志位可能来不及设置，就会在进主菜单时错误地贴上模组图。
    /// </summary>
    private static bool StyleIsNotMod()
    {
        try
        {
            AppearanceSettings.EnsureLoaded();
            return AppearanceSettings.ButtonStyle != MainButtonStyle.Mod;
        }
        catch { return false; }
    }

    /// <summary>
    /// 撤销动作栈：<see cref="ReplaceButton"/> 每改一样东西就压一条进来，
    /// 切回「原版样式」时按顺序全部回滚。
    ///
    /// ⚠️ 只能还原**贴图和 enabled**。图标（Icon）是被 <c>Destroy</c> 掉的，
    ///    销毁了没法复活 —— 所以真正的"完全原版"要靠 <see cref="Suppress"/>
    ///    **在替换之前就拦住**（进主菜单时按当前样式决定跑不跑）。
    /// </summary>
    private static readonly List<Action> _undo = new();

    private static void Record(Action undo)
    {
        try { _undo.Add(undo); } catch { }
    }

    /// <summary>把所有替换回滚（贴图 + enabled + 文字色）。</summary>
    public static void RestoreOriginals()
    {
        int n = 0;
        try
        {
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try { _undo[i]?.Invoke(); n++; } catch { }
            }
            _undo.Clear();
            _watch.Clear();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.RestoreOriginals]", ex);
        }
        LightLogger.Log($"[MainMenuButtonSprite] 已回滚 {n} 项改动（贴图/enabled）");
    }

    /// <summary>重新贴一遍模组图（切回 MOD 样式时用）。</summary>
    public static void ReapplyModSprites()
    {
        try
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            if (menu == null) return;
            Suppress = false;
            _watch.Clear();          // 重新登记，避免重复条目
            StartPostfix(menu);
            RightPanelPostfix(menu);
            LightLogger.Log("[MainMenuButtonSprite] 已重新贴上模组贴图");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.ReapplyModSprites]", ex);
        }
    }

    /// <summary>名字里带这些词的渲染器优先当"主图"（真正的按钮底/卡片大图）。</summary>
    private static readonly string[] BackgroundHints =
        { "background", "bg", "field", "frame", "screen", "block", "base" };

    /// <summary>
    /// 右侧面板专用：启发式挑主渲染器。
    /// 先看"名字像背景的"（取其中面积最大者），没有再退到"所有渲染器里面积最大的"。
    /// 这样 本地/在线 会挑中 Scaler/Background(5.12x5.12) 而不是 0.90x0.81 的小标签，
    /// 输入代码 会挑中 Background 而不是更大的 Checkmark。
    /// </summary>
    private static SpriteRenderer? FindMainRendererSmart(PassiveButton btn)
    {
        SpriteRenderer? bestHinted = null;
        float bestHintedArea = -1f;
        SpriteRenderer? bestAny = null;
        float bestAnyArea = -1f;

        foreach (var sr in btn.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.sprite == null || IsDecorative(sr)) continue;

            float area = SpriteArea(sr.sprite);
            if (area > bestAnyArea)
            {
                bestAnyArea = area;
                bestAny = sr;
            }

            string name = sr.gameObject.name.ToLowerInvariant();
            bool hinted = false;
            foreach (var hint in BackgroundHints)
            {
                if (name.Contains(hint)) { hinted = true; break; }
            }
            if (hinted && area > bestHintedArea)
            {
                bestHintedArea = area;
                bestHinted = sr;
            }
        }

        return bestHinted ?? bestAny;
    }

    /// <summary>这些是装饰层（辉光/图标/勾选），永远不当目标 —— 否则新图会被赋给它们、随后又被隐藏掉。</summary>
    private static readonly string[] ExcludeHints = { "shine", "glow", "icon", "checkmark", "check", "sparkle" };

    private static bool HasHint(string lowerName, string[] hints)
    {
        foreach (var h in hints)
        {
            if (lowerName.Contains(h)) return true;
        }
        return false;
    }

    private static bool IsDecorative(SpriteRenderer sr)
    {
        try { return HasHint(sr.gameObject.name.ToLowerInvariant(), ExcludeHints); }
        catch { return false; }
    }

    /// <summary>
    /// 在一个状态容器里挑"真正的按钮图"，优先级：
    ///   ① 容器自己身上的渲染器 —— 左侧主菜单按钮就是这种结构（必须优先，
    ///      否则容器里那块更大的 Shine 辉光会被当成目标，新图赋给辉光后被隐藏 = 常态仍是原版）；
    ///   ② 子物体里名字像底图的（Background / Bg / Field / Frame / Screen …）—— 右侧面板是这种结构；
    ///   ③ 子物体里面积最大的非装饰渲染器。
    /// </summary>
    private static SpriteRenderer? PickInContainer(GameObject? container)
    {
        if (container == null) return null;

        var own = container.GetComponent<SpriteRenderer>();
        if (own != null && !IsDecorative(own)) return own;

        SpriteRenderer? hinted = null;
        float hintedArea = -1f;
        SpriteRenderer? any = null;
        float anyArea = -1f;

        foreach (var sr in container.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.sprite == null || IsDecorative(sr) || sr == own) continue;

            float area = SpriteArea(sr.sprite);
            if (area > anyArea)
            {
                anyArea = area;
                any = sr;
            }
            if (HasHint(sr.gameObject.name.ToLowerInvariant(), BackgroundHints) && area > hintedArea)
            {
                hintedArea = area;
                hinted = sr;
            }
        }

        return hinted ?? any ?? own;
    }

    /// <summary>sprite 的世界尺寸面积（用于比较"谁是大图"）。</summary>
    private static float SpriteArea(Sprite? sprite)
    {
        if (sprite == null) return 0f;
        var size = sprite.bounds.size;
        return size.x * size.y;
    }

    /// <summary>
    /// 找主渲染器：优先各状态容器；都找不到就退到"贴图面积最大的那个子渲染器"
    /// （左侧按钮走这条老逻辑）。
    /// </summary>
    private static SpriteRenderer? FindMainRenderer(PassiveButton btn, GameObject?[] containers)
    {
        foreach (var c in containers)
        {
            var sr = PickInContainer(c);
            if (sr != null) return sr;
        }

        SpriteRenderer? best = null;
        float bestArea = -1f;
        foreach (var sr in btn.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.sprite == null || IsDecorative(sr)) continue;
            float area = SpriteArea(sr.sprite);
            if (area > bestArea)
            {
                bestArea = area;
                best = sr;
            }
        }
        return best;
    }

    /// <summary>相对按钮根节点的层级路径，便于看日志定位。</summary>
    private static string RelPath(Transform root, Component c)
    {
        try
        {
            var sb = new System.Text.StringBuilder(c.gameObject.name);
            var t = c.transform.parent;
            int guard = 0;
            while (t != null && t != root && guard++ < 8)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }
        catch { return "(?)"; }
    }

    /// <summary>检测非透明像素的最小/最大包围盒。</summary>
    private static bool FindOpaqueBounds(Texture2D tex, byte alphaThreshold, out int minX, out int minY, out int w, out int h)
    {
        minX = 0;
        minY = 0;
        w = tex.width;
        h = tex.height;

        var pixels = tex.GetPixels32();
        int width = tex.width;
        int height = tex.height;
        int xMin = width, yMin = height, xMax = -1, yMax = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[row + x].a > alphaThreshold)
                {
                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }
        }

        if (xMax < 0) return false;
        minX = xMin;
        minY = yMin;
        w = xMax - xMin + 1;
        h = yMax - yMin + 1;
        return true;
    }

    /// <summary>按新贴图的世界包围盒修正点击碰撞盒，避免透明区误触或热区错位。</summary>
    private static void FixCollider(PassiveButton btn, SpriteRenderer activeSr)
    {
        try
        {
            var col = btn.activeSprites != null ? btn.activeSprites.GetComponent<BoxCollider2D>() : null;
            if (col == null) return;

            var bounds = activeSr.bounds;
            var scale = col.transform.lossyScale;
            col.size = new Vector2(
                bounds.size.x / Mathf.Abs(scale.x),
                bounds.size.y / Mathf.Abs(scale.y));

            var centerLocal = col.transform.InverseTransformPoint(bounds.center);
            col.offset = new Vector2(centerLocal.x, centerLocal.y);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.FixCollider]", ex);
        }
    }

    /// <summary>隐藏辉光方块(Shine)，删除所有 sprite 容器下的左侧图标(Icon)，保留按钮文字。</summary>
    private static void HideDecorations(PassiveButton btn)
    {
        try
        {
            var containers = new[]
            {
                btn.activeSprites, btn.inactiveSprites, btn.disabledSprites,
                btn.selectedSprites, btn.selectedInactiveSprites,
            };
            foreach (var container in containers)
            {
                if (container == null) continue;
                HideChild(container, "Shine");
                var icon = container.transform.FindChild("Icon");
                // ⚠️⚠️ **绝对不能 Destroy！**
                //
                //  实测堆栈（LightLog.log 的 [异常堆栈]）：
                //    UnityEngine.SpriteRenderer.get_size ()
                //    AspectScaledAsset.<ScaleObject>b__0 (AspectScaledAsset+ScaledSprite s)
                //    AspectScaledAsset.ScaleObject (Single aspectDiff)
                //    SlicedAspectScaler.Update ()        ← 原版每帧跑
                //
                //  原版 SlicedAspectScaler.Update() **每帧**遍历 AspectScaledAsset 的
                //  ScaledSprite 列表并对每项取 .size。Destroy 掉 Icon 之后列表里那个
                //  SpriteRenderer 就成了"已销毁"引用 → **每帧一条 NRE**（就是那个刷屏）。
                //
                //  正确做法：**不销毁，只让它看不见**（见 SuppressRenderer）。
                if (icon != null) SuppressRenderer(icon.gameObject);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.HideDecorations]", ex);
        }
    }

    /// <summary>与主界面按钮文字同款的"辉光白"（颜色统一取自 MenuTextTemplate，避免两处各写一份）。</summary>
    private static readonly Color GlowWhite = MenuTextTemplate.GlowWhite;

    /// <summary>按钮文字设为辉光白，与主界面文字保持一致。</summary>
    private static void SetButtonTextColor(PassiveButton btn)
    {
        try
        {
            // 记下原来的文字色，切回原版样式时要还原
            var oldActive = btn.activeTextColor;
            var oldInactive = btn.inactiveTextColor;
            var oldSelected = btn.selectedTextColor;
            var oldDisabled = btn.disabledTextColor;
            Record(() =>
            {
                if (btn == null) return;
                btn.activeTextColor = oldActive;
                btn.inactiveTextColor = oldInactive;
                btn.selectedTextColor = oldSelected;
                btn.disabledTextColor = oldDisabled;
            });

            btn.activeTextColor = GlowWhite;
            btn.inactiveTextColor = GlowWhite;
            btn.selectedTextColor = GlowWhite;
            btn.disabledTextColor = GlowWhite;
            if (btn.buttonText != null)
            {
                btn.buttonText.gameObject.SetActive(true);
                btn.buttonText.color = GlowWhite;
            }

            // 右侧面板等按钮的文字不一定挂在 buttonText 上，直接把子物体里的文字一并刷成同一个白
            foreach (var tmp in btn.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                tmp.color = GlowWhite;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonSprite.SetButtonTextColor]", ex);
        }
    }

    /// <summary>
    /// 让一个 GameObject 上**所有**渲染器"看不见"，但**不销毁它**。
    ///
    /// ⚠️⚠️ 这是为了绕开原版 <c>SlicedAspectScaler</c> 的坑：
    ///    它每帧遍历 <c>AspectScaledAsset</c> 的 ScaledSprite 列表取 <c>.size</c>，
    ///    Destroy 掉任何一个被登记的 SpriteRenderer 都会让那一帧抛 NRE（且每帧重复）。
    ///     实测堆栈：SpriteRenderer.get_size ← AspectScaledAsset.ScaleObject ← SlicedAspectScaler.Update
    ///
    /// 所以这里三道保险，全都是"隐藏"而不是"删除"：
    ///   ① <c>enabled = false</c> —— 最直接
    ///   ② <c>color.a = 0</c>     —— 被重新 enable 也不显示
    ///   ③ <c>localScale = 0</c>  —— 前两道都被覆盖也画不出来
    /// </summary>
    private static void SuppressRenderer(GameObject go)
    {
        try
        {
            if (go == null) return;

            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null) continue;
                sr.enabled = false;
                var c = sr.color; c.a = 0f; sr.color = c;
                sr.transform.localScale = Vector3.zero;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuButtonSprite.SuppressRenderer] {ex.Message}");
        }
    }
    private static void HideChild(GameObject parent, string childName)
    {
        if (parent == null) return;
        parent.transform.FindChild(childName)?.gameObject.SetActive(false);
    }
}
