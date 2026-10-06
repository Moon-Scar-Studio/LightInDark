namespace LightInDark.Configuration;

/// <summary>
/// 角色类别
/// </summary>
public enum RoleCategory
{
    Crewmate,
    Impostor,
    Neutral,

    /// <summary>
    /// **鬼魂职业**（对齐 Nebula 的 `Roles.AllGhostRoles` / `GhostRoleAssignmentPatch`，2026-10-06 新增）。
    ///
    /// 语义：玩家**死亡后**才可能被分配（不是开局分配），死后转成这个职业继续行动。
    /// 本工程原来只有前 3 个值 → 死即职业终止；而配置界面里的"幽灵"页签一直是空的 ✗
    ///
    /// ⚠️ 加在**末尾**：前 3 个值的数值不变，不影响任何已序列化配置。
    /// ⚠️ 开局分配器只抽 Crewmate/Impostor/Neutral 三个池 →
    ///    鬼魂职业**不会被开局分配**，只能由 `GhostRoleAllocator` 在死亡时给 ✓（纯增量）
    /// </summary>
    Ghost,
}
