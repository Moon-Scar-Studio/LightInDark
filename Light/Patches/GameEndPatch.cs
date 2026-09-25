using System;
using System.IO;
using HarmonyLib;
using InnerNet;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Game;
using LightInDark.Roles;

namespace Light.Patches;

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
public static class GameEndPatch
{
    public static void Postfix(AmongUsClient __instance, ref EndGameResult endGameResult)
    {
        try
        {
            // 使用模组结束原因（通过 EndGameManager 自定义）。原版 GameOverReason 仅作兜底。
            // 注意：原版 Assembly-CSharp 也有全局 EndGameManager，必须全限定模组类型。
            var modReason = LightInDark.Game.EndGameManager.GetCurrentReason();
            bool impWin;
            string reasonStr;
            if (modReason != LightInDark.Game.GameEndReason.None)
            {
                impWin = LightInDark.Game.EndGameReasonHelper.IsImpostorWin(modReason);
                reasonStr = modReason.ToString();
            }
            else
            {
                var reason = endGameResult?.GameOverReason;
                impWin = reason.HasValue && IsImpostorWin(reason.Value);
                reasonStr = reason?.ToString() ?? "Unknown";
            }
            bool crewWin = !impWin;

            LightPlayerDataManager.CrewmatesWin = crewWin;
            LightPlayerDataManager.ImpostorsWin = impWin;
            LightPlayerDataManager.WinReason = reasonStr;

            // 房间号
            try
            {
                var gameId = AmongUsClient.Instance.GameId;
                LightPlayerDataManager.RoomCode = GameCode.IntToGameNameV2(gameId);
            }
            catch { LightPlayerDataManager.RoomCode = "Unknown"; }

            // 检测本地/练习模式
            LightPlayerDataManager.IsLocalMode = AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame;
            LightPlayerDataManager.IsPracticeMode = false; // AU 没有明确的练习模式标记

            // 触发 GameEndEvent
            EventTriggers.OnGameEnd(crewWin, impWin, LightPlayerDataManager.WinReason);

            // 触发胜利检查事件
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                EventTriggers.OnPlayerCheckWin(pc, LightPlayerDataManager.WinReason);
                EventTriggers.OnPlayerCheckExtraWin(pc, LightPlayerDataManager.WinReason);
                bool isWinner = (crewWin && !pc.Data.Role.IsImpostor) || (impWin && pc.Data.Role.IsImpostor);
                EventTriggers.OnPlayerBlockWin(pc, isWinner, LightPlayerDataManager.WinReason);
            }

            LightLogger.Log($"[GameEnd] 船员胜={crewWin}, 内鬼胜={impWin}, 原因={LightPlayerDataManager.WinReason}");

            // Autosave
            if (LightPlayerDataManager.AutoSaveEnabled)
            {
                SaveReplayToFile();
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>保存复盘信息到文件</summary>
    public static void SaveReplayToFile()
    {
        try
        {
            var now = DateTime.Now;
            var roomDisplay = LightPlayerDataManager.IsLocalMode ? "本地模式"
                            : LightPlayerDataManager.IsPracticeMode ? "练习"
                            : LightPlayerDataManager.RoomCode;
            var fileName = $"{now:yyyy_MM_dd_HH_mm_ss}_{roomDisplay}.txt";

            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "Replay");
            Directory.CreateDirectory(dir);
            string fullPath = Path.Combine(dir, fileName);

            var content = LightPlayerDataManager.BuildReplayText();
            File.WriteAllText(fullPath, content);
            LightLogger.Log($"[GameEnd] 复盘已保存: {fullPath}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameEnd] 保存复盘失败: {ex.Message}");
        }
    }

    /// <summary>按原版 GameOverReason 判定内鬼是否获胜。</summary>
    private static bool IsImpostorWin(GameOverReason reason)
    {
        switch (reason)
        {
            case GameOverReason.ImpostorsByVote:
            case GameOverReason.ImpostorsByKill:
            case GameOverReason.ImpostorsBySabotage:
            case GameOverReason.ImpostorDisconnect:
            case GameOverReason.HideAndSeek_ImpostorsByKills:
                return true;
            default:
                return false;
        }
    }
}
