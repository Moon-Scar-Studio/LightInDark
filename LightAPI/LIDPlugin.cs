global using Color = LightInDark.Color;
global using UColor = UnityEngine.Color;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.UI;
using LightInDark.UI.Window;
using LightInDark.Roles;
using LightInDark.RPCs;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace LightInDark;

[BepInPlugin("cn.moonscar.lightapi", "Light in Dark","1.0.0")]
[BepInProcess("Among Us.exe")]
public partial class LIDPlugin : BasePlugin
{
    public Harmony Harmony { get; } = new("LightAPI.harmony");
    public const string Version = "0.0.1";
    public const string VisualVersion = "Dev 1.0.0";
    public const string RichVersion = "<color=#4FD1C5>ver</color> <color=#38B2AC>1.0.0</color>";
    public static string AUVersion;
    public override void Load()
    {
        // ⚠️ 把 BepInEx 日志源接到 LightLogger（非 Info 级别**同步**转发）。
        //    主插件也会接一次（覆盖这里）—— 两边都接是为了"只装 API 时也能转发"。
        try
        {
            LightLogger.BepInExInfo = m => Log.LogInfo(m);
            LightLogger.BepInExWarning = m => Log.LogWarning(m);
            LightLogger.BepInExError = m => Log.LogError(m);
        }
        catch { }
        try
        {
            Harmony.PatchAll();
            LidRpcRegistry.ScanAndPatch(Harmony);
            EventSystem.RegisterAssembly(typeof(LIDPlugin).Assembly);
            UnityEngine.SceneManagement.SceneManager.add_activeSceneChanged((Action<Scene, Scene>)((prev, next) =>
            {
                try { EventTriggers.OnSceneChanged(prev.name, next.name); }
                catch (Exception ex) { LightLogger.LogError("SceneChanged handler", ex); }
            }));
            Language.Language.Load();
            LightLogger.Log("API加载成功");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("LIDPlugin.Load", ex);
        }
    }

}
[HarmonyPatch(typeof(KeyboardJoystick),nameof(KeyboardJoystick.Update))]
public static class ShowChatPatch
{
    public static bool NeedShowFreeChat = false;
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (HudManager.Instance?.Chat == null) return;
#if !DEBUG
            if (!NeedShowFreeChat) return;
#endif
            HudManager.Instance.Chat.gameObject.SetActive(true);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("ShowChatPatch.Postfix", ex);
        }
    }
}
