using System;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using UnityEngine;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 按钮工厂：职业运行时实例创建按钮的统一入口。
    /// 所有 Create 返回已注册到 RoleButtonManager 的按钮实例。
    /// </summary>
    public static class AbilityButtonFactory
    {
        /// <summary>创建普通技能按钮（点击一次进冷却）。</summary>
        public static AbilityButton Create(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
            => AbilityButton.Create(role, config, onClick);

        /// <summary>杀手刀按钮：自动锁最近目标（队友与隔墙除外），冷却取 KillCooldown。</summary>
        public static AbilityButton CreateKill(RuntimeRoleTemplate role, Action<Player> onKill = null,
            Func<Player, bool> extraFilter = null, KeyCode hotkey = KeyCode.Q)
        {
            var owner = role?.MyPlayer;
            if (owner == null) return null;

            var tracker = new PlayerTracker(owner, RoleUtils.KillDistance(),
                p => !RoleTeam.IsTeammate(owner, p) && (extraFilter?.Invoke(p) ?? true));
            RoleButtonManager.RegisterTracker(tracker);

            return AbilityButton.Create(role, new RoleButtonConfig { IsKillButton = true }
                .SetArrangedAsKillButton(true)
                .SetLabelType(ButtonLabelType.Impostor)
                .SetHotkey(hotkey)
                .SetLabelKey("Button.Kill.label")
                .SetCooldown(role.Role.KillCooldown)
                .SetCanUse(() => tracker.CurrentTarget != null&&PlayerControl.LocalPlayer.CanMove),
                () =>
                {
                    var target = tracker.CurrentTarget;
                    if (target?.Control == null) return;

                    // ★ 死因：**由职业决定** ✓（`RoleTemplate.KillDeathCauseId`，模组可自定义）
                    //   没覆写 → null → `SetDeath` 自动映射成内置"被击杀" ✓（行为与改动前一致 ✓）
                    RpcDefinitions.MurderPlayer(owner.Control, target.Control, PlayerState.BeKilled,
                        role.Role.KillDeathCauseId);
                    onKill?.Invoke(target);
                });
        }

        /// <summary>创建持续效果按钮（效果期间可再点取消，见 EffectButton 配置）。</summary>
        public static EffectButton CreateEffect(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
            => EffectButton.Create(role, config, onClick);

        /// <summary>创建会议目标按钮（会议中为每位玩家生成目标按钮）。</summary>
        public static MeetingTargetButton CreateMeetingTarget(RuntimeRoleTemplate role,
            Action<MeetingHud, PlayerControl> onClick,
            Func<PlayerControl, bool> canAdd = null,
            Sprite icon = null,
            string sfx = null)
            => MeetingTargetButton.Create(role, onClick, canAdd, icon, sfx);

        /// <summary>创建会议右下角按钮（会议期间显示）。</summary>
        public static MeetingAbilityButton CreateMeetingAbility(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
            => MeetingAbilityButton.Create(role, config, onClick);
    }
}
