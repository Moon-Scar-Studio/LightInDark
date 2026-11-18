using System.Collections.Generic;
using System.Linq;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;
using Color = LightInDark.Color;

namespace Light.Roles.Crewmates;

public class Oracle : RoleTemplate
{
    public const string Code = "Oracle";

    public static readonly Oracle MyRole = new();

    public override string CodeName => Code;
    public override Color Color => new(214, 156, 45);
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;
    public override string From => null;

    static readonly RoleIntConfig MaxUsesCfg = RoleConfig.Int("maxUses", 5, 0, 10);
    static readonly RoleFloatConfig CooldownCfg = RoleConfig.Float("cooldown", 20f, 0f, 60f, 2.5f, ConfigSuffix.Second);
    static readonly RoleFloatConfig ExtraCooldownCfg = RoleConfig.Float("extraCooldown", 1f, 0f, 30f, 0.5f, ConfigSuffix.Second);
    static readonly RoleIntConfig CandidatesCfg = RoleConfig.Int("candidates", 6, 1, 6);
    static readonly RoleBoolConfig DieOnOracleCfg = RoleConfig.Bool("dieOnOracle", true);

    public override RoleConfigBase[] RoleConfiguration =>
        [MaxUsesCfg, CooldownCfg, ExtraCooldownCfg, CandidatesCfg, DieOnOracleCfg];

    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 30 };

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        private PlayerTracker _tracker;
        private RoleButtonConfig _cfg;
        private Player _target;
        private readonly List<string> _pending = new();
        private int _used;

        protected override void OnActivated()
        {
            if (!AmOwner) return;

            _tracker = new PlayerTracker(MyPlayer, RoleUtils.KillDistance(), p => !p.IsDead);
            RoleButtonManager.RegisterTracker(_tracker);

            _cfg = new RoleButtonConfig()
                .SetLabelKey("Button.Oracle.label")
                .SetCooldown(CooldownCfg.Value)
                .SetHotkey(KeyCode.E);
            _cfg.MaxUses = MaxUsesCfg.Value;
            _cfg.CanShow = () => !MyPlayer.IsDead;
            _cfg.CanUse = () => _tracker != null && _tracker.CurrentTarget != null
                                && PlayerControl.LocalPlayer.CanMove;

            AbilityButtonFactory.Create(this, _cfg, () =>
            {
                BeginCast();
                Divine();
            });

            LightLogger.Log($"[Oracle] activated cd={CooldownCfg.Value} uses={MaxUsesCfg.Value} cand={CandidatesCfg.Value}");
        }

        private void BeginCast()
        {
            _used++;
            _cfg.Cooldown = CooldownCfg.Value + ExtraCooldownCfg.Value * (_used - 1);

            _target = _tracker?.CurrentTarget;
            if (_tracker != null) _tracker.IsLocked = true;

            LightLogger.Log($"[Oracle] cast start target={(_target?.Control != null ? _target.Control.PlayerId : (byte)255)}");
        }

        private void Divine()
        {
            if (_tracker != null) _tracker.IsLocked = false;

            var me = MyPlayer?.Control;
            var target = _target;
            _target = null;

            LightLogger.Log($"[Oracle] divine enter me={(me != null)} ctrl={(target?.Control != null)} " +
                            $"roleNull={(target?.Role == null)} tmplNull={(target?.Role?.Role == null)}");

            if (me == null || target?.Control == null)
            {
                LightLogger.Log("[Oracle] divine aborted: no target");
                return;
            }
            if (me.Data != null && me.Data.IsDead) return;

            var template = target.Role?.Role;
            bool impostor = template != null
                ? template.RoleCategory == RoleCategory.Impostor
                : (target.Control.Data?.Role?.IsImpostor ?? false);
            var category = template?.RoleCategory ?? (impostor ? RoleCategory.Impostor : RoleCategory.Crewmate);

            if (template != null && DieOnOracleCfg.Value && template.CodeName == Code)
            {
                RpcDefinitions.Suicide(me, true, "oracle", PlayerState.Suicide);
                return;
            }

            var picks = PickCandidates(template, impostor, category);
            string list = string.Join(" / ", picks);

            string text = Language.Translate("role.Oracle.result")
                .Replace("%NAME%", target.Name)
                .Replace("%LIST%", list);

            LightLogger.Log($"[Oracle] divine target={target.Control.PlayerId} picks={picks.Count} len={text.Length}");
            _pending.Add(text);
        }

        private void ShowResults(MeetingStartEvent ev)
        {
            if (!AmOwner || _pending.Count == 0) return;

            var hud = HudManager.Instance;
            var chat = hud != null ? hud.Chat : null;
            var pc = PlayerControl.LocalPlayer;
            if (chat == null || pc == null) return;

            foreach (var line in _pending) chat.AddChat(pc, line, false);
            _pending.Clear();
        }

        private List<string> PickCandidates(RoleTemplate real, bool impostor, RoleCategory category)
        {
            var pool = Player.AllPlayers
                .Where(p => p.Role?.Role != null)
                .Select(p => p.Role.Role)
                .Where(r => r.RoleCategory == category && r != real && r.CanBeAssigned)
                .Distinct()
                .ToList();

            if (pool.Count == 0)
                pool = RoleRegistry.AllRoles
                    .Where(r => r.RoleCategory == category && r != real && r.CanBeAssigned)
                    .ToList();

            var result = new List<string>
            {
                real != null
                    ? ColorName(real.Name, real.Color)
                    : Language.Translate(impostor ? "role.impostor.name" : "role.crewmate.name")
            };

            while (result.Count < CandidatesCfg.Value && pool.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, pool.Count);
                result.Add(ColorName(pool[index].Name, pool[index].Color));
                pool.RemoveAt(index);
            }

            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }

            return result;
        }

        private static string ColorName(string name, Color color)
            => $"<color=#{ColorUtility.ToHtmlStringRGB(color.ToUnityColor())}>{name}</color>";
    }
}
