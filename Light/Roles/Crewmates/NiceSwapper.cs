using LightInDark.Configuration;
using LightInDark.Roles;

namespace Light.Roles.Crewmates;

public class NiceSwapper : SwapperRole
{
    public const string Code = "NiceSwapper";

    public static readonly NiceSwapper MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(134, 55, 86);
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;

    // ★ 适应当前 `RoleTemplate`：`From` 现在是 abstract ✓
    //   （配置数组由基类 `SwapperRole.RoleConfiguration` 提供 ✓，这里不用再写 ✓）
    public override string From => null;
}
