using System;
using System.Collections.Generic;
using HarmonyLib;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.UI.Window;
using Light.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using UColor = UnityEngine.Color;

namespace Light.Patches;

/// <summary>
/// 原版规则编辑界面（GameSettingMenu）改造。
///
/// 左边栏 = 8 个页签（**克隆原版按钮**，两列排布）：
///   左列 从上到下：预设 / 游戏设置 / MOD / 幽灵
///   右列 从上到下：船员 / 内鬼 / 中立 / 附加
/// 每个页签右侧有一个小图标位（TONE 里"星星"的位置），三态：
///   常态 = 未选中图（原始大小）／悬浮 = 未选中图（放大）／选中 = 选中图（保持放大）。
///   放大使用**绝对缩放**，反复点击不会累积变大。
///
/// 页签切换交给原版 <c>GameSettingMenu.ChangeTab</c>（由原版负责互斥/遮罩/描述文字），
/// 我们只负责各页内容：预设页 = 4 按钮框架；游戏设置 = 原版不动；
/// MOD/幽灵/船员/内鬼/中立/附加 = 配置项面板或「暂未实现」占位。
///
/// 配置行用原版控件渲染（见 Light.UI.Config.ConfigUIPanel），
/// 行模板取自 <c>GameSettingMenu.GameSettingsTab</c> 的私有预制体字段。
///
/// 美术资源按约定路径从嵌入资源加载（见 LoadTabAndPresetAssets），文件缺失时保持占位、不崩。
///
/// ─────────────────────────────────────────────────────────────
/// 【借鉴声明】页签的实现方式借鉴自 TONE（Town of Next，社区常称 ToN/TONE）：
///   · 克隆原版按钮当页签模板、替换 OnClick、用 ChangeTab 切页 —— TONE\Patches\GameSettingMenuPatch.cs
///   · 配置行从 GameOptionsMenu 的私有预制体字段克隆，并合成 BaseGameSetting 走 SetUpFromData
///     —— TONE\Patches\GameOptionsMenuPatch.cs（其 GetSetting / CreateSettingsPrefix）
/// 本项目按其思路用 IL2CPP interop 重写，未直接复制代码。感谢 TONE 作者。
/// ─────────────────────────────────────────────────────────────
/// </summary>
[HarmonyPatch]
public static class GameSettingMenuPatch
{
    /// <summary>
    /// 左边栏 8 个页签（克隆原版按钮样式），**坐标照抄 TONE**。
    ///
    /// TONE 的排布算法（GameSettingMenuPatch.cs:71-73）：
    ///   offset = (0, 0.5 * ((t+1)/2), 0)
    ///   pos    = (((t+1) % 2 == 0) ? Left(-3.9,-0.4) : Right(-2.4,-0.4)) - offset
    ///   scale  = (0.45, 0.6, 1)
    /// 即从下往上、左右交错：t=0 在右下，t=1 在左上，逐排上升。
    ///
    /// 我们的编号与 TONE 的视觉顺序对齐（用户指定）：
    ///   左列从上到下：预设 / 游戏设置 / MOD / 幽灵
    ///   右列从上到下：船员 / 内鬼 / 中立 / 附加
    /// 所以把 TONE 的"从下往上"结果反排到我们的数组下标上。
    /// </summary>
    private static readonly (string Key, string Cn, int Column, int Row)[] VanillaTabs =
    {
        // 左列（Column=0），TONE 的 x=-3.9；从上往下 Row 递增
        ("Preset",       "预设",     0, 0),
        ("GameSettings", "游戏设置", 0, 1),
        ("Mod",          "模组设置", 0, 2),
        ("Ghost",        "幽灵",     0, 3),
        // 右列（Column=1），TONE 的 x=-2.4
        ("Crewmate",     "船员",     1, 0),
        ("Impostor",     "内鬼",     1, 1),
        ("Neutral",      "中立",     1, 2),
        ("Modifier",     "附加",     1, 3),
    };

    /// <summary>页签下标常量（供别处引用）。</summary>
    private const int TabPreset = 0;
    private const int TabGameSettings = 1;
    private const int TabMod = 2;
    private const int TabGhost = 3;
    private const int TabCrewmate = 4;
    private const int TabImpostor = 5;
    private const int TabNeutral = 6;
    private const int TabModifier = 7;

    // =====================================================================
    //  配色（用户指定）
    //   · MOD/预设/游戏设置 → 中性色
    //   · 职业类页签        → 用它们**原本的描边色**（与 FS / ToN 的职业配色一致）
    //   · 按钮边框          → 淡金色
    // =====================================================================

    /// <summary>淡金色（按钮边框）。</summary>
    private static readonly UColor PaleGold = new(0.85f, 0.72f, 0.38f, 1f);
    /// <summary>淡金色（更亮一档，用于选中态）。</summary>
    private static readonly UColor PaleGoldBright = new(1f, 0.88f, 0.55f, 1f);

    /// <summary>
    /// 各页签文字/描边色。职业类沿用原版的职业配色（"原本的描边色"）：
    ///   船员 #8CFFFF / 内鬼 #FF1919 / 中立 #7F8C8D / 附加 #FF9ACE / 幽灵 #E7E7E7
    /// 非职业页签用中性灰白。
    /// </summary>
    private static UColor RoleTabTextColor(int index)
    {
        switch (index)
        {
            case TabCrewmate: return new UColor(0.549f, 1.000f, 1.000f, 1f);   // #8CFFFF
            case TabImpostor: return new UColor(1.000f, 0.098f, 0.098f, 1f);   // #FF1919
            case TabNeutral:  return new UColor(0.498f, 0.549f, 0.553f, 1f);   // #7F8C8D
            case TabModifier: return new UColor(1.000f, 0.604f, 0.808f, 1f);   // #FF9ACE
            case TabGhost:    return new UColor(0.906f, 0.906f, 0.906f, 1f);   // #E7E7E7
            default:          return MenuTextTemplate.GlowWhite;               // 预设/游戏设置/MOD
        }
    }

    /// <summary>各 MOD 类页签对应的配置分类（预设/游戏设置为原版功能，不在其中）。</summary>
    private static readonly ConfigCategory[] TabCategories =
    {
        ConfigCategory.Mod,        // Mod
        ConfigCategory.Ghost,      // Ghost
        ConfigCategory.Crewmate,   // Crewmate
        ConfigCategory.Impostor,   // Impostor
        ConfigCategory.Neutral,    // Neutral
        ConfigCategory.Modifier,   // Modifier
    };

    private static readonly string[] PresetLabels =
    {
        "加载预设", "保存预设", "导出预设为TXT文件", "导入预设",
    };

    // =====================================================================
    //  预设按钮（2026-10-06 重构）
    //
    //  之前这 4 个按钮的 OnClick 是 `/* 本轮无效果 */` —— 只有 UI、没有逻辑。
    //  现在接上 LightInDark.Configuration.PresetStore / PresetCodec：
    //    · 配置项一改，PresetStore 就异步写 <persistentDataPath>/LightInDark/Preset/Current.lidpreset
    //    · 这四个按钮操作的是那一套（Current = 当前配置；Save/ = 具名预设，后续接）
    // =====================================================================

    private const int PresetLoad = 0;
    private const int PresetSave = 1;
    private const int PresetExport = 2;
    private const int PresetImport = 3;

    /// <summary>预设页 4 个按钮的点击。</summary>
    private static void OnPresetButton(int index)
    {
        try
        {
            switch (index)
            {
                case PresetLoad:
                    // ★ 打开**预设窗口**（用户设计图那个二级窗口）：
                    //   左侧可搜索的预设列表 + 右侧详情 + 加载/删除。
                    //   再点一次同一个按钮 = 关掉（Toggle）。
                    Light.UI.Config.PresetWindow.Toggle();
                    break;

                case PresetSave:
                    // ★ 打开**保存窗口**（预设名 / 作者两个输入框 + 保存/取消）。
                    //   ⚠️ 先关掉预设窗口 —— 三个窗口叠一起点击关系会很难理
                    //     （而且预设窗口自己会屏蔽输入，会把保存窗口的输入框一起挡掉）。
                    Light.UI.Config.PresetWindow.Close();
                    Light.UI.Config.PresetSaveWindow.Open();
                    break;

                case PresetExport:
                    // 导出**给人看的 TXT**：弹 Windows 自带的保存框选位置（用户要求）。
                    //   ⚠️ 这个对话框是模态的，会阻塞主线程直到用户关掉 —— 只在这里调，别放每帧。
                    {
                        var path = LightInDark.Configuration.FileDialog.Save(
                            "导出预设为 TXT",
                            LightInDark.Configuration.FileDialog.TxtFilter,
                            "txt",
                            defaultName: "LID预设.txt",
                            initialDir: LightInDark.Configuration.FileDialog.DefaultDir());

                        if (path == null) { ShowPresetToast("已取消导出", ""); break; }

                        var (ok, msg) = LightInDark.Configuration.PresetLibrary.ExportTxt(path, "");
                        ShowPresetToast(ok ? "已导出 TXT" : "导出失败", msg);
                    }
                    break;

                case PresetImport:
                    // 导入：弹 Windows 自带的打开框选 .lidpreset（用户要求）
                    {
                        var path = LightInDark.Configuration.FileDialog.Open(
                            "选择要导入的预设 (.lidpreset)",
                            LightInDark.Configuration.FileDialog.PresetFilter,
                            LightInDark.Configuration.PresetStore.SaveDir);

                        if (path == null) { ShowPresetToast("已取消导入", ""); break; }

                        var (ok, msg) = LightInDark.Configuration.PresetStore.LoadFrom(path);
                        ShowPresetToast(ok ? "已导入" : "导入失败", msg);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.OnPresetButton]", ex);
        }
    }

    /// <summary>预设操作的结果提示 —— 走原版左上角那块描述文字（和配置项悬停同一块）。</summary>
    private static void ShowPresetToast(string title, string detail)
    {
        try
        {
            var menu = GameSettingMenu.Instance;
            var tmp = menu != null ? menu.MenuDescriptionText : null;
            if (tmp == null) { LightLogger.Log($"[预设] {title}：{detail}"); return; }

            tmp.SetText($"{title}\n{detail}");
            tmp.fontStyle = FontStyles.Bold;
            tmp.ForceMeshUpdate();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.ShowPresetToast] {ex.Message}");
        }
    }

