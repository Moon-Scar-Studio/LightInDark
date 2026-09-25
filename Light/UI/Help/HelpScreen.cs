using System;
using System.Collections.Generic;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Game;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.UI.Window;
using Light.UI.HudUI;
using Light.UI.Window;
using UnityEngine;
using LightInDark.Core;
using Color = LightInDark.Color;
using FontStyle = LightInDark.UI.Window.FontStyle;
using LightGameManager = LightInDark.Game.GameManager;
using MetaScreen = Light.UI.HudUI.MetaScreen;
using TextAlignment = LightInDark.UI.Window.TextAlignment;

namespace Light.UI.Help;

/// <summary>
/// H 键帮助菜单
/// </summary>
public static class HelpScreen
{
    /// <summary>帮助页签（位掩码，配合 GetValidTabs）</summary>
    [Flags]
    public enum HelpTab
    {
        Search = 1, MyInfo = 2, Roles = 4, Overview = 8, Options = 16,
        Slides = 32, Achievements = 64, Stamps = 128,
    }

    /// <summary>窗口尺寸（高度 4.1 + 0.6）</summary>
    private static readonly Vector2 HelpSize = new(7.8f, 4.7f);

    private static MetaScreen? _lastScreen;
    private static HelpTab _lastTab = HelpTab.Roles;
    private static string _searchKeyword = "";

    /// <summary>帮助菜单是否已打开（Unity 判断：窗口销毁后视为未打开）</summary>
    public static bool OpenedAnyHelpScreen => _lastScreen;

