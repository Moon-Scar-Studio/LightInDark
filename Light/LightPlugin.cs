global using HarmonyLib;
global using System.Collections;
global using UnityEngine;
global using Light.Utilities; 
using Light.Configuration;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using LightInDark.Events;
using LightInDark.Language;
using LightInDark.Roles;
using LightInDark.RPCs;
using Light.ChatCommands;
using Light.Patches;
using Light.Roles.Crewmates;
using Light.Roles.Vanilla;
using LightInDark.Core;
using System;
using UnityEngine.SceneManagement;
using Light.Components;

namespace Light;

[BepInPlugin(Id, Name, Version)]
[BepInProcess("Among Us.exe")]
[BepInDependency("com.moonscar.lightapi",BepInDependency.DependencyFlags.HardDependency)]
public partial class LightPlugin : BasePlugin
{
    public const string Id = "com.moonscar.lightindark";
    public const string Name = "LightInTheDark";
    public const string Version = "1.0.0";

    public const string VisualVersion = "v1.0.0";

    public const string RichVersion = "<color=#4FD1C5>ver</color> <color=#38B2AC>1.0.0</color>";
    public static string AUVersion;
    public static string CursurDataPath = Application.persistentDataPath;
    public static MainColor.ModColorData ColorData;
    public static LightSettings.LightSettingsData LightSettingsData;
    public Harmony Harmony { get; } = new(Id);
    public static string LightUserDataPath => Path.Combine(Application.persistentDataPath, "LightInDark");

    internal static ManualLogSource StaticLog { get; private set; } = null!;

    public override void Load()
    {
        try
        {
#if DEBUG
            FirstChanceExceptionLogger.Initialize();
#endif
            StaticLog = Log;
            Harmony.PatchAll();
            LightSettingsData = LightSettings.LoadSettingData();
            if (!VersionMaker.MakeVersion())
                Log.LogError($"VM json 加载失败。具体异常请查看Light.log。");
            LoadCommand();
            EventSystem.ScanAndRegisterAll();
            ExtractLanguageFiles();
            Language.Load();
            LidRpcRegistry.ScanAndPatch(Harmony);
            ColorData = MainColor.LoadChatColor();
            LoadRole();
            PresetManager.ApplyCurrentPreset();
            Dispatcher.Initialize();
#if !DEBUG
            LightLogger.ClearLog();
#endif
            RpcDefinitions.OnFreeChatStateChanged += show => ShowChatPatch.NeedShowFreeChat = show;
            AddCursorComponent();
            RegisterShowModStampOnMainMenu();
            ChatHistoryLogUtils.Init();
            // [已禁用-握手系统] 先确保模组可玩性，握手验证暂停（2026-09-26）。
            // 恢复时取消注释下一行，并同步服务器 official.json 的 hash。
            // Handshake.HandshakeManager.Initialize();
            
            Log.LogInfo($"模组 {Name} v{Version} 已加载！");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.Load]", ex);
        }
    }
    private static void RegisterShowModStampOnMainMenu()
    {
        try
        {
            SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>)((scene, mode) =>
            {
                try
                {
                    if (scene.name == "MainMenu")
                        ModManager.Instance.ShowModStamp();
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("[LightPlugin.ShowModStampOnMainMenu]", ex);
                }
            }));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.RegisterShowModStampOnMainMenu]", ex);
        }
    }

    private static void AddCursorComponent()
    {
        try
        {
            UI.Cursor.Initialize();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.AddCursorComponent]", ex);
        }
    }

    private static void ExtractLanguageFiles()
    {
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Language");
            Directory.CreateDirectory(folder);
            var asm = typeof(LightPlugin).Assembly;
            foreach (var res in new[] { "Light.Resources.Language.SChinese.json", "Light.Resources.Language.English.json" })
            {
                string fileName = res.Substring(res.LastIndexOf('.', res.LastIndexOf('.') - 1) + 1);
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path)) continue;
                using var stream = asm.GetManifestResourceStream(res);
                if (stream == null) continue;
                using var fs = File.Create(path);
                stream.CopyTo(fs);
            }
        }
        catch (Exception ex)
        {
            StaticLog.LogWarning($"解压语言文件失败：{ex.Message}");
        }
    }

    private static void LoadCommand()
    {
        try
        {
            var harmony = new Harmony("Light.cmd.harmony");
            var orig = AccessTools.Method(typeof(ChatController), "SendChat");
            if (orig == null) return;
            var prefixMethod = AccessTools.Method(typeof(PatchManager), nameof(PatchManager.OnSendChat));
            if (prefixMethod == null) return;
            harmony.Patch(orig, new HarmonyMethod(prefixMethod));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.LoadCommand]", ex);
        }
    }
    private void LoadRole()
    {
        try
        {
            LightInDark.Configuration.RoleConfig.Initialize(Config);
            RoleRegistry.Register<Caller>();
            RoleRegistry.Register(VanillaImpostor.Instance);
            RoleRegistry.Register(VanillaCrewmate.Instance);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LightPlugin.LoadRole]", ex);
        }
    }
    public static class FirstChanceExceptionLogger
    {
        static bool _init = false;
        static readonly object _lock = new();
        public static void Initialize()
        {
            if (_init) return;
            lock (_lock)
            {
                if (_init) return;
                AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
                {
                    try
                    {
                        string dir = @"D:\log";
                        if(!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string name = $"FIRST_CHANCE{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
                        string pathF = Path.Combine(dir, name);
                        File.WriteAllText(pathF,e.Exception.ToString());
                    }
                    catch(Exception ex)
                    {
                        LightLogger.LogError($"FirstChance捕获失败",ex);
                    }
                };
                _init = true;
            }
        }
    }
}

/// <summary>
/// 当颜色配置无效时抛出。
/// </summary>
public class InvalidColorTypeException : Exception
{
    public InvalidColorTypeException(string message) : base(message) { }
    public InvalidColorTypeException(string message, Exception innerException) : base(message, innerException) { }
}

