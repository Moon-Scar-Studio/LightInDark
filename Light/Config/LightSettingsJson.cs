using Light.Patches;
using LightInDark.Core;
using LightInDark.Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using static Light.Config.MainColor;

namespace Light.Config;

public static class LightSettings
{
    static string JsonPath => Path.Combine(LightPlugin.LightUserDataPath, "Settings.json");
    static JsonSerializerOptions _options = new() { WriteIndented = true };
    [Serializable]
    public class LightSettingsData
    {
        /// <summary>
        /// 解锁全部装扮
        /// </summary>
        public bool UnlockAllCosmic { get; set; } = true;
        /// <summary>
        /// 在局内不展示所有装扮
        /// </summary>
        public bool DontShowCosmic { get; set; } = false;
        /// <summary>
        /// 在会议中显示任务面板
        /// </summary>
        public bool ShowTaskPanelInMeeting { get; set; } = true;
        /// <summary>
        /// 最高帧数上限 最多为150
        /// </summary>
        public int MaxFPS { get; set; } = 60;
        /// <summary>
        /// 跳过自定义加载动画（true 时静默加载，不播放加载页动画）
        /// </summary>
        public bool SkipLoadAnimation { get; set; } = false;
        /// <summary>
        /// 模组验证服务器地址
        /// </summary>
        public string VerifyServerUrl { get; set; } = "https://lidverify.moonscar.cn";
        /// <summary>
        /// 握手失败处理：0=仅提示 1=踢出该玩家（按房主的配置生效）
        /// </summary>
        public int HandshakeMode { get; set; } = 0;
        /// <summary>
        /// 是否启用握手验证（默认关闭）。关闭时不校验玩家、不请求票据。
        /// </summary>
        public bool EnableHandshake { get; set; } = false;
        /// <summary>
        /// 握手验证超时时间（秒），范围 1~60，默认 10 秒。
        /// </summary>
        public float HandshakeTimeoutSeconds { get; set; } = 10f;
        /// <summary>
        /// 启动时自动检查模组更新。
        /// 关掉之后加载页不再请求 version.json（也就不会显示新版本金字）；
        /// 主界面「检查更新」按钮仍然可以手动跑。
        /// </summary>
        public bool AutoCheckUpdate { get; set; } = true;
    }
    public static LightSettingsData LoadSettingData()
    {
        try
        {
            if (!File.Exists(JsonPath))
            {
                LightLogger.Log("未发现设置JSON，开始创建。");
                var r = new LightSettingsData();
                Save(r);
                LightLogger.Log("创建完毕");
                return r;
            }
            string json = File.ReadAllText(JsonPath);
            var result = JsonSerializer.Deserialize<LightSettingsData>(json,_options);
            if (result == null)
            {
                LightLogger.LogWarning("JSON格式无效，使用默认值");
                result = new LightSettingsData();
                Save(result);
            }
            if (result.MaxFPS > 150) result.MaxFPS = 150;
            if (result.HandshakeTimeoutSeconds < 1f) result.HandshakeTimeoutSeconds = 1f;
            if (result.HandshakeTimeoutSeconds > 60f) result.HandshakeTimeoutSeconds = 60f;

            // 迁移：旧配置文件缺少新字段时，自动补默认值并写回（保证 VerifyServerUrl/HandshakeMode 等存在）
            if (!json.Contains("VerifyServerUrl") || !json.Contains("HandshakeMode") || !json.Contains("SkipLoadAnimation")
                || !json.Contains("EnableHandshake") || !json.Contains("HandshakeTimeoutSeconds"))
            {
                LightLogger.Log("设置文件缺少新字段，自动补齐默认值。");
                Save(result);
            }
            return result;
        }
        catch(Exception ex)
        {
            LightLogger.LogError("设置加载失败,使用默认值",ex);
            return new LightSettingsData();
        }
    }
    public static void Save(LightSettingsData data)
    {
        try
        {
            FileUtil.EnsureDirectoryExists(JsonPath);
            string json = JsonSerializer.Serialize(data, _options);
            File.WriteAllText(JsonPath, json);
            LightLogger.Log("设置配置已保存。");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightSettings.Save]", ex);
        }
    }

    /// <summary>
    /// 重载所有**能重载**的配置，并把结果立即应用到运行时：
    /// <list type="bullet">
    ///   <item>Settings.json（本文件） → LightPlugin.LightSettingsData</item>
    ///   <item>ChatSettings.json（模组主色 / 聊天色） → LightPlugin.ColorData，并重新套用原版"接受绿"覆盖</item>
    ///   <item>语言文件 → Language.Load()</item>
    ///   <item>光标配置（Cursor.json） → Cursor.Reload()</item>
    ///   <item>帧率上限 → Application.targetFrameRate 立即生效</item>
    ///   <item>Light 设置页签上显示的值 → LightOptionsRegistry.SyncFromSettings()</item>
    /// </list>
    /// 各步互相独立：某一步失败不会中断后面的，只把返回值置为 false。
    /// <para>不可重载（需要重启游戏）：Harmony 补丁、角色注册、RPC 定义、Dispatcher、握手系统。</para>
    /// </summary>
    /// <returns>全部成功返回 true；任一步骤抛异常返回 false</returns>
    public static bool ReloadConfig()
    {
        bool ok = true;

        // ① 设置 JSON
        try
        {
            LightPlugin.LightSettingsData = LoadSettingData();
            LightLogger.Log("[ReloadConfig] 设置 JSON 已重载");
        }
        catch (Exception ex)
        {
            ok = false;
            LightLogger.LogError("[ReloadConfig] 设置 JSON 重载失败（继续重载其它项）", ex);
        }

        // ② 颜色配置（模组主色 / 聊天色）
        try
        {
            LightPlugin.ColorData = MainColor.LoadChatColor();
            PaletteColorOverride.Apply();                 // 主色可能变了 → 重新写一遍原版"接受绿"
            LightLogger.Log("[ReloadConfig] 颜色配置已重载并套用");
        }
        catch (Exception ex)
        {
            ok = false;
            LightLogger.LogError("[ReloadConfig] 颜色配置重载失败（继续）", ex);
        }

        // ③ 语言文件
        try
        {
            Language.Load();
            LightLogger.Log("[ReloadConfig] 语言文件已重载");
        }
        catch (Exception ex)
        {
            ok = false;
            LightLogger.LogError("[ReloadConfig] 语言重载失败（继续）", ex);
        }

        // ④ 光标配置
        try
        {
            Cursor.Reload();
            LightLogger.Log("[ReloadConfig] 光标配置已重载");
        }
        catch (Exception ex)
        {
            ok = false;
            LightLogger.LogError("[ReloadConfig] 光标重载失败（继续）", ex);
        }

        // ⑤ 帧率上限立即生效
        try
        {
            Application.targetFrameRate = LightPlugin.LightSettingsData?.MaxFPS ?? 60;
            LightLogger.Log($"[ReloadConfig] 帧率上限已应用：{Application.targetFrameRate}");
        }
        catch (Exception ex)
        {
            ok = false;
            LightLogger.LogError("[ReloadConfig] 帧率应用失败（继续）", ex);
        }

        // ⑥ 让 Light 设置页签显示最新值（不通过行点击改的值也能同步）
        try
        {
            LightOptionsRegistry.SyncFromSettings();
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[ReloadConfig] 设置页签显示同步失败：{ex.Message}");
        }

        LightLogger.Log($"[ReloadConfig] 全部完成，全部成功={ok}");
        return ok;
    }
}