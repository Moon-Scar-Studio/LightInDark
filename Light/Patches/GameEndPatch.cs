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
            bool invalid = modReason == LightInDark.Game.GameEndReason.Invalid;
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
            bool crewWin = !invalid && !impWin;
            impWin = !invalid && impWin;

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

            // ★ 换局回收（2026-10-06 审查 #5/#6）：
            //   原来这里只发事件 + 存复盘，**完全不碰职业状态** →
            //   回大厅后上一局的 RuntimeRoleTemplate 仍然 IsActive=true、
            //   名字上的职业信息与职业按钮都还在（直到下一局 SetRole 才被顺带清掉），
            //   而且跨局持有已销毁的场景对象；RoleButtonManager.Clear() 全工程无人调用 = 死代码 ✗
            //   更彻底的那一半（Player.Release + 事件注销）由 GameManager.Initialize() 幂等化后负责：
            //   它会 Release 掉已销毁的旧实体 ✓
            CleanupRolesAtGameEnd();

            // Autosave
            if (LightPlayerDataManager.AutoSaveEnabled)
            {
                SaveReplayToFile();
            }
        }
        catch (Exception ex)
        {
            // ⚠️ 原来是空 catch（AGENTS §11.6）：整局结束这条链路坏了会一个字都不留 ✗
            LightLogger.LogError("[GameEndPatch.Postfix]", ex);
        }
    }

    /// <summary>整局结束：把所有人的职业失活（触发 OnInactivated + Release）+ 清空职业按钮。</summary>
    private static void CleanupRolesAtGameEnd()
    {
        try
        {
            int players = 0, roles = 0;
            foreach (var p in LightInDark.Game.GameManager.Instance.AllPlayers)
            {
                if (p == null) continue;
                players++;
                try
                {
                    var role = p.Role;
                    if (role == null || !role.IsActive) continue;
                    role.Inactivate();      // → OnInactivated() + Release()（注销事件、回收按钮、还原名字颜色）
                    roles++;
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[GameEndPatch] 失活职业失败: {ex.Message}");
                }
            }

            try { LightInDark.UI.Ability.RoleButtonManager.Clear(); } catch { }

            // ★ 卸载所有**实例**监听（审查 #5）：Player 包装与 RuntimeRoleTemplate 构造时都会
            //   RegisterInstance(this)，但换局从来不 Detach → _attached/_listeners 里永久累积历史对象，
            //   派发越来越慢，第二局还会跑上一局残留的职业事件。静态处理器不受影响 ✓
            try { LightInDark.Events.EventSystem.DetachAllInstances(); } catch { }

            LightLogger.Log($"[GameEndPatch] 换局回收完成：{players} 名玩家、{roles} 个职业已失活，职业按钮已清空");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[GameEndPatch.CleanupRolesAtGameEnd] {ex.Message}");
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
