using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using LightInDark.Core;
using LightInDark.Game;
using UnityEngine;

namespace LightInDark.Roles
{
    /// <summary>
    /// 玩家追踪器。
    /// 每帧检测最近的合法目标，支持高亮显示。
    /// </summary>
    public class PlayerTracker
    {
        /// <summary>当前追踪目标</summary>
        public Player CurrentTarget { get; private set; }

        /// <summary>是否锁定当前目标（锁定后不再切换）</summary>
        public bool IsLocked { get; set; }

        /// <summary>高亮颜色</summary>
        public Color HighlightColor { get; set; } = Color.Yellow;

        private readonly Player _source;
        private readonly float _maxDistance;
        private readonly Func<Player, bool> _predicate;

        /// <param name="source">追踪发起者</param>
        /// <param name="maxDistance">最大检测距离（默认原版击杀距离）</param>
        /// <param name="predicate">额外过滤条件</param>
        public PlayerTracker(Player source, float? maxDistance = null, Func<Player, bool>? predicate = null)
        {
            try
            {
                _source = source;
                _maxDistance = maxDistance ?? 2f; // 默认击杀距离
                _predicate = predicate ?? (_ => true);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.PlayerTracker", ex);
            }
        }

        /// <summary>
        /// 每帧更新（由按钮系统调用）
        /// </summary>
        public void Update()
        {
            try
            {
                if (IsLocked)
                {
                    HighlightTarget(CurrentTarget);
                    return;
                }

                CurrentTarget = FindClosestTarget();
                HighlightTarget(CurrentTarget);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.Update", ex);
            }
        }

        private Player FindClosestTarget()
        {
            try
            {
                if (_source?.Control == null) return null;

                var sourcePos = _source.Position;
                Player closest = null;
                float closestDist = float.MaxValue;

                var game = LightInDark.Game.GameManager.Instance;
                if (game == null) return null;

                foreach (var player in game.AllPlayers)
                {
                    if (player == null || player.Control == null) continue;
                    if (player.Control == _source.Control) continue;
                    if (player.IsDead) continue;
                    if (!_predicate(player)) continue;

                    float dist = Vector2.Distance(sourcePos, player.Position);
                    if (dist > _maxDistance) continue;

                    var source = _source.Control.transform.position;
                    var target = player.Control.transform.position;
                    Vector2 dir = (target - source);
                    float mag = dir.magnitude;
                    if (mag > 0.01f)
                    {
                        Vector2 dirNorm = dir / mag;
                        if (PhysicsHelpers.AnyNonTriggersBetween(
                                (Vector2)source, dirNorm, mag, Constants.ShipAndObjectsMask))
                            continue;
                    }

                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = player;
                    }
                }

                return closest;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.FindClosestTarget", ex);
                return null;
            }
        }

        private void HighlightTarget(Player target)
        {
            try
            {
                var game = LightInDark.Game.GameManager.Instance;
                if (game == null) return;

                foreach (var player in game.AllPlayers)
                {
                    if (player?.Control == null) continue;
                    if (player != target)
                    {
                        var rend = player.Control.cosmetics.currentBodySprite.BodySprite;
                        if (rend != null) rend.material.SetFloat("_Outline", 0f);
                    }
                }

                if (target?.Control != null)
                {
                    var rend = target.Control.cosmetics.currentBodySprite.BodySprite;
                    if (rend != null)
                    {
                        rend.material.SetFloat("_Outline", 1f);
                        rend.material.SetColor("_OutlineColor", HighlightColor.ToUnityColor());
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.HighlightTarget", ex);
            }
        }

        /// <summary>停止追踪并清除高亮</summary>
        public void Stop()
        {
            try
            {
                if (CurrentTarget?.Control != null)
                {
                    var rend = CurrentTarget.Control.cosmetics.currentBodySprite.BodySprite;
                    if (rend != null) rend.material.SetFloat("_Outline", 0f);
                }
                CurrentTarget = null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.Stop", ex);
            }
        }
    }

    /// <summary>
    /// 标准追踪谓词工厂。
    /// </summary>
    public static class TrackerPredicates
    {
        /// <summary>标准过滤：非自己、非死亡</summary>
        public static Func<Player, bool> Standard(Player source)
        {
            try
            {
                return p => p.Control != source.Control && !p.IsDead;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("TrackerPredicates.Standard", ex);
                return default;
            }
        }

        /// <summary>可击杀过滤：标准 + 非内鬼</summary>
        public static Func<Player, bool> Killable(Player source)
        {
            try
            {
                return p => p.Control != source.Control && !p.IsDead && !p.IsImpostor();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("TrackerPredicates.Killable", ex);
                return default;
            }
        }

        /// <summary>仅内鬼目标过滤：标准 + 非内鬼</summary>
        public static Func<Player, bool> ImpostorTarget(Player source)
        {
            try
            {
                return p => p.Control != source.Control && !p.IsDead && !p.IsImpostor();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("TrackerPredicates.ImpostorTarget", ex);
                return default;
            }
        }
    }
}
