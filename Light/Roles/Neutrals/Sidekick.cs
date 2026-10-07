using LightInDark;
using LightInDark.Configuration;
using LightInDark.Roles;
using UnityEngine;

namespace Light.Roles.Neutrals;

public class Sidekick : RoleTemplate
{
    public const string Code = "Sidekick";

    public static readonly Sidekick MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => Jackal.MyRole.Color;
    public override RoleCategory RoleCategory => RoleCategory.Neutral;
    public override NeutralType NeutralType => NeutralType.Evil;
    public override string TeamCode => Jackal.Code;

    public override AllocationParameters Allocation => default;
    public override bool CanBeAssigned => false;

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        // Role 由基类提供（ctor 存下 template）—— 审查 A2：不再各处硬写 `=> MyRole`（两个真相）

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            try
            {
                if (!AmOwner) return;

                // ★ 规整底层原版职业（用户 2026-10-06 报的三个 Bug 的共同根因）：
                //   没有这一步，底层的原版技能职业会继续每帧驱动 HudManager.AbilityButton
                //   画它自己那套冷却（SetFillUp = 最后三秒才出现）✗
                //   详见 RuntimeRoleTemplate.NormalizeVanillaRole
                NormalizeVanillaRole();
            }
            catch (System.Exception ex)
            {
                LightCoreLog(ex);
            }
        }

        private static void LightCoreLog(System.Exception ex)
        {
            try { LightInDark.Core.LightLogger.LogWarning($"[Sidekick] OnActivated 失败: {ex.Message}"); } catch { }
        }
    }
}
