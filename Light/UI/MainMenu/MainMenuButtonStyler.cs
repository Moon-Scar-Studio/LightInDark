using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Light.UI.Window;
using LightInDark.Core;
using UnityEngine;

namespace Light.UI.MainMenu;

/// <summary>
/// 主界面按钮样式：**MOD 样式 / 原版样式 / 亚克力样式**，并支持逐个或批量改颜色。
///
/// =====================================================================
///  【原版贴图从哪来】
///
/// <c>MainMenuButtonSpritePatch</c> 会在 <c>MainMenuManager.Start</c> 的 **postfix** 里
/// 把左侧按钮贴成模组自己的 PNG，而且有个"保图守卫"**每帧**盯着、改回去就立刻补。
/// 所以想拿到**原版**贴图，必须在它动手之前抓：
///   本类用 <c>[HarmonyPrefix]</c> 挂在同一个 <c>MainMenuManager.Start</c> 上 ——
///   prefix 一定先于所有 postfix 执行，那一刻按钮上还是原版图。
///
/// 左侧按钮共用同一张原版底图（同一个预制体），所以抓 **一个** 就够所有按钮用。
///
/// 【三种样式怎么落地】
///   · MOD样式    —— 让保图守卫恢复正常（它会把模组 PNG 补回来），并把颜色还原成白色
///   · 原版样式   —— 压制保图守卫 + 把所有按钮贴回抓到的原版图 + 颜色还原成白色
///   · 亚克力样式 —— 压制保图守卫 + 贴原版图 + 用选定的颜色**整体染色**（FS 那种纯色板）
///
/// 右侧面板（本地/在线/创建/加入/输入代码）在**原版/亚克力**样式下也一并回到原版贴图 ——
/// 用户明确要求。
/// </summary>
internal static class MainMenuButtonStyler
{
    internal sealed class Entry
    {
        public string Key = "";          // 稳定标识（按钮 GameObject 名）
        public string Label = "";        // 界面上显示的中文名
        public PassiveButton Btn = null!;
        public bool IsRightPanel;
    }

    private static readonly List<Entry> _entries = new();
    private static Sprite? _vanillaSprite;
    private static bool _captured;
    private static MainButtonStyle _applied = (MainButtonStyle)(-1);
    private static int _appliedColorHash;

