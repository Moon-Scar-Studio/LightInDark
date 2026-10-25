using LightInDark.Configuration;
using LightInDark.Roles;

namespace Light.Roles.Crewmates;

public class NiceGuesser : GuesserRole
{
    public const string Code = "NiceGuesser";

    public static readonly NiceGuesser MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => LightInDark.Color.Yellow;
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;
    // ⚠️ TODO（2026-10-25）：RoleTemplate 里 From / RoleConfiguration 已改成 abstract，
    //    这里先用**改造前的默认值**把构建恢复；需要真实"职业来源"时替换成实际值 ✓
    public override string From => null;
    public override RoleConfigBase[] RoleConfiguration => Array.Empty<RoleConfigBase>();
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };
}
