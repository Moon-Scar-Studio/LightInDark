using UnityEngine;

namespace LightInDark.Events
{
    // =====================================================================
    //  玩家生死事件
    // =====================================================================

    /// <summary>玩家自杀时触发。</summary>
    public class PlayerSuicideEvent : BasePlayerEvent
    {
        public string Reason { get; init; } = "suicide";
        public bool NeedLog { get; init; } = true;
        public Game.PlayerState State { get; init; } = Game.PlayerState.Suicide;
        public PlayerSuicideEvent() { }
        public PlayerSuicideEvent(PlayerControl player, string reason = "suicide", Game.PlayerState state = Game.PlayerState.Suicide) : base(player) { Reason = reason; State = state; }
    }

    /// <summary>玩家击杀玩家时触发。</summary>
    /// <remarks>
    /// 2026-10-25 用户要求补参数（对照 NOS 的 `PlayerMurderedEvent`）✓：
    ///   · <see cref="Body"/> / <see cref="DeadBodyPosition"/>：这次击杀生成的**那具尸体** ✓（自杀/无尸体杀法 → null）
    ///   · <see cref="WithDeadBody"/> / <see cref="WithBlink"/>：有没有尸体 / 有没有原版击杀动画 ✓
    /// </remarks>
    public class PlayerMurderEvent : BasePlayerEvent
    {
        public PlayerControl Killer => Player;
        public PlayerControl Victim { get; init; }
        public Game.PlayerState State { get; init; } = Game.PlayerState.BeKilled;

        /// <summary>这次击杀**生成的尸体**（没尸体时 null ✓）</summary>
        /// <remarks>
        /// ⚠️ 原版尸体是在 `KillAnimation.CoPerformKill` **协程里**创建的（`KillAnimation.cs:22`），
        ///    而击杀事件在协程**启动前**就触发了 → 当场读可能是 null ✗
        ///    → 所以这里是**实时查找**（`DeadBodyUtil.FindByPlayer(Victim)` ✓），
        ///      调用方晚一点读（同一帧末尾/下一帧）就能拿到 ✓
        ///    · 放逐 / 自杀 / `Vanish` 这类无尸体杀法 → 永远 null（正常 ✓）
        ///    · **想稳定拿尸体请用 <see cref="PlayerDeathEvent.Body"/>**（那时尸体一定已建好 ✓）
        /// </remarks>
        public DeadBody Body
        {
            get => _body != null ? _body : Game.DeadBodyUtil.FindByPlayer(Victim);
            init => _body = value;
        }

        private readonly DeadBody _body;

        /// <summary>尸体的世界坐标（没尸体时 null ✓；同样是实时查找 ✓）</summary>
        public Vector2? DeadBodyPosition
        {
            get
            {
                if (_position.HasValue) return _position;
                var body = Body;
                if (body == null) return null;
                var pos = body.transform.position;
                return new Vector2(pos.x, pos.y);
            }
            init => _position = value;
        }

        private readonly Vector2? _position;

        /// <summary>是否真的留了尸体 ✓（= <see cref="Body"/> 不为 null）</summary>
        public bool WithDeadBody => Body != null;

        /// <summary>是否播了**原版击杀动画**（黑屏闪光 ✓）</summary>
        public bool WithBlink { get; init; }

        public PlayerMurderEvent() { }
        public PlayerMurderEvent(PlayerControl killer, PlayerControl victim, Game.PlayerState state = Game.PlayerState.BeKilled,
                                 DeadBody body = null, Vector2? deadBodyPosition = null, bool withBlink = true)
            : base(killer) { Victim = victim; State = state; _body = body; _position = deadBodyPosition; WithBlink = withBlink; }
    }

    /// <summary>尝试击杀时触发（可阻止）。</summary>
    public class PlayerTryMurderEvent : BaseCancelablePlayerEvent
    {
        public PlayerControl Killer => Player;
        public PlayerControl Victim { get; init; }
        public PlayerTryMurderEvent() { }
        public PlayerTryMurderEvent(PlayerControl killer, PlayerControl victim) : base(killer) { Victim = victim; }
    }

    /// <summary>玩家死亡时触发。</summary>
    /// <remarks>
    /// 2026-10-25 用户要求补参数（"比如 PlayerDeathEvent 获取死者 body 的方式"）✓，对照 NOS 的 `PlayerDieEvent`：
    ///   · <see cref="Body"/>：死者留下的**那具尸体** ✓
    ///     ← 原版靠 `DeadBody.ParentId` 反查（见 `Game.DeadBodyUtil` ✓，原版没有公开的反查接口 ✗）
    ///   · <see cref="DeadBodyPosition"/>：尸体坐标 ✓（做"指引尸体"类技能直接能用 ✓）
    ///   · **放逐 / 自杀 / 无尸体杀法 → `Body == null`**（那不是错误 ✓）
    ///   · <see cref="WithBlink"/>：有没有播原版击杀动画 ✓
    /// </remarks>
    public class PlayerDeathEvent : BasePlayerEvent
    {
        public PlayerControl Killer { get; init; }
        public DeathReason Reason { get; init; }
        public Game.PlayerState State { get; init; } = Game.PlayerState.Dead;

        /// <summary>死者留下的**尸体**（放逐/自杀/无尸体杀法 → null ✓）</summary>
        public DeadBody Body { get; init; }

        /// <summary>尸体的世界坐标（没尸体时 null ✓）</summary>
        public Vector2? DeadBodyPosition { get; init; }

