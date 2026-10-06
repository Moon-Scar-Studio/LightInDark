using AmongUs.GameOptions;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Roles.Impostors;

/// <summary>普通内鬼：未分配自定义职业时的兜底职业。</summary>
public class Impostor : RoleTemplate
{
    public static readonly Impostor MyRole = new();

    public override string CodeName => "impostor";
    public override RoleCategory RoleCategory => RoleCategory.Impostor;
    public override bool CanBeAssigned => false; // 仅作兜底，不参与随机分配

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        // Role 由基类提供（ctor 存下 template）—— 审查 #2：不再各处硬写 `=> MyRole`（两个真相）

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            var control = MyPlayer?.Control;
            if (control == null) return;
            RoleManager.Instance.SetRole(control, RoleTypes.Impostor);
        }
    }
}
