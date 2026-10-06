using System;
using System.Collections.Generic;
using LightInDark.Core;

namespace LightInDark.Game
{
    /// <summary>
    /// 单次职业变化记录。
    /// </summary>
    public class RoleChangeRecord
    {
        public string FromRole { get; set; } = "";
        public string ToRole { get; set; } = "";
        /// <summary>第几轮会议时变化（0=开局分配后首次变化）</summary>
        public int MeetingNumber { get; set; }
    }

    /// <summary>
    /// 单名玩家在一局游戏中的完整数据。
    /// 参考 FS 的 FinalPlayerData。
    /// </summary>
    public class LightPlayerData
    {
        // ── 基础信息 ──
        public byte PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public int ColorId { get; set; }
        public bool IsDead { get; set; }
        public bool Disconnected { get; set; }

        // ── 职业信息 ──
        public string AssignedRoleName { get; set; } = "";
        public string FinalRoleName { get; set; } = "";
        public List<RoleChangeRecord> RoleHistory { get; set; } = new();

        // ── 死亡信息 ──
        public PlayerState? State { get; set; }
        public byte? KillerId { get; set; }
        public string KillerName { get; set; } = "";
        public int DeathMeetingNumber { get; set; } = -1;

        // ── 任务信息 ──
        public int CompletedTasks { get; set; }
        public int TotalTasks { get; set; }

        // ── 便捷属性 ──
        public bool HasRoleChanged => RoleHistory.Count > 0;

        /// <summary>获取死亡原因的中文描述</summary>
        public string GetDeathCauseText()
        {
            try
            {
                if (!IsDead && !Disconnected) return "存活";
                if (Disconnected) return "断线";
                if (!State.HasValue) return "未知";
                return State.Value switch
                {
                    PlayerState.Dead => $"被 {KillerName} 击杀",
                    PlayerState.Suicide => "自杀",
                    PlayerState.BeGuessed => $"被 {KillerName} 猜中",
                    PlayerState.BeKilled => $"被 {KillerName} 击杀",
                    PlayerState.GoOff => $"走火（{KillerName}）",
                    PlayerState.Exile => "被放逐",
                    _ => "死亡"
                };
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerData.GetDeathCauseText", ex);
                return default;
            }
        }
    }

    /// <summary>
    /// 全局玩家数据管理器（static）。
    /// 参考 FS 的 FinalPlayerData.AllPlayerData 模式。
    /// </summary>
    public static class LightPlayerDataManager
    {
        public static List<LightPlayerData> AllPlayerData { get; private set; } = new();
        public static LightPlayerData LocalPlayerData { get; private set; }
        public static int CurrentMeetingNumber { get; set; }
        public static string RoomCode { get; set; } = "";
        public static bool CrewmatesWin { get; set; }
        public static bool ImpostorsWin { get; set; }
        public static bool CustomWin { get; set; }
        public static string CustomWinnerCode { get; set; } = "";
        public static string WinReason { get; set; } = "";
        public static DateTime GameStartTime { get; set; }
        public static bool IsLocalMode { get; set; }
        public static bool IsPracticeMode { get; set; }
        public static bool AutoSaveEnabled { get; set; }

        /// <summary>游戏开始时初始化所有玩家数据</summary>
        public static void Initialize()
        {
            try
            {
                AllPlayerData.Clear();
                CurrentMeetingNumber = 0;
                CrewmatesWin = false;
                ImpostorsWin = false;
                CustomWin = false;
                CustomWinnerCode = "";
                WinReason = "";
                GameStartTime = DateTime.Now;

                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    var data = new LightPlayerData
                    {
                        PlayerId = pc.PlayerId,
                        PlayerName = pc.Data?.PlayerName ?? "Unknown",
                        ColorId = pc.Data?.DefaultOutfit.ColorId ?? 0,
                    };
                    AllPlayerData.Add(data);
                    if (pc == PlayerControl.LocalPlayer)
                        LocalPlayerData = data;
                }

                LightLogger.Log($"[PlayerData] 初始化 {AllPlayerData.Count} 名玩家数据");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.Initialize", ex);
            }
        }

