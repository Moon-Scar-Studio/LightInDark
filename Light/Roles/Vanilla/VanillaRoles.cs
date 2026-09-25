using AmongUs.GameOptions;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Roles;
using System;

namespace Light.Roles.Vanilla;

/// <summary>默认内鬼职业：未分配自定义职业时的默认占位（不参与自定义分配）。</summary>
public class VanillaImpostor : Role
{
    public static readonly VanillaImpostor Instance = new();

    public override string CodeName => "VanillaImpostor";
    public override LightInDark.Color Color => LightInDark.Color.Red;
    public override RoleCategory Category => RoleCategory.Impostor;
    public override string IntroBlurbKey => "Role.VanillaImpostor.intro";
    public override string SkillDescriptionKey => "Role.VanillaImpostor.skill";
    public override bool CanSpawnIn() => false;

    protected override void OnActivated()
    {
        try
        {
            if (MyPlayer.Control != null)
                RoleManager.Instance.SetRole(MyPlayer.Control, RoleTypes.Impostor);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[VanillaImpostor.OnActivated]", ex);
        }
    }
}

/// <summary>默认船员职业：未分配自定义职业时的默认占位（不参与自定义分配）。</summary>
public class VanillaCrewmate : Role
{
    public static readonly VanillaCrewmate Instance = new();

    public override string CodeName => "VanillaCrewmate";
    public override LightInDark.Color Color => LightInDark.Color.Green;
    public override RoleCategory Category => RoleCategory.Crewmate;
    public override string IntroBlurbKey => "Role.VanillaCrewmate.intro";
    public override string SkillDescriptionKey => "Role.VanillaCrewmate.skill";
    public override bool CanSpawnIn() => false;

    protected override void OnActivated()
    {
        try
        {
            if (MyPlayer.Control != null)
                RoleManager.Instance.SetRole(MyPlayer.Control, RoleTypes.Crewmate);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[VanillaCrewmate.OnActivated]", ex);
        }
    }
}