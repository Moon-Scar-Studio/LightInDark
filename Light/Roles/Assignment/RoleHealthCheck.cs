using System;
using System.Linq;
using System.Text;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Roles.Assignment
{
    /// <summary>
    /// **职业系统启动自检**（2026-10-06）。
    ///
    /// 为什么需要：这次按 Nebula 标准审查职业/按钮系统，一共有 50+ 条问题，其中**绝大多数是"静默失效"** ——
    ///   职业 ID 发成 0（静默换错职业）、钩子签名写错（静默不绑定）、职业关着却还能预定、
    ///   开场白/名字翻译缺失（界面显示成 key）、分配上限与帮助页不一致……
    ///   它们都不会抛异常、日志也一片正常，只能靠**把关键状态主动打出来**才能发现（AGENTS §4.8）。
    ///
    /// 这里在插件启动、配置注册完成之后跑一次：把每个职业的"能不能出 / 上限 / 概率 / 名字是否解析成功"
    /// 打进一条日志，并把明显不对的地方升级成 warning。**开销一次性，不影响运行期。**
    /// </summary>
    internal static class RoleHealthCheck
    {
        public static void Run()
        {
            try
            {
                var roles = RoleRegistry.AllRoles.OrderBy(r => r.Id).ToList();
                if (roles.Count == 0)
                {
                    LightLogger.LogError("[自检] **一个职业都没注册** —— 职业系统不可能工作。检查 RoleRegistry.RegisterAssembly 是否被调用");
                    return;
                }

                var sb = new StringBuilder(256);
                sb.Append($"[自检] 职业共 {roles.Count} 个：");
                foreach (var r in roles)
                {
                    string flags = r.CanBeAssigned ? "" : "兜底";
                    string spawn = r.IsSpawnable()
                        ? $"出/上限{StandardRoleAllocator.GetMaxCount(r)}/概率{StandardRoleAllocator.GetChance(r)}"
                        : "不出";
                    sb.Append($" #{r.Id}={r.CodeName}({flags}{spawn})");
                }
                LightLogger.Log(sb.ToString());

                // 名单里 Id 必须是唯一且连续的 —— 一旦重复/跳号，说明注册路径有问题（RPC 按 Id 传输）
                var ids = roles.Select(r => r.Id).ToList();
                if (ids.Distinct().Count() != ids.Count)
                    LightLogger.LogError("[自检] 职业 Id **有重复** —— RPC 会解析到错误职业！");
                if (ids.Count > 0 && (ids.Min() != 0 || ids.Max() != ids.Count - 1))
                    LightLogger.LogWarning($"[自检] 职业 Id 不连续（范围 {ids.Min()}..{ids.Max()}，共 {ids.Count} 个）—— 一般无害，但说明中间有注册失败");

                // 翻译自检：`GetStringOrKey` 命中不了会把 key 原样返回 → 界面上直接显示 role.xxx.name ✗
                foreach (var r in roles)
                {
                    if (!r.CanBeAssigned) continue;

                    string name = r.Name ?? "";
                    if (name.StartsWith("role.", StringComparison.Ordinal))
                        LightLogger.LogWarning($"[自检] 职业 {r.CodeName} 的名字**翻译缺失**（界面会显示成 {name}）——补 role.{r.CodeName}.name");

                    if (string.IsNullOrEmpty(r.IntroText))
                        LightLogger.LogWarning($"[自检] 职业 {r.CodeName} 缺开场白 —— 补 role.{r.CodeName}.intro");

                    if (!r.IsSpawnable())
                        continue;

                    // 上限/概率的边界（配置项自身范围是 0-15 / 0-100，这里复核夹紧后的值）
                    int max = StandardRoleAllocator.GetMaxCount(r);
                    int chance = StandardRoleAllocator.GetChance(r);
                    if (max > 0 && chance <= 0)
                        LightLogger.LogWarning($"[自检] 职业 {r.CodeName} 开着（上限 {max}）但**概率是 0** → 永远不会被抽到（除非配了必出）");
                    if (r.Allocation.GuaranteedCount > max)
                        LightLogger.LogWarning($"[自检] 职业 {r.CodeName} 的 GuaranteedCount({r.Allocation.GuaranteedCount}) > 上限({max}) —— 分配器会夹到 {max}");
                }

                // 配置项对账：每个可分配职业都应有 count/chance 两个配置项
                foreach (var r in roles)
                {
                    if (!r.CanBeAssigned) continue;
                    if (ConfigRegistry.Get($"role.{r.CodeName}.count") == null)
                        LightLogger.LogWarning($"[自检] 职业 {r.CodeName} **没有 count 配置项** —— 配置界面里看不到它（RoleConfigRegistrar 没注册到？）");
                }

                // ★ 职业表指纹（2026-10-06 审查 A3 的现场诊断手段）
                //
                // 背景：`Id` 是按**注册顺序**分配的。若某一端少注册了一个职业（构造抛异常 /
                // 部分类型加载失败 / 第三方职业注册时机不同），**后面所有 Id 都会整体错位** →
                // RPC 里的 roleId 就会解析成别的职业，**静默发错职业** ✗
                //
                // 原版没有可用的握手通道（本工程的握手系统是停用状态），所以先用**日志指纹**兜住：
                // 两台机器的这一行不一致 = 职业表不同 = 一定会发错职业。
                try
                {
                    var sb2 = new StringBuilder(128);
                    foreach (var r in roles) sb2.Append(r.Id).Append(':').Append(r.CodeName).Append(';');

                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb2.ToString()));
                    string fingerprint = BitConverter.ToString(bytes).Replace("-", "").Substring(0, 12);

                    LightLogger.Log($"[自检] 职业表指纹 {fingerprint}（两台机器这一行必须一致 —— 不一致就会静默发错职业）");
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[自检] 职业表指纹计算失败：{ex.Message}");
                }

                LightLogger.Log("[自检] 职业系统自检结束（上面若有 warning/error，就是真正需要修的地方）");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[自检] RoleHealthCheck 失败", ex);
            }
        }
    }
}
