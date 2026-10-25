using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using Light.ChatCommands;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;

namespace Light.Roles.Crewmates;

public class Tester : RoleTemplate
{
    public const string Code = "Tester";

    public static readonly Tester MyRole = new();

    public override string CodeName => Code;

    /// <summary>船员阵营 ✓（测试用，不参与真实胜负逻辑之外的东西 ✓）</summary>
    public override RoleCategory RoleCategory => RoleCategory.Crewmate;

    public override string From => null;
    public override RoleConfigBase[] RoleConfiguration => Array.Empty<RoleConfigBase>();

    /// <summary>**淡白色**（比普通船员白、但不到纯白，避免和名字/描边糊在一起 ✓）</summary>
    public override LightInDark.Color Color => new(0.93f, 0.93f, 0.97f, 1f);

    /// <summary>不进正常抽选 ✓（只能手动切成它 ✓）</summary>
    public override bool CanBeAssigned => false;

    /// <summary>测试击杀按钮需要能杀 ✓</summary>
    public override bool CanKill => true;

    public override float KillCooldown => 10f;

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);

    // =====================================================================
    //  RPC 触发器（用户要求："有一个 Rpc 触发器可以触发 Rpc" ✓）
    // =====================================================================

    /// <summary>
    /// 测试用的 RPC：**广播**给所有客户端 ✓。
    ///
    /// ⚠️ 它用 `[LidRPC]` 标注 → 调用点会被 Harmony 拦截：
    ///    ① 序列化参数发到网络 ✓ ② 本地直接执行方法体 ✓
    ///    → 所以**自己也会收到**（本地执行）+ **别人也能收到**（网络分发）✓
    ///    这就是验证 RPC 链路最省事的办法 ✓
    /// </summary>
    [LidRPC]
    public static void TesterPing(byte fromPlayerId, string note)
    {
        try
        {
            string who = null;
            try
            {
                var local = PlayerControl.LocalPlayer;
                who = local != null && local.Data != null ? local.Data.PlayerName : "?";
            }
            catch { }

            // 每个客户端都在**自己的**聊天框里说一句 → 一眼看出"RPC 到底有没有到" ✓
            PatchManager.SendLocalMessage(
                $"<color=#EDEDF7>[测试者 RPC]</color> 来自 playerId={fromPlayerId} 的广播「{note}」" +
                $"（本端={who}）", rename: false);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[Tester.TesterPing] {ex.Message}");
        }
    }

    public class RuntimeInstance : RuntimeRoleTemplate
    {
        private EffectButton _effectButton;
        private bool _jitterAnnounced;
        private int _pingCount;

        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }

        /// <summary>往**自己**的聊天框发一条反馈 ✓（不广播 ✓）</summary>
        private void Say(string text)
        {
            try { PatchManager.SendLocalMessage($"<color=#EDEDF7>[测试者]</color> {text}", rename: false); }
            catch { }
        }

        protected override void OnActivated()
        {
            try
            {
                if (!AmOwner) return;

                // ── ① 普通按钮（冷却 10s）✓ ──
                var normalConfig = new RoleButtonConfig()
                    .SetLabel("测试·普通")
                    .SetCooldown(10f);
                bool normalWasCooling = false;
                normalConfig.OnUpdate = b =>
                {
                    // ⚠️ `OnCooldownFinished` 是**按钮类**的 protected 方法 ✗ 角色实例里没法 override ✗
                    //    → 换成在 `OnUpdate`（每帧都会回调 ✓）里看"冷却从进行中 → 停止"这个跳变 ✓
                    bool cooling = b != null && b.CooldownTimer.IsProgressing;
                    if (normalWasCooling && !cooling) Say("冷却结束 ✓（按钮重新可点）");
                    normalWasCooling = cooling;
                };
                AbilityButtonFactory.Create(this, normalConfig, () =>
                {
                    Say("普通按钮：点击 ✓ → 进入 10s 冷却（冷却结束时还会再发一条 ✓）");
                });

                // ── ② 击杀按钮 ✓ ──
                AbilityButtonFactory.CreateKill(this, target =>
                {
                    Say($"击杀按钮：目标 = {target?.Name ?? "null"} ✓");
                });

                // ── ③ 效果按钮（6s 持续 + 最后 3s 颤动）✓ ──
                var fxConfig = new RoleButtonConfig()
                    .SetLabel("测试·效果")
                    .SetCooldown(8f);
                // ★ 2026-10-25：`effectDuration` 现在是**必选**（照 NOS）→ 直接在这里传 6f ✓
                //   （原来是创建完再 `_effectButton.EffectDuration = 6f;` ✗ 忘了写就静默变成"持续到手动取消"）
                _effectButton = AbilityButtonFactory.CreateEffect(this, fxConfig, () =>
                {
                    Say("效果按钮：点击 ✓（6s 效果，最后 3s 会颤动 ✓）");
                }, 6f);

                if (_effectButton != null)
                {
                    _effectButton.ShowEffectCountdown = true;

                    // ★ 用户要求的那套开关：剩余 3 秒开始颤动 ✓（= 原版"解除变形" ✓）
                    _effectButton.SetEndingJitter(true, 3f);

                    _effectButton.OnEffectStart = () =>
                    {
                        _jitterAnnounced = false;
                        Say("效果开始（6s）✓ 进度环按原版 SetFillUp 逐渐填满 ✓");
                    };
                    _effectButton.OnEffectEnd = () => Say("效果结束 ✓");
                }

                // 用在"每帧刷新"里检测"开始颤动"的那一刻（只报一次 ✓）
                fxConfig.OnUpdate = b => TickEffectFeedback(b as EffectButton);

                // ── ④ 会议按钮 ✓
                //   ⚠️ 上一版改成 HudGrid 的 `AlwaysShow` 路线 ✗ → 在会议里**整个消失了** ✗
                //      （`AlwaysShow` 只解决"会议中不隐藏"，但 `_hudActive`/容器状态在会议里并不成立 ✗）
                //   → 退回 `CreateMeetingAbility` ✓，但**坐标不再靠猜** ✓：
                //     现在 `MeetingAbilityButton.Position` 直接抄原版 HUD 技能按钮的位置 ✓
                //     再往右让一格 ✓（见那边的注释 ✓）
                AbilityButtonFactory.CreateMeetingAbility(this, new RoleButtonConfig()
                    .SetLabel("测试·会议")
                    .SetCooldown(0f), () =>
                    {
                        Say("会议按钮：点击 ✓");
                    });

                // ── ⑤ 会议目标按钮（会议中点头像）✓ ──
                AbilityButtonFactory.CreateMeetingTarget(this, (meeting, target) =>
                {
                    Say($"会议目标按钮：点了 {target?.Data?.PlayerName ?? "null"} ✓");
                });

                // ── ⑥ RPC 触发按钮（0 冷却，可以连点）✓ ──
                AbilityButtonFactory.Create(this, new RoleButtonConfig()
                    .SetLabel("测试·RPC")
                    .SetCooldown(0f), () =>
                    {
                        _pingCount++;
                        Say($"触发 RPC 第 {_pingCount} 次 ✓（**每个客户端**的聊天框都会出现「[测试者 RPC]」✓）");

                        try
                        {
                            var local = MyPlayer?.Control;
                            if (local != null) Tester.TesterPing(local.PlayerId, $"ping #{_pingCount}");
                        }
                        catch (Exception ex)
                        {
                            Say($"RPC 发送失败：{ex.Message} ✗");
                        }
                    });

                Say("测试者已就绪 ✓ —— 6 个按钮都点一遍，看聊天框的反馈即可 ✓");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[Tester.OnActivated]", ex);
            }
        }

        /// <summary>检测"开始颤动"那一刻并报一次 ✓</summary>
        private void TickEffectFeedback(EffectButton fx)
        {
            try
            {
                if (fx == null || !fx.IsInEffect || _jitterAnnounced) return;

                float remain = fx.EffectTimeRemaining;
                if (remain > 0f && remain <= 3f)
                {
                    _jitterAnnounced = true;
                    Say($"效果剩余 {remain:0.0}s → **开始颤动** ✓（图标抖动，按钮本体不动 ✓）");
                }
            }
            catch { }
        }

        /// <summary>冷却结束时也发一条 ✓（用户点名的反馈之一 ✓）</summary>
    }
}
