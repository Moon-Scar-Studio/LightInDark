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
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// ★ 2026-10-25 新写法（用户："写法过于臃肿，开发者得一个个来回切换文件…改成像 NOS 的那样"）
    ///
    /// 【为什么改成命名可选参数】
    ///   原来只有 `Create(role, new RoleButtonConfig { … }, onClick)` ✗ ——
    ///   想知道能填什么必须**打开另一个文件**看 `RoleButtonConfig` ✗
    ///   NOS 的做法是**一个大方法的命名可选参数**（`NebulaAPI\ModuleFactory.cs:33`）：
    ///   <code>
    ///   AbilityButton(lifespan, player, isArrangedAsKillButton, isLeftSideButton, input, inputHelp,
    ///                 cooldown, label, image, availability, visibility, asGhostButton)
    ///   </code>
    ///   → 在调用处敲 `Create(`，IDE 直接把全部可填项列出来 ✓ 不用切文件 ✓
    ///
    /// 【用法】
    /// <code>
    /// // **必选 5 项**（照 NOS：label / image / cooldown / input；我们再加 onClick ✓）
    /// _button = AbilityButtonFactory.Create(this,
    ///     label: "击杀",                        // ① 文本（也可 label: "" + labelKey: "role.x.ability.kill"）
    ///     icon: null,                           // ② 图标（null = 用模板原图标，必须显式写 ✓）
    ///     cooldown: Role.KillCooldown,          // ③ 冷却（秒）
    ///     hotkey: KeyCode.Q,                    // ④ 热键
    ///     onClick: TryKill,                     // ⑤ 点击回调
    ///     // ↓ 以下全部可选
    ///     isKillButton: true,
    ///     arrangedAsKillButton: true,
    ///     labelType: ButtonLabelType.Impostor,
    ///     canShow: () => _killsLeft > 0,
    ///     canUse: () => _killsLeft > 0 &amp;&amp; _tracker?.CurrentTarget != null);
    /// </code>
    /// ⚠️ 旧的 `Create(role, new RoleButtonConfig { … }, onClick)` **保留** ✓（兼容，不用动老代码 ✓）
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class AbilityButtonFactory
    {
    // ─────────────────────────────────────────────────────────────────────
    //  新写法：必选在前 + 可选在后，全部走命名参数（照 NOS）✓
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 创建普通技能按钮（点击一次进冷却）—— **命名参数版** ✓
    /// <para>**必选 5 项**（照 NOS 的完整重载 ✓）：`label` / `icon` / `cooldown` / `hotkey` / `onClick`</para>
    /// <para>**其余全部可选**：不填就走 `RoleButtonConfig` 的安全默认值 ✓（旧写法 `Create(role, config, onClick)` 仍保留 ✓）</para>
    /// </summary>
    /// <param name="role">所属职业的运行时实例（职业里直接传 `this` ✓）；按钮会注册进它的按钮列表，随 HUD 显示/隐藏、随换局回收 ✓</param>
    /// <param name="label">按钮上的**文字**（直接文本）；想用语言键就传 `""` 并填 <paramref name="labelKey"/> ✓</param>
    /// <param name="icon">按钮**图标**；`null` = 用模板原图标（**必须显式传** ✓，跟 NOS 的 `Image? image` 一样要求你决定 ✓）</param>
    /// <param name="cooldown">**冷却秒数**（`0` = 不冷却 ✓）；从"用掉技能"那刻开始走 ✓</param>
    /// <param name="hotkey">**热键**（`KeyCode.None` = 没热键 ✓）；受输入闸门限制 —— 小游戏 / 地图打开 / 会议 / 聊天框聚焦时不响应 ✓</param>
    /// <param name="onClick">**点击回调**（必选：没回调的按钮没意义 ✓）；触发前冷却与可用性已经判过 ✓</param>
    /// <param name="labelKey">语言键（如 `role.x.ability.kill`）；<paramref name="label"/> 非空时优先用 label ✓</param>
    /// <param name="brokenIcon">次数用完后显示的"破损"图标；`null` = 用模板的破损图 ✓</param>
    /// <param name="startOnCooldown">创建时是否**立刻进冷却**（默认 `true`，与开局一致 ✓）</param>
    /// <param name="maxUses">**最大使用次数**（`0` = 不限 ✓）；用完后按钮变破损并不可点 ✓</param>
    /// <param name="subHotkey">副热键（`KeyCode.None` = 无 ✓），触发 <paramref name="subAction"/> ✓</param>
    /// <param name="subAction">副热键的回调（配合 <paramref name="subHotkey"/> ✓）</param>
    /// <param name="canShow">**是否显示**（false = 按钮隐藏 ✓）；不传 = 默认"活着就显示"✓</param>
    /// <param name="canUse">**是否可用**（false = 灰掉、点了没反应 ✓）；不传 = 默认"能移动就可用"✓</param>
    /// <param name="flashWhile">何时让按钮**闪烁**（不传 = 不闪 ✓）</param>
    /// <param name="isKillButton">是否克隆**击杀按钮**模板（默认克隆技能按钮模板 ✓）</param>
    /// <param name="arrangedAsKillButton">是否**按击杀按钮的位置/大小**排布（默认 false ✓）</param>
    /// <param name="labelType">文字样式：`Standard` / `Impostor` / `Utility` / `Crewmate`（影响字号/描边 ✓）</param>
    /// <param name="clickSfx">点击音效的 mp3 相对路径（`""` = 不播 ✓）</param>
    /// <param name="readySfx">冷却就绪音效的 mp3 相对路径（`""` = 不播 ✓）</param>
    /// <param name="leftSide">是否放到屏幕**左侧**按钮组（默认右侧 ✓）</param>
    /// <param name="priority">同侧按钮的**排序优先级**（数值大的更靠前 ✓）</param>
    /// <param name="occupiesLine">是否**独占一行**（默认 false ✓）</param>
    /// <param name="lastLine">是否强制排在**最后一行**（默认 false ✓）</param>
    /// <param name="alwaysShow">是否无视 HUD 隐藏状态**常驻显示**（默认 false ✓）</param>
    /// <param name="useByMouseClick">是否也允许**鼠标点击**触发（默认 false ✓）</param>
    /// <param name="allowDuringMeeting">**会议中**是否也允许触发（默认 false ✓）</param>
    /// <param name="meetingStarted">会议**开始**时的回调（不传 = 无 ✓）</param>
    /// <param name="meetingEnded">会议**结束**时的回调（不传 = 无 ✓）</param>
    /// <param name="onUpdate">**每帧回调**，参数是按钮本身（做自定义动画/提示用 ✓）</param>
    /// <param name="onReleased">按钮**被回收**时的回调（不传 = 无 ✓）</param>
    /// <param name="onBroken">按钮**破损**（次数耗尽）时的回调（不传 = 无 ✓）</param>
    public static AbilityButton Create(
        RuntimeRoleTemplate role,
        string label,
        Sprite icon,
        float cooldown,
        KeyCode hotkey,
        Action onClick,
        string labelKey = "",
        Sprite brokenIcon = null,
        bool startOnCooldown = true,
        int maxUses = 0,
        KeyCode subHotkey = KeyCode.None,
        Action subAction = null,
        Func<bool> canShow = null,
        Func<bool> canUse = null,
        Func<bool> flashWhile = null,
        bool isKillButton = false,
        bool arrangedAsKillButton = false,
        ButtonLabelType labelType = ButtonLabelType.Standard,
        string clickSfx = "",
        string readySfx = "",
        bool leftSide = false,
        int priority = 0,
        bool occupiesLine = false,
        bool lastLine = false,
        bool alwaysShow = false,
        bool useByMouseClick = false,
        bool allowDuringMeeting = false,
        Action meetingStarted = null,
        Action meetingEnded = null,
        Action<RoleButtonBase> onUpdate = null,
        Action<RoleButtonBase> onReleased = null,
        Action<RoleButtonBase> onBroken = null)
    {
        var config = new RoleButtonConfig
        {
            Label = label ?? "",
            LabelKey = labelKey ?? "",
            Icon = icon,
            BrokenIcon = brokenIcon,
            Cooldown = cooldown,
            StartOnCooldown = startOnCooldown,
            MaxUses = maxUses,
            Hotkey = hotkey,
            SubHotkey = subHotkey,
            SubAction = subAction,
            IsKillButton = isKillButton,
            ArrangedAsKillButton = arrangedAsKillButton,
            LabelType = labelType,
            OnClickSFX = clickSfx ?? "",
            CooldownReadySFX = readySfx ?? "",
            IsLeftSide = leftSide,
            Priority = priority,
            OccupiesLine = occupiesLine,
            ShouldBeInLastLine = lastLine,
            AlwaysShow = alwaysShow,
            UseByMouseClick = useByMouseClick,
            AllowDuringMeeting = allowDuringMeeting,
            MeetingStarted = meetingStarted,
            MeetingEnded = meetingEnded,
            OnUpdate = onUpdate,
            OnReleased = onReleased,
            OnBroken = onBroken,
        };
        if (canShow != null) config.CanShow = canShow;
        if (canUse != null) config.CanUse = canUse;
        if (flashWhile != null) config.FlashWhile = flashWhile;

        return AbilityButton.Create(role, config, onClick);
    }

    /// <summary>创建普通技能按钮（旧写法，保留兼容 ✓；新代码建议用命名参数版 ✓）</summary>
    /// <param name="role">所属职业的运行时实例 ✓</param>
    /// <param name="config">旧的配置对象（`new RoleButtonConfig { … }` ✓）</param>
    /// <param name="onClick">点击回调 ✓</param>
    public static AbilityButton Create(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
        => AbilityButton.Create(role, config, onClick);

        /// <summary>杀手刀按钮：自动锁最近目标（队友与隔墙除外），冷却取 KillCooldown ✓</summary>
        /// <param name="role">所属职业的运行时实例 ✓（用它的 `MyPlayer` 当攻击者）</param>
        /// <param name="onKill">**击杀成功后的回调**（不传 = 只做击杀 ✓）</param>
        /// <param name="extraFilter">候选目标的**额外筛选**（返回 false 的人不会被锁定 ✓；队友/隔墙已被自动排除 ✓）</param>
        /// <param name="hotkey">热键（默认 `KeyCode.Q` ✓）</param>
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

        /// <summary>
        /// 创建**持续效果**按钮 —— **命名参数版** ✓（旧写法保留 ✓）
        /// <para>**必选 6 项**：`label` / `icon` / `cooldown` / `hotkey` / `onClick` + **`effectDuration`**</para>
        /// <para>⚠️ 效果时长**独立于冷却** ✓：效果**结束后**才开始走冷却 ✓（原来两者复用同一字段 ✗）</para>
        /// </summary>
        /// <param name="role">所属职业的运行时实例（职业里传 `this` ✓）</param>
        /// <param name="label">按钮**文字**（直接文本）；用语言键就传 `""` + <paramref name="labelKey"/> ✓</param>
        /// <param name="icon">按钮**图标**；`null` = 模板原图标（必须显式传 ✓）</param>
        /// <param name="cooldown">**冷却秒数**（`0` = 不冷却 ✓）；从"效果结束"那刻开始走 ✓</param>
        /// <param name="hotkey">**热键**（`KeyCode.None` = 无 ✓）</param>
        /// <param name="onClick">**点击回调**（必选 ✓）；效果中再点是否提前结束由 <paramref name="allowCancelByReclick"/> 决定 ✓</param>
        /// <param name="effectDuration">**效果持续秒数**（必选 ✓）；`0` = 一直持续到手动取消 ✓（**不是**"忘了写"的默认值 ✗）</param>
        /// <param name="labelKey">语言键；<paramref name="label"/> 非空时优先用 label ✓</param>
        /// <param name="brokenIcon">次数用完后显示的破损图标；`null` = 模板破损图 ✓</param>
        /// <param name="startOnCooldown">创建时是否立刻进冷却（默认 `true` ✓）</param>
        /// <param name="maxUses">最大使用次数（`0` = 不限 ✓）</param>
        /// <param name="subHotkey">副热键（`KeyCode.None` = 无 ✓）</param>
        /// <param name="subAction">副热键的回调 ✓</param>
        /// <param name="canShow">是否**显示**；不传 = 活着就显示 ✓</param>
        /// <param name="canUse">是否**可用**；不传 = 能移动就可用 ✓</param>
        /// <param name="flashWhile">何时让按钮闪烁（不传 = 不闪 ✓）</param>
        /// <param name="labelType">文字样式：`Standard` / `Impostor` / `Utility` / `Crewmate` ✓</param>
        /// <param name="clickSfx">点击音效 mp3 相对路径（`""` = 不播 ✓）</param>
        /// <param name="readySfx">冷却就绪音效 mp3 相对路径（`""` = 不播 ✓）</param>
        /// <param name="leftSide">是否放到屏幕左侧按钮组（默认右侧 ✓）</param>
        /// <param name="priority">同侧排序优先级（大的更靠前 ✓）</param>
        /// <param name="occupiesLine">是否独占一行（默认 false ✓）</param>
        /// <param name="lastLine">是否强制排最后一行（默认 false ✓）</param>
        /// <param name="alwaysShow">是否常驻显示（默认 false ✓）</param>
        /// <param name="useByMouseClick">是否也允许鼠标点击触发（默认 false ✓）</param>
        /// <param name="allowDuringMeeting">会议中是否也允许触发（默认 false ✓）</param>
        /// <param name="meetingStarted">会议开始回调（不传 = 无 ✓）</param>
        /// <param name="meetingEnded">会议结束回调（不传 = 无 ✓）</param>
        /// <param name="onUpdate">每帧回调（参数是按钮本身 ✓）</param>
        /// <param name="onReleased">按钮被回收时的回调 ✓</param>
        /// <param name="onBroken">按钮破损（次数耗尽）时的回调 ✓</param>
        /// <param name="allowCancelByReclick">
        /// 效果持续中**再点一下**是否**提前结束**（默认 `true` ✓）：
        /// `true` = 再点即结束（结束时才开始冷却 ✓）；`false` = 点了没反应，等时间自己结束 ✓
        /// </param>
        public static EffectButton CreateEffect(
            RuntimeRoleTemplate role,
            string label,
            Sprite icon,
            float cooldown,
            KeyCode hotkey,
            Action onClick,
            float effectDuration,
            string labelKey = "",
            Sprite brokenIcon = null,
            bool startOnCooldown = true,
            int maxUses = 0,
            KeyCode subHotkey = KeyCode.None,
            Action subAction = null,
            Func<bool> canShow = null,
            Func<bool> canUse = null,
            Func<bool> flashWhile = null,
            ButtonLabelType labelType = ButtonLabelType.Standard,
            string clickSfx = "",
            string readySfx = "",
            bool leftSide = false,
            int priority = 0,
            bool occupiesLine = false,
            bool lastLine = false,
            bool alwaysShow = false,
            bool useByMouseClick = false,
            bool allowDuringMeeting = false,
            Action meetingStarted = null,
            Action meetingEnded = null,
            Action<RoleButtonBase> onUpdate = null,
            Action<RoleButtonBase> onReleased = null,
            Action<RoleButtonBase> onBroken = null,
            bool allowCancelByReclick = true)
        {
            var config = new RoleButtonConfig
            {
                Label = label ?? "",
                LabelKey = labelKey ?? "",
                Icon = icon,
                BrokenIcon = brokenIcon,
                Cooldown = cooldown,
                StartOnCooldown = startOnCooldown,
                MaxUses = maxUses,
                Hotkey = hotkey,
                SubHotkey = subHotkey,
                SubAction = subAction,
                LabelType = labelType,
                OnClickSFX = clickSfx ?? "",
                CooldownReadySFX = readySfx ?? "",
                IsLeftSide = leftSide,
                Priority = priority,
                OccupiesLine = occupiesLine,
                ShouldBeInLastLine = lastLine,
                AlwaysShow = alwaysShow,
                UseByMouseClick = useByMouseClick,
                AllowDuringMeeting = allowDuringMeeting,
                MeetingStarted = meetingStarted,
                MeetingEnded = meetingEnded,
                OnUpdate = onUpdate,
                OnReleased = onReleased,
                OnBroken = onBroken,
            };
            if (canShow != null) config.CanShow = canShow;
            if (canUse != null) config.CanUse = canUse;
            if (flashWhile != null) config.FlashWhile = flashWhile;

            return CreateEffect(role, config, onClick, effectDuration, allowCancelByReclick);
        }

        /// <summary>
        /// 创建**持续效果**按钮（旧写法，保留兼容 ✓）
        /// <para>⚠️ 2026-10-25：`effectDuration` 也是**必选**（去掉默认值 ✓）—— 想要"持续到手动取消"就显式传 `0f` ✓</para>
        /// </summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="config">旧的配置对象（`new RoleButtonConfig { … }` ✓；新代码优先用命名参数版 ✓）</param>
        /// <param name="onClick">点击回调 ✓</param>
        /// <param name="effectDuration">**效果持续秒数**（必选 ✓）；`0` = 持续到手动取消 ✓，**独立于冷却** ✓</param>
        /// <param name="allowCancelByReclick">效果中再点一下是否提前结束（默认 `true` ✓）</param>
        public static EffectButton CreateEffect(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick,
                                               float effectDuration, bool allowCancelByReclick = true)
        {
            var button = EffectButton.Create(role, config, onClick);
            if (button == null) return null;

            button.EffectDuration = effectDuration;              // 0 = 一直持续到手动取消 ✓
            button.AllowCancelByReclick = allowCancelByReclick;
            return button;
        }

        /// <summary>创建会议目标按钮（会议中为**每位玩家**生成一个目标按钮 ✓）</summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="onClick">点击某位玩家的按钮时的回调（参数 = 会议界面 + 那名玩家 ✓）</param>
        /// <param name="canAdd">**哪些玩家**才生成按钮（返回 false 的人没有按钮 ✓；不传 = 全部 ✓）</param>
        /// <param name="icon">按钮图标（`null` = 用模板图标 ✓）</param>
        /// <param name="sfx">点击音效的 mp3 相对路径（不传/空 = 不播 ✓）</param>
        public static MeetingTargetButton CreateMeetingTarget(RuntimeRoleTemplate role,
            Action<MeetingHud, PlayerControl> onClick,
            Func<PlayerControl, bool> canAdd = null,
            Sprite icon = null,
            string sfx = null)
            => MeetingTargetButton.Create(role, onClick, canAdd, icon, sfx);

        /// <summary>
        /// 创建**会议右下角**按钮 —— **命名参数版** ✓（旧写法保留 ✓）
        /// <para>**必选 3 项**：`label` / `icon` / `onClick`（会议按钮一般不用热键/冷却，`cooldown` 保持可选 ✓）</para>
        /// </summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="label">按钮**文字**（直接文本）；用语言键就传 `""` + <paramref name="labelKey"/> ✓</param>
        /// <param name="icon">按钮**图标**；`null` = 模板原图标（必须显式传 ✓）</param>
        /// <param name="onClick">**点击回调**（必选 ✓）</param>
        /// <param name="labelKey">语言键；<paramref name="label"/> 非空时优先用 label ✓</param>
        /// <param name="cooldown">冷却秒数（默认 `0` = 不冷却 ✓）</param>
        /// <param name="maxUses">最大使用次数（`0` = 不限 ✓）</param>
        /// <param name="hotkey">热键（`KeyCode.None` = 无 ✓；会议里一般不用 ✓）</param>
        /// <param name="canShow">是否显示；不传 = 默认显示 ✓</param>
        /// <param name="canUse">是否可用；不传 = 默认可用 ✓</param>
        /// <param name="labelType">文字样式：`Standard` / `Impostor` / `Utility` / `Crewmate` ✓</param>
        /// <param name="clickSfx">点击音效 mp3 相对路径（`""` = 不播 ✓）</param>
        /// <param name="leftSide">是否放到**左下角**（默认右下角 ✓）</param>
        /// <param name="priority">同角排序优先级（大的更靠前 ✓）</param>
        /// <param name="corner">放在会议的哪个角：`BottomRight`（默认）/ `BottomLeft` ✓</param>
        /// <param name="meetingStarted">会议开始回调（不传 = 无 ✓）</param>
        /// <param name="meetingEnded">会议结束回调（不传 = 无 ✓）</param>
        public static MeetingAbilityButton CreateMeetingAbility(
            RuntimeRoleTemplate role,
            string label,
            Sprite icon,
            Action onClick,
            string labelKey = "",
            float cooldown = 0f,
            int maxUses = 0,
            KeyCode hotkey = KeyCode.None,
            Func<bool> canShow = null,
            Func<bool> canUse = null,
            ButtonLabelType labelType = ButtonLabelType.Standard,
            string clickSfx = "",
            bool leftSide = false,
            int priority = 0,
            MeetingCorner corner = MeetingCorner.BottomRight,
            Action meetingStarted = null,
            Action meetingEnded = null)
        {
            var config = new RoleButtonConfig
            {
                Label = label ?? "",
                LabelKey = labelKey ?? "",
                Icon = icon,
                Cooldown = cooldown,
                MaxUses = maxUses,
                Hotkey = hotkey,
                LabelType = labelType,
                OnClickSFX = clickSfx ?? "",
                IsLeftSide = leftSide,
                Priority = priority,
                AllowDuringMeeting = true,
                MeetingStarted = meetingStarted,
                MeetingEnded = meetingEnded,
            };
            if (canShow != null) config.CanShow = canShow;
            if (canUse != null) config.CanUse = canUse;

            return MeetingAbilityButton.Create(role, config, onClick, corner);
        }

        /// <summary>创建会议右下角按钮（旧写法，保留兼容 ✓）</summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="config">旧的配置对象（`new RoleButtonConfig { … }` ✓）</param>
        /// <param name="onClick">点击回调 ✓</param>
        public static MeetingAbilityButton CreateMeetingAbility(RuntimeRoleTemplate role, RoleButtonConfig config, Action onClick)
            => MeetingAbilityButton.Create(role, config, onClick);

        /// <summary>创建会议角落按钮，可指定角落（旧写法，保留兼容 ✓）</summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="config">旧的配置对象 ✓</param>
        /// <param name="onClick">点击回调 ✓</param>
        /// <param name="corner">放在哪个角：`BottomRight` / `BottomLeft` ✓</param>
        public static MeetingAbilityButton CreateMeetingAbility(RuntimeRoleTemplate role, RoleButtonConfig config,
            Action onClick, MeetingCorner corner) => MeetingAbilityButton.Create(role, config, onClick, corner);

        /// <summary>创建会议目标按钮（旧写法：传 `MeetingTargetButtonConfig` ✓）</summary>
        /// <param name="role">所属职业的运行时实例 ✓</param>
        /// <param name="config">会议目标按钮的配置（含位置/缩放/悬停图标等 ✓）</param>
        /// <param name="onClick">点击某位玩家的按钮时的回调 ✓</param>
        public static MeetingTargetButton CreateMeetingTarget(RuntimeRoleTemplate role, MeetingTargetButtonConfig config,
            Action<MeetingHud, PlayerControl> onClick) => MeetingTargetButton.Create(role, config, onClick);
    }
}
