using HarmonyLib;
using LightInDark.Core;
using UnityEngine;
using System;

namespace Light.Patches;

/// <summary>
/// 需要显示免费聊天时，强制显示聊天框
/// </summary>
[HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
public static class ShowChatPatch
{
    public static bool NeedShowFreeChat = false;

    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (HudManager.Instance?.Chat == null) return;
            if (!NeedShowFreeChat) return;
            HudManager.Instance.Chat.gameObject.SetActive(true);
        }
        catch (System.Exception)
        {
        }
    }
}
