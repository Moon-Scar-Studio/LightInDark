using LightInDark.Game;
using LightInDark.Modifiers;

namespace Light.Modifiers;

public sealed class Flash : Modifier
{
    public const string Code = "Flash";
    public const string Symbol = "⚡";

    public static readonly Flash Instance = new();

    private Flash() : base("闪电侠", new LightInDark.Color(255, 220, 0), "移动速度更快。")
    {
    }

    public override string Key => Code;

    public override RuntimeModifier CreateInstance(Player player) => new RuntimeFlash(this, player);

    public static float SpeedOf(Player? player)
    {
        if (player?.Control == null || !ModifierManager.HasModifier(player, Code)) return 1f;
        float value = ModifierRegistry.GetValue(Code, player.Control.PlayerId);
        return value <= 0f ? 1f : value;
    }
}

public sealed class RuntimeFlash : RuntimeModifier
{
    public RuntimeFlash(Modifier definition, Player player) : base(definition, player)
    {
    }
}
