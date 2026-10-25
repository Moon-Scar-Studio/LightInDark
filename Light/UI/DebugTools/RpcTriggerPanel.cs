using System;
using System.Collections.Generic;
using System.Linq;
using Light.UI.HudUI;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.RPCs;
using TMPro;
using UnityEngine;

namespace Light.UI.DebugTools;

/// <summary>
/// 自由模式专用：**RPC 触发器**（2026-10-25 用户要求）
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【功能】
///   · 列出**所有**通过 `[LidRPC]` 注册的 RPC（名字 + 参数类型 + OnlyHost 标记 ✓）
///   · **搜索框**：直接打字即可过滤 —— 匹配**短名 / 完整名 / 常量哈希 id / 参数名** ✓
///     （退格删一个字符，Esc 清空；不抢原版输入框焦点 ✓）
///   · 点一下 = 用**默认参数**触发（byte → 本地玩家 id ✓，其它 = 0/""/false/null ✓）
///     走的就是正常调用 → 会被 Prefix 拦截 → 广播 + 本地执行 ✓（和业务代码调用完全一致 ✓）
///   · 分页（每页 7 条 ✓，上一页/下一页 ✓）
///
/// ⚠️ 只在**自由模式**（`GameModeManager.AllowRoleChanges`）里开启 ✓ —— 普通局里列表里都是
///    房主权威 RPC，随手点会搅乱对局 ✗
/// ═══════════════════════════════════════════════════════════════════════
public static class RpcTriggerPanel
{
    private const int PageSize = 7;
    private static readonly Vector2 WindowSize = new(6.2f, 6.4f);

    private static HudUIWindow? _window;
    private static int _frame;
    private static int _failures;
    private static bool _disabled;

    private static string _query = "";
    private static int _page;
    private static List<LidRpcInfo> _all = new();

    /// <summary>由 <c>BuffPatches.HudUpdatePostfix</c> 每帧调用（内部限频 ✓）</summary>
    public static void Tick()
    {
        if (_disabled) return;
        if (++_frame % 10 != 0) return;

        try
        {
            if (!_windowOpen()) return;

            // 离开自由模式 / 对局结束 → 关掉
            if (ShouldShow()) return;
            Close();
        }
        catch (Exception ex)
        {
            if (++_failures >= 3)
            {
                _disabled = true;
                LightLogger.LogError("[RpcTriggerPanel] 连续失败 3 次，已停用", ex);
            }
        }
    }

    private static bool _windowOpen()
    {
        return _window != null;
    }

    private static bool ShouldShow()
    {
        try
        {
            var client = AmongUsClient.Instance;
            if (client == null) return false;
            if (client.GameState != AmongUsClient.GameStates.Started) return false;
            if (!GameModeManager.AllowRoleChanges) return false;
            return HudManager.Instance != null;
        }
        catch { return false; }
    }

    public static void OpenWindow()
    {
        try
        {
            if (_window != null) { Close(); return; }
            if (!ShouldShow()) return;

            var hud = HudManager.Instance;
            if (hud == null) return;

            _all = LidRpcRegistry.GetRpcs();
            _query = "";
            _page = 0;

            var window = HudUIWindow.Create("RPC 触发器", WindowSize, hud.transform);
            if (window == null) return;

            _window = window;
            BuildContent();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RpcTriggerPanel.OpenWindow]", ex);
        }
    }

    private static void Close()
    {
        try { _window?.Close(); } catch { }
        _window = null;
    }

    // ─────────────────────────────────────────────────────────────────
    //  UI
    // ─────────────────────────────────────────────────────────────────

    private static void BuildContent()
    {
        var window = _window;
        if (window == null) return;

        try
        {
            window.ClearContent();

            window.AddText($"搜索：{(_query.Length == 0 ? "（直接打字过滤 / 退格删字 / Esc 清空）" : _query)}",
                1.05f, TextAlignmentOptions.Center);
            window.AddMargin(0.05f);

            var list = Filtered();
            int pages = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            window.AddText($"共 {_all.Count} 条 RPC，匹配 {list.Count} 条　第 {_page + 1}/{pages} 页",
                0.9f, TextAlignmentOptions.Center);
            window.AddMargin(0.05f);

            foreach (var rpc in list.Skip(_page * PageSize).Take(PageSize))
            {
                var info = rpc;   // 闭包捕获 ✓
                string tag = info.OnlyHost ? "房主" : "广播";
                window.AddButton($"[{tag}] {info.Signature}  #{info.Hash}", () => Trigger(info),
                    new Vector2(5.0f, 0.42f), null);
            }

            window.AddMargin(0.05f);
            window.AddButton("< 上一页", () => { if (_page > 0) { _page--; BuildContent(); } },
                new Vector2(1.6f, 0.42f), null);
            window.AddButton("下一页 >", () => { _page++; BuildContent(); },
                new Vector2(1.6f, 0.42f), null);

            window.AddButton("关闭", Close, new Vector2(1.4f, 0.42f), null);
            window.ApplyCjkFont();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RpcTriggerPanel.BuildContent]", ex);
        }
    }

    private static List<LidRpcInfo> Filtered()
    {
        if (string.IsNullOrEmpty(_query)) return _all;

        try
        {
            string q = _query;
            return _all.Where(r =>
                (r.ShortName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (r.Name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || r.Hash.ToString() == q
                || r.Hash.ToString().Contains(q)
                || r.Signature.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        catch { return _all; }
    }

    private static void Trigger(LidRpcInfo info)
    {
        try
        {
            bool ok = LidRpcRegistry.Invoke(info);
            LightLogger.Log($"[RpcTriggerPanel] {info.Name} → {(ok ? "已触发" : "失败")}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RpcTriggerPanel.Trigger]", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  键盘输入（自己收键，不用 TMP_InputField ✓ 避免抢焦点 ✗）
    // ─────────────────────────────────────────────────────────────────

    /// <summary>每帧由 Tick 之外的输入驱动调用（窗口开着才吃键 ✓）</summary>
    internal static void HandleInput()
    {
        if (_window == null) return;

        try
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_query.Length > 0) { _query = ""; _page = 0; BuildContent(); }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _query.Length > 0)
            {
                _query = _query.Substring(0, _query.Length - 1);
                _page = 0;
                BuildContent();
                return;
            }

            string typed = Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            var sb = new System.Text.StringBuilder(_query);
            foreach (char ch in typed)
            {
                if (ch == '\b') { if (sb.Length > 0) sb.Length--; continue; }
                if (ch == '\n' || ch == '\r') continue;
                if (char.IsControl(ch)) continue;
                sb.Append(ch);
            }

            string next = sb.ToString();
            if (next == _query) return;

            _query = next;
            _page = 0;
            BuildContent();
        }
        catch { }
    }
}
