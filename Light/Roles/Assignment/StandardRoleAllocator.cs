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

    /// <summary>
    /// 各类别自定义职业数量上限（**可配置**，2026-10-06 审查 B7）。
    ///
    /// ⚠️ 原来是三个 `const`（注释自称"配置系统删除后的临时硬编码"）→
    ///   15 人局最多只有 5 个自定义职业，玩家在配置界面里**无法调整** ✗
    ///   Nebula 对应的是 `options.assignment.crewmate / impostor / neutral` ✓
    /// ✅ **默认值就是原来的 2 / 1 / 2** → 没人改配置时行为与改动前一致（纯增量）。
    /// </summary>
    private static int MaxImpostorRoles => GetCap("lid.assignment.impostorMax", 2);

    /// <inheritdoc cref="MaxImpostorRoles"/>
    private static int MaxNeutralRoles => GetCap("lid.assignment.neutralMax", 1);

    /// <inheritdoc cref="MaxImpostorRoles"/>
    private static int MaxCrewmateRoles => GetCap("lid.assignment.crewmateMax", 2);

    /// <summary>读上限配置（缺失/异常回退默认；夹到 `[0,15]`，与职业数量项同一口径）。</summary>
    private static int GetCap(string key, int fallback)
    {
        try
        {
            var item = ConfigRegistry.Get(key);
            if (item == null) return fallback;

            int v = item.GetInt();
            if (v < 0) return 0;
            return v > 15 ? 15 : v;
        }
        catch { return fallback; }
    }

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

            // ★ 诊断：把**分配结果表**打出来。
            //   "职业分配不对 / 某个职业从没出现过"这类问题，没有这张表就只能猜（AGENTS §4.8）。
            //   每局一条，不会刷屏。
            try
            {
                var sb = new System.Text.StringBuilder(256);
                sb.Append($"[StandardRoleAllocator] 分配结果（内鬼 {impostors.Count} / 其他 {others.Count}）：");
                foreach (var cat in new[] { RoleCategory.Impostor, RoleCategory.Neutral, RoleCategory.Crewmate })
                {
                    foreach (var (pid, role) in table.GetPlayers(cat))
                        sb.Append(' ').Append(NameOf(pid)).Append('=').Append(role.Name).Append('(').Append(role.CodeName).Append(')');
                }
                LightLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[StandardRoleAllocator] 结果日志失败：{ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.Assign]", ex);
        }
    }

    /// <summary>玩家名（诊断用；取不到就退回 id）。</summary>
    private static string NameOf(byte playerId)
    {
        try
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.PlayerId != playerId) continue;
                var data = pc.Data;
                return data != null && !string.IsNullOrEmpty(data.PlayerName) ? data.PlayerName : pc.name;
            }
        }
        catch { }
        return playerId.ToString();
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

        // ⚠️ merge 注意：PR 这里原来调 `PickByChance(pool)`，而那个方法在第 3 轮已经被删掉 ——
        //   它的语义是"按池顺序返回第一个命中者"：池首职业垄断名额、而且**不记名额**
        //   （MaxCount=1 的中立职业能被发给多人）。改成与 `Roll` 完全同一套：
        //   `PoolEntry`（带本局剩余名额）+ `PickOne`（每个候选各掷一次骰再随机取一个）。
        var entries = new List<PoolEntry>();
        foreach (var role in pool)
        {
            int max = GetMaxCount(role);
            if (max <= 0) continue;
            entries.Add(new PoolEntry { Role = role, Remaining = max });
        }
        if (entries.Count == 0) return;

        var candidates = others.Where(p => !table.HasRole(p)).OrderBy(_ => Rng.Next()).ToList();

        int assigned = 0;
        foreach (var pid in candidates)
        {
            if (assigned >= slots) break;
            var picked = PickOne(entries);
            if (picked == null) continue;      // 这一轮没人命中 → 换下一个候选（与旧循环形状一致）
            table.SetRole(pid, picked.Role);
            picked.Remaining--;                // 记名额：为 0 时 PickOne 会把它剔出池
            assigned++;
        }
    }

    /// <summary>
    /// 构建某类别的抽选池。
    /// ★ 用统一的 <see cref="RoleTemplate.IsSpawnable"/> 判定（配置 → 默认 → CanBeAssigned，异常返回 false），
    ///   不再各处自己拼条件（审查 #13）。
    /// </summary>
    private List<RoleTemplate> BuildPool(RoleCategory category)
        => RoleRegistry.AllRoles.Where(r => r.RoleCategory == category && r.IsSpawnable()).ToList();

    /// <summary>池里的候选职业 + **本局剩余名额**（保底与概率共用同一份额度）。</summary>
    private sealed class PoolEntry
    {
        public RoleTemplate Role = null!;
        public int Remaining;
    }

    /// <summary>
    /// 抽选：① 保底职业（按 `GuaranteedCount` **数值**发，洗牌后发）② 其余逐名额按概率抽，
    /// 直到达到本类别数量上限（<paramref name="preAssigned"/> = 预定已占用数）。
    ///
    /// ⚠️⚠️ 2026-10-06 重写（对照 Nebula `Roles/Assignment/RoleAssignment.cs`）。原来有四个必修问题：
    ///   ① **`GuaranteedCount` 只被当布尔用** —— 发牌数取的是 `MaxCount`，填 2 和填 1 完全一样，
    ///      而帮助页却显示"必出 2"✗ → 现在真的按它发（并夹到 `[0, MaxCount]`）；
    ///   ② **概率抽选是"按池顺序逐个独立掷骰、返回第一个命中者"** → 池首职业垄断名额，
    ///      后面的职业即使也写 100% 也永远不出（"开了两个职业只出一个"）✗
    ///      → 现在：≥100% 的进"必出轮"，其余**每个名额先让所有合格职业各掷一次骰，
    ///        再在被命中者里随机挑一个**（保留"概率"语义，同时没有任何职业能垄断）；
    ///   ③ **全程不记账** → `MaxCount = 1` 的职业可以被分给多人（一局两个召集者）✗
    ///      → 现在池元素带 `Remaining`，每次命中扣 1，为 0 出池（Nebula 的 `selected.left--` 同款）；
    ///   ④ **候选里含已有职业的玩家** → `/up` 预定的职业会被后续类别抽选**覆盖**，
    ///      而预定早已被 `Consume` → 玩家两头空且无提示 ✗ → 现在只从"还没职业"的玩家里选。
    /// </summary>
    private void Roll(RoleTable table, List<byte> players, List<RoleTemplate> pool, int globalMax, int preAssigned = 0)
    {
        try
        {
            if (pool.Count == 0 || players.Count == 0 || preAssigned >= globalMax) return;

            var candidates = players.Where(p => !table.HasRole(p)).OrderBy(_ => Rng.Next()).ToList();
            if (candidates.Count == 0) return;

            var entries = new List<PoolEntry>();
            foreach (var role in pool)
            {
                int max = GetMaxCount(role);
                if (max <= 0) continue;
                entries.Add(new PoolEntry { Role = role, Remaining = max });
            }
            if (entries.Count == 0) return;

            int assigned = preAssigned;

            // ① 必出轮：按 GuaranteedCount 的**数值**发；洗牌避免"扫描顺序靠前的吃光名额"
            foreach (var e in entries.Where(e => GuaranteedOf(e.Role) > 0).OrderBy(_ => Rng.Next()).ToList())
            {
                int want = Math.Min(GuaranteedOf(e.Role), e.Remaining);
                for (int i = 0; i < want && candidates.Count > 0 && assigned < globalMax; i++)
                {
                    table.SetRole(candidates[0], e.Role);
                    candidates.RemoveAt(0);
                    e.Remaining--;
                    assigned++;
                }
            }

            // ② 概率轮：每个名额一次抽选
            while (assigned < globalMax && candidates.Count > 0)
            {
                var picked = PickOne(entries);
                if (picked == null) break;          // 没人命中 / 池空了 → 本类别到此为止
                table.SetRole(candidates[0], picked.Role);
                candidates.RemoveAt(0);
                picked.Remaining--;
                assigned++;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.Roll]", ex);
        }
    }

    /// <summary>保底份数（夹到 [0, 该职业上限]；`GuaranteedCount` 比上限大时以上限为准）。</summary>
    private static int GuaranteedOf(RoleTemplate role)
    {
        int g = role.Allocation.GuaranteedCount;
        if (g < 0) g = 0;
        int max = GetMaxCount(role);
        return g > max ? max : g;
    }

    /// <summary>
    /// 抽一个职业：先清掉"名额用完 / 概率为 0"的，然后
    /// **让每个合格职业各掷一次骰（概率语义），再在被命中者里等权随机挑一个**。
    /// 没有任何职业命中时返回 null（调用方结束本类别抽选）。
    /// </summary>
    private PoolEntry? PickOne(List<PoolEntry> entries)
    {
        try
        {
            entries.RemoveAll(e => e.Remaining <= 0 || GetChance(e.Role) <= 0);
            if (entries.Count == 0) return null;

            var hit = new List<PoolEntry>();
            foreach (var e in entries)
            {
                int chance = GetChance(e.Role);
                if (chance >= 100 || Rng.Next(100) < chance) hit.Add(e);
            }
            if (hit.Count == 0) return null;

            return hit[Rng.Next(hit.Count)];
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[StandardRoleAllocator.PickOne]", ex);
            return null;      // 原来是 return default（静默放弃且可能返回 null 角色）
        }
    }

    /// <summary>
    /// 读取职业最大数量：优先读配置 `role.&lt;CodeName&gt;.count`，无配置时回退 `Allocation` 默认。
    ///
    /// ⚠️ **在这里夹紧**（2026-10-06 审查 #14）：`Allocation` 是代码里手写的 struct，
    ///   注册时不做任何裁剪 —— `MaxCount = 99` 会让整个类别的名额被一个职业吃满。
    ///   配置项本身范围是 0-15，但代码默认值不受它约束，所以读取侧必须兜住。
    /// </summary>
    public static int GetMaxCount(RoleTemplate role)
    {
        int v;
        var item = ConfigRegistry.Get($"role.{role.CodeName}.count");
        v = item != null ? item.GetInt() : role.Allocation.MaxCount;
        if (v < 0) return 0;
        return v > 15 ? 15 : v;
    }

    /// <summary>
    /// 读取职业分配概率：优先读配置 `role.&lt;CodeName&gt;.chance`，无配置时回退 `Allocation` 默认。
    /// ⚠️ 同样夹到 `[0,100]`：填 150 会"恒中"、填 -1 会"恒不中"，两种都是静默的行为异常。
    /// </summary>
    public static int GetChance(RoleTemplate role)
    {
        int v;
        var item = ConfigRegistry.Get($"role.{role.CodeName}.chance");
        v = item != null ? item.GetInt() : role.Allocation.Chance;
        if (v < 0) return 0;
        return v > 100 ? 100 : v;
    }
}
