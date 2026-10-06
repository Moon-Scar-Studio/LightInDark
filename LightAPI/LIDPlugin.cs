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

            // ★ 也扫一遍 **API 程序集**里的职业（2026-10-06 审查 #18）：
            //   原来 `Load()` 只对 API 程序集注册了**事件**，没注册**职业** →
            //   写在 LightInDark 程序集里的 `RoleTemplate` 子类会被**静默忽略**
            //   （不进 AllRoles、不出配置块、帮助页也不显示；`SetRole` 收到它的 Id 只打一行"未知角色Id"）✗
            //   注册是幂等的（按类型去重），重复调用无副作用 ✓
            try { Roles.RoleRegistry.RegisterAssembly(typeof(LIDPlugin).Assembly); }
            catch (Exception ex) { LightLogger.LogError("[LIDPlugin] API 程序集职业注册失败", ex); }

            UnityEngine.SceneManagement.SceneManager.add_activeSceneChanged((Action<Scene, Scene>)((prev, next) =>
            {
                try { EventTriggers.OnSceneChanged(prev.name, next.name); }
                catch (Exception ex) { LightLogger.LogError("SceneChanged handler", ex); }

                // ★ 回到主菜单 = 已经离开房间 → 清空职业预定（2026-10-06 审查 B10 的剩余部分）
                //   `RolePinManager._pins` 原来**跨房间残留**：退出房间/换房后旧预定还在，
                //   配合 PlayerId 复用就会出现"我明明没预定，怎么强制给了我一个职业"✗
                //   （单局内的"阵营不符则保留到下一局"语义不受影响 —— 那只发生在同一房间内 ✓）
                try
                {
                    if (next.name == "MainMenu") Roles.Assignment.RolePinManager.Clear();
                }
                catch (Exception ex) { LightLogger.LogWarning($"[LIDPlugin] 清空职业预定失败：{ex.Message}"); }
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
