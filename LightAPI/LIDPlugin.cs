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

[BepInPlugin("com.moonscar.lightapi", "Light in Dark","1.0.0")]
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
        try
        {
            Harmony.PatchAll();
            LidRpcRegistry.ScanAndPatch(Harmony);
            EventSystem.RegisterAssembly(typeof(LIDPlugin).Assembly);
            // 场景切换事件：监听 UNITY activeSceneChanged，切换后触发 EventSystem 事件
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
[HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
public static class GameManager_StartGame_Patch
{
    public static void Postfix()
    {
        try
        {
            LightLogger.Log("[游戏] 游戏开始，初始化 GameManager");
            Game.GameManager.Instance.Initialize();
            EventTriggers.OnGameStart(PlayerControl.AllPlayerControls.Count);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("GameManager_StartGame_Patch.Postfix", ex);
        }
    }
}
