using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using LightInDark.Audio;
using LightInDark.Core;
using UnityEngine;
using LightGameManager = LightInDark.Game.GameManager;

namespace Light.Patches;

/// <summary>开局播报：等待 YouAreText 出现后，替换职业开场白文本 + 替换原版职业开场音效。</summary>
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
public static class IntroPatch
{
    public static void Postfix(IntroCutscene __instance)
    {
        try
        {
            __instance.StartCoroutine(CoOverrideIntro(__instance).WrapToIl2Cpp());
        }
        catch (System.Exception)
        {
        }
    }

    private static IEnumerator CoOverrideIntro(IntroCutscene __instance)
    {
        // 等待角色揭示画面出现（YouAreText 激活），带超时保护防止死等
        float wait = 0f;
        while (__instance != null
            && (__instance.YouAreText == null || !__instance.YouAreText.gameObject.activeSelf)
            && wait < 15f)
        {
            wait += Time.deltaTime;
            yield return null;
        }

        try
        {
            if (__instance == null) yield break;

            var role = LightGameManager.Instance?.LocalPlayer?.Role;
            if (role == null) yield break;

            // ── 1. 开场白文本：非空则替换（空则保持原版文案）──
            if (__instance.RoleBlurbText != null && !string.IsNullOrEmpty(role.IntroBlurb))
            {
                var blurb = __instance.RoleBlurbText;
                blurb.text = role.IntroBlurb;
                blurb.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
                blurb.gameObject.SetActive(true);
            }

            // ── 2. 开场音效：IntroSFX 非空且资源存在则替换原版音效；否则不改，让原版音效播放 ──
            if (!string.IsNullOrEmpty(role.IntroSFX) && SfxManager.ResourceExists(role.IntroSFX))
            {
                // 先停掉原版职业开场音效（原版在 YouAreText 出现前播放 Role.IntroSound）
                var vanillaClip = PlayerControl.LocalPlayer?.Data?.Role?.IntroSound;
                if (vanillaClip != null)
                {
                    try { SoundManager.Instance.StopSound(vanillaClip); }
                    catch (System.Exception) { }
                }

                // 播放模组开场音效（首次异步解码，完成即播）
                SfxManager.Play(role.IntroSFX);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroPatch] 开场播报异常: {ex.Message}");
        }
    }
}