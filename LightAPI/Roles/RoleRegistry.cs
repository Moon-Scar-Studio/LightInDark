using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业注册中心。在插件主类 Load() 中调用 RoleRegistry.Register() 注册职业。
    /// 每个职业注册一个"定义实例"（MyPlayer==null），分配时经 CreateInstance 生成绑定玩家的运行时实例。
    /// </summary>
    public static class RoleRegistry
    {
        private static readonly Dictionary<string, Role> _roles = new();
        private static readonly Dictionary<Type, Role> _rolesByType = new();
        private static readonly Dictionary<int, Role> _rolesById = new();
        private static int _nextId;

        /// <summary>已注册的所有职业定义</summary>
        public static IReadOnlyCollection<Role> AllRoles => _roles.Values;

        /// <summary>
        /// 注册职业（校验 CodeName 必须重写、Intro 不可为空；不合格则拒绝并告警）。
        /// 在插件主类 Load() 中调用。
        /// </summary>
        public static T Register<T>() where T : Role, new()
        {
            try
            {
                return Register(new T());
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.Register", ex);
                return default;
            }
        }

        /// <summary>
        /// 注册职业实例。校验失败（CodeName 为空、已注册、或 Intro 为空）时拒绝注册并返回 default。
        /// </summary>
        public static T Register<T>(T role) where T : Role
        {
            try
            {
                if (!IsValid(role, out string reason))
                {
                    LightLogger.LogWarning($"拒绝注册职业 {role?.CodeName ?? "null"}：{reason}");
                    return default;
                }

                role.Id = _nextId++;
                _roles[role.CodeName] = role;
                _rolesByType[typeof(T)] = role;
                _rolesById[role.Id] = role;

                // 自动注册该职业的配置项（[RoleOption] 扫描 + 默认 MaxCount/Chance）
                RoleConfig.RegisterRole(role);
                return role;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.Register<T>", ex);
                return default;
            }
        }

        /// <summary>
        /// 尝试注册职业，返回是否成功。
        /// </summary>
        /// <param name="logPrefix">错误日志前缀，默认 "{CodeName} 注册失败"。</param>
        /// <param name="includeStackTrace">是否输出异常堆栈（默认 true，等效 LightLogger.LogError(msg, ex)）。</param>
        public static bool TryRegister<T>(string logPrefix = null, bool includeStackTrace = true)
            where T : Role, new()
        {
            try
            {
                var role = new T();
                if (!IsValid(role, out string reason))
                {
                    string msg = $"{logPrefix ?? $"{role.CodeName} 注册失败"}：{reason}";
                    if (includeStackTrace) LightLogger.LogError(msg, new InvalidOperationException(reason));
                    else LightLogger.LogError(msg);
                    return false;
                }
                Register(role);
                return true;
            }
            catch (Exception ex)
            {
                string msg = logPrefix ?? "职业注册失败";
                if (includeStackTrace) LightLogger.LogError(msg, ex);
                else LightLogger.LogError(msg);
                return false;
            }
        }

        /// <summary>校验职业：CodeName 必须非空，Intro 必须非 null/空，CodeName 不得重复。</summary>
        private static bool IsValid(Role role, out string reason)
        {
            reason = null;
            if (role == null) { reason = "role 为 null"; return false; }
            if (string.IsNullOrEmpty(role.CodeName)) { reason = "必须重写 CodeName（内部名）"; return false; }
            if (_roles.ContainsKey(role.CodeName)) { reason = $"CodeName 已注册：{role.CodeName}"; return false; }
            if (string.IsNullOrEmpty(role.IntroBlurb)) { reason = $"Intro（开场白）为空，职业 {role.CodeName} 必须重写 IntroBlurbKey 且译文中译本不可为空"; return false; }
            return true;
        }

        /// <summary>按 CodeName（内部名）获取职业定义</summary>
        public static Role GetByName(string name)
        {
            try
            {
                return _roles.TryGetValue(name, out var role) ? role : null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.GetByName", ex);
                return null;
            }
        }

        /// <summary>按类型获取职业定义</summary>
        public static T Get<T>() where T : Role
        {
            try
            {
                return _rolesByType.TryGetValue(typeof(T), out var role) ? role as T : null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.Get", ex);
                return default;
            }
        }

        /// <summary>按注册序号获取职业定义</summary>
        public static Role GetById(int id)
        {
            try
            {
                return _rolesById.TryGetValue(id, out var role) ? role : null;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.GetById", ex);
                return null;
            }
        }

        /// <summary>职业是否已注册</summary>
        public static bool IsRegistered(string name)
        {
            try
            {
                return _roles.ContainsKey(name);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.IsRegistered", ex);
                return default;
            }
        }

        /// <summary>清空所有注册（游戏结束时调用）</summary>
        public static void Clear()
        {
            try
            {
                _roles.Clear();
                _rolesByType.Clear();
                _rolesById.Clear();
                _nextId = 0;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.Clear", ex);
            }
        }
    }

    /// <summary>
    /// 职业类型检查扩展。使用方式：player.Is&lt;Caller&gt;()
    /// 或 player.HasRole&lt;Caller&gt;()
    /// </summary>
    public static class RoleTypeChecker
    {
        /// <summary>检查玩家是否拥有指定类型的职业</summary>
        public static bool HasRole<T>(this Game.Player player) where T : Role
        {
            try
            {
                return player.Role is T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.HasRole", ex);
                return default;
            }
        }

        /// <summary>获取玩家的指定类型职业实例（如果存在）</summary>
        public static T GetRole<T>(this Game.Player player) where T : Role
        {
            try
            {
                return player.Role as T;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.GetRole", ex);
                return default;
            }
        }

        /// <summary>检查玩家是否为指定类别</summary>
        public static bool IsCategory(this Game.Player player, RoleCategory category)
        {
            try
            {
                return player.Role?.Category == category;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.IsCategory", ex);
                return default;
            }
        }

        /// <summary>检查玩家是否为船员</summary>
        public static bool IsCrewmate(this Game.Player player)
        {
            try
            {
                return player.IsCategory(RoleCategory.Crewmate);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.IsCrewmate", ex);
                return default;
            }
        }

        /// <summary>检查玩家是否为内鬼</summary>
        public static bool IsImpostor(this Game.Player player)
        {
            try
            {
                return player.IsCategory(RoleCategory.Impostor);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.IsImpostor", ex);
                return default;
            }
        }

        /// <summary>检查玩家是否为中立</summary>
        public static bool IsNeutral(this Game.Player player)
        {
            try
            {
                return player.IsCategory(RoleCategory.Neutral);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.IsNeutral", ex);
                return default;
            }
        }

        /// <summary>检查玩家是否存活且有职业</summary>
        public static bool IsAliveWithRole(this Game.Player player)
        {
            try
            {
                return !player.IsDead && player.HasRole;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleTypeChecker.IsAliveWithRole", ex);
                return default;
            }
        }
    }
}
