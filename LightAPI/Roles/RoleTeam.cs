using System.Collections.Generic;
using System.Linq;
using LightInDark.Core;
using LightInDark.Game;

namespace LightInDark.Roles
{
    public static class RoleTeam
    {
        public static string CodeOf(Player player) => player?.Role?.Role?.TeamCode;

        public static bool IsTeammate(Player a, Player b)
        {
            var code = CodeOf(a);
            return !string.IsNullOrEmpty(code) && CodeOf(b) == code;
        }

        public static IEnumerable<Player> Members(string teamCode, bool onFieldOnly = false)
        {
            if (string.IsNullOrEmpty(teamCode)) return Enumerable.Empty<Player>();

            var all = LightInDark.Game.GameManager.Instance?.AllPlayers;
            if (all == null) return Enumerable.Empty<Player>();

            return all.Where(p => CodeOf(p) == teamCode && (!onFieldOnly || RoleUtils.IsOnField(p)));
        }

        public static void PaintMemberNames(RuntimeRoleTemplate viewer)
        {
            if (viewer == null) return;

            var code = viewer.Role?.TeamCode;
            if (string.IsNullOrEmpty(code)) return;

            var unity = viewer.Role.Color.ToUnityColor();
            var members = Members(code).ToList();
            var all = LightInDark.Game.GameManager.Instance?.AllPlayers;
            LightLogger.Log($"[Team] code={code} count={members.Count} local={LightInDark.Game.GameManager.Instance?.LocalPlayer?.Name}"
                + (all == null ? " all=null" : " | " + string.Join(" | ", all.Select(x => $"{x.Name}:{x.Role?.Role?.CodeName ?? "-"}:{CodeOf(x) ?? "-"}"))));

            foreach (var member in members)
            {
                var cosmetics = member.Control?.cosmetics;
                if (cosmetics == null) continue;

                var nameText = cosmetics.nameText;
                if (nameText == null) continue;

                cosmetics.SetNameColor(unity);
                nameText.color = unity;
            }
        }
    }
}
