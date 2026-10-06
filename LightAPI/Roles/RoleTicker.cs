using LightInDark.Events;

namespace LightInDark.Roles
{
    internal static class RoleWinWatcher
    {
        public static void OnHudUpdate(GameHudUpdateEvent ev)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                if (LightInDark.Game.EndGameManager.GetCurrentReason() != LightInDark.Game.GameEndReason.None) return;

                var game = LightInDark.Game.GameManager.Instance;
                if (game == null) return;

                foreach (var player in game.AllPlayers)
                {
                    if (!RoleUtils.IsOnField(player)) continue;

                    var runtime = player.Role;
                    if (runtime == null || !runtime.CheckWin()) continue;

                    var template = runtime.Role;
                    LightInDark.Game.EndGameManager.MarkCustomWin(template?.TeamCode ?? template?.CodeName);
                    LightInDark.Game.EndGameManager.TryEndGame(LightInDark.Game.GameEndReason.CustomWin);
                    return;
                }
            }
            catch { }
        }
    }

    internal static class RoleNamePainter
    {
        public static void OnHudUpdate(GameHudUpdateEvent ev)
        {
            try
            {
                var local = LightInDark.Game.GameManager.Instance?.LocalPlayer;
                if (local?.Role == null) return;
                RoleTeam.PaintMemberNames(local.Role);
            }
            catch { }
        }
    }
}
