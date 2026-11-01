using System.Linq;
using LightInDark.Configuration;
using LightInDark.Roles;

namespace Light.Roles.Impostors;

public class EvilSwapper : SwapperRole
{
    public const string Code = "EvilSwapper";

    public static readonly EvilSwapper MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(197, 17, 17);
    public override RoleCategory RoleCategory => RoleCategory.Impostor;
    public override bool CanKill => true;
    public override bool CanUseVents => true;

    // ★ 适应当前 `RoleTemplate`（`RoleConfiguration` 现在是 `RoleConfigBase[]` ✓）
    public override string From => null;

    static readonly RoleFloatConfig KillCooldownCfg =
        RoleConfig.Float("killCooldown", 20f, 5f, 60f, 2.5f, ConfigSuffix.Second);

    public override float KillCooldown => KillCooldownCfg.Value;

    /// <summary>基类的 4 项 + 内鬼专属「击杀冷却」✓</summary>
    public override RoleConfigBase[] RoleConfiguration =>
        base.RoleConfiguration.Concat(new RoleConfigBase[] { KillCooldownCfg }).ToArray();
}
