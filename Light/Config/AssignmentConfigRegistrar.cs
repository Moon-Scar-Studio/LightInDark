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

            LightLogger.Log("[AssignmentConfigRegistrar] 分配设置注册完成（船员/内鬼/中立上限，默认 2/2/1）");
        }
        catch (Exception ex)
        {
            _registered = false;   // 失败不算注册过，下次还能重试
            LightLogger.LogError("[AssignmentConfigRegistrar.Register]", ex);
        }
    }
}
