using System;
using HarmonyLib;
using LightInDark.Core;

namespace Light.Patches;

/// <summary>
/// **房主开始游戏时自动加载"当前预设"（`Current.lidpreset`）** —— 用户 2026-10-06 要求 ✓：
/// "房主创建游戏时应该自动加载 current.lidpreset，也就是上次最后的改动" ✓
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么挂 `GameStartManager.BeginGame`】
///   它是"真正开局"的唯一入口 ✓ —— 原版倒计时归零和本工程的倒计时补丁
///   （`GameStartCountdownPatch`：注释里写明"直接调用 BeginGame()"）**都走它** ✓
///   → 挂这里不会漏 ✓（挂 `SetStartCounter` 之类只会在倒计时期间反复触发 ✗）
///
/// 【只房主做】
///   · `OnlyHost`：配置是房主权威的（同步走 `ConfigSync`）✓，客户端自己加载会**打架** ✗
///   · 加载失败**只警告、不阻止开局** ✓（宁可进游戏，也别因为一个预设文件把房主卡住 ✗）
///
/// ⚠️ 目标文件不存在时（第一次装的玩家）`LoadFrom` 会自己处理并说明 ✓，不是错误 ✓
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
internal static class AutoLoadPresetPatch
{
    /// <summary>本局是否已经加载过（BeginGame 在极端情况下可能被调多次 ✓ 只做一次）</summary>
    private static int _lastLoadedFrame = -1;

    public static void Prefix()
    {
        try
        {
            // 只房主 ✓
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

            // 同一帧重复触发 → 跳过 ✓（正常开局一帧只会调一次）
            if (_lastLoadedFrame == UnityEngine.Time.frameCount) return;
            _lastLoadedFrame = UnityEngine.Time.frameCount;

            var path = LightInDark.Configuration.PresetStore.CurrentPath;
            if (!System.IO.File.Exists(path))
            {
                LightLogger.Log($"[AutoLoadPreset] 还没有当前预设（{path}）→ 跳过");
                return;
            }

            var (ok, msg) = LightInDark.Configuration.PresetStore.LoadFrom(path);
            if (ok) LightLogger.Log($"[AutoLoadPreset] 开局前已加载上次的改动：{msg}");
            else LightLogger.LogWarning($"[AutoLoadPreset] 加载当前预设失败（不影响开局）：{msg}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[AutoLoadPreset] {ex.Message}");   // ⚠️ catch 里别碰 ex.Message 之外的字段
        }
    }
}
