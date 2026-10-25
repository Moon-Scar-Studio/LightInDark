using System;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.Roles;

namespace LightInDark.Configuration;

/// <summary>
/// **职业配置项**（照 NOS 的 `IntegerConfiguration` / `BoolConfiguration` 那套改，2026-10-25）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么改】
///   原来是一堆"声明式记录"：
///   <code>
///   new RoleConfigItem { Key = "killCooldown", Type = ConfigType.Float, Default = 20f,
///                        Min = 5f, Max = 60f, Step = 2.5f, Suffix = ConfigSuffix.Second }
///   </code>
///   取值还得再写一遍字符串键：<c>ConfigRegistry.GetFloat($"role.{Code}.killCooldown")</c> ✗
///   → 键写错不报错（只是永远读到 0 ✗）、声明和使用两处各写一遍 ✗、读起来难受 ✗
///
/// 【NOS 的形状】（`NebulaPluginNova\Roles\Crewmate\Sheriff.cs:60-62`）
///   <code>
///   static internal readonly IRelativeCooldownConfiguration KillCoolDownOption =
///       NebulaAPI.Configurations.KillConfiguration("options.role.sheriff.killCoolDown", …);
///   static internal readonly IntegerConfiguration NumOfShotsOption =
///       NebulaAPI.Configurations.Configuration("options.role.sheriff.numOfShots", (1, 15), 3);
///   // 用值：KillCoolDownOption.GetValue() / NumOfShotsOption
///   // 绑定：base("sheriff", color, category, team, [KillCoolDownOption, NumOfShotsOption, …])
///   </code>
///
/// 【我们的形状】
///   <code>
///   // ① 声明（静态字段，本身就是配置项 ✓）
///   static readonly RoleFloatConfig KillCooldownCfg =
///       RoleConfig.Float("killCooldown", 20f, 5f, 60f, 2.5f, ConfigSuffix.Second);
///   static readonly RoleIntConfig KillsCfg = RoleConfig.Int("kills", 3, 1, 15);
///
///   // ② 绑定（数组里放对象 ✓，注册时自动补 role.&lt;CodeName&gt;. 前缀 ✓）
///   public override RoleConfigBase[] RoleConfiguration => [KillCooldownCfg, KillsCfg];
///
///   // ③ 取值（**不再写字符串键** ✓，未注册时自动退回默认值 ✓）
///   public override float KillCooldown => KillCooldownCfg.Value;
///   </code>
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public abstract class RoleConfigBase
{
    /// <summary>配置键（**不含**前缀；注册时自动补 <c>role.&lt;CodeName&gt;.</c>）。</summary>
    public string Key { get; }

    /// <summary>显示名；null = 按翻译键 <c>role.&lt;CodeName&gt;.&lt;Key&gt;</c> 解析。</summary>
    public string Label { get; }

    /// <summary>悬停详情；null = 不挂提示。</summary>
    public string Detail { get; }

    /// <summary>数值后缀（`s` / `%` …）。</summary>
    public ConfigSuffix Suffix { get; }

    /// <summary>UI 分派用的类型。</summary>
    public abstract ConfigType Type { get; }

    /// <summary>注册后绑定的真实配置项；未注册为 null。</summary>
    public ConfigItem? Item { get; private set; }

    /// <summary>
    /// **显示条件**（2026-10-25 用户要求："配置要一个 Action，控制这个配置显示/隐藏"）
    /// <para>返回 false → 配置界面里**不显示**这一项 ✓（null = 一直显示 ✓）</para>
    /// <para>注册前后都能设：<c>RoleConfig.Int(...).SetVisibleWhen(() =&gt; …)</c> ✓</para>
    /// </summary>
    public Func<bool> VisibleWhen { get; private set; }

    /// <summary>
    /// **依赖项**（另一个**布尔**配置对象）：它为 false 时本项隐藏 ✓
    /// <para>= NOS / 本工程老的 `SetDependsOn` 语义 ✓（比手写 `VisibleWhen` 省事 ✓）</para>
    /// </summary>
    public RoleConfigBase DependsOn { get; private set; }

    /// <summary>是否已注册到 <see cref="ConfigRegistry"/>。</summary>
    public bool IsRegistered => Item != null;

    protected RoleConfigBase(string key, string label, string detail, ConfigSuffix suffix)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("职业配置项的 Key 不能为空", nameof(key));
        Key = key;
        Label = label;
        Detail = detail;
        Suffix = suffix;
    }

    /// <summary>子类负责真正往 block 里注册（返回值会被存进 <see cref="Item"/>）。</summary>
    protected abstract ConfigItem Create(ConfigBlock block, string fullKey, string label, string detail);

    /// <summary>
    /// 由 <c>RoleConfigRegistrar</c> 调用：算出完整键 + 显示名 → 注册 → 绑定。
    /// ⚠️ 重复注册返回已有的 <see cref="Item"/>（幂等 ✓，配置界面重进不会炸 ✗）
    /// </summary>
    public ConfigItem Register(RoleTemplate role, ConfigBlock block, ConfigItem? countItem)
    {
        if (Item != null) return Item;

        string fullKey = $"role.{role.CodeName}.{Key}";
        // ⚠️ 必须写全限定：`Language` 既是命名空间 `LightInDark.Language` 又是类名 ✗
        string label = string.IsNullOrEmpty(Label)
            ? LightInDark.Language.Language.GetStringOrKey($"role.{role.CodeName}.{Key}", $"{role.Name} {Key}")
            : Label!;

        Item = Create(block, fullKey, label, Detail ?? "");
        if (Item == null) throw new InvalidOperationException($"[RoleConfig] {fullKey} 注册失败（Create 返回 null）");

        if (countItem != null) Item.SetDependsOn(countItem);      // 数量=0（职业关闭）时自动隐藏 ✓
        if (Suffix != ConfigSuffix.None) Item.WithSuffix(Suffix);

        // ★ 2026-10-25：把"显示条件 / 依赖项"落到真实配置项上 ✓
        //   ⚠️ 不能直接 `Item.SetDependsOn(依赖项)` ✗ —— 那会**覆盖**上面的"数量=0 隐藏" ✗
        //      → 统一用 `Item.VisibleWhen` 把两个条件**与**起来 ✓
        ApplyVisibility();

        return Item;
    }

    /// <summary>
    /// 把「计数依赖（数量&gt;0）+ <see cref="VisibleWhen"/> + <see cref="DependsOn"/>」合成一个条件，
    /// 写进真实配置项的 `VisibleWhen` ✓（`IsVisible` 里 `DependsOn` 与 `VisibleWhen` 是**与**关系）
    /// </summary>
    private void ApplyVisibility()
    {
        if (Item == null) return;

        var dependency = DependsOn;            // 另一个布尔配置对象
        var condition = VisibleWhen;           // 自定义条件
        var countItem = Item.DependsOn;        // 注册时挂的"数量"依赖（保留 ✓）

        if (dependency == null && condition == null)
        {
            Item.SetVisibleWhen(null);         // 不设 → 只看数量依赖 ✓（行为与改动前一致 ✓）
            return;
        }

        Item.SetVisibleWhen(() =>
        {
            if (countItem != null && !countItem.GetBool()) return false;          // 数量=0 隐藏 ✓
            if (dependency != null && dependency.Item != null && !dependency.Item.GetBool()) return false;  // 依赖项关掉 → 隐藏 ✓
            if (condition != null && !condition()) return false;                  // 自定义条件 ✓
            return true;
        });
    }

    /// <summary>
    /// **设置显示条件**（false → 界面里隐藏 ✓）。注册前/后调用都有效 ✓
    /// <para>例：<c>MaxSidekicksCfg.SetVisibleWhen(() =&gt; CanCreateSidekickCfg.Value);</c> ✓</para>
    /// </summary>
    /// <param name="condition">显示条件（null = 恢复"一直显示" ✓）</param>
    public RoleConfigBase SetVisibleWhen(Func<bool> condition)
    {
        VisibleWhen = condition;
        ApplyVisibility();
        return this;
    }

    /// <summary>
    /// **依赖另一个布尔配置**（它为 false 时本项隐藏 ✓）。注册前/后调用都有效 ✓
    /// <para>例：<c>MaxSidekicksCfg.SetDependsOn(CanCreateSidekickCfg);</c> ✓（比手写 lambda 省事 ✓）</para>
    /// </summary>
    /// <param name="other">被依赖的**布尔**配置对象（null = 取消依赖 ✓）</param>
    public RoleConfigBase SetDependsOn(RoleConfigBase other)
    {
        DependsOn = other;
        ApplyVisibility();
        return this;
    }

    public override string ToString() => $"{Key}={(Item != null ? Item.GetValueText() : "(未注册)")}";
}

