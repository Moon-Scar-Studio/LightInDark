using System;

namespace LightInDark.Configuration
{
    /// <summary>
    /// 标记 Role 类中需要注册为配置项的静态字段/属性。
    /// 注册角色时（RoleRegistry.Register → RoleConfig.RegisterRole）会自动反射扫描：
    ///  1. 为每个标记成员在 BepInEx .cfg 中创建配置项（键：{RoleKey}.{Key}）；
    ///  2. 把 cfg 中的值写回该静态成员（字段 SetValue / 属性 SetValue），Role 代码直接读静态成员即可；
    ///  3. 在 MOD设置 UI 中自动生成编辑控件。
    /// 每个职业还自动注册两个默认配置项：MaxCount（最大数量）与 Chance（生成概率），无需标记。
    /// 若某个配置项没有声明在 Role 类里，则调用方必须手动通过形参传递（例如构造 RoleButtonConfig 时传入）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class RoleOptionAttribute : Attribute
    {
        /// <summary>配置键（缺省取成员名）。最终 cfg 键为 {RoleKey}.{Key}。</summary>
        public string Key;

        /// <summary>默认值（缺省取成员当前值）。</summary>
        public object DefaultValue;

        /// <summary>最小值（数值类型适用）。</summary>
        public float Min;

        /// <summary>最大值（数值类型适用）。</summary>
        public float Max;

        /// <summary>UI 显示名（可填语言键或直接文本；缺省用 Key）。</summary>
        public string DisplayName;

        public RoleOptionAttribute(string key = null, object defaultValue = null,
            float min = 0f, float max = 100f, string displayName = null)
        {
            Key = key;
            DefaultValue = defaultValue;
            Min = min;
            Max = max;
            DisplayName = displayName;
        }
    }
}
