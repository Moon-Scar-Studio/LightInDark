using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Game;
using UnityEngine;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业/玩家通用工具。为职业开发提供常用的查询与计算便捷方法。
    /// </summary>
    public static class RoleUtils
    {
        // =====================================================================
        // 玩家集合
        // =====================================================================

        /// <summary>所有玩家（按 GameManager 注册表）。</summary>
        public static IEnumerable<Player> AllPlayers()
        {
            try
            {
                return LightInDark.Game.GameManager.Instance?.AllPlayers ?? Enumerable.Empty<Player>();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.AllPlayers", ex);
                return Enumerable.Empty<Player>();
            }
        }

        /// <summary>所有存活玩家。</summary>
        public static IEnumerable<Player> AlivePlayers()
            => AllPlayers().Where(p => p.Control != null && p.Control.Data != null && !p.Control.Data.IsDead);

        /// <summary>所有已死亡玩家。</summary>
        public static IEnumerable<Player> DeadPlayers()
            => AllPlayers().Where(p => p.Control == null || p.Control.Data == null || p.Control.Data.IsDead);

        /// <summary>指定阵营的所有玩家。</summary>
        public static IEnumerable<Player> PlayersOf(RoleCategory category)
        {
            try
            {
                return AllPlayers().Where(p => p.Role?.Category == category);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.PlayersOf", ex);
                return Enumerable.Empty<Player>();
            }
        }

        /// <summary>存活的内鬼玩家。</summary>
        public static IEnumerable<Player> AliveImpostors() => AlivePlayers().Where(p => p.Role?.Category == RoleCategory.Impostor);

        /// <summary>存活的船员玩家。</summary>
        public static IEnumerable<Player> AliveCrewmates() => AlivePlayers().Where(p => p.Role?.Category == RoleCategory.Crewmate);

        /// <summary>存活的独立（中立）玩家。</summary>
        public static IEnumerable<Player> AliveNeutrals() => AlivePlayers().Where(p => p.Role?.Category == RoleCategory.Neutral);

        /// <summary>按 PlayerId 获取玩家。</summary>
        public static Player GetPlayerById(byte playerId)
        {
            try
            {
                return AllPlayers().FirstOrDefault(p => p.Control != null && p.Control.PlayerId == playerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.GetPlayerById", ex);
                return null;
            }
        }

        // =====================================================================
        // 距离 / 范围
        // =====================================================================

        /// <summary>两个玩家之间的距离。</summary>
        public static float GetDistance(Player a, Player b)
        {
            try
            {
                if (a?.Control == null || b?.Control == null) return float.MaxValue;
                return Vector2.Distance((Vector2)a.Control.transform.position, (Vector2)b.Control.transform.position);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.GetDistance", ex);
                return float.MaxValue;
            }
        }

        /// <summary>a 是否在 b 的指定范围内。</summary>
        public static bool IsInRange(Player a, Player b, float range)
            => GetDistance(a, b) <= range;

        /// <summary>
        /// 查找离 source 最近的满足 predicate 的玩家。
        /// </summary>
        /// <param name="source">参照玩家（可 null，此时返回最近存活玩家）。</param>
        /// <param name="predicate">可选过滤（默认排除 source 本人与死亡者）。</param>
        /// <param name="maxDistance">可选最大距离限制。</param>
        public static Player FindClosestPlayer(Player source, Func<Player, bool> predicate = null, float? maxDistance = null)
        {
            try
            {
                Player best = null;
                float bestDist = float.MaxValue;
                foreach (var p in AlivePlayers())
                {
                    if (source != null && p.Control == source.Control) continue;
                    if (predicate != null && !predicate(p)) continue;
                    float d = source != null ? GetDistance(source, p) : 0f;
                    if (maxDistance.HasValue && d > maxDistance.Value) continue;
                    if (d < bestDist) { bestDist = d; best = p; }
                }
                return best;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.FindClosestPlayer", ex);
                return null;
            }
        }

        /// <summary>随机存活玩家。</summary>
        public static Player RandomAlivePlayer()
        {
            try
            {
                var alive = AlivePlayers().ToList();
                if (alive.Count == 0) return null;
                return alive[UnityEngine.Random.Range(0, alive.Count)];
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.RandomAlivePlayer", ex);
                return null;
            }
        }

        // =====================================================================
        // 判断
        // =====================================================================

        /// <summary>玩家是否存活。</summary>
        public static bool IsAlive(Player p)
            => p?.Control != null && p.Control.Data != null && !p.Control.Data.IsDead;

        /// <summary>玩家是否拥有指定职业。</summary>
        public static bool HasRole<T>(Player p) where T : Role
        {
            try
            {
                return p?.Role is T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.HasRole", ex);
                return false;
            }
        }

        /// <summary>玩家的指定职业实例（无则 null）。</summary>
        public static T GetRole<T>(Player p) where T : Role
        {
            try
            {
                return p?.Role as T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.GetRole", ex);
                return null;
            }
        }
    }
}
