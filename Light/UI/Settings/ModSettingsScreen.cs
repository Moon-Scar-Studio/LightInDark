using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using AmongUs.GameOptions;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.UI.Window;
using Light.Configuration;
using Light.UI.Window;
using Light.UI.HudUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using Color = LightInDark.Color;
using TextAlignment = LightInDark.UI.Window.TextAlignment;
using FontStyle = LightInDark.UI.Window.FontStyle;
using MetaScreen = Light.UI.Window.MetaScreen;

namespace Light.UI.Settings;

/// <summary>
/// MOD 设置内容页（完全参考 Nebula）：不弹窗、不加窗口，直接内嵌到克隆的原版设置页下。
/// 内部三个子选项卡：游戏设置 | 职业设置 | 预设（LIDGUI 小按钮，选中高亮）。
/// 职业设置：5 分类（船员/伪装者/中立/附加/幽灵）→ 职业名列表 → 点击进详情页（左上角返回）。
/// 预设：Light_Data/Settings/*.lid 列表 + 保存/加载/删除；加载含版本检查与键对比确认。
/// 所有配置改动实时镜像到 current.lid（PresetManager.SaveCurrent）。
/// </summary>
public static class ModSettingsScreen
{
    private static GameObject? _root;
    private static MetaScreen? _screen;
    private static int _activeTab;
    private static Action? _onClose;

    // 职业设置
    private static int _category;          // 0船员 1伪装者 2中立 3附加 4幽灵
    private static string? _roleKey;

    // 预设
    private static TextFieldWidget? _presetField;

    // 选项卡图标（先复用 Nebula 的，后续自行替换）
    private static Sprite? _iconGame;
    private static Sprite? _iconRoles;
    private static Sprite? _iconPreset;

    public static bool IsOpen => _root != null;