    // =====================================================================
    //  美术资源槽位
    //
    //  约定路径（相对 Light.Resources，即 Light\Resources\ 下）：
    //    页签图标 → Configuration\Settings\Tab_{Key}_0.png   （常态/未选中）
    //               Configuration\Settings\Tab_{Key}_1.png   （选中）
    //      Key ∈ Preset / GameSettings / Mod / Ghost / Crewmate / Impostor / Neutral / Modifier
    //    预设页按钮 → GUI\Preset\<名字>Normal.png / <名字>Hover.png
    //
    //  文件缺失时该项保持 null → 走占位（图标槽留空），不崩。
    //  所以"只画好了其中几张"也能正常工作。
    // =====================================================================

    /// <summary>页签图标 8 张常态图 + 8 张选中图（顺序同 <see cref="VanillaTabs"/>）。</summary>
    private static readonly Sprite?[] _tabIconNormal = new Sprite?[8];
    private static readonly Sprite?[] _tabIconSelected = new Sprite?[8];

    /// <summary>预设按钮 4 张常态图（顺序同 <see cref="PresetLabels"/>）。</summary>
    private static readonly Sprite?[] _presetNormal = new Sprite?[4];
    /// <summary>预设按钮 4 张高光图（顺序同上）。</summary>
    private static readonly Sprite?[] _presetHover = new Sprite?[4];

    private static bool _assetsLoaded;

    /// <summary>预设按钮图片文件名（不带 Normal/Hover 后缀）。</summary>
    private static readonly string[] PresetFileNames =
    {
        "LoadPreset", "SavePreset", "ExportPreset", "ImportPreset",
    };

    /// <summary>
    /// 载入全部槽位图片。每个文件独立 try/catch 且缺失即留 null。
    /// </summary>
    private static void LoadTabAndPresetAssets()
    {
        if (_assetsLoaded) return;
        _assetsLoaded = true;

        for (int i = 0; i < PresetFileNames.Length && i < _presetNormal.Length; i++)
        {
            _presetNormal[i] = TryLoad($"GUI/Preset/{PresetFileNames[i]}Normal.png");
            _presetHover[i] = TryLoad($"GUI/Preset/{PresetFileNames[i]}Hover.png");
        }

        for (int i = 0; i < VanillaTabs.Length && i < _tabIconNormal.Length; i++)
        {
            string key = VanillaTabs[i].Key;
            _tabIconNormal[i] = TryLoad($"Configuration/Settings/Tab_{key}_0.png");
            _tabIconSelected[i] = TryLoad($"Configuration/Settings/Tab_{key}_1.png");
        }

        int ok = 0, miss = 0;
        foreach (var s in _presetNormal) { if (s != null) ok++; else miss++; }
        foreach (var s in _presetHover) { if (s != null) ok++; else miss++; }
        foreach (var s in _tabIconNormal) { if (s != null) ok++; else miss++; }
        foreach (var s in _tabIconSelected) { if (s != null) ok++; else miss++; }
        LightLogger.Log($"[GameSettingMenuPatch] 槽位图片载入完成：{ok} 张就绪，{miss} 张缺失（缺失项保持占位）");
    }

