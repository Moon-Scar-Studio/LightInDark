using System;

namespace LightInDark.Events
{
    // =====================================================================
    //  事件基础类型
    //
    //  - IEvent:          标记接口，所有事件实现它
    //  - ICancelableEvent: 可取消事件接口
    //  - BasePlayerEvent: 持有 PlayerControl 的玩家事件基类
    //  - BaseCancelableEvent:     可取消事件基类
    //  - BaseCancelablePlayerEvent: 可取消的玩家事件基类
    // =====================================================================

    /// <summary>所有事件的标记接口。</summary>
    public interface IEvent { }

    /// <summary> 监听接口。实现类会自动实例化并注册  </summary>
    public interface IEventListener { }

    /// <summary>可取消事件接口。监听者设置 IsCanceled 后，调用方检查该属性决定是否继续。</summary>
    public interface ICancelableEvent : IEvent
    {
        /// <summary>是否已被取消。</summary>
        bool IsCanceled { get; set; }
    }

    /// <summary>
    /// 玩家事件基类。
    /// 持有 PlayerControl，支持 [OnlyMyPlayer] / [OnlyLocalPlayer] 等过滤。
    /// </summary>
    public abstract class BasePlayerEvent : IEvent
    {
        /// <summary>事件关联的玩家。</summary>
        public PlayerControl Player { get; init; }
        protected BasePlayerEvent() { }
        protected BasePlayerEvent(PlayerControl player) => Player = player;
    }

    /// <summary>
    /// 可取消事件基类。
    /// IsCanceled 为合作式：监听者设置后，后续监听者可读取。
    /// 调度器不会自动中断，由调用方检查 IsCanceled。
    /// </summary>
    public abstract class BaseCancelableEvent : ICancelableEvent
    {
        public bool IsCanceled { get; set; }
    }

    /// <summary>可取消的玩家事件。</summary>
    public abstract class BaseCancelablePlayerEvent : BasePlayerEvent, ICancelableEvent
    {
        public bool IsCanceled { get; set; }
        protected BaseCancelablePlayerEvent() { }
        protected BaseCancelablePlayerEvent(PlayerControl player) : base(player) { }
    }

    // =====================================================================
    //  特性
    // =====================================================================

    /// <summary>事件监听优先级。数值越大越先执行。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class EventPriorityAttribute : Attribute
    {
        public int Priority;
        public EventPriorityAttribute(int priority = 0) => Priority = priority;
    }

    /// <summary>仅当事件关联的玩家是本地玩家时触发。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnlyMyPlayerAttribute : Attribute { }

    /// <summary>仅当本地客户端是房主时触发。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnlyHostAttribute : Attribute { }

    /// <summary>仅在本机客户端触发。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LocalAttribute : Attribute { }
}

// =====================================================================
//  向后兼容：保留旧特性名作为别名
// =====================================================================
namespace LightInDark.Events
{
    /// <summary>OnlyMyPlayer 的旧名别名。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnlyMyPlayer : OnlyMyPlayerAttribute { }

    /// <summary>OnlyHost 的旧名别名。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OnlyHost : OnlyHostAttribute { }

    /// <summary>Local 的旧名别名。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class Local : LocalAttribute { }
}
