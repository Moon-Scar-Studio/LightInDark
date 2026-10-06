using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Roles.Assignment
{
    /// <summary>
    /// **鬼魂职业分配**（对齐 Nebula 的 `GhostRoleAssignmentPatch` + `Roles.AllGhostRoles`，2026-10-06）。
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 背景：审查里「Nebula 有而我们完全没有的能力」第 2 条 —— **GhostRole 鬼魂职业体系**。
    ///   我们原来 `RoleCategory` 只有 3 个值 → **死亡即职业终止**；而配置界面里那个
    ///   "幽灵"页签（`ConfigCategory.Ghost`）一直是**空的** ✗
    ///
    /// 设计（⚠️ **纯增量**，可安全上线）：
    ///   · 没有任何 `RoleCategory.Ghost` 职业注册时，<see cref="TryAssign"/> **第一步就 return** ——
    ///     现有行为与改动前**完全一致**
    ///   · 开局分配器只抽 Crewmate / Impostor / Neutral 三个池 → 鬼魂职业不会被开局分配 ✓
    ///   · 只有**房主**执行（结果经已有的 `SetRole` RPC 同步，那条 RPC 本来就 `OnlyHost` ✓）
    ///   · 名额与概率沿用与开局分配**同一套入口**（`IsSpawnable` / `GetMaxCount` / `GetChance`），
    ///     不会再出现"两处口径不一致"（审查 #13）
    ///
    /// 怎么做一个鬼魂职业：
    /// <code>
    ///   public class Poltergeist : RoleTemplate
    ///   {
    ///       public static readonly Poltergeist MyRole = new();
    ///       public override string CodeName =&gt; "poltergeist";
    ///       public override RoleCategory RoleCategory =&gt; RoleCategory.Ghost;   // ← 关键
    ///       public override AllocationParameters Allocation =&gt; new() { MaxCount = 1, Chance = 50 };
    ///       ...
    ///   }
    /// </code>
    /// 它的按钮要能在**死后**显示：用 <c>RoleButtonConfig.AlwaysShow = true</c> +
    /// <c>CanShow = () =&gt; MyPlayer.IsDead</c>（`AlwaysShow` 分支不检查存活，正合适）。
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    internal static class GhostRoleAllocator
    {
        private static readonly System.Random Rng = new();

        /// <summary>死亡时尝试分配鬼魂职业（**仅房主**，非房主调用直接返回）。</summary>
        public static void TryAssign(byte playerId)
        {
            try
            {
                // ★★ 第一道闸：一个鬼魂职业都没有 → 什么都不做（现有行为完全不变）
                var pool = RoleRegistry.AllRoles.Where(r => r.IsGhostRole && r.IsSpawnable()).ToList();
                if (pool.Count == 0) return;

                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;

                var mgr = LightInDark.Game.GameManager.Instance;
                var target = mgr.GetPlayer(playerId);
                if (target == null) return;

                // 已经是鬼魂职业 → 不重复给
                var current = target.Role;
                if (current != null && current.RoleCategory == RoleCategory.Ghost) return;

                // 名额：MaxCount 是"本局最多几个"，按当前已持有者扣减
                var used = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in mgr.AllPlayers)
                {
                    var cat = p.Role?.RoleCategory;
                    if (cat != RoleCategory.Ghost) continue;
                    string code = p.Role.CodeName;
                    used[code] = used.TryGetValue(code, out var n) ? n + 1 : 1;
                }

                var avail = pool
                    .Where(r => (used.TryGetValue(r.CodeName, out var n) ? n : 0) < StandardRoleAllocator.GetMaxCount(r))
                    .ToList();

                if (avail.Count == 0)
                {
                    LightLogger.Log("[GhostRole] 鬼魂职业名额已满，本次不分配");
                    return;
                }

                // 概率：与开局分配同一语义 —— 每个候选各掷一次骰，再在命中者里等权随机取一个
                var hit = avail.Where(r =>
                {
                    int chance = StandardRoleAllocator.GetChance(r);
                    return chance >= 100 || Rng.Next(100) < chance;
                }).ToList();

                if (hit.Count == 0)
                {
                    LightLogger.Log($"[GhostRole] {target.Name} 死亡，但 {avail.Count} 个候选鬼魂职业都没命中概率");
                    return;
                }

                var role = hit[Rng.Next(hit.Count)];
                target.SetRole(role);   // 房主本地生效 + 经 SetRole RPC 同步给所有人（该 RPC 是 OnlyHost ✓）

                LightLogger.Log($"[GhostRole] {target.Name}(id={playerId}) 死亡 → 鬼魂职业 {role.CodeName}" +
                                $"（候选 {avail.Count}，命中 {hit.Count}）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[GhostRole.TryAssign] {ex.Message}");
            }
        }
    }
}
