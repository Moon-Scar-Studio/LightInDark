using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Configuration;
using LightInDark.Game;
using LightInDark.Modifiers;
using LightInDark.RPCs;

namespace Light.Modifiers;

public static class ModifierRegistry
{
    private static readonly Modifier[] _all = { Flash.Instance };
    private static readonly Dictionary<(string Key, byte PlayerId), float> _values = new();
    private static bool _initialized;

    public static IReadOnlyList<Modifier> All => _all;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        RpcDefinitions.OnAddModifier += Apply;
        RpcDefinitions.OnRemoveModifier += (playerId, key) =>
        {
            var player = Player.Get(playerId);
            if (player != null) ModifierManager.RemoveModifier(player, key);
        };
    }

    public static Modifier? Get(string key)
    {
        foreach (var modifier in _all)
            if (string.Equals(modifier.Key, key, StringComparison.OrdinalIgnoreCase))
                return modifier;
        return null;
    }

    public static void SetValue(string key, byte playerId, float value)
        => _values[(key, playerId)] = value;

    public static float GetValue(string key, byte playerId)
        => _values.TryGetValue((key, playerId), out var value) ? value : 1f;

    private static void Apply(byte playerId, string key, float value)
    {
        var player = Player.Get(playerId);
        var modifier = Get(key);
        if (player == null || modifier == null) return;

        SetValue(key, playerId, value);
        ModifierManager.AddModifier(player, modifier);
    }
}

public static class ModifierAssigner
{
    public static void Assign()
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost) return;

        var players = Player.AllPlayers
            .Where(p => p?.Control != null && !p.IsDeadObject && !p.Control.isDummy)
            .OrderBy(_ => _rng.Next())
            .ToList();

        foreach (var modifier in ModifierRegistry.All)
        {
            int count = ConfigRegistry.GetInt($"modifier.{modifier.Key}.count");
            int chance = ConfigRegistry.GetInt($"modifier.{modifier.Key}.chance");
            if (count <= 0 || chance <= 0) continue;

            int assigned = 0;
            foreach (var player in players)
            {
                if (assigned >= count) break;
                if (ModifierManager.HasModifier(player, modifier.Key)) continue;
                if (_rng.Next(100) >= chance) continue;

                float value = ConfigRegistry.GetFloat($"modifier.{modifier.Key}.speed");
                RpcDefinitions.AddModifier(player.Control.PlayerId, modifier.Key, value);
                assigned++;
            }
        }
    }

    private static readonly System.Random _rng = new();
}
