using LightInDark.Configuration;
using LightInDark.Game;
using LightInDark.Modifiers.Buffs.Examples;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.UI.Ability;
using LightInDark.Visual;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Btn = LightInDark.UI.Ability.AbilityButton;
using Color = LightInDark.Color;

namespace Light.Roles.Impostors;

public class Camouflager : RoleTemplate
{
    static readonly RoleFloatConfig CamoCD = RoleConfig.Float("camocd",35,5,60,2.5f,ConfigSuffix.Second);
    static readonly RoleFloatConfig CamoDur = RoleConfig.Float("camodur",10,5,30,2.5f,ConfigSuffix.Second);
    public override string CodeName => "camouflager";

    public override Color Color => Color.ImpostorColor;

    public override RoleCategory RoleCategory => RoleCategory.Impostor;

    public override string From => null;

    public override RoleConfigBase[] RoleConfiguration => [CamoCD, CamoDur];

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner) => new RuntimeInstance(owner, this);
    public class RuntimeInstance : RuntimeRoleTemplate
    {
        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template){ }
        EffectButton camoBtn;
        protected override void OnActivated()
        {
            if (!AmOwner) return;
            camoBtn = AbilityButtonFactory.CreateEffect(this,
                "camo",
                null,
                CamoCD.Value,
                KeyCode.F,
                () => { },
                CamoDur.Value,
                allowCancelByReclick:false,
                canUse:()=>!MyPlayer.IsDead,
                canShow:() =>!MyPlayer.IsDead
                );
            if(camoBtn != null)
            {
                camoBtn.SetEndingJitter(true, 3f);
                camoBtn.OnEffectStart = () => 
                {
                    RpcCamo.Invoke(new(MyPlayer.Control.PlayerId,true));
                };
                camoBtn.OnEffectEnd = () =>
                {
                    RpcCamo.Invoke(new(MyPlayer.Control.PlayerId, false));
                };
            }
        }
        public static RemoteProcess<(byte id, bool on)> RpcCamo = new(
            "Camouflage",
            (msg, _) =>
            {
                PlayerAppearance.SetAnonymousAll(msg.on);
            }
        );
    }
}