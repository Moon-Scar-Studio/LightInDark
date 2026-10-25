using System;
using LightInDark.Core;
using LightInDark.Game;

namespace LightInDark.Modifiers.Buffs
{
    /// <summary>
    /// Buff **模板**：可直接拿来用的具体定义。
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【怎么自己写一个 Buff 模板】—— 从"最省事"到"最完整"三档
    ///
    /// ★ **0. 连类都不用写（推荐，2026-10-25 用户要求："加一个很简单的注册方式，耦合低一点"）**
    ///    <code>
    ///    // 一行：key、名字、颜色、时长、生效回调、结束回调 —— 自动进 All / BuffRegistry ✓
    ///    var invisible = BuffStorage.Add(
    ///        "invisible", "隐身", LightInDark.Color.Gray, 8f,
    ///        onApply:  p =&gt; PlayerAppearance.SetAnonymousAll(true),
    ///        onExpire: p =&gt; PlayerAppearance.SetAnonymousAll(false));
    ///    </code>
    ///    ⚠️ `onApply` 改过的状态**必须在 `onExpire` 里改回去** ✓（死亡/换局也会走到 onExpire ✓）
    ///
    /// 1. 属性型（速度/视野/体型）—— **不用写类**，直接用现成的参数化定义：
    ///    <code>
    ///    BuffStorage.Add(new SpeedBuff(1.4f, 8f, "haste", "加速"));        // 速度 ×1.4，8 秒
    ///    BuffStorage.Add(new SpeedBuff(0f, 3f, "freeze", "定身"));         // 速度 ×0（不能动）
    ///    BuffStorage.Add(new ViewDistanceBuff(2f, 6f, "eye", "千里眼"));   // 视野 ×2
    ///    BuffStorage.Add(new ScaleBuff(0.5f, 9f, "shrink", "缩小"));       // 体型 ×0.5
    ///    </code>
    ///    属性聚合在 `BuffManager.ApplySpeed` / `ApplyAttributes` 里按**类型**做，所以必须用这三个类 ✓
    ///
    /// 2. 纯标记型（没有任何属性效果）—— 用 <see cref="MarkBuff"/>：
    ///    <code>
    ///    BuffStorage.Add(new MarkBuff("marked", "被标记", LightInDark.Color.ModGolden, 30f, canPassMeeting: true));
    ///    player.HasBuff(BuffStorage.Marked);      // 别的系统按 key 判状态 ✓
    ///    </code>
    ///
    /// 3. 要**每帧逻辑 / 复杂状态** —— 写类（仿 `Examples\ShadowBuff.cs`）：
    ///    <code>
    ///    public sealed class MyBuff : BuffDefinition
    ///    {
    ///        public static readonly MyBuff Instance = new();
    ///        private MyBuff() : base("my_buff", "我的Buff", LightInDark.Color.Gold, 10f) { }
    ///        public override RuntimeBuff CreateInstance(Player player) =&gt; new Instance_(this, player);
    ///        private sealed class Instance_ : RuntimeBuff
    ///        {
    ///            public Instance_(MyBuff d, Player p) : base(d, p, d.DefaultDuration) { }
    ///            protected override void OnApply()  { /* 生效 */ }
    ///            protected override void OnTick(float dt) { /* 每帧 */ }
    ///            protected override void OnExpire() { /* 恢复 */ }
    ///        }
    ///    }
    ///    </code>
    ///    ⚠️ `Instance` 必须用**传进来的 definition**（不是静态单例 ✗）—— 否则 `new ShadowBuff(8f)` 这种
    ///       不同时长的实例会读错时长（这个坑 `ShadowBuff` 曾经踩过，2026-10-25 已修 ✓）
    ///
    /// ⚠️ 写完 **用 `BuffStorage.Add(...)` 注册** ✓ —— 否则 RPC 同步到别的客户端时
    ///    `BuffRegistry` 查不到 → 只能打 warning 丢弃 ✗
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class BuffTemplates
    {
        // 这里只放"定义类型"；具体实例由 BuffStorage 持有 ✓
    }

    /// <summary>
    /// **纯标记 Buff**：没有任何属性效果，只表示"身上挂着这个状态"✓
    /// <para>用途：职业/系统之间传状态（"被通缉"、"被标记"、"投过票"…）而不用自己写 RPC ✓</para>
    /// <para>判定：<c>player.HasBuff(key)</c> 或 <c>player.HasBuff(BuffStorage.Marked)</c> ✓</para>
    /// </summary>
    public sealed class MarkBuff : BuffDefinition
    {
        public MarkBuff(string key, string name, LightInDark.Color color, float duration = 30f,
                        bool canPassMeeting = true, string description = "纯标记，没有属性效果")
            : base(key, name, color, duration, canPassMeeting, description: description) { }

        public override RuntimeBuff CreateInstance(Player player) => new SimpleRuntimeBuff(this, player, DefaultDuration);
    }

    /// <summary>
    /// **回调式 Buff**：不用写类，用委托决定生效/结束/每帧做什么 ✓
    /// <para>由 `BuffStorage.Add(key, name, color, duration, onApply, onExpire, …)` 创建 ✓</para>
    /// <para>⚠️ `onApply` 改过的状态**必须在 `onExpire` 里恢复**（死亡/换局也会触发 onExpire ✓）</para>
    /// </summary>
    public sealed class SimpleBuff : BuffDefinition
    {
        private readonly Action<Player> _onApply;
        private readonly Action<Player> _onExpire;
        private readonly Action<Player, float> _onTick;

        /// <summary>创建回调式 Buff ✓</summary>
        /// <param name="key">唯一键（RPC 同步用 ✓）</param>
        /// <param name="name">显示名 ✓</param>
        /// <param name="color">主题色 ✓</param>
        /// <param name="duration">默认时长（秒）；`-1` = 整局 ✓</param>
        /// <param name="onApply">生效回调（参数 = 挂上 Buff 的玩家 ✓）</param>
        /// <param name="onExpire">结束/被移除回调 ✓</param>
        /// <param name="onTick">每帧回调（参数 = 玩家 + 本帧秒数 ✓）</param>
        /// <param name="canPassMeeting">会议结束后是否保留（默认 false ✓）</param>
        /// <param name="priority">展示排序（默认 0 ✓）</param>
        /// <param name="description">说明文本 ✓</param>
        public SimpleBuff(string key, string name, LightInDark.Color color, float duration,
                          Action<Player> onApply = null, Action<Player> onExpire = null,
                          Action<Player, float> onTick = null,
                          bool canPassMeeting = false, int priority = 0, string description = "")
            : base(key, name, color, duration, canPassMeeting, priority, description)
        {
            _onApply = onApply;
            _onExpire = onExpire;
            _onTick = onTick;
        }

        public override RuntimeBuff CreateInstance(Player player) => new Instance(this, player);

        private sealed class Instance : RuntimeBuff
        {
            public Instance(SimpleBuff definition, Player player)
                : base(definition, player, definition.DefaultDuration) { }

            private SimpleBuff Def => (SimpleBuff)Definition;

            protected override void OnApply()
            {
                try { Def._onApply?.Invoke(MyPlayer); }
                catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key}.onApply 失败：{ex.Message}"); }
            }

            protected override void OnTick(float dt)
            {
                try { Def._onTick?.Invoke(MyPlayer, dt); }
                catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key}.onTick 失败：{ex.Message}"); }
            }

            protected override void OnExpire()
            {
                try { Def._onExpire?.Invoke(MyPlayer); }
                catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key}.onExpire 失败：{ex.Message}"); }
            }
        }
    }
}

