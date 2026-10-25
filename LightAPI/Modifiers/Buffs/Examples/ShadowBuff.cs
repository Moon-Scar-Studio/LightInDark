using LightInDark.Game;
using LightInDark.Visual;

namespace LightInDark.Modifiers.Buffs.Examples
{
    /// <summary>
    /// **小黑人**：全场玩家清空装扮、身体变灰、名字隐藏 ✓
    ///
    /// ⚠️ 2026-10-25 用户要求："小黑人就非得锁死 15s 吗" → **不再锁死** ✓
    ///   · 时长在**构造时给**（默认 15 秒 ✓）→ <c>new ShadowBuff(8f)</c> / <c>new ShadowBuff(30f)</c> ✓
    ///   · 也可以 `GiveBuff(BuffStorage.Shadow, 8f)` 覆盖单次时长 ✓
    ///   · `key` 也能改 → 同一局可以并存好几个不同来源的小黑人 ✓
    ///     （`new ShadowBuff(8f, "shadow_trap")` 与默认的 `"shadow"` 互不影响 ✓）
    ///   · 默认的那个单例仍然在：<see cref="MyBuff"/>（15 秒 ✓）
    /// </summary>
    public sealed class ShadowBuff : BuffDefinition
    {
        /// <summary>默认实例：15 秒、key = `"shadow"` ✓</summary>
        public static readonly ShadowBuff MyBuff = new();

        /// <summary>创建小黑人定义 ✓</summary>
        /// <param name="duration">持续秒数（默认 15 ✓；`-1` = 整局 ✓）</param>
        /// <param name="key">唯一键（默认 `"shadow"` ✓；同局要并存多个就给它不同的 key ✓）</param>
        /// <param name="name">显示名（默认"小黑人" ✓）</param>
        public ShadowBuff(float duration = 15f, string key = "shadow", string name = "小黑人")
            : base(key, name, LightInDark.Color.Gray, duration, canPassMeeting: true,
                   description: "全场玩家清空装扮、身体变灰并隐藏名字") { }

        public override RuntimeBuff CreateInstance(Player player) => new Instance(this, player);

        private sealed class Instance : RuntimeBuff
        {
            // ⚠️ 用传进来的 definition（不是 MyBuff）→ 每个实例的时长/key 都是自己那份 ✓
            public Instance(ShadowBuff definition, Player player)
                : base(definition, player, definition.DefaultDuration) { }

            // ⚠️ 2026-10-25：改成**外观栈**（照 NOS 的 outfit 栈 ✓）
            //   旧写法 OnApply/OnExpire 直接开关**全场** bool ✗ → 两个 Buff 叠加时先结束的会把全场恢复 ✗
            //   现在只给"自己这个玩家"推一层，按 Tag 弹（同 Tag 多个实例会覆盖，不会互相踩 ✓）
            private const string Tag = "buff.shadow.appearance";

            protected override void OnApply()
                => OutfitStack.Push(MyPlayer?.Control, PlayerAppearance.CreateAnonymousLayer(Tag));

            protected override void OnExpire()
                => OutfitStack.Pop(MyPlayer?.Control, Tag);
        }
    }
}
