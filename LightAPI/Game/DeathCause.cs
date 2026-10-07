using System;
using System.Collections.Generic;

namespace LightInDark.Game;

/// <summary>
/// **死因注册表** —— 照抄 Nebula 的做法（`NebulaAPI\Text\NoSTextTag.cs` 的 `PlayerStates` +
/// `NebulaPluginNova\Player\PlayerModInfo.cs` 里 `static PlayerState()` 的赋值注册）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么不能用枚举】
///   我们原来把死因写成 `PlayerState` **枚举** ✗ → 模组**没法新增自己的死因**
///   （"被豺狼撕咬 / 被狙击手击穿 / 中毒身亡…" 只能挤进那几个固定值里 ✗）
///   而且枚举还有个已经踩过的坑：**有人硬编码了序号**
///   （`DeathReason` 实际是 `Exile=0, Kill=1`，代码按"0=Kill"写 → **击杀被记成放逐** ✗✗）
///
/// 【Nebula 的思路（本文件照抄）】
///   死因 = 一个**可注册的文本标签** ✓：
///     · `PlayerStates.Alive / Dead / Exiled / Misfired / Sniped / Beaten / Guessed / Misguessed …`
///     · 每个标签有**稳定 id** + **可翻译文本**，模组可以自己再注册新的 ✓
///     · 玩家的死亡信息里除了主状态，还带一个 `ExtraDeadInfo`（额外死因记录）✓
///
/// 【我们的落地】
///   · 死因用**稳定字符串 id** 标识 ✓（与 `RoleData.RegisterId` / `SetRoleByCode` 同一条规矩：
///     **永不使用"按注册顺序编的号"** ✓）
///   · `PlayerState` 枚举**保留**（现有调用方不用改 ✓），由 <see cref="FromPlayerState"/> 映射成内置 id ✓
///   · 显示走 <see cref="Resolve"/>：**自定义优先**，没注册就退回老的枚举文案 ✓
///   · 同步：`RpcDefinitions.MurderPlayer/Suicide` 本来就是 `[LidRPC]` ✓
///     → 只要把 `causeId` 当参数传下去，**所有客户端自动一致** ✓（不需要额外 RPC ✓）
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public static class DeathCause
{
    // ── 内置死因 id（原版语义；等于我们已有的 PlayerState 那几档）──
    public const string Alive = "lid.state.alive";
    public const string Dead = "lid.state.dead";                 // 普通死亡（含被击杀）
    public const string BeKilled = "lid.state.bekilled";         // 被击杀（更精确的说法）
    public const string Exiled = "lid.state.exiled";             // 被放逐
    public const string Suicide = "lid.state.suicide";
    public const string Guessed = "lid.state.guessed";           // 被猜中
    public const string Misfired = "lid.state.misfired";         // 走火
    public const string Disconnected = "lid.state.disconnected"; // 断线

    private sealed class Entry
    {
        public string Id = "";
        public Func<string> Text = () => "";
    }

    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private static readonly List<string> _order = new();

    static DeathCause() => RegisterBuiltIns();

    /// <summary>注册（或覆盖）一个死因的显示方式。模组加载时调用一次即可 ✓</summary>
    /// <param name="causeId">稳定 id，建议 <c>lid.death.xxx</c> 形式（**不要用序号** ✗）</param>
    /// <param name="text">返回显示文本；可用 <c>{0}</c> 占位凶手名 ✓</param>
    public static void Register(string causeId, Func<string> text)
    {
        try
        {
            if (string.IsNullOrEmpty(causeId) || text == null) return;

            if (!_entries.TryGetValue(causeId, out var e))
            {
                e = new Entry { Id = causeId };
                _entries[causeId] = e;
                _order.Add(causeId);
            }
            e.Text = text;
        }
        catch (Exception ex)
        {
            Core.LightLogger.LogError("[DeathCause.Register]", ex);
        }
    }

    /// <summary>注册（语言键版）✓ —— 文本随当前语言变化。</summary>
    public static void Register(string causeId, string langKey, string fallback = null)
        => Register(causeId, () => LightInDark.Language.Language.Translate(langKey, fallback ?? langKey));

    public static bool IsRegistered(string causeId)
        => !string.IsNullOrEmpty(causeId) && _entries.ContainsKey(causeId);

    /// <summary>全部已注册的死因 id（顺序 = 注册顺序；给调试/工具用 ✓）</summary>
    public static IReadOnlyList<string> AllIds => _order;

    /// <summary>
    /// 取显示文本。
    /// ⚠️ **未注册返回 null**（让调用方退回老的枚举文案 ✓）—— 而不是硬编一个"未知" ✗
    /// <paramref name="killerName"/> 会替换文本里的 <c>{0}</c> ✓
    /// </summary>
    public static string Resolve(string causeId, string killerName = null)
    {
        try
        {
            if (string.IsNullOrEmpty(causeId)) return null;
            if (!_entries.TryGetValue(causeId, out var e)) return null;

            string text = e.Text();
            if (string.IsNullOrEmpty(text)) return null;

            if (!string.IsNullOrEmpty(killerName) && text.Contains("{0}"))
                text = text.Replace("{0}", killerName);

            // 凶手名拿不到时别留下光秃秃的 `{0}` ✗（原版直接触发的死亡就没有凶手 ✓）
            if (text.Contains("{0}")) text = text.Replace("{0}", "").Replace("  ", " ").Trim();

            return text;
        }
        catch (Exception ex)
        {
            Core.LightLogger.LogWarning($"[DeathCause.Resolve] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// `PlayerState` 枚举 → 内置死因 id。
    /// ⚠️ 用**枚举名**映射，永远不碰序号 ✓（就是"击杀记成放逐"那次的教训 ✗）
    /// </summary>
    public static string FromPlayerState(PlayerState state) => state switch
    {
        PlayerState.Dead => Dead,
        PlayerState.Suicide => Suicide,
        PlayerState.BeGuessed => Guessed,
        PlayerState.BeKilled => BeKilled,
        PlayerState.GoOff => Misfired,
        PlayerState.Exile => Exiled,
        _ => Dead,
    };

    /// <summary>注册内置死因（静态构造里自动调用 ✓）。</summary>
    private static void RegisterBuiltIns()
    {
        Register(Alive, "death.alive", "存活");
        Register(Dead, "death.dead", "死亡");
        Register(BeKilled, "death.bekilled", "被 {0} 击杀");
        Register(Exiled, "death.exiled", "被放逐");
        Register(Suicide, "death.suicide", "自杀");
        Register(Guessed, "death.guessed", "被 {0} 猜中");
        Register(Misfired, "death.misfired", "走火（{0}）");
        Register(Disconnected, "death.disconnected", "断线");
    }
}
