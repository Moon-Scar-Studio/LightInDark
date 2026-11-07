using System;
using System.Linq;
using Light.UI.HudUI;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Modifiers.Buffs;
using LightInDark.Modifiers.Buffs.Examples;
using LightInDark.Visual;
using TMPro;
using UnityEngine;

namespace Light.UI.DebugTools;

/// <summary>
/// 自由模式专用：BUFF 测试窗口。
/// 入口按钮由 <see cref="Light.UI.FreePlayControls"/> 创建（与「选择职业 / 自杀 / 复活」同一排，
/// 位于自杀右侧）；本类只负责窗口内容。
/// </summary>
public static class BuffDebugPanel
{
    private static readonly Vector2 WindowSize = new(5.8f, 6.0f);

    private static HudUIWindow? _window;
    private static int _frame;
    private static int _failures;
    private static bool _disabled;

    /// <summary>由 <c>BuffPatches.HudUpdatePostfix</c> 每帧调用（内部每 30 帧才检查一次）。</summary>
    public static void Tick()
    {
        if (_disabled) return;
        if (++_frame % 30 != 0) return;

        try
        {
            if (ShouldShow()) return;

            // 离开自由模式 / 对局结束 → 关掉窗口
            if (_window == null) return;
            _window.Close();
            _window = null;
        }
        catch (Exception ex)
        {
            if (++_failures >= 3)
            {
                _disabled = true;
                LightLogger.LogError("[BuffDebugPanel] 连续失败 3 次，已停用", ex);
            }
        }
    }

    private static bool ShouldShow()
    {
        try
        {
            var client = AmongUsClient.Instance;
            if (client == null) return false;
            if (client.GameState != AmongUsClient.GameStates.Started) return false;
            if (!GameModeManager.AllowRoleChanges) return false;
            if (HudManager.Instance == null) return false;
            return Player.LocalPlayer != null;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenWindow()
    {
        try
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
                return;
            }

            var hud = HudManager.Instance;
            if (hud == null) return;

            var window = HudUIWindow.Create("BUFF 测试", WindowSize, hud.transform);
            if (window == null) return;

            _window = window;
            BuildContent(window);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BuffDebugPanel.OpenWindow]", ex);
        }
    }

    private static void BuildContent(HudUIWindow window)
    {
        try
        {
            window.ClearContent();

            var me = Player.LocalPlayer;
            window.AddText(me == null ? "取不到本地玩家" : StatusLine(me), 1.1f, TextAlignmentOptions.Center);
            window.AddMargin(0.1f);

            window.AddButton("加速 ×1.3（10 秒）", () => Apply(SpeedBoostBuff.MyBuff), new Vector2(3.8f, 0.5f), null);
            window.AddButton("视野 ×1.5（10 秒）", () => Apply(new ViewDistanceBuff(1.5f, 10f)), new Vector2(3.8f, 0.5f), null);
            window.AddButton("缩小 ×0.6（10 秒）", () => Apply(new ScaleBuff(0.6f, 10f)), new Vector2(3.8f, 0.5f), null);
            window.AddButton("个人小黑人（15 秒）", () => Apply(ShadowBuff.MyBuff), new Vector2(3.8f, 0.5f), null);

            bool anonymous = PlayerAppearance.IsGlobalAnonymous;
            window.AddButton(anonymous ? "小黑人（全场整局）：关" : "小黑人（全场整局）：开",
                ToggleAnonymous, new Vector2(3.8f, 0.5f), null);

            window.AddButton("清除全部 Buff", ClearAll, new Vector2(3.8f, 0.5f), null);
            window.AddButton("关闭", () => { try { window.Close(); } catch { } _window = null; },
                new Vector2(1.8f, 0.45f), null);

            window.ApplyCjkFont();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[BuffDebugPanel.BuildContent]", ex);
        }
    }

    private static string StatusLine(Player me)
    {
        var buffs = BuffManager.Get(me);
        string buffText = buffs.Count == 0
            ? "无"
            : string.Join("、", buffs.Select(b => b.IsPermanent
                ? b.Definition.Name
                : $"{b.Definition.Name} {b.Remaining:0.#}s"));

        string anonymous = PlayerAppearance.IsGlobalAnonymous ? "开" : "关";
        return $"Buff：{buffText}　小黑人（全场）：{anonymous}";
    }

    private static void Apply(BuffDefinition definition)
    {
        var me = Player.LocalPlayer;
        if (me == null) return;

        // 走带同步的入口：房主加 Buff 时会广播给其他客户端
        BuffManager.AddAndBroadcast(me, definition);
        Refresh();
    }

    private static void ToggleAnonymous()
    {
        PlayerAppearance.SetAnonymousAll(!PlayerAppearance.IsGlobalAnonymous);
        Refresh();
    }

    private static void ClearAll()
    {
        var me = Player.LocalPlayer;
        if (me == null) return;

        BuffSync.RemoveAll(me);
        PlayerAppearance.SetAnonymousAll(false);
        Refresh();
    }

    private static void Refresh()
    {
        if (_window != null) BuildContent(_window);
    }
}
