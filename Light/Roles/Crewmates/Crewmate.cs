using AmongUs.GameOptions;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;

namespace Light.Roles.Crewmates;

/// <summary>普通船员：未分配自定义职业时的兜底职业。</summary>
public class Crewmate : RoleTemplate
{
    public static readonly Crewmate MyRole = new();

    public override string CodeName => "crewmate";
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;
    // ⚠️ TODO（2026-10-25）：RoleTemplate 里 From / RoleConfiguration 已改成 abstract，
    //    这里先用**改造前的默认值**把构建恢复；需要真实"职业来源"时替换成实际值 ✓
    public override string From => null;
    public override RoleConfigBase[] RoleConfiguration => Array.Empty<RoleConfigBase>();

    /// <summary>
    /// 兜底职业，**不参与随机分配 / 不进配置界面**。
    ///
    /// ⚠️ `Impostor` 一直有这一行、`Crewmate` **漏了**（2026-10-06 审查发现）→
    ///   后果：配置界面船员页会多出一个"船员"块，房主把它数量调高后它会进抽选池，
    ///   **占掉船员职业的 2 个名额**并作为"自定义职业"走一遍 RPC/职业信息显示，
    ///   与 <c>RoleConfigRegistrar</c> 注释里"兜底职业不出配置"的意图正好相反。
    /// </summary>
    public override bool CanBeAssigned => false;

    public override LightInDark.Color Color => LightInDark.Color.CrewmateColor;

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
            RoleManager.Instance.SetRole(control, RoleTypes.Crewmate);
        }
    }
}
