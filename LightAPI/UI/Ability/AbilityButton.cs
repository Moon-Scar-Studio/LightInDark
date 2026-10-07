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

        protected AbilityButton(RuntimeRoleTemplate role, Player player, RoleButtonConfig config, Action onClick)
            : base(role, player, config, onClick) { }

        /// <summary>创建普通按钮并注册到管理器。</summary>
        public static AbilityButton Create(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
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

            if (_actionButton.usesRemainingSprite != null && _actionButton.usesRemainingText != null)
            {
                if (HasLimitedUses) _actionButton.SetUsesRemaining(_usesLeft);
                else _actionButton.SetInfiniteUses();
            }

            AttachToGrid();

            _gameObject.SetActive(false);
        }

        private void AttachToGrid()
        {
            if (_gameObject == null) return;
            var grid = HudGrid.Ensure();
            var content = new HudContent(_gameObject);
            content.SetPriority(_config.Priority);
            content.MarkAsKillButtonContent(_config.ArrangedAsKillButton);
            content.IsStaticContent = _config.AlwaysShow;
            content.OccupiesLine = _config.OccupiesLine;
            content.ShouldBeInLastLine = _config.ShouldBeInLastLine;
            content.ActiveFunc = () => _gameObject != null && _gameObject.activeSelf;
            if (grid != null) grid.RegisterContent(content, _config.IsLeftSide);
        }

        protected void ApplyConfig()
        {
            if (_actionButton == null) return;
            ApplyIcon(_config.Icon);
            string label = _config.ResolvedLabel;
            if (!string.IsNullOrEmpty(label)) _actionButton.OverrideText(label);
            if (_config.Cooldown > 0f)
            {
                // 初始为满冷却遮罩（数字+进度由 UpdateCooldownDisplay 每帧驱动）
                _actionButton.SetCoolDown(_config.Cooldown, _config.Cooldown);
            }
        }

        /// <summary>
        /// 换图标 + **重算冷却遮罩的 UV 包围盒**（两件事必须成对做）。
        ///
        /// ⚠️⚠️ 这是用户 2026-10-06 报的「装填动画错误」的**根因**：
        ///   原版冷却遮罩不是一个矩形贴图，而是往 <c>graphic.material</c> 写**两个** shader 属性算出来的：
        ///   <code>
        ///     ActionButton.SetCooldownFill(p)  → material.SetFloat("_Percent", p)            // ActionButton.cs:143-146
        ///     CooldownHelpers.SetCooldownNormalizedUvs(sr)
        ///         → material.SetVector("_NormalizedUvs", 该 sprite 的 uv 包围盒)               // CooldownHelpers.cs:8-39
        ///   </code>
        ///   遮罩是"按**当前 sprite 的 UV 包围盒**"算的 ✓ → **换图就必须重算** ✓
        ///   原版每处换图都跟着调：`AbilityButton.SetFromSettings`(:51-52/:60-61)、
        ///   `UseButton.SetFromSettings`(:85-86)、`KillButton.ResetKillButton`(:69/84/92) ✓
        ///
        ///   而本工程原来只写 `graphic.sprite = _config.Icon`、**从不重算 `_NormalizedUvs`**
        ///   （全工程 grep 零命中）✗ → shader 拿"新图标的真实 uv"去套"旧图标的 uv 包围盒"
        ///   → 遮罩算到按钮外面 → 看起来就是"装填动画完全是坏的" ✗
        /// </summary>
        protected void ApplyIcon(UnityEngine.Sprite icon)
        {
            if (_actionButton == null || icon == null) return;
            try
            {
                _actionButton.graphic.sprite = icon;
                _actionButton.graphic.SetCooldownNormalizedUvs();   // ★ 换图后必须重算（见上）
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[AbilityButton.ApplyIcon] 换图标失败: {ex.Message}");
            }
        }

        /// <summary>是否可用的基础判断（供子类扩展）。</summary>
        protected bool CanUseNow
            => !_inCooldown && _config.CanUse() && (!HasLimitedUses || _usesLeft > 0);
    }
}