/// <summary>布尔型职业配置（勾选框）。</summary>
public sealed class RoleBoolConfig : RoleConfigBase
{
    /// <summary>默认值（未注册时 <see cref="Value"/> 也返回它）。</summary>
    public bool Default { get; }

    public RoleBoolConfig(string key, bool defaultValue, string label = null, string detail = null)
        : base(key, label, detail, ConfigSuffix.None)
        => Default = defaultValue;

    public override ConfigType Type => ConfigType.Bool;

    /// <summary>设置显示条件（返回**自身类型** ✓，方便写进 `static readonly RoleBoolConfig`）</summary>
    public new RoleBoolConfig SetVisibleWhen(Func<bool> condition) { base.SetVisibleWhen(condition); return this; }

    /// <summary>依赖另一个布尔配置（返回**自身类型** ✓）</summary>
    public new RoleBoolConfig SetDependsOn(RoleConfigBase other) { base.SetDependsOn(other); return this; }

    /// <summary>当前值 ✓（未注册时退回 <see cref="Default"/>）</summary>
    public bool Value => Item != null ? Item.GetBool() : Default;

    protected override ConfigItem Create(ConfigBlock block, string fullKey, string label, string detail)
        => block.AddConfiguration(fullKey, Default, label, detail);
}

/// <summary>整数型职业配置（原版 NumberOption，按 Step 步进）。</summary>
public sealed class RoleIntConfig : RoleConfigBase
{
    public int Default { get; }
    public int Min { get; }
    public int Max { get; }
    public int Step { get; }

