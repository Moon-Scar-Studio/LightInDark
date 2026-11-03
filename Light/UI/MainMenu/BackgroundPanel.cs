using System;
using System.Collections.Generic;
using System.IO;
using Light.UI.HudUI;
using Light.UI.Window;
using LightInDark.Core;
using LightInDark.UI.Window;          // LayerExpansion 在这里
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using MetaScreen = Light.UI.HudUI.MetaScreen;

namespace Light.UI.MainMenu;

/// <summary>
/// 「更换背景图」面板。
///
/// 布局（按用户给的设计稿，见仓库 UI设计稿记录.md）：
/// <code>
/// ┌──────────────────────────────────────────────────────────────────────┐
/// │ 更换背景图                                                            │
/// │ ┌────────┐  ┌──────────────────────────────┐  ┌──────────────────┐  │
/// │ │素材选择 │  │ 图片预览 / 视频预览            │  │ 文件列表          │  │
/// │ │[背景图] │  │ ┌──────────────────────────┐ │  │ ┌──────────────┐ │  │
/// │ │[MOD粒子]│  │ │                          │ │  │ │ xxx.png      │ │  │
/// │ │        │  │ │        预览              │ │  │ │ yyy.mp4      │ │  │
/// │ │说明文字 │  │ └──────────────────────────┘ │  │ │ ...          │ │  │
/// │ │        │  │ 裁剪模式: [自适应][原图][拉伸] │  │ │           ▓  │ │  │
/// │ │        │  │ 调暗: [0%]  船员: [显示]      │  │ └──────────────┘ │  │
/// │ └────────┘  └──────────────────────────────┘  │ [刷新][打开文件夹]│  │
/// │──────────────────────────────────────────────────────────────────────│
/// │ 当前背景图：xxx.png    分辨率：1920×1080   大小：2.1 MB               │
/// │ SHA-256：abcdef...                                    [应用]         │
/// └──────────────────────────────────────────────────────────────────────┘
/// </code>
///
/// 样式说明（用户要求）：
///   · 按钮底色用 **FS(FinalSuspect) 主界面按钮**那套半透明深灰
///     （常态 0.13/0.92、悬浮 0.24/0.98，与 GameSettingMenuPatch.ApplyFsButtonStyle 同一组值）；
///   · 其余（窗口底、关闭按钮、列表底）一律**原版**素材，不自绘。
/// </summary>
public class BackgroundPanel
{
    // ===== 尺寸 =====
    // 高度从 6.0 加到 6.6：底部要多放一行「按钮样式 / 按钮颜色」。
    // 同时把整体缩放从 0.78 收到 0.72，视觉尺寸基本不变（7.2 × 4.75 ＜ 原来的 7.8 × 4.68）。
    private static readonly Vector2 WindowSize = new(10.0f, 6.6f);
    private const int VisibleRows = 7;
    private const float RowHeight = 0.38f;

    /// <summary>
    /// 整体缩放。窗口背景是按 <see cref="WindowSize"/> 生成的，所以直接把
    /// MetaWindow 的 localScale 调小即可 —— 内容坐标不用改，边框/遮罩一起等比缩小。
    /// </summary>
    private const float PanelScale = 0.72f;

    // ===== 配色 =====
    // 按钮的渐变/描边配色搬到了 GradientButton（辉光白 → 淡金）。
    // 这里只留面板自己的分隔线、框底色。
    private static readonly Color GlowWhite = new(1f, 0.95f, 0.85f, 1f);
    private static readonly Color BorderGlow = new(1f, 0.95f, 0.85f, 0.80f);
    private static readonly Color FsPanel = new(0.16f, 0.16f, 0.19f, 0.94f); // 列表底/预览底

    private static readonly Color TextMain = new(1f, 0.95f, 0.85f, 1f);      // 辉光白
    private static readonly Color TextDim = new(0.78f, 0.75f, 0.72f, 1f);    // 次要文字（提亮过）

    // ===== 运行时 =====
    private MetaScreen? _screen;
    private GameObject? _windowObj;
    private Transform? _listInner;
    private SpriteRenderer? _previewRenderer;
    private TextMeshPro? _previewHeader;
    private TextMeshPro? _infoCurrent;
    private TextMeshPro? _infoRes;
    private TextMeshPro? _infoSize;
    private TextMeshPro? _infoHash;
    private TextMeshPro? _emptyHint;
    private TextMeshPro? _previewHint;
    private SpriteRenderer? _scrollThumb;

    private readonly List<BackgroundEntry> _entries = new();
    private readonly List<GradientButton> _rowButtons = new();

    private int _selectedRow;
    private int _scrollTop;
    private bool _built;
    private bool _showing;
    private Sprite? _previewSprite;
    private string _previewKey = "";

    /// <summary>视频预览抽帧器（选中视频行时启动，取开头 60 帧里非纯黑的一帧）。</summary>
    private readonly VideoThumbnail _thumb = new();
    /// <summary>已经抽好预览的视频路径 → 对应的 Sprite。</summary>
    private readonly Dictionary<string, Sprite> _videoPreviews = new();
    /// <summary>抽帧失败/全黑的视频路径 —— 记下来，避免每帧重试（否则会疯狂重建 VideoPlayer）。</summary>
    private readonly HashSet<string> _thumbFailed = new();

    // ---- 列表行的映射：前两行是"虚拟项"，后面才是文件 ----
    private const int RowVanilla = 0;    // 用原版背景
    private const int RowRandom = 1;     // 每次随机
    private const int VirtualRows = 2;
    private int TotalRows => _entries.Count + VirtualRows;

    private string SelectionOfRow(int row)
    {
        if (row == RowVanilla) return BackgroundStore.SelectionOff;
        if (row == RowRandom) return BackgroundStore.SelectionRandom;
        int i = row - VirtualRows;
        return i >= 0 && i < _entries.Count ? _entries[i].FileName : BackgroundStore.SelectionOff;
    }

    private BackgroundEntry? EntryOfRow(int row)
    {
        int i = row - VirtualRows;
        return i >= 0 && i < _entries.Count ? _entries[i] : null;
    }

    private GradientButton? _fitCover, _fitContain, _fitStretch;
    private GradientButton? _dimButton, _crewButton, _volumeButton;
    private GradientButton? _styleButton, _colorButton;
    private GradientButton? _applyButton;

    /// <summary>当前打开的实例（供 MainMenuPatch.LateUpdate 驱动滚轮）。</summary>
    public static BackgroundPanel? Active { get; private set; }

    /// <summary>当前是否正在显示（供 MainMenuPatch 判断要不要顺手关掉别的面板）。</summary>
    public bool IsShown => _showing && IsAlive;

    /// <summary>窗口对象是否还活着（Unity 假 null 判定）。主菜单重建后据此新建面板。</summary>
    public bool IsAlive
    {
        get
        {
            try { return _windowObj != null && _windowObj; }
            catch { return false; }
        }
    }

    // =====================================================================
    //  显示 / 隐藏
    // =====================================================================

    public void Show()
    {
        try
        {
            // ⚠️ 幂等保护：原版按钮的 OnClick 一次点击可能被触发多次
            //    （PassiveButtonManager 对 TouchStart 走 ReceiveClickDown、对 Released 走
            //     ReceiveClickUp，而 ReceiveClickUp 内部又会调 ReceiveClickDown）。
            //    实测日志里出现过连续多次「已打开」。重复 Show 会让遮罩登记与界面状态错乱。
            if (_showing && IsAlive) return;

            Build();
            if (_windowObj == null) return;

            LoadDraft();               // 先读设置到草稿（Rescan 要用草稿定位选中行）
            Rescan();
            _windowObj.SetActive(true);
            _showing = true;
            Active = this;

            // 让右侧面板滑走：它会从弹窗旁边露出一块框（用户反馈"应用完后 RightPanel 会留下来一个框"）
            Patches.MainMenuPatch.SetRightPanelVisible(false);

            // ⚠️ 登记模态遮罩：窗口一显示，原版 UI 的点击就被拦住（见 UiModalGuard）
            UiModalGuard.Push(_windowObj.transform);

            RefreshAll();
            LightLogger.Log($"[BackgroundPanel] 已打开（{UiModalGuard.Describe()}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.Show]", ex);
        }
    }

