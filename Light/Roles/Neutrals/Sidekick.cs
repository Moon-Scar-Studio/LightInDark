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
        public override RoleTemplate Role => MyRole;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }
    }
}
