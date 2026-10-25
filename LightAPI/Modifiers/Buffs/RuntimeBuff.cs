using System;
using LightInDark.Core;
using LightInDark.Game;

namespace LightInDark.Modifiers.Buffs
{
    /// <summary>
    /// Buff 运行时实例：绑定某个玩家，由 <see cref="BuffManager"/> 统一计时。
    /// 时长由管理器扣减，子类只实现"生效 / 每帧 / 结束"三个钩子。
    /// </summary>
    public abstract class RuntimeBuff : IGameOperator, ILifespan
    {
        public BuffDefinition Definition { get; }
        public Player MyPlayer { get; }

        /// <summary>
        /// **实例键**（同一种 Buff 可按不同来源并存，也是移除时的**推荐**选择器 ✓）。
        ///
        /// ⚠️ 2026-10-25 用户要求：`GiveBuff(buff, duration, key)` 的第三个参数就是这个 ✓
        ///   · 空 = 没给 key → 同类型只留一个（旧行为 ✓）
        ///   · 非空 → (类型 + key) 组成唯一键 ✓，同类型可以并存多个 ✓
        /// </summary>
        public string InstanceKey { get; internal set; } = "";

        /// <summary>本实例的总时长（&lt; 0 = 整局）。</summary>
        public float Duration { get; internal set; }

        /// <summary>剩余时间（整局 Buff 恒等于 Duration）。</summary>
        public float Remaining { get; internal set; }

        public bool IsPermanent => Duration < 0f;
        public bool IsExpired => !IsPermanent && Remaining <= 0f;
        public bool AmOwner => MyPlayer != null && MyPlayer.AmOwner;

        private bool _released;
        // 只认"控件没了/已释放"，不把死亡算作失效（死亡时 Buff 继续计时）
        public bool IsDeadObject => _released || MyPlayer == null || MyPlayer.Control == null;

        protected RuntimeBuff(BuffDefinition definition, Player player, float duration)
        {
            Definition = definition;
            MyPlayer = player;
            Duration = duration;
            Remaining = duration;
        }

        internal void Apply()
        {
            try { OnApply(); }
            catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key} OnApply 失败: {ex.Message}"); }
        }

        internal void Tick(float dt)
        {
            if (!IsPermanent) Remaining -= dt;
            try { OnTick(dt); }
            catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key} OnTick 失败: {ex.Message}"); }
        }

        internal void Expire()
        {
            if (_released) return;
            try { OnExpire(); }
            catch (Exception ex) { LightLogger.LogWarning($"[Buff] {Definition.Key} OnExpire 失败: {ex.Message}"); }
            Release();
        }

        /// <summary>立即移除（会触发 OnExpire）。</summary>
        public void Remove() => Expire();

        protected virtual void OnApply() { }
        protected virtual void OnTick(float dt) { }
        protected virtual void OnExpire() { }

        public void Release() => _released = true;

        void IGameOperator.OnReleased() { }
    }

    /// <summary>无额外行为的通用实例（属性 Buff 直接用它）。</summary>
    internal sealed class SimpleRuntimeBuff : RuntimeBuff
    {
        public SimpleRuntimeBuff(BuffDefinition definition, Player player, float duration)
            : base(definition, player, duration) { }
    }
}
