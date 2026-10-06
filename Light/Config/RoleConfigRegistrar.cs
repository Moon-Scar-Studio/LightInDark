using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Config;

/// <summary>
/// 职业配置自动注册器：为每个可分配职业生成配置块，
/// 含通用"数量/概率"配置与职业专属配置（RoleConfiguration 轻量项，键自动加前缀）。
/// </summary>
internal static class RoleConfigRegistrar
{
    /// <summary>已经注册过配置块的职业（CodeName）—— 支持**增量注册**。</summary>
    private static readonly HashSet<string> _registeredRoles = new(StringComparer.Ordinal);
    private static bool _hooked;

    /// <summary>
    /// 注册全部职业配置块。**可重复调用**：只补没注册过的职业。
    ///
    /// ⚠️ 2026-10-06 审查：原来是一个一次性闸门（`_registered`）→
    ///   插件启动之后才注册的职业（第三方附加包 / 运行期 `RoleRegistry.Register`）
    ///   **永远拿不到** `role.X.count` / `.chance` 配置 → 无法被开启，配置界面里也看不到它 ✗
    ///   现在：① 增量补注册 ② 订阅 `RoleRegistry.RoleRegistered`，新职业一到就补。
    ///   （`ConfigRegistry` / 配置界面是打开时读注册表的，所以补上的项下次打开就能看到 ✓）
    /// </summary>
    public static void Register()
    {
        HookRegistry();
        RegisterMissing();
    }

    private static void HookRegistry()
    {
        if (_hooked) return;
        _hooked = true;

        try
        {
            RoleRegistry.RoleRegistered += role =>
            {
                try { RegisterOne(role); }
                catch (Exception ex) { LightLogger.LogError($"[RoleConfigRegistrar] 新职业 {role?.CodeName} 配置注册失败", ex); }
            };
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[RoleConfigRegistrar] 订阅 RoleRegistered 失败：{ex.Message}");
        }
    }

    private static void RegisterMissing()
    {
        try
        {
            int added = 0;
            foreach (var role in RoleRegistry.AllRoles)
                if (RegisterOne(role)) added++;

            LightLogger.Log($"[RoleConfigRegistrar] 职业配置注册完成（本次新增 {added} 个，累计 {_registeredRoles.Count} 个）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[RoleConfigRegistrar.RegisterMissing]", ex);
        }
    }

    /// <summary>给一个职业补配置块。返回是否真的新建了（已注册过 / 兜底职业返回 false）。</summary>
    private static bool RegisterOne(RoleTemplate role)
    {
        if (role == null) return false;
        if (!role.CanBeAssigned) return false;                     // 兜底职业（普通船员/内鬼）不出配置
        if (!_registeredRoles.Add(role.CodeName)) return false;    // 已经注册过

        try
        {
            var block = new ConfigBlock(
                    $"lid.role.{role.CodeName}", role.Name, ToCategory(role.RoleCategory))
                    .SetHeaderColor(role.Color.ToUnityColor());

                // 通用配置：出现数量 / 出现概率
                //
                // ⚠️ 标签里**不要**再拼职业名（2026-10-06 用户要求）。
                //    这些项永远出现在**该职业自己的配置块/详情页**里，块头/标题已经写了职业名，
                //    再拼一遍就是"召集者 数量"这种重复（用户原话："把那个召集者删掉"）。
                //
                // ★ 但**悬停说明**要拼职业名，而且要**用职业自己的颜色**
                //   （用户 2026-10-06："数量和概率显示的文字我要 '职业名最大出现的数量'
                //     '职业名可能出现的概率'，其中职业名对应该职业的颜色"）。
                //   说明文字走 TMP 富文本，所以直接塞 <color=#RRGGBB>。
                string nameHex = RoleNameHex(role);
                var countItem = block.AddConfiguration(
                    $"role.{role.CodeName}.count", role.Allocation.MaxCount, 0, 15, 1,
                    "数量", $"<color=#{nameHex}>{role.Name}</color>最大出现的数量");
                block.AddConfiguration(
                    $"role.{role.CodeName}.chance", role.Allocation.Chance, 0, 100, 5,
                    "概率", $"<color=#{nameHex}>{role.Name}</color>可能出现的概率")
                    // ★ 依赖"数量"（2026-10-06 审查）：数量 = 0 表示这个职业不参与分配，
                    //   那"概率"就不该还能编辑。`ConfigItem.IsVisible` 会用 `DependsOn.GetBool()`，
                    //   而 `GetBool()` 是 `_value > 0.5f` —— int 型的数量项正好可用 ✓
                    //   （这套依赖机制一直存在，只是全工程没人用过；配合配置界面的增量可见性刷新即可）
                    .SetDependsOn(countItem)
                    .WithSuffix(ConfigSuffix.Percent);

                // 职业专属配置（轻量项）：同样挂在"数量"下面
                foreach (var item in role.RoleConfiguration)
                    AddItem(block, role, item, countItem);

            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError($"[RoleConfigRegistrar] 职业 {role.CodeName} 配置注册失败", ex);
            _registeredRoles.Remove(role.CodeName);     // 失败不算注册过，下次还能重试
            return false;
        }
    }

