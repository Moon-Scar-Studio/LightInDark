using System;
using System.Collections.Generic;
using System.Linq;
using Light.UI.HudUI;
using Light.UI.MainMenu;
using Light.UI.Window;
using Light.Modifiers;             // ModifierRegistry（【2026-11-01 合并自 `/LightInDark (2)`】✓）
using LightInDark;                 // LightInDark.Color（本模组自研的颜色结构体）
using LightInDark.Configuration;   // RoleCategory 在这里（不是 LightInDark.Roles）
using LightInDark.Core;
using LightInDark.Modifiers;       // Modifier / ModifierManager ✓
using LightInDark.Roles;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.UI.DebugTools;

/// <summary>
/// **调试模式专属**：游戏内的「选择职业」按钮 + 职业选择窗口。
///
/// 需求（用户原话）："在调试模式里的专属按钮，点击后立即切换为这个职业"。
///
/// 参考 NOS 的做法（把 AllRoles 铺成按钮列表）
/// （`Nebula\MetaAbility.cs:99`：把 `Roles.AllRoles` 铺成按钮列表，
///   并用 `ShowOnFreeplayScreen` 过滤 —— 它就是挂在自由模式上的）。
///
/// ⚠️ 门禁只有两条：**调试模式开着** + **在游戏里**。
///    （原先还判断 `NetworkMode == FreePlay`，但自由模式入口已被 MainMenuPatch 删掉，
///      那个判断只会把功能挡死 —— 用户明确指出后已去掉。）
/// 满足才出现：
///   ① 调试模式开启（<see cref="Light.Patches.DebugMode.Enabled"/>）
///   ② HudManager 已就绪（= 在游戏里）
///
/// 点职业按钮 → <see cref="LightInDark.Game.Player.SetRole"/>
///   （它的注释就是"切换角色（本地立即切换，并发送RPC同步）"，正是要的"立即切换"）。
///
/// ⚠️ 层级：HUD 里 **z 越小越靠前**。
///    按钮放 z=-45、窗口是 HudUIWindow 默认的 z=-50，
///    所以窗口会盖在按钮上面（点开之后不会误触到按钮）。
///    反过来说，按钮比原版 HUD 内容(z≈-1)更靠前，会画在它们上面 ——
///    位置在左下角，那边本来是空的，可以接受。
/// </summary>
public static class RoleDebugPanel
{
    // 左下角
    private const float BtnX = -3.30f;
    private const float BtnY = -2.35f;
    private const float BtnZ = -45f;

    /// <summary>每页几个职业（按 ~0.65 行高算，6 个刚好塞进 5.4 高的窗口）。</summary>
    private const int PageSize = 4;

    private static readonly Vector2 WindowSize = new(6.0f, 5.4f);

    private static GameObject? _buttonGo;
    private static GradientButton? _button;
    private static HudUIWindow? _window;
    private static int _page;
    /// <summary>是否在“附加身份”（修饰器）页 ✓（合并自 `/LightInDark (2)`）</summary>
    private static bool _showModifiers;

