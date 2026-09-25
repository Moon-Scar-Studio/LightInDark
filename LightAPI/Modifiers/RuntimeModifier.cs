using System;
using LightInDark.Core;
using LightInDark.Game;

namespace LightInDark.Modifiers
{
    /// <summary>
    /// 修饰器运行时实例，绑定到某个玩家。
    /// 与职业（Role）不同，修饰器可叠加、可随时增删。
    /// </summary>
    public abstract class RuntimeModifier : IGameOperator, ILifespan
    {
        public Modifier Definition { get; }
        public Player MyPlayer { get; }
        public bool AmOwner => MyPlayer.AmOwner;

        private bool _released;
        public bool IsDeadObject => _released || MyPlayer.IsDeadObject;

        protected RuntimeModifier(Modifier definition, Player player)
        {
            Definition = definition;
            MyPlayer = player;
            this.Register(player);
            try { OnAdded(); }
            catch (System.Exception ex) { LightLogger.LogWarning($"[RuntimeModifier] OnAdded 失败: {ex.Message}"); }
        }

        /// <summary>修饰器加到玩家身上时调用（子类覆写做初始化/表现）。</summary>
        protected virtual void OnAdded() { }

        /// <summary>修饰器从玩家移除时调用（子类覆写做清理）。</summary>
        protected virtual void OnRemoved() { }

        /// <summary>移除修饰器。</summary>
        public void Remove()
        {
            try
            {
                try { OnRemoved(); }
                catch (System.Exception ex) { LightLogger.LogWarning($"[RuntimeModifier] OnRemoved 失败: {ex.Message}"); }
                Release();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RuntimeModifier.Remove", ex);
            }
        }

        public void Release()
        {
            _released = true;
        }
    }
}
