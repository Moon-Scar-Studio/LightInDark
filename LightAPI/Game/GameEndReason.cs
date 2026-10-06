namespace LightInDark.Game
{
    /// <summary>
    /// 模组自定义游戏结束原因。
    /// 原版 <c>GameOverReason</c> 已弃用，统一使用此枚举驱动结算/复盘/事件。
    /// 各模组角色/逻辑可通过 <see cref="EndGameManager.TryEndGame"/> 以自定义原因结束一局。
    /// </summary>
    public enum GameEndReason
    {
        /// <summary>未指定 / 兜底</summary>
        None = 0,

        /// <summary>船员完成任务获胜</summary>
        CrewmatesByTasks = 1,

        /// <summary>船员投票放逐全部内鬼获胜</summary>
        CrewmatesByVote = 2,

        /// <summary>内鬼击杀足够船员获胜</summary>
        ImpostorsByKill = 3,

        /// <summary>内鬼投票放逐船员获胜</summary>
        ImpostorsByVote = 4,

        /// <summary>破坏系统（反应堆/O2）崩溃获胜</summary>
        ImpostorsBySabotage = 5,

        /// <summary>内鬼断线/离开获胜</summary>
        ImpostorsByDisconnect = 6,

        /// <summary>船员断线/离开获胜</summary>
        CrewmatesByDisconnect = 7,

        Invalid = 8,

        CustomWin = 9,

        // ---- 模组扩展结束原因（自定义）----

        /// <summary>第三方/自定义原因占位起始值以外，自定义结束时使用高位值。</summary>
        CustomFirst = 100,

        // 示例：玩家自由扩展
        // CustomTeamWin = 101,
        // CustomJesterWin = 102,
    }

    /// <summary>判断某结束原因是内鬼获胜还是船员获胜。</summary>
    public static class EndGameReasonHelper
    {
        public static bool IsImpostorWin(GameEndReason reason)
        {
            switch (reason)
            {
                case GameEndReason.ImpostorsByKill:
                case GameEndReason.ImpostorsByVote:
                case GameEndReason.ImpostorsBySabotage:
                case GameEndReason.ImpostorsByDisconnect:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsCustomWin(GameEndReason reason) => reason == GameEndReason.CustomWin;

        /// <summary>判断结束原因是否来自模组扩展（自定义）。</summary>
        public static bool IsCustom(GameEndReason reason) => (int)reason >= (int)GameEndReason.CustomFirst;
    }
}
