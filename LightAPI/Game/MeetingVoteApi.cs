using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LightInDark.Core;
using LightInDark.RPCs;
using UnityEngine;

namespace LightInDark.Game;

public static class MeetingVoteApi
{
    private const byte SkippedVote = 253;

    private static readonly Dictionary<byte, int> _weights = new();
    private static readonly Dictionary<byte, int> _stored = new();
    private static readonly Dictionary<byte, int> _storeTimes = new();
    private static readonly Dictionary<byte, byte> _storedTarget = new();
    private static readonly List<(byte First, byte Second)> _swaps = new();
    private static bool _initialized;

    public static event Func<byte, bool>? ConfirmHandler;
    public static event Action? Update;
    public static event Action? FrameUpdate;

    internal static bool InvokeConfirm(byte target)
    {
        var handlers = ConfirmHandler;
        if (handlers == null) return false;

        foreach (var handler in handlers.GetInvocationList())
            if (handler is Func<byte, bool> confirm && confirm(target))
                return true;

        return false;
    }

    internal static void InvokeUpdate() => Update?.Invoke();
    internal static void InvokeFrameUpdate() => FrameUpdate?.Invoke();

    public static int GetStoredVotes(byte playerId)
        => _stored.TryGetValue(playerId, out var value) ? value : 0;