    // =====================================================================
    //  抓原版贴图（必须早于 MainMenuButtonSpritePatch 的 postfix）
    // =====================================================================

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPrefix]
    public static void CapturePrefix(MainMenuManager __instance)
    {
        try
        {
            if (_captured) return;

            // 左侧按钮共用一张原版底图，取"开始"那个按钮的常态图即可
            var probe = __instance.playButton ?? __instance.settingsButton ?? __instance.quitButton;
            var sr = FindMainRenderer(probe);
            if (sr != null && sr.sprite != null)
            {
                _vanillaSprite = sr.sprite;
                _captured = true;
                LightLogger.Log($"[MainMenuButtonStyler] 已抓到原版按钮贴图：{sr.sprite.name}");
            }
            else
            {
                LightLogger.LogWarning("[MainMenuButtonStyler] 没抓到原版按钮贴图（原版样式会退化成只还原颜色）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuButtonStyler.CapturePrefix] {ex.Message}");
        }
    }

    /// <summary>取一个按钮"常态"那张主渲染器。</summary>
    private static SpriteRenderer? FindMainRenderer(PassiveButton? btn)
    {
        if (btn == null) return null;
        try
        {
            if (btn.inactiveSprites != null)
            {
                var sr = btn.inactiveSprites.GetComponent<SpriteRenderer>();
                if (sr != null) return sr;
            }
            return btn.GetComponentInChildren<SpriteRenderer>(true);
        }
        catch { return null; }
    }

    // =====================================================================
    //  收集按钮
    // =====================================================================

    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PlayButton"] = "开始",
        ["Inventory Button"] = "仓库",
        ["InventoryButton"] = "仓库",
        ["ShopButton"] = "商店",
        ["NewsButton"] = "新闻",
        ["MyAccountButton"] = "我的账户",
        ["SettingsButton"] = "设置",
        ["CreditsButton"] = "制作人员",
        ["QuitButton"] = "退出",
        ["LightButton"] = "LIGHT",
        ["UpdateButton"] = "检查更新",
        ["WebsiteButton"] = "模组官网",
        ["GithubButton"] = "GITHUB",
        ["GitHubButton"] = "GITHUB",
        ["QQButton"] = "QQ群",
        ["DiscordButton"] = "Discord",
    };

    /// <summary>不该被当成"主界面按钮"的容器名（我们自己的 UI）。</summary>
    private static readonly string[] ExcludeRoots =
    {
        "MetaWindow", "LightScreen", "LightSubScreen", "LightConfigPage",
        "PlayerOptionsMenu", "LightConfigRow", "GradientButton",
    };

    /// <summary>扫描主界面里的按钮。每次进主菜单都会重新扫（对象会重建）。</summary>
    public static void Refresh(MainMenuManager menu)
    {
        try
        {
            _entries.Clear();
            var seen = new HashSet<int>();

            void Add(PassiveButton? btn, bool rightPanel)
            {
                if (btn == null) return;
                var go = btn.gameObject;
                if (go == null) return;

                int id = go.GetInstanceID();
                if (!seen.Add(id)) return;
                if (IsExcluded(go)) return;

                string key = string.IsNullOrEmpty(go.name) ? $"btn{id}" : go.name;
                // 重名就加序号，保证 key 唯一（持久化要稳定）
                string baseKey = key;
                int n = 2;
                while (_entries.Any(e => e.Key == key)) key = $"{baseKey}#{n++}";

                _entries.Add(new Entry
                {
                    Key = key,
                    Label = Friendly.TryGetValue(baseKey, out var cn) ? cn : baseKey,
                    Btn = btn,
                    IsRightPanel = rightPanel,
                });
            }

            // ① 明确定义的左侧按钮（顺序固定，界面上好看）
            Add(menu.playButton, false);
            Add(menu.inventoryButton, false);
            Add(menu.shopButton, false);
            Add(menu.newsButton, false);
            Add(menu.myAccountButton, false);
            Add(menu.settingsButton, false);
            Add(menu.creditsButton, false);
            Add(menu.quitButton, false);

            // ★ 2026-10-25 性能修复：原来这两处用的是
            //   `FindObjectsOfType<PassiveButton>()` ✗ —— **全场景枚举**（每次进主菜单 2 遍）→ 卡一下 ✓
            //   语义上本来就只要"主界面层级里的按钮"（下面还会 `IsChildOf(menu.transform)` 过滤），
            //   → 直接用 `menu.GetComponentsInChildren<PassiveButton>(true)` **限定子树** ✓（一遍搞定 ✓）
            var allButtons = menu.GetComponentsInChildren<PassiveButton>(true);

            // ② LIGHT 按钮 + 底部那些（检查更新/模组官网/GITHUB/QQ群）—— 自动扫，
            //    它们可能挂在不同父节点下，硬编码容易漏。
            foreach (var pb in allButtons)
            {
                if (pb == null) continue;
                var go = pb.gameObject;
                if (go == null) continue;
                if (IsExcluded(go)) continue;

                // 只收主界面层级里的（排除大厅/对局里的按钮）
                if (menu.transform != null && !go.transform.IsChildOf(menu.transform)) continue;
                Add(pb, false);
            }

            // ③ 右侧面板
            Add(menu.createGameButton, true);
            foreach (var pb in allButtons)
            {
                if (pb == null) continue;
                var go = pb.gameObject;
                if (go == null || IsExcluded(go)) continue;
                if (!IsRightPanelButton(go.name)) continue;
                Add(pb, true);
            }

            LightLogger.Log($"[MainMenuButtonStyler] 收集到 {_entries.Count} 个主界面按钮：" +
                            string.Join(", ", _entries.Take(20).Select(e => e.Label)));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonStyler.Refresh]", ex);
        }
    }

    private static bool IsRightPanelButton(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("local") || n.Contains("online") || n.Contains("creategame")
            || n.Contains("findgame") || n.Contains("entercode");
    }

    private static bool IsExcluded(GameObject go)
    {
        try
        {
            var t = go.transform;
            for (int i = 0; i < 10 && t != null; i++)
            {
                foreach (var bad in ExcludeRoots)
                    if (t.name.StartsWith(bad, StringComparison.OrdinalIgnoreCase)) return true;
                t = t.parent;
            }
            return false;
        }
        catch { return true; }
    }

    /// <summary>跨场景：清掉收集到的按钮引用（它们是场景对象，会随场景销毁）。</summary>
    public static void ResetForSceneChange()
    {
        _entries.Clear();
        _applied = (MainButtonStyle)(-1);
        _appliedColorHash = 0;
        // ⚠️ 保图守卫的 Suppress 不要重置：它是设置驱动的，
        //    重置了会在重进主菜单时把用户选的原版/亚克力样式顶掉。
    }

    /// <summary>给颜色窗口用：当前收集到的按钮。</summary>
    public static IReadOnlyList<Entry> Entries => _entries;

    /// <summary>
    /// 颜色覆盖只有在**亚克力样式**下才看得出来
    /// （MOD/原版样式会把颜色还原成白色，好让原版贴图和悬浮逻辑正常工作）。
    /// 颜色窗口据此给用户一句提示。
    /// </summary>
    public static bool ColorsSelectable =>
        AppearanceSettings.ButtonStyle == MainButtonStyle.Acrylic;

    // =====================================================================
    //  应用
    // =====================================================================

    /// <summary>按当前设置把样式和颜色刷上去（代价很小，可以直接反复调）。</summary>
    public static void Apply(bool force = false)
    {
        try
        {
            AppearanceSettings.EnsureLoaded();
            var style = AppearanceSettings.ButtonStyle;

            int hash = ComputeColorHash();
            bool styleChanged = style != _applied;
            if (!force && !styleChanged && hash == _appliedColorHash) return;
            _applied = style;
            _appliedColorHash = hash;



            if (styleChanged || force)
            {
                if (style == MainButtonStyle.Mod)
                {
                    // 回到 MOD：让替换重新跑一遍，把模组 PNG 贴回来
                    Patches.MainMenuButtonSpritePatch.Suppress = false;
                    Patches.MainMenuButtonSpritePatch.ReapplyModSprites();
                }
                else
                {
                    // 原版 / 亚克力：阻止替换（进主菜单时就不贴），并回滚这次会话已经贴上的
                    Patches.MainMenuButtonSpritePatch.Suppress = true;
                    Patches.MainMenuButtonSpritePatch.RestoreOriginals();
                }
            }

            // 颜色：只有亚克力会真的染色，其余一律还原成白色
            //（原版贴图本身是白的，靠 ButtonRolloverHandler 改色；我们写死颜色会吃掉悬浮反馈）
            foreach (var e in _entries)
            {
                if (e.Btn == null) continue;
                ApplyColorTo(e, style);
            }

            LightLogger.Log($"[MainMenuButtonStyler] 已应用 {style} 样式到 {_entries.Count} 个按钮" +
                            $"（可通过性={ColorsSelectable}）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuButtonStyler.Apply]", ex);
        }
    }

    /// <summary>只刷颜色（样式变了才需要动贴图）。</summary>
    private static void ApplyColorTo(Entry e, MainButtonStyle style)
    {
        try
        {
            var btn = e.Btn;

            if (style == MainButtonStyle.Acrylic)
            {
                // ⚠️⚠️ 照抄 FS（FinalSuspect/Patches/System/MainMenuManagerPatch.cs:129-130）：
                //      **只动 inactiveSprites / activeSprites 这两张，而且只改颜色不改贴图** ——
                //          inactive → (r,g,b, 0.8f)  常态半透明
                //          active   → (r,g,b, 1.0f)  悬浮/按下不透明
                //      圆角/描边交给原版贴图本身。
                //      我上一版把 disabled/selected/onClick 等**所有**状态容器都染成同一个色，
                //      形状和边框一起被改掉，所以"没达到效果"。
                var c = AppearanceSettings.GetButtonColor(e.Key) ?? DefaultAcrylic;

                SetStateColor(btn.inactiveSprites, new Color(c.r, c.g, c.b, 0.8f));
                SetStateColor(btn.activeSprites, new Color(c.r, c.g, c.b, 1.0f));

                // 其余状态还原成白，免得残留上一次的染色
                SetStateColor(btn.disabledSprites, Color.white);
                SetStateColor(btn.selectedSprites, Color.white);
                SetStateColor(btn.selectedInactiveSprites, Color.white);
                SetStateColor(btn.onClickSprites, Color.white);
                return;
            }

            // MOD / 原版：全部还原成白色
            //（原版贴图本来是白的，靠 ButtonRolloverHandler 改色；写死颜色会吃掉悬浮反馈）
            foreach (var sr in EnumerateStateRenderers(btn))
            {
                if (sr == null) continue;
                sr.color = Color.white;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuButtonStyler.ApplyColorTo] {e.Key}: {ex.Message}");
        }
    }
    /// <summary>亚克力样式没单独指定颜色时的默认色（FS 那种深灰紫感觉太抢，用中性深灰）。</summary>
    private static readonly Color DefaultAcrylic = new(0.30f, 0.30f, 0.34f, 1f);
    private static void SetStateColor(GameObject? state, Color c)
    {
        try
        {
            if (state == null) return;
            var sr = state.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = c;
        }
        catch { }
    }

    /// <summary>枚举一个按钮上所有状态图的渲染器。</summary>
    private static IEnumerable<SpriteRenderer?> EnumerateStateRenderers(PassiveButton btn)
    {
        GameObject?[] states =
        {
            btn.inactiveSprites, btn.activeSprites, btn.disabledSprites,
            btn.selectedSprites, btn.selectedInactiveSprites, btn.onClickSprites,
        };

        var seen = new HashSet<int>();
        foreach (var st in states)
        {
            if (st == null) continue;
            SpriteRenderer? sr = null;
            try { sr = st.GetComponent<SpriteRenderer>(); } catch { }
            if (sr == null) continue;
            if (seen.Add(sr.GetInstanceID())) yield return sr;
        }
    }

    private static int ComputeColorHash()
    {
        int h = 17;
        foreach (var kv in AppearanceSettings.ButtonColors)
            h = h * 31 + kv.Key.GetHashCode() + (kv.Value?.GetHashCode() ?? 0);
        return h;
    }

    /// <summary>把某个按钮的单独颜色清掉。</summary>
    public static void ClearColor(string key)
    {
        AppearanceSettings.SetButtonColor(key, null);
        Apply(force: true);
    }

    /// <summary>批量把所有按钮设成同一个颜色。</summary>
    public static void SetAllColors(Color color)
    {
        AppearanceSettings.EnsureLoaded();
        foreach (var e in _entries) AppearanceSettings.ButtonColors[e.Key] = AppearanceSettings.ToHex(color);
        AppearanceSettings.Save();
        Apply(force: true);
    }
}
