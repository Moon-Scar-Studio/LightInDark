using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// 会议结束黑屏兜底：放逐流程结束后检查原版 <c>HudManager.FullScreen</c>，
/// 若仍是接近不透明的黑（黑幕没随原版协程退掉）就复位透明。
/// 原版流程正常时这里不会写入任何东西（纯差量）。
/// </summary>
[HarmonyPatch(typeof(ExileController), nameof(ExileController.Begin))]
public static class MeetingBlackScreenFixPatch
{
    public static void Postfix(ExileController __instance)
    {
        try
        {
            if (__instance != null)
                __instance.StartCoroutine(CoFixAfterExile().WrapToIl2Cpp());
        }
        catch (System.Exception ex)
        {
            LightLogger.LogWarning($"[BlackScreenFix] 启动失败: {ex.Message}");
        }
    }

    private static IEnumerator CoFixAfterExile()
    {
        // 等放逐控制器销毁（上限 30 秒，防卡死）
        float wait = 0f;
        while (ExileController.Instance != null && wait < 30f)
        {
            wait += Time.deltaTime;
            yield return null;
        }

        yield return null;   // 再等一帧让原版收尾动画落地

        var hud = HudManager.Instance;
        if (hud == null || hud.FullScreen == null) yield break;

        var c = hud.FullScreen.color;
        if (c.a > 0.95f && c.r < 0.05f && c.g < 0.05f && c.b < 0.05f)
        {
            hud.FullScreen.color = new Color(c.r, c.g, c.b, 0f);
            LightLogger.Log("[BlackScreenFix] 会议后检测到残留黑幕，已复位 FullScreen");
        }
    }
}
