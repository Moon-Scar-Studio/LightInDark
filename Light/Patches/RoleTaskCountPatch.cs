using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LightInDark.Core;

namespace Light.Patches;

/// <summary>
/// **按职业覆写任务数量**（用户 2026-10-06 要求："继承 RoleTemplate 的职业可以选择覆写这个职业的 Task 数量"，
/// 并且那个值可以**动态**，例如 <c>=> 某个配置项.GetValue()</c>）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【原版是怎么发任务的】
///   `ShipStatus.Begin`（19.0 `ShipStatus.cs:399-425`）：
///   <code>
///     int short = options.GetInt(NumShortTasks);
///     int common = options.GetInt(NumCommonTasks);
///     int long  = options.GetInt(NumLongTasks);
///     if (common + long + short == 0) short = 1;
///     foreach (玩家) { 组出 byte[] → info.RpcSetTasks(array); }     // :421
///   </code>
///   而 `NetworkedPlayerInfo.RpcSetTasks(byte[])`（`:442-450`）：
///   <code>
///     if (AmClient) SetTasks(taskTypeIds);                       // 本地落地
///     LateBroadcastReliableMessage(new RpcSetTasksMessage(NetId, taskTypeIds));   // 广播
///   </code>
///
/// 【为什么打在 `RpcSetTasks` 的 Prefix 上（而不是 `SetTasks`）】
///   ① Prefix 能拿到 `ref byte[] taskTypeIds` → **改参数** ✓
///   ② 这样就**同时**改到了"本地落地"和"广播出去的那份" ✓✓
///      —— 如果打在私有的 `SetTasks` 上，广播里带的还是原版数组 ✗
///        客户端会各自算出不同的任务数 → 有的人多有的人少 ✗
///
/// 【数组里装的是什么】`task.Index`（不是 TaskType！）
///   证据：`ShipStatus.cs:217` 用 `t.Index == (int)idx` 反查任务 →
///   所以"加任务"必须从 `ShipStatus` 的任务池里取 **Index** ✓ 不能编造 TaskType ✗
///
/// ⚠️ **纯增量**：角色没有覆写（`TaskCount` 保持默认 1）时，本补丁**一行都不改** ✓
///    想用的话在职业里写：<c>public override int TaskCount => ConfigRegistry.Get("role.x.tasks")?.GetInt() ?? 3;</c>
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.RpcSetTasks))]
public static class RoleTaskCountPatch
{
    /// <summary>`TaskCount` 的默认值（= 原版按选项发出来的数量 → 不干预）。</summary>
    private const int DefaultTaskCount = 1;

    public static void Prefix(NetworkedPlayerInfo __instance, ref byte[] taskTypeIds)
    {
        try
        {
            if (__instance == null || taskTypeIds == null) return;

            // 这个 NetworkedPlayerInfo 对应的玩家（原版自己也用 __instance.Object，见 SetTasks :423/:437）
            var control = __instance.Object;
            if (control == null) return;

            var player = LightInDark.Game.GameManager.Instance.GetPlayer(control.PlayerId);
            var template = player?.Role?.Role;                      // 自定义职业的**定义侧**（RoleTemplate）
            if (template == null) return;

            int want = template.TaskCount;                          // ★ 职业覆写的数量（可以动态求值）
            if (want == DefaultTaskCount) return;                   // 没覆写 → 完全不干预 ✓
            if (want < 0) want = 0;

            if (want < taskTypeIds.Length)
            {
                // 变少：直接截断（保留原版挑出来的前 N 个，类型不变 ✓）
                int before = taskTypeIds.Length;
                var trimmed = new byte[want];
                Array.Copy(taskTypeIds, trimmed, want);
                taskTypeIds = trimmed;

                LightLogger.Log($"[TaskCount] {template.CodeName}：任务 {before} → {want}（截断）");
            }
            else if (want > taskTypeIds.Length)
            {
                // 变多：从地图任务池里补**没用过**的 Index（不能编 TaskType，见类注释）
                var extra = TakeUnusedTaskIndexes(taskTypeIds, want - taskTypeIds.Length);
                if (extra.Count == 0)
                {
                    LightLogger.LogWarning($"[TaskCount] {template.CodeName} 想要 {want} 个任务，" +
                                           $"但地图任务池里已经没有可加的（保持 {taskTypeIds.Length} 个）");
                    return;
                }

                var grown = new byte[taskTypeIds.Length + extra.Count];
                Array.Copy(taskTypeIds, grown, taskTypeIds.Length);
                for (int i = 0; i < extra.Count; i++) grown[taskTypeIds.Length + i] = extra[i];
                int before = taskTypeIds.Length;
                taskTypeIds = grown;
                LightLogger.Log($"[TaskCount] {template.CodeName}：任务 {before} → {grown.Length}" +
                                (extra.Count < want - before ? $"（池子只够补 {extra.Count} 个）" : ""));
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleTaskCountPatch] {ex.Message}");
        }
    }

    /// <summary>从 `ShipStatus` 的三个任务池里取还没被用过的 Index（最多 <paramref name="count"/> 个）。</summary>
    private static List<byte> TakeUnusedTaskIndexes(byte[] used, int count)
    {
        var result = new List<byte>();
        try
        {
            var ship = ShipStatus.Instance;
            if (ship == null) return result;

            var usedSet = new HashSet<byte>(used);
            var pools = new[] { ship.ShortTasks, ship.LongTasks, ship.CommonTasks };

            foreach (var pool in pools)
            {
                if (pool == null) continue;
                for (int i = 0; i < pool.Length && result.Count < count; i++)
                {
                    var task = pool[i];
                    if (task == null) continue;

                    byte idx = (byte)task.Index;
                    if (usedSet.Contains(idx)) continue;

                    usedSet.Add(idx);
                    result.Add(idx);
                }
                if (result.Count >= count) break;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleTaskCountPatch.TakeUnusedTaskIndexes] {ex.Message}");
        }
        return result;
    }
}