        /// <summary>是否留了尸体 ✓（= <see cref="Body"/> 不为 null）</summary>
        public bool WithDeadBody => Body != null;

        /// <summary>是否播了**原版击杀动画** ✓</summary>
        public bool WithBlink { get; init; }

        public PlayerDeathEvent() { }
        public PlayerDeathEvent(PlayerControl player, DeathReason reason, PlayerControl killer = null,
                                Game.PlayerState state = Game.PlayerState.Dead,
                                DeadBody body = null, Vector2? deadBodyPosition = null, bool withBlink = true)
            : base(player) { Reason = reason; Killer = killer; State = state; Body = body; DeadBodyPosition = deadBodyPosition; WithBlink = withBlink; }
    }

    /// <summary>玩家复活时触发。</summary>
    public class PlayerReviveEvent : BasePlayerEvent
    {
        public PlayerControl Healer { get; init; }
        public PlayerReviveEvent() { }
        public PlayerReviveEvent(PlayerControl player, PlayerControl healer = null) : base(player) { Healer = healer; }
    }

    /// <summary>玩家断开连接时触发。</summary>
    public class PlayerDisconnectEvent : BasePlayerEvent
    {
        public PlayerDisconnectEvent() { }
        public PlayerDisconnectEvent(PlayerControl player) : base(player) { }
    }

    /// <summary>检查能否击杀目标时触发（仅本地）。</summary>
    public class PlayerCheckCanKillEvent : BasePlayerEvent
    {
        public PlayerControl Target { get; init; }
        private bool _cannotKillBasically;
        private bool _canKillForcedly;
        private bool _cannotKillForcedly;
        public bool CanKill => !_cannotKillForcedly && (_canKillForcedly || !_cannotKillBasically);
        public void SetAsCannotKillBasically() => _cannotKillBasically = true;
        public void SetAsCanKillForcedly() => _canKillForcedly = true;
        public void SetAsCannotKillForcedly() => _cannotKillForcedly = true;
        public PlayerCheckCanKillEvent() { }
        public PlayerCheckCanKillEvent(PlayerControl player, PlayerControl target) : base(player) { Target = target; }
    }

    /// <summary>尝试原版击杀时触发（可阻止）。</summary>
    public class PlayerTryVanillaKillEvent : BaseCancelablePlayerEvent
    {
        public PlayerControl Target { get; init; }
        public bool ResetCooldown { get; private set; } = true;
        public void Cancel(bool resetCooldown = false)
        {
            IsCanceled = true;
            ResetCooldown = resetCooldown;
        }
        public PlayerTryVanillaKillEvent() { }
        public PlayerTryVanillaKillEvent(PlayerControl killer, PlayerControl target) : base(killer) { Target = target; }
    }

    /// <summary>玩家被保护免受击杀时触发。</summary>
    public class PlayerGuardEvent : BasePlayerEvent
    {
        public PlayerControl Murderer { get; init; }
        public PlayerGuardEvent() { }
        public PlayerGuardEvent(PlayerControl player, PlayerControl killer) : base(player) { Murderer = killer; }
    }

    // =====================================================================
    //  玩家移动 / 交互
    // =====================================================================

    /// <summary>玩家移动时触发（每帧，高频）。</summary>
    public class PlayerMoveEvent : BasePlayerEvent
    {
        public Vector2 Position { get; init; }
        public PlayerMoveEvent() { }
        public PlayerMoveEvent(PlayerControl player, Vector2 pos) : base(player) { Position = pos; }
    }

    /// <summary>玩家通过控制台开始小游戏时触发。</summary>
    public class PlayerBeginMinigameByConsoleEvent : BasePlayerEvent
    {
        public Console Console { get; init; }
        public PlayerBeginMinigameByConsoleEvent() { }
        public PlayerBeginMinigameByConsoleEvent(PlayerControl player, Console console) : base(player) { Console = console; }
    }

    /// <summary>玩家通过门控制台开始小游戏时触发。</summary>
    public class PlayerBeginMinigameByDoorEvent : BasePlayerEvent
    {
        public DoorConsole Door { get; init; }
        public PlayerBeginMinigameByDoorEvent() { }
        public PlayerBeginMinigameByDoorEvent(PlayerControl player, DoorConsole door) : base(player) { Door = door; }
    }

    // =====================================================================
    //  聊天 / 踢出 / 击杀冷却
    // =====================================================================

    /// <summary>聊天消息时触发。</summary>
    public class ChatMessageEvent : BasePlayerEvent
    {
        public string Message { get; init; } = "";
        public ChatMessageEvent() { }
        public ChatMessageEvent(PlayerControl player, string message) : base(player) { Message = message; }
    }

    /// <summary>玩家被踢出时触发。</summary>
    public class PlayerKickEvent : BasePlayerEvent
    {
        public string Reason { get; init; } = "";
        public PlayerControl Kicker { get; init; }
        public PlayerKickEvent() { }
        public PlayerKickEvent(PlayerControl player, PlayerControl kicker, string reason = "") : base(player) { Kicker = kicker; Reason = reason; }
    }

    /// <summary>重置击杀冷却时触发（仅本地）。</summary>
    public class ResetKillCooldownEvent : BasePlayerEvent
    {
        private float? _cooldown;
        public bool UseDefaultCooldown => !_cooldown.HasValue;
        public float? FixedCooldown => _cooldown;
        public void SetFixedCooldown(float cooldown) => _cooldown = cooldown;
        public ResetKillCooldownEvent() { }
        public ResetKillCooldownEvent(PlayerControl player) : base(player) { _cooldown = null; }
    }
}
