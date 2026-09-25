using HarmonyLib;
using Light;
using LightInDark.Core;
using TMPro;
using System;
namespace Light.Patches;

[HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start))]
public static class VersionPatch
{
    public static void Postfix(VersionShower __instance)
    {
        try
        {
            LightPlugin.AUVersion = __instance.text.text;
            string auVer = __instance.text.text;
            int i = auVer.IndexOf('(');
            if (i >= 0) auVer = auVer[..i].Trim();
            string lightText = "<color=#D3C678>L</color><color=#DCCF86>i</color>" +
                               "<color=#CABC6E>g</color><color=#D5C87C>h</color><color=#C0B361>t</color>";
            string inText = "<color=#4FD1C5>I</color><color=#38B2AC>n</color>";
            string darkText = "<color=#9F7AEA>D</color><color=#805AD5>a</color>" +
                              "<color=#6B46C1>r</color><color=#553C9A>k</color>";
            string modVersion = $"{lightText} {inText} {darkText} - {LightPlugin.RichVersion}";
            auVer = "  Among Us " + auVer;

            __instance.text.alignment = TextAlignmentOptions.Left;
            __instance.text.text = auVer;

            // 原
            var vs = __instance.gameObject;
            var ap = vs.GetComponent<AspectPosition>();
            ap.Alignment = AspectPosition.EdgeAlignments.LeftBottom;
            ap.DistanceFromEdge = new Vector3(2.369f, -0.3f);
            ap.updateAlways = true;

            // mod ver
            var clone = UnityEngine.Object.Instantiate(__instance.text, __instance.transform.parent);
            clone.name = "LightModVersion";
            clone.text = modVersion;
            clone.alignment = TextAlignmentOptions.Left;
            clone.fontSize = 2.8f;
            clone.alpha = 0.8f;
            var apClone = clone.gameObject.AddComponent<AspectPosition>();
            apClone.Alignment = AspectPosition.EdgeAlignments.LeftBottom;
            apClone.DistanceFromEdge = new Vector3(2.369f,-0.45f);
            apClone.updateAlways = true;

        }
        catch (Exception)
        {
        }
    }
}


//public static class ModVersionShow
//{
//    static TextMeshPro _verText;
//    static AspectPosition _verAspect;
//    public static string VersionTextContent = "我是测试。\n我是测试二号。";

//    [HarmonyPatch(typeof(PingTracker),nameof(PingTracker.Update))]
//    public static class PingTrackerUpdatePatch
//    {
//        [HarmonyPostfix]
//        public static void PingTracker_ModVersionPostfix(PingTracker __instance)
//        {
//            if (_verText == null)
//            {
//                var tmp = __instance.text;
//                var newText = UnityEngine.Object.Instantiate(tmp,tmp.transform.parent);
//                newText.name = "LIDModVersion";
//                newText.alignment = TextAlignmentOptions.TopRight;
//                newText.fontSize = 2.5f;
//                newText.text = VersionTextContent;

//                _verAspect = newText.gameObject.AddComponent<AspectPosition>();
//                _verAspect.Alignment = AspectPosition.EdgeAlignments.RightTop;
//                _verAspect.DistanceFromEdge =
//                    HudManager.InstanceExists && HudManager.Instance.Chat.chatButton.gameObject.active? new Vector3(2.5f, 0f, -800f): new Vector3(1.8f, 0f, -800f);
//                _verAspect.updateAlways = true;
//                _verText = newText;
//            }
//            if (_verAspect != null)
//            {
//                float offsetY = 0f;
//                if (HudManager.InstanceExists && HudManager.Instance.Chat.chatButton.gameObject.active)
//                    offsetY = -0.5f;
//                _verAspect.DistanceFromEdge = new Vector3(1.8f, offsetY, -800f);
//            }

//            bool shouldHide = GameSettingMenu.Instance?.gameObject.active ?? false;
//            _verText?.gameObject.SetActive(!shouldHide);
//        }
//    }
//}