using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Utilities;
using LightInDark.Core;
using UnityEngine;

namespace Light.Patches
{
    /// <summary>
    /// 调试模式 · 开局自动生成假人。
    ///
    /// 需求：调试模式下，"生成假人的数量"配置项决定开局时生成几个 AI 假人。
    ///
    /// 时机选择（关键）：
    ///   假人必须在**船只生成之前**就存在于 GameData 里，
    ///   否则原版 <c>ShipStatus</c> 分配任务/出生点时会漏掉它们，
    ///   假人会站在原地不动（DummyBehaviour 找不到任务点）。
    ///   所以挂在 <see cref="GameStartManager.BeginGame"/> 的 **Prefix**，
    ///   而不是 BeginGame 之后（那时船已经建好了）。
    ///
    /// 只用房主执行：假人是房主本地生成的，客户端不需要也无法生成。
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    public static class DummyAutoSpawnPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            try
            {
                if (!DebugMode.Enabled) return;

                int count = DebugMode.DummyCount;
                if (count <= 0) return;

                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost)
                {
                    LightLogger.Log("[DummyAutoSpawn] 非房主，跳过假人生成");
                    return;
                }

                // 上限保护：原版一局最多 15 人，别把房间挤爆
                int room = 15 - (PlayerControl.AllPlayerControls?.Count ?? 0);
                if (room <= 0)
                {
                    LightLogger.LogWarning("[DummyAutoSpawn] 房间已满，不生成假人");
                    return;
                }
                if (count > room) count = room;

                LightLogger.Log($"[DummyAutoSpawn] 开局生成 {count} 个假人");
                DummySpawner.SpawnImmediate(count);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[DummyAutoSpawnPatch.Prefix]", ex);
            }
        }
    }

    /// <summary>
    /// 备选时机：从大厅（尚未开局）手动补假人用。
    /// 挂在 <c>GameStartManager.Update</c> 上只在倒计时开始后触发一次，
    /// 给"忘了开调试模式"的场景留个后路（本轮不接 UI，仅日志）。
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    public static class DummySpawnDiagnosticsPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        public static void Postfix(GameStartManager __instance)
        {
            try
            {
                if (_logged || __instance == null) return;
                // StartingStates 是 private 枚举 → 用 int 值判断（NotStarting=0, Countdown=1, Starting=2）
                if ((int)__instance.startState == 0) return;

                _logged = true;
                LightLogger.Log($"[DummyAutoSpawn] 倒计时开始：调试模式={DebugMode.Enabled}，" +
                                $"假人数量={DebugMode.DummyCount}，" +
                                $"当前人数={PlayerControl.AllPlayerControls?.Count ?? 0}");
            }
            catch { /* 诊断失败不影响游戏 */ }
        }
    }
}
