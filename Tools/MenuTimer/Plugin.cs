using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MenuTimer;

/// <summary>
/// **独立的小测量插件**（2026-11-03 用户要求："单独写个小插件，临时禁用 LID，就打日志看进入主菜单所用时间"）。
///
/// 它**不依赖任何 LID 代码** ✓，只做三件事：
///   ① 记下"启动 → 主菜单可交互"的总时长 ✓
///   ② 逐帧记录主菜单里的帧耗时（前 20 帧全记 ✓，之后每 60 帧汇总一次 ✓）
///   ③ 找出"卡顿尖峰"（单帧 > 50ms ✓）并记录**那一刻**在做什么（场景/帧号/前后帧 ✓）
///
/// 输出：`<游戏目录>/BepInEx/plugins/MenuTimer.log`（UTF-8 ✓，同时也会进 BepInEx 日志 ✓）
/// 用法：把 MenuTimer.dll 丢进 plugins ✓，把 Light.dll / LightInDark.dll 改名成 `.off` ✓ → 启动 → 复现 ✓。
///      **开 LID / 关 LID 各跑一次**，两次的日志一比就知道卡是不是 LID 造成的 ✓。
/// </summary>
[BepInPlugin(Guid, "MenuTimer", "0.1.0")]
public sealed class MenuTimerPlugin : BasePlugin
{
    public const string Guid = "com.moonscar.menutimer";

    internal static ManualLogSource Logger = null!;
    internal static string LogPath = "";

    public override void Load()
    {
        Logger = Log;
        try
        {
            LogPath = Path.Combine(Paths.PluginPath, "MenuTimer.log");
            File.WriteAllText(LogPath, $"# MenuTimer 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", new UTF8Encoding(false));
        }
        catch { LogPath = ""; }

        BootTime = Time.realtimeSinceStartup;

        var harmony = new Harmony(Guid);
        try
        {
            harmony.PatchAll(typeof(MenuTimerPlugin).Assembly);
            Say($"[MenuTimer] 已加载（Harmony 补丁挂载完成）。日志文件：{(LogPath.Length > 0 ? LogPath : "(写文件失败，看 BepInEx 日志)")}");
        }
        catch (Exception ex)
        {
            Say($"[MenuTimer] Harmony.PatchAll 失败：{ex}");
        }
    }

    internal static float BootTime;

    /// <summary>同时写到文件和 BepInEx 日志 ✓（文件是给用户直接发的 ✓）</summary>
    internal static void Say(string line)
    {
        try { Logger.LogInfo(line); } catch { }
        try
        {
            if (LogPath.Length > 0)
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

// =====================================================================
//  场景 / 主菜单生命周期
// =====================================================================

[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Start))]
internal static class SplashStartPatch
{
    public static void Postfix()
        => MenuTimerPlugin.Say($"[阶段] SplashManager.Start  t={Time.realtimeSinceStartup:F3}s  frame={Time.frameCount}");
}

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
internal static class MainMenuStartPatch
{
    public static void Postfix()
    {
        var st = FrameStats.Instance;
        st.OnMenuStart();
        MenuTimerPlugin.Say($"[阶段] MainMenuManager.Start  t={Now():F3}s  frame={Time.frameCount}  " +
                            $"场景='{SafeScene()}'  自启动={Now() - MenuTimerPlugin.BootTime:F3}s");
    }

    private static float Now() => Time.realtimeSinceStartup;
    private static string SafeScene()
    {
        try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
    }
}

/// <summary>
/// 主菜单每帧入口：这里**只记录**，不做任何游戏逻辑 ✓（所以它自己不会造成卡顿 ✓）。
/// </summary>
[HarmonyPatch(typeof(MainMenuManager), "LateUpdate")]
internal static class MainMenuLateUpdatePatch
{
    public static void Postfix() => FrameStats.Instance.Tick();
}

// =====================================================================
//  帧耗时统计
// =====================================================================

internal sealed class FrameStats
{
    public static readonly FrameStats Instance = new();

    private const int Window = 60;              // 汇总窗口（≈1 秒）✓
    private const float SpikeMs = 50f;          // 单帧超过这个就当"尖峰" ✓
    private const int MaxSpikes = 40;           // 日志上限，避免刷屏 ✓

    private readonly float[] _deltas = new float[Window];
    private int _count;
    private int _frameInMenu;

    private float _sumMs;
    private float _maxMs;
    private int _over33;
    private int _spikesLogged;

    private bool _inMenu;

    private readonly List<string> _firstFrames = new();

    public void OnMenuStart()
    {
        _inMenu = true;
        _frameInMenu = 0;
        _count = 0;
        _sumMs = 0f; _maxMs = 0f; _over33 = 0; _spikesLogged = 0;
        _firstFrames.Clear();

        MenuTimerPlugin.Say("[统计] 开始记录主菜单帧耗时（前 20 帧逐帧 ✓，之后每 60 帧汇总 ✓）");
    }

    public void Tick()
    {
        try
        {
            if (!_inMenu) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            _frameInMenu++;

            // ① 前 20 帧逐帧记（进主界面那一下的卡顿都在这里 ✓）
            if (_frameInMenu <= 20)
            {
                _firstFrames.Add(ms.ToString("F1"));
                MenuTimerPlugin.Say($"[逐帧] #{_frameInMenu:00}  {ms:F1}ms");
            }

            // ② 尖峰
            if (ms >= SpikeMs && _spikesLogged < MaxSpikes)
            {
                _spikesLogged++;
                MenuTimerPlugin.Say($"[尖峰] frame={Time.frameCount}  {ms:F1}ms  （第 {_frameInMenu} 帧进入主菜单后）");
            }

            // ③ 窗口汇总
            _sumMs += ms;
            if (ms > _maxMs) _maxMs = ms;
            if (ms > 33.4f) _over33++;
            _count++;

            if (_count >= Window)
            {
                float avg = _sumMs / _count;
                MenuTimerPlugin.Say($"[汇总] 近 {_count} 帧：平均 {avg:F1}ms / 最大 {_maxMs:F1}ms / >33ms 的 {_over33} 帧" +
                                    $"（{(_over33 > _count * 0.3f ? "★ 卡 ✓" : "流畅 ✓")}）");
                _count = 0; _sumMs = 0f; _maxMs = 0f; _over33 = 0;
            }
        }
        catch { }
    }
}
