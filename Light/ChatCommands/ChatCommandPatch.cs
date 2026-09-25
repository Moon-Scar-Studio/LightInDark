using HarmonyLib;
using Il2CppSystem.Linq.Expressions.Interpreter;
using InnerNet;
using Light.Patches;
using Light.Roles.Crewmates;
using Light.UI.HudUI;
using Light.UI.Window;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Roles;
using LightInDark.RPCs;
using LightInDark.Utilities;
using Steamworks;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace Light.ChatCommands;

[Harmony]
public class PatchManager
{
    public static ChatHistoryManager HistoryManager = new();
    public static bool IsHost(PlayerControl player) => AmongUsClient.Instance.AmHost;
    public static void SendLocalMessage(string msg,bool rename = true)
    {
        try
        {
            var pc = PlayerControl.LocalPlayer;
            string orig = pc.name;
            if(rename)
                pc.SetName("System");
            HudManager.Instance.Chat.AddChat(pc, msg);
            pc.SetName(orig);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PatchManager.SendLocalMessage]", ex);
        }
    }
    public static bool SendNormalMessage(string msg)
    {
        try
        {
            PlayerControl.LocalPlayer.RpcSendChat(msg);
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PatchManager.SendNormalMessage]", ex); return default;
        }
    }
    public static bool OnSendChat(ChatController __instance)
    {
        try
        {
            bool isHost = IsHost(PlayerControl.LocalPlayer);
            string text = __instance.freeChatField.textArea.text.Trim();
            string raw = __instance.freeChatField.textArea.text;
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return true;
            string cmd = parts[0].ToLower();
            switch (cmd)
            {
                case "/play":
                    try
                    {
                        SendLocalMessage("播放测试音效 TestSFX.mp3");
                        SfxManager.Play("./Resources/SFX/TestSFX.mp3");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"/play 指令失败:{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/test":
                    if (!isHost) return false;
                    SendLocalMessage("指令测试");
                    __instance.freeChatField.Clear();
                    return false;
                case "/suicide":
                    RpcDefinitions.Suicide(PlayerControl.LocalPlayer);
                    __instance.freeChatField.Clear();
                    return false;
                case "/showchat":
                    RpcDefinitions.ShowChat();
                    __instance.freeChatField.Clear();
                    return false;
                case "/hidechat":
                    RpcDefinitions.HideChat();
                    __instance.freeChatField.Clear();
                    return false;
                case "t":
                    SendLocalMessage("t-Complate");
                    __instance.freeChatField.Clear();
                    return false;
                case "/sr":
                    try
                    {
                        var gil = LightInDark.Game.GameManager.Instance.LocalPlayer;
                        LightLogger.Log(gil.ToString());
                        if (gil == null) return false;
                        gil.SetRole(RoleRegistry.Get<Caller>());
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch(Exception ex)
                    {
                        LightLogger.LogError($"设置角色失败:{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/sw":
                    try
                    {
                        HudUI.OpenButtonWindow("测试窗口"
                            ,new HudUI.ButtonOption("按钮1", () => SendLocalMessage("按钮1被点击"))
                            , new HudUI.ButtonOption("按钮2", () => SendLocalMessage("按钮2被点击"))
                            );
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch(Exception ex)
                    {
                        LightLogger.LogWarning($"window异常：{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/code":
                    int i = AmongUsClient.Instance.GameId;
                    string code = GameCode.IntToGameNameV2(i);
                    SendLocalMessage($"当前房间码为:{code}");
                    __instance.freeChatField.Clear();
                    return false;
                case "/kick":
                    try
                    {
                        if (!isHost) return false;
                        if (parts.Length < 2)
                        {
                            SendLocalMessage("请指定要踢出的玩家ID");
                            __instance.freeChatField.Clear();
                            return false;
                        }
                        PlayerControl target = null;
                        string targetName = parts[1];
                        foreach (var p in PlayerControl.AllPlayerControls)
                            if (p.Data.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
                        if (target == null)
                        {
                            SendLocalMessage("未找到指定玩家");
                            __instance.freeChatField.Clear();
                            return false;
                        }
                        string reason = parts.Length >= 3 ? string.Join(' ', parts.Skip(2)) : "无";
                        LightUtils.KickPlayer(target.ToLIDPlayer(), PlayerControl.LocalPlayer.name, reason);
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch(Exception ex)
                    {
                        LightLogger.LogWarning($"踢人失败:{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/autosave":
                    try
                    {
                        LightPlayerDataManager.AutoSaveEnabled = !LightPlayerDataManager.AutoSaveEnabled;
                        SendLocalMessage($"自动保存复盘: {(LightPlayerDataManager.AutoSaveEnabled ? "已开启" : "已关闭")}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"autosave切换失败:{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/replay":
                    try
                    {
                        var replayText = LightInDark.Game.LightPlayerDataManager.BuildReplayText();
                        SendLocalMessage(replayText);
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LightLogger.LogWarning($"复盘显示失败:{ex.Message}");
                        __instance.freeChatField.Clear();
                        return false;
                    }
                case "/cur":
#if !WINDOWS
                    SendLocalMessage("非Windows无法使用/cur指令。");
#endif
                    try
                    {
                        if(parts.Length < 2
                            )
                        {
                            SendLocalMessage("请输入子命令。");
                        }
                        string subCmd = parts[1].ToLower();
                        switch (subCmd)
                        {
                            case "help":
                                SendLocalMessage("/cur set <索引> 设置鼠标指针。\n/cur list 获取所有指针的索引。\n/cur now 获取当前鼠标指针的索引。");
                                break;
                            case "list":
                                StringBuilder sb = new();
                                sb.AppendLine("0 - 系统默认鼠标");
                                sb.AppendLine("1 - 全家福");
                                sb.AppendLine("2 - 二代全家福");
                                SendLocalMessage(sb.ToString());
                                break;
                            case "set":
                                string subInxStr = parts[2];
                                if(int.TryParse(subInxStr,out int subInx))
                                {
                                    if (UI.Cursor.ChangeCursorFromIndex(subInx) == true)
                                    {
                                        SendLocalMessage($"更换成功！已将光标更换为索引{subInx}");
                                    }
                                    else if(UI.Cursor.ChangeCursorFromIndex(subInx) == null)
                                    {
                                        SendLocalMessage($"更换失败！请将根目录下Light.log发送给开发者！");
                                    }
                                    else
                                    {
                                        SendLocalMessage("请输入正确的索引数字！");
                                    }
                                }
                                else
                                {
                                    SendLocalMessage("请输入正确的索引数字！");
                                }
                                break;
                        }
                        __instance.freeChatField.Clear();
                        return false;
                    }
                    catch
                    {
                        __instance.freeChatField.Clear();
                        return false;
                    }
            }
            var historyText = __instance.freeChatField.textArea.text;
            HistoryManager.AddMessage(historyText);
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[PatchManager.OnSendChat]", ex); return true;
        }
    }
}