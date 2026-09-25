using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace LightInDark.Configuration
{
    /// <summary>职业配置项（注册表中的一项）。</summary>
    public sealed class RoleOptionEntry
    {
        /// <summary>所属职业内部名（CodeName）。</summary>
        public string RoleKey = "";

        /// <summary>配置键（如 MaxCount / Chance / Cooldown）。</summary>
        public string Key = "";

        /// <summary>UI 显示名。</summary>
        public string DisplayName = "";

        /// <summary>值类型（int / float / bool / string）。</summary>
        public Type ValueType = typeof(int);

        public float Min;
        public float Max;

        /// <summary>默认值。</summary>
        public object DefaultValue;

        /// <summary>读取当前值（静态成员或 cfg）。</summary>
        public Func<object> Getter;

        /// <summary>写入当前值（静态成员 + cfg）。</summary>
        public Action<object> Setter;

        /// <summary>是否为 Role 类内声明的成员（[RoleOption] 扫描所得）。</summary>
        public bool IsRoleMember;

        public object GetValue() { try { return Getter != null ? Getter() : DefaultValue; } catch { return DefaultValue; } }
        public void SetValue(object v) { try { Setter?.Invoke(v); } catch (Exception ex) { LightLogger.LogError($"[RoleOptionEntry.SetValue] {RoleKey}.{Key}", ex); } }
    }

    /// <summary>
    /// 职业配置项系统（注册表版）。
    ///
    /// 每个职业注册时自动生成两个默认配置项：
    ///  - MaxCount（最大数量，默认取 role.Allocation.MaxCount，范围 0-15）
    ///  - Chance（生成概率，默认取 role.Allocation.Chance，范围 0-100）
    /// 以及 Role 类内所有 [RoleOption] 标记的静态字段/属性（自动扫描、cfg 双向绑定）。
    ///
    /// 旧 API（GetRoleCount/GetRoleChance/GetInt/GetBool/GetFloat/GetString）保持可用，
    /// 与注册表共用同一批 cfg 键，分配机无需改动。
    /// </summary>
    public static class RoleConfig
    {
        private static ConfigFile _config;
        private const string Section = "Roles";
        private const int CountMax = 15;

        private static readonly Dictionary<string, List<RoleOptionEntry>> _options = new();

        /// <summary>必须先在插件 Load() 中调用，传入 BepInEx 的 ConfigFile。</summary>
        public static void Initialize(ConfigFile config)
        {
            _config = config;
        }

        private static bool HasConfig => _config != null;

        /// <summary>某职业是否已注册配置。</summary>
        public static bool IsRoleRegistered(string roleKey) => _options.ContainsKey(roleKey);

        /// <summary>取某职业的全部配置项（含 MaxCount/Chance 与 [RoleOption] 成员）。</summary>
        public static IReadOnlyList<RoleOptionEntry> GetRoleOptions(string roleKey)
            => _options.TryGetValue(roleKey, out var list) ? list : Array.Empty<RoleOptionEntry>();

        /// <summary>取某职业指定配置项，不存在返回 null。</summary>
        public static RoleOptionEntry GetOption(string roleKey, string key)
        {
            if (_options.TryGetValue(roleKey, out var list))
                foreach (var e in list) if (e.Key == key) return e;
            return null;
        }

        /// <summary>
        /// 注册一个职业的配置项（RoleRegistry.Register 自动调用）：
        ///  1. 默认项 MaxCount / Chance（cfg 绑定，默认取 Allocation）；
        ///  2. 反射扫描 Role 类型上 [RoleOption] 的静态字段/属性，cfg 双向绑定。
        /// </summary>
        public static void RegisterRole(Role role)
        {
            try
            {
                if (role == null || string.IsNullOrEmpty(role.CodeName)) return;
                if (_options.ContainsKey(role.CodeName)) _options.Remove(role.CodeName);

                var list = new List<RoleOptionEntry>();
                var allocation = role.Allocation;

                // 1) 默认配置项：最大数量 + 生成概率
                var maxCountDef = new ConfigDefinition(Section, $"MaxCount {role.CodeName}");
                var maxCountEntry = _config?.Bind(maxCountDef, allocation.MaxCount,
                    new ConfigDescription($"职业 {role.CodeName} 最大出现数量", new AcceptableValueRange<int>(0, CountMax)));
                list.Add(new RoleOptionEntry
                {
                    RoleKey = role.CodeName,
                    Key = "MaxCount",
                    DisplayName = "最大数量",
                    ValueType = typeof(int),
                    Min = 0, Max = CountMax,
                    DefaultValue = allocation.MaxCount,
                    Getter = () => maxCountEntry != null ? maxCountEntry.Value : allocation.MaxCount,
                    Setter = v => { if (maxCountEntry != null) maxCountEntry.Value = Convert.ToInt32(v); },
                    IsRoleMember = false,
                });

                var chanceDef = new ConfigDefinition(Section, $"Chance {role.CodeName}");
                var chanceEntry = _config?.Bind(chanceDef, allocation.Chance,
                    new ConfigDescription($"职业 {role.CodeName} 分配概率 %", new AcceptableValueRange<int>(0, 100)));
                list.Add(new RoleOptionEntry
                {
                    RoleKey = role.CodeName,
                    Key = "Chance",
                    DisplayName = "生成概率",
                    ValueType = typeof(int),
                    Min = 0, Max = 100,
                    DefaultValue = allocation.Chance,
                    Getter = () => chanceEntry != null ? chanceEntry.Value : allocation.Chance,
                    Setter = v => { if (chanceEntry != null) chanceEntry.Value = Convert.ToInt32(v); },
                    IsRoleMember = false,
                });

                // 2) 反射扫描 [RoleOption] 静态字段/属性
                ScanRoleMembers(role, list);

                _options[role.CodeName] = list;
                LightLogger.Log($"[RoleConfig] 已注册职业配置: {role.CodeName}（{list.Count} 项）");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleConfig.RegisterRole]", ex);
            }
        }

        private static void ScanRoleMembers(Role role, List<RoleOptionEntry> list)
        {
            var type = role.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            foreach (var field in type.GetFields(flags))
            {
                var attr = field.GetCustomAttribute<RoleOptionAttribute>();
                if (attr == null) continue;
                string key = attr.Key ?? field.Name;
                object def = attr.DefaultValue ?? field.GetValue(null) ?? 0;
                var valueType = field.FieldType;
                list.Add(CreateMemberEntry(role.CodeName, key, attr, def, valueType,
                    () => field.GetValue(null),
                    v => field.SetValue(null, ConvertValue(v, valueType))));
            }

            foreach (var prop in type.GetProperties(flags))
            {
                var attr = prop.GetCustomAttribute<RoleOptionAttribute>();
                if (attr == null) continue;
                if (!prop.CanRead || !prop.CanWrite) continue;
                string key = attr.Key ?? prop.Name;
                object def = attr.DefaultValue ?? prop.GetValue(null) ?? 0;
                var valueType = prop.PropertyType;
                list.Add(CreateMemberEntry(role.CodeName, key, attr, def, valueType,
                    () => prop.GetValue(null),
                    v => prop.SetValue(null, ConvertValue(v, valueType))));
            }
        }

        private static RoleOptionEntry CreateMemberEntry(string roleKey, string key, RoleOptionAttribute attr,
            object def, Type valueType, Func<object> getter, Action<object> setter)
        {
            // 用 cfg 键 {RoleKey}.{Key} 绑定；cfg 值优先，写回静态成员
            var defn = new ConfigDefinition(Section, $"{roleKey}.{key}");
            var entry = BindTyped(defn, def, attr.Min, attr.Max, valueType);

            // cfg -> 静态成员（保证 Role 代码读到的就是 cfg 值）
            if (entry != null)
            {
                try { setter(entry.Get()); } catch { }
            }

            return new RoleOptionEntry
            {
                RoleKey = roleKey,
                Key = key,
                DisplayName = attr.DisplayName ?? key,
                ValueType = valueType,
                Min = attr.Min,
                Max = attr.Max,
                DefaultValue = def,
                Getter = () => entry != null ? entry.Get() : getter(),
                Setter = v => { if (entry != null) entry.Set(v); setter(v); },
                IsRoleMember = true,
            };
        }

        /// <summary>带类型化 Get/Set 的配置项包装（避免 ConfigEntryBase 无 Value 的问题）。</summary>
        private sealed class EntryHolder
        {
            public Func<object> Get;
            public Action<object> Set;
        }

        private static EntryHolder BindTyped(ConfigDefinition def, object defaultValue, float min, float max, Type valueType)
        {
            try
            {
                if (_config == null) return null;
                if (valueType == typeof(int) || valueType == typeof(short) || valueType == typeof(byte))
                {
                    var e = _config.Bind(def, Convert.ToInt32(defaultValue),
                        new ConfigDescription("", new AcceptableValueRange<int>((int)min, (int)max)));
                    return new EntryHolder { Get = () => e.Value, Set = v => e.Value = Convert.ToInt32(v) };
                }
                if (valueType == typeof(float) || valueType == typeof(double))
                {
                    var e = _config.Bind(def, Convert.ToSingle(defaultValue),
                        new ConfigDescription("", new AcceptableValueRange<float>(min, max)));
                    return new EntryHolder { Get = () => e.Value, Set = v => e.Value = Convert.ToSingle(v) };
                }
                if (valueType == typeof(bool))
                {
                    var e = _config.Bind(def, Convert.ToBoolean(defaultValue));
                    return new EntryHolder { Get = () => e.Value, Set = v => e.Value = Convert.ToBoolean(v) };
                }
                var es = _config.Bind(def, Convert.ToString(defaultValue) ?? "");
                return new EntryHolder { Get = () => es.Value, Set = v => es.Value = Convert.ToString(v) ?? "" };
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleConfig.BindTyped]", ex);
                return null;
            }
        }

        private static object ConvertValue(object v, Type target)
        {
            try
            {
                if (target == typeof(int)) return Convert.ToInt32(v);
                if (target == typeof(float)) return Convert.ToSingle(v);
                if (target == typeof(double)) return Convert.ToDouble(v);
                if (target == typeof(bool)) return Convert.ToBoolean(v);
                if (target == typeof(string)) return Convert.ToString(v) ?? "";
                return v;
            }
            catch { return v; }
        }

        // ── 旧 API（兼容，与注册表共用 cfg 键）──

        /// <summary>读取职业最大数量配置（默认 = <paramref name="defaultCount"/>，范围 0-<see cref="CountMax"/>）。</summary>
        public static int GetRoleCount(string roleKey, int defaultCount)
        {
            var def = new ConfigDefinition(Section, $"MaxCount {roleKey}");
            return HasConfig ? _config.Bind(def, defaultCount, new ConfigDescription($"职业 {roleKey} 最大出现数量", new AcceptableValueRange<int>(0, CountMax))).Value
                             : defaultCount;
        }

        /// <summary>读取职业分配概率配置（默认 = <paramref name="defaultChance"/>，范围 0-100）。</summary>
        public static int GetRoleChance(string roleKey, int defaultChance)
        {
            var def = new ConfigDefinition(Section, $"Chance {roleKey}");
            return HasConfig ? _config.Bind(def, defaultChance, new ConfigDescription($"职业 {roleKey} 分配概率 %", new AcceptableValueRange<int>(0, 100))).Value
                             : defaultChance;
        }

        /// <summary>自定义 Int 配置项，以 roleKey.optionName 绑定形参读取。</summary>
        public static int GetInt(string roleKey, string optionName, int defaultValue, int min = 0, int max = 100)
        {
            var def = new ConfigDefinition(Section, $"{roleKey}.{optionName}");
            return HasConfig ? _config.Bind(def, defaultValue, new ConfigDescription($"{roleKey}.{optionName}", new AcceptableValueRange<int>(min, max))).Value
                             : defaultValue;
        }

        /// <summary>自定义 Bool 配置项。</summary>
        public static bool GetBool(string roleKey, string optionName, bool defaultValue)
        {
            var def = new ConfigDefinition(Section, $"{roleKey}.{optionName}");
            return HasConfig ? _config.Bind(def, defaultValue).Value : defaultValue;
        }

        /// <summary>自定义 Float 配置项。</summary>
        public static float GetFloat(string roleKey, string optionName, float defaultValue, float min = 0f, float max = 100f)
        {
            var def = new ConfigDefinition(Section, $"{roleKey}.{optionName}");
            return HasConfig ? _config.Bind(def, defaultValue, new ConfigDescription($"{roleKey}.{optionName}", new AcceptableValueRange<float>(min, max))).Value
                             : defaultValue;
        }

        /// <summary>自定义 String 配置项。</summary>
        public static string GetString(string roleKey, string optionName, string defaultValue)
        {
            var def = new ConfigDefinition(Section, $"{roleKey}.{optionName}");
            return HasConfig ? _config.Bind(def, defaultValue).Value : defaultValue;
        }
    }
}
