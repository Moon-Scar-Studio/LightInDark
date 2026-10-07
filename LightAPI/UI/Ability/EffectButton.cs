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
        private float _effectTimer;
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

        protected EffectButton(RuntimeRoleTemplate role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

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

                // 效果持续中再次点击 → 取消效果（默认允许）
                if (_inEffect && AllowCancelByReclick)
                {
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
                if (_inCooldown) return;

                if (!_config.CanUse()) return;
                if (HasLimitedUses && _usesLeft <= 0) return;

                PlayOnClickSFX();
                _onClick?.Invoke();

                if (HasLimitedUses)
                {
                    _usesLeft--;
                    if (_actionButton != null) _actionButton.SetUsesRemaining(_usesLeft);
                }

                // 开始效果（效果时长复用 Cooldown 字段），并进入冷却
                StartEffect();
                if (_config.Cooldown > 0f) StartCooldown();
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
                _effectTimer = EffectDuration <= 0f ? float.MaxValue : EffectDuration;
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
                _effectTimer = 0f;
                _onEffectEnd?.Invoke();
                if (ShowEffectCountdown && EffectLabel != null)
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
            if (IsDeadObject) return;
            try
            {
                if (_inEffect)
                {
                    _effectTimer -= Time.deltaTime;

                    if (EffectDuration > 0f && ShowEffectCountdown && EffectLabel != null)
                        _actionButton.buttonLabelText.text = Mathf.CeilToInt(_effectTimer).ToString();

                    if (_effectTimer <= 0f)
                    {
                        StopEffect();
                        if (_actionButton != null)
                        {
                            _actionButton.SetCooldownFill(0f);
                            if (_actionButton.cooldownTimerText != null)
                                _actionButton.cooldownTimerText.gameObject.SetActive(false);
                        }
                    }
                    // 效果期间：不显示冷却（效果倒计时已展示）
                }
                else
                {
                    base.Update();
                    return;
                }

                // ★ 第 4 批：效果期间也走**统一的 UI 刷新出口** ——
                //   绿色提示的补色逻辑已经并进 `Refresh()` 覆写里 ✓（原来是散在这里的 ✗）
                //
                // ★★ 2026-10-06 用户报「点击后进入 CD，但 **CD 不转**」——
                //    根因就是这里：效果分支原来**完全不推冷却** ✗
                //    → 持续型效果（`EffectDuration <= 0`，例如"一直开着"的技能）
                //      期间 CD 永远停在原地，点击还能再触发一次 ✗✗
                //    修法：效果期间**照常推进冷却** ✓（Nebula 里效果与冷却也是同一条计时链 ✓）
                TickCooldown();
                Refresh();

                UpdateHotkey();
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

            if (_inEffect && ShowEffectCountdown && EffectLabel != null)
                _actionButton.buttonLabelText.color = EffectColor;
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

                var action = Button;
                if (action == null) return;

                float remain = _effectTimer;
                if (remain < 0f) remain = 0f;
                if (remain > EffectDuration) remain = EffectDuration;

                // 与 `ActionButton.SetCoolDown` 同一个公式：fill = 剩余 / 总时长（ActionButton.cs:54）
                action.SetCooldownFill(remain / EffectDuration);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.UpdateCooldownDisplay] {ex.Message}");
            }
        }

        /// <summary>效果中始终可用（可取消）；否则看基础条件。</summary>
        protected override bool ShouldBeUsable
            => !IsBroken && (_inEffect || (!_inCooldown && _config.CanUse() && (!HasLimitedUses || _usesLeft > 0)));
    }
}