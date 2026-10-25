namespace LightInDark.Events
{
    // =====================================================================
    //  门事件
    // =====================================================================

    /// <summary>门已打开时触发。</summary>
    public class PlayerOpenDoorEvent : BasePlayerEvent
    {
        public OpenableDoor Door { get; init; }
        public PlayerOpenDoorEvent() { }
        public PlayerOpenDoorEvent(PlayerControl player, OpenableDoor door) : base(player) { Door = door; }
    }

    /// <summary>尝试开门时触发（仅房主，可取消）。</summary>
    public class PlayerTryOpenDoorHostEvent : BaseCancelablePlayerEvent
    {
        public OpenableDoor Door { get; init; }
        public PlayerTryOpenDoorHostEvent() { }
        public PlayerTryOpenDoorHostEvent(PlayerControl player, OpenableDoor door) : base(player) { Door = door; }
    }

    /// <summary>尝试开门时触发（仅本地，可取消）。</summary>
    public class PlayerTryOpenDoorLocalEvent : BaseCancelablePlayerEvent
    {
        public OpenableDoor Door { get; init; }
        public PlayerTryOpenDoorLocalEvent() { }
        public PlayerTryOpenDoorLocalEvent(PlayerControl player, OpenableDoor door) : base(player) { Door = door; }
    }

    // =====================================================================
    //  地图事件
    // =====================================================================

    /// <summary>地图打开事件基类。</summary>
    public class AbstractMapOpenEvent : IEvent { }

    /// <summary>普通地图打开时触发。</summary>
    public class MapOpenNormalEvent : AbstractMapOpenEvent { }

    /// <summary>管理地图打开时触发。</summary>
    public class MapOpenAdminEvent : AbstractMapOpenEvent { }

    /// <summary>破坏地图打开时触发。</summary>
    public class MapOpenSabotageEvent : AbstractMapOpenEvent { }

    /// <summary>地图关闭时触发。</summary>
    public class MapCloseEvent : IEvent { }

    // =====================================================================
    //  管道事件
    // =====================================================================

    /// <summary>管道被使用时触发。</summary>
    public class VentUsedEvent : IEvent
    {
        public int VentId { get; init; }
        public PlayerControl Player { get; init; }
        public VentUsedEvent() { }
        public VentUsedEvent(PlayerControl player, int ventId) { Player = player; VentId = ventId; }
    }

    /// <summary>玩家进入管道时触发。</summary>
    public class PlayerVentEnterEvent : BasePlayerEvent
    {
        public Vent Vent { get; init; }

        /// <summary>管道 id（= `Vent.Id` ✓；拿不到管道对象时为 -1 ✓，2026-10-25 补）</summary>
        public int VentId => Vent == null ? -1 : Vent.Id;

        public PlayerVentEnterEvent() { }
        public PlayerVentEnterEvent(PlayerControl player, Vent vent) : base(player) { Vent = vent; }
    }

    /// <summary>玩家退出管道时触发。</summary>
    public class PlayerVentExitEvent : BasePlayerEvent
    {
        public Vent Vent { get; set; }

        /// <summary>管道 id（= `Vent.Id` ✓；拿不到管道对象时为 -1 ✓，2026-10-25 补）</summary>
        public int VentId => Vent == null ? -1 : Vent.Id;

        public PlayerVentExitEvent() { }
        public PlayerVentExitEvent(PlayerControl player, Vent vent) : base(player) { Vent = vent; }
    }
}
