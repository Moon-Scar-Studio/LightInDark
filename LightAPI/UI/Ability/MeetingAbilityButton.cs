using System;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 会议右下角按钮：与普通技能按钮同款外观（克隆 HudManager.AbilityButton 模板），
    /// 但只在整个会议期间显示（会议开始创建、会议结束销毁）。
    /// 默认位置为会议界面右下角，可用 <see cref="MeetingAbilityButton"/> 的 Position 调整。
    ///
    /// 职业用法（OnActivated 中）：
    /// <code>
    /// MeetingAbilityButton.Create(role, new RoleButtonConfig()
    ///     .SetLabelKey("Button.Xxx.meeting")
    ///     .SetOnClickSFX("./Resources/SFX/Click.mp3"),
    ///     () => DoInMeeting());
    /// </code>
    /// </summary>
    public class MeetingAbilityButton : RoleButtonBase
    {
        private ActionButton _actionButton;
        private PassiveButton _passiveButton;
        private bool _meetingActive;

        /// <summary>按钮在会议界面中的位置（默认右下角，相对 MeetingHud 根节点）。</summary>
        public Vector3 Position { get; set; } = new Vector3(5.1f, -2.2f, -1.5f);

        /// <summary>按钮旋转（默认 0）。</summary>
        public Quaternion Rotation { get; set; } = Quaternion.identity;

        private MeetingAbilityButton(Role role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

        /// <summary>创建会议右下角按钮并注册到管理器。</summary>
        public static MeetingAbilityButton Create(Role role, RoleButtonConfig config, Action onClick)
        {
            try
            {
                var button = new MeetingAbilityButton(role, role.MyPlayer, config, onClick);
                RoleButtonManager.Register(button);
                return button;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MeetingAbilityButton.Create]", ex);
                return null;
            }
        }

        // ---- 会议生命周期（由 RoleButtonManager 的 MeetingHud patch 驱动）----

        internal void OnMeetingStart(MeetingHud meeting)
        {
            try
            {
                _meetingActive = true;
                if (meeting == null) return;
                if (PlayerControl.LocalPlayer?.Data?.IsDead == true) return;
                if (_gameObject != null) return; // 已创建

                var template = HudManager.Instance?.AbilityButton;
                if (template == null) return;
                if (template.transform.parent == null) return;

                _gameObject = Object.Instantiate(template.gameObject, meeting.transform);
                _gameObject.name = $"MeetingAbility_{_config.ResolvedLabel}";
                _gameObject.transform.localPosition = Position;
                _gameObject.transform.localRotation = Rotation;

                _actionButton = _gameObject.GetComponent<ActionButton>();
                _passiveButton = _gameObject.GetComponent<PassiveButton>();

                // 克隆材质，避免与原版按钮互相覆盖冷却进度
                if (_actionButton?.graphic != null && _actionButton.graphic.material != null)
                    _actionButton.graphic.material = new Material(_actionButton.graphic.material);

                ApplyMeetingConfig();

                if (_passiveButton != null)
                {
                    _passiveButton.OnClick = new Button.ButtonClickedEvent();
                    _passiveButton.OnMouseOver = new UnityEvent();
                    _passiveButton.OnMouseOut = new UnityEvent();
                    _passiveButton.OnClick.AddListener((UnityAction)HandleClick);
                }

                if (HasLimitedUses) _actionButton?.SetUsesRemaining(_usesLeft);
                else _actionButton?.SetInfiniteUses();

                _gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MeetingAbilityButton] OnMeetingStart", ex);
            }
        }

        internal void OnMeetingEnd()
        {
            _meetingActive = false;
            DestroyUI();
        }

        private void ApplyMeetingConfig()
        {
            if (_actionButton == null) return;
            if (_config.Icon != null) _actionButton.graphic.sprite = _config.Icon;
            string label = _config.ResolvedLabel;
            if (!string.IsNullOrEmpty(label)) _actionButton.OverrideText(label);
            if (_config.Cooldown > 0f)
            {
                _actionButton.SetCoolDown(0f, _config.Cooldown);
                if (_actionButton.cooldownTimerText != null)
                    _actionButton.cooldownTimerText.gameObject.SetActive(false);
            }
        }

        // ---- RoleButtonBase 接口 ----

        /// <summary>会议按钮无常驻 UI（由 OnMeetingStart 创建），覆写为空。</summary>
        protected override void CreateUI()
        {
            // 无操作：会议按钮在 MeetingHud.Start 后由 OnMeetingStart 创建
        }

        protected override void DestroyUI()
        {
            if (_gameObject != null)
            {
                Object.Destroy(_gameObject);
                _gameObject = null;
            }
            _actionButton = null;
            _passiveButton = null;
        }

        /// <summary>会议期间可见；非会议期间隐藏。</summary>
        protected override bool ShouldShow
            => _meetingActive && _gameObject != null && _player.IsLocal && !_player.IsDead
                && _config.CanShow();
    }
}