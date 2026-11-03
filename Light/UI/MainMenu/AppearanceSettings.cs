using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LightInDark.Core;
using UnityEngine;

namespace Light.UI.MainMenu;

/// <summary>主界面按钮样式。</summary>
public enum MainButtonStyle
{
    /// <summary>MOD 样式（默认）：保留模组自己贴的那套 PNG。</summary>
    Mod = 0,
    /// <summary>原版样式：换回游戏原本的按钮贴图。</summary>
    Vanilla = 1,
    /// <summary>亚克力样式（FS 那种）：丢掉贴图，做成纯色半透明板，可单独/批量调色。</summary>
    Acrylic = 2,
    // ⚠️ 2026-11-03 用户："美化版你删了吧，bug 太多了" ✓ → Pretty 枚举已删除 ✗
    //   （存档里若还是 3，读盘时会被夹到 2 → 再手动切回 MOD 即可 ✓）
}

/// <summary>
/// 外观设置落盘 —— <c>&lt;LightUserDataPath&gt;\BackgroundSettings.json</c>。
///
/// ⚠️ 为什么**手写** JSON，而不是用现成的：
///   · <c>JsonUtility.FromJson&lt;T&gt;</c> —— IL2CPP interop 里泛型参数被约束成
///     <c>Il2CppSystem.Object</c>，传自己的 C# 类直接编译不过（error CS1503）。
///   · <c>System.Text.Json</c> / <c>Newtonsoft</c> —— 能编译，但要靠反射做映射，
///     在 IL2CPP 环境下多一层不确定性，而且我们只有十来个标量 + 一张小字典。
///   → 结构这么简单，手写反而最稳、零依赖，而且写出来的就是标准 JSON。
/// </summary>
public static class AppearanceSettings
{
    // ---- 背景 ----
    public static string Selected = "";
    public static int Fit;
    public static int Dim;
    public static bool HideCrewmates;
    /// <summary>视频音量百分比 0~100（步长 10）。</summary>
    public static int VideoVolumePercent = 100;

    // ---- 主界面按钮 ----
    public static MainButtonStyle ButtonStyle = MainButtonStyle.Mod;
    /// <summary>按钮标识 → 颜色（"#RRGGBB"）。空串表示"不覆盖，用样式默认"。</summary>
    public static readonly Dictionary<string, string> ButtonColors = new();

    public const string SelectionOff = "";
    public const string SelectionRandom = "@random";

    private static bool _loaded;

    private static string FilePath =>
        Path.Combine(LightPlugin.LightUserDataPath, "BackgroundSettings.json");

