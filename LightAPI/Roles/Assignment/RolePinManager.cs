using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.RPCs;

namespace LightInDark.Roles.Assignment
{
    /// <summary>
    /// 职业预定管理器：玩家通过 /up 指令预定下一局强制分配的职业。
    /// 仅房主维护预定表；按 PlayerId 记录，分配时消耗。
    /// 玩家未上局时预定保留，直到其参加的下一局。
    /// </summary>
    public static class RolePinManager
    {
        // playerId -> 职业 CodeName（原文保存，分配时再解析）
        private static readonly Dictionary<byte, string> _pins = new();

        /// <summary>
        /// playerId → 预定时的**玩家名**。
        ///
        /// ⚠️ 为什么需要（2026-10-06 审查）：PlayerId 会被**复用**（有人离开后新玩家拿到同一个 id），
        ///   只按 id 查预定会让**新玩家继承别人的预定**（"我明明没预定，怎么强制给了我一个职业"）。
        ///   Nebula 的做法是按**玩家名**做键（`LoadMetaRoleAssignments()` 每局重读）。
        ///   这里用"id + 名字"双校验：名字对不上就当没有，并顺手删掉。
        /// </summary>
        private static readonly Dictionary<byte, string> _pinOwners = new();

        /// <summary>处理预定请求（仅房主调用）。校验职业存在且已开启，成功后回执。</summary>
        public static void HandleRequest(byte senderPlayerId, string roleName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(roleName)) return;
                var sender = FindPlayer(senderPlayerId);
                if (sender == null) return;

                var role = ResolveRole(roleName);
                if (role == null)
                {
                    RpcDefinitions.ShowSystemMessage(senderPlayerId, $"未找到职业「{roleName}」");
                    return;
                }
                if (!IsRoleEnabled(role))
                {
                    RpcDefinitions.ShowSystemMessage(senderPlayerId, $"职业「{role.Name}」未开启，无法预定");
                    return;
                }

                _pins[senderPlayerId] = role.CodeName;
                _pinOwners[senderPlayerId] = sender.Data != null ? sender.Data.PlayerName : "";
                RpcDefinitions.ShowSystemMessage(senderPlayerId, $"已预定下一局职业：{role.Name}");
                LightLogger.Log($"[RolePin] {sender.Data.PlayerName} 预定 {role.CodeName}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RolePinManager.HandleRequest", ex);
            }
        }

        /// <summary>处理取消预定（仅房主调用）。</summary>
        public static void HandleCancel(byte senderPlayerId)
        {
            try
            {
                if (_pins.Remove(senderPlayerId))
                    RpcDefinitions.ShowSystemMessage(senderPlayerId, "已取消职业预定");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RolePinManager.HandleCancel", ex);
            }
        }

        /// <summary>分配时查询预定（不消耗）。职业未注册 / **玩家已换人**时返回 false。</summary>
        public static bool TryGetPin(byte playerId, out RoleTemplate role)
        {
            role = null;
            try
            {
                if (!_pins.TryGetValue(playerId, out var code)) return false;

                // ⚠️ 名字校验（2026-10-06 审查）：PlayerId 会被复用，
                //    只按 id 查会让新玩家"继承"上一个占着这个 id 的人的预定。
                if (_pinOwners.TryGetValue(playerId, out var ownerName) && !string.IsNullOrEmpty(ownerName))
                {
                    var now = FindPlayer(playerId);
                    var nowName = now != null && now.Data != null ? now.Data.PlayerName : null;
                    if (!string.IsNullOrEmpty(nowName) && !string.Equals(nowName, ownerName, StringComparison.Ordinal))
                    {
                        _pins.Remove(playerId);
                        _pinOwners.Remove(playerId);
                        LightLogger.Log($"[RolePin] 玩家 {playerId} 已换人（{ownerName} → {nowName}），旧预定作废");
                        return false;
                    }
                }

                role = RoleRegistry.GetByName(code);
                if (role == null) { _pins.Remove(playerId); _pinOwners.Remove(playerId); return false; }
                return true;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RolePinManager.TryGetPin", ex);
                return false;
            }
        }

        /// <summary>分配成功后消耗预定。</summary>
        public static void Consume(byte playerId)
        {
            _pins.Remove(playerId);
            _pinOwners.Remove(playerId);   // 两张表必须成对清理，否则名字会残留给下一个占用该 id 的玩家
        }

        /// <summary>清空全部预定（换房/房主变更时）。</summary>
        public static void Clear()
        {
            _pins.Clear();
            _pinOwners.Clear();
        }

        /// <summary>按 CodeName 精确匹配，再按显示名匹配。</summary>
        private static RoleTemplate ResolveRole(string name)
        {
            var role = RoleRegistry.GetByName(name);
            if (role != null) return role;
            foreach (var r in RoleRegistry.AllRoles)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        /// <summary>
        /// 职业是否开启 —— 统一走 <see cref="RoleTemplate.IsSpawnable"/>（2026-10-06 审查 #13）。
        ///
        /// ⚠️ 这里原来自己又实现了一遍"配置优先、回退默认"（和 `StandardRoleAllocator.GetMaxCount` 重复，
        ///   而且是**跨程序集**的两份，永远不可能保证同步）→ 现在只有一份实现，在 `RoleTemplate` 上。
        /// </summary>
        private static bool IsRoleEnabled(RoleTemplate role) => role.IsSpawnable();

        private static PlayerControl FindPlayer(byte id)
        {
            foreach (var pc in PlayerControl.AllPlayerControls)
                if (pc.PlayerId == id) return pc;
            return null;
        }
    }
}
