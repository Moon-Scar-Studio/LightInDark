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
                return AllPlayers().Where(p => p.Role?.RoleCategory == category);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.PlayersOf", ex);
                return Enumerable.Empty<Player>();
            }
        }

        /// <summary>存活的内鬼玩家。</summary>
        public static IEnumerable<Player> AliveImpostors() => AlivePlayers().Where(p => p.Role?.RoleCategory == RoleCategory.Impostor);

        /// <summary>存活的船员玩家。</summary>
        public static IEnumerable<Player> AliveCrewmates() => AlivePlayers().Where(p => p.Role?.RoleCategory == RoleCategory.Crewmate);

        /// <summary>存活的独立（中立）玩家。</summary>
        public static IEnumerable<Player> AliveNeutrals() => AlivePlayers().Where(p => p.Role?.RoleCategory == RoleCategory.Neutral);

        public static IEnumerable<Player> AliveEvilNeutrals()
            => AliveNeutrals().Where(p => p.Role?.Role?.NeutralType == NeutralType.Evil);

        public static IEnumerable<Player> AliveNeutralsOf(string codeName)
            => AliveNeutrals().Where(p => p.Role?.CodeName == codeName);

        public static bool IsOnField(Player p)
            => p?.Control != null && p.Control.Data != null
               && !p.Control.Data.IsDead && !p.Control.Data.Disconnected;

        public static float KillDistance()
        {
            try { return global::GameManager.Instance?.LogicOptions?.GetKillDistance() ?? 1f; }
            catch { return 1f; }
        }

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
        ///
        /// ⚠️ 2026-10-06 审查 #15：`source == null` 时原来 `d = 0f` →
        ///   于是**第一个**满足条件的存活玩家被当成"最近的"返回（**误导性结果**，不是失败）✗
        ///   组合上"`MyPlayer` 为 null"的历史问题，表现就是"静默选中一个随机玩家"。
        ///   现在 `source == null` **明确返回 null 并打一次性 warning**（职业侧应自己判空）。
        ///   （`RoleUtils.IsInRange` 同理：任一为 null 直接 false；`GetDistance` 返回 float.MaxValue ✓）
        /// </summary>
        /// <param name="source">参照玩家（**不可为 null**）。</param>
        /// <param name="predicate">可选过滤（默认排除 source 本人与死亡者）。</param>
        /// <param name="maxDistance">可选最大距离限制。</param>
        public static Player FindClosestPlayer(Player source, Func<Player, bool> predicate = null, float? maxDistance = null)
        {
            try
            {
                if (source == null)
                {
                    if (!_warnedNullSource)
                    {
                        _warnedNullSource = true;      // 只打一次，避免每帧刷屏
                        LightLogger.LogWarning("[RoleUtils] FindClosestPlayer 收到 null source —— 返回 null（以前会返回「最近」= 第一个匹配的玩家，属于误导性结果）");
                    }
                    return null;
                }

                Player best = null;
                float bestDist = float.MaxValue;
                foreach (var p in AlivePlayers())
                {
                    if (p.Control == source.Control) continue;
                    if (predicate != null && !predicate(p)) continue;
                    float d = GetDistance(source, p);
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

        private static bool _warnedNullSource;

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
        public static bool HasRole<T>(Player p) where T : RoleTemplate
        {
            try
            {
                return p?.Role?.Role is T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.HasRole", ex);
                return false;
            }
        }

        /// <summary>玩家的指定职业定义（无则 null）。</summary>
        public static T GetRole<T>(Player p) where T : RoleTemplate
        {
            try
            {
                return p?.Role?.Role as T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleUtils.GetRole", ex);
                return null;
            }
        }
    }
}