    /// <summary>打开帮助</summary>
    public static void TryOpenHelpScreen()
    {
        try
        {
            if (_lastScreen) return;
            _lastScreen = OpenHelpScreen();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.TryOpenHelpScreen]", ex);
        }
    }

    /// <summary>
    /// 打开帮助并直接定位到“我的职业”页（F1 快捷查看自己职业）。
    /// </summary>
    public static void TryOpenMyInfo()
    {
        try
        {
            if (LightGameManager.Instance?.LocalPlayer?.HasRole != true) return; // 未分配角色时不打开
            _lastTab = HelpTab.MyInfo;
            _lastScreen = OpenHelpScreen();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.TryOpenMyInfo]", ex);
        }
    }

    /// <summary>关闭帮助</summary>
    public static void TryCloseHelpScreen()
    {
        try
        {
            if (_lastScreen)
            {
                _lastScreen!.CloseScreen();
                _lastScreen = null;
            }
        }
        catch { }
    }

    /// <summary>当前有效页签（MyInfo 仅游戏中已分配角色时显示）</summary>
    private static HelpTab GetValidTabs()
    {
        try
        {
            var valid = HelpTab.Search | HelpTab.Roles | HelpTab.Overview | HelpTab.Options
                | HelpTab.Slides | HelpTab.Achievements | HelpTab.Stamps;
            if (LightGameManager.Instance?.LocalPlayer?.HasRole == true)
                valid |= HelpTab.MyInfo;
            return valid;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.GetValidTabs]", ex); return default;
        }
    }

    private static MetaScreen OpenHelpScreen()
    {
        try
        {
            Transform? parent;
            if (HudManager.Instance != null)
                // 挂 HUD 根节点（UI 层由 UI 相机最后渲染），确保窗口盖住场景内所有 UI
                parent = HudManager.Instance.transform;
            else if (GameObject.FindObjectOfType<MainMenuManager>() != null)
                parent = GameObject.FindObjectOfType<MainMenuManager>().transform;
            else
                parent = Camera.main != null ? Camera.main.transform : null;

            var screen = MetaScreen.GenerateWindow(HelpSize, parent, new Vector3(0, 0, -50f),
                withBlackScreen: true, closeOnClickOutside: false,
                background: BackgroundSetting.Modern, withCloseButton: true);

            var validTabs = GetValidTabs();
            // 上次标签已无效（MyInfo 仅游戏中已分配角色时有效）时回退到默认标签
            if (_lastTab == HelpTab.MyInfo && LightGameManager.Instance?.LocalPlayer?.HasRole != true)
                _lastTab = HelpTab.Roles;

            ShowScreen(screen, validTabs, _lastTab);
            return screen;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.OpenHelpScreen]", ex); return default;
        }
    }

    private static void ShowScreen(MetaScreen screen, HelpTab validTabs, HelpTab tab)
    {
        try
        {
            _lastTab = tab;
            screen.SetWidget(BuildTabWidget(screen, validTabs, tab), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowScreen]", ex);
        }
    }

    // =====================================================================
    // 标签栏 + 分发
    // =====================================================================

    /// <summary>标签按钮属性（0.82×0.21，字号 1.6）</summary>
    private static TextAttribute TabButtonAttr => new(
        TextAlignment.Center, LIDGUI.Instance.GetFont(FontAsset.Gothic), FontStyle.Bold,
        new FontSize(1.6f, false), new Size(0.82f, 0.21f), Color.White, false);

    /// <summary>显示顺序：Search → MyInfo → Roles → Overview → Options → Slides → Achievements → Stamps</summary>
    private static readonly HelpTab[] TabOrder =
    {
        HelpTab.Search, HelpTab.MyInfo, HelpTab.Roles, HelpTab.Overview, HelpTab.Options,
        HelpTab.Slides, HelpTab.Achievements, HelpTab.Stamps,
    };

    private static string GetTabName(HelpTab tab) => tab switch
    {
        HelpTab.Search => Language.Translate("help.tabs.search", "搜索"),
        HelpTab.MyInfo => Language.Translate("help.tabs.myInfo", "我的职业"),
        HelpTab.Roles => Language.Translate("help.tabs.roles", "职业"),
        HelpTab.Overview => Language.Translate("help.tabs.overview", "概览"),
        HelpTab.Options => Language.Translate("help.tabs.options", "设置"),
        HelpTab.Slides => Language.Translate("help.tabs.slides", "幻灯片"),
        HelpTab.Achievements => Language.Translate("help.tabs.achievements", "成就"),
        HelpTab.Stamps => Language.Translate("help.tabs.stamps", "印章"),
        _ => "?",
    };

    private static GUIWidget BuildTabWidget(MetaScreen screen, HelpTab validTabs, HelpTab tab)
    {
        try
        {
            var gui = LIDGUI.Instance;
            // 布局改为“左侧页签栏 + 右侧内容”两栏式（区别于原水平的顶部标签栏）。
            return gui.HorizontalHolder(GUIAlignment.Center,
                BuildTabsWidget(screen, validTabs, tab),
                gui.HorizontalMargin(0.15f),
                gui.VerticalHolder(GUIAlignment.Center, BuildTabContent(tab)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.BuildTabWidget]", ex); return default;
        }
    }

    /// <summary>左侧页签栏：当前白、其他灰，按钮纵向堆叠排列</summary>
    private static GUIWidget BuildTabsWidget(MetaScreen screen, HelpTab validTabs, HelpTab current)
    {
        try
        {
            var gui = LIDGUI.Instance;
            var buttons = new List<GUIWidget?>();
            foreach (var tab in TabOrder)
            {
                if ((validTabs & tab) == 0) continue;
                var color = tab == current ? Color.White : Color.Gray;
                buttons.Add(gui.RawButton(GUIAlignment.Center, TabButtonAttr, GetTabName(tab),
                    _ => ShowScreen(screen, validTabs, tab), color: color, selectedColor: color));
            }
            // 纵向堆叠成左侧栏
            return gui.VerticalHolder(GUIAlignment.Center, buttons.ToArray());
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.BuildTabsWidget]", ex); return default;
        }
    }

    private static GUIWidget BuildTabContent(HelpTab tab) => tab switch
    {
        HelpTab.Search => ShowSearchScreen(),
        HelpTab.MyInfo => ShowMyRolesScreen(),
        HelpTab.Roles => ShowAssignableScreen(),
        HelpTab.Overview => ShowPreviewScreen(),
        HelpTab.Options => ShowOptionsScreen(),
        HelpTab.Slides => ShowPlaceholderScreen("help.tabs.slides", "幻灯片"),
        HelpTab.Achievements => ShowPlaceholderScreen("help.tabs.achievements", "成就"),
        HelpTab.Stamps => ShowPlaceholderScreen("help.tabs.stamps", "印章"),
        _ => LIDGUI.Instance.EmptyWidget,
    };

    /// <summary>占位页：功能尚未实现时的空滚动页</summary>
    private static GUIWidget ShowPlaceholderScreen(string key, string name)
    {
        try
        {
            var gui = LIDGUI.Instance;
            var text = gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                Language.Translate(key + ".empty", name + "内容尚未实装"));
            return gui.ScrollView(GUIAlignment.Center, new Size(7.4f, 4.1f), null,
                gui.VerticalHolder(GUIAlignment.Center, text), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowPlaceholderScreen]", ex); return default;
        }
    }

    // =====================================================================
    // Roles 页
    // =====================================================================

    /// <summary>职业按钮属性（字号 1.8）</summary>
    private static TextAttribute RoleButtonAttr => new(
        TextAlignment.Center, LIDGUI.Instance.GetFont(FontAsset.GothicMasked), FontStyle.Bold,
        new FontSize(1.8f, false), new Size(1.2f, 0.29f), Color.White, false);

    private static GUIWidget ShowAssignableScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var listed = new List<Role>();
            var inner = new List<GUIWidget?>();

            void AddCategory(RoleCategory category, string title, Color titleColor)
            {
                var roles = new List<Role>();
                foreach (var role in RoleRegistry.AllRoles)
                    if (role.Category == category) roles.Add(role);
                if (roles.Count == 0) return;

                if (inner.Count > 0) inner.Add(gui.VerticalMargin(0.2f));
                inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentTitle),
                    gui.ColorTextComponent(titleColor, new RawTextComponent(title)).GetString()));
                inner.Add(gui.VerticalMargin(0.1f));

                var buttons = new List<GUIWidget?>();
                foreach (var role in roles)
                {
                    listed.Add(role);
                    int index = listed.Count - 1;
                    var name = gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString();
                    buttons.Add(gui.RawButton(GUIAlignment.Center, RoleButtonAttr, name,
                        _ => OpenAssignableHelp(listed, index)));
                }
                inner.Add(gui.Arrange(GUIAlignment.Center, buttons, 4));
            }

            AddCategory(RoleCategory.Impostor, Language.Translate("role.category.impostor", "内鬼"), Color.ImpostorColor);
            AddCategory(RoleCategory.Neutral, Language.Translate("role.category.neutral", "中立"), new Color(1f, 0.7f, 0f));
            AddCategory(RoleCategory.Crewmate, Language.Translate("role.category.crewmate", "船员"), Color.CrewmateColor);

            return gui.ScrollView(GUIAlignment.Center, new Size(7.4f, 4.1f), null,
                gui.VerticalHolder(GUIAlignment.Center, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowAssignableScreen]", ex); return default;
        }
    }

    // =====================================================================
    // MyInfo 页
    // =====================================================================

    private static GUIWidget ShowMyRolesScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var role = LightGameManager.Instance?.LocalPlayer?.Role;
            var inner = new List<GUIWidget?>();

            if (role != null)
            {
                var name = gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString();
                inner.Add(gui.RawButton(GUIAlignment.Center, RoleButtonAttr, name,
                    _ => OpenAssignableHelp(new List<Role> { role }, 0)));
            }
            else
            {
                inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("help.myInfo.none", "当前未分配职业")));
            }

            return gui.ScrollView(GUIAlignment.Center, new Size(7.4f, 3.4f), null,
                gui.VerticalHolder(GUIAlignment.Center, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowMyRolesScreen]", ex); return default;
        }
    }

    // =====================================================================
    // Search 页
    // =====================================================================

    private static GUIWidget ShowSearchScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var scrollView = new GUIScrollView(GUIAlignment.Center, new Size(7.4f, 3.2f),
                () => BuildSearchResultWidget(gui, _searchKeyword));

            void ShowResult(string keyword)
            {
                _searchKeyword = keyword;
                scrollView.Artifact?.SetWidget(BuildSearchResultWidget(gui, keyword), out _);
            }

            var field = new TextFieldWidget(GUIAlignment.Center, new Vector2(5f, 0.38f),
                Language.Translate("help.search.inputHint", "输入关键词"),
                keyword => ShowResult(keyword));

            var searchButton = gui.RawButton(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.CenteredBoldFixed),
                Language.Translate("help.search.search", "搜索"),
                _ => ShowResult(field.Field?.Text ?? ""));

            return gui.VerticalHolder(GUIAlignment.Center,
                gui.HorizontalHolder(GUIAlignment.Center, field, gui.HorizontalMargin(0.1f), searchButton),
                gui.VerticalMargin(0.1f),
                scrollView);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowSearchScreen]", ex); return default;
        }
    }

    /// <summary>搜索结果：匹配 Name 或 Description</summary>
    private static GUIWidget BuildSearchResultWidget(LIDGUI gui, string keyword)
    {
        try
        {
            var inner = new List<GUIWidget?>();
            keyword = keyword.Trim();

            if (keyword.Length == 0)
            {
                inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("help.search.hint", "输入关键词搜索职业")));
                return gui.VerticalHolder(GUIAlignment.Left, inner);
            }

            var matched = new List<Role>();
            foreach (var role in RoleRegistry.AllRoles)
            {
                if (role.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    role.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    matched.Add(role);
            }

            if (matched.Count == 0)
            {
                inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("help.search.noResult", "未找到相关职业")));
            }
            else
            {
                for (int i = 0; i < matched.Count; i++)
                {
                    int index = i;
                    var name = gui.ColorTextComponent(matched[i].Color, new RawTextComponent(matched[i].Name)).GetString();
                    inner.Add(gui.RawButton(GUIAlignment.Left, RoleButtonAttr, name,
                        _ => OpenAssignableHelp(matched, index)));
                    inner.Add(gui.VerticalMargin(0.1f));
                }
            }

            return gui.VerticalHolder(GUIAlignment.Left, inner);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.BuildSearchResultWidget]", ex); return default;
        }
    }

    // =====================================================================
    // Overview 页（简化：无模拟人数切换）
    // =====================================================================

    private static GUIWidget ShowPreviewScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;

            // Il2Cpp 集合不支持 System.Linq，复制到 List 再 Count
            int playerCount = 0;
            if (PlayerControl.AllPlayerControls != null)
            {
                var players = new List<PlayerControl>();
                foreach (var pc in PlayerControl.AllPlayerControls) players.Add(pc);
                playerCount = players.Count;
            }

            // 单列：分类标题 + 职业分配信息
            GUIWidget BuildColumn(RoleCategory category, string title)
            {
                var column = new List<GUIWidget?>();
                column.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentTitle), title));
                column.Add(gui.VerticalMargin(0.15f));
                foreach (var role in RoleRegistry.AllRoles)
                {
                    if (role.Category != category) continue;
                    column.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentStandard),
                        GetAllocationLine(role)));
                    column.Add(gui.VerticalMargin(0.08f));
                }
                return gui.VerticalHolder(GUIAlignment.Center, column);
            }

            // 三栏并排（内鬼 / 中立 / 船员）
            var columns = gui.HorizontalHolder(GUIAlignment.Center,
                BuildColumn(RoleCategory.Impostor, Language.Translate("help.category.impostor", "内鬼")),
                gui.HorizontalMargin(0.5f),
                BuildColumn(RoleCategory.Neutral, Language.Translate("help.category.neutral", "中立")),
                gui.HorizontalMargin(0.5f),
                BuildColumn(RoleCategory.Crewmate, Language.Translate("help.category.crewmate", "船员")));

            var header = gui.VerticalHolder(GUIAlignment.Center,
                gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentTitle),
                    Language.Translate("help.overview.header", "分配计划")),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("help.overview.players", "当前玩家数") + ": " + playerCount),
                gui.VerticalMargin(0.1f),
                columns);

            return gui.ScrollView(GUIAlignment.Center, new Size(7.4f, 2.92f), null, header, out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowPreviewScreen]", ex); return default;
        }
    }

    // =====================================================================
    // Options 页
    // =====================================================================

    private static GUIWidget ShowOptionsScreen()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var inner = new List<GUIWidget?>();

            try
            {
                // CurrentGameOptions 返回 IGameOptions 接口，仅 MapId/NumImpostors 可直接访问，
                // 其余字段运行时反射读取（字段名以 AmongUs.GameOptions 为准）
                var options = GameOptionsManager.Instance.CurrentGameOptions;
                if (options != null)
                {
                    void AddLine(string name, string value)
                    {
                        inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentStandard),
                            $"{name}: {value}"));
                        inner.Add(gui.VerticalMargin(0.03f));
                    }

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

                    string GetBoolValue(string fieldName) => GetValue(fieldName) == "True" ? GetBoolText(true) : GetBoolText(false);

                    AddLine(Language.Translate("options.map", "地图"), GetMapName(options.MapId));
                    AddLine(Language.Translate("options.impostors", "内鬼数量"), options.NumImpostors.ToString());
                    AddLine(Language.Translate("options.killCooldown", "击杀冷却"),
                        GetValue("KillCooldown") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.playerSpeed", "玩家速度"),
                        GetValue("PlayerSpeedMod") + "×");
                    AddLine(Language.Translate("options.crewLight", "船员视野"),
                        GetValue("CrewLightMod") + "×");
                    AddLine(Language.Translate("options.impostorLight", "内鬼视野"),
                        GetValue("ImpostorLightMod") + "×");
                    AddLine(Language.Translate("options.killDistance", "击杀距离"), GetKillDistance(GetValue("KillDistance")));
                    AddLine(Language.Translate("options.commonTasks", "普通任务"), GetValue("NumCommonTasks"));
                    AddLine(Language.Translate("options.longTasks", "长任务"), GetValue("NumLongTasks"));
                    AddLine(Language.Translate("options.shortTasks", "短任务"), GetValue("NumShortTasks"));
                    AddLine(Language.Translate("options.emergencyMeetings", "紧急会议"), GetValue("NumEmergencyMeetings"));
                    AddLine(Language.Translate("options.emergencyCooldown", "会议冷却"),
                        GetValue("EmergencyCooldown") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.discussionTime", "讨论时间"),
                        GetValue("DiscussionTime") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.votingTime", "投票时间"),
                        GetValue("VotingTime") + Language.Translate("options.sec", "秒"));
                    AddLine(Language.Translate("options.anonymousVotes", "匿名投票"), GetBoolValue("AnonymousVotes"));
                    AddLine(Language.Translate("options.confirmImpostor", "确认内鬼"), GetBoolValue("ConfirmImpostor"));
                    AddLine(Language.Translate("options.visualTasks", "视觉任务"), GetBoolValue("VisualTasks"));
                }
            }
            catch { }

            if (inner.Count == 0)
                inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("help.options.unavailable", "无法读取游戏设置")));

            return gui.ScrollView(GUIAlignment.Center, new Size(7.4f, 3.6f), null,
                gui.VerticalHolder(GUIAlignment.Left, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.ShowOptionsScreen]", ex); return default;
        }
    }

    // =====================================================================
    // 职业详情子窗口
    // =====================================================================

    private static void OpenAssignableHelp(List<Role> roles, int index)
    {
        try
        {
            if (roles.Count == 0) return;
            if (index < 0 || index >= roles.Count) index = 0;

            int currentIndex = index;
            MetaScreen? window = null;

            Transform? GetParent()
            {
                if (HudManager.Instance != null)
                    // 挂 HUD 根节点，与主帮助菜单同层级
                    return HudManager.Instance.transform;
                if (GameObject.FindObjectOfType<MainMenuManager>() != null)
                    return GameObject.FindObjectOfType<MainMenuManager>().transform;
                return Camera.main != null ? Camera.main.transform : null;
            }

            void ReopenWindow()
            {
                if (window) window!.CloseScreen();

                // 详情窗口后创建，排序相同的情况下自然渲染在帮助菜单之上
                window = MetaScreen.GenerateWindow(new Vector2(7f, 4.5f), GetParent(), new Vector3(0, 0, -100f),
                    withBlackScreen: true, closeOnClickOutside: true,
                    background: BackgroundSetting.Modern, sortingGroupOrder: 200);
                window.SetWidget(BuildRoleDetailWidget(roles[currentIndex]), out _);

                MetaScreen.SetUpNavButton(window, increment =>
                {
                    currentIndex = (roles.Count + currentIndex + (increment ? 1 : -1)) % roles.Count;
                    ReopenWindow();
                });
            }

            ReopenWindow();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.OpenAssignableHelp]", ex);
        }
    }

    private static GUIWidget BuildRoleDetailWidget(Role role)
    {
        try
        {
            var gui = LIDGUI.Instance;
            var attr = gui.GetAttribute(AttributeAsset.OverlayContent);

            // 立绘区：框 + 画像重叠，无立绘时不显示
            GUIWidget? portrait = null;
            if (role.IconImage != null)
            {
                portrait = new GUIOverlapHolder(GUIAlignment.Left,
                    gui.Image(GUIAlignment.Left, HudUIAssets.FrameSprite, new FuzzySize(0.55f, 0.55f)),
                    gui.Image(GUIAlignment.Left, role.IconImage, new FuzzySize(0.5f, 0.5f)));
            }

            // 右侧文字列：职业名 + 开场白（开场白为空时不显示）
            var texts = new List<GUIWidget?>
            {
                gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.OverlayTitle),
                    gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString()),
                gui.VerticalMargin(0.03f),
            };
            if (!string.IsNullOrEmpty(role.IntroBlurb))
            {
                texts.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.OverlayContent),
                    gui.ColorTextComponent(role.Color, new RawTextComponent(role.IntroBlurb)).GetString()));
            }
            var textColumn = gui.VerticalHolder(GUIAlignment.Left, texts.ToArray());

            // 头部行：左对齐，立绘与文字列垂直居中等高
            var headerWidgets = new List<GUIWidget?>();
            if (portrait != null) headerWidgets.Add(portrait);
            headerWidgets.Add(textColumn);
            var header = gui.HorizontalHolder(GUIAlignment.Left, headerWidgets.ToArray());

            // 技能介绍：优先 SkillDescription，为空回退 Description
            var skill = string.IsNullOrEmpty(role.SkillDescription) ? role.Description : role.SkillDescription;

            return gui.VerticalHolder(GUIAlignment.Left,
                header,
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentStandard), skill),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, attr,
                    Language.Translate("help.role.category", "阵营") + ": " + GetCategoryName(role.Category)),
                gui.VerticalMargin(0.1f),
                gui.RawText(GUIAlignment.Left, attr, GetAllocationLine(role)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.BuildRoleDetailWidget]", ex); return default;
        }
    }

    // =====================================================================
    // 公共小工具
    // =====================================================================

    private static string GetCategoryName(RoleCategory category) => category switch
    {
        RoleCategory.Impostor => Language.Translate("role.category.impostor", "内鬼"),
        RoleCategory.Neutral => Language.Translate("role.category.neutral", "中立"),
        _ => Language.Translate("role.category.crewmate", "船员"),
    };

    /// <summary>分配信息行（MaxCount==0 显示不参与分配）</summary>
    /// <summary>分配信息行（读取 RoleConfig 注册表当前值：最大数量/生成概率，MaxCount==0 显示不参与分配）。</summary>
    private static string GetAllocationLine(Role role)
    {
        try
        {
            int maxCount = RoleConfig.GetRoleCount(role.CodeName, role.Allocation.MaxCount);
            int chance = RoleConfig.GetRoleChance(role.CodeName, role.Allocation.Chance);
            int guaranteed = role.Allocation.GuaranteedCount;

            if (maxCount <= 0)
                return role.Name + ": " + Language.Translate("help.overview.noAssign", "不参与分配");

            string text = $"{role.Name} × {maxCount}";
            if (guaranteed > 0)
                text += $" ({Language.Translate("help.overview.guaranteed", "必出")} {guaranteed})";
            else if (chance < 100)
                text += $" ({Language.Translate("help.overview.chance", "概率")} {chance}%)";
            return text;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HelpScreen.GetAllocationLine]", ex); return default;
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
