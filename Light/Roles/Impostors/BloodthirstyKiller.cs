using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using System;

namespace Light.Roles.Impostors;

/// <summary>嗜血杀手：无冷却击杀的内鬼职业。</summary>
public class BloodThirstyKiller : RoleTemplate
{
    public const string Code = "BloodThirstyKiller";

    public static readonly BloodThirstyKiller MyRole = new();

    public override string CodeName => Code;
    public override LightInDark.Color Color => LightInDark.Color.Red;
    public override RoleCategory RoleCategory => RoleCategory.Impostor;
    public override AllocationParameters Allocation => new() { MaxCount = 1, Chance = 50 };

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        // Role 由基类提供（ctor 存下 template）—— 审查 #2：不再各处硬写 `=> MyRole`（两个真相）

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        private bool _active;

        protected override void OnActivated()
        {
            // 设为原版内鬼
            var control = MyPlayer?.Control;
            if (control == null) return;
            RoleManager.Instance.SetRole(control, RoleTypes.Impostor);
            // 仅本人：每帧将击杀冷却归零
            if (!AmOwner) return;
            _active = true;
            Dispatcher.Instance?.StartCoroutine(CoZeroKillCooldown(control).WrapToIl2Cpp());
        }

        protected override void OnInactivated() => _active = false;

        private System.Collections.IEnumerator CoZeroKillCooldown(PlayerControl control)
        {
            while (_active && control != null && !control.Data.IsDead)
            {
                if (control.killTimer > 0f) control.SetKillTimer(0f);
                yield return null;
            }
        }
    }
}
