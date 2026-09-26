using LightInDark.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Light.Components.MainColor;

namespace Light.Components;

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
        /// 模组验证服务器地址（留空 = 禁用手握校验）。
        /// 默认启用官方验证服务器。
        /// </summary>
        public string VerifyServerUrl { get; set; } = "https://lidverify.moonscar.cn";
        /// <summary>
        /// 握手失败处理：0=仅提示 1=踢出该玩家（按房主的配置生效）
        /// </summary>
        public int HandshakeMode { get; set; } = 0;
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

            // 迁移：旧配置文件缺少新字段时，自动补默认值并写回（保证 VerifyServerUrl/HandshakeMode 等存在）
            if (!json.Contains("VerifyServerUrl") || !json.Contains("HandshakeMode") || !json.Contains("SkipLoadAnimation"))
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
}