    public RoleIntConfig(string key, int defaultValue, int min, int max, int step = 1,
        string label = null, string detail = null)
        : base(key, label, detail, ConfigSuffix.None)
    {
        Default = defaultValue;
        Min = min;
        Max = max;
        Step = step <= 0 ? 1 : step;
    }

    public override ConfigType Type => ConfigType.Int;

    /// <summary>设置显示条件（返回**自身类型** ✓）</summary>
    public new RoleIntConfig SetVisibleWhen(Func<bool> condition) { base.SetVisibleWhen(condition); return this; }

    /// <summary>当前值 ✓（未注册时退回 <see cref="Default"/>）</summary>
    public int Value => Item != null ? Item.GetInt() : Default;

    protected override ConfigItem Create(ConfigBlock block, string fullKey, string label, string detail)
        => block.AddConfiguration(fullKey, Default, Min, Max, Step, label, detail);
}

/// <summary>浮点型职业配置（原版 NumberOption，小数位由 Step 决定）。</summary>
public sealed class RoleFloatConfig : RoleConfigBase
{
    public float Default { get; }
    public float Min { get; }
    public float Max { get; }
    public float Step { get; }

    public RoleFloatConfig(string key, float defaultValue, float min, float max, float step,
        ConfigSuffix suffix = ConfigSuffix.None, string label = null, string detail = null)
        : base(key, label, detail, suffix)
    {
        Default = defaultValue;
        Min = min;
        Max = max;
        Step = step <= 0f ? 1f : step;
    }