        /// <summary>清除所有数据（游戏结束保存复盘后调用）</summary>
        public static void Clear()
        {
            try
            {
                AllPlayerData.Clear();
                LocalPlayerData = null;
                CurrentMeetingNumber = 0;
                RoomCode = "";
                CrewmatesWin = false;
                ImpostorsWin = false;
                CustomWin = false;
                CustomWinnerCode = "";
                WinReason = "";
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.Clear", ex);
            }
        }

        /// <summary>按 PlayerId 获取数据</summary>
        public static LightPlayerData GetData(byte playerId)
        {
            try
            {
                return AllPlayerData.Find(d => d.PlayerId == playerId);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.GetData", ex);
                return null;
            }
        }

        /// <summary>设置玩家初始分配职业</summary>
        public static void SetRole(byte playerId, string roleName)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                data.AssignedRoleName = roleName;
                data.FinalRoleName = roleName;
                LightLogger.Log($"[PlayerData] {data.PlayerName} 分配职业: {roleName}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.SetRole", ex);
            }
        }

        /// <summary>记录职业变化</summary>
        public static void ChangeRole(byte playerId, string newRole, int meetingNumber)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                var record = new RoleChangeRecord
                {
                    FromRole = data.FinalRoleName,
                    ToRole = newRole,
                    MeetingNumber = meetingNumber
                };
                data.RoleHistory.Add(record);
                data.FinalRoleName = newRole;
                LightLogger.Log($"[PlayerData] {data.PlayerName} 职业变化: {record.FromRole} → {newRole}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.ChangeRole", ex);
            }
        }

        /// <summary>记录玩家死亡</summary>
        public static void SetDeath(byte playerId, PlayerState state, byte? killerId, int meetingNumber)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                data.IsDead = true;
                data.State = state;
                data.KillerId = killerId;
                data.DeathMeetingNumber = meetingNumber;

                if (killerId.HasValue)
                {
                    var killer = GetData(killerId.Value);
                    data.KillerName = killer?.PlayerName ?? "Unknown";
                }

                LightLogger.Log($"[PlayerData] {data.PlayerName} 死亡: {state}, 凶手: {data.KillerName}");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.SetDeath", ex);
            }
        }

        /// <summary>记录玩家断线</summary>
        public static void SetDisconnected(byte playerId)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                data.Disconnected = true;
                LightLogger.Log($"[PlayerData] {data.PlayerName} 断线");
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.SetDisconnected", ex);
            }
        }

        /// <summary>更新任务进度</summary>
        public static void UpdateTaskProgress(byte playerId, int completed, int total)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                data.CompletedTasks = completed;
                data.TotalTasks = total;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.UpdateTaskProgress", ex);
            }
        }

        /// <summary>生成复盘文本</summary>
        public static string BuildReplayText()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var roomDisplay = RoomCode;
                if (IsLocalMode) roomDisplay = "本地模式";
                else if (IsPracticeMode) roomDisplay = "练习";

                sb.AppendLine($"═══ 暗中辉复盘 ═══");
                sb.AppendLine($"房间: {roomDisplay}  时间: {GameStartTime:yyyy/MM/dd HH:mm}");
                var winSide = CrewmatesWin ? "船员胜利"
                    : ImpostorsWin ? "内鬼胜利"
                    : CustomWin ? $"{CustomWinnerCode}胜利"
                    : "平局";
                sb.AppendLine($"结果: {winSide}  原因: {WinReason}");
                sb.AppendLine($"会议轮数: {CurrentMeetingNumber}");
                sb.AppendLine();

                foreach (var data in AllPlayerData)
                {
                    var status = data.GetDeathCauseText();
                    var role = string.IsNullOrEmpty(data.FinalRoleName) ? "未知" : data.FinalRoleName;
                    var taskInfo = data.TotalTasks > 0 ? $" 任务:{data.CompletedTasks}/{data.TotalTasks}" : "";
                    var changeInfo = data.HasRoleChanged ? $" (原:{data.AssignedRoleName})" : "";

                    sb.AppendLine($"{data.PlayerName} | {role}{changeInfo} | {status}{taskInfo}");

                    // 职业变化历史
                    foreach (var change in data.RoleHistory)
                        sb.AppendLine($"  └ 第{change.MeetingNumber}轮: {change.FromRole} → {change.ToRole}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("LightPlayerDataManager.BuildReplayText", ex);
                return default;
            }
        }
    }
}
