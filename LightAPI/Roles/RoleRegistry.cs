using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LightInDark.Configuration;
using LightInDark.Core;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业注册中心。启动时扫描程序集自动发现全部 RoleTemplate 子类（加类文件即生效，无需改注册表），
    /// 也可手动注册。每个职业全局只保留一个模板定义（约定 MyRole 单例）。
    /// </summary>
    public static class RoleRegistry
    {
        private static readonly Dictionary<string, RoleTemplate> _roles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, RoleTemplate> _rolesByType = new();
        private static readonly Dictionary<int, RoleTemplate> _rolesById = new();
        private static int _nextId;

        /// <summary>已注册的所有职业模板。</summary>
        public static IReadOnlyCollection<RoleTemplate> AllRoles => _roles.Values;

        /// <summary>扫描程序集，自动注册其中全部职业模板子类。</summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            try
            {
                if (assembly == null) return;
                int count = 0;

                foreach (var type in SafeGetTypes(assembly))
                {
                    if (!typeof(RoleTemplate).IsAssignableFrom(type)) continue;
                    if (type.IsAbstract || !type.IsClass) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        LightLogger.LogWarning($"[RoleRegistry] {type.Name} 没有无参构造，跳过自动注册");
                        continue;
                    }
                    if (_rolesByType.ContainsKey(type)) continue;

                    try
                    {
                        var template = (RoleTemplate)Activator.CreateInstance(type);
                        if (Register(template) != null) count++;
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogError($"[RoleRegistry] 自动注册 {type.Name} 失败", ex);
                    }
                }

                LightLogger.Log($"[RoleRegistry] {assembly.GetName().Name} 自动扫描注册职业 {count} 个");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.RegisterAssembly", ex);
            }
        }

        /// <summary>注册职业模板（CodeName 为空或重复时拒绝）。返回注册后的模板，失败返回 null。</summary>
        public static RoleTemplate Register(RoleTemplate role)
        {
            try
            {
                if (!IsValid(role, out string reason))
                {
                    LightLogger.LogWarning($"拒绝注册职业 {role?.CodeName ?? "null"}：{reason}");
                    return null;
                }

                role.Id = _nextId++;
                _roles[role.CodeName] = role;
                _rolesByType[role.GetType()] = role;
                _rolesById[role.Id] = role;
                SyncMyRoleId(role);

                if (string.IsNullOrEmpty(role.IntroText))
                    LightLogger.LogWarning($"[RoleRegistry] {role.CodeName} 的开场白翻译缺失（role.{role.CodeName}.intro）");

                return role;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.Register", ex);
                return null;
            }
        }

        /// <summary>
        /// 职业类里的 static MyRole 单例不是注册那个实例，Id 会一直是 0，
        /// 拿它下发 RPC 会变成「注册顺序第一个职业」。注册时把 Id 同步过去。
        /// </summary>
        private static void SyncMyRoleId(RoleTemplate role)
        {
            try
            {
                var field = role.GetType().GetField("MyRole",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field?.GetValue(null) is RoleTemplate singleton && !ReferenceEquals(singleton, role))
                    singleton.Id = role.Id;
            }
            catch { }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        /// <summary>校验：CodeName 必须非空且不重复。</summary>
        private static bool IsValid(RoleTemplate role, out string reason)
        {
            reason = null;
            if (role == null) { reason = "role 为 null"; return false; }
            if (string.IsNullOrEmpty(role.CodeName)) { reason = "必须重写 CodeName（内部名）"; return false; }
            if (_roles.ContainsKey(role.CodeName)) { reason = $"CodeName 已注册：{role.CodeName}"; return false; }
            return true;
        }

        /// <summary>按 CodeName 获取职业模板。</summary>
        public static RoleTemplate GetByName(string name)
        {
            try { return _roles.TryGetValue(name, out var role) ? role : null; }
            catch (Exception ex) { LightLogger.LogError("RoleRegistry.GetByName", ex); return null; }
        }

        /// <summary>按类型获取职业模板。</summary>
        public static T Get<T>() where T : RoleTemplate
        {
            try { return _rolesByType.TryGetValue(typeof(T), out var role) ? role as T : null; }
            catch (Exception ex) { LightLogger.LogError("RoleRegistry.Get", ex); return null; }
        }

        /// <summary>按注册序号获取职业模板。</summary>
        public static RoleTemplate GetById(int id)
        {
            try { return _rolesById.TryGetValue(id, out var role) ? role : null; }
            catch (Exception ex) { LightLogger.LogError("RoleRegistry.GetById", ex); return null; }
        }

        /// <summary>职业是否已注册。</summary>
        public static bool IsRegistered(string name)
        {
            try { return _roles.ContainsKey(name); }
            catch (Exception ex) { LightLogger.LogError("RoleRegistry.IsRegistered", ex); return false; }
        }

        /// <summary>清空所有注册。</summary>
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
    /// 职业类型检查扩展。用法：player.HasRole&lt;Caller&gt;()
    /// </summary>
    public static class RoleTypeChecker
    {
        /// <summary>检查玩家是否拥有指定模板类型的职业</summary>
        public static bool HasRole<T>(this Game.Player player) where T : RoleTemplate
        {
            try { return player.Role?.Role is T; }
            catch (Exception ex) { LightLogger.LogError("RoleTypeChecker.HasRole", ex); return false; }
        }

        /// <summary>获取玩家的运行时实例（模板类型匹配时）</summary>
        public static RuntimeRoleTemplate GetRoleRuntime<T>(this Game.Player player) where T : RoleTemplate
        {
            try { return player.Role?.Role is T ? player.Role : null; }
            catch (Exception ex) { LightLogger.LogError("RoleTypeChecker.GetRoleRuntime", ex); return null; }
        }

        /// <summary>检查玩家是否为指定类别</summary>
        public static bool IsCategory(this Game.Player player, RoleCategory category)
        {
            try { return player.RoleCategory == category; }
            catch (Exception ex) { LightLogger.LogError("RoleTypeChecker.IsCategory", ex); return false; }
        }

        /// <summary>检查玩家是否为船员</summary>
        public static bool IsCrewmate(this Game.Player player) => player.IsCategory(RoleCategory.Crewmate);

        /// <summary>检查玩家是否为内鬼</summary>
        public static bool IsImpostor(this Game.Player player) => player.IsCategory(RoleCategory.Impostor);

        /// <summary>检查玩家是否为中立</summary>
        public static bool IsNeutral(this Game.Player player) => player.IsCategory(RoleCategory.Neutral);

        public static bool IsEvilNeutral(this Game.Player player)
            => player.IsNeutral() && player.Role?.Role?.NeutralType == NeutralType.Evil;

        public static bool IsBenignNeutral(this Game.Player player)
            => player.IsNeutral() && player.Role?.Role?.NeutralType != NeutralType.Evil;

        /// <summary>检查玩家是否存活且有职业</summary>
        public static bool IsAliveWithRole(this Game.Player player) => !player.IsDead && player.HasRole;
    }
}
