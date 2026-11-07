using Color = LightInDark.Color;
using System;
using TMPro;
using UnityEngine;
using LightInDark.Core;

namespace Light.UI.HudUI;

/// <summary>
/// HudUI 高级辅助方法。
/// </summary>
public static class HudUI
{
    public readonly struct ButtonOption
    {
        public readonly string Label;
        public readonly Action OnClick;
        public readonly Color? Color;
        public readonly Vector2? Size;

        public ButtonOption(string label, Action onClick, Color? color = null, Vector2? size = null)
        {
            try
            {
                Label = label;
                OnClick = onClick;
                Color = color;
                Size = size;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[HudUIHelpers.ButtonOption]", ex);
            }
        }
    }

    /// <summary>
    /// 打开一个按钮选择窗口
    /// </summary>
    public static HudUIWindow OpenButtonWindow(string title, params ButtonOption[] options)
    {
        try
        {
            var window = HudUIWindow.Create(title, new Vector2(4.5f, 1.5f + options.Length * 0.65f));

            window.AddText(title, 2.2f, TextAlignmentOptions.Center);
            window.AddMargin(0.2f);

            foreach (var opt in options)
            {
                var localOpt = opt;
                window.AddButton(localOpt.Label, () =>
                {
                    localOpt.OnClick?.Invoke();
                    window.Close();
                }, localOpt.Size ?? ButtonSize.Rectangle, localOpt.Color);
            }

            // ★ 2026-11-03 用户："二级窗口又没用字体" ✓ → 所有二级窗口统一用 Mod 惯用字体（Noto Sans SC ✓）
            window.ApplyCjkFont();
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIHelpers.OpenButtonWindow]", ex); return default;
        }
    }

    /// <summary>
    /// 打开一个确认对话框（是/否）
    /// </summary>
    public static HudUIWindow OpenConfirmDialog(string message, Action onConfirm, Action? onCancel = null)
    {
        try
        {
            var window = HudUIWindow.Create("", WindowSize.Confirm);

            window.AddText(message, 1.5f, TextAlignmentOptions.Center);
            window.AddMargin(0.2f);

            var localOnConfirm = onConfirm;
            var localOnCancel = onCancel;

            // ★ 2026-11-03 用户："那个确认按钮啥的直接用 HudUI 里有的，为什么要自建还带个破颜色"
            //   → 去掉自建的绿/红 ✗，用 HudUI 的**默认按钮**（无色 ✓，与其它窗口一致 ✓）
            window.AddButton("是", () =>
            {
                localOnConfirm?.Invoke();
                window.Close();
            }, ButtonSize.Rectangle, null);

            window.AddButton("否", () =>
            {
                localOnCancel?.Invoke();
                window.Close();
            }, ButtonSize.Rectangle, null);

            window.ApplyCjkFont();
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIHelpers.OpenConfirmDialog]", ex); return default;
        }
    }

    /// <summary>
    /// 打开一个消息对话框（仅含"确定"按钮）
    /// </summary>
    public static HudUIWindow OpenMessageDialog(string message, Action? onClose = null)
    {
        try
        {
            var window = HudUIWindow.Create("", WindowSize.Small);

            window.AddText(message, 1.5f, TextAlignmentOptions.Center);
            window.AddMargin(0.2f);

            var localOnClose = onClose;
            window.AddButton("确定", () =>
            {
                localOnClose?.Invoke();
                window.Close();
            }, ButtonSize.Rectangle);

            window.ApplyCjkFont();   // ★ 2026-11-03：二级窗口统一用 Mod 字体 ✓
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIHelpers.OpenMessageDialog]", ex); return default;
        }
    }

    /// <summary>
    /// 打开一个文本展示窗口（带标题）
    /// </summary>
    public static HudUIWindow OpenTextWindow(string title, string text)
    {
        try
        {
            var window = HudUIWindow.Create(title, new Vector2(5f, 3.5f));

            window.AddText(title, 2.2f, TextAlignmentOptions.Center);
            window.AddMargin(0.2f);
            window.AddText(text, 1.2f, TextAlignmentOptions.Left);

            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIHelpers.OpenTextWindow]", ex); return default;
        }
    }

    /// <summary>
    /// 打开一个自定义内容窗口（支持自定义尺寸）
    /// </summary>
    public static HudUIWindow OpenCustomWindow(string title, Action<HudUIWindow> builder, Vector2? windowSize = null)
    {
        try
        {
            var window = HudUIWindow.Create(title, windowSize ?? WindowSize.Standard);

            if (!string.IsNullOrEmpty(title))
            {
                window.AddText(title, 2.2f, TextAlignmentOptions.Center);
                window.AddMargin(0.2f);
            }

            builder?.Invoke(window);
            return window;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[HudUIHelpers.OpenCustomWindow]", ex); return default;
        }
    }
}
