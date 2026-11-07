using LightInDark.Game;
using LightInDark.Visual;

namespace LightInDark.Modifiers.Buffs.Examples
{
    /// <summary>
    /// **小黑人**：让**持有这个 Buff 的玩家**清空装扮、身体变灰、名字隐藏。
    ///
    /// · 时长在构造时给（默认 15 秒；`-1` = 整局）
    /// · `key` 可改：同一局能并存多个不同来源的小黑人（`new ShadowBuff(8f, "shadow_trap")` 与默认 `"shadow"` 互不影响）
    /// · 网络同步由 <see cref="LightInDark.Modifiers.Buffs.BuffSync"/> 负责：每个客户端都会在目标玩家身上推同一层，
    ///   外观层标签按 `InstanceKey` 派生，所以多个实例互不干扰、各自到期各自恢复
    /// </summary>
    public sealed class ShadowBuff : BuffDefinition
    {
        /// <summary>默认实例：15 秒、key = `"shadow"`。</summary>
        public static readonly ShadowBuff MyBuff = new();

        /// <param name="duration">持续秒数（默认 15；`-1` = 整局）</param>
        /// <param name="key">唯一键（默认 `"shadow"`；同局要并存多个就给它不同的 key）</param>
        /// <param name="name">显示名（默认"小黑人"）</param>
        public ShadowBuff(float duration = 15f, string key = "shadow", string name = "小黑人")
            : base(key, name, LightInDark.Color.Gray, duration, canPassMeeting: true,
                   description: "清空装扮、身体变灰并隐藏名字") { }

        public override RuntimeBuff CreateInstance(Player player) => new Instance(this, player);

        private sealed class Instance : RuntimeBuff
        {
            private const string TagPrefix = "buff.shadow#";

            public Instance(ShadowBuff definition, Player player)
                : base(definition, player, definition.DefaultDuration) { }

            // 每个实例一个独立标签，避免"先到期的把还在生效的一起弹掉"
            private string LayerTag => TagPrefix + InstanceKey;

            protected override void OnApply()
                => OutfitStack.Push(MyPlayer?.Control, PlayerAppearance.CreateAnonymousLayer(LayerTag));

            protected override void OnExpire()
                => OutfitStack.Pop(MyPlayer?.Control, LayerTag);
        }
    }
}
