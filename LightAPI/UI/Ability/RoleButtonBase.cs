using System;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 按钮内部抽象基类。四种公开按钮（普通/持续/会议目标/会议右下角）共享的骨架：
    /// 绑定职业与玩家、冷却计时、使用次数、可见性/可用性、以及 SFX 调用点。
    ///
    /// SFX 调用点（播放实现 TODO，见 <see cref="SfxManager"/>）：
    ///  - <see cref="PlayOnClickSFX"/>：点击时调用
    ///  - <see cref="PlayCooldownReadySFX"/>：冷却归零时调用
    /// </summary>
    public abstract class RoleButtonBase : ILifespan, IGameOperator, IReleasable
    {
        protected readonly Role _role;
        protected readonly Player _player;
        protected readonly RoleButtonConfig _config;
        protected Action _onClick;

        protected GameObject _gameObject;
        protected float _cooldownTimer;
        protected bool _inCooldown;
        protected bool _hudActive = true;
        protected int _usesLeft;

        protected RoleButtonBase(Role role, Player player, RoleButtonConfig config, Action onClick)
        {
            _role = role;
            _player = player;
            _config = config ?? new RoleButtonConfig();
            _onClick = onClick;
            _usesLeft = _config.MaxUses;
        }

        /// <summary>所属职业。</summary>
        public Role Role => _role;

        /// <summary>绑定的玩家。</summary>
        public Player MyPlayer => _player;

        /// <summary>是否属于本地玩家。</summary>
        public bool AmOwner => _player?.AmOwner ?? false;

        /// <summary>按钮 GameObject 是否已销毁。</summary>
        public bool IsDeadObject => _gameObject == null;

        /// <summary>是否处于冷却。</summary>
        public bool IsInCooldown => _inCooldown;

        /// <summary>剩余使用次数。</summary>
        public int UsesLeft => _usesLeft;

        /// <summary>是否限制使用次数。</summary>
        public bool HasLimitedUses => _config.MaxUses > 0;

        /// <summary>当前是否可见。</summary>
        public bool IsVisible => _gameObject != null && _gameObject.activeSelf;

        /// <summary>点击回调（可整体替换）。</summary>
        public Action OnClick { set => _onClick = value; }

        // =====================================================================
        // SFX 调用点（TODO：播放实现见 SfxManager）
        // =====================================================================

        /// <summary>点击音效调用点。</summary>
        protected void PlayOnClickSFX()
        {
            try { SfxManager.Play(_config.OnClickSFX); }
            catch (Exception ex) { LightLogger.LogWarning($"[RoleButton] 点击音效失败: {ex.Message}"); }
        }

        /// <summary>冷却完成音效调用点。</summary>
        protected void PlayCooldownReadySFX()
        {
            try { SfxManager.Play(_config.CooldownReadySFX); }
            catch (Exception ex) { LightLogger.LogWarning($"[RoleButton] 冷却音效失败: {ex.Message}"); }
        }

        // =====================================================================
        // 生命周期（子类实现 UI）
        // =====================================================================

        /// <summary>子类覆写：创建 UI 并绑定点击。</summary>
        protected abstract void CreateUI();

        /// <summary>子类覆写：销毁 UI。</summary>
        protected virtual void DestroyUI()
        {
            if (_gameObject != null)
            {
                Object.Destroy(_gameObject);
                _gameObject = null;
            }
        }

        /// <summary>创建按钮（由 RoleButtonManager 调用）。</summary>
        internal void Create()
        {
            try
            {
                // 预热点击/冷却音效：避免首次播放的异步解码延迟导致第一次点击无声
                SfxManager.Warmup(_config.OnClickSFX);
                SfxManager.Warmup(_config.CooldownReadySFX);

                CreateUI();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[RoleButtonBase.Create]", ex);
            }
        }

        /// <summary>每帧更新（由 RoleButtonManager 驱动）。</summary>
        public virtual void Update()
        {
            if (IsDeadObject) return;
            try
            {
                if (_inCooldown)
                {
                    _cooldownTimer -= Time.deltaTime;
                    if (_cooldownTimer <= 0f)
                    {
                        _cooldownTimer = 0f;
                        _inCooldown = false;
                        OnCooldownFinished();
                    }
                }
                UpdateVisibility();
                UpdateUsability();
                UpdateHotkey();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonBase.Update] {ex.Message}");
            }
        }

        /// <summary>冷却归零时的处理（子类可覆写，默认播放冷却完成音效）。</summary>
        protected virtual void OnCooldownFinished()
        {
            PlayCooldownReadySFX();
        }

        /// <summary>开始冷却。</summary>
        public void StartCooldown()
        {
            _cooldownTimer = _config.Cooldown;
            _inCooldown = _config.Cooldown > 0f;
        }

        /// <summary>立即结束冷却。</summary>
        public void StopCooldown()
        {
            _cooldownTimer = 0f;
            _inCooldown = false;
        }

        /// <summary>全局 HUD 激活状态（SetHudActive 调用）。</summary>
        public void SetHudActive(bool active)
        {
            _hudActive = active;
        }

        /// <summary>直接设置可见性。</summary>
        public void SetVisible(bool visible)
        {
            if (_gameObject != null) _gameObject.SetActive(visible);
        }

        /// <summary>计算应否显示（子类可覆写）。</summary>
        protected virtual bool ShouldShow
        {
            get
            {
                if (_config.AlwaysShow) return _hudActive && _config.CanShow();
                return _hudActive && _player.IsLocal && !_player.IsDead
                    && MeetingHud.Instance == null && _config.CanShow();
            }
        }

        protected void UpdateVisibility()
        {
            if (_gameObject == null) return;
            bool shouldShow = ShouldShow;
            if (_gameObject.activeSelf != shouldShow)
                _gameObject.SetActive(shouldShow);
        }

        /// <summary>计算应否可用（子类可覆写）。</summary>
        protected virtual bool ShouldBeUsable
            => _config.CanUse() && !_inCooldown && (!HasLimitedUses || _usesLeft > 0);

        protected void UpdateUsability()
        {
            if (_gameObject == null || !_gameObject.activeSelf) return;
            var action = _gameObject.GetComponent<ActionButton>();
            if (action == null) return;
            if (ShouldBeUsable) action.SetEnabled();
            else action.SetDisabled();
        }

        protected void UpdateHotkey()
        {
            if (_config.Hotkey == KeyCode.None) return;
            if (_gameObject == null || !_gameObject.activeSelf) return;
            if (Input.GetKeyDown(_config.Hotkey))
                HandleClick();
        }

        /// <summary>点击入口（子类可覆写；默认：SFX → 回调 → 扣次数 → 冷却）。</summary>
        protected virtual void HandleClick()
        {
            try
            {
                if (_inCooldown) return;
                if (!_config.CanUse()) return;
                if (HasLimitedUses && _usesLeft <= 0) return;

                PlayOnClickSFX();
                _onClick?.Invoke();

                if (HasLimitedUses)
                {
                    _usesLeft--;
                    if (_gameObject != null)
                    {
                        var action = _gameObject.GetComponent<ActionButton>();
                        if (action != null) action.SetUsesRemaining(_usesLeft);
                    }
                }
                if (_config.Cooldown > 0f) StartCooldown();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonBase.HandleClick] {ex.Message}");
            }
        }

        // =====================================================================
        // 释放
        // =====================================================================

        public void Release()
        {
            try
            {
                DestroyUI();
                _onClick = null;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonBase.Release] {ex.Message}");
            }
        }

        void IGameOperator.OnReleased() { }
    }
}
