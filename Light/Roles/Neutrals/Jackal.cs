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

    public override string CodeName => Code;
    public override LightInDark.Color Color => new(0f, 162f / 255f, 211f / 255f, 1f);
    public override RoleCategory RoleCategory => RoleCategory.Neutral;
    public override NeutralType NeutralType => NeutralType.Evil;
    public override string TeamCode => Code;
    public override bool CanKill => true;
    public override bool CanUseVents => true;
    public override float KillCooldown => ConfigRegistry.GetFloat($"role.{Code}.killCooldown");
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    public static bool CanCreateSidekick => ConfigRegistry.GetBool($"role.{Code}.canCreateSidekick");
    public static int MaxSidekicks => ConfigRegistry.GetInt($"role.{Code}.maxSidekicks");

    public override RoleConfigItem[] RoleConfiguration => new[]
    {
        new RoleConfigItem { Key = "killCooldown", Type = ConfigType.Float, Default = 20f, Min = 5f, Max = 60f, Step = 2.5f, Suffix = ConfigSuffix.Second },
        new RoleConfigItem { Key = "canCreateSidekick", Type = ConfigType.Bool, Default = true },
        new RoleConfigItem { Key = "maxSidekicks", Type = ConfigType.Int, Default = 1, Min = 0, Max = 3, Step = 1 },
    };

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
                .SetCanUse(() => _killCount >= 1 && _recruitTracker?.CurrentTarget != null)
                .SetCanRunCooldownPredicate((t) => PlayerControl.LocalPlayer.CanMove),
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

            RpcDefinitions.SetRole(target.Control.PlayerId, Sidekick.MyRole.Id, null);
            _recruitButton?.Release();
            _recruitButton = null;
        }

        public override bool CheckWin()
        {
            var client = AmongUsClient.Instance;
            if (client == null || client.GameState != InnerNetClient.GameStates.Started) return false;

            if (RoleUtils.AliveImpostors().Any()) return false;

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
                RpcDefinitions.SetRole(member.Control.PlayerId, MyRole.Id, null);
        }
    }
}
