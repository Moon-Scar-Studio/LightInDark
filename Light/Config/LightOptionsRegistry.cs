using Light.Patches;
using LightInDark.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Light.Config;

internal class LightOptionsRegistry
{
    static bool _reg;
    static LightOptionButton? _unlockAll;
    static LightOptionButton? _dontShow;
    static LightOptionButton? _reloadConfig;
    static LightOptionButton? _showTaskPanelInMeeting;
    static LightOptionButton? _cursorIdx;
    static LightOptionButton? _autoCheckUpdate;
    static LightOptionButton? _handshakeEnabled;
    static LightOptionButton? _handshakeMode;
    static LightOptionButton? _handshakeTimeout;

    // 握手超时可选值 1~60（秒），索引 0 对应 1 秒
    static readonly string[] HandshakeTimeoutOptions = Enumerable.Range(1, 60).Select(v => v.ToString()).ToArray();

    /// <summary>把超时秒数换算成选择器索引（1 秒 = 索引 0）。</summary>
    static int TimeoutToIndex(float seconds)
    {
        int i = (int)Math.Round(seconds) - 1;
        if (i < 0) i = 0;
        if (i > HandshakeTimeoutOptions.Length - 1) i = HandshakeTimeoutOptions.Length - 1;
        return i;
    }

    public static void Register()
    {
        if (_reg) return;
        _reg = true;

        var s = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();

        _unlockAll = SettingsTabPatch.AddToggleButton(
            "解锁所有装扮", s.UnlockAllCosmic, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.UnlockAllCosmic = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }
            );

        _dontShow = SettingsTabPatch.AddToggleButton(
            "装扮简洁模式", s.DontShowCosmic, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.DontShowCosmic = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "不显示任何人的装扮"
            );
        _reloadConfig = SettingsTabPatch.AddActionButton("重载配置",()=>{ LightSettings.ReloadConfig(); },"手动重载配置");
        _showTaskPanelInMeeting = SettingsTabPatch.AddToggleButton("在会议中显示任务面板",s.ShowTaskPanelInMeeting, 
            on => 
            { 
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.ShowTaskPanelInMeeting = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            });
        _cursorIdx = SettingsTabPatch.AddSelectorButton("鼠标样式", ["不使用MOD光标", "全家福", "全家福2"], Cursor.Index,
         idx => 
         {
             var result = Cursor.ChangeCursorFromIndex(idx);
             if (result == true) return;

             LightLogger.LogWarning($"[LightOptions] 切换鼠标样式失败（idx={idx}，结果={(result == null ? "null：贴图未加载" : "false：索引非法")}），把显示回滚成实际值");
             SyncFromSettings();
         });

        _autoCheckUpdate = SettingsTabPatch.AddToggleButton(
            "自动检查更新", s.AutoCheckUpdate, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.AutoCheckUpdate = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "启动时自动比对云端版本");

        // 握手验证（设置块 lid.handshake）
        SettingsTabPatch.AddEmptyButton("握手验证");
        _handshakeEnabled = SettingsTabPatch.AddToggleButton(
            "启用握手验证", s.EnableHandshake, on =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.EnableHandshake = on;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "默认关闭；开启后房主按「验证服务器地址」核验玩家（地址取自 VerifyServerUrl）");

        _handshakeMode = SettingsTabPatch.AddSelectorButton(
            "验证失败处理", ["仅提示", "踢出"], s.HandshakeMode, idx =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.HandshakeMode = idx;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "验证失败时：仅提示=右下角警告；踢出=移出该玩家（仅房主配置生效）");

        _handshakeTimeout = SettingsTabPatch.AddSelectorButton(
            "握手超时(秒)", HandshakeTimeoutOptions, TimeoutToIndex(s.HandshakeTimeoutSeconds), idx =>
            {
                var cur = LightPlugin.LightSettingsData ??= new LightSettings.LightSettingsData();
                cur.HandshakeTimeoutSeconds = idx + 1;
                LightSettings.Save(cur);
                LightSettings.ReloadConfig();
            }, "等待玩家握手完成的最长时间，1~60 秒；超时按验证失败处理");

        // ⚠️ MCI 注册**不做成设置项**（用户决定：之后自研 MCI 时另行实现）。
        //    在自研方案落地前，LightPlugin.Load 里那一行保持注释状态 ——
        //    一旦注册 GUID，开房会切到 Tags.HostModdedGame(25)，而只有官方服务器
        //    和匹配器实现了它，私服和本地游戏会直接「创建游戏连线区失败」
        SettingsTabPatch.LightTabOpened += SyncFromSettings;
    }

    /// <summary>
    /// 配置被重新加载（<see cref="LightSettings.ReloadConfig"/>）之后，
    /// 把 Light 设置页签上显示的值同步成最新配置（不通过行点击改的值也能同步）。
    /// </summary>
    public static void SyncFromSettings()
    {
        var s = LightPlugin.LightSettingsData;
        if (s == null) return;

        SyncToggle(_unlockAll, s.UnlockAllCosmic);
        SyncToggle(_dontShow, s.DontShowCosmic);
        SyncToggle(_showTaskPanelInMeeting, s.ShowTaskPanelInMeeting);
        SyncToggle(_autoCheckUpdate, s.AutoCheckUpdate);
        SyncToggle(_handshakeEnabled, s.EnableHandshake);

        SyncSelector(_cursorIdx, Cursor.Index);
        SyncSelector(_handshakeMode, s.HandshakeMode);
        SyncSelector(_handshakeTimeout, TimeoutToIndex(s.HandshakeTimeoutSeconds));

        SettingsTabPatch.Refresh();
    }

    private static void SyncToggle(LightOptionButton? btn, bool value)
    {
        if (btn == null) return;
        btn.IsOn = value;
        btn.ValueText = value ? "启用" : "禁用";
    }

    /// <summary>把选择器显示同步成实际索引（同时修正 SelectorIndex，避免下次点击循环时错位）。</summary>
    private static void SyncSelector(LightOptionButton? btn, int index)
    {
        if (btn == null) return;

        var options = btn.SelectorOptions;
        if (options == null || options.Length == 0) return;

        if (index < 0 || index >= options.Length) index = 0;

        btn.SelectorIndex = index;
        btn.ValueText = options[index];
    }
}
