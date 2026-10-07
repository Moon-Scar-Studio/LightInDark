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
                        SetOutline(player.Control, 0f, default);
                }

                if (target?.Control != null)
                    SetOutline(target.Control, 1f, HighlightColor.ToUnityColor());
            }
            catch (Exception ex)
            {
                LightLogger.LogError("PlayerTracker.HighlightTarget", ex);
            }
        }

        /// <summary>
        /// 给一个玩家写轮廓（开/关）。
        ///
        /// ⚠️⚠️ 必须**连 `LongModeParts` 一起写**（用户 2026-10-06 报的「追踪器不显示」的一条确证原因）：
        ///   原版 `CosmeticsLayer.SetOutline`（19.0 `CosmeticsLayer.cs:817-834`）除了 `BodySprite`，
        ///   还会循环 `currentBodySprite.LongModeParts` 的**每个元素**写同一组属性 ✓
        ///   "长身模式"（如 Fungle 的蛇形皮肤）和一部分皮肤的身体分段**画在那些渲染器上**，
        ///   只写 `BodySprite` 的话它们**不会亮** ✗ —— 看起来就是"追踪器没显示/只有一截亮" ✓
        ///
        /// ⚠️ 属性名是对的（`_Outline` / `_OutlineColor`，见 `CosmeticsLayer.cs:819/827`），
        ///    不要改成别的名字 ✓
        /// </summary>
        private static void SetOutline(PlayerControl control, float outline, UnityEngine.Color color)
        {
            try
            {
                var cosmetics = control.cosmetics;
                if (cosmetics == null) return;

                var body = cosmetics.currentBodySprite;
                if (body == null) return;

                var main = body.BodySprite;
                if (main != null && main.material != null)
                {
                    main.material.SetFloat("_Outline", outline);
                    if (outline > 0f) main.material.SetColor("_OutlineColor", color);
                }

                var longParts = body.LongModeParts;
                if (longParts == null) return;

                for (int i = 0; i < longParts.Length; i++)
                {
                    var part = longParts[i];
                    if (part == null || part.material == null) continue;
                    part.material.SetFloat("_Outline", outline);
                    if (outline > 0f) part.material.SetColor("_OutlineColor", color);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[PlayerTracker.SetOutline] {ex.Message}");
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

        /// <summary>仅内鬼目标过滤：标准 + **是内鬼**</summary>
        public static Func<Player, bool> ImpostorTarget(Player source)
        {
            try
            {
                // ⚠️ 2026-10-06 审查：原来是 `!p.IsImpostor()` —— 与 CrewmateTarget / Killable 完全同形，
                //    而方法名与注释都写着"仅内鬼目标" → **谓词写反了**（接上技能只会选到船员）。
                return p => p.Control != source.Control && !p.IsDead && p.IsImpostor();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("TrackerPredicates.ImpostorTarget", ex);
                return default;
            }
        }
    }
}
