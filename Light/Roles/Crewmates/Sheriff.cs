using LightInDark.Configuration;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;

namespace Light.Roles.Crewmates;

public class Sheriff : RoleTemplate
{
    public const string Code = "Sheriff";

    public static readonly Sheriff MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => LightInDark.Color.Yellow;
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;
    // ⚠️ TODO（2026-10-25）：RoleTemplate 里 From 已改成 abstract，
    //    这里先用**改造前的默认值**；需要真实"职业来源"时替换成实际值 ✓
    public override string From => null;

    // ── 职业专属配置（照 NOS：配置对象本身就是配置项，用 `.Value` 取值 ✓）──
    static readonly RoleFloatConfig KillCooldownCfg =
        RoleConfig.Float("killCooldown", 20f, 5f, 60f, 2.5f, ConfigSuffix.Second);   // 秒 ✓
    static readonly RoleIntConfig KillsCfg =
        RoleConfig.Int("kills", 3, 1, 15);

    /// <summary>数组绑定 ✓（键自动补 `role.Sheriff.` 前缀）</summary>
    public override RoleConfigBase[] RoleConfiguration => [KillCooldownCfg, KillsCfg];

    public override bool CanKill => true;
    public override float KillCooldown => KillCooldownCfg.Value;      // ★ 不再写字符串键 ✓
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        private LightInDark.UI.Ability.AbilityButton _button;
        private PlayerTracker _tracker;
        private int _killsLeft;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            if (!AmOwner) return;

            // （不再需要 NormalizeVanillaRole ✓ —— 分配机已被完全接管，见 RuntimeRoleTemplate ✓）

            _killsLeft = KillsCfg.Value;   // ★ 配置对象直接取值 ✓（不再写字符串键）
            _tracker = new PlayerTracker(MyPlayer, RoleUtils.KillDistance(), p => !p.IsDead);
            RoleButtonManager.RegisterTracker(_tracker);

            // ★ 2026-10-25 新写法（照 NOS 的命名可选参数）：IDE 里敲 `Create(` 就能看到全部可填项 ✓
            //   不再需要打开 `RoleButtonConfig.cs` 才知道能填什么 ✓
            _button = AbilityButtonFactory.Create(this,
                label: "击杀",                 // 必选 ①（文本，或 label: "" + labelKey: "…"）
                icon: null,                     // 必选 ②（null = 用模板原图标 ✓）
                cooldown: Role.KillCooldown,    // 必选 ③
                hotkey: KeyCode.Q,              // 必选 ④
                onClick: TryKill,               // 必选 ⑤
                // ↓ 可选
                isKillButton: true,
                arrangedAsKillButton: true,
                labelType: ButtonLabelType.Impostor,
                canShow: () => _killsLeft > 0,
                canUse: () => _killsLeft > 0 && _tracker != null && _tracker.CurrentTarget != null
                                && PlayerControl.LocalPlayer.CanMove);

            RefreshUses();
        }

        private void TryKill()
        {
            var target = _tracker?.CurrentTarget;
            var me = MyPlayer?.Control;
            if (me == null || target?.Control == null || _killsLeft <= 0) return;

            var category = FactionOf(target);

            _killsLeft--;
            RefreshUses();

            if (category == RoleCategory.Crewmate)
                RpcDefinitions.Suicide(me, true, "sheriff", PlayerState.GoOff);
            else
                RpcDefinitions.MurderPlayer(me, target.Control, PlayerState.BeKilled);
        }

        private static RoleCategory FactionOf(Player player)
        {
            var template = player.Role?.Role;
            if (template != null) return template.RoleCategory;

            var data = player.Control?.Data;
            return data?.Role != null && data.Role.IsImpostor ? RoleCategory.Impostor : RoleCategory.Crewmate;
        }

        private void RefreshUses()
        {
            if (_button == null) return;
            if (_killsLeft > 0) _button.ShowUsesIcon(_killsLeft.ToString());
            else _button.HideUsesIcon();
        }
    }
}
