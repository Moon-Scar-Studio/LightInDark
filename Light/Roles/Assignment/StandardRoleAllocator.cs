using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Configuration;
using LightInDark.Events;
using LightInDark.Roles;
using LightInDark.RPCs;
using Light.Roles.Crewmates;
using Light.Roles.Impostors;
using LightInDark.Core;

namespace Light.Roles.Assignment;

/// <summary>标准职业分配器：按内鬼→中立→船员顺序抽选自定义职业，剩余玩家兜底普通职业</summary>
public class StandardRoleAllocator : IRoleAllocator
{
    private static readonly System.Random Rng = new();

    // 各类别自定义职业分配数量上限（配置系统删除后的临时硬编码默认值）
    private const int MaxImpostorRoles = 2;
    private const int MaxNeutralRoles = 1;
    private const int MaxCrewmateRoles = 2;

    public void Assign(List<byte> impostors, List<byte> others)
    {
        try
        {
            var table = new RoleTable();

            // /up 预定优先：先消耗各类别名额（职业未开启→忽略；名额满→预定保留到下局）
            int usedImp = AssignPinned(table, impostors, RoleCategory.Impostor, MaxImpostorRoles);
            int usedNeu = AssignPinned(table, others, RoleCategory.Neutral, MaxNeutralRoles);
            int usedCrew = AssignPinned(table, others, RoleCategory.Crewmate, MaxCrewmateRoles);

            // 内鬼 → 中立 → 船员，依次抽选自定义职业（扣除预定已用名额）
            Roll(table, impostors, BuildPool(RoleCategory.Impostor), MaxImpostorRoles, usedImp);

            AssignNeutrals(table, others, usedNeu);

            var crew = others.Where(p => !table.HasRole(p)).ToList();
            Roll(table, crew, BuildPool(RoleCategory.Crewmate), MaxCrewmateRoles, usedCrew);

            // 兜底：未分配自定义职业的玩家给普通职业模板（内鬼→普通内鬼，其他→普通船员）
            foreach (var pid in impostors)
                if (!table.HasRole(pid)) table.SetRole(pid, Impostor.MyRole);
            foreach (var pid in others)
                if (!table.HasRole(pid)) table.SetRole(pid, Crewmate.MyRole);

            EventTriggers.OnPreFixAssignment(table);
            table.Determine();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.Assign]", ex);
        }
    }

    /// <summary>
    /// 分配 /up 预定：只处理阵营匹配且已开启的职业。
    /// 名额已满时预定保留到下局；返回本类别已被预定占用的名额。
    /// </summary>
    private int AssignPinned(RoleTable table, List<byte> players, RoleCategory category, int cap)
    {
        int used = 0;
        foreach (var pid in players)
        {
            if (used >= cap) break;
            if (!LightInDark.Roles.Assignment.RolePinManager.TryGetPin(pid, out var role)) continue;
            if (role.RoleCategory != category) continue;      // 阵营不符：预定保留，不消耗
            if (GetMaxCount(role) <= 0)
            {
                // 预定后职业被房主关闭：消耗预定并告知
                LightInDark.Roles.Assignment.RolePinManager.Consume(pid);
                RpcDefinitions.ShowSystemMessage(pid, $"预定职业「{role.Name}」未开启，已忽略");
                continue;
            }
            table.SetRole(pid, role);
            LightInDark.Roles.Assignment.RolePinManager.Consume(pid);
            used++;
        }
        return used;
    }

    /// <summary>
    /// 独立的中立分配：从中立池里按概率挑 1 个中立职业，随机安到一名尚未分配的非内鬼身上。
    /// 中立不与船员共用抽选流程，被中立选走的人立刻从船员候选里剔除。
    /// </summary>
    private void AssignNeutrals(RoleTable table, List<byte> others, int preAssigned)
    {
        int slots = MaxNeutralRoles - preAssigned;
        if (slots <= 0) return;

        var pool = BuildPool(RoleCategory.Neutral);
        if (pool.Count == 0) return;

        var candidates = others.Where(p => !table.HasRole(p)).OrderBy(_ => Rng.Next()).ToList();

        int assigned = 0;
        foreach (var pid in candidates)
        {
            if (assigned >= slots) break;
            var role = PickByChance(pool);
            if (role == null) continue;
            table.SetRole(pid, role);
            assigned++;
        }
    }

    /// <summary>构建某类别的抽选池（可分配且配置最大数量>0 的职业）</summary>
    private List<RoleTemplate> BuildPool(RoleCategory category)
        => RoleRegistry.AllRoles.Where(r => r.RoleCategory == category && r.CanBeAssigned && GetMaxCount(r) > 0).ToList();

    /// <summary>抽选：先保证必出职业，再按概率补足，直到达到本类别数量上限（preAssigned 为预定已占用数）</summary>
    private void Roll(RoleTable table, List<byte> players, List<RoleTemplate> pool, int globalMax, int preAssigned = 0)
    {
        try
        {
            if (pool.Count == 0 || players.Count == 0 || preAssigned >= globalMax) return;

            var candidates = players.OrderBy(_ => Rng.Next()).ToList();
            int assigned = preAssigned;

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
    private RoleTemplate PickByChance(List<RoleTemplate> pool)
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

    /// <summary>读取职业最大数量：优先读配置 role.&lt;CodeName&gt;.count，无配置时回退 Allocation 默认。</summary>
    public static int GetMaxCount(RoleTemplate role)
    {
        var item = ConfigRegistry.Get($"role.{role.CodeName}.count");
        if (item != null) return item.GetInt();
        return role.Allocation.MaxCount;
    }

    /// <summary>读取职业分配概率：优先读配置 role.&lt;CodeName&gt;.chance，无配置时回退 Allocation 默认。</summary>
    public static int GetChance(RoleTemplate role)
    {
        var item = ConfigRegistry.Get($"role.{role.CodeName}.chance");
        if (item != null) return item.GetInt();
        return role.Allocation.Chance;
    }
}
