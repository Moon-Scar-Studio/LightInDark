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
    // ⚠️ TODO（2026-10-25）：RoleTemplate 里 From / RoleConfiguration 已改成 abstract，
    //    这里先用**改造前的默认值**把构建恢复；需要真实"职业来源"时替换成实际值 ✓
    public override string From => null;
    public override RoleConfigBase[] RoleConfiguration => Array.Empty<RoleConfigBase>();

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

                // ★ 不再需要规整底层原版职业 ✓ —— 分配机已被完全接管
                //   （`VanillaSelectRolesBlockPatch` 的 Prefix 直接 return false →
                //     原版技能职业从根源上不会出现，说明见 RuntimeRoleTemplate ✓）

                // 普通按钮：主持人技能（秒会议）
                AbilityButtonFactory.Create(this, new RoleButtonConfig()
                    .SetLabelKey("Button.Caller.label")
                    .SetHotkey(KeyCode.E)
                    .SetCooldown(Cooldown)
                    .SetOnClickSFX("./Resources/SFX/CallerUse.mp3")
                    .SetCooldownReadySFX("./Resources/SFX/CooldownReady.mp3"),
                    () => RpcDefinitions.RpcStartMeeting());

                // 持续按钮：点一下开启效果，再点一下取消
                // ★ 2026-10-25：`effectDuration` 现在是**必选**（照 NOS）→ 示例效果给 5s ✓
                AbilityButtonFactory.CreateEffect(this, new RoleButtonConfig()
                    .SetLabel("驱散")
                    .SetCooldown(5f)
                    .SetOnClickSFX("./Resources/SFX/CallerUse.mp3"),
                    () => LightLogger.Log("[Caller] 效果触发(示例)"),
                    5f);   // ← 效果时长（秒）；0 = 持续到手动取消 ✓
                
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[Caller.OnActivated]", ex);
            }
        }
    }
}