    /// <summary>把轻量配置项展开为 ConfigBlock 配置（键自动加 role.&lt;CodeName&gt;. 前缀）。</summary>
    private static void AddItem(ConfigBlock block, RoleTemplate role, RoleConfigItem item, ConfigItem countItem)
    {
        try
        {
            // ⚠️ 保留键检查（2026-10-06 审查）：`count` / `chance` 是上面自动生成的通用项，
            //   专属项如果也叫这两个名字，会**撞键** —— 而 `ConfigRegistry` 撞键时只打一条 warning
            //   然后**丢弃后注册的那个** → 职业作者会看到"我这项怎么没了"。
            //   这里提前拦下并给出明确错误。
            string bareKey = item.Key ?? "";
            if (bareKey.Equals("count", StringComparison.OrdinalIgnoreCase)
                || bareKey.Equals("chance", StringComparison.OrdinalIgnoreCase))
            {
                LightLogger.LogError($"[RoleConfigRegistrar] 职业 {role.CodeName} 的专属配置项用了**保留键**「{bareKey}」" +
                                     "—— 会和自动生成的「数量/概率」撞键并被丢弃。请改名（例如 xxxCount）。");
                return;
            }

            string key = item.FullKey(role);
            string label = item.ResolveLabel(role);
            ConfigItem added = item.Type switch
            {
                ConfigType.Bool => block.AddConfiguration(key, item.Default is bool b && b, label, item.Detail),
                ConfigType.Float => block.AddConfiguration(key, ToFloat(item.Default), item.Min, item.Max, item.Step, label, item.Detail),
                ConfigType.Value => block.AddConfiguration(key, item.Default as string[] ?? Array.Empty<string>(), label, item.Detail),
                _ => block.AddConfiguration(key, ToInt(item.Default), (int)item.Min, (int)item.Max, (int)item.Step, label, item.Detail),
            };
            if (countItem != null) added.SetDependsOn(countItem);   // 数量=0（职业关闭）时自动隐藏
            if (item.Suffix != ConfigSuffix.None) added.WithSuffix(item.Suffix);
        }
        catch (Exception ex)
        {
            LightLogger.LogError($"[RoleConfigRegistrar] 配置项 {role.CodeName}.{item.Key} 注册失败", ex);
        }
    }

    /// <summary>
    /// 职业名在富文本里用的颜色（<c>#RRGGBB</c> 大写十六进制）。
    /// 说明文字是 TMP 富文本，所以直接塞 <c>&lt;color=#RRGGBB&gt;</c> 就能给职业名单独上色。
    /// </summary>
    private static string RoleNameHex(RoleTemplate role)
    {
        try
        {
            var c = LightInDark.ColorHelper.ToUnityColor(role.Color);
            return UnityEngine.ColorUtility.ToHtmlStringRGB(c);
        }
        catch { return "FFFFFF"; }
    }
    private static int ToInt(object v) => v is int i ? i : Convert.ToInt32(v ?? 0);
    private static float ToFloat(object v) => v is float f ? f : Convert.ToSingle(v ?? 0f);

    /// <summary>阵营枚举映射到配置分类。</summary>
    private static ConfigCategory ToCategory(RoleCategory category) => category switch
    {
        RoleCategory.Crewmate => ConfigCategory.Crewmate,
        RoleCategory.Impostor => ConfigCategory.Impostor,
        // ★ 鬼魂职业归到"幽灵"分类（2026-10-06）：`ConfigCategory.Ghost` 一直存在、
        //   设置界面里也一直有那个页签，但**从来没有职业被放进去**（因为 RoleCategory 里没这个值）✗
        RoleCategory.Ghost => ConfigCategory.Ghost,
        _ => ConfigCategory.Neutral,
    };
}
