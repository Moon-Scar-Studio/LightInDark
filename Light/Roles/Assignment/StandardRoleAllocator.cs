using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Configuration;
using LightInDark.Events;
using LightInDark.Roles;
using Light.Roles.Vanilla;
using LightInDark.Core;

namespace Light.Roles.Assignment;

/// <summary>标准职业分配器：按内鬼→中立→船员顺序抽选自定义职业，剩余玩家兜底原版职业</summary>
public class StandardRoleAllocator : IRoleAllocator
{
    private static readonly System.Random Rng = new();

    public void Assign(List<byte> impostors, List<byte> others)
    {
        try
        {
            var table = new RoleTable();

            // 内鬼 → 中立 → 船员，依次抽选自定义职业
            Roll(table, impostors, BuildPool(RoleCategory.Impostor), GameConfig.MaxImpostorRoles);
            Roll(table, others, BuildPool(RoleCategory.Neutral), GameConfig.MaxNeutralRoles);

            var neutralIds = table.GetPlayers(RoleCategory.Neutral).Select(p => p.PlayerId).ToHashSet();
            var crew = others.Where(p => !neutralIds.Contains(p)).ToList();
            Roll(table, crew, BuildPool(RoleCategory.Crewmate), GameConfig.MaxCrewmateRoles);

            // 兜底：未分配到自定义职业的玩家由原版 SelectRoles 处理
            // 不再强制分配 VanillaImpostor/VanillaCrewmate

            EventTriggers.OnPreFixAssignment(table);
            table.Determine();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.Assign]", ex);
        }
    }

    /// <summary>构建某类别的抽选池（仅参与分配且配置最大数量>0 的职业）</summary>
    private List<Role> BuildPool(RoleCategory category)
        => RoleRegistry.AllRoles.Where(r => r.Category == category && GetMaxCount(r) > 0).ToList();

    /// <summary>抽选：先保证必出职业，再按概率补足，直到达到本类别数量上限</summary>
    private void Roll(RoleTable table, List<byte> players, List<Role> pool, int globalMax)
    {
        try
        {
            if (pool.Count == 0 || players.Count == 0) return;

            var candidates = players.OrderBy(_ => Rng.Next()).ToList();
            int assigned = 0;

            // 必出职业优先分配（数量由配置/默认决定）
            foreach (var role in pool.Where(r => r.Allocation.GuaranteedCount > 0))
            {
                int count = GetMaxCount(role);
                for (int i = 0; i < count && candidates.Count > 0 && assigned < globalMax; i++)
                {
                    table.SetRole(candidates[0], role);
                    candidates.RemoveAt(0);
                    assigned++;
                }
            }

            // 剩余候选按概率抽选，直到达到该类别的最大数量上限
            foreach (var player in candidates)
            {
                if (assigned >= globalMax) break;
                var role = PickByChance(pool);
                if (role != null)
                {
                    table.SetRole(player, role);
                    assigned++;
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.Roll]", ex);
        }
    }

    /// <summary>按概率从池中抽选一个职业，未命中返回 null（概率由配置/默认决定）</summary>
    private Role PickByChance(List<Role> pool)
    {
        try
        {
            foreach (var role in pool)
                if (Rng.Next(100) < GetChance(role))
                    return role;
            return null;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.PickByChance]", ex); return default;
        }
    }

    /// <summary>读取职业最大数量（配置优先，回退到代码 Allocation 默认）。</summary>
    public static int GetMaxCount(Role role)
        => RoleConfig.GetRoleCount(role.CodeName, role.Allocation.MaxCount);

    /// <summary>读取职业分配概率（配置优先，回退到代码 Allocation 默认）。</summary>
    public static int GetChance(Role role)
        => RoleConfig.GetRoleChance(role.CodeName, role.Allocation.Chance);
}