    /// <summary>从嵌入资源按相对路径取 Sprite；不存在时静默返回 null（不打错误日志）。</summary>
    private static Sprite? TryLoad(string relativePath)
    {
        try
        {
            var asm = typeof(LightPlugin).Assembly;
            string resName = "Light.Resources." + relativePath.Replace('/', '.').Replace('\\', '.');
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return null;      // 美术未提供：正常情况，不报错

            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);

            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false)) return null;
            tex.wrapMode = TextureWrapMode.Clamp;

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.TryLoad] {relativePath} 载入失败：{ex.Message}");
            return null;
        }
    }

    // ---- 尺寸 ----
    // 页签坐标**照抄 TONE**（GameSettingMenuPatch.cs:12-15, 71-73）
    private static readonly Vector3 TabBaseLeft = new(-3.9f, -0.4f, 0f);   // TONE ButtonPositionLeft
    private static readonly Vector3 TabBaseRight = new(-2.4f, -0.4f, 0f);  // TONE ButtonPositionRight
    private static readonly Vector3 TabButtonScale = new(0.45f, 0.6f, 1f); // TONE ButtonSize
    private const float TabRowStep = 0.5f;                                 // TONE 每排上升 0.5

    // 图标位（页签右侧，TONE 星星的位置）
    private const float TabIconOffsetX = 1.15f;
    private static readonly Vector2 TabIconSize = new(0.34f, 0.34f);
    private const float TabIconHoverScale = 1.45f;   // 悬浮/选中时的**绝对**缩放（不累乘）

    /// <summary>原版 3 个按钮被挪去的远处坐标（不销毁，避免删组件出 bug）。</summary>
    private static readonly Vector3 VanillaButtonsParkPosition = new(0f, -100f, 0f);

    // ⚠️ 2026-10-06 放大（用户："这个碰撞箱也太'小气'了，按我画的来，
    //    文字放下面小框，放大一点点"）—— 按截图上的红框量的，约 2.33 × 1.42。
    private const float PresetWidth = 2.30f;      // 1.5 → 2.30
    private const float PresetHeight = 1.40f;     // 0.75 → 1.40

    /// <summary>
    /// 图片**下方那条文字**占的高度。
    /// 点击区 = <c>PresetWidth × (PresetHeight + PresetTextStrip)</c> ——
    /// **把文字也盖进去**，不然"文字露在点击区外面"，看着像能点其实点不到（用户要的"文字放下面小框"）。
    /// </summary>
    private const float PresetTextStrip = 0.55f;

    private const float PresetSpacingX = 2.55f;    // 1.7 → 2.55（按钮变宽了，间距必须跟着涨，否则会叠）
    private const float PresetSpacingY = 2.10f;    // 1.0 → 2.10

    // ---- 运行时引用 ----
    private static GameObject? _presetsPage;
    private static GameObject? _modPage;
    private static TextMeshPro? _modPlaceholderText;
    /// <summary>
    /// 当前的 GameSettingMenu 实例。
    /// ⚠️ 原版**没有** <c>GameSettingMenu.Instance</c> 这个静态字段，是我们自己在
    /// <see cref="StartPostfix"/> 里记下来的（TONE 同样做法），供按钮回调使用。
    /// </summary>
    public static GameSettingMenu? Instance { get; private set; }

    /// <summary>当前打开的页签下标。</summary>
    private static int _currentTab = TabMod;

    /// <summary>
    /// 原版 `GameSettingMenu` 实例（`ChangeTabPrefix` 里存下来 ✓）。
    /// ⚠️ 原来没有这个字段 —— 实例只作为 patch 参数出现 ✗，
    ///    所以"每帧自愈"（<see cref="TickTabGuard"/>）拿不到它 ✓ → 必须存一份 ✓
    /// </summary>
    private static GameSettingMenu? _menuInstance;
    /// <summary>8 个页签按钮。</summary>
    private static readonly GameObject?[] _tabButtons = new GameObject?[8];

    // =====================================================================
    //  Harmony Patches
    // =====================================================================

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
    [HarmonyPostfix]
    public static void StartPostfix(GameSettingMenu __instance)
    {
        try
        {
            // ⚠️ 必须自己记住实例：原版 GameSettingMenu 没有静态 Instance 字段，
            // 我们要在按钮回调里用它调 ChangeTab。（TONE 也是自己在 Start 里赋值：
            // TONE\Patches\GameSettingMenuPatch.cs:23,29 `Instance = __instance;`）
            Instance = __instance;

            BuildPages(__instance);

            // 原版 3 个按钮不删，挪到远处（省得删组件出 bug）
            ParkVanillaButtons(__instance);

            // ⚠️ 必须先克隆页签菜单：配置行要铺进真 GameOptionsMenu 才会渲染
            CreateTabMenus(__instance);

            // 左边栏 8 个页签（克隆原版按钮，两列 + 图标位）
            CreateVanillaTabs(__instance);

            // 构建完成后立刻应用一次，确保刚打开的页签不会同时露出原版内容
            ApplyPresetsVisibility();
            ApplyModVisibility();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.StartPostfix]", ex);
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Close))]
    [HarmonyPostfix]
    public static void ClosePostfix()
    {
        // 菜单销毁：清空引用，下次打开重新构建
        _presetsPage = null;
        _modPage = null;
        _modPlaceholderText = null;
        Instance = null;
        for (int i = 0; i < _tabMenus.Length; i++) _tabMenus[i] = null;
        Light.UI.Config.ConfigUIPanel.Clear();
        Light.UI.Config.RoleListPage.Clear();
    }

    /// <summary>预设页启用时：只显示我们的框架页，隐藏原版预设内容。</summary>
    [HarmonyPatch(typeof(GamePresetsTab), nameof(GamePresetsTab.OnEnable))]
    [HarmonyPostfix]
    public static void PresetsOnEnablePostfix()
    {
        ApplyPresetsVisibility();
    }

    /// <summary>MOD 设置页（原版职业设置页）启用时：只显示我们的框架页。</summary>
    [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.OnEnable))]
    [HarmonyPostfix]
    public static void RolesOnEnablePostfix()
    {
        ApplyModVisibility();
    }

    /// <summary>
    /// 原版 OpenMenu/ChangeTab 会走 OpenChancesTab，把 RoleChancesSettings 重新 SetActive(true)，
    /// 因此在其之后再压一次显示状态。
    /// </summary>
    [HarmonyPatch(typeof(RolesSettingsMenu), nameof(RolesSettingsMenu.OpenChancesTab),
        new Type[] { typeof(bool) })]
    [HarmonyPostfix]
    public static void RolesOpenChancesTabPostfix()
    {
        ApplyModVisibility();
    }

    // =====================================================================
    //  显示状态
    // =====================================================================

    private static void ApplyPresetsVisibility()
    {
        try
        {
            if (_presetsPage == null) return;
            var parent = _presetsPage.transform.parent;
            if (parent == null) return;
            HideChildrenExcept(parent, _presetsPage);
            _presetsPage.SetActive(true);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ApplyPresetsVisibility]", ex);
        }
    }

    private static void ApplyModVisibility()
    {
        try
        {
            // ⚠️ 这里**不要**再 SetActive(false) 原版 RoleSettingsTab：
            //    它会触发 RolesSettingsMenu/GameOptionsMenu 的 OnDisable → CloseMenu
            //    → ControllerManager 递归（栈溢出来源）。
            //    原版角色页的隐藏改由"激活我们的克隆菜单"自然盖上，
            //    并在 ClearVanillaContent 里关掉它的背景板。

            if (_modPage == null) return;
            // 只在我们确实要显示时才打开（预设/游戏设置页签下不显示）
            if (_currentTab != TabPreset && _currentTab != TabGameSettings)
            {
                _modPage.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ApplyModVisibility]", ex);
        }
    }

    /// <summary>隐藏 parent 的所有直接子对象，except 例外（保留显示）。</summary>
    private static void HideChildrenExcept(Transform parent, GameObject? except)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;
            if (except != null && child == except.transform) continue;
            child.gameObject.SetActive(false);
        }
    }

    // =====================================================================
    //  第三个主按钮改名
    // =====================================================================

    private static void RenameThirdTabButton(GameSettingMenu menu)
    {
        try
        {
            // 按文本找（中文 / 英文），找不到再按对象名兜底
            var rolesBtn = FindButtonByText(menu, "角色设置")
                ?? FindButtonByText(menu, "Role Setting");
            if (rolesBtn == null)
            {
                var byName = FindChildRecursive(menu.transform, "RoleSettingsButton")
                    ?? FindChildRecursive(menu.transform, "RolesButton")
                    ?? FindChildRecursive(menu.transform, "RoleSettings");
                if (byName != null) rolesBtn = byName.GetComponent<PassiveButton>();
            }

            if (rolesBtn == null)
            {
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到第三个主按钮，跳过改名");
                return;
            }

            SetButtonText(rolesBtn, Language.Translate("gss.tab.mod", "MOD 设置"));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.RenameThirdTabButton]", ex);
        }
    }

    // =====================================================================
    //  框架页构建
    // =====================================================================

    private static void BuildPages(GameSettingMenu menu)
    {
        try
        {
            if (_presetsPage != null || _modPage != null) return;

            // ⚠️ 先把原版 GameOptionsMenu 交给配置面板当"模板源"。
            // 它持有 checkboxOrigin / numberOptionOrigin / stringOptionOrigin / categoryHeaderOrigin
            // 这些私有预制体字段，配置行必须从它们克隆。
            // 不能等 GameOptionsMenu.Initialize：我们的 MOD 页签是 RolesSettingsMenu，
            // 原版 GameSettingsTab 永远不会被打开 → Initialize 不跑 → 模板源永远是 null
            // （表现为"分类头出来了、但一行都没有"）。
            // GameSettingMenu.GameSettingsTab 是**活的实例**，在 Start 时就能拿到（ToN 同做法）。
            try
            {
                var liveMenu = menu.GameSettingsTab;
                if (liveMenu != null)
                {
                    Light.UI.Config.ConfigUIPanel.SetTemplateSource(liveMenu);
                    LightLogger.Log($"[GameSettingMenuPatch] 配置模板源已登记：{liveMenu.name} " +
                                    $"(checkbox={(liveMenu.checkboxOrigin != null)}, " +
                                    $"number={(liveMenu.numberOptionOrigin != null)}, " +
                                    $"string={(liveMenu.stringOptionOrigin != null)}, " +
                                    $"header={(liveMenu.categoryHeaderOrigin != null)})");
                }
                else
                {
                    LightLogger.LogWarning("[GameSettingMenuPatch] GameSettingsTab 为 null，配置行无法克隆");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[GameSettingMenuPatch] 登记配置模板源失败", ex);
            }

            // 预设页容器（原版预设 Tab）
            var presetsTab = menu.transform.Find("PresetsTab")
                ?? FindChildRecursive(menu.transform, "PresetsTab");
            if (presetsTab == null)
            {
                var gpt = menu.GetComponentInChildren<GamePresetsTab>(true);
                if (gpt != null) presetsTab = gpt.transform;
            }
            if (presetsTab != null)
                _presetsPage = BuildPresetsPage(presetsTab);
            else
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到预设页容器");

            // MOD 设置页容器（原版职业设置 Tab）
            var rolesTab = menu.transform.Find("RoleSettingsTab")
                ?? FindChildRecursive(menu.transform, "RoleSettingsTab");
            if (rolesTab == null)
            {
                var rsm = menu.GetComponentInChildren<RolesSettingsMenu>(true);
                if (rsm != null) rolesTab = rsm.transform;
            }
            if (rolesTab != null)
            {
                _modPage = BuildModPage(rolesTab);
                ShowTabConfig(0);   // 默认显示第一个标签内容（须在 _modPage 赋值之后）
            }
            else
                LightLogger.LogWarning("[GameSettingMenuPatch] 未找到 MOD 设置页容器");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.BuildPages]", ex);
        }
    }

    // ---------------------------------------------------------------------
    //  预设页：4 按钮框架
    // ---------------------------------------------------------------------

    private static GameObject BuildPresetsPage(Transform parent)
    {
        LoadTabAndPresetAssets();

        var page = NewUIObject("LightPresetsPage", parent, new Vector3(0f, 0.1f, -2.5f));

        for (int i = 0; i < PresetLabels.Length; i++)
        {
            int row = i / 2;
            int col = i % 2;
            var pos = new Vector2((col - 0.5f) * PresetSpacingX, (0.5f - row) * PresetSpacingY);
            CreatePresetButton(page.transform, PresetLabels[i], pos, i);
        }

        return page;
    }

    /// <summary>
    /// 预设页按钮：每个按钮用自己的常态图 + 高光图（4 组共 8 张），
    /// 资源缺失时退回暗色底 + 下方文字；点击本轮无效果。
    /// </summary>
    private static void CreatePresetButton(Transform parent, string label, Vector2 pos, int index)
    {
        var normal = index < _presetNormal.Length ? _presetNormal[index] : null;
        var hover = index < _presetHover.Length ? _presetHover[index] : null;

        var go = NewUIObject($"LightPresetButton_{label}", parent, new Vector3(pos.x, pos.y, 0f));

        // 图片槽位。
        //   ⚠️ 往上抬 `PresetTextStrip/2`，让"图片 + 下方文字"整体**居中于点击区**
        //      （点击区是以 go 为中心、高 PresetHeight + PresetTextStrip 的整块）。
        var img = NewUIObject("Image", go.transform, new Vector3(0f, PresetTextStrip * 0.5f, 0f));
        var imgSr = img.AddComponent<SpriteRenderer>();
        imgSr.sprite = normal;                                   // 资源未提供 → null
        imgSr.drawMode = SpriteDrawMode.Sliced;
        imgSr.size = new Vector2(PresetWidth, PresetHeight);
        imgSr.color = normal != null
            ? UColor.white
            : new UColor(0.15f, 0.15f, 0.15f, 0.8f);

        // 下方文字：走统一模板（与主界面"本地/在线"卡片同字体 + 辉光白）
        // 有图时文字压在图片下缘内，省出纵向空间；无图时也贴着图片下缘（都在点击区里）
        float textY = -PresetHeight * 0.5f + PresetTextStrip * 0.5f - 0.16f;
        {
            var tabTmp = MenuTextTemplate.Create(go.transform, new Vector3(0f, textY, -0.1f), label, 1.1f);
            // 用户 2026-10-06：页签文字也要 Bold（AGENTS.md §12.1）
            if (tabTmp != null)
            {
                tabTmp.fontStyle = FontStyles.Bold;
                tabTmp.ForceMeshUpdate();
            }
        }

        // 点击区域 + PassiveButton
        AddButtonArea(go, PresetWidth, PresetHeight + PresetTextStrip);

        // ★ 调试：没图的时候把**可点击区域**画出来，方便对位。
        //   只画在 normal == null（没图）的按钮上；有图了就不会画。
        if (DebugShowPresetClickArea && normal == null)
            DrawClickAreaOutline(go.transform, PresetWidth, PresetHeight + PresetTextStrip);

        var pb = go.SetUpButton(true, null, null, null, false);
        // ⚠️ `go` 是我们自己 new 的，不是原版克隆体，所以这里 **AddListener 就够**，
        //    不需要像克隆行那样先 `OnClick = new(...)` 清空（§4.5 针对的是克隆控件）。
        pb.OnClick.AddListener((UnityAction)(() => OnPresetButton(index)));
        pb.OnMouseOver.AddListener((UnityAction)(() =>
        {
            if (hover != null) imgSr.sprite = hover;
        }));
        pb.OnMouseOut.AddListener((UnityAction)(() =>
        {
            if (normal != null) imgSr.sprite = normal;
        }));
    }

    // ---------------------------------------------------------------------
    //  预设按钮"可点击区域"调试描边（用户 2026-10-06 要的临时功能）
    //
    //  用途：那 4 张按钮图还没画，按钮只有一块暗色底 + 文字，
    //        看不出**鼠标到底该点哪里**。这里把真正的点击范围用亮线画出来。
    //  关掉：改成 false（或以后有图了这条分支自然就不会走 —— 只画 normal == null 的）。
    // ---------------------------------------------------------------------

    /// <summary>是否给"没图的"预设按钮画可点击区域描边。<b>纯调试用，出厂应设 false。</b></summary>
    private const bool DebugShowPresetClickArea = true;

    /// <summary>描边颜色（随便挑的洋红，最显眼）。</summary>
    private static readonly UColor DebugClickAreaColor = new(1f, 0f, 1f, 0.95f);

    /// <summary>描边线宽（世界单位）。</summary>
    private const float DebugClickAreaThickness = 0.035f;

    /// <summary>自建的 1×1 白图 —— ⚠️ 必须自己持有引用并打 DontUnloadUnusedAsset（§4.6）。</summary>
    private static Sprite? _dbgWhite;

    private static Sprite? GetDbgWhite()
    {
        // ⚠️ 用 `!= null` 而不是 `??` —— Unity 的假 null 识别不了（§4.6.1）
        if (_dbgWhite != null) return _dbgWhite;

        try
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, UColor.white);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            _dbgWhite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            _dbgWhite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.GetDbgWhite] {ex.Message}");
        }
        return _dbgWhite;
    }

    /// <summary>在 parent 下画一个 w×h 的矩形描边（四条细长条，铺在内容之上 z 更小）。</summary>
    private static void DrawClickAreaOutline(Transform parent, float w, float h)
    {
        try
        {
            const float z = -0.3f;      // 同容器内 z 越小越靠前 → 盖在暗色底和文字之上
            float t = DebugClickAreaThickness;
            float hw = w * 0.5f, hh = h * 0.5f;

            DbgBar(parent, "DbgClickTop", new Vector3(0f, hh, z), new Vector2(w + t, t));
            DbgBar(parent, "DbgClickBottom", new Vector3(0f, -hh, z), new Vector2(w + t, t));
            DbgBar(parent, "DbgClickLeft", new Vector3(-hw, 0f, z), new Vector2(t, h + t));
            DbgBar(parent, "DbgClickRight", new Vector3(hw, 0f, z), new Vector2(t, h + t));

            LightLogger.Log($"[预设调试] 已画出可点击区域描边 {w}×{h}（{parent.name}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.DrawClickAreaOutline] {ex.Message}");
        }
    }

    private static void DbgBar(Transform parent, string name, Vector3 pos, Vector2 size)
    {
        var go = NewUIObject(name, parent, pos);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetDbgWhite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = DebugClickAreaColor;
    }

    // ---------------------------------------------------------------------
    //  左边栏：8 个页签（克隆原版按钮样式，两列排布）
    //
    //  借鉴 TONE(Town of Next) 的做法：克隆原版 GameSettingsButton 当模板，
    //  替换 OnClick 后用 GameSettingMenu.ChangeTab(index, false) 切页，
    //  而不是自己 SetActive 各页 —— 让原版负责页签互斥、遮罩与描述文字。
    //  参考：TONE\Patches\GameSettingMenuPatch.cs 的 StartPostfix / ChangeTabPrefix
    // ---------------------------------------------------------------------

    /// <summary>克隆原版按钮得到"页签模板"（TONE 同款做法）。</summary>
    private static PassiveButton? _tabButtonTemplate;

    private static PassiveButton? GetTabButtonTemplate(GameSettingMenu menu)
    {
        if (_tabButtonTemplate != null) return _tabButtonTemplate;
        try
        {
            var src = menu.GameSettingsButton;
            if (src == null) return null;

            // 克隆一个挂在同一父级下、隐藏起来的模板
            var clone = Object.Instantiate(src, src.transform.parent);
            clone.gameObject.name = "LightTabButtonTemplate";
            clone.gameObject.SetActive(false);
            _tabButtonTemplate = clone;
            return clone;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.GetTabButtonTemplate]", ex);
            return null;
        }
    }

    private static GameObject BuildModPage(Transform parent)
    {
        LoadTabAndPresetAssets();

        // ⚠️ 不能挂在 RoleSettingsTab 下：切到 MOD 类页签时 ChangeTabPrefix 会把
        //    RoleSettingsTab 整体 SetActive(false)（不像预设页会打开 PresetsTab），
        //    挂在其下的占位文字/职业按钮列表会被连带隐藏 → 用户"看不到职业"。
        //    改挂到其父级（与各克隆菜单同级，切页签不会被关），并换算世界变换，
        //    保证页内元素视觉位置不变。
        var root = parent.parent != null ? parent.parent : parent;
        var page = NewUIObject("LightModSettingsPage", root, Vector3.zero);
        page.transform.position = parent.TransformPoint(new Vector3(0f, 1.2f, -2.5f));
        page.transform.rotation = parent.rotation;
        page.transform.localScale = parent.localScale;

        // 占位提示（无内容分类显示「XX页签暂未实现。」）
        _modPlaceholderText = CloneText(page.transform, new Vector3(0f, -0.8f, -0.1f), "", 1.5f);

        return page;
    }

    /// <summary>
    /// 在左边栏创建 8 个页签按钮（克隆原版按钮），每个右侧带一个小图标位。
    ///
    /// 图标三态（按用户要求）：
    ///   · 常态      → 未选中图，原始大小
    ///   · 鼠标悬浮  → **仍未选中图**，但放大
    ///   · 已选中    → 选中图，保持放大
    /// 放大用固定的"放大后尺寸"，**不会累积**（反复点击也只是一直维持放大态）。
    /// </summary>
    private static void CreateVanillaTabs(GameSettingMenu menu)
    {
        try
        {
            var template = GetTabButtonTemplate(menu);
            if (template == null)
            {
                LightLogger.LogWarning("[GameSettingMenuPatch] 未取得页签按钮模板，跳过建页签");
                return;
            }

            var parent = menu.GameSettingsButton.transform.parent;

            for (int i = 0; i < VanillaTabs.Length; i++)
            {
                var (key, cn, column, row) = VanillaTabs[i];
                int idx = i;

                var btn = Object.Instantiate(template, parent);
                btn.gameObject.name = $"LightTab_{key}";
                btn.gameObject.SetActive(true);
                _tabButtons[i] = btn.gameObject;

                // 位置：两列。左列 column=0，右列 column=1；行从上到下。
                var pos = TabPosition(column, row);
                btn.transform.localPosition = new Vector3(pos.x, pos.y, -2f);
                btn.transform.localScale = TabButtonScale;

                // 文字：原版按钮自带 TMP → 换成**辉光白那套字体**（用户要求）
                var label = btn.GetComponentInChildren<TextMeshPro>();
                if (label != null)
                {
                    var tr = label.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;

                    // ⚠️⚠️ 字体换成 **NotoSansSC**（= 职业配置按钮那个字体）。
                    //     用户 2026-10-06："那几个页签用的字体改成我们职业配置按钮的那个什么 nano 字体"。
                    //     原来用 `MenuTextTemplate.MenuFont`（主界面卡片字体），不是一套。
                    //     ⚠️ 换字体**必须同时换材质**（TMP 字形从字体自己的图集材质取），
                    //        所以走 ApplyFont，不要直接写 label.font（见 AGENTS.md §12.2）。
                    try { MenuTextTemplate2.ApplyFont(label); } catch { }

                    label.text = cn;

                    // ⚠️⚠️ **不要再写 FontStyles.UpperCase** —— 它是**整体替换**字重，
                    //     会把 Bold 直接冲掉。用户反馈的"加粗我也没看见"就是这个原因。
                    //     中文也没有大小写，UpperCase 本来就没意义。
                    label.fontStyle = FontStyles.Bold;
                    label.color = RoleTabTextColor(i);   // 职业页签用其原本的描边色
                    label.outlineWidth = 0.17f;          // 与 ToN 的分类头一致
                }

                // ⚠️ AGENTS.md §4.5：克隆原版控件必须**整体替换** OnClick
                btn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                btn.OnClick.AddListener((UnityAction)(() => OnVanillaTabClicked(idx)));

                // 图标位（在页签旁边，即 TONE 那个"星星"的位置）
                CreateTabIcon(btn, i);

                // FS 主界面风格的配色 + 淡金色边框（用户要求）
                ApplyFsButtonStyle(btn, RoleTabTextColor(i));

                // ⚠️ 原版按钮的**高光**来自 PassiveButton 的 activeSprites + SelectButton(true)，
                //    不是 OnMouseOver。我们替换 OnClick 时不会破坏它，但必须自己调用
                //    SelectButton 来驱动，否则页签永远停在 inactive（= 没有高光）。
                //    OnMouseOver/OnMouseOut 是**追加**监听（原版的视觉逻辑仍在），
                //    这里只额外驱动我们自己的图标状态。
                btn.OnMouseOver.AddListener((UnityAction)(() => SetTabIconState(idx, hover: true)));
                btn.OnMouseOut.AddListener((UnityAction)(() => SetTabIconState(idx, hover: false)));
            }

            // 默认选中 MOD 页签（显示高光）
            RefreshTabSelection();
            LightLogger.Log($"[GameSettingMenuPatch] 已创建 {VanillaTabs.Length} 个页签");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.CreateVanillaTabs]", ex);
        }
    }

    /// <summary>
    /// 刷新所有页签的"选中高光"：只有当前页签调 SelectButton(true)，其余 false。
    /// 原版 GameSettingMenu.ChangeTab 只对**它自己的**三个按钮做这件事，
    /// 我们克隆来的 8 个按钮得自己维护。
    /// </summary>
    private static void RefreshTabSelection()
    {
        try
        {
            for (int i = 0; i < VanillaTabs.Length && i < _tabButtons.Length; i++)
            {
                var go = _tabButtons[i];
                if (go == null) continue;
                var pb = go.GetComponent<PassiveButton>();
                if (pb == null) continue;

                bool selected = i == _currentTab;
                pb.SelectButton(selected);

                // 顺带把可交互状态打开（避免克隆体因原状态被禁用而不响应）
                pb.SetButtonEnableState(true);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.RefreshTabSelection] {ex.Message}");
        }
    }

    /// <summary>
    /// 把页签按钮做成 **FS(Final Suspect) 主界面按钮**那种样式，并加**淡金色边框**。
    ///
    /// 【借鉴 FS】FS 的做法（FinalSuspect\Patches\System\MainMenuManagerPatch.cs:128-130）：
    ///   克隆主界面的 PlayButton，然后给 PassiveButton 的三套 sprite 上色 ——
    ///     inactiveSprites → 暗一档（常态）
    ///     activeSprites   → 亮一档（悬浮/选中）
    ///   而不是自己画按钮底图。这样形状/圆角/阴影天然与原版一致。
    ///
    /// 我们在此之上再补一条**淡金色边框**（用户要求），做法是给按钮对象加一个
    /// 描边用的 SpriteRenderer（用原版圆角九宫格图拉伸，叠在按钮底图稍后一层）。
    /// </summary>
    private static void ApplyFsButtonStyle(PassiveButton btn, UColor accent)
    {
        try
        {
            // FS 那种"常态暗、悬浮亮"的两档底色（FinalSuspect\MainMenuManagerPatch.cs:128-130）
            // 用户已明确：**不要**金色描边、**不要**职业色细线（太丑）。
            // 只保留这套底色，样式交给原版按钮自己的形状。
            var baseDark = new UColor(0.13f, 0.13f, 0.15f, 0.92f);
            var baseLite = new UColor(0.24f, 0.24f, 0.28f, 0.98f);

            var inSr = btn.inactiveSprites != null ? btn.inactiveSprites.GetComponent<SpriteRenderer>() : null;
            if (inSr != null) inSr.color = baseDark;

            var actSr = btn.activeSprites != null ? btn.activeSprites.GetComponent<SpriteRenderer>() : null;
            if (actSr != null) actSr.color = baseLite;

            var selSr = btn.selectedSprites != null ? btn.selectedSprites.GetComponent<SpriteRenderer>() : null;
            if (selSr != null) selSr.color = baseLite;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.ApplyFsButtonStyle] {ex.Message}");
        }
    }

    /// <summary>
    /// 页签坐标，**照抄 TONE 的公式**（GameSettingMenuPatch.cs:71-72）。
    ///
    /// TONE 原式对 t=0..7 产出这一组坐标（左右交错、逐排上升 0.5）：
    ///   t=0 (-2.4,-0.4)  t=1 (-3.9,-0.9)  t=2 (-2.4,-0.9)  t=3 (-3.9,-1.4)
    ///   t=4 (-2.4,-1.4)  t=5 (-3.9,-1.9)  t=6 (-2.4,-1.9)  t=7 (-3.9,-2.4)
    ///
    /// ⚠️ 不能用 <c>0.5 * ((level+1)/2)</c> 这种"再推导一次"的写法：
    ///   整数除法在相邻两个 level 上会算出同一个 y（-0.9 出现两次）→ 两个页签重叠。
    ///   所以直接把 TONE 的结果按"列 + 行"列成表。
    ///
    /// 映射（用户指定顺序）：
    ///   左列 x=-3.9，从上到下：预设 / 游戏设置 / MOD / 幽灵
    ///   右列 x=-2.4，从上到下：船员 / 内鬼 / 中立 / 附加
    /// </summary>
    private static Vector2 TabPosition(int column, int row)
    {
        // 每列 4 个，y 从高到低（-0.4 最上、-1.9 最下）
        float y = -0.4f - row * TabRowStep;
        return new Vector2(column == 0 ? TabBaseLeft.x : TabBaseRight.x, y);
    }

    /// <summary>
    /// 处理原版 3 个页签按钮 —— **照抄 TONE 的 SetDefaultButton**
    /// （TONE\Patches\GameSettingMenuPatch.cs:106-111）。
    ///
    /// TONE 的做法很干脆：
    ///   · GamePresetsButton  → **SetActive(false)** 直接隐藏
    ///   · GameSettingsButton → 保留（作为"原版游戏设置"页签的原生入口），挪到左上角
    ///   · RoleSettingsButton → 保留但也被我们自己的页签体系覆盖
    ///
    /// 我之前的做法是"挪到 (0,-100,0) 藏起来"，问题是一旦用户从别的页签切回 MOD，
    /// 原版逻辑又会把它们的 SetActive/位置恢复，于是原版界面又叠上来
    /// （用户反馈"别的页签点回来MOD就会继续叠"）。
    /// 按 TONE 这样**直接 SetActive(false)** 才是稳的。
    /// </summary>
    private static void ParkVanillaButtons(GameSettingMenu menu)
    {
        try
        {
            // 预设按钮：TONE 直接隐藏（GameSettingMenuPatch.cs:108）
            SetActiveSafe(menu.GamePresetsButton?.gameObject, false);

            // 游戏设置按钮：TONE **保留并重定位**（GameSettingMenuPatch.cs:110-111,143-150）。
            // 之前我以为它"回来了"是 bug，其实 TONE 本来就留着它 ——
            // 关键是下面要把 ControllerSelectable 重建，否则 ControllerManager
            // 会拿着旧列表反复开合 → 递归。
            var gs = menu.GameSettingsButton;
            if (gs != null)
            {
                foreach (var ap in gs.GetComponents<AspectPosition>())
                    if (ap != null) ap.enabled = false;

                // ⚠️ 用户 2026-10-06："预设按钮下面有个原版的'游戏设置'，别删他，给他移到屏幕外面。"
                //    TONE 是把它重定位到 TabBaseLeft（和我们自己的页签同一处）——
                //    于是它就**藏在我们「预设」页签底下**，用户看到的就是那个。
                //    这里改成移到屏幕外（AspectPosition 上面已经禁掉了，不会自己跑回来）。
                gs.transform.localPosition = new Vector3(0f, -200f, 0f);
                gs.transform.localScale = TabButtonScale;        // TONE: ButtonSize
            }

            // 角色设置按钮（原版那个）：TONE 明确隐藏（GameSettingMenuPatch.cs:146）
            SetActiveSafe(menu.RoleSettingsButton?.gameObject, false);

            // ⚠️ 关键（TONE GameSettingMenuPatch.cs:148-150）：
            //    清空并重建 ControllerSelectable，只留一个默认按钮。
            //    这是 ControllerManager 递归的第二个来源 —— 旧的 selectable 列表里
            //    还引用着已被我们隐藏/替换的对象，手柄导航会反复开合菜单。
            if (gs != null)
            {
                menu.DefaultButtonSelected = gs;
                menu.ControllerSelectable = new Il2CppSystem.Collections.Generic.List<UiElement>();
                menu.ControllerSelectable.Add(gs);
            }

            LightLogger.Log("[GameSettingMenuPatch] 原版按钮已处理 + ControllerSelectable 已重建");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ParkVanillaButtons]", ex);
        }
    }

    // =====================================================================
    //  【核心 · 照抄 TONE】接管 GameSettingMenu.ChangeTab
    //
    //  这是栈溢出的**真正解法**。之前我一直让原版 ChangeTab 跑，只在外围打补丁，
    //  但原版 ChangeTab 会走：
    //      GameOptionsMenu.OpenMenu() / OnDisable→CloseMenu()
    //      → ControllerManager.OpenOverlayMenu/CloseOverlayMenu
    //      → 再次操作菜单状态 → 递归 → 栈溢出
    //  日志实测 OpenTopmostMenu 40 次、CloseOverlayMenu 73 次就是这个。
    //
    //  TONE 的做法（TONE\Patches\GameSettingMenuPatch.cs:325-411）：
    //    对 ChangeTab 打 **HarmonyPrefix 并 return false** ——
    //    原版整个方法不执行，TONE 自己用纯 SetActive 完成"切页签"，
    //    于是 ControllerManager 完全不会被牵扯进来，**不可能递归**。
    //
    //  ⚠️ 这正是我前几轮反复失败的原因：我在外围加补丁，却让病根继续运行。
    // =====================================================================

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.ChangeTab))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static bool ChangeTabPrefix(GameSettingMenu __instance, ref int tabNum,
        [HarmonyArgument(1)] bool previewOnly)
    {
        try
        {
            // 只接管"真正的切换"；手柄预览态仍交回原版（TONE 也保留 previewOnly 分支）
            if (previewOnly) return true;

            _currentTab = tabNum;
            _menuInstance = __instance;      // ★ 存下来给"每帧自愈"用（TickTabGuard ✓）

            // ① 关掉所有内容页：原版三个 + 我们克隆的
            SetActiveSafe(__instance.PresetsTab?.gameObject, false);
            SetActiveSafe(__instance.GameSettingsTab?.gameObject, false);
            SetActiveSafe(__instance.RoleSettingsTab?.gameObject, false);

            for (int i = 0; i < _tabMenus.Length; i++)
                SetActiveSafe(_tabMenus[i]?.gameObject, false);

            // ② 原版三按钮的高光全部熄灭；我们的 8 个按钮由 RefreshTabSelection 处理
            __instance.GamePresetsButton?.SelectButton(false);
            __instance.GameSettingsButton?.SelectButton(false);
            __instance.RoleSettingsButton?.SelectButton(false);

            // ③ 按页签号打开对应页
            switch (tabNum)
            {
                case TabPreset:
                    SetActiveSafe(__instance.PresetsTab?.gameObject, true);
                    break;

                case TabGameSettings:
                    SetActiveSafe(__instance.GameSettingsTab?.gameObject, true);
                    RefreshVanillaGameSettings(__instance);
                    break;

                default:
                    // 其余 6 个都是我们克隆的 GameOptionsMenu
                    var target = tabNum >= 0 && tabNum < _tabMenus.Length ? _tabMenus[tabNum] : null;
                    if (target != null)
                    {
                        SetActiveSafe(target.gameObject, true);
                        Light.UI.Config.ConfigUIPanel.SetHostMenu(target);

                        // ⚠️ 每次切页签都要清一次原版内容（不能只依赖 Build）：
                        //    克隆菜单每次 SetActive(true) 都会走 OnEnable→Initialize，
                        //    原版内容（分类头"伪装者"/"任务"、地图预览、原版设置行）可能回来。
                        //    用户反馈"header还是会叠""其他页签还是有游戏设置"就是这个。
                        Light.UI.Config.ConfigUIPanel.CleanMenu(target);

                        // ⚠️ 原版"伪装者/任务"那些分类头来自原版职业设置页的内容。
                        //    我们用的是克隆菜单，原版 ROLES TAB 必须保持关闭。
                        SetActiveSafe(__instance.RoleSettingsTab?.gameObject, false);
                    }
                    break;
            }

            // ④ 遮罩：左边亮、右边暗（照 TONE）
            __instance.ToggleLeftSideDarkener(true);
            __instance.ToggleRightSideDarkener(false);

            // ⑤ 我们自己的内容与图标状态
            ShowTabConfig(tabNum);
            RefreshAllTabIcons();
            RefreshTabSelection();

            return false;   // ⚠️ 关键：原版 ChangeTab 不执行
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ChangeTabPrefix]", ex);
            return true;    // 出错就交回原版，至少不把界面搞死
        }
    }

    /// <summary>
    /// **每帧自愈：把"不该显示的另一页签"压回去** ✓✓
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么必须有这个】（2026-10-10 用户连报两条 ✓）
    ///   · "职业页签还没进详情页就外泄**游戏设置**的内容" ✗
    ///   · "调整职业数量时外泄出**模组设置**的内容" ✗
    ///
    /// `ChangeTabPrefix` 里**已经**做过互斥显隐（关掉三个原版页签 + 全部克隆菜单 ✓），
    /// 但只做**一次**不够 ✗ —— 工程注释里早就记着原因：
    ///   "原版 `OpenMenu/ChangeTab` 会走 `OpenChancesTab`，
    ///     把 `RoleChancesSettings` **重新 `SetActive(true)`**"
    /// 也就是：原版在我们之后又把某个页签**点亮** ✗ → 它的内容就留在别人的页上 ✓✓
    ///
    /// 【做法】状态差量：**只在真的发现"不该亮的页签亮着"时才写** ✓
    ///   由 `ConfigUIPanel.Refresh`（每帧都会跑 ✓）调用 ✓
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    internal static void TickTabGuard()
    {
        try
        {
            var menu = _menuInstance;
            if (menu == null) return;

            // 只在"当前停在 MOD 页签"时守护 ✓ —— 原版页签当然要让它亮着 ✓
            if (!Light.UI.Config.ConfigUIPanel.ModTabActive) return;

            int fixedCount = 0;

            // ★★ 2026-10-10 用户："跟没修一样" —— 前三轮我都在猜"该关掉谁" ✗，全猜错 ✗✗
            //    日志给出了关键事实（配置行的真实路径）：
            //      Main Camera/PlayerOptionsMenu(Clone)/**MainArea**/LightModSettingsPage/LightConfigPage/…
            //    → 我们的页和原版页签**都在 `MainArea` 下** ✓
            //    → 那就不必知道"具体是哪个对象"了 ✓：**在 MOD 页签上，`MainArea` 里除我们的页之外一律压住** ✓✓
            //      （原版 `OpenMenu/OpenChancesTab` 事后点亮谁都没用 ✓ 每帧压一次 ✓）
            //    ⚠️ 压的时候走 `ConfigUIPanel.RememberHiddenForRestore` ✓
            //       —— 这样切回原版页签时 `RestoreVanillaState()` 能把它们**原样放回来** ✓（不然又是空白 ✗）
            fixedCount += SuppressNonModPagesUnderMainArea(menu);

            // ① 三个原版页签一个都不该亮 ✓（我们有自己的 6 个克隆菜单 ✓）
            fixedCount += HideIfActive(menu.PresetsTab?.gameObject, "PresetsTab");
            fixedCount += HideIfActive(menu.GameSettingsTab?.gameObject, "GameSettingsTab");
            fixedCount += HideIfActive(menu.RoleSettingsTab?.gameObject, "RoleSettingsTab");

            // ② 其它克隆菜单也不该亮 ✓（只留当前那个 ✓）
            var cur = _currentTab >= 0 && _currentTab < _tabMenus.Length ? _tabMenus[_currentTab] : null;
            for (int i = 0; i < _tabMenus.Length; i++)
            {
                var m = _tabMenus[i];
                if (m == null || m == cur) continue;
                fixedCount += HideIfActive(m.gameObject, $"克隆菜单[{i}]");
            }

            if (fixedCount > 0)
                LightLogger.Log($"[GameSettingMenuPatch] 压回了 {fixedCount} 个「不该亮」的页签 ✓" +
                                "（原版 OpenMenu/OpenChancesTab 会把它重新 SetActive(true) ✗）");
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[GameSettingMenuPatch.TickTabGuard] {ex.Message}");
        }
    }

    /// <summary>
    /// **压住 `MainArea` 里"不是我们的"那些页**（状态差量 ✓）。
    ///
    /// ⚠️ 为什么要这么"粗"：用户连报三条外泄（游戏设置 / 模组设置 / 角色设置的内容互相串 ✗），
    ///    我按"点名关掉某个字段"改了**三轮全错** ✗✗ —— 因为原版在我们之后
    ///    （`OpenMenu` / `OpenChancesTab` / `OnEnable→Initialize`）会把别的东西点亮 ✓
    ///    → 与其猜名字 ✗，不如**只留我们自己的页** ✓✓
    ///
    /// ⚠️ 只认 `_modPage` 那一支 ✓（它就是 `LightModSettingsPage` ✓，日志里的真实路径实证 ✓）
    /// ⚠️ 压的时候**必须**走 `ConfigUIPanel.RememberHiddenForRestore` ✓
    ///    —— 切回原版页签时 `RestoreVanillaState()` 才放得回来 ✓（否则"游戏设置"又是空白 ✗）
    /// </summary>
    private static int SuppressNonModPagesUnderMainArea(GameSettingMenu menu)
    {
        int n = 0;
        try
        {
            if (_modPage == null) return 0;

            var mainArea = menu.transform != null ? menu.transform.Find("MainArea") : null;
            if (mainArea == null) return 0;

            // ⚠️⚠️ 2026-10-10 第二轮修正（用户："游戏设置内容又没了" ✗）：
            //    上一版是**广谱压制** —— `MainArea` 下除我们页之外一律关掉 ✗
            //    → 连**原版页签**都一起压了 → "游戏设置"又空了 ✓
            //    而日志这次给出了**精准名单** ✓✓：
            //      压住了 MainArea 下的 'LightTabMenu_Crewmate' ✓
            //      压住了 MainArea 下的 'LightTabMenu_Mod'      ✓
            //    → 真正漏出来的是**我们自己克隆的那 6 个页签菜单**（`LightTabMenu_*` ✓）——
            //      它们并排躺在 `MainArea` 下 ✓，离开时没关 ✗ →
            //      里面**克隆自带的原版行**（伪装者数/击杀冷却/视野/范围）就露出来了 ✓✓
            //    → 所以：**只压 `LightTabMenu_*`，而且只压"不是当前页签"的那个** ✓✓
            //      （原版 `PresetsTab`/`GameSettingsTab`/`RoleSettingsTab` 一律**不碰** ✓
            //        这样"游戏设置"永远不会被我们关掉 ✓）
            var cur = _currentTab >= 0 && _currentTab < _tabMenus.Length ? _tabMenus[_currentTab] : null;

            for (int i = 0; i < mainArea.childCount; i++)
            {
                var child = mainArea.GetChild(i);
                if (child == null) continue;

                // ★ 只认我们自己克隆的页签菜单 ✓（原版页签名字不是这个前缀 ✓ → 绝不误伤 ✓）
                if (!child.name.StartsWith("LightTabMenu_", StringComparison.Ordinal)) continue;

                // 当前页签那一份要留着用 ✓
                if (cur != null && child.gameObject == cur.gameObject) continue;

                if (!child.gameObject.activeSelf) continue;

                Light.UI.Config.ConfigUIPanel.RememberHiddenForRestore(child.gameObject);
                child.gameObject.SetActive(false);
                n++;

                if (_suppressLogs < 8)
                {
                    _suppressLogs++;
                    LightLogger.Log($"[GameSettingMenuPatch] 关掉了没在用的页签菜单 '{child.name}' ✓" +
                                    "（它里面克隆自带的原版行会漏到别的页上 ✗）");
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogDebug($"[GameSettingMenuPatch.SuppressNonModPagesUnderMainArea] {ex.Message}");
        }
        return n;
    }

    private static int _suppressLogs;

    /// <summary>`GameSettingMenuPatch` 外部（`ConfigUIPanel.CleanCurrentHost` ✓）触发的"立刻压一次"</summary>
    internal static void SuppressNonModPagesNow()
    {
        try
        {
            var menu = _menuInstance;
            if (menu == null) return;
            SuppressNonModPagesUnderMainArea(menu);
        }
        catch { }
    }

    /// <summary>亮着就关掉，返回 1 表示确实关了一个 ✓（状态差量用）</summary>
    private static int HideIfActive(GameObject? go, string what)
    {
        try
        {
            if (go == null) return 0;
            if (!go.activeSelf) return 0;
            go.SetActive(false);
            LightLogger.LogDebug($"[GameSettingMenuPatch] 关掉了不该显示的 '{what}' ✓");
            return 1;
        }
        catch { return 0; }
    }

    /// <summary>SetActive 的安全封装（null 不抛）。</summary>
    private static void SetActiveSafe(GameObject? go, bool active)
    {
        try
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
        catch { }
    }

    /// <summary>
    /// 原版"游戏设置"页第一次打开时要铺一次行
    /// （克隆体不会自动跑，原版那次 Initialize 我们要主动补）。
    /// </summary>
    private static void RefreshVanillaGameSettings(GameSettingMenu menu)
    {
        try
        {
            var tab = menu.GameSettingsTab;
            if (tab == null) return;

            var children = tab.Children;
            if (children == null || children.Count == 0)
            {
                tab.CreateSettings();
                LightLogger.Log("[GameSettingMenuPatch] 已为原版游戏设置页补铺一次行");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.RefreshVanillaGameSettings] {ex.Message}");
        }
    }

    /// <summary>每个 MOD 类页签对应的克隆 GameOptionsMenu（下标同 VanillaTabs）。</summary>
    private static readonly GameOptionsMenu?[] _tabMenus = new GameOptionsMenu?[8];

    /// <summary>
    /// 为 6 个 MOD 类页签各克隆一个 GameOptionsMenu（挂在原版 GameSettingsTab 的父级下）。
    /// 克隆体默认隐藏，切页签时只激活一个。
    /// </summary>
    private static void CreateTabMenus(GameSettingMenu menu)
    {
        try
        {
            var source = menu.GameSettingsTab;
            if (source == null)
            {
                LightLogger.LogWarning("[GameSettingMenuPatch] GameSettingsTab 为 null，无法克隆页签菜单");
                return;
            }

            var parent = source.transform.parent;
            int made = 0;

            for (int i = 0; i < VanillaTabs.Length; i++)
            {
                // 预设 / 游戏设置用原版页，不克隆
                if (i == TabPreset || i == TabGameSettings) continue;

                var clone = Object.Instantiate(source, parent);
                clone.gameObject.name = $"LightTabMenu_{VanillaTabs[i].Key}";
                clone.gameObject.SetActive(false);

                // ⚠️ 登记为"我们的菜单"：OpenMenu/CloseMenu 会被拦掉，
                //    否则 SetActive 切换会经 OnDisable/OnEnable 触发
                //    ControllerManager 递归 → 栈溢出。
                Light.UI.Config.ConfigRowPatches.RegisterOurMenu(clone);

                _tabMenus[i] = clone;
                made++;
            }

            LightLogger.Log($"[GameSettingMenuPatch] 已为 {made} 个 MOD 页签克隆 GameOptionsMenu");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.CreateTabMenus]", ex);
        }
    }

    /// <summary>
    /// 激活某个 MOD 页签的克隆菜单，其余全部关闭（照 TONE 的 ChangeTab 逻辑）。
    /// 返回被激活的 menu（供配置面板登记为宿主）。
    /// </summary>
    private static GameOptionsMenu? ActivateTabMenu(int index)
    {
        try
        {
            // 先全关
            for (int i = 0; i < _tabMenus.Length; i++)
            {
                var m = _tabMenus[i];
                if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
            }

            if (index == TabPreset || index == TabGameSettings) return null;

            var target = index < _tabMenus.Length ? _tabMenus[index] : null;
            if (target == null) return null;

            // 打开前先把旧行清掉，让原版 CreateSettings 重新铺
            Light.UI.Config.ConfigUIPanel.ClearRowsOnly();

            target.gameObject.SetActive(true);
            LightLogger.Log($"[GameSettingMenuPatch] 已激活页签菜单 {target.name}");
            return target;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.ActivateTabMenu]", ex);
            return null;
        }
    }

    /// <summary>给页签按钮创建一个图标位（TONE 星星的位置），并登记以便切状态。</summary>
    private static void CreateTabIcon(PassiveButton btn, int index)
    {
        try
        {
            var go = NewUIObject($"LightTabIcon_{VanillaTabs[index].Key}", btn.transform,
                new Vector3(TabIconOffsetX, 0f, -0.1f));

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _tabIconNormal[index];       // 可能为 null（美术还没画）→ 空槽，不崩
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = TabIconSize;
            sr.color = sr.sprite != null ? UColor.white : new UColor(1f, 1f, 1f, 0f);  // 无图时全透明

            _tabIcons[index] = new TabIcon { Renderer = sr, Go = go };
            ApplyTabIconState(index, selected: index == _currentTab, hover: false);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.CreateTabIcon] {ex.Message}");
        }
    }

    /// <summary>页签图标的运行时引用。</summary>
    private class TabIcon
    {
        public SpriteRenderer? Renderer;
        public GameObject? Go;
    }

    private static readonly TabIcon?[] _tabIcons = new TabIcon?[8];

    /// <summary>
    /// 设置某个页签图标的视觉状态。
    /// 选中 → 选中图 + 放大；仅悬浮 → **未选中图** + 放大；都没有 → 未选中图 + 原始大小。
    ///
    /// ⚠️ 尺寸直接赋**绝对缩放**（不是 localScale *= ），所以反复点击/悬浮**不会累积变大**。
    /// </summary>
    private static void ApplyTabIconState(int index, bool selected, bool hover)
    {
        var icon = index >= 0 && index < _tabIcons.Length ? _tabIcons[index] : null;
        if (icon?.Renderer == null) return;

        var sprite = (selected ? _tabIconSelected[index] : _tabIconNormal[index])
                     ?? _tabIconNormal[index];      // 选中图缺失时退回未选中图
        if (sprite != null)
        {
            icon.Renderer.sprite = sprite;
            icon.Renderer.color = UColor.white;
        }

        // 绝对缩放：悬浮或选中都放大，否则原始大小。绝不累乘。
        float s = (selected || hover) ? TabIconHoverScale : 1f;
        if (icon.Go != null) icon.Go.transform.localScale = new Vector3(s, s, 1f);
    }

    private static void SetTabIconState(int index, bool hover)
    {
        bool selected = index == _currentTab;
        ApplyTabIconState(index, selected, hover);
    }

    /// <summary>刷新所有页签图标的选中态。</summary>
    private static void RefreshAllTabIcons()
    {
        for (int i = 0; i < VanillaTabs.Length; i++)
            ApplyTabIconState(i, selected: i == _currentTab, hover: false);
    }

    /// <summary>
    /// 点击页签：统一走 <c>GameSettingMenu.ChangeTab</c>。
    ///
    /// 注意 ChangeTab 已被我们的 Prefix 接管（return false），
    /// 所以这里调它 = 走我们自己的切换逻辑，**不会**碰 ControllerManager，
    /// 也就不会有递归/栈溢出。
    /// </summary>
    private static void OnVanillaTabClicked(int index)
    {
        try
        {
            _currentTab = index;
            RefreshAllTabIcons();
            RefreshTabSelection();

            var menu = GameSettingMenu.Instance;
            if (menu == null)
            {
                ShowTabConfig(index);
                return;
            }

            // 预设 / 游戏设置：让原版页签显示
            // MOD 类：tabNum 直接用我们的下标（ChangeTabPrefix 里按同一套下标分发）
            menu.ChangeTab(index, false);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.OnVanillaTabClicked]", ex);
        }
    }

    /// <summary>
    /// 切换 MOD 类页签的内容：
    ///  - 有对应分类的职业块 → 职业按钮列表（点击进该职业配置页）
    ///  - 有对应分类的其它配置块 → 平铺渲染（调试项在 MOD 页）
    ///  - 都没有 → 占位「XX页签暂未实现。」
    /// </summary>
    private static void ShowTabConfig(int index)
    {
        if (_modPage == null) return;
        _currentTab = index;

        if (_modPlaceholderText != null) _modPlaceholderText.text = "";

        // 预设/游戏设置是原版页 —— ⚠️ 必须把我们自己的页面**关掉**再返回。
        // 之前这里直接 return 而没关 _modPage，导致切到预设页时
        // 我们的内容仍挂在上面（用户反馈"预设的四个文字也过来了"）。
        if (index == TabPreset || index == TabGameSettings)
        {
            Light.UI.Config.RoleListPage.Clear();
            Light.UI.Config.ConfigUIPanel.Clear();
            SetActiveSafe(_modPage, false);

            // ★ 2026-10-10：告诉配置面板"现在停在原版页" ✓
            //   它靠这个决定**要不要每帧再隐藏原版行** —— 原版页当然不能隐藏 ✗
            //   （上一版这里没标记，导致角色页签上"再隐藏"永远不生效 ✓ = 原版行漏进我们的页 ✓）
            Light.UI.Config.ConfigUIPanel.ModTabActive = false;

            // 原版内容不需要我们的宿主
            Light.UI.Config.ConfigUIPanel.SetHostMenu(null);
            return;
        }

        SetActiveSafe(_modPage, true);

        // ★★ 2026-10-10：**每个 MOD 页签都做这两件事**（用户报的两条 bug 的根治点 ✓）
        //    ① 标记"当前是 MOD 页" → 让 `ConfigUIPanel.Refresh` 里的**每帧再隐藏**生效 ✓
        //       （MOD 页签有三条不同路径：配置页 / 职业列表页 / 独立窗口 ✗
        //         只看其中一个 `_page` 必然漏 ✓ —— 日志实证：角色页签上"再隐藏"从没触发过 ✓）
        //    ② 立刻清一次原版内容，**并关掉原版 `ScrollToSelection`** ✓
        //       （它每帧读"已隐藏的选中项"的 localPosition → 空引用 ✗，用户报的 NRE 就是它 ✓）
        Light.UI.Config.ConfigUIPanel.ModTabActive = true;
        Light.UI.Config.ConfigUIPanel.CleanCurrentHost();

        // 取该页签对应的配置分类
        var cats = CategoriesForTab(index);

        // 职业块收集成按钮列表（船员/内鬼/中立页签用），其它配置块照旧平铺
        var roleBlocks = new List<ConfigBlock>();
        bool hasOther = false;
        if (cats != null)
        {
            foreach (var block in ConfigRegistry.Blocks)
            {
                if (!MatchesCats(block, cats)) continue;
                if (block.Key.StartsWith("lid.role.")) { roleBlocks.Add(block); continue; }
                hasOther = true;
            }
        }

        if (roleBlocks.Count > 0)
        {
            // 职业页签：平铺职业按钮，点击进入该职业的独立配置页
            Light.UI.Config.ConfigUIPanel.Clear();
            Light.UI.Config.RoleListPage.Show(roleBlocks, _modPage.transform, OnRoleSelected);
        }
        else if (hasOther)
        {
            Light.UI.Config.RoleListPage.Clear();

            // ⚠️ 配置行要铺进**克隆出来的真 GameOptionsMenu** 的 settingsContainer。
            // 宿主已由 OnVanillaTabClicked 通过 ActivateTabMenu 登记好了。
            Light.UI.Config.ConfigUIPanel.Show(cats!, _modPage.transform);
        }
        else
        {
            Light.UI.Config.RoleListPage.Clear();
            Light.UI.Config.ConfigUIPanel.Clear();
            if (_modPlaceholderText != null)
                _modPlaceholderText.text = $"{VanillaTabs[index].Cn}页签暂未实现。";
        }
    }

    /// <summary>
    /// 把"MOD 设置"页签背后的那个 GameOptionsMenu 登记给配置面板当宿主。
    /// 它就是原版职业设置页里的 <c>GameOptionsMenu</c>（含 settingsContainer / 滚动条 / 各模板预制体）。
    /// </summary>
    private static void RegisterHostMenu()
    {
        try
        {
            var host = _modPage != null
                ? _modPage.GetComponentInParent<GameOptionsMenu>(true)
                : null;

            if (host == null)
            {
                // 退一步：从整个菜单里找（职业设置页那个）
                var menu = GameSettingMenu.Instance;
                if (menu != null)
                    host = menu.GetComponentInChildren<GameOptionsMenu>(true);
            }

            Light.UI.Config.ConfigUIPanel.SetHostMenu(host);
            LightLogger.Log(host != null
                ? $"[GameSettingMenuPatch] 宿主 GameOptionsMenu 已登记：{host.name}"
                : "[GameSettingMenuPatch] 未找到宿主 GameOptionsMenu（将退回自建容器）");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameSettingMenuPatch.RegisterHostMenu] {ex.Message}");
        }
    }

    /// <summary>某页签对应的配置分类（预设/游戏设置返回 null）。</summary>
    private static ConfigCategory[]? CategoriesForTab(int index)
    {
        switch (index)
        {
            case TabMod:      return new[] { ConfigCategory.Mod, ConfigCategory.Debug };
            case TabGhost:    return new[] { ConfigCategory.Ghost };
            case TabCrewmate: return new[] { ConfigCategory.Crewmate };
            case TabImpostor: return new[] { ConfigCategory.Impostor };
            case TabNeutral:  return new[] { ConfigCategory.Neutral };
            case TabModifier: return new[] { ConfigCategory.Modifier };
            default:          return null;
        }
    }

    /// <summary>块是否属于任一给定分类。</summary>
    private static bool MatchesCats(ConfigBlock block, ConfigCategory[] cats)
    {
        foreach (var c in cats)
            if (block.Category == c) return true;
        return false;
    }

    /// <summary>
    /// 点击职业按钮：**就地切换页面** —— 把该职业的配置内容嵌进设置菜单中间那块。
    ///
    /// ⚠️ 2026-10-06 **推翻了独立浮窗方案**（用户："我们推翻吧，制作组所有人都不愿意。
    ///    换成嵌入"）。原因是浮窗要做的事太多、和原版菜单的耦合太重：
    ///      · 行是原版 OptionBehaviour 克隆体，自带 AspectPosition 会把它拽回原版位置；
    ///      · 窗口还得自己驱动 UiModalGuard 防点击穿透；
    ///      · 居中/关闭按钮/层级全要自己摆。
    ///    改回"就地嵌入"之后这些全都不存在 —— 行还铺在原版菜单的滚动容器里，
    ///    AspectPosition 算出来的**正好**是对的位置，什么都不用管。
    /// </summary>
    private static void OnRoleSelected(ConfigBlock block)
    {
        Light.UI.Config.RoleListPage.Clear();

        // 页签保留（用户要求）—— 不调 SetTabButtonsVisible(false)

        // 就地切换到该职业的配置页（返回按钮由 ConfigUIPanel.AddBackButton 画，
        // 它现在克隆的是原版 GameSettingMenu.BackButton）。
        Light.UI.Config.ConfigUIPanel.ShowRole(block, _modPage!.transform, OnRolePageBack);
    }

    /// <summary>职业页点返回：回到职业列表（页签一直是显示的，不用恢复）。</summary>
    private static void OnRolePageBack()
    {
        Light.UI.Config.ConfigUIPanel.Clear();

        // ⚠️ 把宿主 GameOptionsMenu 重新登记回去（切换页面过程中可能被动过）
        RegisterHostMenu();

        SetTabButtonsVisible(true);      // 幂等，无害
        ShowTabConfig(_currentTab);
    }

    /// <summary>显示/隐藏 8 个页签按钮。</summary>
    private static void SetTabButtonsVisible(bool visible)
    {
        foreach (var btn in _tabButtons)
            if (btn != null) btn.SetActive(visible);
    }

    // =====================================================================
    //  UI 工具
    // =====================================================================

    private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.layer = LayerExpansion.GetUILayer();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>用白 sprite（Sliced）画一块纯色矩形（边框用）。</summary>
    private static void MakeRect(Transform parent, string name, float width, float height,
        float x, float y, UColor color)
    {
        var go = NewUIObject(name, parent, new Vector3(x, y, 0f));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = VanillaAsset.WhiteSprite;                    // null 安全
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(width, height);
        sr.color = color;
    }

    /// <summary>给按钮对象加点击/悬浮检测用的碰撞体（PassiveButton 依赖 Collider2D）。</summary>
    private static void AddButtonArea(GameObject go, float width, float height)
    {
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.offset = Vector2.zero;
        col.size = new Vector2(width, height);
    }

    /// <summary>克隆原版标准文本预制体（带字体）；预制体不可用时返回 null，不崩。</summary>
    private static TextMeshPro? CloneText(Transform parent, Vector3 pos, string text, float fontSize)
    {
        var prefab = VanillaAsset.GetStandardTextPrefab();
        if (prefab == null) return null;

        var tmp = Object.Instantiate(prefab, parent);
        tmp.transform.localPosition = pos;
        tmp.fontSize = fontSize;
        tmp.color = UColor.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.text = text;
        tmp.ForceMeshUpdate();
        return tmp;
    }

    // =====================================================================
    //  原版对象查找/文本工具
    // =====================================================================

    private static Transform? FindChildRecursive(Transform parent, string name)
    {
        try
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var r = FindChildRecursive(child, name);
                if (r != null) return r;
            }
            return null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.FindChildRecursive]", ex);
            return null;
        }
    }

    private static PassiveButton? FindButtonByText(GameSettingMenu menu, string keyword)
    {
        try
        {
            var btns = menu.GetComponentsInChildren<PassiveButton>(true);
            foreach (var pb in btns)
            {
                if (pb == null) continue;
                var text = GetButtonText(pb);
                if (!string.IsNullOrEmpty(text) && text.Contains(keyword)) return pb;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.FindButtonByText]", ex);
        }
        return null;
    }

    private static string GetButtonText(PassiveButton btn)
    {
        try
        {
            TextMeshPro? tmp = null;
            var fp = btn.transform.FindChild("FontPlacer");
            if (fp != null && fp.childCount > 0)
                tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
            if (tmp == null)
                tmp = btn.GetComponentInChildren<TextMeshPro>(true);
            return tmp != null ? tmp.text : "";
        }
        catch { return ""; }
    }

    private static void SetButtonText(PassiveButton? btn, string text)
    {
        try
        {
            if (btn == null) return;
            TextMeshPro? tmp = null;
            var fp = btn.transform.FindChild("FontPlacer");
            if (fp != null && fp.childCount > 0)
                tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
            if (tmp == null)
                tmp = btn.GetComponentInChildren<TextMeshPro>(true);
            if (tmp == null) return;

            tmp.text = text;
            // 关掉原版翻译组件，否则会被本地化文本覆盖
            var translators = btn.GetComponentsInChildren<TextTranslatorTMP>(true);
            foreach (var tr in translators)
                if (tr != null) tr.enabled = false;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[GameSettingMenuPatch.SetButtonText]", ex);
        }
    }
}
