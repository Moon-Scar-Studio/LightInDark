using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using System;
using UnityEngine;

namespace Light.Roles.Crewmates;

/// <summary>
/// Caller（单类声明：一个职业一个类）。
/// 定义侧用虚属性（CodeName/Color/Category/IntroBlurbKey/SkillDescriptionKey/Allocation/IntroSFX），
/// 静态配置用 [RoleOption]（注册时自动绑定 cfg），技能按钮在 OnActivated 中直接声明。
/// </summary>
public class Caller : Role
{
    public const string Code = "Caller";

    public override string CodeName => Code;
    public override LightInDark.Color Color => LightInDark.Color.Yellow;
    public override RoleCategory Category => RoleCategory.Crewmate;
    public override string IntroBlurbKey => "Caller.intro";
    public override string SkillDescriptionKey => "Caller.skill";

    /// <summary>开场音效（相对路径 mp3，YouAreText 出现时播放）。TODO: 下一轮实现 mp3 解析。</summary>
    public override string IntroSFX => "./Resources/SFX/CallerIntro.mp3";

    /// <summary>分配参数：每局必出 1 名 Caller（MaxCount/Chance 可由 .cfg 覆盖）。</summary>
    public override AllocationParameters Allocation => new() { MaxCount = 1, GuaranteedCount = 1, Chance = 100 };

    /// <summary>技能冷却（秒）。[RoleOption] 自动注册进配置并写回此属性。</summary>
    [RoleOption("Cooldown", 20f, 0f, 120f, "技能冷却")]
    public static float Cooldown { get; set; } = 20f;

    protected override void OnActivated()
    {
        try
        {
            if (!AmOwner) return;

            // 普通按钮：主持人技能（秒会议）示例
            CreateAbilityButton(new RoleButtonConfig()
                .SetLabelKey("Button.Caller.label")
                .SetHotkey(KeyCode.E)
                .SetCooldown(Cooldown)
                .SetOnClickSFX("./Resources/SFX/CallerUse.mp3")
                .SetCooldownReadySFX("./Resources/SFX/CooldownReady.mp3"),
                () => RpcDefinitions.RpcStartMeeting());

            // 持续按钮（效果按钮）示例：点一下开启效果，再点一下取消（AllowCancelByReclick 默认 true）
            CreateEffectButton(new RoleButtonConfig()
                .SetLabel("驱散")
                .SetCooldown(5f)
                .SetOnClickSFX("./Resources/SFX/CallerUse.mp3"),
                () => LightLogger.Log("[Caller] 效果触发(示例)"));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[Caller.OnActivated]", ex);
        }
    }
}