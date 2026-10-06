using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.UI.Window;
using Light.UI.HudUI;
using Light.UI.Window;
using TMPro;
using UnityEngine;
using Color = LightInDark.Color;
using LightGameManager = LightInDark.Game.GameManager;
using MetaScreen = Light.UI.HudUI.MetaScreen;      // 与 Light.UI.Window.MetaScreen 同名，必须显式指定

namespace Light.UI.Help;

/// <summary>
/// H 键帮助菜单（2026-10-06 **用 HudUI 重构**）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【结构】
///   · **一个 <see cref="HudUIWindow"/> 窗口**（<see cref="WindowSize.Help"/> = 9×5.5）
///   · 顶部一排**页面按钮**（<see cref="HudUIButton"/>，进窗口时建一次，切页只改选中态，不重建）
///   · 内容区：点页面**原地换内容**（<c>ClearContent()</c> + 重铺），窗口不重建、不闪
///   · 底部分页栏：HudUI 窗口**没有滚动**，所以长列表（职业/搜索/设置）一律**分页**
///   · 职业详情 = **第二个 HudUI 窗口**，用 HudUI 自带的左右箭头循环切换
///
/// 【为什么不能全部用 AddText / AddButton】
///   <c>HudUIWindow</c> 的 AddText/AddButton 是**纵向流水**（内部 _currentY 一路递减），
///   排不出"一行多个按钮"的网格。所以网格用 <see cref="HudUIButton.Create"/> 手工定位，
///   并用 <c>AddMargin</c> 让流水线跳过网格占的高度 —— **两边必须对得上**，
///   不然下面的文字会叠在网格上（这也是下面那堆常量存在的意义）。
///
/// 【对外接口】保持与旧版一致（<c>HelpKeyPatch</c> / <c>MeetingCloseHelpPatch</c> / <c>ExileCloseHelpPatch</c> 都在用）：
///   <see cref="OpenedAnyHelpScreen"/>、<see cref="TryOpenHelpScreen"/>、<see cref="TryOpenMyInfo"/>、
///   <see cref="TryCloseTopWindow"/>、<see cref="TryCloseHelpScreen"/>，另加 <see cref="OpenHelpScreen"/>。
/// </summary>
public static class HelpScreen
{
    /// <summary>帮助页签（位掩码）</summary>
    [Flags]
    public enum HelpTab
    {
        Search = 1, MyInfo = 2, Roles = 4, Overview = 8, Options = 16,
        Achievements = 64, Stamps = 128,
    }

    // =====================================================================
    // 可调常量（用户看效果后要微调的话，全在这里）
    // =====================================================================

    /// <summary>
    /// 窗口尺寸（用户 2026-10-06："窗口小一点（参考 Nebula 的尺寸）整体小一点"）。
    /// Nebula 自己的 Help 菜单就是 7.8 宽 → 这里 7.8 × 4.7。
    /// </summary>
    private static readonly Vector2 HelpSize = new(7.8f, 4.7f);

    // ─────────────────────────────────────────────────────────────────────
    // 字号：套**配置项 UI 那套**（简中字体 MenuTextTemplate2 + Bold，见 ApplyConfigTextStyle），
    //       尺寸参考 Nebula 的 Help 菜单（页签 1.02×0.34、职业按钮 1.42×0.38、扁按钮 + 顶满的字）。
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>顶部页签按钮。</summary>
    private const float TabBtnW = 1.02f;
    private const float TabBtnH = 0.34f;
    private const float TabGap = 0.05f;
    private const float TabRowY = 1.82f;
    private const float TabFontSize = 1.28f;

    /// <summary>页面标题（居中）。</summary>
    private const float HeaderY = 1.48f;
    private const float HeaderFontSize = 1.70f;

    /// <summary>分类标题（内鬼/中立/船员，居中）。</summary>
    private const float SectionFontSize = 1.55f;

    /// <summary>正文。</summary>
    private const float BodyFontSize = 1.22f;

    /// <summary>内容流水从顶部让开多少（页签栏的高度 + 余量）。</summary>
    private const float ContentTopMargin = 0.62f;

    /// <summary>职业网格（**每行按内容居中**）。</summary>
    private const float RoleBtnW = 1.42f;
    private const float RoleBtnH = 0.38f;
    private const float RoleGapX = 0.09f;
    private const float RoleGapY = 0.08f;
    private const int RoleColumns = 4;
    private const int RowsPerPage = 6;               // 1 分类标题行 + 5 职业行
    private const float GridTopY = 0.98f;
    private const float RoleFontSize = 1.20f;

    /// <summary>底部翻页按钮。</summary>
    private const float PagerBtnW = 1.40f;
    private const float PagerBtnH = 0.34f;
    private const float PagerY = -1.98f;
    private const float PagerFontSize = 1.22f;

    /// <summary>纯文字页每页行数（设置/概览）与行距。</summary>
    private const int TextPageLines = 8;
    private const float LineStepY = 0.30f;
    private const float LineFontSize = 1.22f;

    /// <summary>详情窗口。</summary>
    private static readonly Vector2 DetailSize = new(6.4f, 5.0f);
    private const float DetailTitleFontSize = 1.80f;
    private const float DetailBodyFontSize = 1.20f;
    private const float PortraitSize = 0.50f;
    private const float PortraitY = 1.60f;
    private const float DetailCloseY = -2.15f;

