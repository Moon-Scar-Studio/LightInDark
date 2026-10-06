using System;
using System.Collections.Generic;
using LightInDark.Core;

namespace LightInDark.Roles
{
    /// <summary>
    /// **职业数据插槽**（对齐 Nebula 的 `GameData.RegisterRoleDataId` / `GetRoleData` / `SetRoleData` /
    /// `OnUpdateRoleData`）。
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【为什么需要它】（2026-10-06 审查：「Nebula 有而我们完全没有的能力」第 1 条）
    ///
    ///   以前职业只能把"每玩家可变状态"（市长的票数、吸血鬼剩余次数…）挂成
    ///   `RuntimeRoleTemplate` 的私有字段 —— **要同步就得自己写一条 RPC、自己处理时机和重放** ✗
    ///   Nebula 专门有一层"职业数据位"：
    ///     <code>
    ///       int id = GameData.RegisterRoleDataId("MayorVotes");   // 拿一个稳定 id
    ///       GameData.SetRoleData(playerId, id, 3);                // 写（框架负责同步）
    ///       void OnUpdateRoleData(int dataId, int newValue)       // 钩子（框架回调）
    ///     </code>
    ///
    /// 【约定】
    ///   · id 由 <see cref="RegisterId"/> 按 key 分配，**进程内稳定**（同一版本双方顺序一致，
    ///     因为双方跑的是同一份代码；跨版本变更时 id 可能位移 —— 与 `RoleRegistry.Id` 同一性质）
    ///   · 写入是**房主权威**：同步 RPC 标了 `OnlyHost = true`，客户端自己发的那条会被接收端忽略
    ///     （客户端本地会先应用一次，随后被房主的权威值覆盖 —— 与 Nebula 的"先本地后校正"一致）
    ///   · 状态随 `Player` 包装走：那是**每局对象**，换局自然清空 ✓（不用手动重置）
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class RoleData
    {
        private static readonly Dictionary<string, int> _idsByKey = new(StringComparer.Ordinal);
        private static readonly List<string> _keys = new();
        private static readonly object _gate = new();

        /// <summary>注册一个职业数据位（幂等）。返回稳定的数据 id；key 为空时返回 -1。</summary>
        public static int RegisterId(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            lock (_gate)
            {
                if (_idsByKey.TryGetValue(key, out var exist)) return exist;

                int id = _keys.Count;
                _keys.Add(key);
                _idsByKey[key] = id;
                LightLogger.Log($"[RoleData] 注册数据位 #{id} = {key}（共 {_keys.Count} 个）");
                return id;
            }
        }

        /// <summary>数据位的 key（日志/诊断用）。</summary>
        public static string KeyOf(int dataId)
            => dataId >= 0 && dataId < _keys.Count ? _keys[dataId] : $"#{dataId}";

        /// <summary>读一个职业数据位（没有记录时为 0 —— 与 Nebula 的默认值语义一致）。</summary>
        public static int Get(byte playerId, int dataId)
        {
            try
            {
                var p = Find(playerId);
                return p == null ? 0 : p.GetRoleData(dataId);
            }
            catch { return 0; }
        }

        /// <summary>
        /// 写一个职业数据位（**房主权威**）。
        /// 房主调用 → 广播给所有人并在本地生效；客户端调用 → 本地先生效，随后被房主的权威值覆盖。
        /// </summary>
        public static void Set(byte playerId, int dataId, int value)
        {
            if (dataId < 0) return;

            try { RPCs.RpcDefinitions.SetRoleData(playerId, dataId, value); }
            catch (Exception ex) { LightLogger.LogError("[RoleData.Set]", ex); }
        }

        /// <summary>在现有值上增减（读写 + <see cref="Set"/> 的组合，避免调用方自己写这段）。</summary>
        public static void Add(byte playerId, int dataId, int delta)
            => Set(playerId, dataId, Get(playerId, dataId) + delta);

        /// <summary>RPC 落地：本地存储 + 回调该玩家的职业钩子。</summary>
        internal static void Apply(byte playerId, int dataId, int value)
        {
            try
            {
                var p = Find(playerId);
                if (p == null) return;

                p.SetRoleData(dataId, value);

                // 通知职业（Nebula: OnUpdateRoleData(dataId, newValue)）
                try { p.Role?.OnRoleData(dataId, value); } catch { }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleData.Apply] {ex.Message}");
            }
        }

        private static Game.Player Find(byte playerId)
        {
            try { return Game.GameManager.Instance.GetPlayer(playerId); }
            catch { return null; }
        }
    }
}
