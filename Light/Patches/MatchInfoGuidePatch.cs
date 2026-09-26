using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// 隐藏游戏原版的“对局信息指南”（MatchInfoGuide）面板及其 HUD 入口按钮：
/// - MatchInfoGuide.Awake 时隐藏面板自身；
/// - 拦截 MatchInfoGuide.Open()，任何入口都打不开；
/// - MatchInfoHudButton.Update 时隐藏入口按钮（避免与聊天按钮重叠/碍眼）。
/// </summary>
[HarmonyPatch(typeof(MatchInfoGuide))]
public static class MatchInfoGuideHidePatch
{
    [HarmonyPatch(nameof(MatchInfoGuide.Awake))]
    [HarmonyPostfix]
    public static void AwakePostfix(MatchInfoGuide __instance)
    {
        try
        {
            if (__instance == null) return;
            __instance.gameObject.SetActive(false);
            LightLogger.Log("[MatchInfoGuide] 已隐藏（Awake）");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MatchInfoGuideHidePatch.Awake]", ex);
        }
    }

    [HarmonyPatch(nameof(MatchInfoGuide.Open))]
    [HarmonyPrefix]
    public static bool OpenPrefix(MatchInfoGuide __instance)
    {
        // 直接拦截，不允许打开
        if (__instance != null)
            __instance.gameObject.SetActive(false);
        return false;
    }
}

/// <summary>
/// 隐藏“对局信息”HUD 入口按钮（那个显示在聊天按钮旁的图标）。
/// 每帧强制隐藏自身（首次隐藏后 GameObject 失活，Update 停止也无妨）。
/// </summary>
[HarmonyPatch(typeof(MatchInfoHudButton), nameof(MatchInfoHudButton.Update))]
public static class MatchInfoHudButtonHidePatch
{
    public static bool Prefix(MatchInfoHudButton __instance)
    {
        try
        {
            if (__instance == null) return false;
            __instance.gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MatchInfoHudButtonHidePatch]", ex);
        }
        return false; // 跳过原版位置调整逻辑
    }
}