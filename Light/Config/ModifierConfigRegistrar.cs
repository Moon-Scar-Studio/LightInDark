using Light.Modifiers;
using LightInDark.Configuration;

namespace Light.Config;

internal static class ModifierConfigRegistrar
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        foreach (var modifier in ModifierRegistry.All)
        {
            var block = new ConfigBlock(
                    $"lid.modifier.{modifier.Key}",
                    modifier.Name,
                    ConfigCategory.Modifier)
                .SetHeaderColor(LightInDark.ColorHelper.ToUnityColor(modifier.Color));

            var count = block.AddConfiguration(
                $"modifier.{modifier.Key}.count", 0, 0, 15, 1,
                "最大数量",
                $"同时拥有{modifier.Name}的玩家数量上限。0 = 不出现。");

            block.AddConfiguration(
                    $"modifier.{modifier.Key}.chance", 100, 0, 100, 5,
                    "出现概率",
                    $"每个名额独立掷骰，命中后才会分配{modifier.Name}。")
                .SetDependsOn(count)
                .WithSuffix(ConfigSuffix.Percent);

            block.AddConfiguration(
                    $"modifier.{modifier.Key}.speed", 1.25f, 1f, 3f, 0.05f,
                    "速度倍率",
                    $"{modifier.Name}的移动速度倍率。")
                .SetDependsOn(count);
        }
    }
}
