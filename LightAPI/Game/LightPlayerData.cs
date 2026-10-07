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

        /// <summary>
        /// **死因 id**（模组可自定义 ✓，照 Nebula 的 `PlayerStates` 做法 —— 见 <see cref="DeathCause"/>）。
        /// 空 = 还没记（显示时退回 <see cref="State"/> 那套老文案 ✓）
        /// </summary>
        public string DeathCauseId { get; set; } = "";

        /// <summary>
        /// **额外死因记录**（照抄 Nebula 的 `ExtraDeadInfo` ✓）：
        /// 一个玩家可能"被击杀 + 被诅咒"之类，主死因只能写一个，其余的记在这里 ✓
        /// </summary>
        public List<string> ExtraDeathCauses { get; set; } = new();

        // ── 任务信息 ──
        public int CompletedTasks { get; set; }
        public int TotalTasks { get; set; }

        // ── 便捷属性 ──
        public bool HasRoleChanged => RoleHistory.Count > 0;

        /// <summary>
        /// 获取死亡原因的中文描述。
        ///
        /// ⚠️ 顺序（照 Nebula 的做法 ✓）：
        ///   ① **自定义死因优先** —— 模组/职业通过 <see cref="DeathCause.Register"/> 注册的文本 ✓
        ///   ② 没注册就退回 `PlayerState` 那套老文案 ✓（保证不影响任何现有行为 ✓）
        /// </summary>
        public string GetDeathCauseText()
        {
            try
            {
                if (!IsDead && !Disconnected) return "存活";
                if (Disconnected) return DeathCause.Resolve(DeathCause.Disconnected) ?? "断线";

                string killer = string.IsNullOrEmpty(KillerName) ? "" : KillerName;

                // ① 自定义死因（注册过就用它 ✓）
                string custom = DeathCause.Resolve(DeathCauseId, killer);
                if (!string.IsNullOrEmpty(custom)) return custom;

                if (!State.HasValue) return "未知";

                // ② 老文案兜底 ✓
                string ByKill() => killer.Length > 0 ? $"被 {killer} 击杀" : "被击杀";

                return State.Value switch
                {
                    PlayerState.Dead => ByKill(),
                    PlayerState.Suicide => "自杀",
                    PlayerState.BeGuessed => killer.Length > 0 ? $"被 {killer} 猜中" : "被猜中",
                    PlayerState.BeKilled => ByKill(),
                    PlayerState.GoOff => killer.Length > 0 ? $"走火（{killer}）" : "走火",
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

        /// <summary>
        /// 记录玩家死亡。
        /// ⚠️ `causeId` 是**自定义死因 id**（模组/职业可以传自己的 ✓，见 <see cref="DeathCause"/>）——
        ///    不传（null/空）时自动按 <paramref name="state"/> 映射成内置 id ✓
        ///    所以**每一次死亡都会有 id** ✓ 显示时不再依赖枚举文案 ✓
        /// </summary>
        public static void SetDeath(byte playerId, PlayerState state, byte? killerId, int meetingNumber,
                                    string causeId = null, string extraCauseId = null)
        {
            try
            {
                var data = GetData(playerId);
                if (data == null) return;
                data.IsDead = true;
                data.State = state;
                data.KillerId = killerId;
                data.DeathMeetingNumber = meetingNumber;

                // ★ 死因 id：自定义优先，否则由枚举映射而来 ✓（枚举只用来"兜底"，不再决定文案 ✓）
                data.DeathCauseId = string.IsNullOrEmpty(causeId)
                    ? DeathCause.FromPlayerState(state)
                    : causeId;

                if (!string.IsNullOrEmpty(extraCauseId))
                    data.ExtraDeathCauses.Add(extraCauseId);

                if (killerId.HasValue)
                {
                    var killer = GetData(killerId.Value);
                    data.KillerName = killer?.PlayerName ?? "Unknown";
                }

                LightLogger.Log($"[PlayerData] {data.PlayerName} 死亡: {state} 死因={data.DeathCauseId}, 凶手: {data.KillerName}");
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

        /// <summary>
        /// 把持久数据里的**职业内部名（CodeName）**翻成当前语言的显示名。
        ///
        /// ⚠️ 审查 A16：存储从"本地化显示名"改成 CodeName 之后，
        ///    **所有**显示点都必须过这一层 —— 否则界面上会直接看到 `caller` 这种内部名 ✗
        ///    （好处：两个职业译名相同也不会混淆、换语言后历史照样能正确翻译、
        ///      而且随时能反查回 `RoleTemplate` ✓）
        /// </summary>
        public static string DisplayRole(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return "";
            try
            {
                var role = Roles.RoleRegistry.GetByName(codeName);
                if (role != null) return role.Name;      // 已注册 → 当前语言的名字 ✓
            }
            catch { }
            return codeName;                              // 注册表里没有（旧存档/第三方职业）→ 原样显示 ✓
        }

        /// <summary>
        /// 没有自定义职业时，用**原版底色职业**兜底显示（用户 2026-10-06 报"/replay 里假人职业全是未知"）。
        ///
        /// ⚠️ 为什么假人会是空的：分配器**刻意跳过假人**（`isDummy` 过滤 ✓），
        ///    所以它们的 `AssignedRoleName` / `FinalRoleName` 从来没被写过 ✗
        ///    → 复盘里显示"未知"，看起来像 Bug ✓
        ///    它们其实有原版底色职业（`RoleManager` 发的 `Crewmate`/`Impostor` ✓），读出来显示即可 ✓
        ///
        /// 取不到就返回"未知"（真的查不到时才说未知 ✓）
        /// </summary>
        private static string FallbackRoleName(LightPlayerData data)
        {
            try
            {
                // ⚠️ 本文件没有 `using System.Linq;` → 手写循环，别用 FirstOrDefault ✗
                PlayerControl? pc = null;
                var all = PlayerControl.AllPlayerControls;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var p = all[i];
                        if (p != null && p.PlayerId == data.PlayerId) { pc = p; break; }
                    }
                }

                var roleType = pc?.Data?.Role?.Role;
                if (roleType != null)
                {
                    switch (roleType.Value)
                    {
                        case AmongUs.GameOptions.RoleTypes.Crewmate: return "船员";
                        case AmongUs.GameOptions.RoleTypes.Impostor: return "内鬼";
                        case AmongUs.GameOptions.RoleTypes.CrewmateGhost: return "船员（幽灵）";
                        case AmongUs.GameOptions.RoleTypes.ImpostorGhost: return "内鬼（幽灵）";
                        case AmongUs.GameOptions.RoleTypes.Engineer: return "工程师";
                        case AmongUs.GameOptions.RoleTypes.Scientist: return "科学家";
                        case AmongUs.GameOptions.RoleTypes.Shapeshifter: return "变形者";
                        case AmongUs.GameOptions.RoleTypes.Phantom: return "魅影";
                        case AmongUs.GameOptions.RoleTypes.Tracker: return "追踪者";
                        case AmongUs.GameOptions.RoleTypes.Noisemaker: return "噪音制造者";
                        case AmongUs.GameOptions.RoleTypes.Detective: return "侦探";
                        case AmongUs.GameOptions.RoleTypes.GuardianAngel: return "守护天使";
                        default: return roleType.Value.ToString();      // 兜底：枚举名（总比"未知"有信息量 ✓）
                    }
                }

                // 原版职业也读不到（玩家已不在场上）→ 至少把"是不是假人"说清楚 ✓
                if (pc != null && pc.isDummy) return "假人";
            }
            catch { }
            return "未知";
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
                    // ★ 审查 A16：持久数据里存的是 **CodeName（内部名）**，显示前必须翻成当前语言的名字 ✓
                    //   （否则复盘里会直接看到 `caller` 这种内部名 ✗）
                    // ★ 2026-10-06 用户报「/replay 输出假人职业全是未知」——
                    //   假人**不会**被分配自定义职业（分配器刻意跳过它们 ✓），所以这里存的是空串 ✗
                    //   → 空的时候退回去读**原版底色职业**（船员/内鬼…），而不是一律显示"未知" ✓
                    var role = string.IsNullOrEmpty(data.FinalRoleName)
                        ? FallbackRoleName(data)
                        : DisplayRole(data.FinalRoleName);
                    var taskInfo = data.TotalTasks > 0 ? $" 任务:{data.CompletedTasks}/{data.TotalTasks}" : "";
                    var changeInfo = data.HasRoleChanged
                        ? $" (原:{DisplayRole(data.AssignedRoleName)})"
                        : "";

                    sb.AppendLine($"{data.PlayerName} | {role}{changeInfo} | {status}{taskInfo}");

                    // 职业变化历史
                    foreach (var change in data.RoleHistory)
                        sb.AppendLine($"  └ 第{change.MeetingNumber}轮: {DisplayRole(change.FromRole)} → {DisplayRole(change.ToRole)}");
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
