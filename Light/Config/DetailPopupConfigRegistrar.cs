using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;

namespace Light.Config;

/// <summary>
/// **详情框（DetailPopup）**配置块 —— 用户 2026-10-06 要求"能自定义大小和布局" ✓。
///
/// ⚠️ 配色为什么用数字档位而不是取色器：配置系统的类型只有 `Bool/Int/Float/Value/Filter` ✗
///    （没有自由字符串 → 没法填十六进制色 ✗）
///    → 所以给几套**预设配色**（0=淡灰底+淡黑边(默认) 1=深灰底+淡金边(旧观感) 2=白卡+深边）✓
///      需要更细的配色时用代码 API：`DetailPopup.SetStyle(new DetailPopupStyle { ... })` ✓
///
/// ⚠️ 默认值 = **现状**（除了配色是用户明确要求改的"淡灰+淡黑" ✓）
/// </summary>
internal static class DetailPopupConfigRegistrar
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            var block = new ConfigBlock(
                "lid.detail",
                LightInDark.Language.Language.Translate("config.detail.title", "详情框"),
                ConfigCategory.Mod);

            block.AddConfiguration(
                "lid.detail.palette", 0, 0, 3, 1,
                LightInDark.Language.Language.Translate("config.detail.palette", "配色"),
                LightInDark.Language.Language.Translate("config.detail.palette.detail",
                    "0 = 淡灰填充 + 淡黑边框（默认）；1 = 深灰 + 淡金（旧观感）；2 = 白卡 + 深边框；3 = Nebula 观感（深灰黑纯色底）"));

            block.AddConfiguration(
                "lid.detail.fontScale", 1.0f, 0.5f, 2.0f, 0.05f,
                LightInDark.Language.Language.Translate("config.detail.fontScale", "字号倍率"),
                LightInDark.Language.Language.Translate("config.detail.fontScale.detail",
                    "文字相对被悬浮那一行的字号倍数（默认 1.0 = 一样大）"));

            block.AddConfiguration(
                "lid.detail.padX", 0.20f, 0f, 1.0f, 0.02f,
                LightInDark.Language.Language.Translate("config.detail.padX", "左右留白"),
                LightInDark.Language.Language.Translate("config.detail.padX.detail", "文字与框左右的间距（默认 0.20）"));

            block.AddConfiguration(
                "lid.detail.padY", 0.12f, 0f, 1.0f, 0.02f,
                LightInDark.Language.Language.Translate("config.detail.padY", "上下留白"),
                LightInDark.Language.Language.Translate("config.detail.padY.detail", "文字与框上下的间距（默认 0.12）"));

            block.AddConfiguration(
                "lid.detail.minWidth", 0.30f, 0f, 3.0f, 0.05f,
                LightInDark.Language.Language.Translate("config.detail.minWidth", "最小宽度"),
                LightInDark.Language.Language.Translate("config.detail.minWidth.detail", "框的最小宽度（0 = 不限制）"));

            block.AddConfiguration(
                "lid.detail.minHeight", 0.20f, 0f, 2.0f, 0.05f,
                LightInDark.Language.Language.Translate("config.detail.minHeight", "最小高度"),
                LightInDark.Language.Language.Translate("config.detail.minHeight.detail", "框的最小高度（0 = 不限制）"));

            block.AddConfiguration(
                "lid.detail.maxWidth", 0f, 0f, 8.0f, 0.25f,
                LightInDark.Language.Language.Translate("config.detail.maxWidth", "换行宽度"),
                LightInDark.Language.Language.Translate("config.detail.maxWidth.detail",
                    "超过这个宽度就换行、框高自动变大（0 = 单行不换行，默认）"));

            block.AddConfiguration(
                "lid.detail.borderThickness", 0.020f, 0f, 0.10f, 0.005f,
                LightInDark.Language.Language.Translate("config.detail.borderThickness", "边框粗细"),
                LightInDark.Language.Language.Translate("config.detail.borderThickness.detail", "默认 0.020；0 = 不画边框"));

            block.AddConfiguration(
                "lid.detail.align", 0, 0, 2, 1,
                LightInDark.Language.Language.Translate("config.detail.align", "文字对齐"),
                LightInDark.Language.Language.Translate("config.detail.align.detail", "0 = 居中（默认）；1 = 左对齐；2 = 右对齐"));

            LightLogger.Log("[DetailPopupConfigRegistrar] 详情框设置注册完成（配色/字号/留白/尺寸/边框/对齐）");
        }
        catch (Exception ex)
        {
            _registered = false;
            LightLogger.LogError("[DetailPopupConfigRegistrar.Register]", ex);
        }
    }
}