    public void Hide()
    {
        try
        {
            // ⚠️ 用户要求"不点应用直接退出就啥也不改" —— 直接丢弃草稿。
            //    （草稿从头到尾就没写进设置，所以这里什么都不用回滚，只要丢掉即可）
            if (DraftDirty)
                LightLogger.Log("[BackgroundPanel] 未点应用就关闭，已丢弃本次改动");

            _showing = false;
            if (Active == this) Active = null;
            StopThumb();
            LoadDraft();               // 草稿复位，下次打开是干净的

            // 顺带把可能还开着的颜色窗口关掉（它是叠在面板上的子窗口）
            try { _colorPanel?.Hide(); } catch { }
            if (_windowObj != null)
            {
                UiModalGuard.Pop(_windowObj.transform);
                _windowObj.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.Hide]", ex);
        }
    }

    /// <summary>跨场景兜底：对象没了就清状态（防止遮罩残留把新场景按钮锁死）。</summary>
    public static void ResetIfSceneChanged()
    {
        var p = Active;
        if (p == null) return;
        try
        {
            if (p._windowObj == null || !p._windowObj) { p._showing = false; Active = null; }
        }
        catch { p._showing = false; Active = null; }
    }

    /// <summary>每帧：滚轮滚动文件列表（由 MainMenuPatch.LateUpdate 驱动）。</summary>
    public void TickInput()
    {
        if (!_showing) return;
        if (!IsAlive) { Hide(); return; }

        // 通用滑条：每帧驱动拖动（鼠标左键按下 → 拖动 → 松开）。
        try { _volumeSlider?.Tick(); } catch { }

        // 视频预览抽帧（每帧推一下，抽到就转成 Sprite）
        try
        {
            if (!_thumb.IsFinished)
            {
                _thumb.Tick();
                if (_thumb.IsFinished) CollectThumbResult();
            }
        }
        catch { }

        // 右键：支持右键的按钮（视频音量）自己轮询。
        // ⚠️ 用 Unity 的 Input + 碰撞盒命中，不走原版 PassiveButton ——
        //    原版那套只处理"触点"，分不出左右键（OnClick 左右键都会触发）。
        try
        {
            if (Input.GetMouseButtonDown(1) && !MenuDialogs.AnyOpen)
            {
                var rcam = Camera.main;
                if (rcam != null)
                {
                    var rwp = rcam.ScreenToWorldPoint(Input.mousePosition);
                    foreach (var rb in new[] { _volumeButton, _styleButton, _dimButton, _crewButton })
                    {
                        if (rb != null && rb.PollRightClick(rwp)) break;
                    }
                }
            }
        }
        catch { }

        try
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;

            // 鼠标在窗口范围内才响应
            var cam = Camera.main;
            if (cam == null || _windowObj == null) return;
            var wp = cam.ScreenToWorldPoint(Input.mousePosition);
            var lp = _windowObj.transform.InverseTransformPoint(wp);
            if (Mathf.Abs(lp.x) > WindowSize.x * 0.5f || Mathf.Abs(lp.y) > WindowSize.y * 0.5f) return;

            if (lp.x < 2.0f) return;    // 只在右侧列表区域滚
            ScrollBy(wheel > 0f ? -1 : 1);
        }
        catch { }
    }

    // =====================================================================
    //  构建界面
    // =====================================================================

    private void Build()
    {
        if (_built && IsAlive) return;

        // 先把可能存在的半成品清掉：上一次 Build 抛异常会留下一个残缺窗口，
        // 而它已经把 _windowObj 赋了值 → 下次会误判成"已经建好了"直接跳过。
        DestroyPartial();

        var parent = DestroyableSingleton<MainMenuManager>.Instance;
        if (parent == null)
        {
            LightLogger.LogWarning("[BackgroundPanel] 主菜单还没准备好，无法创建面板");
            return;
        }

        try
        {
            BuildCore(parent);
            _built = true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.Build]", ex);
            DestroyPartial();       // 半成品不要留着
            _built = false;
        }
    }

    /// <summary>销毁建了一半的窗口并清空所有引用。</summary>
    private void DestroyPartial()
    {
        try
        {
            if (_windowObj != null)
            {
                UiModalGuard.Pop(_windowObj.transform);
                UnityEngine.Object.Destroy(_windowObj);
            }
        }
        catch { }

        _windowObj = null;
        _screen = null;
        _listInner = null;
        _previewRenderer = null;
        _previewHeader = null;
        _infoCurrent = null;
        _infoRes = null;
        _infoSize = null;
        _infoHash = null;
        _emptyHint = null;
        _previewHint = null;
        _scrollThumb = null;
        _applyButton = null;
        _fitCover = _fitContain = _fitStretch = null;
        _dimButton = null;
        _crewButton = null;
        _volumeButton = null;
        _styleButton = null;
        _colorButton = null;
        _colorPanel = null;
        _rowButtons.Clear();
        _built = false;
    }

    private void BuildCore(MainMenuManager parent)
    {
        // 注意：GenerateWindow 是 MetaScreen 上的静态方法（不是 HudUI 上的）
        _screen = MetaScreen.GenerateWindow(
            WindowSize, parent.transform, new Vector3(0f, 0f, -30f),
            withBlackScreen: true,
            // ⚠️ 用户要求："点击空白处这个弹窗会直接被关闭" —— 不要这个行为。
            //    点空白处只吞掉点击（ClickGuard 仍在），必须点左上角关闭按钮才关。
            closeOnClickOutside: true,
            background: BackgroundSetting.Modern,
            withCloseButton: true,
            sortingGroupOrder: 300);

        if (_screen == null) return;
        _windowObj = _screen.transform.parent.gameObject;

        // 整体缩小（用户："整体有点大"）。窗口背景是按 WindowSize 建的，
        // 所以缩放 MetaWindow 会让内容与边框等比缩小，坐标一个字都不用改。
        _windowObj.transform.localScale = Vector3.one * PanelScale;

        // 把遮罩调淡一点，并把列表/预览底提亮，整体不要那么黑
        BrightenWindowChrome();

        RewireCloseActions();

        // 点**窗口以外**才关窗。
        // ⚠️ HudUI 自带的 ClickGuard 是铺满全屏的一整块，会把窗口**内部**的空白也吃掉
        //    → 表现为"点窗口里任何非按钮的地方都关窗"（用户报的 bug）。
        //    这里关掉那一整块，改成一圈四条边框带子，中间挖出窗口大小的洞。
        UiModalGuard.InstallOutsideClickGuard(_windowObj, WindowSize, () =>
        {
            // 弹窗开着时不要关面板（原版点击判定不看 z，弹窗外的点击会同时命中这里）
            if (MenuDialogs.AnyOpen) return;
            Hide();
        });

        var root = _screen.transform;

        // ---- 标题 ----
        // width 必须给：左对齐 TMP 是从矩形左边缘开始画的（见 MakeText 注释）
        MakeText(root, "Title", "更换背景图", new Vector3(-4.55f, 2.66f, -0.1f),
            Vector3.one, 1.7f, FontStyles.Bold, TextAlignmentOptions.Left, GlowWhite, width: 5.0f);

        // 右上角操作提示（原 0.8 太小，用户看不清）
        MakeText(root, "TitleHint", "选一个素材 → 点「应用」", new Vector3(0.55f, 2.66f, -0.1f),
            Vector3.one, 1.1f, FontStyles.Normal, TextAlignmentOptions.Left, TextDim, width: 4.1f);

        // ---- 分隔线（辉光白）----
        MakeLine(root, new Vector3(0f, 2.4f, -0.05f), new Vector2(9.4f, 0.024f));
        MakeLine(root, new Vector3(0f, -2.6f, -0.05f), new Vector2(9.4f, 0.024f));

        BuildLeftColumn(root);
        BuildMiddleColumn(root);
        BuildRightColumn(root);
        BuildBottomBar(root);

        LightLogger.Log("[BackgroundPanel] 界面已构建");
    }

    /// <summary>
    /// 把窗口的"底"提亮一点。
    ///
    /// <c>HudUI.GenerateWindow</c> 里 BlackScreen 的 alpha 是**写死的 0.4226**，
    /// 叠上本来就偏暗的面板底，整块界面看起来发黑（用户反馈"整体UI看着太暗了"）。
    /// 这里把遮罩调淡，给列表底/预览底换更亮的颜色。
    /// </summary>
    private void BrightenWindowChrome()
    {
        try
        {
            if (_windowObj == null) return;
            var t = _windowObj.transform;

            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null) continue;

                if (c.name == "BlackScreen")
                {
                    var sr = c.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.color = new Color(0f, 0f, 0f, 0.22f);   // 0.42 → 0.22
                }
                else if (c.name == "Inner")
                {
                    // Modern 风格的窗口底（原版九宫格图），原来被乘了 0.55 → 太暗
                    var sr = c.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.color = Color.white.RGBMultiplied(0.78f);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.BrightenWindowChrome] {ex.Message}");
        }
    }

    /// <summary>
    /// 把 HudUI 默认的关闭动作（<c>Object.Destroy(窗口)</c>）换成 <see cref="Hide"/>。
    ///
    /// ⚠️ 只改「关闭按钮」，**不动 ClickGuard**。
    ///    ClickGuard 现在只负责"吞掉点空白处的点击"，不该关窗
    ///    （用户明确说不要"点空白处直接关掉"）。
    /// </summary>
    private void RewireCloseActions()
    {
        try
        {
            if (_windowObj == null) return;
            var parent = _windowObj.transform;

            Transform? child = null;
            // 关闭按钮 + 点空白处（ClickGuard）都走 Hide()。
            // ⚠️ HudUI 默认给 ClickGuard 挂的是 Object.Destroy(窗口) —— 那会让
            //    遮罩登记和 _showing 状态不一致，所以必须换成 Hide()。
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c == null) continue;
                bool isClose = c.name == "CloseButton";
                if (!isClose && c.name != "ClickGuard") continue;

                // ① 放大那个叉（HudUI 里写死 0.57，用户反馈"太小"）
                if (isClose) c.localScale = Vector3.one * CloseButtonScale;

                var pbc = c.GetComponent<PassiveButton>();
                if (pbc == null) continue;

                // ②⚠️ 音效修复：HudUI 的 SetUpButton(playSound:true) 把音效挂成了
                //   OnClick 的一个监听（VanillaAsset.cs:456-461），我原来直接
                //   `OnClick = new ButtonClickedEvent()` 把音效一起清掉了。
                //   现在清空后自己把音效补回来。（同时干掉 HudUI 挂的 Object.Destroy）
                pbc.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                if (isClose)
                    pbc.OnClick.AddListener((UnityAction)(() => GradientButton.PlayClickSound()));
                pbc.OnClick.AddListener((UnityAction)(() => Hide()));

                // ③ 悬浮音效也补一份
                if (isClose && pbc.OnMouseOver != null)
                    pbc.OnMouseOver.AddListener((UnityAction)(() => PlayHoverSound()));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.RewireCloseActions] {ex.Message}");
        }
    }

    /// <summary>关闭按钮的缩放（HudUI 里写死 0.57，太小）。</summary>
    private const float CloseButtonScale = 0.95f;

    private static void PlayHoverSound()
    {
        try
        {
            var clip = VanillaAsset.FindSoundClip("UI_Hover");
            if (clip != null) SoundManager.Instance?.PlaySound(clip, false, 0.7f);
        }
        catch { }
    }

    private void BuildLeftColumn(Transform root)
    {
        const float x = -4.0f;

        MakeText(root, "CatLabel", "素材选择", new Vector3(x, 2.05f, -0.1f),
            Vector3.one, 1.35f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);

        // 背景图（当前唯一实现的分类）
        var cat = MakeButton(root, "CatBackground", "背景图", new Vector2(1.75f, 0.44f),
            new Vector3(x, 1.45f, -0.1f), () => { }, selected: true);
        if (cat != null)
        {
            cat.Button.OnClick.RemoveAllListeners();
            cat.Button.OnClick.AddListener((UnityAction)(() => { /* 已经在背景图分类 */ }));
        }

        // MOD粒子（用户确认本次不做，占位并说明）
        var particles = MakeButton(root, "CatParticles", "MOD粒子", new Vector2(1.75f, 0.44f),
            new Vector3(x, 0.92f, -0.1f), () => { }, selected: false);
        if (particles != null)
        {
            particles.Button.OnClick.RemoveAllListeners();
            particles.Button.OnClick.AddListener((UnityAction)(() =>
                ShowToast("MOD粒子还没做，以后再加。")));
            particles.Renderer.color = new Color(0.10f, 0.10f, 0.11f, 0.75f);   // 灰掉表示不可用
            particles.Ring.color = new Color(1f, 1f, 1f, 0.28f);                // 边框也压暗
            particles.Text.color = new Color(0.55f, 0.55f, 0.57f, 1f);
        }

        // 说明文字（y 是"文字顶边"：MakeText 会按 height 换算成矩形中心）
        // ⚠️ 字号提到 0.9 之后每行能放的汉字变少（约 8 个），所以文字要短。
        var help = MakeText(root, "Help", "", new Vector3(-4.9f, 0.6f, -0.1f),
            Vector3.one, 0.9f, FontStyles.Normal, TextAlignmentOptions.TopLeft, TextDim,
            width: 1.85f, height: 2.2f);
        help.text =
            "图片/视频放进：\n" +
            "Light_Data\n" +
            "  \\MainBackGround\n" +
            "     \\Image\n" +
            "     \\Video\n\n" +
            "放好后点「刷新」\n" +
            "就会出现在列表\n\n" +
            "内置图首次运行\n" +
            "会自动释放过去";

        // 打开文件夹放左栏底部（右栏留给列表，别挤在一起）
        MakeButton(root, "OpenFolder", "打开素材文件夹", new Vector2(1.9f, 0.46f),
            new Vector3(x, -1.9f, -0.1f), OpenFolder, false);
    }

    private void BuildMiddleColumn(Transform root)
    {
        const float cx = -1.2f;

        // 预览标题：用户要求"放大、往上拉"（原来是 2.0 / 字号 1.1）
        _previewHeader = MakeText(root, "PreviewHeader", "图片预览", new Vector3(cx, 2.12f, -0.1f),
            Vector3.one, 1.5f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);

        // 预览框（原版九宫格底）
        var frame = new GameObject("PreviewFrame");
        frame.layer = LayerExpansion.GetUILayer();
        frame.transform.SetParent(root, false);
        frame.transform.localPosition = new Vector3(cx, 0.85f, 0.05f);
        var fsr = frame.AddComponent<SpriteRenderer>();
        fsr.sprite = VanillaAsset.PopUpBackSprite;
        fsr.drawMode = SpriteDrawMode.Sliced;
        fsr.size = new Vector2(4.3f, 1.95f);
        fsr.color = FsPanel;
        AddGlowBorder(root, new Vector3(cx, 0.85f, 0.05f), new Vector2(4.3f, 1.95f), BorderGlow, 0.045f);

        // 预览图本体
        var img = new GameObject("PreviewImage");
        img.layer = LayerExpansion.GetUILayer();
        img.transform.SetParent(root, false);
        img.transform.localPosition = new Vector3(cx, 0.85f, 0f);
        _previewRenderer = img.AddComponent<SpriteRenderer>();
        _previewRenderer.color = Color.white;

        // 空列表提示
        _emptyHint = MakeText(root, "EmptyHint", "", new Vector3(cx, 0.85f, -0.2f),
            Vector3.one, 1.0f, FontStyles.Italic, TextAlignmentOptions.Center, TextDim,
            width: 4.0f, height: 1.85f);
        _emptyHint.gameObject.SetActive(false);

        // 预览区提示（选中"原版背景/随机"或视频时，预览框里不能是纯黑一片）
        _previewHint = MakeText(root, "PreviewHint", "", new Vector3(cx, 0.85f, -0.2f),
            Vector3.one, 1.0f, FontStyles.Normal, TextAlignmentOptions.Center, TextDim,
            width: 4.0f, height: 1.8f);

        // ---- 裁剪模式（放在预览框**外面**，别落在框里让人以为是框的内容）----
        MakeText(root, "FitLabel", "裁剪模式", new Vector3(-2.45f, -0.42f, -0.1f),
            Vector3.one, 1.15f, FontStyles.Bold, TextAlignmentOptions.Left, TextDim, width: 2.0f);

        // 三列等宽：左边缘统一从 -2.45 起，避免"两行左边缘不对齐"
        _fitCover = MakeButton(root, "FitCover", "自适应", new Vector2(1.05f, 0.42f),
            new Vector3(-1.925f, -0.88f, -0.1f), () => SetFit(BackgroundFit.Cover), FitIs(BackgroundFit.Cover));
        _fitContain = MakeButton(root, "FitContain", "原图", new Vector2(1.05f, 0.42f),
            new Vector3(-0.825f, -0.88f, -0.1f), () => SetFit(BackgroundFit.Contain), FitIs(BackgroundFit.Contain));
        _fitStretch = MakeButton(root, "FitStretch", "拉伸", new Vector2(1.05f, 0.42f),
            new Vector3(0.275f, -0.88f, -0.1f), () => SetFit(BackgroundFit.Stretch), FitIs(BackgroundFit.Stretch));

        // 调暗 / 船员 / 视频音量
        _dimButton = MakeButton(root, "Dim", DimLabel(), new Vector2(1.6f, 0.42f),
            new Vector3(-1.65f, -1.44f, -0.1f), CycleDim, false);

        _crewButton = MakeButton(root, "Crew", CrewLabel(), new Vector2(1.6f, 0.42f),
            new Vector3(0.0f, -1.44f, -0.1f), ToggleCrew, false);

        // 视频音量行：**左标签 + 右滑条共享一行**。
        //   原来是"一整条 4.3 宽按钮、点一下 +10"，粒度太粗（用户早期就吐槽过）。
        //   ⚠️ 面板纵向很挤（音量行 -1.88、下面按钮行 -2.34，中间只有 0.46），
        //      塞不下一整行滑条 → 拆成左右两半共享这一行，**不动任何其它行的坐标**。
        _volumeButton = MakeButton(root, "VideoVolume", VolLabel(), new Vector2(1.95f, 0.4f),
            new Vector3(cx - 1.175f, -1.88f, -0.1f), CycleVolume, false, fontSize: 1.25f);
        if (_volumeButton != null) _volumeButton.OnRightClick = PromptVolumeExact;

        // 右半：连续滑条（0~100）。左标签保留点击 +10 / 右键填值的老习惯。
        _volumeSlider = Light.UI.Window.LightSlider.Create(root, "VideoVolumeSlider",
            new Vector3(cx + 1.00f, -1.88f, -0.1f), 2.30f,
            Mathf.Clamp01(_draftVideoVolume / 100f), OnVolumeSliderChanged);

        // ---- 按钮颜色 ----
        // ⚠️ 2026-11-03 用户："更换背景图中『按钮样式』删掉" ✓
        //   → 「按钮样式」按钮不再创建 ✓（主界面样式的入口统一在主菜单 LightScreen ✓）
        //   ⚠️ 2026-11-03：亚克力样式已**完全停用** ✓ → 「按钮颜色」也一并删除 ✓
        _colorButton = null;
    }

    private string BtnStyleLabel() => _draftStyle switch
    {
        MainButtonStyle.Vanilla => "按钮样式 原版",
        MainButtonStyle.Acrylic => "按钮样式 亚克力",
        _ => "按钮样式 MOD",
    };

    private void CycleButtonStyle()
    {
        // ⚠️ 只改草稿 —— 真正切换（含重载场景询问）在「应用」里做
        _draftStyle = (MainButtonStyle)(((int)_draftStyle + 1) % 3);
        RefreshControls();
    }

    private void OpenColorPanel()
    {
        try
        {
            _colorPanel ??= new ButtonColorPanel();
            _colorPanel.Show();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.OpenColorPanel]", ex);
        }
    }

    private ButtonColorPanel? _colorPanel;

    private string VolLabel() => $"视频音量 {Mathf.Clamp(_draftVideoVolume, 0, 100)}%   （右键输入具体值）";

    /// <summary>左键：音量 +10（100 → 0 循环）。</summary>
    /// <summary>滑条拖动回调：0~1 落成 0~100，并同步左标签文字。</summary>
    private void OnVolumeSliderChanged(float v)
    {
        try
        {
            _draftVideoVolume = Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 100);
            RefreshControls();      // 里面会重写 _volumeButton 的文字
            RefreshPreview();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.OnVolumeSliderChanged] {ex.Message}");
        }
    }

    /// <summary>把草稿音量同步到滑条（点标签 +10 / 右键填值 / 载入草稿后调用）。
    /// SetValue(notify:false) 避免回调里再写一次草稿值造成回环。</summary>
    private void SyncVolumeSlider()
    {
        try { _volumeSlider?.SetValue(Mathf.Clamp01(_draftVideoVolume / 100f), notify: false); }
        catch (Exception ex) { LightLogger.LogWarning($"[BackgroundPanel.SyncVolumeSlider] {ex.Message}"); }
    }

    private void CycleVolume()
    {
        _draftVideoVolume = _draftVideoVolume >= 100 ? 0 : _draftVideoVolume + 10;
        SyncVolumeSlider();
        RefreshControls();
        RefreshPreview();
    }

    /// <summary>右键：弹输入框，直接填 0~100 的具体值。</summary>
    private void PromptVolumeExact()
    {
        MenuDialogs.ShowInput(
            "视频音量（0 ~ 100）",
            Mathf.Clamp(_draftVideoVolume, 0, 100).ToString(),
            text =>
            {
                if (!int.TryParse(text.Trim(), out int v))
                {
                    MenuDialogs.ShowMessage($"「{text}」不是数字", red: true);
                    return;
                }
                _draftVideoVolume = Mathf.Clamp(v, 0, 100);
                RefreshControls();
                RefreshPreview();
            });
    }

    private void BuildRightColumn(Transform root)
    {
        const float cx = 3.35f;

        MakeText(root, "ListLabel", "文件列表", new Vector3(cx, 2.05f, -0.1f),
            Vector3.one, 1.35f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);

        // 列表底（往下让开标题：标题在 2.05，列表顶边压到 1.88 以下）
        var bg = new GameObject("ListBg");
        bg.layer = LayerExpansion.GetUILayer();
        bg.transform.SetParent(root, false);
        bg.transform.localPosition = new Vector3(cx, 0.3f, 0.05f);
        var bsr = bg.AddComponent<SpriteRenderer>();
        bsr.sprite = VanillaAsset.PopUpBackSprite;
        bsr.drawMode = SpriteDrawMode.Sliced;
        bsr.size = new Vector2(2.7f, 3.15f);
        bsr.color = FsPanel;
        AddGlowBorder(root, new Vector3(cx, 0.3f, 0.05f), new Vector2(2.7f, 3.15f), BorderGlow, 0.04f);

        // 列表容器
        var inner = new GameObject("ListInner");
        inner.layer = LayerExpansion.GetUILayer();
        inner.transform.SetParent(root, false);
        inner.transform.localPosition = new Vector3(cx, 0.3f, 0f);
        _listInner = inner.transform;

        // 滚动条轨道 + 滑块（视觉；滚动靠滚轮与 ▲▼）
        var track = new GameObject("ScrollTrack");
        track.layer = LayerExpansion.GetUILayer();
        track.transform.SetParent(root, false);
        track.transform.localPosition = new Vector3(cx + 1.42f, 0.3f, -0.02f);
        var tsr = track.AddComponent<SpriteRenderer>();
        tsr.sprite = VanillaAsset.FullScreenSprite;
        tsr.drawMode = SpriteDrawMode.Sliced;
        tsr.size = new Vector2(0.06f, 2.95f);
        tsr.color = new Color(1f, 1f, 1f, 0.10f);

        var thumb = new GameObject("ScrollThumb");
        thumb.layer = LayerExpansion.GetUILayer();
        thumb.transform.SetParent(root, false);
        thumb.transform.localPosition = new Vector3(cx + 1.42f, 0.3f, -0.03f);
        _scrollThumb = thumb.AddComponent<SpriteRenderer>();
        _scrollThumb.sprite = VanillaAsset.FullScreenSprite;
        _scrollThumb.drawMode = SpriteDrawMode.Sliced;
        _scrollThumb.size = new Vector2(0.06f, 1.0f);
        _scrollThumb.color = new Color(1f, 0.95f, 0.85f, 0.45f);

        // 行按钮（复用，不重复创建）
        for (int i = 0; i < VisibleRows; i++)
        {
            int idx = i;
            var btn = MakeButton(_listInner!, $"Row{idx}", "", new Vector2(2.4f, RowHeight),
                new Vector3(0f, 1.3f - idx * (RowHeight + 0.03f), -0.1f),
                () => OnRowClicked(idx), false);
            if (btn == null) continue;      // 创建失败就别加进来，否则后面 RefreshList 会 NRE
            btn.Text.fontSizeMax = 1.25f;
            _rowButtons.Add(btn);
        }

        // 翻页：两个箭头居中放在列表下方（视觉上归成一组）
        MakeButton(root, "ScrollUp", "▲ 上一页", new Vector2(1.15f, 0.36f),
            new Vector3(cx - 0.65f, -1.42f, -0.1f), () => ScrollBy(-VisibleRows), false);
        MakeButton(root, "ScrollDown", "▼ 下一页", new Vector2(1.15f, 0.36f),
            new Vector3(cx + 0.65f, -1.42f, -0.1f), () => ScrollBy(VisibleRows), false);

        MakeButton(root, "Refresh", "刷新列表", new Vector2(1.15f, 0.36f),
            new Vector3(cx - 0.65f, -1.86f, -0.1f), () =>
            {
                Rescan();
                RefreshAll();
                ShowToast($"已刷新，共 {_entries.Count} 个素材");
            }, false);

        MakeButton(root, "Random", "随机挑一张", new Vector2(1.15f, 0.36f),
            new Vector3(cx + 0.65f, -1.86f, -0.1f), () =>
            {
                _selectedRow = RowRandom;
                ApplySelected();
                RefreshAll();
            }, false);
    }

    private void BuildBottomBar(Transform root)
    {
        // ⚠️ 左对齐 TMP 是按矩形左边缘画的 —— 传的是"左边缘"，width 必须给
        //   （第一版没给 width，默认 100 宽 → 文字被画到面板外面，看起来"信息全丢了"）
        // 用户反馈"分辨率 / SHA-256 字样太小了" → 字号整体再加大一档。
        _infoCurrent = MakeText(root, "InfoCurrent", "当前背景图：—", new Vector3(-4.55f, -2.76f, -0.1f),
            Vector3.one, 1.15f, FontStyles.Bold, TextAlignmentOptions.Left, TextMain, width: 5.6f);

        _infoRes = MakeText(root, "InfoRes", "分辨率：—", new Vector3(-4.55f, -3.0f, -0.1f),
            Vector3.one, 1.0f, FontStyles.Normal, TextAlignmentOptions.Left, TextDim, width: 2.8f);

        _infoSize = MakeText(root, "InfoSize", "大小：—", new Vector3(-1.7f, -3.0f, -0.1f),
            Vector3.one, 1.0f, FontStyles.Normal, TextAlignmentOptions.Left, TextDim, width: 2.8f);

        _infoHash = MakeText(root, "InfoHash", "SHA-256：—", new Vector3(-4.55f, -3.19f, -0.1f),
            Vector3.one, 0.9f, FontStyles.Normal, TextAlignmentOptions.Left,
            new Color(0.80f, 0.78f, 0.75f, 1f), width: 6.5f);

        _applyButton = MakeButton(root, "Apply", "应用", new Vector2(1.5f, 0.6f),
            new Vector3(4.2f, -2.95f, -0.1f), ApplySelected, true, fontSize: 1.6f);
    }

    // =====================================================================
    //  控件工厂
    // =====================================================================

    /// <summary>
    /// 建一段文字。
    ///
    /// ⚠️ 关键坑：TMP 是**按矩形边缘**对齐的。左对齐时文字从矩形**左边缘**开始画，
    ///    所以传入的 <paramref name="pos"/>.x 是"矩形中心"，不是"文字左边缘"。
    ///    第一版没注意这点：标题用了默认 100 宽的矩形 + 左对齐，
    ///    结果文字被画到 x=-54 位置 —— **整个标题跑到面板外面，屏幕上根本看不见**。
    ///    现在传入 <paramref name="width"/> 后会自动帮你换算：
    ///    你给的是"想要的左边缘"，内部换算成矩形中心。
    /// </summary>
    private static TextMeshPro MakeText(Transform parent, string name, string text, Vector3 pos,
        Vector3 scale, float fontSize, FontStyles style, TextAlignmentOptions align, Color color,
        float width = 0f, float height = 0f)
    {
        var obj = new GameObject(name);
        obj.layer = LayerExpansion.GetUILayer();
        obj.transform.SetParent(parent, false);
        obj.transform.localScale = scale;

        var tmp = obj.AddComponent<TextMeshPro>();
        try
        {
            // 用**第二个模板**：简中字体（NotoSansSC，Nebula 同款来源）。
            // 原来的 HudUIFont 只是"抓到什么用什么"，可能是 Barlow —— 没有中文字形，
            // 中文全靠 fallback 硬撑，所以字形不统一、也容易缺字。
            var scFont = MenuTextTemplate2.Font;
            if (scFont != null)
            {
                tmp.font = scFont;
                var scMat = MenuTextTemplate2.FontMaterial;
                if (scMat != null) tmp.fontSharedMaterial = scMat;
            }
            else
            {
                // 简中字体拿不到 → 退回原来的做法，至少不会更糟
                HudUIFont.EnsureLoaded();
                if (HudUIFont.FontAsset != null)
                {
                    tmp.font = HudUIFont.FontAsset;
                    tmp.fontSharedMaterial = HudUIFont.FontMaterial;
                }
            }
        }
        catch { }

        if (width > 0f || height > 0f)
            tmp.rectTransform.sizeDelta = new Vector2(width > 0f ? width : 100f,
                                                      height > 0f ? height : 100f);

        // 左/右对齐：把"想要的边缘"换算成矩形中心
        if (width > 0f)
        {
            if (align is TextAlignmentOptions.Left or TextAlignmentOptions.TopLeft
                or TextAlignmentOptions.BottomLeft or TextAlignmentOptions.BaselineLeft
                or TextAlignmentOptions.MidlineLeft)
                pos.x += width * 0.5f;
            else if (align is TextAlignmentOptions.Right or TextAlignmentOptions.TopRight
                or TextAlignmentOptions.BottomRight or TextAlignmentOptions.BaselineRight
                or TextAlignmentOptions.MidlineRight)
                pos.x -= width * 0.5f;
        }

        // ⚠️ 同理，**上/下对齐**也要换算：矩形是以 pos 为中心的，
        //    一个 2.3 高的矩形放在 y=0.55 会向上长到 y=1.7，
        //    于是 TopLeft 的文字从 1.7 开始往下画 —— 正好压在上面的按钮上。
        //    （实测：左栏说明文字和「背景图」「MOD粒子」两个按钮重叠，就是这么来的。）
        //    现在传进来的 y 就是"想要的文字顶边"。
        if (height > 0f)
        {
            if (align is TextAlignmentOptions.TopLeft or TextAlignmentOptions.Top
                or TextAlignmentOptions.TopRight or TextAlignmentOptions.TopJustified
                or TextAlignmentOptions.TopFlush)
                pos.y -= height * 0.5f;
            else if (align is TextAlignmentOptions.BottomLeft or TextAlignmentOptions.Bottom
                or TextAlignmentOptions.BottomRight or TextAlignmentOptions.BottomJustified
                or TextAlignmentOptions.BottomFlush)
                pos.y += height * 0.5f;
        }

        obj.transform.localPosition = pos;

        tmp.text = text;
        tmp.alignment = align;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void MakeLine(Transform parent, Vector3 pos, Vector2 size, Color? color = null)
    {
        var obj = new GameObject("Divider");
        obj.layer = LayerExpansion.GetUILayer();
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = VanillaAsset.FullScreenSprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color ?? BorderGlow;      // 分隔线默认改成辉光白（原来是 0.12 白，太暗）
    }

    /// <summary>
    /// 在 <paramref name="center"/> 处画一圈**辉光白描边**（四条细条拼的，不用贴图）。
    ///
    /// 为什么要自绘：按钮贴图（<c>HudUIAssets.ButtonNormal</c>）的边框是**画在图里**的，
    /// 用 <c>SpriteRenderer.color</c> 上色会把填充和边框一起染，没办法只改边框。
    /// 用户要求"按钮的边框改成辉光白"，所以单独叠一圈细条上去。
    /// z 比按钮略小 → 画在按钮前面（本项目约定：同一容器内 z 越小越靠前）。
    /// </summary>
    private static void AddGlowBorder(Transform parent, Vector3 center, Vector2 size,
        Color color, float thickness = 0.035f)
    {
        try
        {
            float z = center.z - 0.02f;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;

            // 上下两条
            MakeLine(parent, new Vector3(center.x, center.y + hy, z),
                new Vector2(size.x + thickness * 2f, thickness), color);
            MakeLine(parent, new Vector3(center.x, center.y - hy, z),
                new Vector2(size.x + thickness * 2f, thickness), color);
            // 左右两条
            MakeLine(parent, new Vector3(center.x - hx, center.y, z),
                new Vector2(thickness, size.y + thickness * 2f), color);
            MakeLine(parent, new Vector3(center.x + hx, center.y, z),
                new Vector2(thickness, size.y + thickness * 2f), color);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.AddGlowBorder] {ex.Message}");
        }
    }

    /// <summary>
    /// 建按钮。现在用的是 <see cref="GradientButton"/> —— **纯代码自绘**的
    /// 辉光白→淡金渐变圆角按钮，不再用程序集里那张原版按钮图。
    ///
    /// 之前那版（<c>HudUIButton</c> + 外面再套一圈描边）用户评价"太丑"：
    /// 因为原版按钮图的形状和边框是**画在图里**的，只能整体上色，
    /// 结果底色发灰、边框发暗，外面那圈自绘描边又和它错开一两个像素，显得脏。
    /// 现在渐变、圆角、描边全在贴图里一次画好，只有一层，干净。
    /// </summary>
    private GradientButton MakeButton(Transform parent, string name, string text, Vector2 size,
        Vector3 pos, Action onClick, bool selected, float fontSize = 1.5f)
    {
        var btn = GradientButton.Create(parent, text, size, onClick, selected, fontSize);
        if (btn == null) return null!;

        btn.GameObject.name = name;
        btn.SetPosition(pos);
        btn.SetSelected(selected);

        if (selected) _selectedButton = btn;
        return btn;
    }

    private GradientButton? _selectedButton;

    // =====================================================================
    //  数据 / 刷新
    // =====================================================================

    private void Rescan()
    {
        try
        {
            BackgroundStore.EnsureExtracted();
            _entries.Clear();
            _entries.AddRange(BackgroundStore.Scan(force: true));

            // 让选中项跟着"当前生效的设置"走
            // 改成跟**草稿**走：列表刷新后索引可能变，用值来定位更稳
            var cur = _draftSelected;
            if (string.IsNullOrEmpty(cur))
            {
                _selectedRow = RowVanilla;
            }
            else if (cur == BackgroundStore.SelectionRandom)
            {
                _selectedRow = RowRandom;
            }
            else
            {
                _selectedRow = RowRandom;   // 选中的文件被删了 → 退回随机（不覆盖已保存的文件名）
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (string.Equals(_entries[i].FileName, cur, StringComparison.OrdinalIgnoreCase))
                    { _selectedRow = i + VirtualRows; break; }
                }
            }
            ClampScroll();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.Rescan]", ex);
        }
    }

    private void ClampScroll()
    {
        int max = Mathf.Max(0, TotalRows - VisibleRows);
        _scrollTop = Mathf.Clamp(_scrollTop, 0, max);
    }

    private void ScrollBy(int delta)
    {
        _scrollTop += delta;
        ClampScroll();
        RefreshList();
    }

    private void RefreshAll()
    {
        RefreshList();
        RefreshPreview();
        RefreshControls();
        RefreshInfo();
    }

    private void RefreshList()
    {
        try
        {
            ClampScroll();
            int total = TotalRows;
            bool noFiles = _entries.Count == 0;

            for (int i = 0; i < _rowButtons.Count; i++)
            {
                var btn = _rowButtons[i];
                if (btn == null) continue;

                int row = _scrollTop + i;
                bool has = row >= 0 && row < total;
                btn.GameObject.SetActive(has);
                if (!has) continue;

                bool isSel = row == _selectedRow;
                string label;
                Color textColor;

                if (row == RowVanilla)
                {
                    label = "○ 原版背景";
                    textColor = TextMain;
                }
                else if (row == RowRandom)
                {
                    label = "◇ 随机";
                    textColor = new Color(0.95f, 0.85f, 0.55f, 1f);
                }
                else
                {
                    var e = EntryOfRow(row)!;
                    label = (e.IsVideo ? "▶ " : "■ ") + Shorten(e.FileName, 21);
                    textColor = e.IsVideo ? new Color(0.75f, 0.85f, 1f, 1f) : TextMain;
                }

                btn.SetText(label);
                // 底色交给 GradientButton（选中/悬浮它自己会调亮），这里只管文字色
                btn.SetSelected(isSel);
                if (!isSel) btn.Text.color = textColor;
            }

            // 没有素材时给个提示（但"原版背景/随机"两行始终在）
            if (_emptyHint != null)
            {
                _emptyHint.gameObject.SetActive(noFiles);
                if (noFiles)
                {
                    _emptyHint.text =
                        "还没有自己的素材。\n\n" +
                        "把图片/视频放进：\n" +
                        $"{BackgroundStore.ImageDir}\n\n" +
                        "或点下方「打开文件夹」。";
                }
            }

            // 滚动条滑块
            if (_scrollThumb != null)
            {
                if (total <= VisibleRows)
                {
                    _scrollThumb.gameObject.SetActive(false);
                }
                else
                {
                    _scrollThumb.gameObject.SetActive(true);
                    float ratio = (float)VisibleRows / total;
                    float h = Mathf.Max(0.25f, 3.1f * ratio);
                    _scrollThumb.size = new Vector2(0.07f, h);
                    float maxScroll = total - VisibleRows;
                    float t = maxScroll <= 0 ? 0f : (float)_scrollTop / maxScroll;
                    float y = 0.5f + (1.55f - h * 0.5f) - t * (3.1f - h);
                    _scrollThumb.transform.localPosition = new Vector3(3.35f + 1.46f, y, -0.03f);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.RefreshList]", ex);
        }
    }

    private void OnRowClicked(int rowIndex)
    {
        int row = _scrollTop + rowIndex;
        if (row < 0 || row >= TotalRows) return;

        if (_selectedRow == row)
        {
            // 再点一次 = 直接应用（省一步）
            ApplySelected();
            return;
        }

        // ⚠️⚠️ 关键：必须把选择写进**草稿**。
        //   之前这里只改 _selectedRow（纯 UI 索引），而 CommitDraft() 提交的是
        //   _draftSelected —— 于是点任何一行再按「应用」都等于没选，
        //   背景永远停在旧素材上（用户看到的就是"视频背景替换失效"）。
        _draftSelected = SelectionOfRow(row);
        _selectedRow = row;
        RefreshList();
        RefreshPreview();
        RefreshInfo();
        RefreshControls();      // 「应用」按钮要跟着从"已应用"变回"应用"
    }

    private void RefreshPreview()
    {
        try
        {
            if (_previewRenderer == null) return;

            var e = EntryOfRow(_selectedRow);

            // 视频：优先显示抽好的预览帧（开头 60 帧里非纯黑的那一张）
            if (e != null && e.IsVideo)
            {
                if (_videoPreviews.TryGetValue(e.FullPath, out var cachedSpr) && cachedSpr != null)
                {
                    _previewRenderer.sprite = cachedSpr;
                    FitPreview(cachedSpr);
                    if (_previewHint != null) _previewHint.gameObject.SetActive(false);
                    if (_previewHeader != null) _previewHeader.text = "视频预览";
                    return;
                }

                EnsureThumbRunning(e);
            }
            else
            {
                StopThumb();
            }

            // 虚拟行 / 视频还没抽到帧：没有静态图可显示 → 用文字说明，别留一片纯黑
            if (e == null || e.IsVideo)
            {
                _previewRenderer.sprite = null;

                if (_previewHint != null)
                {
                    _previewHint.gameObject.SetActive(true);
                    if (_selectedRow == RowRandom)
                    {
                        _previewHeader!.text = "随机模式";
                        _previewHint.text =
                            "每次进入主菜单，\n从右侧列表里随机挑一张。\n\n" +
                            "想固定某一张，\n直接点列表里的文件名。";
                    }
                    else if (_selectedRow == RowVanilla)
                    {
                        _previewHeader!.text = "原版背景";
                        _previewHint.text =
                            "不使用自定义背景，\n完全保留游戏原本的主界面。";
                    }
                    else
                    {
                        _previewHeader!.text = "视频预览";

                        bool grabbing = _thumb.Path == e!.FullPath && !_thumb.IsFinished;
                        bool failed = _thumbFailed.Contains(e.FullPath);
                        string head = grabbing ? "正在从视频开头取一帧画面…\n（跳过纯黑的部分）"
                                    : failed ? "没能取到预览画面\n（开头 60 帧都是纯黑，或编码不支持）"
                                    : "暂无预览画面";

                        int tracks = BackgroundRenderer.VideoAudioTrackCount;
                        string audioLine = tracks <= 0
                            ? "音频：未检测到（该文件可能没有音轨）"
                            : $"音频：{tracks} 条音轨，音量 {Mathf.RoundToInt(BackgroundRenderer.VideoVolume * 100f)}%";

                        _previewHint.text =
                            $"{e.FileName}\n\n{head}\n\n" +
                            audioLine + "\n" +
                            $"视频音量：{BackgroundStore.VideoVolumeLabel}";
                    }
                }
                return;
            }

            if (_previewHint != null) _previewHint.gameObject.SetActive(false);
            if (_previewHeader != null)
                _previewHeader.text = "图片预览";

            string key = e.FullPath + "|" + File.GetLastWriteTimeUtc(e.FullPath).Ticks;
            if (_previewSprite != null && _previewKey == key)
            {
                _previewRenderer.sprite = _previewSprite;
                FitPreview(_previewSprite);
                return;
            }

            var bytes = File.ReadAllBytes(e.FullPath);
            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                _previewRenderer.sprite = null;
                return;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            spr.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _previewSprite = spr;
            _previewKey = key;
            _previewRenderer.sprite = spr;
            FitPreview(spr);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.RefreshPreview] {ex.Message}");
            if (_previewRenderer != null) _previewRenderer.sprite = null;
        }
    }

    /// <summary>把预览图缩放到预览框内（等比，留一点边距）。</summary>
    private void FitPreview(Sprite spr)
    {
        if (_previewRenderer == null || spr == null) return;
        const float boxW = 4.1f, boxH = 2.5f;
        float w = spr.texture.width / 100f;
        float h = spr.texture.height / 100f;
        float scale = Mathf.Min(boxW / Mathf.Max(0.01f, w), boxH / Mathf.Max(0.01f, h));
        _previewRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void RefreshControls()
    {
        SetSelectedState(_fitCover, FitIs(BackgroundFit.Cover));
        SetSelectedState(_fitContain, FitIs(BackgroundFit.Contain));
        SetSelectedState(_fitStretch, FitIs(BackgroundFit.Stretch));

        if (_dimButton != null) _dimButton.Text.text = DimLabel();
        if (_crewButton != null) _crewButton.Text.text = CrewLabel();
        if (_volumeButton != null) _volumeButton.Text.text = VolLabel();
        SyncVolumeSlider();   // ⚠️ 任何改了 _draftVideoVolume 的路径（载入草稿/点标签+10/右键填值）都要让滑条跟上；SetValue(notify:false) 不会回环
        if (_styleButton != null) _styleButton.Text.text = BtnStyleLabel();

        // 「按钮颜色」只在（草稿的）亚克力样式下出现
        if (_colorButton != null)
            _colorButton.SetActive(_draftStyle == MainButtonStyle.Acrylic);

        if (_applyButton != null)
        {
            // 草稿和已保存设置一致 → 显示"已应用"（且压暗）
            bool applied = !DraftDirty;
            _applyButton.Text.text = applied ? "已应用" : "应用";
            _applyButton.SetSelected(!applied);
        }
    }

    private void RefreshInfo()
    {
        string name, res = "—", size = "—", hash = "—";
        var e = EntryOfRow(_selectedRow);

        if (_selectedRow == RowVanilla)
        {
            name = "原版背景";
            res = "—";
            size = "—";
            hash = "—";
        }
        else if (_selectedRow == RowRandom)
        {
            name = "随机（每次进入主菜单随机挑一张）";
            res = "—";
            size = "—";
            hash = "—";
        }
        else if (e != null)
        {
            name = e.FileName;
            size = BackgroundStore.HumanSize(e.SizeBytes);

            if (e.IsVideo)
            {
                res = "视频（分辨率要等播放器准备好）";
                hash = "视频不计算 SHA-256";
            }
            else
            {
                try
                {
                    // 只为了拿分辨率：LoadImage 必须整图解码。图片一般不大，可接受。
                    var bytes = File.ReadAllBytes(e.FullPath);
                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (ImageConversion.LoadImage(tex, bytes, false))
                        res = $"{tex.width} × {tex.height}";
                    UnityEngine.Object.Destroy(tex);
                }
                catch { res = "读取失败"; }
                hash = BackgroundStore.Sha256(e.FullPath);
            }
        }
        else
        {
            name = "—";
        }

        if (_infoCurrent != null) _infoCurrent.text = $"当前背景图：{name}";
        if (_infoRes != null) _infoRes.text = $"分辨率：{res}";
        if (_infoSize != null) _infoSize.text = $"大小：{size}";
        if (_infoHash != null)
        {
            string shown = hash.Length > 48 ? hash.Substring(0, 48) + "…" : hash;
            _infoHash.text = $"SHA-256：{shown}";
        }
    }

    // =====================================================================
    //  动作
    // =====================================================================

    // =====================================================================
    //  草稿状态
    //  ⚠️ 用户要求"不点应用直接退出就啥也不改"。
    //     所以这里所有控件改的都是**草稿**，只有按「应用」才写进设置并作用到游戏。
    //     离开面板（关闭按钮 / 点窗口外）＝丢弃草稿。
    // =====================================================================

    private string _draftSelected = "";
    private BackgroundFit _draftFit = BackgroundFit.Cover;
    private int _draftDim;
    private bool _draftHideCrew;
    private int _draftVideoVolume;

    /// <summary>视频音量滑条（0~100 连续）。由 TickInput 每帧驱动拖动。</summary>
    private Light.UI.Window.LightSlider? _volumeSlider;
    private MainButtonStyle _draftStyle = MainButtonStyle.Mod;

    /// <summary>
    /// 用户在草稿里**重新点了「随机」那一行** → 提交时要真的重roll 一张。
    /// 否则原地刷新会沿用当前这张（那是我们为了避免"改船员把背景也换了"故意做的）。
    /// </summary>
    private bool _draftReroll;

    /// <summary>把当前设置拷进草稿（面板打开 / 应用完 / 取消后调用）。</summary>
    private void LoadDraft()
    {
        _draftSelected = BackgroundStore.Selected;
        _draftFit = BackgroundStore.Fit;
        _draftDim = BackgroundStore.DimIndex;
        _draftHideCrew = BackgroundStore.HideCrewmates;
        _draftVideoVolume = BackgroundStore.VideoVolumePercent;
        _draftStyle = BackgroundStore.ButtonStyle;
    }

    /// <summary>草稿里有没有和已保存设置不一样的地方。</summary>
    private bool DraftDirty
        => _draftSelected != BackgroundStore.Selected
        || _draftFit != BackgroundStore.Fit
        || _draftDim != BackgroundStore.DimIndex
        || _draftHideCrew != BackgroundStore.HideCrewmates
        || _draftVideoVolume != BackgroundStore.VideoVolumePercent
        || _draftStyle != BackgroundStore.ButtonStyle;

    private bool FitIs(BackgroundFit f) => _draftFit == f;

    private void SetFit(BackgroundFit f)
    {
        _draftFit = f;
        RefreshControls();
    }

    private string DimLabel() => $"调暗 {Mathf.RoundToInt(BackgroundStore.DimLevels[
        Mathf.Clamp(_draftDim, 0, BackgroundStore.DimLevels.Length - 1)] * 100f)}%";

    private void CycleDim()
    {
        _draftDim = (_draftDim + 1) % BackgroundStore.DimLevels.Length;
        RefreshControls();
    }

    private string CrewLabel() => _draftHideCrew ? "船员：隐藏" : "船员：显示";

    private void ToggleCrew()
    {
        _draftHideCrew = !_draftHideCrew;
        RefreshControls();
    }

    /// <summary>草稿是否和"已经生效的"一致（用来决定「应用」按钮显示"应用"还是"已应用"）。</summary>
    private bool IsSelectedApplied() => !DraftDirty;

    /// <summary>
    /// 「应用」：把整份草稿提交出去。
    ///
    /// 用户要求：**改了按钮样式**时不直接应用，而是弹一个红字选择框问要不要重载场景
    /// （因为 HideDecorations 会 Destroy 图标，运行时回不去，必须重载 MainMenu 才干净）；
    /// 点「否」就不改按钮样式。
    /// </summary>
    private void ApplySelected()
    {
        try
        {
            bool styleChanged = _draftStyle != BackgroundStore.ButtonStyle;

            if (styleChanged)
            {
                MenuDialogs.ShowConfirm(
                    "换了按钮样式，需要重载场景才能完全显示。\n是否现在重载？\n\n" +
                    "（点「否」不会更改按钮样式）",
                    onYes: () => { CommitDraft(applyStyle: true); ReloadMainMenuScene(); },
                    onNo: () =>
                    {
                        _draftStyle = BackgroundStore.ButtonStyle;   // 放弃样式改动
                        CommitDraft(applyStyle: false);
                        MenuDialogs.ShowMessage("已应用（按钮样式保持不变）");
                    },
                    red: true);
                return;
            }

            CommitDraft(applyStyle: false);

            var fail = BackgroundRenderer.LastFailReason;
            if (!string.IsNullOrEmpty(fail))
                MenuDialogs.ShowMessage(fail, red: true);
            else
                MenuDialogs.ShowMessage("已应用");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.ApplySelected]", ex);
        }
    }

    /// <summary>把草稿写进设置并作用到游戏。</summary>
    private void CommitDraft(bool applyStyle)
    {
        try
        {
            // ⚠️ 先记下"改之前"的值，用来决定**最小更新范围**。
            //    以前这里无条件 Reapply()（= Teardown + Build），
            //    结果改个音量都会把 VideoPlayer 销毁重建 → **视频从头播**。
            //    现在：能不动就不动。
            string oldSel = BackgroundStore.Selected;
            bool oldCrew = BackgroundStore.HideCrewmates;
            int oldVol = BackgroundStore.VideoVolumePercent;
            var oldFit = BackgroundStore.Fit;
            int oldDim = BackgroundStore.DimIndex;

            BackgroundStore.Selected = _draftSelected;
            BackgroundStore.Fit = _draftFit;
            BackgroundStore.DimIndex = _draftDim;
            BackgroundStore.HideCrewmates = _draftHideCrew;
            BackgroundStore.VideoVolumePercent = _draftVideoVolume;

            if (applyStyle)
            {
                BackgroundStore.ButtonStyle = _draftStyle;
                MainMenuButtonStyler.Apply(force: true);
            }

            bool mediaChanged = oldSel != _draftSelected;
            bool crewChanged = oldCrew != _draftHideCrew;
            bool layoutChanged = oldFit != _draftFit || oldDim != _draftDim;
            bool volChanged = oldVol != _draftVideoVolume;
            bool reroll = _draftReroll;
            _draftReroll = false;

            if (mediaChanged || crewChanged || reroll)
            {
                // ⚠️ 这里**不一定真的重建** —— Reapply 内部会判断：
                //    同一个素材 → 原地刷新（视频不中断）；换了素材 → 才拆建。
                BackgroundRenderer.Reapply(rerollRandom: reroll);
            }
            else
            {
                // 裁剪/调暗：只要重排网格，不用重建 → 视频不会断
                if (layoutChanged) BackgroundRenderer.ApplyLayoutOnly();
                // 音量：只喂新音量给正在播的视频 → 视频不会断
                if (volChanged) BackgroundRenderer.ApplyAudioOnly();
            }

            LoadDraft();          // 提交后草稿 = 已保存状态
            RefreshControls();

            LightLogger.Log($"[BackgroundPanel] 已应用（背景={_draftSelected} 裁剪={_draftFit} " +
                            $"调暗={_draftDim} 船员隐藏={_draftHideCrew} 音量={_draftVideoVolume} 样式={_draftStyle}）" +
                            $"｜需重应用={(mediaChanged || crewChanged || reroll)} 重排={layoutChanged} 音量={volChanged} 重roll={reroll}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.CommitDraft]", ex);
        }
    }

    /// <summary>重载 MainMenu 场景（按钮样式的贴图/图标只能靠重建场景才干净）。</summary>
    private static void ReloadMainMenuScene()
    {
        try
        {
            LightLogger.Log("[BackgroundPanel] 正在重载 MainMenu 场景…");
            UiModalGuard.Clear();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BackgroundPanel.ReloadMainMenuScene]", ex);
            MenuDialogs.ShowMessage($"重载失败：{ex.Message}", red: true);
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(BackgroundStore.ImageDir);
            Directory.CreateDirectory(BackgroundStore.VideoDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = BackgroundStore.RootDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.OpenFolder] {ex.Message}");
            ShowToast($"打开失败，路径：{BackgroundStore.RootDir}");
        }
    }

    private void ShowToast(string msg)
    {
        try { LightLogger.Log("[BackgroundPanel] " + msg); } catch { }
        if (_infoCurrent != null) _infoCurrent.text = msg;
    }

    /// <summary>确保正在为这个视频抽预览帧（已经抽过就直接用缓存）。</summary>
    private void EnsureThumbRunning(BackgroundEntry e)
    {
        try
        {
            if (_videoPreviews.ContainsKey(e.FullPath)) return;      // 已有缓存
            if (_thumbFailed.Contains(e.FullPath)) return;            // 之前失败过，别重试
            if (_thumb.Path == e.FullPath && !_thumb.IsFinished) return;  // 正在抽

            _thumb.Start(e.FullPath);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.EnsureThumbRunning] {ex.Message}");
        }
    }

    private void StopThumb()
    {
        try { if (!string.IsNullOrEmpty(_thumb.Path)) _thumb.Stop(); } catch { }
    }

    /// <summary>抽帧完成后把它转成 Sprite 并刷新预览。</summary>
    private void CollectThumbResult()
    {
        try
        {
            if (!_thumb.IsFinished) return;
            var tex = _thumb.Result;
            string path = _thumb.Path;
            if (tex == null || string.IsNullOrEmpty(path))
            {
                // 没抽到（全黑 / 解码失败）→ 记下来别每帧重试
                if (!string.IsNullOrEmpty(path)) _thumbFailed.Add(path);
                _thumb.Stop();
                // 让提示文字从"正在取帧…"变成"没取到"
                var cur0 = EntryOfRow(_selectedRow);
                if (cur0 != null && cur0.FullPath == path) RefreshPreview();
                return;
            }
            if (_videoPreviews.ContainsKey(path)) { _thumb.StopKeepResult(); return; }

            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            spr.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _videoPreviews[path] = spr;
            _thumb.StopKeepResult();     // 贴图要留着，别被清掉

            // 如果当前正选中这个视频，马上把预览换上去
            var cur = EntryOfRow(_selectedRow);
            if (cur != null && cur.FullPath == path) RefreshPreview();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[BackgroundPanel.CollectThumbResult] {ex.Message}");
            try { _thumb.Stop(); } catch { }
        }
    }

    private void SetSelectedState(GradientButton? btn, bool selected)
    {
        if (btn == null) return;
        try
        {
            // 交给 GradientButton 自己处理（它会同时管底色和文字色）
            btn.SetSelected(selected);
            if (selected) _selectedButton = btn;
        }
        catch { }
    }

    private static string Shorten(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max - 2) + "..";
}