    /// <summary>由 <c>HudManager.Update</c> 的 postfix 每帧驱动。</summary>
    public static void Tick()
    {
        try
        {
            // 每帧拼诊断字符串太浪费 → 每 15 帧才查一次门禁
            if (++_frame % 15 != 0) return;

            bool ok = CheckGate(out string gate);
            if (gate != _lastGate)
            {
                _lastGate = gate;
                LightLogger.Log($"[RoleDebugPanel] {(ok ? "条件满足 → 放置按钮" : "暂不显示按钮")} ｜ {gate}");
            }

            if (!ok) { HideAll(); return; }

            EnsureButton();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDebugPanel.Tick] {ex.Message}");
        }
    }

    private static int _frame;
    private static string _lastGate = "";

    /// <summary>
    /// 检查门禁条件，并把**每一项的真实值**写进 <paramref name="gate"/> 返回。
    ///
    /// ⚠️⚠️ 上一版这里是 `catch { return false; }` —— 静默失败、日志里一个字都没有，
    ///    结果"看不见按钮"完全无从查起（用户报的就是这个）。
    ///    现在每一项单独 try，异常也写进返回串，**门禁一变就打日志**。
    /// </summary>
    private static bool CheckGate(out string gate)
    {
        bool dbg = false, clientOk = false, hudOk = false, inGame = false;
        var mode = (NetworkModes)(-1);
        string err = "";

        try { dbg = Light.Patches.DebugMode.Enabled; }
        catch (Exception e) { err += $" DebugMode异常={e.GetType().Name}"; }

        try
        {
            var c = AmongUsClient.Instance;
            clientOk = c != null;
            if (clientOk) mode = c.NetworkMode;
        }
        catch (Exception e) { err += $" AmongUsClient异常={e.GetType().Name}"; }

        try { hudOk = HudManager.Instance != null; }
        catch (Exception e) { err += $" HudManager异常={e.GetType().Name}"; }

        try { inGame = GameData.Instance != null; } catch { }

        // ⚠️ **不再判断 NetworkMode**。
        //    用户反馈："不需要判断 FreePlay！那个入口都删了" ——
        //    自由模式按钮已经被 MainMenuPatch 拿掉，游戏里根本进不去 FreePlay，
        //    所以那个判断只会把功能挡死。
        //    现在的门禁就是两条：**调试模式开着** + **在游戏里（HudManager 就绪）**。
        //    模式仍然打进日志，纯粹为了排查时能看清当时是什么局。
        gate = $"调试模式={dbg} AmongUsClient={clientOk} NetworkMode={mode}({(int)mode}) " +
               $"HudManager={hudOk} 局内={inGame}{err}";

        return (dbg || LightInDark.Game.GameModeManager.AllowRoleChanges) && clientOk && hudOk
            && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Started;
    }

    // =====================================================================
    //  左下角按钮
    // =====================================================================

    private static void EnsureButton()
    {
        // ⚠️ 必须用 Unity 的 == 判活：场景切换后 _buttonGo 会变成"假 null"
        //    （对象已销毁但 C# 引用还在），用 ?? 之类是识别不出来的。
        if (_buttonGo != null && _button != null) return;

        _buttonGo = null;
        _button = null;

        var hud = HudManager.Instance;
        if (hud == null) return;

        _button = GradientButton.Create(hud.transform, "选择职业", new Vector2(2.3f, 0.52f),
            OpenWindow, false, 1.05f);

        if (_button == null)
        {
            LightLogger.LogWarning("[RoleDebugPanel] 按钮创建失败");
            return;
        }

        _buttonGo = _button.GameObject;
        _button.SetPosition(new Vector3(BtnX, BtnY, BtnZ));

        // 一次性把"看得见看不见"相关的关键事实全打出来（AGENTS.md §4.3/§4.8）
        try
        {
            var wp = _buttonGo.transform.position;
            var vp = Camera.main != null ? Camera.main.WorldToViewportPoint(wp) : Vector3.zero;
            LightLogger.Log($"[RoleDebugPanel] 已放置「选择职业」按钮 " +
                            $"local=({BtnX},{BtnY},{BtnZ}) layer={_buttonGo.layer} " +
                            $"active={_buttonGo.activeInHierarchy} world=({wp.x:F2},{wp.y:F2},{wp.z:F2}) " +
                            $"viewport=({vp.x:F2},{vp.y:F2}) HudManager层={hud.gameObject.layer}");
        }
        catch
        {
            LightLogger.Log($"[RoleDebugPanel] 已放置「选择职业」按钮 local=({BtnX},{BtnY},{BtnZ})");
        }
    }

    private static void HideAll()
    {
        try
        {
            if (_window != null) { _window.Close(); }
        }
        catch { }
        _window = null;

        try
        {
            if (_buttonGo != null) Object.Destroy(_buttonGo);
        }
        catch { }
        _buttonGo = null;
        _button = null;
    }

    // =====================================================================
    //  职业选择窗口
    // =====================================================================

    public static void OpenWindow()
    {
        try
        {
            if (_window != null)
            {
                // 已经开着 → 再点一次就关掉
                _window.Close();
                _window = null;
                return;
            }

            var hud = HudManager.Instance;
            if (hud == null) return;

            var w = HudUIWindow.Create("选择职业", WindowSize, hud.transform);
            if (w == null)
            {
                LightLogger.LogWarning("[RoleDebugPanel] 窗口创建失败");
                return;
            }

            _page = 0;
            _window = w;
            BuildContent(w);

            LightLogger.Log($"[RoleDebugPanel] 已打开职业选择窗口（共 {Roles().Count} 个职业）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDebugPanel.OpenWindow]", ex);
        }
    }

    private static void BuildContent(HudUIWindow w)
    {
        try
        {
            w.ClearContent();

            var all = Roles();
            int pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
            _page = Mathf.Clamp(_page, 0, pages - 1);

            // 顶部：当前职业
            w.AddText($"当前职业：{CurrentRoleName()}", 1.3f, TextAlignmentOptions.Center);
            w.AddMargin(0.08f);

            // 【2026-11-01 合并自 `/LightInDark (2)`】附加身份（修饰器）页 ✓
            //   合并进来的 `Flash` 修饰器**没有任何职业会发放** ✗ →
            //   没有这个入口就完全没法测 ✓（这里手动挂到本地玩家身上 ✓）
            w.AddButton(_showModifiers ? "返回职业" : "附加身份",
                () => { _showModifiers = !_showModifiers; _page = 0; BuildContent(w); },
                new Vector2(2.4f, 0.42f), null);
            w.AddMargin(0.08f);

            if (_showModifiers)
            {
                BuildModifiers(w);
                w.ApplyCjkFont();
                return;
            }

            if (all.Count == 0)
            {
                w.AddText("（没有注册任何职业）", 1.2f, TextAlignmentOptions.Center);
                w.AddMargin(0.15f);
            }
            else
            {
                foreach (var role in all.Skip(_page * PageSize).Take(PageSize))
                {
                    var r = role;   // 闭包捕获
                    var label = $"{CategoryPrefix(r.RoleCategory)} {r.Name}";

                    // ⚠️ 注意：本模组的 Role.Color 是**自研的 LightInDark.Color 结构体**，
                    //    而 HudUIWindow.AddButton 的颜色参数正好也是 LightInDark.Color?，
                    //    所以**直接传**，别转成 UnityEngine.Color。
                    LightInDark.Color c;
                    try { c = r.Color; }
                    catch { c = LightInDark.Color.White; }

                    w.AddButton(label, () => Assign(r), null, c);
                }
            }

            // 翻页（超过一页才有）
            if (pages > 1)
            {
                w.AddMargin(0.05f);
                w.AddText($"第 {_page + 1} / {pages} 页", 1.1f, TextAlignmentOptions.Center);
                w.AddMargin(0.05f);

                w.AddButton("上一页", () => { _page--; BuildContent(w); },
                    new Vector2(1.6f, 0.45f), null);
                w.AddButton("下一页", () => { _page++; BuildContent(w); },
                    new Vector2(1.6f, 0.45f), null);
            }

            w.AddMargin(0.1f);
            w.AddButton("关闭", () => { try { w.Close(); } catch { } _window = null; },
                new Vector2(1.8f, 0.45f), null);

            // 简中字体（第二个 TMP 模板）
            w.ApplyCjkFont();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDebugPanel.BuildContent]", ex);
        }
    }

    /// <summary>
    /// “附加身份”页：列出所有已注册的修饰器，点一下挂到**本地玩家**身上 ✓
    /// （【2026-11-01 合并自 `/LightInDark (2)`】，签名已适配当前 `ModifierManager.AddModifier(Player, Modifier)` ✓）
    /// </summary>
    private static void BuildModifiers(HudUIWindow w)
    {
        try
        {
            var me = LightInDark.Game.GameManager.Instance?.LocalPlayer;
            var modifiers = ModifierRegistry.All;

            if (modifiers.Count == 0)
            {
                w.AddText("（没有注册任何附加身份）", 1.2f, TextAlignmentOptions.Center);
                return;
            }

            foreach (var modifier in modifiers)
            {
                var m = modifier;   // 闭包捕获 ✓
                bool has = me != null && ModifierManager.HasModifier(me, m.Key);
                string label = (has ? "✔ " : "") + m.Name;

                w.AddButton(label, () => ToggleModifier(m), null, null);
            }

            w.AddMargin(0.06f);
            w.AddButton("全部移除", () =>
            {
                try
                {
                    if (me != null)
                        foreach (var rm in ModifierManager.GetModifiers(me))
                            ModifierManager.RemoveModifier(me, rm.Definition.Key);
                }
                catch (Exception ex) { LightLogger.LogWarning($"[RoleDebugPanel] 移除修饰器失败：{ex.Message}"); }
                if (_window != null) BuildContent(_window);
            }, new Vector2(1.9f, 0.42f), null);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDebugPanel.BuildModifiers]", ex);
        }
    }

    /// <summary>给本地玩家挂 / 摘一个修饰器 ✓</summary>
    private static void ToggleModifier(Modifier modifier)
    {
        try
        {
            var me = LightInDark.Game.GameManager.Instance?.LocalPlayer;
            if (me == null || modifier == null) return;

            if (ModifierManager.HasModifier(me, modifier.Key))
            {
                ModifierManager.RemoveModifier(me, modifier.Key);
                LightLogger.Log($"[RoleDebugPanel] 已移除附加身份 → {modifier.Name}");
            }
            else
            {
                ModifierManager.AddModifier(me, modifier);
                LightLogger.Log($"[RoleDebugPanel] 已添加附加身份 → {modifier.Name}");
            }

            if (_window != null) BuildContent(_window);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDebugPanel.ToggleModifier]", ex);
        }
    }

    /// <summary>点职业 → 立即把自己换成这个职业。</summary>
    private static void Assign(RoleTemplate role)
    {
        try
        {
            if (role == null) return;

            var me = LightInDark.Game.GameManager.Instance?.LocalPlayer;
            if (me == null)
            {
                LightLogger.LogWarning("[RoleDebugPanel] 取不到本地玩家，无法切换职业");
                return;
            }

            if (LightInDark.Game.GameModeManager.AllowRoleChanges)
                LightInDark.RPCs.RpcDefinitions.RequestFreePlayRole(me.Control.PlayerId, role.CodeName);
            else
                me.SetRole(role);
            LightLogger.Log($"[RoleDebugPanel] 已切换职业 → {role.Name}（{role.CodeName} / {role.RoleCategory}）");

            // 刷一下"当前职业"那行
            if (_window != null) BuildContent(_window);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleDebugPanel.Assign]", ex);
        }
    }

    // =====================================================================
    //  小工具
    // =====================================================================

    /// <summary>所有已注册职业，按类别排序（内鬼 → 中立 → 船员），方便找。</summary>
    private static List<RoleTemplate> Roles()
    {
        try
        {
            return RoleRegistry.AllRoles
                .Where(r => r != null)
                .OrderBy(r => (int)r.RoleCategory)
                .ThenBy(r => r.CodeName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleDebugPanel.Roles] {ex.Message}");
            return new List<RoleTemplate>();
        }
    }

    private static string CurrentRoleName()
    {
        try
        {
            var me = LightInDark.Game.GameManager.Instance?.LocalPlayer;
            var r = me?.Role;
            if (r == null) return "（无 / 原版职业）";
            return $"{r.Name}";
        }
        catch { return "—"; }
    }

    private static string CategoryPrefix(RoleCategory c) => c switch
    {
        RoleCategory.Impostor => "[内鬼]",
        RoleCategory.Neutral => "[中立]",
        _ => "[船员]",
    };
}
