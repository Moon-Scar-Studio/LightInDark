using System.Collections.Generic;
using UnityEngine;

namespace LightInDark.Game
{
    /// <summary>
    /// **尸体（<see cref="DeadBody"/>）的查找工具** ✓
    ///
    /// ⚠️ 2026-10-25 用户要求："比如 PlayerDeathEvent 获取死者 body 的方式" ——
    ///   原版 `DeadBody` **没有**"按玩家 id 反查"的公开接口 ✗（只有 `DeadBody.ParentId` 字段 + 场景里一堆实例），
    ///   所以这里统一收口，别在业务代码里到处 `FindObjectsOfType` ✗
    ///
    /// 【原版事实（19.0 源码）】
    ///   · 尸体在 `KillAnimation.CoPerformKill` 里 `Object.Instantiate&lt;DeadBody&gt;(…)` 创建 ✓
    ///   · 创建后立刻 `deadBody.ParentId = target.PlayerId` ✓（这就是"这具尸体是谁"的唯一标识）
    ///   · `target.Die(...)` 在尸体创建**之后**才调用 ✓ → 死亡事件里能查到尸体 ✓
    ///   · **放逐没有尸体**（只有击杀才有）→ 查不到是正常的，别当错误 ✗
    /// </summary>
    public static class DeadBodyUtil
    {
        private static readonly List<DeadBody> _buffer = new();

        /// <summary>
        /// 按**玩家 id** 找尸体（找不到 null ✓）。
        /// <para>击杀瞬间 `Die` 被调用时尸体已经建好 ✓，所以死亡事件里能直接拿到 ✓</para>
        /// </summary>
        /// <param name="playerId">死者的玩家 id（`PlayerControl.PlayerId` ✓）</param>
        public static DeadBody FindByPlayerId(byte playerId)
        {
            var all = All();
            foreach (var body in all)
            {
                if (body == null) continue;
                if (body.ParentId == playerId) return body;
            }
            return null;
        }

        /// <summary>按死者 `PlayerControl` 找尸体（找不到 null ✓）</summary>
        /// <param name="player">死者 ✓</param>
        public static DeadBody FindByPlayer(PlayerControl player)
            => player == null ? null : FindByPlayerId(player.PlayerId);

        /// <summary>场上所有尸体（**复用内部列表**，别保存返回值 ✓）</summary>
        public static List<DeadBody> All()
        {
            _buffer.Clear();
            try
            {
                var found = Object.FindObjectsOfType<DeadBody>();
                if (found != null)
                {
                    foreach (var body in found)
                        if (body != null) _buffer.Add(body);
                }
            }
            catch { /* 场景切换瞬间可能拿不到 → 当作没有 ✓ */ }
            return _buffer;
        }

        /// <summary>尸体的世界坐标（找不到返回 null ✓）</summary>
        /// <param name="playerId">死者的玩家 id ✓</param>
        public static Vector2? PositionOf(byte playerId)
        {
            var body = FindByPlayerId(playerId);
            if (body == null) return null;
            var pos = body.transform.position;
            return new Vector2(pos.x, pos.y);
        }

        /// <summary>这具尸体是不是"没被报告过"（= 场上还躺着 ✓）</summary>
        /// <param name="playerId">死者的玩家 id ✓</param>
        /// <remarks>被报告后原版会把尸体移除 → `FindByPlayerId` 直接返回 null ✓</remarks>
        public static bool IsLyingOnGround(byte playerId) => FindByPlayerId(playerId) != null;
    }
}
