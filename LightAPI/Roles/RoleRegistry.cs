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
                    if (_rolesByType.ContainsKey(type)) continue;

                    try
                    {
                        // ⚠️⚠️ **必须优先用职业类里的静态单例**（约定 `public static readonly Xxx MyRole = new()`）。
                        //
                        //  原来无条件 `Activator.CreateInstance` 会造出**第二个实例**：
                        //    · 被注册的那个拿到真 Id（1..N）
                        //    · 而 `MyRole` 单例的 Id 永远是默认值 0
                        //  但运行时侧 (`Xxx.cs: public override RoleTemplate Role => MyRole`) 和
                        //  兜底分配 (`StandardRoleAllocator` 用 `Crewmate.MyRole` / `Impostor.MyRole`)
                        //  **走的都是 MyRole** → `RoleTable.Determine()` 发出去的 roleId = 0
                        //  → 接收端 `GetById(0)` 解析成"扫描顺序里的第 0 个职业"（可能是 Caller）
                        //  → **所有没分到自定义职业的玩家被静默分到错误职业，且日志无异常** ✗✗
                        //
                        //  取到单例后 Id 就落在同一个对象上，两侧彻底一致 ✓
                        var template = GetStaticSingleton(type);

                        if (template == null)
                        {
                            if (type.GetConstructor(Type.EmptyTypes) == null)
                            {
                                LightLogger.LogWarning($"[RoleRegistry] {type.Name} 既没有静态单例、也没有无参构造，跳过自动注册");
                                continue;
                            }
                            template = (RoleTemplate)Activator.CreateInstance(type);
                        }

                        if (Register(template) != null) count++;
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogError($"[RoleRegistry] 自动注册 {type.Name} 失败", ex);
                    }
                }

                // 打一份完整清单（含 Id）—— 「静默发错职业」这类问题只有靠这张表才能发现
                try
                {
                    var list = string.Join(", ", _roles.Values.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.CodeName}"));
                    LightLogger.Log($"[RoleRegistry] {assembly.GetName().Name} 自动扫描注册职业 {count} 个 → {list}");
                }
                catch
                {
                    LightLogger.Log($"[RoleRegistry] {assembly.GetName().Name} 自动扫描注册职业 {count} 个");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("RoleRegistry.RegisterAssembly", ex);
            }
        }

        /// <summary>
        /// 新职业注册成功时触发（2026-10-06 审查）。
        ///
        /// ⚠️ 用途：配置注册器（在 **Light** 程序集里，本类在 API 程序集，不能反向引用）需要知道
        ///   "又来了一个新职业" 才能给它补上 `role.X.count` / `role.X.chance` 配置块 ——
        ///   否则第三方/后加载的职业**永远拿不到配置项**（`GetMaxCount` 回退代码默认值、
        ///   配置界面里也看不到它）✗
        /// </summary>
        public static event Action<RoleTemplate>? RoleRegistered;

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

                // 通知订阅者（配置注册器等）—— 自身异常不能影响注册结果
                try { RoleRegistered?.Invoke(role); }
                catch (Exception ex) { LightLogger.LogWarning($"[RoleRegistry] RoleRegistered 回调失败：{ex.Message}"); }

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

        /// <summary>
        /// 取职业类里的**静态单例**（约定 `public static readonly Xxx MyRole = new()`）。
        ///
        /// ⚠️⚠️ 为什么必须优先用它（而不是 `Activator.CreateInstance`）：
        ///   运行时侧 (`Xxx.cs: public override RoleTemplate Role => MyRole`) 和
        ///   兜底分配 (`StandardRoleAllocator` 用 `Crewmate.MyRole` / `Impostor.MyRole`)
        ///   **全都指向这个单例**。若注册时另造一个实例，就会有两个"真相"：
        ///     · 注册那份拿到真 Id（1..N）
        ///     · 单例那份 Id 恒为 0
        ///   → `RoleTable.Determine()` 发出去的 roleId = 0
        ///   → 对端 `GetById(0)` 解析成"扫描顺序里的第 0 个职业"（可能是 Caller）
        ///   → **所有没分到自定义职业的玩家被静默分到错误职业，日志无异常**。
        /// </summary>
        private static RoleTemplate? GetStaticSingleton(Type type)
        {
            try
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f == null) continue;
                    if (!typeof(RoleTemplate).IsAssignableFrom(f.FieldType)) continue;
                    if (f.GetValue(null) is RoleTemplate v) return v;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleRegistry] 取 {type.Name} 的静态单例失败：{ex.Message}");
            }
            return null;
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
