using System.Linq;
using InnerNet;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using Light.Utilities;
using UnityEngine;

namespace Light.Roles.Neutrals;

public class Jackal : RoleTemplate
{
    public const string Code = "Jackal";

    public static readonly Jackal MyRole = new();

    /// <summary>本职业击杀时记录的自定义死因 id（用户在 2026-10-06 要求"模组自定义死因" ✓）。</summary>
    public const string KillCauseId = "lid.death.jackal";

    /// <summary>
    /// 注册死因显示文本。
    /// ⚠️ 静态构造里做一次 —— 放在这里的原因：`MyRole` 是静态字段，
    ///    而静态字段初始化**先于**静态构造体执行 ✓ 所以注册一定发生在职业被使用之前 ✓
    /// </summary>
    static Jackal()
    {
        LightInDark.Game.DeathCause.Register(KillCauseId, "death.jackal", "被豺狼击杀");
    }

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(0f, 162f / 255f, 211f / 255f, 1f);
    public override RoleCategory RoleCategory => RoleCategory.Neutral;
    // ⚠️ TODO（2026-10-25）：RoleTemplate 里 From / RoleConfiguration 已改成 abstract，
    //    这里先用**改造前的默认值**把构建恢复；需要真实"职业来源"时替换成实际值 ✓
    public override string From => null;
    public override NeutralType NeutralType => NeutralType.Evil;
    public override string TeamCode => Code;
    public override bool CanKill => true;
    public override bool CanUseVents => true;
    // ── 职业专属配置（照 NOS：配置对象本身就是配置项，`.Value` 直接取值 ✓）──
    static readonly RoleFloatConfig KillCooldownCfg =
        RoleConfig.Float("killCooldown", 20f, 5f, 60f, 2.5f, ConfigSuffix.Second);
    static readonly RoleBoolConfig CanCreateSidekickCfg = RoleConfig.Bool("canCreateSidekick", true);
    // ★ 2026-10-25：「能不能招跟班」关掉时，这个数量项**自动隐藏** ✓（正是用户要的"配置显隐 Action"）
    static readonly RoleIntConfig MaxSidekicksCfg =
        RoleConfig.Int("maxSidekicks", 1, 0, 3).SetVisibleWhen(() => CanCreateSidekickCfg.Value);

    public override float KillCooldown => KillCooldownCfg.Value;
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    /// <summary>击杀时用自定义死因 ✓（复盘里会显示"被豺狼击杀"而不是笼统的"被击杀" ✓）</summary>
    public override string KillDeathCauseId => KillCauseId;

    public static bool CanCreateSidekick => CanCreateSidekickCfg.Value;
    public static int MaxSidekicks => MaxSidekicksCfg.Value;

        /// <summary>数组绑定 ✓（键自动补 `role.Jackal.` 前缀）</summary>
    public override RoleConfigBase[] RoleConfiguration => [KillCooldownCfg, CanCreateSidekickCfg, MaxSidekicksCfg];

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        private LightInDark.UI.Ability.AbilityButton _recruitButton;
        private PlayerTracker _recruitTracker;
        private int _killCount;

        // Role 由基类提供（ctor 存下 template）—— 审查 A2：不再各处硬写 `=> MyRole`（两个真相）

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            if (!AmOwner) return;

            // （不再需要 NormalizeVanillaRole ✓ —— 分配机已被完全接管：
            //   原版技能职业从根源上不会出现，底层必然是 Crewmate/Impostor ✓
            //   中立必然是 Crewmate → `IsImpostor` 保持 false ✓ 见 RuntimeRoleTemplate ✓）

            AbilityButtonFactory.CreateKill(this, _ => { _killCount++; RefreshRecruitHint(); });

            if (!CanCreateSidekick || MaxSidekicks <= 0) return;

            _recruitTracker = new PlayerTracker(MyPlayer, RoleUtils.KillDistance() * 1.5f,
                p => !RoleTeam.IsTeammate(MyPlayer, p));
            RoleButtonManager.RegisterTracker(_recruitTracker);

            _recruitButton = AbilityButtonFactory.Create(this, new RoleButtonConfig()
                .SetLabelType(ButtonLabelType.Crewmate)
                .SetHotkey(KeyCode.F)
                .SetLeftSide(true)
                .SetIcon(ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.SidekickButton.png", 115f))
                .SetLabelKey("Jackal.recruit")
                .SetCanUse(() =>PlayerControl.LocalPlayer.CanMove&& _killCount >= 1 && _recruitTracker?.CurrentTarget != null),
                RecruitTrackedTarget);

            RefreshRecruitHint();
        }

        private void RefreshRecruitHint()
        {
            if (_recruitButton == null) return;

            if (_killCount >= 1) _recruitButton.HideUsesIcon();
            else _recruitButton.ShowUsesIcon("1");
        }

        private void RecruitTrackedTarget()
        {
            var target = _recruitTracker?.CurrentTarget;
            if (target?.Control == null) return;

            // ★ 审查 A3：按 **CodeName** 下发（Id 会因注册顺序错位而发错职业 ✗）
            RpcDefinitions.SetRoleByCode(target.Control.PlayerId, Sidekick.MyRole.CodeName, null);
            _recruitButton?.Release();
            _recruitButton = null;
        }

        public override bool CheckWin()
        {
            var client = AmongUsClient.Instance;
            if (client == null || client.GameState != InnerNetClient.GameStates.Started) return false;

            if (RoleUtils.AliveImpostors().Any()) return false;
            if (RoleUtils.AliveEvilNeutrals().Any(p => p.Role?.Role?.VictoryTeamCode != Code)) return false;

            int team = RoleTeam.Members(Code, true).Count();
            return team > 0 && team * 2 >= RoleUtils.AlivePlayers().Count();
        }

        private void OnTeamMemberDied(PlayerDeathEvent ev)
        {
            if (ev.Player == null || MyPlayer?.Control == null) return;
            if (ev.Player.PlayerId != MyPlayer.Control.PlayerId) return;

            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;

            foreach (var member in RoleTeam.Members(Code, true).Where(p => p.Role?.Role is Sidekick).ToList())
                RpcDefinitions.SetRoleByCode(member.Control.PlayerId, MyRole.CodeName, null);   // A3：按 CodeName ✓
        }
    }
}
