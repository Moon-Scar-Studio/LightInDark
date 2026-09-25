using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Roles;
using LightInDark.RPCs;
using UnityEngine;
using System;
using System.Linq;

namespace LightInDark.Game
{
    public interface IPlayer
    {
        bool IsDead { get; }
        bool IsLocal { get; }
        string Name { get; }
        Vector2 Position { get; }
        Role Role { get; }
    }

    public interface IBindPlayer
    {
        Player MyPlayer { get; }
        bool AmOwner { get; }
    }

    /// <summary>
    /// 健全的 Player 系统，封装 PlayerControl 并联合 Role。
    /// </summary>
    public class Player : IPlayer, IBindPlayer, IGameOperator, ILifespan
    {
        public PlayerControl Control { get; private set; }

        // ---- IPlayer ----
        public bool IsDead => Control?.Data?.IsDead ?? true;
        public bool IsLocal => Control == PlayerControl.LocalPlayer;
        public string Name => Control?.Data?.PlayerName ?? "Unknown";
        public Vector2 Position => Control?.transform?.position ?? Vector2.zero;
        public Role Role { get; internal set; }

        public Player MyPlayer => this;
        public bool AmOwner => IsLocal;

        public bool IsDeadObject => Control == null || Control.Data == null
            || Control.Data.Disconnected || Control.Data.IsDead;

        public Color PlayerColor
        {
            get
            {
                try
                {
                    try
                    {
                        if (Control?.Data != null)
                        {
                            var colorId = Control.Data.DefaultOutfit.ColorId;
                            if (colorId >= 0 && colorId < Palette.PlayerColors.Length)
                                return ((UnityEngine.Color)Palette.PlayerColors[colorId]).ToLIDColor();
                        }
                    }
                    catch { }
                    return Color.White;
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("Player.PlayerColor", ex);
                    return default;
                }
            }
        }

        // ---- 角色 ----
        public RoleCategory? RoleCategory => Role?.Category;

        public Player(PlayerControl control)
        {
            try
            {
                Control = control;
                EventSystem.RegisterInstance(this);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.Player", ex);
            }
        }

        /// <summary>
        /// 切换角色（本地立即切换，并发送RPC同步）
        /// </summary>
        public void SetRole(Role newRole, int[] arguments = null)
        {
            try
            {
                if (newRole == null) return;
                arguments ??= newRole.DefaultArguments;

                var ev = new PlayerTryToChangeRoleEvent(Control, Role, newRole);
                EventSystem.RunEvent(ev);
                if (ev.IsCanceled) return;

                Role?.Inactivate();
                Role = newRole.CreateInstance(this, arguments);
                EventTriggers.OnRoleAssigned(Control, newRole, arguments);
                RpcDefinitions.SetRole(Control.PlayerId, newRole.Id, arguments);

                Core.LightLogger.Log($"[Player] {Name} → {newRole.Name}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.SetRole", ex);
            }
        }

        /// <summary>
        /// 仅本地设置角色（用于RPC接收）
        /// </summary>
        internal void SetRoleLocal(Role newRole, int[] arguments = null)
        {
            try
            {
                if (newRole == null) return;
                arguments ??= newRole.DefaultArguments;

                Role?.Inactivate();
                Role = newRole.CreateInstance(this, arguments);

                Core.LightLogger.Log($"[Player] {Name} (本地) → {newRole.Name}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.SetRoleLocal", ex);
            }
        }

        // ---- 操作 ----
        public void Suicide(PlayerState state = PlayerState.Suicide)
        {
            try
            {
                RpcDefinitions.Suicide(Control, playerState: state);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.Suicide", ex);
            }
        }

        public void MurderPlayer(PlayerControl victim, PlayerState state = PlayerState.BeKilled)
        {
            try
            {
                RpcDefinitions.MurderPlayer(Control, victim, state);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.MurderPlayer", ex);
            }
        }

        // ---- 判断 ----
        public bool IsRole(string roleName)
        {
            try
            {
                return Role?.Name == roleName;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.IsRole", ex);
                return default;
            }
        }

        public bool Is<T>() where T : Role
        {
            try
            {
                return Role is T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.Is", ex);
                return default;
            }
        }

        public bool HasRole => Role != null;

        // ---- 清理 ----
        public void Release()
        {
            try
            {
                EventSystem.UnregisterInstance(this);
                Role?.Release();
                Role = null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.Release", ex);
            }
        }

        void IGameOperator.OnReleased()
        {
            try
            {
            }
            catch (Exception ex)
            {
                LightLogger.LogError("Player.OnReleased", ex);
            }
        }
    }
    public enum PlayerState : uint
    {
        /// <summary>
        /// 普通死亡，会在复盘时显示真正的凶手。
        /// </summary>
        Dead = 0,
        /// <summary>
        /// 自杀，凶手记录为本人。
        /// </summary>
        Suicide = 1,
        /// <summary>
        /// 被猜测，凶手记录为猜测者。非会议中触发本死因将会被替换为PlayerState.Dead。
        /// </summary>
        BeGuessed = 2,
        /// <summary>
        /// 更精确的指向"被击杀"的死因。事实上，更建议使用PlayerState.Dead。凶手被记录为击杀者。
        /// </summary>
        BeKilled = 3,
        /// <summary>
        /// 警长击杀时走火。凶手记录为尝试击杀的人。
        /// </summary>
        GoOff = 4,
        /// <summary>
        /// 被放逐，不记录凶手。
        /// </summary>
        Exile = 5,
    }
}
