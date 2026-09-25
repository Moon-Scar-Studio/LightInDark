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

        protected EffectButton(Role role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

        /// <summary>创建持续按钮并注册到管理器。</summary>
        public static new EffectButton Create(Role role, RoleButtonConfig config, Action onClick)
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
                // 效果持续中再次点击 → 取消效果（默认允许）
                if (_inEffect && AllowCancelByReclick)
                {
                    StopEffect();
                    return;
                }

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

        /// <summary>开启效果。EffectDuration&lt;=0 表示持续到手动取消（StopEffect 或 AllowCancelByReclick 重击）。</summary>
        public void StartEffect()
        {
            try
            {
                _effectTimer = EffectDuration <= 0f ? float.MaxValue : EffectDuration;
                _inEffect = true;
                _onEffectStart?.Invoke();
                if (ShowEffectCountdown && _actionButton?.buttonLabelText != null)
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
                if (ShowEffectCountdown && _actionButton?.buttonLabelText != null)
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

                    if (EffectDuration > 0f && ShowEffectCountdown && _actionButton?.buttonLabelText != null)
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

                UpdateVisibility();
                UpdateUsability();
                UpdateHotkey();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[EffectButton.Update] {ex.Message}");
            }
        }

        /// <summary>效果中始终可用（可取消）；否则看基础条件。</summary>
        protected override bool ShouldBeUsable
            => _inEffect || (!_inCooldown && _config.CanUse() && (!HasLimitedUses || _usesLeft > 0));
    }
}