    /// <summary>排序（HudUI 窗口默认会被 HUD 压住，建完内容后要抬到很大）。</summary>
    private const int BaseOrder = 30000;
    private const int DetailOrder = 31000;

    // =====================================================================
    // 状态
    // =====================================================================

    private static HudUIWindow? _mainWindow;
    private static HudUIWindow? _detailWindow;

    private static HelpTab _tab = HelpTab.Roles;
    private static string _searchKeyword = "";
    private static int _page;

    private static readonly List<(HelpTab Tab, HudUIButton Btn)> _tabButtons = new();
    private static readonly List<GameObject> _temp = new();      // 手工建的对象（ClearContent 管不到，得自己收）

    private static List<RoleTemplate> _detailRoles = new();
    private static int _detailIndex;

    // =====================================================================
    // 对外接口
    // =====================================================================

    /// <summary>帮助菜单是否已打开（窗口销毁后视为未打开）</summary>
    public static bool OpenedAnyHelpScreen
    {
        get
        {
            try { return _mainWindow != null && _mainWindow.Screen != null; }
            catch { return false; }
        }
    }

    /// <summary>打开帮助（对外名字，等价于 <see cref="TryOpenHelpScreen"/>）。</summary>
    public static void OpenHelpScreen() => TryOpenHelpScreen();

    /// <summary>打开帮助（回到上次看的那一页）</summary>
    public static void TryOpenHelpScreen()
    {
        try
        {
            if (OpenedAnyHelpScreen) return;
            OpenWindow(_tab);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryOpenHelpScreen", ex);
        }
    }

    /// <summary>打开帮助并定位到"我的职业"页（F1）；未分配职业时不打开</summary>
    public static void TryOpenMyInfo()
    {
        try
        {
            if (!HasLocalRole) return;
            OpenWindow(HelpTab.MyInfo);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryOpenMyInfo", ex);
        }
    }

