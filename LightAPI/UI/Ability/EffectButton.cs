using System;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using UnityEngine;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 持续按钮（效果按钮）：点一下开启效果，效果持续中再点一下关闭（可配置）。
    /// 参考原版的"强制解除变形"：效果进行中按钮仍可点击，点击即取消效果。
    ///
    /// 关键配置：
    ///  - <see cref="AllowCancelByReclick"/>：默认为 true；为 true 时效果持续中再次点击可取消效果。
    ///  - <see cref="EffectDuration"/>：效果持续时间（秒）；&lt;=0 表示持续到手动取消。
    ///  - <see cref="OnEffectStart"/> / <see cref="OnEffectEnd"/>：效果启停回调。
    ///
    /// 点击流程：未在效果中 → 执行主点击回调 + 开启效果；已在效果中且 AllowCancelByReclick → 关闭效果。
    /// </summary>
    public class EffectButton : AbilityButton
    {
        public AbilityTimer EffectTimer { get; } = new(0f);
        public override IAbilityTimer CurrentTimer => _inEffect ? EffectTimer : CooldownTimer;
        private float _effectTimer => EffectTimer.Remaining;
        private bool _inEffect;

        private static readonly UnityEngine.Color EffectColor = new(0f, 1f, 0f, 1f);
        private static readonly UnityEngine.Color NormalColor = UnityEngine.Color.white;

        private Action _onEffectStart;
        private Action _onEffectEnd;

        /// <summary>为 true 时效果持续中再次点击可取消效果（默认 true）。</summary>
        public bool AllowCancelByReclick { get; set; } = true;

        /// <summary>
        /// 效果持续时间（秒）；&lt;=0 表示持续到手动取消（默认 0）。
        /// 注意：这是效果本身时长，与按钮冷却（Cooldown）相互独立。
        /// </summary>
        public float EffectDuration { get; set; } = 0f;

        /// <summary>是否处于效果中。</summary>
        public bool IsInEffect => _inEffect;

        /// <summary>剩余效果时间。</summary>
        public float EffectTimeRemaining => _effectTimer;

        /// <summary>效果开始回调。</summary>
        public Action OnEffectStart { set => _onEffectStart = value; }

        /// <summary>效果结束回调。</summary>
        public Action OnEffectEnd { set => _onEffectEnd = value; }

        /// <summary>是否在效果中显示倒计时（默认 true）。</summary>
        public bool ShowEffectCountdown { get; set; } = true;

        /// <summary>
        /// 效果期间是否画进度环（【2026-11-01 自 `/LightInDark (2)` 合并】✓）。
        /// `false` = 只跑计时、不显示环（挑战者那种"等待延迟"用它 ✓）
        /// </summary>
        public bool ShowEffectRing { get; set; } = true;

        // ── “时间快没了就抖动” ──（原版 `ActionButton.SetFillUp` 的行为 ✓）
        private bool _jitterEnabled = true;      // 默认开（和原版解除变形一致 ✓）
        private float _jitterThreshold = 3f;     // 剩余 3 秒开始抖（原版写死 3f ✓）

        /// <summary>
        /// **选择性地**启用"时间快没了就抖动"（用户 2026-10-10 要求 ✓）。
        ///
        /// 用法（就是你举的例子 ✓）：
        /// <code>
        ///   SetEndingJitter(true, 3f);    // 会抖，剩余 3 秒时开始（和原版"解除变形"一样 ✓）
        ///   SetEndingJitter(true, 5f);    // 会抖，提前到剩余 5 秒开始
        ///   SetEndingJitter(false);       // 完全不抖（数字仍然照原版那样，最后 3 秒才出现 ✓）
        /// </code>
        ///
        /// ⚠️ 抖动只动**图标**（`graphic.transform`），不动按钮本体 ✓
        ///    —— 与按钮本体上的排序/点击区域无关，所以不会影响点击 ✓
        /// </summary>
        public void SetEndingJitter(bool enabled, float threshold = 3f)
        {
            _jitterEnabled = enabled;
            _jitterThreshold = Mathf.Max(0f, threshold);
        }

        protected EffectButton(RuntimeRoleTemplate role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { EffectTimer.Completed += StopEffect; }

        /// <summary>创建持续按钮并注册到管理器。</summary>
        public static new EffectButton Create(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
        {
            try
            {
                var button = new EffectButton(role, role.MyPlayer, config, onClick);
                RoleButtonManager.Register(button);
                return button;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[EffectButton.Create]", ex);
                return null;
            }
        }

        protected override void HandleClick()
        {
            try
            {
                if (IsBroken) return;
                if (!CanAcceptInput || InputBlocked()) return;
                if (_lastClickFrame == Time.frameCount) return;

                // 效果持续中再次点击 → 取消效果（默认允许）
                if (_inEffect && AllowCancelByReclick)
                {
                    _lastClickFrame = Time.frameCount;
                    StopEffect();
                    return;
                }

                // ⚠️⚠️ 2026-10-06 用户报「按钮可以点两次」——根因就在这里：
                //   本方法**覆写了基类的 HandleClick，却把基类那三道闸全丢了** ✗
                //   ① `ShouldBeUsable`（可用性重算）② `CanInteract()` ③ **`_lastClickFrame` 同帧去重**
                //   其中 ③ 最要命：原版 `PassiveButton.OnClick` 与我们自己的鼠标半径判定
                //   **会在同一帧各触发一次** → 第一次"放技能"、第二次（同一帧）命中上面那条
                //   `AllowCancelByReclick` → **立刻把效果取消了** ✗
                //   → 表现就是用户说的"点一下进了 CD，但什么都没发生 / 还能再点一次" ✓
                if (!ShouldBeUsable) return;

                var action = Button;
                if (action != null && !action.CanInteract()) return;

                if (_lastClickFrame == Time.frameCount) return;   // ★ 同帧去重（与基类同一套）
                _lastClickFrame = Time.frameCount;

                // 效果中（且不允许重击取消）→ 仍然不能重复放技能 ✓
                // （`ShouldBeUsable` 在效果中**恒为 true**（为了允许取消 ✓），所以这里必须再拦一道 ✓）
                if (_inCooldown || _inEffect) return;

                if (!_config.CanUse()) return;
                if (HasLimitedUses && _usesLeft <= 0) return;

                PlayOnClickSFX();
                _onClick?.Invoke();

                if (HasLimitedUses)
                {
                    _usesLeft--;
                    if (_actionButton != null) _actionButton.SetUsesRemaining(_usesLeft);
                }

                // ⚠️⚠️ 2026-10-10 用户报「效果按钮持续时间和冷却时间共用了」——
                //   原来这里是：`StartEffect(); StartCooldown();` ✗ —— **点一下就同时开了两条计时** ✓
                //   而且效果期间还会 `TickCooldown()` ✗ → 效果结束时冷却也快走完了 ✓
                //   → 看起来就是"效果时长 = 冷却时长"✗
                //
                //   ★ 原版语义（参考"解除变形"）：**先跑效果，效果结束后才开始冷却** ✓
                //     → 这里只 StartEffect() ✓；`StopEffect()` 里才开始冷却 ✓
                StartEffect();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.HandleClick] {ex.Message}");
            }
        }

        /// <summary>
        /// 效果倒计时用的文本（自带"按钮还在"的判断）。
        /// ⚠️ 不要写 `_actionButton?.buttonLabelText`（AGENTS §4.6.1）：`ActionButton` 是 Unity 对象，
        ///   被销毁成假 null 时 `?.` 挡不住，访问 `buttonLabelText` 会抛 `MissingReferenceException` ✗
        /// </summary>
        private TMPro.TextMeshPro? EffectLabel
        {
            get
            {
                var ab = _actionButton;
                if (ab == null) return null;          // == 走 UnityEngine.Object 重载，能识别假 null
                return ab.buttonLabelText;
            }
        }

        /// <summary>开启效果。EffectDuration&lt;=0 表示持续到手动取消（StopEffect 或 AllowCancelByReclick 重击）。</summary>
        public void StartEffect()
        {
            try
            {
                EffectTimer.SetDuration(EffectDuration <= 0f ? float.MaxValue : EffectDuration);
                EffectTimer.Start();
                _inEffect = true;
                _onEffectStart?.Invoke();
                if (ShowEffectCountdown && EffectLabel != null)
                    _actionButton.buttonLabelText.color = EffectColor;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.StartEffect] {ex.Message}");
            }
        }

        /// <summary>手动结束效果。</summary>
        public void StopEffect()
        {
            try
            {
                if (!_inEffect) return;
                _inEffect = false;
                EffectTimer.Stop();
                _onEffectEnd?.Invoke();
                ResetGraphicPos();          // ★ 停止抖动、图标回原位 ✓

                // ★ 把冷却数字那层改回原版颜色（NOS 也是在结束时还原 ✓）
                var action = Button;
                if (action != null && action.cooldownTimerText != null)
                    action.cooldownTimerText.color = NormalColor;

                // ★ 效果结束 → **现在**才开始冷却（原版语义 ✓，见 HandleClick 的说明）
                if (_config.Cooldown > 0f) StartCooldown();

                if (EffectLabel != null)
                {
                    _actionButton.buttonLabelText.color = NormalColor;
                    _actionButton.buttonLabelText.text = _config.ResolvedLabel;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.StopEffect] {ex.Message}");
            }
        }

        public override void Update()
        {
            if (IsDeadObject || !HasUI) return;
            if (_player == null || _player.IsDead) StopEffect();
            try
            {
                if (_inEffect)
                {
                    EffectTimer.Tick(Time.deltaTime);

                    // ⚠️ 2026-10-10：效果期间**不再推进冷却** ✗
                    //   （原来这里调 `TickCooldown()` → 效果和冷却同时走 → 用户看到的"共用了" ✓）
                    //   原版语义：效果结束才开始冷却 ✓（现在由 `StopEffect()` 负责 ✓）

                    if (_effectTimer <= 0f)
                    {
                        StopEffect();
                    }
                    // 效果期间：不显示冷却（效果倒计时已展示）
                }
                else
                {
                    base.Update();
                    return;
                }

                // ★ 第 4 批：效果期间也走**统一的 UI 刷新出口** ✓（绿色补色已并进 `Refresh()` ✓）
                _config.OnUpdate?.Invoke(this);
                Refresh();

                UpdateHotkey();
                UpdateSubHotkey();
                UpdateMouseClick();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.Update] {ex.Message}");
            }
        }

        /// <summary>
        /// 效果期间的统一刷新：先让基类刷新（显示/可用性/进度环/闪白），
        /// **再补一次绿色** ✓
        ///
        /// ⚠️⚠️ §4.4「两边打架」：`base.Refresh()` → `UpdateUsability()` → `ActionButton.SetEnabled()`
        ///    会写 `buttonLabelText.color`（原版行为）→ **把 StartEffect 里设的绿色冲掉**，
        ///    于是"效果进行中"的绿色提示永远看不到（表现：按钮文字莫名其妙不变色）✗
        ///    所以必须在**可用性刷新之后**写 —— 放在覆写里，从此不会再有人漏掉这一步 ✓
        /// </summary>
        protected override void Refresh()
        {
            base.Refresh();

            // ★ 用户 2026-10-10："我想要绿色字"（上一版只改了标签 ✗ → 看不到绿 ✓）
            //
            //   ★ 看 NOS 的做法（`Nebula/Objects/CustomButton.cs:858-862`）：
            //     它改的是 **`cooldownTimerText`** 的颜色 ——
            //     `actionButton.cooldownTimerText.color = new Color(0F, 0.8F, 0F);`
            //     而不是按钮标签 ✓ 因为**标签每帧都会被 `ActionButton.SetEnabled/SetDisabled` 写回白色** ✗
            //     （`SetEnabled` 里 `this.buttonLabelText.color = Palette.EnabledColor;` ✓）
            //
            //   → 现在两处都上色 ✓：
            //     ① 冷却数字层（`cooldownTimerText`）—— 和 NOS 完全一样，不会被覆盖 ✓
            //     ② 标签层（`buttonLabelText`）—— 写在 `base.Refresh()` 之后，本帧最后写 ✓
            //    ⚠️ 顺序很重要：先 `base.Refresh()`（它内部会 SetEnabled 写白 ✗）再写绿 ✓
            if (!_inEffect) return;

            var action = Button;
            if (action == null) return;

            var number = action.cooldownTimerText;
            if (number != null && number.color != EffectColor) number.color = EffectColor;

            if (EffectLabel != null) _actionButton.buttonLabelText.color = EffectColor;
        }

        /// <summary>
        /// 效果进行中：进度环显示**效果剩余时长**（对齐 Nebula 的 `CurrentTimer` 语义 —— 效果计时优先，冷却其次）。
        ///
        /// ⚠️ 用户 2026-10-06 报的"装填动画错误"的另一半：
        ///   原来效果期间走的是本类自己的分支、**根本不调 `base.Update()`** →
        ///   `UpdateCooldownDisplay()` 压根不执行 → 进度环**冻结在效果开始那一刻** ✗
        ///   （而且 `_cooldownTimer` 也是停的 → 看起来就是"动画卡住了"）
        ///
        /// ⚠️ 这里**只写进度环**（`SetCooldownFill`），倒计时数字继续沿用本类既有的 `buttonLabelText` 写法 ✓
        ///    —— 若改用 `SetCoolDown`，它会顺手点亮 `cooldownTimerText`，
        ///       那就会**同时出现两个数字** ✗
        /// </summary>
        protected override void UpdateCooldownDisplay()
        {
            try
            {
                if (!_inEffect || EffectDuration <= 0f)
                {
                    base.UpdateCooldownDisplay();
                    return;
                }

                // ★★ 2026-10-10：改用**原版 `ActionButton.SetFillUp` 的语义** ✓
                //    （就是原版"解除变形"那个按钮的显示方式 ✓）
                //      · 进度环 = **已用时间**比例（越接近结束越满 ✓）
                //      · 数字写在 `cooldownTimerText`、**最后 3 秒才出现** ✓
                //      · 剩余 < 阈值时**图标抖动**（可关 ✓，见 SetEndingJitter ✓）
                //    ⚠️ 原来是"进度 = 剩余比例"的相反方向 ✗、还把数字塞进标签 ✗
                // ⚠️ 顺序：先 ApplyFillUpDisplay（它会按原版"最后 3 秒才显示数字"的规则**隐藏**数字 ✗），
                //    再强行把数字点亮成绿色 ✓ —— 反过来就会被它盖掉 ✓
                ApplyFillUpDisplay(_effectTimer, EffectDuration, _jitterEnabled, _jitterThreshold);

                // ★ `ShowEffectRing = false` → 把环清空（合并自 `/LightInDark (2)` 的语义 ✓）
                //   只清环、不动数字与抖动，因为"不显示环"不等于"不显示剩余时间" ✓
                if (!ShowEffectRing)
                {
                    var ringAction = Button;
                    if (ringAction != null) ringAction.SetCooldownFill(0f);
                }

                // ★★ 2026-10-10 用户："绿字确实出来了，但我想要点击时就显示绿字显示剩余时间" ✓
                //   原版 `SetFillUp` 的数字是"最后 3 秒才出现" ✗ → 效果按钮上前几秒只能看进度环 ✓
                //   → 效果按钮**全程**显示剩余秒数 ✓（`Mathf.CeilToInt` 与原版同一取整 ✓），并且是绿色 ✓
                var action = Button;
                if (action != null && ShowEffectCountdown)
                {
                    var t = action.cooldownTimerText;
                    if (t != null)
                    {
                        float remain = _effectTimer;
                        if (remain < 0f) remain = 0f;
                        t.text = Mathf.CeilToInt(remain).ToString();
                        t.color = EffectColor;
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.UpdateCooldownDisplay] {ex.Message}");
            }
        }

        /// <summary>效果中始终可用（可取消）；否则看基础条件。</summary>
        protected override bool ShouldBeUsable
            => !IsBroken && (_inEffect ? AllowCancelByReclick : (!_inCooldown && _config.CanUse() && (!HasLimitedUses || _usesLeft > 0)));

        internal override void NotifyMeetingStarted()
        {
            if (_config.CancelEffectOnMeeting) StopEffect();
            base.NotifyMeetingStarted();
        }
        public override void Release()
        {
            StopEffect();
            base.Release();
        }
    }
}
