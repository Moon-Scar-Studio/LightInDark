using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;

namespace Light.Config;

/// <summary>
/// **HUD 布局**配置块（第 5 批：按钮自动排版优化，用户 2026-10-06 点名的"自己的特色"）。
///
/// 两项都**照抄 Nebula**（`NebulaPluginNova\Modules\HudGrid.cs`）：
///   ① **小 HUD**：整个技能按钮阵缩到 0.72 倍，并换一套边距公式
///      （Nebula `HudGrid.cs:55-66` 的 `localScale = 0.72` + `:210-226` 的 `EdgeX/EdgeY` 双公式）
///      —— 小分辨率 / 开了游戏内小 HUD 时按钮不会挤在一起或飘出屏幕 ✓
///   ② **排列档位**：把按钮整体（或只左侧那一列）抬高 0.85
///      （Nebula `:234-235`：`if ((arrangement == 1 && isLeftSide) || arrangement == 2) pos.y += 0.85f`）
///      —— 按钮多起来之后不再和原版按钮重叠 ✓
///
/// ⚠️ **默认值 = 现状**：`smallGrid = false`、`buttonArrangement = 0`
///    → 不改配置时与改动前**逐字节一致** ✓（纯增量，可随时关掉 ✓）
/// </summary>
internal static class HudLayoutConfigRegistrar
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            var block = new ConfigBlock(
                "lid.hud",
                LightInDark.Language.Language.Translate("config.hud.title", "HUD 布局"),
                ConfigCategory.Mod);

            block.AddConfiguration(
                "lid.hud.smallGrid", false,
                LightInDark.Language.Language.Translate("config.hud.smallGrid", "HUD 紧凑化"),
                LightInDark.Language.Language.Translate("config.hud.smallGrid.detail",
                    "将屏幕中的按钮等界面元素以更小巧紧凑的方式显示（缩到 0.72 倍并调整边距）。默认关"));

            block.AddConfiguration(
                "lid.hud.buttonArrangement", 0, 0, 2, 1,
                LightInDark.Language.Language.Translate("config.hud.buttonArrangement", "按钮排列"),
                LightInDark.Language.Language.Translate("config.hud.buttonArrangement.detail",
                    "0 = 默认；1 = 只把左侧那一列抬高；2 = 全部抬高（按钮多时不和原版按钮挤在一起）"));

            // ⚠️ 地图配色**不在这里注册** ✓ —— 用户 2026-10-16 明确要求"要注册到 option Light 页签里" ✓
            //    → 它走 `LightOptionsRegistry`（设置菜单 → Light 页签 ✓，存 `LightSettings.MapColor` ✓），
            //      生效在 `Light\Patches\MapColorPatch.cs` ✓ —— **只留这一个入口** ✓，避免两处各配一份 ✗

            LightLogger.Log("[HudLayoutConfigRegistrar] HUD 布局设置注册完成（小 HUD / 按钮排列，默认 = 现状）");
        }
        catch (Exception ex)
        {
            _registered = false;   // 失败不算注册过，下次还能重试
            LightLogger.LogError("[HudLayoutConfigRegistrar.Register]", ex);
        }
    }
}