    public override ConfigType Type => ConfigType.Float;

    /// <summary>设置显示条件（返回**自身类型** ✓）</summary>
    public new RoleFloatConfig SetVisibleWhen(Func<bool> condition) { base.SetVisibleWhen(condition); return this; }

    /// <summary>当前值 ✓（未注册时退回 <see cref="Default"/>）</summary>
    public float Value => Item != null ? Item.GetFloat() : Default;

    protected override ConfigItem Create(ConfigBlock block, string fullKey, string label, string detail)
        => block.AddConfiguration(fullKey, Default, Min, Max, Step, label, detail);
}

/// <summary>枚举/预设型职业配置（原版 StringOption 语义：候选表里循环切换）。</summary>
public sealed class RoleValueConfig : RoleConfigBase
{
    /// <summary>候选表（首项 = 默认值，与 ConfigBlock 的语义一致 ✓）。</summary>
    public string[] Selections { get; }

    public RoleValueConfig(string key, string[] selections, string label = null, string detail = null)
        : base(key, label, detail, ConfigSuffix.None)
    {
        if (selections == null || selections.Length == 0)
            throw new ArgumentException($"职业配置项 {key} 的候选表不能为空", nameof(selections));
        Selections = (string[])selections.Clone();
    }

    public override ConfigType Type => ConfigType.Value;

    /// <summary>设置显示条件（返回**自身类型** ✓）</summary>
    public new RoleValueConfig SetVisibleWhen(Func<bool> condition) { base.SetVisibleWhen(condition); return this; }

    /// <summary>当前候选索引 ✓</summary>
    public int Index => Item != null ? Item.Selection : 0;

    /// <summary>当前候选名字 ✓（未注册时是首项）</summary>
    public string Text => Item != null ? Item.GetSelectionName() : Selections[0];

    protected override ConfigItem Create(ConfigBlock block, string fullKey, string label, string detail)
        => block.AddConfiguration(fullKey, Selections, label, detail);
}

/// <summary>
/// 职业配置项的**工厂**（照 NOS 的 <c>NebulaAPI.Configurations.Configuration(...)</c> 形状）。
/// 用法见 <see cref="RoleConfigBase"/> 的注释。
/// </summary>
public static class RoleConfig
{
    /// <summary>勾选框。</summary>
    public static RoleBoolConfig Bool(string key, bool defaultValue, string label = null, string detail = null,
        Func<bool> visibleWhen = null)
        => new RoleBoolConfig(key, defaultValue, label, detail).SetVisibleWhen(visibleWhen);

    /// <summary>整数（含范围与步进，写入时 clamp ✓）。</summary>
    public static RoleIntConfig Int(string key, int defaultValue, int min, int max, int step = 1,
        string label = null, string detail = null, Func<bool> visibleWhen = null)
        => new RoleIntConfig(key, defaultValue, min, max, step, label, detail).SetVisibleWhen(visibleWhen);

    /// <summary>浮点（含范围与步进，写入时 clamp ✓）。</summary>
    public static RoleFloatConfig Float(string key, float defaultValue, float min, float max, float step,
        ConfigSuffix suffix = ConfigSuffix.None, string label = null, string detail = null,
        Func<bool> visibleWhen = null)
        => new RoleFloatConfig(key, defaultValue, min, max, step, suffix, label, detail).SetVisibleWhen(visibleWhen);

    /// <summary>枚举/预设（候选表循环切换）。</summary>
    public static RoleValueConfig Value(string key, string[] selections, string label = null, string detail = null,
        Func<bool> visibleWhen = null)
        => new RoleValueConfig(key, selections, label, detail).SetVisibleWhen(visibleWhen);
}
