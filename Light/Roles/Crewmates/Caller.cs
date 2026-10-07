using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using System;
using UnityEngine;

namespace Light.Roles.Crewmates;

/// <summary>召集者：可随时强制召开紧急会议的船员职业。</summary>
public class Caller : RoleTemplate
{
    public const string Code = "Caller";

    public static readonly Caller MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => LightInDark.Color.Yellow;
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;

    /// <summary>开场音效（相对路径 mp3，YouAreText 出现时播放）。</summary>
    public override string IntroSFX => "./Resources/SFX/CallerIntro.mp3";

    /// <summary>分配参数：每局必出 1 名（MaxCount/Chance 可由配置覆盖）。</summary>
    public override AllocationParameters Allocation => new() { MaxCount = 1, GuaranteedCount = 1, Chance = 100 };

    /// <summary>技能冷却（秒）。</summary>
    public static float Cooldown { get; set; } = 20f;

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);
    public override string Intro => "一份按钮";
    public override string? RoleImagePath => "RoleImage/CallerImage.png";

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        // Role 由基类提供（ctor 存下 template）—— 审查 #2：不再各处硬写 `=> MyRole`（两个真相）

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            try
            {
                if (!AmOwner) return;

                // ★ 先规整底层的原版职业（用户 2026-10-06 报的三个 Bug 的共同根因）：
                //   不换掉底层职业的话，原版技能职业（Engineer/Tracker/…）会**继续每帧**
                //   驱动 HudManager.AbilityButton 画它自己那套冷却（SetFillUp = 最后三秒才出现）
                //   → 与我们自己的按钮形成两套计时 ✗ 详见 RuntimeRoleTemplate.NormalizeVanillaRole
                NormalizeVanillaRole();

                // 普通按钮：主持人技能（秒会议）
                AbilityButtonFactory.Create(this, new RoleButtonConfig()
                    .SetLabelKey("Button.Caller.label")
                    .SetHotkey(KeyCode.E)
                    .SetCooldown(Cooldown)
                    .SetOnClickSFX("./Resources/SFX/CallerUse.mp3")
                    .SetCooldownReadySFX("./Resources/SFX/CooldownReady.mp3"),
                    () => RpcDefinitions.RpcStartMeeting());

                // 持续按钮：点一下开启效果，再点一下取消
                AbilityButtonFactory.CreateEffect(this, new RoleButtonConfig()
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
}
