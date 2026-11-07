using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using LightInDark.Audio;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using UnityEngine;
using LightGameManager = LightInDark.Game.GameManager;

namespace Light.Patches;

/// <summary>
/// 开局播报：替换职业名 / 开场白 / 阵营标题，替换开场音效，并启动开场演出。
/// 文本覆盖由 <see cref="IntroRoleTextDriver"/> 在 LateUpdate 每帧兜底。
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

            // ★ 关键：挂一个 LateUpdate 驱动器 —— 保证"原版写完、渲染之前"再覆盖一次
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

            ApplyTeamTitle(role);

            // 职业名：值相同就不写，TMP 每次赋值会触发重排，也会打断打字机效果
            if (inst.RoleText != null)
            {
                if (inst.RoleText.text != role.Name) inst.RoleText.text = role.Name;
                inst.RoleText.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
            }

            // 开场白：为空时保留原版文案
            if (inst.RoleBlurbText != null && !string.IsNullOrEmpty(role.IntroText))
            {
                if (inst.RoleBlurbText.text != role.IntroText) inst.RoleBlurbText.text = role.IntroText;
                inst.RoleBlurbText.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
            }

            // "你的职业是"那行也染职业色，否则与上方职业名颜色不一致
            if (inst.YouAreText != null)
                inst.YouAreText.color = LightInDark.ColorHelper.ToUnityColor(role.Color);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroPatch] 覆盖职业文本异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 团队揭示阶段的阵营标题：中立职业显示「中立」+ 职业色，并把背景横幅一起染色。
    /// 中立的底层职业已被校正为船员，不覆盖会显示成「船员」。
    /// </summary>
    internal static void ApplyTeamTitle(RuntimeRoleTemplate role)
    {
        try
        {
            var inst = Current;
            if (inst == null || inst.TeamTitle == null) return;
            if (role.RoleCategory != RoleCategory.Neutral) return;

            var color = LightInDark.ColorHelper.ToUnityColor(role.Color);

            inst.TeamTitle.text = LightInDark.Language.Language.Translate("role.category.neutral", "中立");
            inst.TeamTitle.color = color;

            // 背景横幅也染阵营色（原版船员=青 / 内鬼=红；NOS CoShowTeam :96 同款）
            try
            {
                if (inst.BackgroundBar != null && inst.BackgroundBar.material != null)
                    inst.BackgroundBar.material.SetColor("_Color", color);
            }
            catch { }
        }
        catch { }
    }

    private static IEnumerator CoOverrideIntro(IntroCutscene __instance)
    {
        // 等待两件事：角色揭示画面出现 + 本地职业已下发。
        // 职业由房主 RPC 下发，可能晚于开场动画，先等再播演出。
        LightInDark.Roles.RuntimeRoleTemplate role = null;
        float wait = 0f;
        while (wait < 15f)
        {
            if (__instance == null) yield break;

            bool textReady = __instance.YouAreText != null && __instance.YouAreText.gameObject.activeSelf;
            role = LightGameManager.Instance?.LocalPlayer?.Role;
            if (textReady && role != null) break;

            wait += Time.deltaTime;
            yield return null;
        }

        if (__instance == null) yield break;
        if (role == null)
        {
            LightLogger.LogWarning("[IntroPatch] 开场等待 15 秒仍未拿到本地职业，跳过演出");
            yield break;
        }

        // C# 迭代器限制：try 块含 catch 时不能 yield return —— 演出调用放在 try 外
        try
        {
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

        if (role == null || __instance == null) yield break;

        // 开场演出已停用（保留实现，需要时取消注释即可恢复）
        // if (IntroRoleTextDriver.RevealStarted) yield break;
        // IntroRoleTextDriver.RevealStarted = true;
        // yield return IntroCinematics.PlayRoleReveal(__instance, role);
    }
}

/// <summary>
/// 中立职业的团队名单只显示自己（NOS `TeamRevealType.OnlyMe` 风格）。
/// 原版按底层职业调 BeginCrewmate（中立底层已被校正为 Crewmate），
/// 默认会把**全部船员**列出来 —— 中立没有队友，只列自己 ✓
/// </summary>
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate))]
public static class NeutralIntroTeamPatch
{
    public static void Prefix(List<PlayerControl> teamToDisplay)
    {
        try
        {
            if (teamToDisplay == null) return;

            var role = LightGameManager.Instance?.LocalPlayer?.Role;
            if (role == null || role.RoleCategory != RoleCategory.Neutral) return;

            var me = PlayerControl.LocalPlayer;
            if (me == null) return;

            teamToDisplay.Clear();
            teamToDisplay.Add(me);
        }
        catch { }
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

    /// <summary>本局扫光是否已播（团队阶段只播一次）。</summary>
    internal static bool SweepPlayed;

    /// <summary>本局角色演出是否已启动（只播一次）。</summary>
    internal static bool RevealStarted;

    private void LateUpdate()
    {
        try
        {
            var inst = IntroPatch.Current;
            if (inst == null) return;

            // 团队揭示阶段（角色名还没出现）：中立阵营标题覆盖
            if (inst.TeamTitle != null && inst.TeamTitle.gameObject.activeInHierarchy)
            {
                var teamRole = LightGameManager.Instance?.LocalPlayer?.Role;
                if (teamRole != null) IntroPatch.ApplyTeamTitle(teamRole);

                // 开场演出已停用（扫光）
                // if (!SweepPlayed)
                // {
                //     SweepPlayed = true;
                //     inst.StartCoroutine(IntroCinematics.PlayTeamSweep(inst).WrapToIl2Cpp());
                // }
            }

            // 只在"角色揭示画面显示期间"覆盖（原版 SetActive(false) 之后就别碰了）
            if (inst.RoleText == null || !inst.RoleText.gameObject.activeSelf) return;

            IntroPatch.ApplyRoleText();

            // 开场演出已停用（角色揭示演出）
            // if (!RevealStarted)
            // {
            //     var role = LightGameManager.Instance?.LocalPlayer?.Role;
            //     if (role != null)
            //     {
            //         RevealStarted = true;
            //         inst.StartCoroutine(IntroCinematics.PlayRoleReveal(inst, role).WrapToIl2Cpp());
            //     }
            // }
        }
        catch { }
    }

    private void OnDestroy()
    {
        // 开场演出已停用；恢复演出时需一并取消注释（用于强制恢复 timeScale 与清理临时对象）
        // try { IntroCinematics.CleanupAll(); } catch { }
        SweepPlayed = false;
        RevealStarted = false;
    }
}