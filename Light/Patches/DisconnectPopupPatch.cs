using HarmonyLib;
using LightInDark.Core;
using UnityEngine;
using static LightInDark.Utilities.LightUtils;

namespace Light.Patches
{
    [HarmonyPatch(typeof(DisconnectPopup), nameof(DisconnectPopup.SetText))]
    public static class DisconnectPopupPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(DisconnectPopup __instance)
        {
            try
            {
                if (string.IsNullOrEmpty(KickManager.kickReason))
                    return true;
                float now = Time.realtimeSinceStartup;
                if (KickManager.kickReasonConsumeUntil > 0f)
                {
                    if (now > KickManager.kickReasonConsumeUntil)
                    {
                        KickManager.Clear();
                        return true;
                    }
                }
                else if (now > KickManager.kickReasonWaitUntil)
                {
                    KickManager.Clear();
                    return true;
                }
                else
                {
                    KickManager.kickReasonConsumeUntil = now + 3f;
                }
                __instance._textArea.text = KickManager.kickReason;
                __instance.OnTextChanged();
                return false;
            }
            catch (System.Exception)
            {
                return true;
            }
        }
    }
}