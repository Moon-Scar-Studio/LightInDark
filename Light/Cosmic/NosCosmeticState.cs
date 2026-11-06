namespace Light.Cosmic;

internal enum NosAnimation { Idle, Run, ClimbUp, ClimbDown, EnterVent, ExitVent }

/// <summary>NOS state selection kept independent of Unity for regression coverage.</summary>
internal static class NosCosmeticState
{
    internal static (CosmicItem? Front, CosmicItem? Back) Select(CosmicItem item, NosAnimation state, bool left)
    {
        CosmicItem? Image(string name)
        {
            string key = name == "Main" ? "Flip" : name + "Flip";
            if (left && item.NosImages.TryGetValue(key, out var facing)) return facing;
            return item.NosImages.TryGetValue(name, out var image) ? image : null;
        }
        var front = Image("Main");
        var back = Image("Back");
        switch (state)
        {
            case NosAnimation.Run:
                front = Image("Move") ?? front;
                // NOS names its moving back layer MoveBack / MoveBackFlip.
                back = Image("MoveBack") ?? back;
                break;
            case NosAnimation.ClimbUp:
                front = Image("Climb"); back = null; break;
            case NosAnimation.ClimbDown:
                front = Image("ClimbDown") ?? Image("Climb"); back = null; break;
            case NosAnimation.EnterVent:
                front = Image("EnterVent") ?? front;
                back = Image("EnterVentBack") ?? back;
                break;
            case NosAnimation.ExitVent:
                front = Image("ExitVent") ?? front;
                back = Image("ExitVentBack") ?? back;
                break;
        }
        return (front, back);
    }
}
