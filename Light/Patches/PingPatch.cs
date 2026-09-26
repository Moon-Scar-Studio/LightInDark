using AmongUs.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;

namespace Light.Patches;

[HarmonyPatch(typeof(PingTracker), "Update")]
public static class BetterPingTrackerPatch
{
    private static int _lastFps = -1;
    private static float _lastFpsUpdateTime = -1f;

    public static void Postfix(PingTracker __instance)
    {
        string ping = __instance.text.text;
        StringBuilder sb = new StringBuilder();

        foreach (char c in ping)
        {
            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
        }

        string pingNum = sb.ToString();
        int value = 0;

        if (!string.IsNullOrEmpty(pingNum))
        {
            int.TryParse(pingNum, out value);
        }

        Color color = GetValueColor(value, 0, 1000);
        string hex = ColorUtility.ToHtmlStringRGB(color);
        string pingText = $"<color=#{hex}>PING:{value}ms</color>";

        bool isChinese = DataManager.Settings.Language.CurrentLanguage == SupportedLangs.SChinese
            || DataManager.Settings.Language.CurrentLanguage == SupportedLangs.TChinese;


        int fps = (int)(1f / Time.smoothDeltaTime);
        float now = Time.time;

        if (_lastFps < 0)
        {
            _lastFps = fps;
            _lastFpsUpdateTime = now;
        }
        else if (Mathf.Abs(fps - _lastFps) > 1)
        {
            _lastFps = fps;
            _lastFpsUpdateTime = now;
        }
        else
        {
            float interval = Mathf.Lerp(3f, 1f, Mathf.Clamp01((float)_lastFps / 60f));

            if (now - _lastFpsUpdateTime >= interval)
            {
                _lastFps = fps;
                _lastFpsUpdateTime = now;
            }
        }

        fps = _lastFps;

        Color fpsColor = GetInverseValueColor(fps, 0, 60);
        string hexFps = ColorUtility.ToHtmlStringRGB(fpsColor);
        string fpsText = $"<color=#{hexFps}>FPS - {fps}</color>";

        __instance.text.text = $"<size=110%><color=#F5D48A>Light In Dark {LightPlugin.VisualVersion}</color></size><size=90%> by Moon-Scar 制作组</size>\n<color=red>模组社群:1101385982</color>\n{pingText} | {fpsText}";
        __instance.text.alignment = TextAlignmentOptions.TopRight;
        var pos = __instance.GetComponent<AspectPosition>();
        if(pos == null) __instance.gameObject.AddComponent<AspectPosition>();
        pos?.Alignment = AspectPosition.EdgeAlignments.RightTop;
        float offsetX = 1.8f;
        if (HudManager.InstanceExists && HudManager.Instance.Chat.chatButton.gameObject.active) offsetX = 2.5f;
        pos?.DistanceFromEdge = new Vector3(offsetX, 0f, -800f);
        pos?.updateAlways = true;
        bool shouldHide = false;

        // 会议中隐藏（用激活状态判断，避免 MeetingHud.Instance 残留引用导致会议结束后不恢复）
        try
        {
            var mh = MeetingHud.Instance;
            shouldHide |= mh != null && mh.isActiveAndEnabled;
        }
        catch { } // 对象已销毁：视为不在会议

        // 设置菜单/好友列表打开时隐藏
        try { shouldHide |= GameSettingMenu.Instance?.gameObject.active ?? false; }
        catch { }
        try { shouldHide |= FriendsListUI.Instance?.gameObject.active ?? false; }
        catch { }

        __instance.text.gameObject.SetActive(!shouldHide);
    }

    private static Color GetValueColor(int val, int min = 0, int max = 1000)
    {
        Color green = new Color(0.2f, 1f, 0.1f);
        Color red = new Color(1f, 0.1f, 0.1f);

        if (val < min)
        {
            return green;
        }

        if (val > max)
        {
            return red;
        }

        if (min == max)
        {
            return green;
        }

        float t = (float)(val - min) / (max - min);
        return Color.Lerp(green, red, t);
    }

    private static Color GetInverseValueColor(int val, int min, int max)
    {
        Color green = new(0.2f, 1f, 0.1f);
        Color red = new(1f, 0.1f, 0.1f);

        if (val <= min)
        {
            return red;
        }

        if (val >= max)
        {
            return green;
        }

        float t = (float)(val - min) / (max - min);
        return Color.Lerp(red, green, t);
    }
}