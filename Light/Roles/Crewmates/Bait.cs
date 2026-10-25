using BepInEx.Unity.IL2CPP.Utils.Collections;
using LightInDark;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.Utilities;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Color = LightInDark.Color;

namespace Light.Roles.Crewmates;

public class Bait : RoleTemplate
{
    public override string CodeName => "bait";

    public override Color Color => Color.Cyan;

    public override RoleCategory RoleCategory => RoleCategory.Crewmate;

    public override string From => null;
    static readonly RoleBoolConfig ShowFlashWhenPlayerDead = RoleConfig.Bool("showflash",true);
    static readonly RoleBoolConfig CantBeGuess = RoleConfig.Bool("cantbeguess",true);
    static readonly RoleBoolConfig TellEveryBait = RoleConfig.Bool("tellevery", true).SetDependsOn(CantBeGuess); // 依赖

    static readonly RoleFloatConfig Delay = RoleConfig.Float("delay",0,0,10,0.5f,ConfigSuffix.Second);

    public override RoleConfigBase[] RoleConfiguration => [ShowFlashWhenPlayerDead, CantBeGuess, TellEveryBait, Delay];

    public override RuntimeRoleTemplate CreateRuntime(PlayerControl owner)
        => new RuntimeInstance(owner, this);
    public class RuntimeInstance : RuntimeRoleTemplate
    {
        public RuntimeInstance(PlayerControl owner, RoleTemplate template) : base(owner, template) { }
        bool needSendBaitMsg;
        protected override void OnActivated()
        {
            if (!AmOwner) return;
            needSendBaitMsg = true;
            if (CantBeGuess.Value)
                MyPlayer.CanBeGuessed = false;
        }
        void OnMyPlayerDead(PlayerDeathEvent ev)
        {
            if (ev.Player != MyPlayer.Control) return;// isMyplayer
            LightCoroutine.Start(CoReport(ev.Killer));
        }
        void FlashEv(PlayerMurderEvent ev)
        {
            if (!AmOwner && ShowFlashWhenPlayerDead.Value && ev.Killer != null && ev.Player != MyPlayer.Control) return;
            LightUtils.PlayFlash(Color.Cyan);
        }
        void ShowMsg(MeetingStartEvent ev)
        {
            if (!needSendBaitMsg) return;
            needSendBaitMsg = false;
            string content = Language.Translate("role.bait.imbait")
                .Replace("%ME%", MyPlayer.Name)
                .Replace("%COLOR%", ColorUtility.ToHtmlStringRGB(MyPlayer.PlayerColor.ToUnityColor()));

            RpcSendBaitMsg.Invoke((true,content));
        }
        // Nebula
        private IEnumerator CoReport(PlayerControl murderer)
        {
            float t = Delay.Value;
            yield return new WaitForSeconds(t);
            if (MeetingHud.Instance) yield break;

            murderer.CmdReportDeadBody(MyPlayer.Control.Data);
        }
        public static RemoteProcess<(bool on,string content)> RpcSendBaitMsg = new(
            "RpcSendBaitMsgs",
            (msg, _) =>
            {
                foreach(var p in LightPlayer.AllPlayers)
                {
                    var pc = PlayerControl.LocalPlayer;
                    string orig = pc.name;
                    pc.SetName("<color=#00FFFF>Bait Message</color>");
                    HudManager.Instance.Chat.AddChat(pc, msg.content);
                    pc.SetName(orig);
                }
            });
    }
}