    public static int GetStoreTimes(byte playerId)
        => _storeTimes.TryGetValue(playerId, out var value) ? value : 0;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        RpcDefinitions.OnMeetingStoreVote += ApplyStore;
        RpcDefinitions.OnMeetingCustomVote += ApplyCustom;
        RpcDefinitions.OnMeetingSetStored += ApplySetStored;
    }

    public static void SetSwap(byte first, byte second)
    {
        if (first == second) return;
        _swaps.Add((first, second));
    }

    internal static void PlaySwapEffects()
    {
        var meeting = MeetingHud.Instance;
        if (meeting == null || _swaps.Count == 0) return;

        foreach (var (first, second) in _swaps)
        {
            var a = FindArea(first);
            var b = FindArea(second);
            if (a == null || b == null) continue;

            var pa = a.transform.localPosition;
            var pb = b.transform.localPosition;
            meeting.StartCoroutine(Effects.Slide3D(a.transform, pa, pb, 1.5f));
            meeting.StartCoroutine(Effects.Slide3D(b.transform, pb, pa, 1.5f));
        }
    }

    public static void ResetAll()
    {
        _weights.Clear();
        _stored.Clear();
        _storeTimes.Clear();
        _storedTarget.Clear();
        _swaps.Clear();
    }

    private static void ApplyStore(byte playerId, byte targetId, int amount)
    {
        _stored[playerId] = GetStoredVotes(playerId) + Mathf.Max(1, amount);
        _storeTimes[playerId] = GetStoreTimes(playerId) + 1;
        _storedTarget[playerId] = targetId;

        var area = FindArea(playerId);
        if (area != null && !area.AmDead && !area.DidVote)
        {
            area.SetVote(targetId);
            area.SetHasVoted();
        }

        var meeting = MeetingHud.Instance;
        if (meeting != null && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            meeting.CheckForEndVoting();
    }

    private static void ApplyCustom(byte voterId, byte targetId, int weight)
    {
        _weights[voterId] = Mathf.Max(1, weight);

        var area = FindArea(voterId);
        if (area != null && !area.AmDead && !area.DidVote)
        {
            area.SetVote(targetId);
            area.SetHasVoted();
        }

        var meeting = MeetingHud.Instance;
        if (meeting != null && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            meeting.CheckForEndVoting();
    }

    private static void ApplySetStored(byte playerId, int value)
        => _stored[playerId] = Mathf.Max(0, value);

    internal static void ResetMeeting()
    {
        _weights.Clear();
        _storedTarget.Clear();
        _swaps.Clear();
    }

    internal static void AddWeightVotes(Il2CppSystem.Collections.Generic.Dictionary<byte, int> result)
    {
        foreach (var pair in _weights)
        {
            if (pair.Value <= 1) continue;

            var area = FindArea(pair.Key);
            if (area == null) continue;

            byte target = (byte)area.VotedForId;
            if (target >= 250) continue;

            int current = result.ContainsKey(target) ? result[target] : 0;
            result[target] = current + pair.Value - 1;
        }

        foreach (var pair in _storedTarget)
        {
            byte target = pair.Value;
            if (target == 254 || target == 255) continue;

            int current = result.ContainsKey(target) ? result[target] : 0;
            if (current > 0) result[target] = current - 1;
        }

        foreach (var (first, second) in _swaps)
        {
            int firstVotes = result.ContainsKey(first) ? result[first] : 0;
            int secondVotes = result.ContainsKey(second) ? result[second] : 0;
            result[first] = secondVotes;
            result[second] = firstVotes;
        }
    }

    internal static bool HasExtraVotes()
    {
        foreach (var pair in _weights)
            if (pair.Value > 1) return true;
        return false;
    }

    internal static Il2CppStructArray<MeetingHud.VoterState> ExpandStates(
        Il2CppStructArray<MeetingHud.VoterState> states)
    {
        int extra = 0;
        foreach (var pair in _weights)
            if (pair.Value > 1) extra += pair.Value - 1;

        if (states == null) return states;
        if (extra <= 0 && _swaps.Count == 0) return states;

        var expanded = new Il2CppStructArray<MeetingHud.VoterState>(states.Length + extra);
        for (int i = 0; i < states.Length; i++)
        {
            var state = states[i];
            state.VotedForId = RemapTarget(state.VotedForId);
            expanded[i] = state;
        }

        int index = states.Length;
        foreach (var pair in _weights)
        {
            if (pair.Value <= 1) continue;

            var area = FindArea(pair.Key);
            if (area == null) continue;

            byte target = RemapTarget((byte)area.VotedForId);
            for (int i = 1; i < pair.Value; i++)
            {
                expanded[index] = new MeetingHud.VoterState { VoterId = pair.Key, VotedForId = target };
                index++;
            }
        }

        return expanded;
    }

    private static byte RemapTarget(byte target)
    {
        foreach (var (first, second) in _swaps)
        {
            if (target == first) target = second;
            else if (target == second) target = first;
        }

        return target;
    }

    private static PlayerVoteArea? FindArea(byte playerId)
    {
        var meeting = MeetingHud.Instance;
        if (meeting == null || meeting.playerStates == null) return null;

        foreach (var area in meeting.playerStates)
            if (area != null && (byte)area.PlayerId == playerId)
                return area;

        return null;
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Confirm))]
public static class MeetingVoteConfirmPatch
{
    public static bool Prefix(byte suspectStateIdx)
        => !MeetingVoteApi.InvokeConfirm(suspectStateIdx);
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
public static class MeetingVoteUpdatePatch
{
    public static void Postfix(MeetingHud __instance)
    {
        if (__instance == null) return;
        MeetingVoteApi.InvokeFrameUpdate();
        if (__instance.state != MeetingHud.MeetingStates.NotVoted) return;
        if (!Input.GetMouseButtonDown(1)) return;
        MeetingVoteApi.InvokeUpdate();
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
public static class MeetingVoteStartPatch
{
    public static void Postfix()
    {
        MeetingVoteApi.ResetMeeting();
    }
}

[HarmonyPatch(typeof(global::GameManager), nameof(global::GameManager.StartGame))]
public static class MeetingVoteGameStartPatch
{
    public static void Postfix()
    {
        MeetingVoteApi.ResetAll();
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CalculateVotes))]
public static class MeetingVoteCalculatePatch
{
    public static void Postfix(Il2CppSystem.Collections.Generic.Dictionary<byte, int> __result)
    {
        try
        {
            MeetingVoteApi.AddWeightVotes(__result);
        }
        catch { }
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.PopulateResults))]
public static class MeetingVotePopulatePatch
{
    public static void Prefix(ref Il2CppStructArray<MeetingHud.VoterState> states)
    {
        try
        {
            states = MeetingVoteApi.ExpandStates(states);
            MeetingVoteApi.PlaySwapEffects();
        }
        catch { }
    }
}