    private static Sprite? LoadIcon(string resourceName)
    {
        try
        {
            return Light.UI.HudUI.SpriteSheetLoader.Load(resourceName, 100f);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.LoadIcon]", ex);
            return null;
        }
    }

    // 小号按钮/文本属性（避免按钮过大）
    private static TextAttribute SmallBtnAttr => new(TextAlignment.Center,
        LIDGUI.Instance.GetFont(FontAsset.Gothic), FontStyle.Bold, new FontSize(1.1f, false),
        new Size(0.55f, 0.3f), Color.White, false);
    private static TextAttribute SmallValAttr => new(TextAlignment.Center,
        LIDGUI.Instance.GetFont(FontAsset.Gothic), FontStyle.Normal, new FontSize(1.1f, false),
        new Size(0.7f, 0.3f), Color.White, false);
    private static TextAttribute SmallNameAttr => new(TextAlignment.Left,
        LIDGUI.Instance.GetFont(FontAsset.Gothic), FontStyle.Bold, new FontSize(1.2f, false),
        new Size(2.2f, 0.3f), Color.White, false);

    // ── 游戏设置选项注册表（A-Z 排列）──
    private sealed class GameOptionDef
    {
        public string Key = "";
        public string Name = "";
        public int Min, Max;
        public Func<int> Get;
        public Action<int> Apply;
    }

    private static readonly List<GameOptionDef> _gameOptions = new();
    private static bool _gameOptionsInit;

    private static void EnsureGameOptions()
    {
        try
        {
            if (_gameOptionsInit) return;
            _gameOptionsInit = true;

            _gameOptions.Add(new GameOptionDef
            {
                Key = "NumImpostors",
                Name = Language.Translate("gss.option.numImpostors", "伪装者数量"),
                Min = 1, Max = 3,
                Get = () => { try { return GameOptionsManager.Instance.CurrentGameOptions.NumImpostors; } catch { return 1; } },
                Apply = v =>
                {
                    try { GameOptionsManager.Instance.CurrentGameOptions.SetInt(Int32OptionNames.NumImpostors, v); }
                    catch { }
                },
            });

            _gameOptions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.EnsureGameOptions]", ex);
        }
    }

    // ── 打开 / 关闭（全屏窗口）──

    /// <summary>在指定父节点（克隆的原版设置页）下内嵌生成 MOD 配置内容（参考 Nebula）。</summary>
    public static void Open(Transform parent)
    {
        try
        {
            Close();
            _root = parent != null ? parent.gameObject : null;
            if (_root == null) return;
            EnsureGameOptions();
            _iconGame = LoadIcon("Light.Resources.GUI.SettingGUI.TabIconGame.png");
            _iconRoles = LoadIcon("Light.Resources.GUI.SettingGUI.TabIconRoles.png");
            _iconPreset = LoadIcon("Light.Resources.GUI.SettingGUI.TabIconPreset.png");

            _screen = MetaScreen.GenerateBlankWindow(new Vector2(6.6f, 4.3f), parent, new Vector3(0f, 0f, -1f));
            _activeTab = 0;
            RebuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.Open]", ex);
        }
    }
    public static void Close()
    {
        try
        {
            if (_screen != null)
            {
                try { _screen.Close(); } catch { }
                _screen = null;
                _root = null;
                _roleKey = null;
                _presetField = null;
                var cb = _onClose;
                _onClose = null;
                try { cb?.Invoke(); } catch { }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.Close]", ex);
        }
    }

    private static void RebuildContent()
    {
        try
        {
            if (_screen == null) return;
            _screen.SetWidget(BuildContent(), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.RebuildContent]", ex);
        }
    }

    private static GUIWidget BuildContent()
    {
        try
        {
            var gui = LIDGUI.Instance;

            // 子选项卡行（图标 + 文字，点击切换；选中绿色高亮）—— 参考 Nebula 顶部带图标选项卡
            var icons = new[] { _iconGame, _iconRoles, _iconPreset };
            var tabNames = new[]
            {
                Language.Translate("gss.fs.tab.game", "游戏设置"),
                Language.Translate("gss.fs.tab.roles", "职业设置"),
                Language.Translate("gss.fs.tab.presets", "预设"),
            };
            var tabs = new List<GUIWidget?>();
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var icon = icons[i];
                tabs.Add(gui.VerticalHolder(GUIAlignment.Center,
                    gui.Image(GUIAlignment.Center, icon, new FuzzySize(0.42f, 0.42f),
                        _ => { _activeTab = idx; RebuildContent(); }),
                    gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.OptionsTitleShortest),
                        i == _activeTab ? "<color=#00FF00>" + tabNames[i] + "</color>" : tabNames[i])));
            }
            var tabRow = gui.HorizontalHolder(GUIAlignment.Center, tabs);

            GUIWidget? body = _activeTab switch
            {
                0 => BuildGameTab(),
                1 => BuildRolesTab(),
                _ => BuildPresetsTab(),
            };

            return gui.VerticalHolder(GUIAlignment.Center,
                tabRow,
                gui.VerticalMargin(0.1f),
                body);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildContent]", ex);
            return default;
        }
    }

    // ── 游戏设置页 ──

    private static GUIWidget BuildGameTab()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var inner = new List<GUIWidget?>();
            inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentTitle),
                Language.Translate("gss.fs.game.title", "游戏设置")));
            inner.Add(gui.VerticalMargin(0.1f));

            foreach (var opt in _gameOptions)
            {
                inner.Add(BuildGameOptionRow(gui, opt));
                inner.Add(gui.VerticalMargin(0.08f));
            }

            if (_gameOptions.Count == 0)
                inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("gss.fs.game.none", "暂无已注册选项")));

            return gui.ScrollView(GUIAlignment.Center, new Size(6.4f, 3.6f), null,
                gui.VerticalHolder(GUIAlignment.Left, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildGameTab]", ex);
            return default;
        }
    }

    private static GUIWidget BuildGameOptionRow(LIDGUI gui, GameOptionDef opt)
    {
        try
        {
            int v = opt.Get();
            return gui.HorizontalHolder(GUIAlignment.Left,
                gui.RawText(GUIAlignment.Left, SmallNameAttr, opt.Name),
                gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "-", _ => StepGame(opt, -1)),
                gui.RawText(GUIAlignment.Center, SmallValAttr, v.ToString()),
                gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "+", _ => StepGame(opt, 1)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildGameOptionRow]", ex);
            return default;
        }
    }

    private static void StepGame(GameOptionDef opt, int delta)
    {
        try
        {
            int v = Mathf.Clamp(opt.Get() + delta, opt.Min, opt.Max);
            opt.Apply(v);
            PresetManager.SaveCurrent();
            RebuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.StepGame]", ex);
        }
    }

    // ── 职业设置页 ──

    private static readonly string[] _categoryNames =
    {
        "船员", "伪装者", "中立", "附加", "幽灵",
    };

    private static GUIWidget BuildRolesTab()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var btnAttr = gui.GetAttribute(AttributeAsset.OptionsTitleShortest);

            if (_roleKey != null)
                return BuildRoleDetail(gui);

            // 5 分类选项卡
            var cats = new List<GUIWidget?>();
            for (int i = 0; i < _categoryNames.Length; i++)
            {
                int idx = i;
                cats.Add(gui.RawButton(GUIAlignment.Center, btnAttr, _categoryNames[i],
                    _ => { _category = idx; RebuildContent(); },
                    color: i == _category ? Color.Green : null,
                    selectedColor: i == _category ? Color.Green : null));
            }
            var catRow = gui.HorizontalHolder(GUIAlignment.Center, cats);

            // 该分类下的职业列表
            var inner = new List<GUIWidget?>();
            var roles = new List<Role>();
            switch (_category)
            {
                case 0:
                    foreach (var r in RoleRegistry.AllRoles) if (r.Category == RoleCategory.Crewmate) roles.Add(r);
                    break;
                case 1:
                    foreach (var r in RoleRegistry.AllRoles) if (r.Category == RoleCategory.Impostor) roles.Add(r);
                    break;
                case 2:
                    foreach (var r in RoleRegistry.AllRoles) if (r.Category == RoleCategory.Neutral) roles.Add(r);
                    break;
                case 3:
                    inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                        Language.Translate("gss.fs.roles.noModifier", "暂无修饰器（Modifier）")));
                    break;
                default:
                    inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                        Language.Translate("gss.fs.roles.noGhost", "暂无幽灵职业")));
                    break;
            }

            foreach (var role in roles)
            {
                var name = gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString();
                string key = role.CodeName;
                inner.Add(gui.RawButton(GUIAlignment.Center, btnAttr, name,
                    _ => { _roleKey = key; RebuildContent(); }));
                inner.Add(gui.VerticalMargin(0.06f));
            }

            return gui.VerticalHolder(GUIAlignment.Center,
                catRow,
                gui.VerticalMargin(0.1f),
                gui.ScrollView(GUIAlignment.Center, new Size(6.4f, 3.3f), null,
                    gui.VerticalHolder(GUIAlignment.Center, inner), out _));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildRolesTab]", ex);
            return default;
        }
    }

    /// <summary>三级菜单：职业详情页（左上角返回 + 配置项）。</summary>
    private static GUIWidget BuildRoleDetail(LIDGUI gui)
    {
        try
        {
            var inner = new List<GUIWidget?>();
            var role = RoleRegistry.GetByName(_roleKey!);

            // 左上角返回按钮
            inner.Add(gui.HorizontalHolder(GUIAlignment.Left,
                gui.RawButton(GUIAlignment.Center, SmallBtnAttr,
                    Language.Translate("gss.back", "←"),
                    _ => { _roleKey = null; RebuildContent(); }),
                gui.HorizontalMargin(0.2f),
                gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentTitle),
                    role != null
                        ? gui.ColorTextComponent(role.Color, new RawTextComponent(role.Name)).GetString() + " - " + Language.Translate("gss.role.options", "设置")
                        : _roleKey!)));
            inner.Add(gui.VerticalMargin(0.1f));

            foreach (var entry in RoleConfig.GetRoleOptions(_roleKey!))
            {
                inner.Add(BuildOptionRow(gui, entry));
                inner.Add(gui.VerticalMargin(0.08f));
            }

            return gui.ScrollView(GUIAlignment.Center, new Size(6.4f, 3.4f), null,
                gui.VerticalHolder(GUIAlignment.Left, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildRoleDetail]", ex);
            return default;
        }
    }

    private static GUIWidget BuildOptionRow(LIDGUI gui, RoleOptionEntry entry)
    {
        try
        {
            var row = new List<GUIWidget?>
            {
                gui.RawText(GUIAlignment.Left, SmallNameAttr, entry.DisplayName),
            };

            if (entry.ValueType == typeof(bool))
            {
                bool v = Convert.ToBoolean(entry.GetValue());
                row.Add(gui.RawButton(GUIAlignment.Center, SmallBtnAttr,
                    v ? Language.Translate("options.on", "开") : Language.Translate("options.off", "关"),
                    _ => { entry.SetValue(!v); PresetManager.SaveCurrent(); RebuildContent(); }));
            }
            else if (entry.ValueType == typeof(float) || entry.ValueType == typeof(double))
            {
                float v = Convert.ToSingle(entry.GetValue());
                row.Add(gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "-", _ => Step(entry, -0.5f)));
                row.Add(gui.RawText(GUIAlignment.Center, SmallValAttr, v.ToString("0.#")));
                row.Add(gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "+", _ => Step(entry, 0.5f)));
            }
            else
            {
                int v = Convert.ToInt32(entry.GetValue());
                int step = entry.Key == "Chance" ? 5 : 1;
                row.Add(gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "-", _ => Step(entry, -step)));
                row.Add(gui.RawText(GUIAlignment.Center, SmallValAttr, v.ToString()));
                row.Add(gui.RawButton(GUIAlignment.Center, SmallBtnAttr, "+", _ => Step(entry, step)));
            }

            return gui.HorizontalHolder(GUIAlignment.Left, row);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildOptionRow]", ex);
            return default;
        }
    }

    private static void Step(RoleOptionEntry entry, float delta)
    {
        try
        {
            if (entry == null) return;
            float v = Convert.ToSingle(entry.GetValue()) + delta;
            v = Mathf.Clamp(v, entry.Min, entry.Max);
            entry.SetValue(v);
            PresetManager.SaveCurrent();
            LightLogger.Log($"[MOD设置] {entry.RoleKey}.{entry.Key} = {entry.GetValue()}");
            RebuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.Step]", ex);
        }
    }

    // ── 预设页 ──

    private static GUIWidget BuildPresetsTab()
    {
        try
        {
            var gui = LIDGUI.Instance;
            var inner = new List<GUIWidget?>();
            inner.Add(gui.RawText(GUIAlignment.Left, gui.GetAttribute(AttributeAsset.DocumentTitle),
                Language.Translate("gss.fs.presets.title", "预设")));
            inner.Add(gui.VerticalMargin(0.1f));

            _presetField = new TextFieldWidget(GUIAlignment.Center, new Vector2(3.4f, 0.34f),
                Language.Translate("gss.fs.presets.hint", "预设名称"), null);
            inner.Add(gui.HorizontalHolder(GUIAlignment.Center,
                _presetField,
                gui.HorizontalMargin(0.12f),
                gui.RawButton(GUIAlignment.Center, SmallBtnAttr,
                    Language.Translate("gss.fs.presets.save", "保存"),
                    _ => SavePresetFromField())));
            inner.Add(gui.VerticalMargin(0.12f));

            var presets = PresetManager.ListPresets();
            if (presets.Count == 0)
            {
                inner.Add(gui.RawText(GUIAlignment.Center, gui.GetAttribute(AttributeAsset.DocumentStandard),
                    Language.Translate("gss.fs.presets.none", "暂无预设，输入名称保存当前配置")));
            }
            else
            {
                foreach (var name in presets)
                {
                    string n = name;
                    inner.Add(gui.HorizontalHolder(GUIAlignment.Left,
                        gui.RawText(GUIAlignment.Left, SmallNameAttr, n),
                        gui.RawButton(GUIAlignment.Center, SmallBtnAttr,
                            Language.Translate("gss.fs.presets.load", "加载"),
                            _ => LoadPresetFlow(n)),
                        gui.RawButton(GUIAlignment.Center, SmallBtnAttr,
                            Language.Translate("gss.fs.presets.delete", "删除"),
                            _ => { PresetManager.DeletePreset(n); RebuildContent(); })));
                    inner.Add(gui.VerticalMargin(0.06f));
                }
            }

            return gui.ScrollView(GUIAlignment.Center, new Size(6.4f, 3.4f), null,
                gui.VerticalHolder(GUIAlignment.Left, inner), out _);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.BuildPresetsTab]", ex);
            return default;
        }
    }

    private static void SavePresetFromField()
    {
        try
        {
            string name = _presetField?.Field?.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                MetaUI.OpenMessageDialog(Language.Translate("gss.fs.presets.nameEmpty", "请输入预设名称"));
                return;
            }
            PresetManager.SavePreset(name);
            RebuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.SavePresetFromField]", ex);
        }
    }

    private static void LoadPresetFlow(string name)
    {
        try
        {
            var result = PresetManager.LoadPreset(name, out string msg, out string _);
            switch (result)
            {
                case PresetManager.LoadResult.Applied:
                    MetaUI.OpenMessageDialog(msg);
                    RebuildContent();
                    break;
                case PresetManager.LoadResult.Rejected:
                    MetaUI.OpenMessageDialog(msg);
                    break;
                case PresetManager.LoadResult.NeedsConfirm:
                    MetaUI.OpenConfirmDialog(msg,
                        () => { PresetManager.ApplyPresetForce(name); MetaUI.OpenMessageDialog("已强制加载: " + name); RebuildContent(); });
                    break;
                default:
                    MetaUI.OpenMessageDialog(msg);
                    break;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[ModSettingsScreen.LoadPresetFlow]", ex);
        }
    }
}