    // =====================================================================
    //  读写
    // =====================================================================

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath))
            {
                // 首次运行：背景默认"随机"（改造前主界面就是随机显示一张内置图，保持观感）
                Selected = SelectionRandom;
                Save();
                return;
            }
            Parse(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[AppearanceSettings] 读取失败：{ex.Message}（用默认值）");
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(LightPlugin.LightUserDataPath);
            File.WriteAllText(FilePath, ToJson(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[AppearanceSettings] 保存失败：{ex.Message}");
        }
    }

    private static string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"_comment\": \"Light In Dark 外观设置。本机生效，不会同步给别人。\",\n");
        sb.Append($"  \"selected\": {Str(Selected)},\n");
        sb.Append($"  \"fit\": {Fit},            // 0=自适应(填满) 1=原图(留黑边) 2=拉伸\n");
        sb.Append($"  \"dim\": {Dim},            // 0=0% 1=20% 2=40% 3=60%\n");
        sb.Append($"  \"hideCrewmates\": {(HideCrewmates ? "true" : "false")},\n");
        sb.Append($"  \"videoVolumePercent\": {VideoVolumePercent},   // 0~100，步长 10\n");
        sb.Append($"  \"buttonStyle\": {(int)ButtonStyle},      // 0=MOD样式 1=原版样式 2=亚克力样式\n");
        sb.Append("  \"buttonColors\": {");

        bool first = true;
        foreach (var kv in ButtonColors)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            if (!first) sb.Append(',');
            sb.Append($"\n    {Str(kv.Key)}: {Str(kv.Value)}");
            first = false;
        }
        sb.Append(first ? "}" : "\n  }");
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    // =====================================================================
    //  极简 JSON 解析（只认我们写出去的那几种：字符串 / 数字 / true,false / 一层嵌套对象）
    // =====================================================================

    private static void Parse(string text)
    {
        int i = 0;
        SkipWs(text, ref i);
        if (i >= text.Length || text[i] != '{') return;
        i++;

        while (i < text.Length)
        {
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] == '}') break;
            if (text[i] == ',') { i++; continue; }

            string key = ReadString(text, ref i);
            if (key == null) break;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != ':') break;
            i++;
            SkipWs(text, ref i);

            if (i < text.Length && text[i] == '{')
            {
                // 只可能是 buttonColors
                i++;
                while (i < text.Length)
                {
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] == '}') { i++; break; }
                    if (text[i] == ',') { i++; continue; }
                    string k2 = ReadString(text, ref i);
                    if (k2 == null) break;
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] != ':') break;
                    i++;
                    SkipWs(text, ref i);
                    string v2 = ReadString(text, ref i) ?? "";
                    ButtonColors[k2] = v2;
                }
                continue;
            }

            if (i < text.Length && text[i] == '"')
            {
                string sv = ReadString(text, ref i) ?? "";
                if (key == "selected") Selected = sv;
                continue;
            }

            // 数字 / 布尔
            int start = i;
            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != '\n') i++;
            string raw = text.Substring(start, i - start).Trim();
            ApplyScalar(key, raw);
        }
    }

    private static void ApplyScalar(string key, string raw)
    {
        // 行尾可能带 // 注释，砍掉
        int cm = raw.IndexOf("//", StringComparison.Ordinal);
        if (cm >= 0) raw = raw.Substring(0, cm).Trim();
        if (raw.Length == 0) return;

        bool isInt = int.TryParse(raw, out int n);
        switch (key)
        {
            case "fit": if (isInt) Fit = n; break;
            case "dim": if (isInt) Dim = n; break;
            case "videoVolumePercent": if (isInt) VideoVolumePercent = Mathf.Clamp(n, 0, 100); break;
            case "hideCrewmates": HideCrewmates = raw == "true" || raw == "1"; break;
            case "buttonStyle":
                // ⚠️ 2026-11-03 修：原来夹到 0..2 ✗ → 新增的 **美化版 = 3** 读盘时会被夹成 2（亚克力）✗✗
                //   → 这就是"模式没持久化"的直接原因 ✓（存 3、读回 2 ✓）
                if (isInt) ButtonStyle = (MainButtonStyle)Mathf.Clamp(n, 0, 2);
                break;
        }
    }

    /// <summary>
    /// 跳过空白 **和 <c>//</c> 行注释**。
    ///
    /// ⚠️⚠️ 注释必须在这里处理，不能只在读数值时顺手砍掉：
    ///   我们写出去的 JSON 每行末尾都带 <c>// 说明</c>（见 <see cref="ToJson"/>），
    ///   而解析主循环是"读 key → 读 value → 跳过逗号 → 读下一个 key"。
    ///   如果 SkipWs 不认注释，读完 `"fit": 0,` 之后跳过空格就撞上 `/`，
    ///   <c>ReadString</c> 返回 null → 主循环 <c>break</c> →
    ///   **后面所有字段全部读不到、每次重启静默重置成默认值**。
    ///   （实测踩到过：selected 读得到，dim/hideCrewmates/videoVolume/buttonStyle/buttonColors 全丢。）
    /// </summary>
    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }

            // // 行注释 → 一路跳到换行
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            break;
        }
    }

    /// <summary>读一个 JSON 字符串（含转义）。失败返回 null。</summary>
    private static string? ReadString(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != '"') return null;
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            sb.Append(e switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                _ => e,
            });
        }
        return sb.ToString();
    }

    // =====================================================================
    //  按钮颜色工具
    // =====================================================================

    /// <summary>取某个按钮的颜色覆盖；返回 null 表示没有覆盖。</summary>
    public static Color? GetButtonColor(string key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key)) return null;
        if (!ButtonColors.TryGetValue(key, out var hex)) return null;
        return ParseHex(hex);
    }

    public static void SetButtonColor(string key, Color? color)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key)) return;
        if (color == null) { ButtonColors.Remove(key); Save(); return; }
        ButtonColors[key] = ToHex(color.Value);
        Save();
    }

    public static void ClearButtonColors()
    {
        EnsureLoaded();
        ButtonColors.Clear();
        Save();
    }

    /// <summary>"#RRGGBB" → Color。失败返回 null。</summary>
    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length == 3)   // #RGB → #RRGGBB
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length == 8) s = s.Substring(0, 6);     // 忽略 alpha
        if (s.Length != 6) return null;

        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
        catch { return null; }
    }

    public static string ToHex(Color c)
        => $"#{(int)(Mathf.Clamp01(c.r) * 255f):X2}{(int)(Mathf.Clamp01(c.g) * 255f):X2}" +
           $"{(int)(Mathf.Clamp01(c.b) * 255f):X2}";
}
