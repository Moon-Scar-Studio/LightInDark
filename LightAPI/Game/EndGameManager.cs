using HarmonyLib;
using LightInDark.Core;

namespace LightInDark.Game
{
    /// <summary>
    /// 模组结束原因管理器。
    /// 通过 <see cref="TryEndGame"/> 以模组自定义原因结束一局；
    /// <see cref="GameEndPatch"/>（在 AmongUsClient.OnGameEnd）会读取当前原因驱动结算/复盘。
    /// </summary>
    public static class EndGameManager
    {
        /// <summary>当前生效的结束原因（本轮游戏）。</summary>
        public static GameEndReason CurrentReason { get; private set; } = GameEndReason.None;

        private static bool _gameActive;

        /// <summary>新一轮开始（GameManager.StartGame 时调用重置状态）。</summary>
        public static void OnNewGameStart()
        {
            _gameActive = true;
            CurrentReason = GameEndReason.None;
            CustomWinnerCode = null;
        }

        /// <summary>
        /// 以自定义原因结束一局。
        /// 内部调用原版 <see cref="GameManager.RpcEndGame"/> 触发结算流程（占位用 CrewmatesByVote），
        /// 结束原因由本类记录，结算补丁读取模组原因。
        /// </summary>
        public static void TryEndGame(GameEndReason reason)
        {
            try
            {
                if (!_gameActive)
                {
                    LightLogger.LogWarning("[EndGameManager] 尝试在非游戏状态下结束，忽略。");
                    return;
                }
                CurrentReason = reason;
                _forceEndRequested = true;
                global::GameManager.Instance?.RpcEndGame(GameOverReason.CrewmatesByVote, true);
                _forceEndRequested = false;
            }
            catch (System.Exception ex)
            {
                LightLogger.LogError("[EndGameManager.TryEndGame]", ex);
            }
        }

        public static void MarkInvalid()
        {
            CurrentReason = GameEndReason.Invalid;
        }

        public static string CustomWinnerCode { get; private set; }

        public static void MarkCustomWin(string winnerCode)
        {
            CustomWinnerCode = winnerCode;
            CurrentReason = GameEndReason.CustomWin;
        }

        private static bool _forceEndRequested;

        public static bool ConsumeForceEnd()
        {
            if (!_forceEndRequested) return false;
            _forceEndRequested = false;
            return true;
        }

        /// <summary>读取当前结束原因，若从未设置则返回 None。</summary>
        public static GameEndReason GetCurrentReason() => CurrentReason;
    }
}
