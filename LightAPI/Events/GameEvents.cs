using UnityEngine;

namespace LightInDark.Events
{
    // =====================================================================
    //  游戏流程事件
    // =====================================================================

    /// <summary>
    /// **尸体生成时触发** ✓（2026-10-25 新增，对照 NOS 的 `DeadBodyInstantiateEvent`）
    /// <para>用于"尸体指引箭头 / 尸体特效 / 尸体变色"这类需要**尸体对象本身**的功能 ✓</para>
    /// <para>⚠️ 只在**击杀**时触发；放逐没有尸体 ✗</para>
    /// </summary>
    public class DeadBodyInstantiateEvent : IEvent
    {
        /// <summary>死者 ✓</summary>
        public PlayerControl Player { get; init; }

        /// <summary>刚生成的尸体对象 ✓（原版的 `DeadBody`）</summary>
        public DeadBody DeadBody { get; init; }

        /// <summary>凶手（自杀/无凶手时 null ✓）</summary>
        public PlayerControl Killer { get; init; }

        /// <summary>死因状态（`PlayerState`：`BeKilled` / `Vanish` / `Burst` … ✓）</summary>
        public Game.PlayerState State { get; init; } = Game.PlayerState.BeKilled;

        /// <summary>尸体世界坐标（拿不到对象时的兜底 ✓）</summary>
        public Vector2? Position { get; init; }

        public DeadBodyInstantiateEvent() { }
        public DeadBodyInstantiateEvent(PlayerControl player, DeadBody deadBody, PlayerControl killer = null,
                                        Game.PlayerState state = Game.PlayerState.BeKilled, Vector2? position = null)
        {
            Player = player;
            DeadBody = deadBody;
            Killer = killer;
            State = state;
            Position = position;
        }
    }

    /// <summary>游戏开始时触发。</summary>
    public class GameStartEvent : IEvent
    {
        /// <summary>参与本局的玩家数量。</summary>
        public int PlayerCount { get; init; }
    }

    /// <summary>游戏结束时触发。</summary>
    public class GameEndEvent : IEvent
    {
        /// <summary>船员是否获胜。</summary>
        public bool CrewmatesWin { get; init; }
        /// <summary>内鬼是否获胜。</summary>
        public bool ImpostorsWin { get; init; }
        /// <summary>获胜原因描述。</summary>
        public string WinReason { get; init; } = "";
    }

    /// <summary>游戏即将结束（可阻止）。</summary>
    public class GameTryEndEvent : BaseCancelableEvent
    {
        public bool CrewmatesWin { get; set; }
        public bool ImpostorsWin { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>游戏每帧更新时触发。</summary>
    public class GameUpdateEvent : IEvent
    {
        public float DeltaTime { get; init; }
    }

    /// <summary>HUD 每帧更新时触发。用于修改玩家轮廓等视觉元素。</summary>
    public class GameHudUpdateEvent : IEvent
    {
        public float DeltaTime { get; init; }
    }

    /// <summary>LateUpdate 时机触发。</summary>
    public class GameLateUpdateEvent : IEvent
    {
        public float DeltaTime { get; init; }
    }

    /// <summary>紧急按钮被破坏时触发。</summary>
    public class EmergencyButtonBrokenEvent : IEvent { }

    /// <summary>破坏系统激活时触发。</summary>
    public class SabotageStartEvent : IEvent
    {
        public SystemTypes SystemType { get; init; }
        public SabotageStartEvent() { }
        public SabotageStartEvent(SystemTypes type) { SystemType = type; }
    }

    /// <summary>破坏系统修复时触发。</summary>
    public class SabotageEndEvent : IEvent
    {
        public SystemTypes SystemType { get; init; }
        public SabotageEndEvent() { }
        public SabotageEndEvent(SystemTypes type) { SystemType = type; }
    }

    /// <summary>HUD 激活状态变更时触发。</summary>
    public class HudActiveChangeEvent : IEvent
    {
        public bool IsActive { get; init; }
    }
}
