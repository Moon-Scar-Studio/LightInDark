using System;
using System.Collections.Generic;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LightInDark.UI.Ability;

/// <summary>Managed per-player meeting buttons. All targets share cooldown and usage limits.</summary>
public class MeetingTargetButton : RoleButtonBase
{
    private sealed record Target(GameObject Object, PlayerControl Player, PlayerVoteArea Area, SpriteRenderer Renderer);
    private readonly MeetingTargetButtonConfig _targetConfig;
    private readonly Action<MeetingHud, PlayerControl> _onTarget;
    private readonly List<Target> _targets = new();
    private MeetingHud _meeting;
    private Target _selectedTarget;

    private MeetingTargetButton(RuntimeRoleTemplate role, MeetingTargetButtonConfig config,
        Action<MeetingHud, PlayerControl> onClick) : base(role, role.MyPlayer, config, null)
    { _targetConfig = config; _onTarget = onClick; }

    public static MeetingTargetButton Create(RuntimeRoleTemplate role,
        Action<MeetingHud, PlayerControl> onClick, Func<PlayerControl, bool> canAdd = null,
        Sprite icon = null, string sfx = null)
    {
        var config = new MeetingTargetButtonConfig { Icon = icon, OnClickSFX = sfx ?? "" };
        if (canAdd != null) config.CanAddTarget = canAdd;
        return Create(role, config, onClick);
    }
    public static MeetingTargetButton Create(RuntimeRoleTemplate role, MeetingTargetButtonConfig config,
        Action<MeetingHud, PlayerControl> onClick)
    {
        if (role == null) throw new ArgumentNullException(nameof(role));
        var button = new MeetingTargetButton(role, config ?? new MeetingTargetButtonConfig(), onClick);
        RoleButtonManager.Register(button);
        return button;
    }
    internal void OnMeetingStart(MeetingHud meeting)
    {
        DestroyUI();
        if (IsDeadObject || !_player.IsLocal || meeting == null || meeting.playerStates == null) return;
        _meeting = meeting;
        int slot = _targetConfig.AutoArrange ? RoleButtonManager.MeetingSlot(this) : 0;
        foreach (var area in meeting.playerStates)
        {
            if (area == null) continue;
            var player = FindPlayer(area.PlayerId);
            if (player == null || (!_targetConfig.IncludeSelf && player.PlayerId == _player.Control.PlayerId)
                || !(_targetConfig.CanAddTarget?.Invoke(player) ?? true)) continue;
            var root = area.Buttons != null ? area.Buttons.transform.Find("CancelButton") : null;
            if (root == null) continue;
            var go = Object.Instantiate(root.gameObject, area.transform);
            go.name = $"MeetingTarget_{Role.CodeName}_{player.PlayerId}";
            go.transform.localPosition = _targetConfig.Position + new Vector3(slot * 0.42f, 0f, 0f);
            if (!_targetConfig.UseTemplateScale) go.transform.localScale = _targetConfig.Scale;
            foreach (var aspect in go.GetComponentsInChildren<AspectPosition>(true)) aspect.enabled = false;
            var passive = go.GetComponent<PassiveButton>();
            if (passive == null) { Object.Destroy(go); continue; }
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && _targetConfig.Icon != null) sr.sprite = _targetConfig.Icon;
            var target = new Target(go, player, area, sr);
            var normalIcon = sr != null ? sr.sprite : null;
            var baseScale = go.transform.localScale;
            passive.OnClick = new Button.ButtonClickedEvent();
            passive.OnMouseOver = new UnityEvent();
            passive.OnMouseOut = new UnityEvent();
            passive.OnClick.AddListener((UnityAction)(() => InvokeTarget(target)));
            passive.OnMouseOver.AddListener((UnityAction)(() =>
            {
                if (sr != null && _targetConfig.HoverIcon != null) sr.sprite = _targetConfig.HoverIcon;
                go.transform.localScale = baseScale * 1.15f;
                _targetConfig.OnHover?.Invoke();
            }));
            passive.OnMouseOut.AddListener((UnityAction)(() =>
            {
                if (sr != null) sr.sprite = normalIcon;
                go.transform.localScale = baseScale;
            }));
            _targets.Add(target);
            if (_gameObject == null) _gameObject = go;
        }
        LightLogger.Log($"[MeetingTarget] {Role.CodeName} built {_targets.Count} targets (playerStates={meeting.playerStates.Count})");
        Update();
    }
    private void InvokeTarget(Target target)
    {
        // Re-evaluate on click: targets may die/disconnect after construction.
        if (!CanTarget(target) || target.Object == null || !target.Object.activeInHierarchy) return;
        _selectedTarget = target;
        try
        {
            _onClick = () => _onTarget?.Invoke(_meeting, target.Player);
            HandleClick();
        }
        finally { _selectedTarget = null; _onClick = null; }
    }
    public bool TryClickTarget(byte playerId)
    {
        var target = _targets.Find(t => t.Player != null && t.Player.PlayerId == playerId);
        if (target == null) return false;
        int previous = _lastClickFrame;
        InvokeTarget(target);
        return previous != _lastClickFrame;
    }
    protected override void HandleClick()
    {
        if (_selectedTarget != null && CanTarget(_selectedTarget))
        {
            base.HandleClick();
            _targetConfig.OnClickFeedback?.Invoke();
        }
    }
    private bool CanTarget(Target target) => target.Player != null && target.Area != null
        && !target.Area.AmDead && (target.Player.Data != null && !target.Player.Data.Disconnected)
        && (_targetConfig.CanUseTarget?.Invoke(target.Player) ?? true);

    // ═══════════════════════════════════════════════════════════════════
    //  【2026-11-01 自 `/LightInDark (2)` 合并】按玩家上色（交换者用来标"已被换过"的人 ✓）
    // ═══════════════════════════════════════════════════════════════════
    private readonly Dictionary<byte, UnityEngine.Color> _targetColors = new();

    /// <summary>给某个玩家的目标按钮上色（传 null = 恢复默认色 ✓）。</summary>
    public void SetTargetColor(byte playerId, UnityEngine.Color? color)
    {
        if (color.HasValue) _targetColors[playerId] = color.Value;
        else _targetColors.Remove(playerId);
    }

    /// <summary>清掉所有自定义颜色 ✓</summary>
    public void ClearTargetColors() => _targetColors.Clear();
    protected override bool ShouldShow => !IsDeadObject && _meeting != null && _player.IsLocal
        && !_player.IsDead && (_config.CanShow?.Invoke() ?? true);
    protected override bool CanAcceptInput => ShouldShow && _meeting != null;
    protected override bool ShouldBeUsable => _meeting != null
        && (_meeting.state == MeetingHud.MeetingStates.Discussion || _meeting.state == MeetingHud.MeetingStates.NotVoted
            || _meeting.state == MeetingHud.MeetingStates.Voted) && base.ShouldBeUsable;
    protected override bool ShouldTickCooldown() => ShouldShow;
    public override void Update()
    {
        if (IsDeadObject || _meeting == null) return;
        TickCooldown();
        foreach (var target in _targets)
        {
            if (target.Object == null) continue;
            bool show = ShouldShow && CanTarget(target) && (_targetConfig.CanAddTarget?.Invoke(target.Player) ?? true);
            target.Object.SetActive(show);
            if (target.Renderer != null)
            {
                // 默认色用配置里的 `DefaultColor` ✓；`SetTargetColor` 指定过的优先（仅在显示时 ✓）
                var baseColor = show && ShouldBeUsable
                    ? _targetConfig.DefaultColor
                    : new UnityEngine.Color(0.4f, 0.4f, 0.4f, 1f);

                if (show && target.Player != null
                    && _targetColors.TryGetValue(target.Player.PlayerId, out var custom))
                    baseColor = custom;

                target.Renderer.color = baseColor;
            }
        }
    }
    internal void OnMeetingEnd() => DestroyUI();
    protected override void CreateUI() { }
    protected override void DestroyUI()
    {
        foreach (var target in _targets) if (target.Object != null) Object.Destroy(target.Object);
        _targets.Clear();
        _gameObject = null;
        _meeting = null;
        _onClick = null;
    }
    private static PlayerControl FindPlayer(byte id)
    {
        foreach (var p in PlayerControl.AllPlayerControls) if (p != null && p.PlayerId == id) return p;
        return null;
    }
}
