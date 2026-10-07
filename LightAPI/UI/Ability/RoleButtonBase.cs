using System;
using AmongUs.GameOptions;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using TMPro;
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
        protected readonly RuntimeRoleTemplate _role;
        protected readonly Player _player;
        protected readonly RoleButtonConfig _config;
        protected Action _onClick;

        protected GameObject _gameObject;
        protected float _cooldownTimer;
        protected bool _inCooldown;
        protected bool _hudActive = true;
        protected int _usesLeft;

        private const float MouseClickRadius = 150f;
        private const float FlashInterval = 0.9f;

        private SpriteRenderer _flashRenderer;
        private float _flashAlpha = -1f;
        private float _nextFlashAt;

        private SpriteRenderer _brokenRenderer;
        private bool _broken;

        private GameObject _usesIcon;
        private TextMeshPro _usesIconText;
        protected RoleButtonBase(RuntimeRoleTemplate role, Player player, RoleButtonConfig config, Action onClick)
        {
            _role = role;
            _player = player;
            _config = config ?? new RoleButtonConfig();
            _onClick = onClick;
            _usesLeft = _config.MaxUses;
        }

        /// <summary>所属职业运行时实例。</summary>
        public RuntimeRoleTemplate Role => _role;
        public Predicate<float> CanRunCooldown => _config.CanRunCooldown;
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
                KillTextTranslator();     // ★ 必须在子类设完 Label 之前/之后都能生效：先干掉原版翻译器
                ApplyLabelType();
                if (_config.Cooldown > 0f) StartCooldown();

                // ★ 诊断：按钮系统以前**只在出错时**打日志 → 出问题时完全没线索（AGENTS §4.8）。
                //   这里把"建成了没有 / 建在哪 / 冷却与次数"打出来：
                //   · `_gameObject == null` 说明 CreateUI 悄悄没建出东西 → 按钮永远不出现，这是最难查的一种。
                string code = _role != null ? _role.CodeName : "?";
                if (_gameObject == null)
                {
                    LightLogger.LogWarning($"[RoleButton] {GetType().Name}({code}) 创建后 **没有 GameObject** —— " +
                                           "CreateUI 没建出东西，按钮不会出现在 HUD 上");
                }
                else
                {
                    LightLogger.Log($"[RoleButton] {GetType().Name}({code}) 已创建：" +
                                    $"pos={_gameObject.transform.localPosition} active={_gameObject.activeSelf} " +
                                    $"冷却={_config.Cooldown}s 次数={(_config.MaxUses > 0 ? _config.MaxUses.ToString() : "∞")} " +
                                    $"标签={_config.Label}");
                }
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
                // ★ 冷却推进条件（两边的修复**合并保留**）：
                //   ① `ShouldTickCooldown()` —— 本轮的**默认策略**（对齐 Nebula 的
                //      `TimerImpl.SetAsAbilityCoolDown` / 老版 `Helpers.ProceedTimer`）：
                //      通风管 / 会议 / 放逐 / 开场 / 非白名单小游戏里 CD 不推进 ✓
                //   ② `_config.CanRunCooldown` —— 对面（bb82d8d）加的**按钮级**可配谓词
                //      （参数 = 当前剩余秒数）✓ 为 null 时不干预 ✓
                //   两个都通过才推进 → 谁的功能都没丢 ✓
                if (_inCooldown
                    && ShouldTickCooldown()
                    && (_config.CanRunCooldown?.Invoke(_cooldownTimer) ?? true))
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
                UpdateCooldownDisplay();
                UpdateHotkey();
                UpdateSubHotkey();
                UpdateMouseClick();
                UpdateFlash();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonBase.Update] {ex.Message}");
            }
        }

        /// <summary>
        /// **冷却是否应当推进** —— 对齐 Nebula 的谓词（新版 `TimerImpl.SetAsAbilityCoolDown()` /
        /// 老版 `Nebula\Helpers.cs:35-68` 的 `ProceedTimer`，后者第 46 行就是 `if (LocalPlayer.inVent) return false;`）。
        ///
        /// 依据：原版 `PlayerControl.CanMove`（19.0 `PlayerControl.cs:23-27`）本身就要求
        /// `<c>!inVent &amp;&amp; !Minigame &amp;&amp; !MeetingHud &amp;&amp; !ExileController &amp;&amp; !IntroCutscene</c>` 等等 ——
        /// 这些场合玩家**根本用不了技能**，所以 CD 也不该前进 ✓
        /// （用户 2026-10-06 报的「管道里技能能走 CD」就是这条谓词缺失的直接后果 ✓）
        ///
        /// ⚠️ 小游戏做了**白名单**：做任务时（`MyNormTask`）以及开关/门卡/生命体征小游戏里
        ///    Nebula 是**允许继续走 CD** 的 ✓（否则做个任务回来 CD 白等 ✗）—— 这里照抄同一份白名单 ✓
        /// ⚠️ 想改某个职业的行为，覆写本方法即可 ✓
        /// </summary>
        protected virtual bool ShouldTickCooldown()
        {
            try
            {
                var control = _player?.Control;
                if (control == null) control = PlayerControl.LocalPlayer;
                if (control == null) return true;                 // 拿不到就当正常（宁可正常走 CD，也别卡死）

                var data = control.Data;
                if (data == null) return false;                   // 数据没就绪：先别走
                if (data.IsDead || data.Disconnected) return false;

                // —— 用不了技能的场合（照抄 PlayerControl.CanMove 的那几条）——
                if (control.inVent) return false;                  // ★ 通风管（用户报的那条）
                if (control.shapeshifting) return false;
                if (MeetingHud.Instance != null) return false;      // 会议
                if (ExileController.Instance != null) return false; // 放逐动画
                if (IntroCutscene.Instance != null) return false;   // 开场动画
                if (PlayerCustomizationMenu.Instance != null) return false;

                var hud = HudManager.Instance;
                if (hud != null && hud.IsIntroDisplayed) return false;

                // —— 小游戏：白名单内仍然推进（与 Nebula 一致）——
                var minigame = Minigame.Instance;
                if (minigame != null && !IsCooldownAllowedMinigame(minigame)) return false;

                return true;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButtonBase.ShouldTickCooldown] {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// 冷却在小游戏里是否照常推进（Nebula 的白名单：做任务 + 开关 + 门卡 + 生命体征）。
        /// ⚠️ 用 `TryCast`（AGENTS §4.2.0：IL2CPP 下 `is` 判断会静默为 false ✗）
        /// </summary>
        private static bool IsCooldownAllowedMinigame(Minigame minigame)
        {
            try
            {
                if (minigame == null) return false;
                if (minigame.MyNormTask != null) return true;                       // 普通任务
                if (minigame.TryCast<SwitchMinigame>() != null) return true;
                if (minigame.TryCast<IDoorMinigame>() != null) return true;
                if (minigame.TryCast<VitalsMinigame>() != null) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>冷却归零时的处理（子类可覆写，默认播放冷却完成音效）。</summary>
        protected virtual void OnCooldownFinished()
        {
            PlayCooldownReadySFX();
            // 清掉残留的进度遮罩与倒计时数字
            var action = Button;
            if (action != null)
            {
                try
                {
                    action.SetCooldownFill(0f);
                    if (action.cooldownTimerText != null)
                        action.cooldownTimerText.gameObject.SetActive(false);
                }
                catch { }
            }
        }

        /// <summary>
        /// 克隆体上的 ActionButton（无则为 null）。
        ///
        /// ⚠️ **必须缓存**：这个属性每帧被 `UpdateUsability` / `UpdateCooldownDisplay` 读好几次，
        ///    原来每次都 `GetComponent<ActionButton>()`（IL2CPP 下是跨托管/原生边界的查找，很贵）。
        ///    Nebula 的按钮实现也是缓存的（`ModAbilityButtonImpl` / `CustomButton`）。
        /// ⚠️ 缓存失效时（对象被销毁成假 null，见 AGENTS §4.6.1）这里会用 Unity 的 `!=` 判出来并重新取 ✓
        /// </summary>
        protected ActionButton Button
        {
            get
            {
                if (_actionButton != null) return _actionButton;
                _actionButton = _gameObject != null ? _gameObject.GetComponent<ActionButton>() : null;
                return _actionButton;
            }
        }

        private ActionButton? _actionButton;

        /// <summary>上次处理点击的帧号（同一帧只处理一次）。</summary>
        private int _lastClickFrame = -1;

        /// <summary>
        /// 让**运行期新建**的渲染器继承克隆体的排序层级（AGENTS §4.3）。
        ///
        /// `GameObject.layer` 决定哪台相机渲染、`sortingLayerID/sortingOrder` 决定前后顺序；
        /// 新 `AddComponent&lt;SpriteRenderer&gt;()` 默认是"Default 层 + order 0"，
        /// 在别的相机/排序层下就可能被底板或图标盖住（表现：闪白/破损图标"看不见"，但逻辑在跑）。
        /// </summary>
        private void InheritSortingFromButton(SpriteRenderer sr, int orderBias)
        {
            try
            {
                if (sr == null || _gameObject == null) return;

                SpriteRenderer? reference = null;
                foreach (var r in _gameObject.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (r == null || ReferenceEquals(r, sr) || !r.enabled) continue;
                    reference = r;
                    break;
                }
                if (reference == null) return;

                sr.sortingLayerID = reference.sortingLayerID;
                sr.sortingOrder = reference.sortingOrder + orderBias;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButton] 继承排序层级失败: {ex.Message}");
            }
        }

        /// <summary>
        /// **干掉克隆体自带的 `TextTranslatorTMP`**（AGENTS §5.2 同类坑，本工程已经踩过 6 次）。
        ///
        /// 它会在第一次 `SetActive(true)` 时跑 `Start()` → `ResetText()`，按 `StringNames` 把文字
        /// **改回原版串**；之后换语言还会再刷一次。于是我们 `ApplyConfig` 设的 `Label` / `LabelKey`
        /// 全部失效（表现就是"按钮文字不对 / 一换语言就变回英文"）。
        /// Nebula 也是显式处理这个组件（`ModAbilityButtonImpl` 里 `enabled = false`、`CustomButton` 里 Destroy）。
        /// </summary>
        private void KillTextTranslator()
        {
            if (_gameObject == null) return;
            try
            {
                var count = 0;
                foreach (var t in _gameObject.GetComponentsInChildren<TextTranslatorTMP>(true))
                {
                    if (t == null) continue;
                    t.enabled = false;          // 先禁用：Start() 不会再跑
                    UnityEngine.Object.Destroy(t);  // 再销毁，双保险
                    count++;
                }
                if (count > 0) LightLogger.Log($"[RoleButton] {GetType().Name} 已清理 {count} 个 TextTranslatorTMP（防止文字被改回原版串）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[RoleButton] 清理 TextTranslatorTMP 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新冷却显示（进度遮罩 + 倒计时数字）。
        /// 子类可覆写以自定义 CD 表现（如持续效果期间改显效果时长）。
        /// </summary>
        protected virtual void UpdateCooldownDisplay()
        {
            if (!_inCooldown || _config.Cooldown <= 0f) return;
            var action = Button;
            if (action == null) return;
            try { action.SetCoolDown(_cooldownTimer, _config.Cooldown); }
            catch (Exception ex) { LightLogger.LogWarning($"[RoleButton] 冷却显示失败: {ex.Message}"); }
        }

        /// <summary>开始冷却。</summary>
        public void StartCooldown()
        {
            _cooldownTimer = _config.Cooldown;
            _inCooldown = _config.Cooldown > 0f;
            if (!_inCooldown) return;
            var action = Button;
            if (action?.cooldownTimerText != null)
                action.cooldownTimerText.gameObject.SetActive(true);
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
                // ★ 地图打开时不该显示（原版 HUD 按钮这时都被藏起来）。
                //   Nebula 的 HudGrid 也是这么做的：`content.ActiveFunc = () => obj.activeSelf && !AmongUsUtil.MapIsOpen`
                if (MapBehaviour.Instance != null && MapBehaviour.Instance.IsOpen) return false;

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
            => !_broken && _config.CanUse() && !_inCooldown && (!HasLimitedUses || _usesLeft > 0);

        protected void UpdateUsability()
        {
            if (_gameObject == null || !_gameObject.activeSelf) return;
            var action = Button;      // ★ 走缓存（原来每帧 GetComponent<ActionButton>()）
            if (action == null) return;
            if (ShouldBeUsable) action.SetEnabled();
            else action.SetDisabled();
        }

        /// <summary>
        /// 输入闸门：**小游戏 / 地图打开 / 会议中 / 聊天框聚焦**时都不该响应热键与鼠标点击。
        ///
        /// ⚠️ 原来热键只判 `activeSelf`（按钮还"显示着"就触发）→ 开会/看地图/做小游戏时按快捷键
        ///    照样把技能放出去。Nebula 用的是 `VirtualInput.KeyDownInGame` + `NebulaInput.SomeUiIsActive`。
        /// </summary>
        private static bool InputBlocked()
        {
            try
            {
                if (Minigame.Instance != null) return true;
                if (MeetingHud.Instance != null) return true;
                if (MapBehaviour.Instance != null && MapBehaviour.Instance.IsOpen) return true;

                var hud = HudManager.Instance;
                if (hud != null)
                {
                    var chat = hud.Chat;
                    if (chat != null && chat.freeChatField != null && chat.freeChatField.textArea != null
                        && chat.freeChatField.textArea.hasFocus) return true;
                }
            }
            catch { }
            return false;
        }

        protected void UpdateHotkey()
        {
            if (_config.Hotkey == KeyCode.None) return;
            if (_gameObject == null || !_gameObject.activeSelf) return;
            if (InputBlocked()) return;
            if (Input.GetKeyDown(_config.Hotkey))
                HandleClick();
        }

        protected void UpdateSubHotkey()
        {
            if (_config.SubHotkey == KeyCode.None || _config.SubAction == null) return;
            if (_gameObject == null || !_gameObject.activeSelf) return;
            if (InputBlocked()) return;
            if (Input.GetKeyDown(_config.SubHotkey))
                HandleSubClick();
        }

        protected void UpdateMouseClick()
        {
            if (!_config.UseByMouseClick) return;
            if (_gameObject == null || !_gameObject.activeSelf) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (InputBlocked()) return;

            var camera = Camera.main;
            if (camera == null) return;

            var buttonScreen = camera.WorldToScreenPoint(_gameObject.transform.position);
            var pointer = (Vector2)Input.mousePosition;
            if (Vector2.Distance(new Vector2(buttonScreen.x, buttonScreen.y), pointer) > MouseClickRadius) return;

            HandleClick();
        }

        protected virtual void HandleSubClick()
        {
            if (_broken || _inCooldown) return;
            if (!_config.CanUse()) return;
            if (HasLimitedUses && _usesLeft <= 0) return;

            PlayOnClickSFX();
            _config.SubAction?.Invoke();
        }

        private void ApplyLabelType()
        {
            if (_gameObject == null) return;
            var label = Button?.buttonLabelText;
            var material = ResolveLabelMaterial(_config.LabelType);
            if (label != null && material != null) label.SetSharedMaterial(material);
        }

        private static Material ResolveLabelMaterial(ButtonLabelType type)
        {
            switch (type)
            {
                case ButtonLabelType.Impostor:
                    return RoleManager.Instance?.GetRole(RoleTypes.Shapeshifter)?.Ability?.FontMaterial;
                case ButtonLabelType.Utility:
                    return ResolveUseButtonMaterial(ImageNames.PolusAdminButton);
                case ButtonLabelType.Crewmate:
                    return RoleManager.Instance?.GetRole(RoleTypes.Engineer)?.Ability?.FontMaterial;
                default:
                    return ResolveUseButtonMaterial(ImageNames.UseButton);
            }
        }

        private static Material ResolveUseButtonMaterial(ImageNames image)
        {
            try
            {
                // ⚠️ 别用 `HudManager.Instance?.UseButton?...`（AGENTS §4.6.1）：
                //   HudManager 是 Unity 对象，场景切换后被销毁成假 null，`?.` 挡不住、访问属性会抛 ✗
                var hud = HudManager.Instance;
                if (hud == null) return null;
                var useButton = hud.UseButton;
                if (useButton == null) return null;

                var settings = useButton.fastUseSettings;
                if (settings == null) return null;

                var raw = settings[image];
                if (raw == null) return null;
                return raw.FontMaterial;
            }
            catch { return null; }
        }

        public bool IsBroken => _broken;

        public void PlayFlash()
        {
            if (_gameObject == null) return;
            var icon = Button?.graphic;
            if (icon == null || icon.sprite == null) return;

            try
            {
                if (_flashRenderer == null)
                {
                    var flash = new GameObject("Flash");
                    flash.transform.SetParent(_gameObject.transform, false);
                    flash.layer = _gameObject.layer;
                    flash.transform.localPosition = new Vector3(0f, 0f, -1f);
                    _flashRenderer = flash.AddComponent<SpriteRenderer>();
                    InheritSortingFromButton(_flashRenderer, 1);   // ★ 否则可能被图标盖住（闪白看不见）

                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) _flashRenderer.material = new Material(shader);
                }

                _flashRenderer.sprite = icon.sprite;
                _flashRenderer.gameObject.SetActive(true);
                _flashRenderer.transform.localScale = Vector3.one;
                _flashRenderer.color = new UnityEngine.Color(1f, 1f, 1f, 1f);
                _flashAlpha = 1f;
            }
            catch { }
        }

        private void UpdateFlash()
        {
            if (_gameObject == null) return;

            if (_config.FlashWhile != null && IsVisible && _config.FlashWhile())
            {
                _nextFlashAt -= Time.deltaTime;
                if (_nextFlashAt <= 0f)
                {
                    _nextFlashAt = FlashInterval;
                    PlayFlash();
                }
            }
            else
            {
                _nextFlashAt = 0f;
            }

            if (_flashRenderer == null || _flashAlpha < 0f) return;

            _flashAlpha -= Time.deltaTime * 1.5f;
            if (_flashAlpha <= 0f)
            {
                _flashAlpha = -1f;
                _flashRenderer.gameObject.SetActive(false);
                return;
            }

            _flashRenderer.color = new UnityEngine.Color(1f, 1f, 1f, _flashAlpha * 0.85f);
            _flashRenderer.transform.localScale = Vector3.one * (2f - _flashAlpha);
        }

        public void Break()
        {
            if (_broken) return;
            _broken = true;

            try
            {
                if (_gameObject == null) return;

                var icon = Button?.graphic;
                if (icon != null) icon.enabled = false;

                if (_brokenRenderer == null)
                {
                    var broken = new GameObject("Broken");
                    broken.transform.SetParent(_gameObject.transform, false);
                    broken.layer = _gameObject.layer;
                    broken.transform.localPosition = Vector3.zero;
                    _brokenRenderer = broken.AddComponent<SpriteRenderer>();
                    InheritSortingFromButton(_brokenRenderer, 1);   // ★ 否则破损图标可能画在底板后面（看不见）
                }

                _brokenRenderer.sprite = _config.BrokenIcon != null
                    ? _config.BrokenIcon
                    : icon != null ? icon.sprite : null;
                _brokenRenderer.color = new UnityEngine.Color(0.32f, 0.32f, 0.32f, 0.85f);
                _brokenRenderer.gameObject.SetActive(true);
            }
            catch { }
        }

        public void ShowUsesIcon(string text)
        {
            try
            {
                if (_gameObject == null) return;

                if (_usesIcon == null)
                {
                    // ⚠️ 别用 `HudManager.Instance?.AbilityButton?...`（AGENTS §4.6.1，Unity 假 null）
                    var hud = HudManager.Instance;
                    var source = hud != null && hud.AbilityButton != null ? hud.AbilityButton.transform : null;
                    if (source == null || source.childCount <= 2)
                    {
                        // ⚠️ 原来是**空 catch + 静默 return**（补丁审查 #18）：次数图标建不出来时毫无痕迹
                        LightLogger.LogWarning($"[RoleButton] 次数图标跳过：原版 AbilityButton 结构不符（childCount={(source != null ? source.childCount : -1)}）");
                        return;
                    }

                    // ⚠️ `GetChild(2)` 是硬编码索引（审查 #18）：原版改结构就默默拿错东西。
                    //    这里**校验**拿到的模板里确实有 TMP**，没有就报警并放弃（不猜别的索引）。
                    var template = source.GetChild(2);
                    var templateTmp = template.GetComponentInChildren<TextMeshPro>(true);
                    if (templateTmp == null)
                    {
                        LightLogger.LogWarning("[RoleButton] 次数图标跳过：AbilityButton 第 3 个子物体里没有 TMP（原版结构可能变了）");
                        return;
                    }

                    _usesIcon = Object.Instantiate(template.gameObject, _gameObject.transform);
                    _usesIcon.name = "UsesIcon";
                    _usesIcon.transform.localScale = template.localScale;
                    _usesIcon.transform.localPosition = template.localPosition * 1.2f;

                    _usesIconText = _usesIcon.transform.childCount > 0
                        ? _usesIcon.transform.GetChild(0).GetComponent<TextMeshPro>()
                        : null;
                }

                _usesIcon.SetActive(true);
                UpdateUsesIcon(text);
            }
            catch { }
        }

        public void UpdateUsesIcon(string text)
        {
            if (_usesIconText != null) _usesIconText.SetText(text);
        }

        public void HideUsesIcon()
        {
            if (_usesIcon != null) _usesIcon.SetActive(false);
        }

        /// <summary>点击入口（子类可覆写；默认：可用性重算 → SFX → 回调 → 扣次数 → 冷却）。</summary>
        protected virtual void HandleClick()
        {
            try
            {
                // ⚠️⚠️ **每次点击都重算可用性**。
                //   原版 `PassiveButton.ReceiveClickDown` **不检查** `ActionButton.CanInteract()`，
                //   而我们替换了 OnClick —— 于是 `SetDisabled()` 只是改了颜色，
                //   "灰掉的 / 冷却中的 / 已 Break 的"按钮**照样能点**（用户看到的就是"点了还有效果"）。
                //   Nebula 的 `DoClick()` 同样每次重算（它注释里写着"发火时机与可见性更新时机会有偏差，所以这里重算"）。
                if (!ShouldBeUsable) return;

                var action = Button;
                if (action != null && !action.CanInteract()) return;

                // ⚠️ 同一帧去重：原版 PassiveButton 的 OnClick 与我们自己的 UpdateMouseClick（鼠标半径判定）
                //    **会在同一帧各触发一次** → 技能连放两次 / 次数扣两次。这里按帧号挡掉第二次。
                if (_lastClickFrame == Time.frameCount) return;
                _lastClickFrame = Time.frameCount;

                PlayOnClickSFX();
                _onClick?.Invoke();

                if (HasLimitedUses)
                {
                    _usesLeft--;
                    if (action != null) action.SetUsesRemaining(_usesLeft);
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
