using System;
using System.Collections.Generic;
using HarmonyLib;
using Light.Utilities;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using UnityEngine;

namespace Light.UI;

internal static class FreePlayControls
{
    private static readonly List<LightInDark.UI.Ability.AbilityButton> _buttons = new();
    private static Player? _owner;
    private static bool _failed;

    internal static void Tick()
    {
        if (_failed) return;

        try
        {
            if (!GameModeManager.AllowRoleChanges)
            {
                Clear();
                return;
            }

            var client = AmongUsClient.Instance;
            if (client == null || client.GameState != InnerNet.InnerNetClient.GameStates.Started)
            {
                Clear();
                return;
            }

            var local = Player.LocalPlayer;
            if (local == null || local.Control == null) return;

            if (_owner != local || _buttons.Exists(b => b == null || b.IsDeadObject))
            {
                Clear();
                _owner = local;
            }

            if (_buttons.Count == 0) Create(local);
        }
        catch (Exception ex)
        {
            _failed = true;
            LightLogger.LogError("[FreePlayControls]", ex);
        }
    }

    private static void Create(Player local)
    {
        var operate = ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.FreePlayOperate.png", 115f);
        var revive = ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.FreePlayRevive.png", 115f);
        var suicide = ResourceHelper.LoadSpriteFromResource("Light.Resources.Roles.FreePlaySuicide.png", 115f);
        var plainFont = Light.UI.Window.MenuTextTemplate2.Font;
        var plainMaterial = Light.UI.Window.MenuTextTemplate2.FontMaterial;

        // ★ 2026-10-25 用户要求：自由模式左侧加「RPC 触发器」按钮（带搜索框，搜名字 / id ✓）
        _buttons.Add(LightInDark.UI.Ability.AbilityButton.CreateForPlayer(local, new RoleButtonConfig
        {
            Label = "RPC 触发器",
        }, () => DebugTools.RpcTriggerPanel.OpenWindow()));
        _buttons.Add(LightInDark.UI.Ability.AbilityButton.CreateForPlayer(local, new RoleButtonConfig
        {
            Label = "选择职业",
            Hotkey = KeyCode.Z,
            StartOnCooldown = false,
            AlwaysShow = true,
            IsLeftSide = true,
            PlainLabel = true,
            PlainFont = plainFont,
            PlainFontMaterial = plainMaterial,
            Icon = operate,
            CanShow = InGame,
        }, () => DebugTools.RoleDebugPanel.OpenWindow()));

        _buttons.Add(LightInDark.UI.Ability.AbilityButton.CreateForPlayer(local, new RoleButtonConfig
        {
            Label = "自杀",
            Hotkey = KeyCode.X,
            StartOnCooldown = false,
            AlwaysShow = true,
            IsLeftSide = true,
            PlainLabel = true,
            PlainFont = plainFont,
            PlainFontMaterial = plainMaterial,
            Icon = suicide,
            CanShow = () => InGame() && !local.IsDead,
        }, () => RpcDefinitions.Suicide(local.Control, true, "freeplay", PlayerState.Suicide)));

        _buttons.Add(LightInDark.UI.Ability.AbilityButton.CreateForPlayer(local, new RoleButtonConfig
        {
            Label = "BUFF 测试",
            Hotkey = KeyCode.B,
            StartOnCooldown = false,
            AlwaysShow = true,
            IsLeftSide = true,
            PlainLabel = true,
            PlainFont = plainFont,
            PlainFontMaterial = plainMaterial,
            Icon = operate,
            CanShow = InGame,
        }, () => DebugTools.BuffDebugPanel.OpenWindow()));

        _buttons.Add(LightInDark.UI.Ability.AbilityButton.CreateForPlayer(local, new RoleButtonConfig
        {
            Label = "复活",
            Hotkey = KeyCode.Y,
            StartOnCooldown = false,
            AlwaysShow = true,
            IsLeftSide = true,
            PlainLabel = true,
            PlainFont = plainFont,
            PlainFontMaterial = plainMaterial,
            Icon = revive,
            CanShow = () => InGame() && local.IsDead,
        }, () => RpcDefinitions.FreePlayRevive(local.Control.PlayerId)));
    }

    private static bool InGame()
    {
        var client = AmongUsClient.Instance;
        if (client == null || client.GameState != InnerNet.InnerNetClient.GameStates.Started) return false;
        if (!GameModeManager.AllowRoleChanges) return false;
        if (MeetingHud.Instance != null || IntroCutscene.Instance != null) return false;
        if (MapBehaviour.Instance != null && MapBehaviour.Instance.IsOpen) return false;
        return true;
    }

    private static void Clear()
    {
        foreach (var button in _buttons) button?.Release();
        _buttons.Clear();
        _owner = null;
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class FreePlayControlsPatch
{
    public static void Postfix() => FreePlayControls.Tick();
}
