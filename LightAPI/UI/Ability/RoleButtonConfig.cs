using System;
using UnityEngine;

namespace LightInDark.UI.Ability
{
    /// <summary>
    /// 角色按钮配置（普通按钮 / 持续按钮共用）。
    /// 使用链式赋值即可，未设置的项全部有安全默认值。
    /// </summary>
    public class RoleButtonConfig
    {
        /// <summary>按钮显示文本（直接文本，未设置时回退 <see cref="LabelKey"/>）。</summary>
        public string Label = "";

        /// <summary>按钮显示文本语言键（Label 为空时解析此键）。</summary>
        public string LabelKey = "";

        /// <summary>按钮图标（为 null 时使用模板原图标）。</summary>
        public Sprite Icon;

        /// <summary>热键（KeyCode.None 表示无热键）。</summary>
        public KeyCode Hotkey = KeyCode.None;

        /// <summary>冷却时间（秒）。</summary>
        public float Cooldown = 0f;

        /// <summary>可用条件（false 时点击无效但仍显示）。</summary>
        public Func<bool> CanUse = () => true;

        /// <summary>显示条件（false 时隐藏按钮）。</summary>
        public Func<bool> CanShow = () => true;
        public Predicate<float> CanRunCooldown = (time) => true;
        /// <summary>是否克隆 KillButton 模板（默认克隆 AbilityButton 模板）。</summary>
        public bool IsKillButton = false;

        /// <summary>是否常驻显示（忽略 HUD 隐藏状态）。</summary>
        public bool AlwaysShow = false;

        /// <summary>最大使用次数；0 表示不限。</summary>
        public int MaxUses = 0;

        /// <summary>点击音效（相对路径 mp3，打包进 dll；为空不播放）。TODO: 下一轮实现 mp3 解析。</summary>
        public string OnClickSFX = "";

        /// <summary>冷却完成音效（相对路径 mp3；为空不播放）。TODO: 下一轮实现 mp3 解析。</summary>
        public string CooldownReadySFX = "";

        public ButtonLabelType LabelType = ButtonLabelType.Standard;

        public Func<bool> FlashWhile;

        public Sprite BrokenIcon;

        public Action SubAction;

        public KeyCode SubHotkey = KeyCode.None;

        public bool UseByMouseClick = false;

        public bool IsLeftSide = false;

        public int Priority = 0;

        public bool ArrangedAsKillButton = false;

        public bool OccupiesLine = false;

        public bool ShouldBeInLastLine = false;

        /// <summary>解析后的显示文本。</summary>
        public string ResolvedLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Label)) return Label;
                if (!string.IsNullOrEmpty(LabelKey)) return LightInDark.Language.Language.GetStringOrKey(LabelKey, LabelKey);
                return "";
            }
        }

        // ---- 链式便捷 ----

        public RoleButtonConfig SetLabel(string label) { Label = label; return this; }
        public RoleButtonConfig SetLabelKey(string key) { LabelKey = key; return this; }
        public RoleButtonConfig SetIcon(Sprite icon) { Icon = icon; return this; }
        public RoleButtonConfig SetHotkey(KeyCode key) { Hotkey = key; return this; }
        public RoleButtonConfig SetCooldown(float value) { Cooldown = value; return this; }
        public RoleButtonConfig SetCanUse(Func<bool> value) { CanUse = value; return this; }
        public RoleButtonConfig SetCanShow(Func<bool> value) { CanShow = value; return this; }
        public RoleButtonConfig SetMaxUses(int value) { MaxUses = value; return this; }
        public RoleButtonConfig SetOnClickSFX(string path) { OnClickSFX = path; return this; }
        public RoleButtonConfig SetCooldownReadySFX(string path) { CooldownReadySFX = path; return this; }
        public RoleButtonConfig SetLabelType(ButtonLabelType type) { LabelType = type; return this; }
        public RoleButtonConfig SetCanRunCooldownPredicate(Predicate<float> p) { CanRunCooldown = p; return this; }
        public RoleButtonConfig SetFlashWhile(Func<bool> value) { FlashWhile = value; return this; }
        public RoleButtonConfig SetBrokenIcon(Sprite icon) { BrokenIcon = icon; return this; }
        public RoleButtonConfig SetSubAction(Action value) { SubAction = value; return this; }
        public RoleButtonConfig SetSubHotkey(KeyCode key) { SubHotkey = key; return this; }
        public RoleButtonConfig SetUseByMouseClick(bool value) { UseByMouseClick = value; return this; }
        public RoleButtonConfig SetLeftSide(bool value) { IsLeftSide = value; return this; }
        public RoleButtonConfig SetPriority(int value) { Priority = value; return this; }
        public RoleButtonConfig SetArrangedAsKillButton(bool value) { ArrangedAsKillButton = value; return this; }
        public RoleButtonConfig SetOccupiesLine(bool value) { OccupiesLine = value; return this; }
        public RoleButtonConfig SetShouldBeInLastLine(bool value) { ShouldBeInLastLine = value; return this; }
    }

    public enum ButtonLabelType
    {
        Standard,
        Impostor,
        Utility,
        Crewmate,
    }
}