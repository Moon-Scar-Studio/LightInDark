using Light.Utilities;
using LightInDark.Configuration;
using LightInDark.Events;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;

namespace Light.Roles;

public abstract class SwapperRole : RoleTemplate
{
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    readonly RoleIntConfig SwapsCfg =
        RoleConfig.Int("swaps", 1, 0, 15);
    readonly RoleIntConfig RechargeTasksCfg =
        RoleConfig.Int("rechargeTasks", 2, 0, 10);
    readonly RoleBoolConfig CanOnlySwapOthersCfg =
        RoleConfig.Bool("canOnlySwapOthers", false);
    readonly RoleBoolConfig CanCallEmergencyCfg =
        RoleConfig.Bool("canCallEmergency", false);

    public override RoleConfigBase[] RoleConfiguration =>
        [SwapsCfg, RechargeTasksCfg, CanOnlySwapOthersCfg, CanCallEmergencyCfg];

    public override bool CanCallEmergencyMeeting => CanCallEmergencyCfg.Value;

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        private static Sprite _icon;
        private static Sprite Icon => _icon ??= ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.SwapperCheck.png", 115f);

        private readonly List<byte> _selected = new();
        private MeetingTargetButton _selectButton;
        private int _charges;
        private int _tasksDone;
        private int _rechargeTasks;
        private bool _used;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        protected override void OnActivated()
        {
            if (!AmOwner) return;

            // ⚠️ 2026-11-01 合并 `/LightInDark (2)` 时删除 `NormalizeVanillaRole(...)` ✗→✓
            //   本项目已完全接管原版分配，原版技能职业不会出现 → 该方法已移除 ✓
            if (Role.CanKill) AbilityButtonFactory.CreateKill(this);

            _charges = ConfigRegistry.GetInt($"role.{CodeName}.swaps");
            _rechargeTasks = ConfigRegistry.GetInt($"role.{CodeName}.rechargeTasks");
            LightInDark.Core.LightLogger.Log($"[Swapper] {CodeName} activated: charges={_charges} recharge={_rechargeTasks}");

            _selectButton = AbilityButtonFactory.CreateMeetingTarget(this, new MeetingTargetButtonConfig
            {
                Icon = Icon,
                DefaultColor = UnityEngine.Color.red,
                IncludeSelf = !ConfigRegistry.GetBool($"role.{CodeName}.canOnlySwapOthers"),
                CanShow = CanSwap,
                CanAddTarget = Alive,
                CanUseTarget = Alive,
            }, Toggle);

            AbilityButtonFactory.CreateMeetingAbility(this, new RoleButtonConfig()
                .SetIcon(Icon)
                .SetLabelKey($"Button.{CodeName}.confirm")
                .SetCanShow(() => _selected.Count == 2 && CanSwap())
                .SetCanUse(() => true), Confirm);
        }

        private bool Alive(PlayerControl p)
        {
            var data = p != null ? p.Data : null;
            return data != null && !data.IsDead && !data.Disconnected;
        }

        private bool CanSwap()
        {
            var me = MyPlayer;
            return _charges > 0 && !_used && me != null && !me.IsDead;
        }

        protected override void OnMeetingStarted(MeetingStartEvent e)
        {
            _selected.Clear();
            _used = false;
            _selectButton?.ClearTargetColors();
        }

        protected override void OnMeetingEnded(MeetingEndEvent e)
        {
            _selected.Clear();
            _selectButton?.ClearTargetColors();
        }

        private void Toggle(MeetingHud meeting, PlayerControl target)
        {
            if (!CanSwap() || target == null) return;

            byte id = target.PlayerId;
            if (_selected.Remove(id))
            {
                _selectButton?.SetTargetColor(id, null);
                return;
            }

            if (_selected.Count >= 2) return;

            _selected.Add(id);
            _selectButton?.SetTargetColor(id, UnityEngine.Color.yellow);
            LightInDark.Core.LightLogger.Log($"[Swapper] {CodeName} selected {_selected.Count}: {id}");
        }

        private void Confirm()
        {
            if (_selected.Count != 2 || !CanSwap()) return;

            byte first = _selected[0];
            byte second = _selected[1];
            _selected.Clear();
            RpcDefinitions.MeetingSwap(first, second);
            _charges--;
            _used = true;
        }

        private void OnTaskCompleted(PlayerTaskCompleteEvent e)
        {
            if (_rechargeTasks <= 0) return;

            var me = MyPlayer != null ? MyPlayer.Control : null;
            if (me == null || e.Player == null || e.Player.PlayerId != me.PlayerId) return;

            if (++_tasksDone < _rechargeTasks) return;

            _tasksDone = 0;
            _charges++;
        }
    }
}
