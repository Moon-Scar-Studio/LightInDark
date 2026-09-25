using System;
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
    /// 普通按钮：克隆原版 AbilityButton/KillButton 模板，点击触发一次技能并进入冷却。
    /// 职业用法：
    /// <code>
    /// AbilityButton.Create(role, new RoleButtonConfig()
    ///     .SetLabelKey("Button.Caller.label")
    ///     .SetCooldown(Cooldown)
    ///     .SetOnClickSFX("./Resources/SFX/Click.mp3"),
    ///     () => DoSomething());
    /// </code>
    /// </summary>
    public class AbilityButton : RoleButtonBase
    {
        protected ActionButton _actionButton;
        protected PassiveButton _passiveButton;

        protected AbilityButton(Role role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

        /// <summary>创建普通按钮并注册到管理器。</summary>
        public static AbilityButton Create(Role role, RoleButtonConfig config, Action onClick)
        {
            try
            {
                var button = new AbilityButton(role, role.MyPlayer, config, onClick);
                RoleButtonManager.Register(button);
                return button;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[AbilityButton.Create]", ex);
                return null;
            }
        }

        protected override void CreateUI()
        {
            ActionButton template = _config.IsKillButton
                ? HudManager.Instance.KillButton
                : HudManager.Instance.AbilityButton;
            if (template == null) throw new Exception("按钮模板为 null");
            if (template.transform.parent == null) throw new Exception("按钮模板 parent 为 null");

            _gameObject = Object.Instantiate(template.gameObject, template.transform.parent);
            _gameObject.name = $"AbilityButton_{_config.ResolvedLabel}";

            _actionButton = _gameObject.GetComponent<ActionButton>();
            if (_actionButton == null) throw new Exception("ActionButton 组件为 null");
            _passiveButton = _gameObject.GetComponent<PassiveButton>();
            if (_passiveButton == null) throw new Exception("PassiveButton 组件为 null");

            // 关键：克隆材质实例，避免与原版按钮共享材质导致冷却进度互相覆盖
            if (_actionButton.graphic != null && _actionButton.graphic.material != null)
                _actionButton.graphic.material = new Material(_actionButton.graphic.material);

            ApplyConfig();

            // 替换点击事件
            _passiveButton.OnClick = new Button.ButtonClickedEvent();
            _passiveButton.OnMouseOver = new UnityEvent();
            _passiveButton.OnMouseOut = new UnityEvent();
            _passiveButton.OnClick.AddListener((UnityAction)HandleClick);

            if (HasLimitedUses) _actionButton.SetUsesRemaining(_usesLeft);
            else _actionButton.SetInfiniteUses();

            _gameObject.SetActive(false);
        }

        protected void ApplyConfig()
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

        /// <summary>是否可用的基础判断（供子类扩展）。</summary>
        protected bool CanUseNow
            => !_inCooldown && _config.CanUse() && (!HasLimitedUses || _usesLeft > 0);
    }
}
