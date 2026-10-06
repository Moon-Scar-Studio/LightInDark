using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using LightInDark.Audio;
using LightInDark.Core;
using UnityEngine;
using LightGameManager = LightInDark.Game.GameManager;

namespace Light.Patches;

/// <summary>
/// 开局播报：替换职业开场白文本 + 替换原版职业开场音效。
///
/// ⚠️⚠️ 文本覆盖走 <see cref="IntroRoleTextDriver"/>（LateUpdate），**不是**只靠协程 ——
///   用户报的"有一帧还是原版职业"就是这么来的，根因见驱动器的注释。
/// </summary>
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
public static class IntroPatch
{
    /// <summary>当前这局的开场（驱动器与协程共用）。</summary>
    internal static IntroCutscene Current;

    public static void Postfix(IntroCutscene __instance)
    {
        try
        {
            Current = __instance;

            // ★ 关键：挂一个 LateUpdate 驱动器 —— 保证"原版写完、渲染之前"再覆盖一次（详见驱动器注释）
            if (__instance != null && __instance.gameObject != null
                && __instance.gameObject.GetComponent<IntroRoleTextDriver>() == null)
                __instance.gameObject.AddComponent<IntroRoleTextDriver>();

            __instance.StartCoroutine(CoOverrideIntro(__instance).WrapToIl2Cpp());
        }
        catch (System.Exception)
        {
        }
    }

    /// <summary>把当前玩家的职业名 / 开场白写进开场画面。可重复调用（幂等）。</summary>
    internal static void ApplyRoleText()
    {
        try
        {
            var inst = Current;
            if (inst == null) return;

            var role = LightGameManager.Instance?.LocalPlayer?.Role;
            if (role == null) return;

            // ── 1. 职业名：替换原版职业名（原版显示的是底色职业名，如"内鬼"）──
            if (inst.RoleText != null)
            {
                inst.RoleText.text = role.Name;
                inst.RoleText.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
            }

            // ── 2. 开场白文本：非空则替换（空则保持原版文案）──
            if (inst.RoleBlurbText != null && !string.IsNullOrEmpty(role.IntroText))
            {
                inst.RoleBlurbText.text = role.IntroText;
                inst.RoleBlurbText.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroPatch] 覆盖职业文本异常: {ex.Message}");
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

            // 文本本身由 LateUpdate 驱动器每帧兜住（这里只是立刻做一次，缩短空窗）
            ApplyRoleText();

            if (__instance.RoleText != null) __instance.RoleText.gameObject.SetActive(true);
            if (__instance.RoleBlurbText != null && !string.IsNullOrEmpty(role.IntroText))
                __instance.RoleBlurbText.gameObject.SetActive(true);

            // ── 3. 开场音效：IntroSFX 非空且资源存在则替换原版音效；否则不改，让原版音效播放 ──
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

/// <summary>
/// 开场角色揭示画面的**文本守卫**：每帧在 **LateUpdate** 里重新覆盖职业名 / 开场白，
/// 挂在 <see cref="IntroCutscene"/> 自己的 GameObject 上（随它一起销毁，不会跨局残留）。
///
/// ⚠️⚠️ 为什么必须是 LateUpdate（用户 2026-10-06 报的"有一帧还是原版职业"的**根因**）：
///
///   原版 `IntroCutscene.ShowRole()` 是一个**协程**，它在里面做：
///   <code>
///     RoleText.text      = 原版职业名;      // 19.0 反编译源码 L324
///     RoleBlurbText.text = 原版职业描述;    // L325
///     YouAreText/RoleText/RoleBlurbText.gameObject.SetActive(true);   // L330-332 → 这一帧开始可见
///     yield return new WaitForSeconds(2.5f);                          // L342 → 画面持续 2.5 秒
///   </code>
///
///   而 **Unity 协程的恢复顺序 = 启动先后**。我们的协程是在 `CoBegin` 的**后置**里启动的，
///   **比原版的 `ShowRole` 更早** ✗ → 于是每一帧都是「我们先跑、原版后写」：
///   我们查 `YouAreText.activeSelf` 时原版这一帧还没写 → 只能等到**下一帧**才发现 → 覆盖晚一帧
///   → **至少露出 1 帧原版职业名**（就是用户看到的现象）。
///
///   LateUpdate 的位置正好是「本帧所有协程都已跑完 + **本帧还没渲染**」✓
///   → 在这里覆盖 = 原版文本**一帧都不会被渲染出来** ✓
///   （画面有 2.5 秒，足够我们每帧覆盖，开销只是两个字符串赋值 + 颜色 ✓）
///
///   ⚠️ AGENTS §11.2：自建 MonoBehaviour 必须先 ClassInjector 注册，否则 AddComponent 抛
///      TypeInitializationException（本工程已经踩过两次：LightTicker / BassMusicPlayer）。
/// </summary>
public class IntroRoleTextDriver : MonoBehaviour
{
    static IntroRoleTextDriver()
    {
        try { ClassInjector.RegisterTypeInIl2Cpp<IntroRoleTextDriver>(); }
        catch (Exception ex) { LightLogger.LogWarning($"[IntroRoleTextDriver] 注册失败: {ex.Message}"); }
    }

    private void LateUpdate()
    {
        try
        {
            var inst = IntroPatch.Current;
            if (inst == null) return;

            // 只在"角色揭示画面显示期间"覆盖（原版 SetActive(false) 之后就别碰了）
            if (inst.RoleText == null || !inst.RoleText.gameObject.activeSelf) return;

            IntroPatch.ApplyRoleText();
        }
        catch { }
    }
}