    /// <summary>
    /// 关闭最上层窗口：有职业详情二级窗口时优先关它，返回 true；否则返回 false（由调用方关整个 H 菜单）。
    /// </summary>
    public static bool TryCloseTopWindow()
    {
        try
        {
            if (OpenedDetail)
            {
                CloseDetail();
                return true;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryCloseTopWindow", ex);
        }
        return false;
    }

    /// <summary>关闭帮助（二级窗口一并关闭）</summary>
    public static void TryCloseHelpScreen()
    {
        try
        {
            CloseDetail();
            CloseMain();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.TryCloseHelpScreen", ex);
        }
    }

    // =====================================================================
    // 窗口开关
    // =====================================================================

    private static bool OpenedDetail
    {
        get
        {
            try { return _detailWindow != null && _detailWindow.Screen != null; }
            catch { return false; }
        }
    }

    private static bool HasLocalRole
    {
        get
        {
            try { return LightGameManager.Instance?.LocalPlayer?.HasRole == true; }
            catch { return false; }
        }
    }

    /// <summary>帮助窗口父级：HUD → 主菜单 → 相机</summary>
    private static Transform? FindParent()
    {
        try
        {
            if (HudManager.Instance != null) return HudManager.Instance.transform;
            var mainMenu = GameObject.FindObjectOfType<MainMenuManager>();
            if (mainMenu != null) return mainMenu.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.FindParent", ex); return null;
        }
    }

    private static void OpenWindow(HelpTab tab)
    {
        try
        {
            CloseMain();

            _tab = IsTabValid(tab) ? tab : HelpTab.Roles;
            _page = 0;
            _searchKeyword = "";

            var parent = FindParent();
            if (parent == null)
            {
                LightLogger.LogWarning("[HelpScreen] 找不到窗口父级，帮助菜单打不开");
                return;
            }

            // blockInputBehind：窗口自己会挂 UiModalGuard + 每帧 Sweep（旧版是手工 Push/Pop）
            var win = HudUIWindow.Create("", HelpSize, parent, blockInputBehind: true, sortingGroupOrder: BaseOrder, z: -50f);
            if (win == null)
            {
                LightLogger.LogWarning("[HelpScreen] HudUI 窗口创建失败");
                return;
            }

            _mainWindow = win;
            BuildTabBar(win);
            BuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.OpenWindow", ex);
        }
    }

    private static void CloseMain()
    {
        try
        {
            _tabButtons.Clear();
            DestroyTemp();

            if (_mainWindow != null)
            {
                _mainWindow.Close();       // 顺带还原被屏蔽的点击
                _mainWindow = null;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.CloseMain] {ex.Message}");
            _mainWindow = null;
        }
    }

    /// <summary>只销毁详情窗口（**不动职业列表和索引** —— 换职业时要保留）。</summary>
    private static void DestroyDetailWindow()
    {
        try
        {
            if (_detailWindow != null)
            {
                _detailWindow.Close();
                _detailWindow = null;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.DestroyDetailWindow] {ex.Message}");
            _detailWindow = null;
        }
    }

    /// <summary>彻底关闭详情（窗口 + 列表状态）。</summary>
    private static void CloseDetail()
    {
        DestroyDetailWindow();
        _detailRoles = new List<RoleTemplate>();
        _detailIndex = 0;
    }

    // =====================================================================
    // 顶部页签栏
    // =====================================================================

    /// <summary>页签显示顺序</summary>
    private static readonly HelpTab[] TabOrder =
    {
        HelpTab.Search, HelpTab.MyInfo, HelpTab.Roles, HelpTab.Overview, HelpTab.Options,
        HelpTab.Achievements, HelpTab.Stamps,
    };

    private static bool IsTabValid(HelpTab tab) => tab != HelpTab.MyInfo || HasLocalRole;

    private static string GetTabName(HelpTab tab) => tab switch
    {
        HelpTab.Search => Language.Translate("help.tabs.search", "搜索"),
        HelpTab.MyInfo => Language.Translate("help.tabs.myInfo", "我的职业"),
        HelpTab.Roles => Language.Translate("help.tabs.roles", "职业"),
        HelpTab.Overview => Language.Translate("help.tabs.overview", "概览"),
        HelpTab.Options => Language.Translate("help.tabs.options", "设置"),
        HelpTab.Achievements => Language.Translate("help.tabs.achievements", "成就"),
        HelpTab.Stamps => Language.Translate("help.tabs.stamps", "印章"),
        _ => "?",
    };

    /// <summary>建顶部页签栏 —— 只在开窗时建一次，切页不动它。</summary>
    private static void BuildTabBar(HudUIWindow win)
    {
        try
        {
            _tabButtons.Clear();

            var tabs = TabOrder.Where(IsTabValid).ToList();
            float total = tabs.Count * TabBtnW + Math.Max(0, tabs.Count - 1) * TabGap;
            float x = -total * 0.5f + TabBtnW * 0.5f;

            foreach (var tab in tabs)
            {
                var local = tab;
                var btn = HudUIButton.Create(win.Screen.transform, GetTabName(local),
                    new Vector2(TabBtnW, TabBtnH), () => SwitchTab(local));
                btn.SetPosition(new Vector3(x, TabRowY, -1f));
                SetButtonFont(btn, TabFontSize);

                // 选中态默认那块 ButtonSelected 太亮(整块高亮)，换成和悬浮同一个观感
                try { btn.SetSelectedSprite(HudUIAssets.ButtonHover, HudUIAssets.ButtonHover); } catch { }

                _tabButtons.Add((local, btn));
                x += TabBtnW + TabGap;
            }

            RefreshTabSelection();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildTabBar", ex);
        }
    }

    private static void RefreshTabSelection()
    {
        foreach (var (tab, btn) in _tabButtons)
        {
            try { btn?.SetSelected(tab == _tab); } catch { }
        }
    }

    private static void SwitchTab(HelpTab tab)
    {
        try
        {
            if (_tab == tab) return;
            _tab = tab;
            _page = 0;
            RefreshTabSelection();
            BuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.SwitchTab", ex);
        }
    }

    // =====================================================================
    // 内容区（原地换内容）
    // =====================================================================

    private static void BuildContent()
    {
        var win = _mainWindow;
        if (win == null) return;

        try
        {
            win.ClearContent();       // 只清 AddText / AddButton 建的
            DestroyTemp();            // 再清手工建的（页签栏会被重建成同一批）

            win.AddMargin(ContentTopMargin);

            switch (_tab)
            {
                case HelpTab.Search: BuildSearchPage(win); break;
                case HelpTab.MyInfo: BuildMyInfoPage(win); break;
                case HelpTab.Roles: BuildRolesPage(win); break;
                case HelpTab.Overview: BuildOverviewPage(win); break;
                case HelpTab.Options: BuildOptionsPage(win); break;
                case HelpTab.Achievements:
                    BuildPlaceholderPage(win, "help.tabs.achievements", "成就");
                    break;
                case HelpTab.Stamps:
                    BuildPlaceholderPage(win, "help.tabs.stamps", "印章");
                    break;
            }

            // ★ 新对象默认 order=0 / 原版字体 → 每次重建都要补一遍
            //   （HudUIWindow 的注释里写了：必须在**内容建完之后**抬排序，否则黑幕 30000 会把内容盖住）
            win.AscendSorting(BaseOrder);
            ApplyConfigTextStyleAll(win);       // 字体 + Bold（配置项 UI 那套）
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildContent", ex);
        }
    }

    private static void BuildPlaceholderPage(HudUIWindow win, string key, string name)
    {
        AddFixedText(win, name, HeaderFontSize, HeaderY);
        AddFixedText(win, Language.Translate(key + ".empty", name + "内容尚未实装"), BodyFontSize, GridTopY);
    }

    // ---------------------------------------------------------------------
    // 角色页（网格 + 分页）
    // ---------------------------------------------------------------------

    /// <summary>网格里的一行：标题行 或 职业行。</summary>
    private sealed class GridRow
    {
        public string? Header;
        public Color HeaderColor = Color.White;
        public List<RoleTemplate> Roles = new();
    }

    /// <summary>按分类铺行：分类标题各占一行，职业按列数切行。</summary>
    private static List<GridRow> BuildRoleRows(List<RoleTemplate> roles)
    {
        var rows = new List<GridRow>();

        void AddCategory(RoleCategory category, string title, Color titleColor)
        {
            var list = roles.Where(r => r.RoleCategory == category).ToList();
            if (list.Count == 0) return;

            rows.Add(new GridRow { Header = title, HeaderColor = titleColor });

            for (int i = 0; i < list.Count; i += RoleColumns)
                rows.Add(new GridRow { Roles = list.GetRange(i, Math.Min(RoleColumns, list.Count - i)) });
        }

        AddCategory(RoleCategory.Impostor, Language.Translate("role.category.impostor", "内鬼"), Color.ImpostorColor);
        AddCategory(RoleCategory.Neutral, Language.Translate("role.category.neutral", "中立"), new Color(1f, 0.7f, 0f));
        AddCategory(RoleCategory.Crewmate, Language.Translate("role.category.crewmate", "船员"), Color.CrewmateColor);

        return rows;
    }

    private static void BuildRolesPage(HudUIWindow win)
    {
        var all = SortedRoles();

        var rows = BuildRoleRows(all);
        int pages = Math.Max(1, (rows.Count + RowsPerPage - 1) / RowsPerPage);
        _page = Math.Clamp(_page, 0, pages - 1);

        // 标题 + 页码合成一行，放固定位置（网格是手工定位的，不能用流水线的 AddTitle）
        AddFixedText(win,
            $"{Language.Translate("help.tabs.roles", "职业")} · {_page + 1}/{pages}",
            HeaderFontSize, HeaderY);

        float y = GridTopY;
        foreach (var row in rows.Skip(_page * RowsPerPage).Take(RowsPerPage))
        {
            if (row.Header != null)
            {
                AddFixedText(win, Colorize(row.Header, row.HeaderColor), SectionFontSize, y);
                y -= 0.30f;
                continue;
            }

            // 每行按本行按钮数**居中**
            float total = row.Roles.Count * RoleBtnW + Math.Max(0, row.Roles.Count - 1) * RoleGapX;
            float x = -total * 0.5f + RoleBtnW * 0.5f;
            foreach (var role in row.Roles)
            {
                var local = role;
                int index = all.IndexOf(local);
                var btn = CreateRawButton(win, Colorize(local.Name, local.Color),
                    new Vector2(RoleBtnW, RoleBtnH),
                    () => OpenDetail(all, index < 0 ? 0 : index));
                btn.SetPosition(new Vector3(x, y, -1f));
                x += RoleBtnW + RoleGapX;
            }
            y -= RoleBtnH + RoleGapY;
        }

        AddPager(win, pages, p => { _page = p; BuildContent(); });
    }

    // ---------------------------------------------------------------------
    // 我的职业
    // ---------------------------------------------------------------------

    private static void BuildMyInfoPage(HudUIWindow win)
    {
        AddFixedText(win, Language.Translate("help.tabs.myInfo", "我的职业"), HeaderFontSize, HeaderY);

        var role = LightGameManager.Instance?.LocalPlayer?.Role;
        if (role == null)
        {
            AddFixedText(win, Language.Translate("help.myInfo.none", "当前未分配职业"), BodyFontSize, GridTopY);
            return;
        }

        var single = new List<RoleTemplate> { role.Role };
        var btn = CreateRawButton(win, Colorize(role.Role.Name, role.Role.Color),
            new Vector2(2.30f, 0.40f), () => OpenDetail(single, 0));
        btn.SetPosition(new Vector3(0f, GridTopY, -1f));

        AddFixedText(win, Language.Translate("help.myInfo.hint", "点上面的按钮看职业详情"), BodyFontSize, GridTopY - 0.50f);
    }

    // ---------------------------------------------------------------------
    // 搜索（输入框用工程现有的 GUITextField）
    // ---------------------------------------------------------------------

    private static void BuildSearchPage(HudUIWindow win)
    {
        AddFixedText(win, Language.Translate("help.tabs.search", "搜索"), HeaderFontSize, HeaderY);

        // 输入框 + 搜索按钮：手工横排（高度照 Nebula 的扁按钮来）
        var field = GUITextField.Create(win.Screen.transform, new Vector2(3.7f, 0.28f),
            Language.Translate("help.search.inputHint", "输入关键词"),
            keyword =>                                   // 回车 = 直接搜（Destroy 是延迟到帧末的，这里销毁自己不会崩）
            {
                _searchKeyword = keyword ?? "";
                _page = 0;
                BuildContent();
            });
        field.SetText(_searchKeyword);
        field.SetPosition(new Vector3(-0.70f, GridTopY, -1f));
        TrackTemp(field.GameObject);

        var searchBtn = CreateRawButton(win, Language.Translate("help.search.search", "搜索"),
            new Vector2(1.25f, 0.28f), () =>
            {
                _searchKeyword = field.Text ?? "";
                _page = 0;
                BuildContent();
            });
        searchBtn.SetPosition(new Vector3(2.10f, GridTopY, -1f));

        // 结果列表
        var keyword = (_searchKeyword ?? "").Trim();
        if (keyword.Length == 0)
        {
            AddFixedText(win, Language.Translate("help.search.hint", "输入关键词搜索职业"), BodyFontSize, GridTopY - 0.52f);
            return;
        }

        var matched = SortedRoles()
            .Where(r => r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || r.GetDocumentText().Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
        {
            AddFixedText(win, Language.Translate("help.search.noResult", "未找到相关职业"), BodyFontSize, GridTopY - 0.52f);
            return;
        }

        // 分页（每页 4 列 × 4 行；每页条数是列数的整数倍，所以下面用全局索引取模也能算出行列）
        int perPage = RoleColumns * 4;
        int pages = Math.Max(1, (matched.Count + perPage - 1) / perPage);
        _page = Math.Clamp(_page, 0, pages - 1);

        float top = GridTopY - 0.52f;
        float gridLeft = -(RoleColumns * RoleBtnW + (RoleColumns - 1) * RoleGapX) * 0.5f + RoleBtnW * 0.5f;
        foreach (var role in matched.Skip(_page * perPage).Take(perPage))
        {
            var local = role;
            int index = matched.IndexOf(local);
            if (index < 0) index = 0;
            int slot = index % perPage;                  // 本页内的序号
            int col = slot % RoleColumns;
            int row = slot / RoleColumns;

            var btn = CreateRawButton(win, Colorize(local.Name, local.Color),
                new Vector2(RoleBtnW, RoleBtnH), () => OpenDetail(matched, index));
            btn.SetPosition(new Vector3(gridLeft + col * (RoleBtnW + RoleGapX),
                                        top - row * (RoleBtnH + RoleGapY), -1f));
        }

        AddPager(win, pages, p => { _page = p; BuildContent(); });
    }

    // ---------------------------------------------------------------------
    // 概览（分配计划，按分类分段 + 分页）
    // ---------------------------------------------------------------------

    private static void BuildOverviewPage(HudUIWindow win)
    {
        AddFixedText(win, Language.Translate("help.overview.header", "分配计划"), HeaderFontSize, HeaderY);
        AddFixedText(win, Language.Translate("help.overview.players", "当前玩家数") + ": " + PlayerCount(),
            BodyFontSize, HeaderY - 0.28f);

        // 三类各自成段（HudUI 是纵向流水，排不出三栏；分段 + 分页更稳）
        var sections = new List<(string Title, Color Color, List<RoleTemplate> Roles)>
        {
            (Language.Translate("help.category.impostor", "内鬼"), Color.ImpostorColor,
                SortedRoles().Where(r => r.RoleCategory == RoleCategory.Impostor).ToList()),
            (Language.Translate("help.category.neutral", "中立"), new Color(1f, 0.7f, 0f),
                SortedRoles().Where(r => r.RoleCategory == RoleCategory.Neutral).ToList()),
            (Language.Translate("help.category.crewmate", "船员"), Color.CrewmateColor,
                SortedRoles().Where(r => r.RoleCategory == RoleCategory.Crewmate).ToList()),
        };

        var lines = new List<(string Text, bool IsHeader, Color Color)>();
        foreach (var s in sections)
        {
            if (s.Roles.Count == 0) continue;
            lines.Add((s.Title, true, s.Color));
            foreach (var role in s.Roles) lines.Add((GetAllocationLine(role), false, s.Color));
        }

        int pages = Math.Max(1, (lines.Count + TextPageLines - 1) / TextPageLines);
        _page = Math.Clamp(_page, 0, pages - 1);

        float top = GridTopY - 0.20f;                // 概览页多一行"当前玩家数"，正文整体下移一点
        float y = top;
        foreach (var line in lines.Skip(_page * TextPageLines).Take(TextPageLines))
        {
            AddFixedText(win, line.IsHeader ? Colorize(line.Text, line.Color) : line.Text,
                line.IsHeader ? SectionFontSize : LineFontSize, y, TextAlignmentOptions.Left);
            y -= LineStepY;
        }

        AddPager(win, pages, p => { _page = p; BuildContent(); });
    }

    private static int PlayerCount()
    {
        try
        {
            if (PlayerControl.AllPlayerControls == null) return 0;
            int n = 0;
            foreach (var pc in PlayerControl.AllPlayerControls) n++;
            return n;
        }
        catch { return 0; }
    }

    // ---------------------------------------------------------------------
    // 设置（游戏规则一览 + 分页）
    // ---------------------------------------------------------------------

    private static void BuildOptionsPage(HudUIWindow win)
    {
        AddFixedText(win, Language.Translate("help.tabs.options", "设置"), HeaderFontSize, HeaderY);

        var lines = new List<string>();
        try
        {
            // CurrentGameOptions 返回 IGameOptions 接口，仅 MapId/NumImpostors 可直接访问，
            // 其余字段运行时反射读取（字段名以 AmongUs.GameOptions 为准）
            var options = GameOptionsManager.Instance.CurrentGameOptions;
            if (options != null)
            {
                void AddLine(string name, string value) => lines.Add($"{name}: {value}");

                string GetValue(string fieldName)
                {
                    try
                    {
                        var type = options.GetType();
                        var field = type.GetField(fieldName);
                        if (field != null) return field.GetValue(options)?.ToString() ?? "?";
                        var prop = type.GetProperty(fieldName);
                        if (prop != null) return prop.GetValue(options)?.ToString() ?? "?";
                    }
                    catch { }
                    return "?";
                }

                string GetBoolValue(string fieldName) => GetBoolText(GetValue(fieldName) == "True");
                string Sec() => Language.Translate("options.sec", "秒");

                AddLine(Language.Translate("options.map", "地图"), GetMapName(options.MapId));
                AddLine(Language.Translate("options.impostors", "内鬼数量"), options.NumImpostors.ToString());
                AddLine(Language.Translate("options.killCooldown", "击杀冷却"), GetValue("KillCooldown") + Sec());
                AddLine(Language.Translate("options.playerSpeed", "玩家速度"), GetValue("PlayerSpeedMod") + "×");
                AddLine(Language.Translate("options.crewLight", "船员视野"), GetValue("CrewLightMod") + "×");
                AddLine(Language.Translate("options.impostorLight", "内鬼视野"), GetValue("ImpostorLightMod") + "×");
                AddLine(Language.Translate("options.killDistance", "击杀距离"), GetKillDistance(GetValue("KillDistance")));
                AddLine(Language.Translate("options.commonTasks", "普通任务"), GetValue("NumCommonTasks"));
                AddLine(Language.Translate("options.longTasks", "长任务"), GetValue("NumLongTasks"));
                AddLine(Language.Translate("options.shortTasks", "短任务"), GetValue("NumShortTasks"));
                AddLine(Language.Translate("options.emergencyMeetings", "紧急会议"), GetValue("NumEmergencyMeetings"));
                AddLine(Language.Translate("options.emergencyCooldown", "会议冷却"), GetValue("EmergencyCooldown") + Sec());
                AddLine(Language.Translate("options.discussionTime", "讨论时间"), GetValue("DiscussionTime") + Sec());
                AddLine(Language.Translate("options.votingTime", "投票时间"), GetValue("VotingTime") + Sec());
                AddLine(Language.Translate("options.anonymousVotes", "匿名投票"), GetBoolValue("AnonymousVotes"));
                AddLine(Language.Translate("options.confirmImpostor", "确认内鬼"), GetBoolValue("ConfirmImpostor"));
                AddLine(Language.Translate("options.visualTasks", "视觉任务"), GetBoolValue("VisualTasks"));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildOptionsPage", ex);
        }

        if (lines.Count == 0)
        {
            AddFixedText(win, Language.Translate("help.options.unavailable", "无法读取游戏设置"), BodyFontSize, GridTopY);
            return;
        }

        int pages = Math.Max(1, (lines.Count + TextPageLines - 1) / TextPageLines);
        _page = Math.Clamp(_page, 0, pages - 1);

        float y = GridTopY;
        foreach (var line in lines.Skip(_page * TextPageLines).Take(TextPageLines))
        {
            AddFixedText(win, line, LineFontSize, y, TextAlignmentOptions.Left);
            y -= LineStepY;
        }

        AddPager(win, pages, p => { _page = p; BuildContent(); });
    }

    // =====================================================================
    // 职业详情（第二个 HudUI 窗口，左右箭头循环）
    // =====================================================================

    private static void OpenDetail(List<RoleTemplate> roles, int index)
    {
        try
        {
            if (roles == null || roles.Count == 0) return;

            _detailRoles = roles;
            int count = roles.Count;
            _detailIndex = ((index % count) + count) % count;
            BuildDetail();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.OpenDetail", ex);
        }
    }

    private static void BuildDetail()
    {
        try
        {
            DestroyDetailWindow();                  // ⚠️ 用 DestroyDetailWindow：CloseDetail 会把职业列表也清掉
            if (_detailRoles.Count == 0) return;

            var parent = FindParent();
            if (parent == null) return;

            var role = _detailRoles[_detailIndex];

            var win = HudUIWindow.Create("", DetailSize, parent, blockInputBehind: true,
                sortingGroupOrder: DetailOrder, z: -100f);
            if (win == null) return;
            _detailWindow = win;

            // ① 立绘（有才显示）—— 手工建 SpriteRenderer，放在最上面
            if (role.IconImage != null)
            {
                AddPortrait(win, role.IconImage);
                win.AddMargin(PortraitSize + 0.55f);   // 让流水线跳过立绘占的高度
            }

            // ② 标题（职业色 + 第 n/m）—— Nebula: OverlayTitle = 1.8
            win.AddText(Colorize(role.Name, role.Color)
                + $" <size=55%>{_detailIndex + 1}/{_detailRoles.Count}</size>",
                DetailTitleFontSize, TextAlignmentOptions.Center);
            win.AddMargin(0.10f);

            // ③ 开场白
            if (!string.IsNullOrEmpty(role.IntroText))
            {
                win.AddText(Colorize(role.IntroText, role.Color), DetailBodyFontSize, TextAlignmentOptions.Center);
                win.AddMargin(0.06f);
            }

            // ④ 说明 / 阵营 / 分配
            win.AddText(role.GetDocumentText(), DetailBodyFontSize, TextAlignmentOptions.Left);
            win.AddMargin(0.06f);
            win.AddText(Language.Translate("help.role.category", "阵营") + ": " + GetCategoryName(role.RoleCategory),
                BodyFontSize, TextAlignmentOptions.Left);
            win.AddText(GetAllocationLine(role), BodyFontSize, TextAlignmentOptions.Left);

            // ⑤ 左右循环切换（HudUI 自带的箭头按钮）
            MetaScreen.SetUpNavButton(win.Screen, increment =>
            {
                try
                {
                    int count = _detailRoles.Count;
                    if (count == 0) return;
                    _detailIndex = ((_detailIndex + (increment ? 1 : -1)) % count + count) % count;
                    BuildDetail();
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("HelpScreen.DetailNav", ex);
                }
            });

            // ⑥ 关闭按钮：**固定在窗口底部**（流水线内容长短不一，跟着流水会跑出窗口）
            var close = win.AddButton(Language.Translate("help.close", "关闭"), CloseDetail, new Vector2(1.70f, 0.38f));
            try { close?.SetPosition(new Vector3(0f, DetailCloseY, -1f)); SetButtonFont(close, PagerFontSize); } catch { }

            win.AscendSorting(DetailOrder);
            ApplyConfigTextStyleAll(win);       // 字体 + Bold（配置项 UI 那套）
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.BuildDetail", ex);
        }
    }

    /// <summary>职业立绘：外框 + 画像（居中放在窗口顶部）。</summary>
    private static void AddPortrait(HudUIWindow win, Sprite icon)
    {
        try
        {
            var frame = new GameObject("PortraitFrame");
            frame.layer = LayerExpansion.GetUILayer();
            frame.transform.SetParent(win.Screen.transform, false);
            frame.transform.localPosition = new Vector3(0f, PortraitY, -1f);
            var frameSr = frame.AddComponent<SpriteRenderer>();
            frameSr.sprite = HudUIAssets.FrameSprite;
            frameSr.drawMode = SpriteDrawMode.Sliced;
            frameSr.size = new Vector2(PortraitSize + 0.08f, PortraitSize + 0.08f);
            TrackTemp(frame);

            var img = new GameObject("PortraitIcon");
            img.layer = LayerExpansion.GetUILayer();
            img.transform.SetParent(frame.transform, false);
            img.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            var imgSr = img.AddComponent<SpriteRenderer>();
            imgSr.sprite = icon;
            imgSr.drawMode = SpriteDrawMode.Sliced;
            imgSr.size = new Vector2(PortraitSize, PortraitSize);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.AddPortrait] {ex.Message}");
        }
    }

    // =====================================================================
    // 排版小工具
    // =====================================================================

    /// <summary>
    /// 在**指定 y** 写一行字。
    ///
    /// ⚠️ 网格页/文字页的排版全是手工定位的，而 <c>AddText</c> 是纵向流水 ——
    ///    所以这里"加完立刻挪到目标 y"，并且这些页面**不再混用流水线内容**，
    ///    免得两套坐标互相打架（旧版就是因此文字压到按钮上）。
    /// </summary>
    private static void AddFixedText(HudUIWindow win, string text, float fontSize, float y,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, float x = 0f, float? width = null)
    {
        var tmp = win.AddText(text, fontSize, alignment);
        try
        {
            ApplyConfigTextStyle(tmp, fontSize);

            // 分栏用：左对齐文字必须自己收窄 rect，否则会从窗口左边一直排到右边
            if (tmp != null && width.HasValue)
                tmp.rectTransform.sizeDelta = new Vector2(width.Value, tmp.rectTransform.sizeDelta.y);

            // ⚠️⚠️ AddText 把 TMP 挂在 "Text" **子物体**里（TMP 是子物体，见 HudUI.AddText）。
            //      直接挪 TMP 只会在父物体内部再偏一次 → 双重偏移（文字跑到页签栏上面/压在按钮上）。
            //      **必须挪父物体。**
            var holder = tmp != null ? tmp.transform.parent : null;
            if (holder != null) holder.localPosition = new Vector3(x, y, -1f);
        }
        catch { }
    }

    /// <summary>
    /// 把一段文字套成**配置项 UI 那套样式**（照抄 <c>RoleInfoPanel.MakeLine</c> / <c>RoleListPage</c>）：
    ///   · 字体 = <see cref="MenuTextTemplate2"/>（简中字体，Nebula 同款）
    ///   · **Bold**（用户要求：除特殊说明所有自建文字都 Bold）
    ///   · 关掉 autoSizing 并锁死 fontSize（AGENTS §12.2：开着 autoSizing 时 fontSize 完全无效）
    ///   · overflow = Overflow（AGENTS §12.3：Truncate 会把放大的字整段裁掉）
    /// </summary>
    private static void ApplyConfigTextStyle(TextMeshPro? tmp, float fontSize)
    {
        if (tmp == null) return;
        try
        {
            var font = MenuTextTemplate2.Font;
            var mat = MenuTextTemplate2.FontMaterial;
            if (font != null) tmp.font = font;
            if (mat != null) tmp.fontSharedMaterial = mat;

            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.fontSizeMin = fontSize;
            tmp.fontSizeMax = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.ApplyConfigTextStyle] {ex.Message}");
        }
    }

    /// <summary>
    /// 把窗口里**所有**文字统一成配置项 UI 的字体 + Bold（字号保持各自设定）。
    /// **必须在内容全部建完之后调**（页签按钮、网格按钮、正文的 TMP 都是建的时候才出现的）。
    /// </summary>
    private static void ApplyConfigTextStyleAll(HudUIWindow win)
    {
        try
        {
            var font = MenuTextTemplate2.Font;
            var mat = MenuTextTemplate2.FontMaterial;
            if (font == null) return;

            int n = 0;
            foreach (var tmp in win.Screen.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                tmp.font = font;
                if (mat != null) tmp.fontSharedMaterial = mat;
                tmp.fontStyle = FontStyles.Bold;      // 字号不动，只统一字体和粗体
                n++;
            }
            LightLogger.Log($"[HelpScreen] 已把 {n} 段文字套成配置项 UI 样式（简中字体 + Bold）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.ApplyConfigTextStyleAll] {ex.Message}");
        }
    }

    /// <summary>把按钮文字设成配置项 UI 样式（HudUIButton 内部 TMP 默认自动缩放，字号不受控）。</summary>
    private static void SetButtonFont(HudUIButton? btn, float size)
    {
        try { ApplyConfigTextStyle(btn?.Text, size); } catch { }
    }

    /// <summary>建一个手工定位的按钮（并登记，切页时一起销毁）。</summary>
    private static HudUIButton CreateRawButton(HudUIWindow win, string text, Vector2 size, Action onClick,
        float fontSize = RoleFontSize)
    {
        var btn = HudUIButton.Create(win.Screen.transform, text, size, onClick);
        SetButtonFont(btn, fontSize);
        TrackTemp(btn.GameObject);
        return btn;
    }

    /// <summary>底部翻页栏（固定在窗口下部，不参与流水线）。</summary>
    private static void AddPager(HudUIWindow win, int pages, Action<int> onChange)
    {
        if (pages <= 1) return;

        try
        {
            int page = _page;
            var size = new Vector2(PagerBtnW, PagerBtnH);

            var prev = CreateRawButton(win, Language.Translate("help.page.prev", "上一页"), size,
                () => { if (page > 0) onChange(page - 1); }, PagerFontSize);
            prev.SetPosition(new Vector3(-0.84f, PagerY, -1f));

            var next = CreateRawButton(win, Language.Translate("help.page.next", "下一页"), size,
                () => { if (page < pages - 1) onChange(page + 1); }, PagerFontSize);
            next.SetPosition(new Vector3(0.84f, PagerY, -1f));

            AddFixedText(win, $"{page + 1} / {pages}", PagerFontSize, PagerY);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[HelpScreen.AddPager] {ex.Message}");
        }
    }

    private static void TrackTemp(GameObject? obj)
    {
        if (obj != null) _temp.Add(obj);
    }

    private static void DestroyTemp()
    {
        foreach (var obj in _temp)
        {
            try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch { }
        }
        _temp.Clear();
    }

    /// <summary>把文字染成指定颜色（TMP 富文本）。</summary>
    private static string Colorize(string text, Color color)
    {
        try
        {
            var c = color.ToUnityColor();
            return $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";
        }
        catch
        {
            return text;
        }
    }

    /// <summary>按注册序号（再按内部名）排序的职业列表，保证各页显示稳定</summary>
    private static List<RoleTemplate> SortedRoles() =>
        RoleRegistry.AllRoles.OrderBy(r => r.Id).ThenBy(r => r.CodeName).ToList();

    private static string GetCategoryName(RoleCategory category) => category switch
    {
        RoleCategory.Impostor => Language.Translate("role.category.impostor", "内鬼"),
        RoleCategory.Neutral => Language.Translate("role.category.neutral", "中立"),
        _ => Language.Translate("role.category.crewmate", "船员"),
    };

    /// <summary>分配信息行（MaxCount==0 显示不参与分配）</summary>
    private static string GetAllocationLine(RoleTemplate role)
    {
        try
        {
            // ⚠️ 2026-10-06 审查 #17：原来直接读 `role.Allocation.*`（**代码里的默认值**），
            //   而实际分配读的是 `role.X.count` / `role.X.chance` **配置** →
            //   房主改过配置后，帮助页写的和真正会出的**不一致**（用户看到的规则说明是错的）✗
            //   现在统一走分配器那两个入口（它们已经带夹紧）。
            int maxCount = Light.Roles.Assignment.StandardRoleAllocator.GetMaxCount(role);
            int chance = Light.Roles.Assignment.StandardRoleAllocator.GetChance(role);
            int guaranteed = role.Allocation.GuaranteedCount;

            if (maxCount <= 0)
                return role.Name + ": " + Language.Translate("help.overview.noAssign", "不参与分配");

            // 帮助页的"必出 N"必须和分配器一致（分配器会把 GuaranteedCount 夹到 [0, MaxCount]）
            if (guaranteed > maxCount) guaranteed = maxCount;

            string text = $"{role.Name} × {maxCount}";
            if (guaranteed > 0)
                text += $" ({Language.Translate("help.overview.guaranteed", "必出")} {guaranteed})";
            else if (chance < 100)
                text += $" ({Language.Translate("help.overview.chance", "概率")} {chance}%)";
            return text;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("HelpScreen.GetAllocationLine", ex);
            return Language.Translate("help.page.error", "该页面加载失败");
        }
    }

    private static string GetBoolText(bool value) =>
        value ? Language.Translate("options.on", "开") : Language.Translate("options.off", "关");

    /// <summary>地图编号转名称</summary>
    private static string GetMapName(int mapId) => mapId switch
    {
        0 => Language.Translate("map.skeld", "飞船"),
        1 => Language.Translate("map.mira", "米拉"),
        2 => Language.Translate("map.polus", "波卢斯"),
        4 => Language.Translate("map.airship", "飞艇"),
        5 => Language.Translate("map.fungle", "真菌"),
        _ => mapId.ToString(),
    };

    /// <summary>击杀距离数值转文本</summary>
    private static string GetKillDistance(string value) => value switch
    {
        "0" => Language.Translate("killDistance.short", "短"),
        "1" => Language.Translate("killDistance.medium", "中"),
        "2" => Language.Translate("killDistance.long", "长"),
        _ => value,
    };
}
