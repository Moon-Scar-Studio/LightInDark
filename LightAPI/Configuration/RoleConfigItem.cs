using System;
using LightInDark.Roles;

namespace LightInDark.Configuration
{
    /// <summary>
    /// 职业轻量配置项：职业模板在 <c>RoleConfiguration</c> 里返回若干个，
    /// 注册器自动加 <c>role.&lt;CodeName&gt;.</c> 前缀并生成完整配置项。
    /// </summary>
    public sealed class RoleConfigItem
    {
        /// <summary>配置键（不含前缀，注册时自动加 role.&lt;CodeName&gt;. 前缀）。</summary>
        public string Key { get; init; }

        /// <summary>配置类型。</summary>
        public ConfigType Type { get; init; } = ConfigType.Int;

        /// <summary>默认值（按 Type 传 int/float/bool/string[]）。</summary>
        public object Default { get; init; }

        /// <summary>数值型下限。</summary>
        public float Min { get; init; }

        /// <summary>数值型上限。</summary>
        public float Max { get; init; } = 100f;

        /// <summary>数值型步进。</summary>
        public float Step { get; init; } = 1f;

        /// <summary>值后缀（% 等）。</summary>
        public ConfigSuffix Suffix { get; init; }

        /// <summary>显示名。缺省时取翻译键 role.&lt;CodeName&gt;.&lt;Key&gt;。</summary>
        public string Label { get; init; }

        /// <summary>悬停详情说明。</summary>
        public string Detail { get; init; }

        /// <summary>生成带前缀的完整键。</summary>
        public string FullKey(RoleTemplate role) => $"role.{role.CodeName}.{Key}";

        /// <summary>生成显示名（无 Label 时按翻译键解析，缺省回退键名）。</summary>
        public string ResolveLabel(RoleTemplate role)
        {
            if (!string.IsNullOrEmpty(Label)) return Label;
            return LightInDark.Language.Language.GetStringOrKey($"role.{role.CodeName}.{Key}", $"{role.Name} {Key}");
        }
    }
}
