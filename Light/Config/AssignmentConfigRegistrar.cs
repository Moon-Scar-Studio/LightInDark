using System;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;

namespace Light.Config;

/// <summary>
/// **分配设置**配置块（2026-10-06 审查 B7）。
///
/// ⚠️ 背景：`StandardRoleAllocator` 里各类别的自定义职业数量上限原来是三个 `const`
///   （注释自称"配置系统删除后的临时硬编码"）→ 15 人局最多只有 5 个自定义职业，
///   而且玩家在配置界面里**完全无法调整** ✗
///   Nebula 对应的是 `options.assignment.crewmate / impostor / neutral`（可配置数量）✓
///
/// ✅ 安全性：这三个配置项的**默认值就是原来的 2 / 1 / 2** →
///   没人动过配置时，行为与改动前**逐字节一致**（纯增量）。
/// </summary>
internal static class AssignmentConfigRegistrar
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            var block = new ConfigBlock(
                "lid.assignment",
                LightInDark.Language.Language.Translate("config.assignment.title", "职业分配"),
                ConfigCategory.Mod);

            block.AddConfiguration(
                "lid.assignment.crewmateMax", 2, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.crewmateMax", "船员职业数上限"),
                LightInDark.Language.Language.Translate("config.assignment.crewmateMax.detail",
                    "一局里最多给几个船员分自定义职业（默认 2）"));

            block.AddConfiguration(
                "lid.assignment.impostorMax", 2, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.impostorMax", "内鬼职业数上限"),
                LightInDark.Language.Language.Translate("config.assignment.impostorMax.detail",
                    "一局里最多给几个内鬼分自定义职业（默认 2）"));

            block.AddConfiguration(
                "lid.assignment.neutralMax", 1, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.neutralMax", "中立职业数上限"),
                LightInDark.Language.Language.Translate("config.assignment.neutralMax.detail",
                    "一局里最多给几个中立分自定义职业（默认 1）"));

            // ★ 审查 B7 下半：**下限** —— "至少发这么多个"（默认 0 = 不改变现状 ✓）
            block.AddConfiguration(
                "lid.assignment.crewmateMin", 0, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.crewmateMin", "船员职业数下限"),
                LightInDark.Language.Language.Translate("config.assignment.crewmateMin.detail",
                    "一局里**至少**给几个船员分自定义职业（忽略概率，但仍受各职业数量上限约束）。默认 0 = 不限制"));

            block.AddConfiguration(
                "lid.assignment.impostorMin", 0, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.impostorMin", "内鬼职业数下限"),
                LightInDark.Language.Language.Translate("config.assignment.impostorMin.detail",
                    "一局里**至少**给几个内鬼分自定义职业（忽略概率，但仍受各职业数量上限约束）。默认 0 = 不限制"));

            block.AddConfiguration(
                "lid.assignment.neutralMin", 0, 0, 15, 1,
                LightInDark.Language.Language.Translate("config.assignment.neutralMin", "中立职业数下限"),
                LightInDark.Language.Language.Translate("config.assignment.neutralMin.detail",
                    "一局里**至少**给几个中立分自定义职业（忽略概率，但仍受各职业数量上限约束）。默认 0 = 不限制"));

            LightLogger.Log("[AssignmentConfigRegistrar] 分配设置注册完成（上限 2/2/1 + 下限 0/0/0，默认 = 现状）");
        }
        catch (Exception ex)
        {
            _registered = false;   // 失败不算注册过，下次还能重试
            LightLogger.LogError("[AssignmentConfigRegistrar.Register]", ex);
        }
    }
}
