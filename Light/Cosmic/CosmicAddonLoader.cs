using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightInDark.Core;

namespace Light.Cosmic;

/// <summary>颜色模式（用户 2026-10-06 明确语义 ✓）</summary>
public enum CosmicColorMode
{
    /// <summary>
    /// `player`：**原图当遮罩** —— **红通道 = 玩家颜色**、**蓝通道 = 阴影色**，
    /// 装上后整体按玩家颜色渲染 ✓（原版帽子的着色方式 ✓）
    /// </summary>
    Player = 0,

    /// <summary>`fixed`：**按原图渲染、完全不换色** ✓（彩色渐变 / 纯黑描边这类素材用 ✓）</summary>
    Fixed = 1,
}

/// <summary>一件自定义装扮（帽子 / 皮肤 / 面罩 / 名牌 通用 ✓）</summary>
public sealed class CosmicItem
{
    /// <summary>JSON 里的键（如 <c>mypack_crown</c>）—— 全局唯一 id 的一部分 ✓</summary>
    public string LocalId = "";

    /// <summary>所属插件的 Id（<c>AddonInfo.Id</c>）✓</summary>
    public string AddonId = "";

    /// <summary>**全局 id** = <c>{AddonId}/{LocalId}</c> —— 注册表/查重都用它 ✓（稳定字符串，不用序号 ✓）</summary>
    public string FullId => $"{AddonId}/{LocalId}";

    /// <summary>zip 内的相对路径（如 <c>hats/crown.png</c>）✓</summary>
    public string Path = "";

    public CosmicColorMode ColorMode = CosmicColorMode.Player;
    public string Author = "";
    public string DisplayName = "";

    public float OffsetX;
    public float OffsetY;
    public float Scale = 1f;
    public int Frames = 1;
    public int FrameDelay;
    public bool FlipX;
    public bool BehindBody;

    /// <summary>png 字节（**在 zip 还开着的时候就读出来** ✓ —— 之后不再依赖磁盘 ✓）</summary>
    public byte[] PngBytes = Array.Empty<byte>();

    /// <summary>所属类别（用于日志/UI 分组 ✓）</summary>
    public string Category = "";
}

/// <summary>一个装扮插件（一个 zip ✓）</summary>
public sealed class CosmicAddon
{
    public string Id = "";
    public string Name = "";
    public string Version = "";
    public string Author = "";
    public string Describe = "";
    public string ZipPath = "";

    public readonly Dictionary<string, List<CosmicItem>> ItemsByCategory = new();

    public int TotalItems
    {
        get
        {
            int n = 0;
            foreach (var kv in ItemsByCategory) n += kv.Value.Count;
            return n;
        }
    }
}

// ───────────────────────── JSON 模型（字段名与用户约定一致 ✓）─────────────────────────

internal sealed class AddonInfoJson
{
    [JsonPropertyName("Id")] public string? Id { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Version")] public string? Version { get; set; }
    [JsonPropertyName("Author")] public string? Author { get; set; }
    [JsonPropertyName("Describe")] public string? Describe { get; set; }
}

internal sealed class CosmicItemJson
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("colorMode")] public string? ColorMode { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("offset")] public CosmicOffsetJson? Offset { get; set; }
    [JsonPropertyName("scale")] public float? Scale { get; set; }
    [JsonPropertyName("frames")] public int? Frames { get; set; }
    [JsonPropertyName("frameDelay")] public int? FrameDelay { get; set; }
    [JsonPropertyName("flipX")] public bool? FlipX { get; set; }
    [JsonPropertyName("behindBody")] public bool? BehindBody { get; set; }
}

internal sealed class CosmicOffsetJson
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
}

internal sealed class CosmicInfoJson
{
    // ⚠️ 用户 2026-10-06：**没有 `formatVersion`**（那是粘贴错了 ✓）——
    //    这里**故意不定义**这个字段 ✓，JSON 里若残留也会被忽略 ✓（`System.Text.Json` 默认忽略未知字段 ✓）
    [JsonPropertyName("Hats")] public Dictionary<string, CosmicItemJson>? Hats { get; set; }
    [JsonPropertyName("Skins")] public Dictionary<string, CosmicItemJson>? Skins { get; set; }
    [JsonPropertyName("Visors")] public Dictionary<string, CosmicItemJson>? Visors { get; set; }
    [JsonPropertyName("NamePlates")] public Dictionary<string, CosmicItemJson>? NamePlates { get; set; }
}

/// <summary>
/// **CosmicAddons 加载器**（第 1 步：发现 zip → 解析 → 建注册表 ✓）。
///
/// 约定（用户 2026-10-06 给出 ✓）：
/// <code>
///   &lt;游戏目录&gt;/CosmicAddons/
///       SomeAddon.zip
///           AddonInfo.json      ← Id / Name / Version / Author / Describe
///           CosmicInfo.json     ← Hats / Skins / Visors / NamePlates（**没有 formatVersion** ✓）
///           Resources/
///               Hats/crown.png …
/// </code>
///
/// 规矩：
///   · 装扮 id 只允许 <c>[a-z0-9_]</c> ✓（非法 → 警告并跳过该件 ✓）
///   · **全局 id 重复 → 警告，保留先加载的那个** ✓（用户指定 ✓）
///   · 解析失败**只警告不抛** ✓（一个坏 zip 不能拖垮整局 ✓）
///   · png 字节**在 zip 打开期间读出来** ✓（之后就不依赖磁盘了 ✓）
///   · **第 1 步只做到"解析 + 注册表"** ✓ —— 注入游戏（CosmeticsCache / 装扮页签）是第 2 步 ✓
/// </summary>
public static class CosmicAddonLoader
{
    /// <summary><c>&lt;游戏目录&gt;/CosmicAddons</c>（BepInEx 的 `Paths.GameRootPath` ✓）</summary>
    public static string AddonsDirectory
    {
        get
        {
            try { return Path.Combine(BepInEx.Paths.GameRootPath, "CosmicAddons"); }
            catch { return "CosmicAddons"; }
        }
    }

    private static readonly List<CosmicAddon> _addons = new();
    private static readonly Dictionary<string, CosmicItem> _byFullId = new(StringComparer.Ordinal);
    private static bool _loaded;

    public static IReadOnlyList<CosmicAddon> Addons => _addons;
    public static int ItemCount => _byFullId.Count;

    /// <summary>按全局 id 取一件装扮（第 2 步注入游戏时要用 ✓）</summary>
    public static CosmicItem? Get(string fullId)
        => !string.IsNullOrEmpty(fullId) && _byFullId.TryGetValue(fullId, out var it) ? it : null;

    /// <summary>加载全部插件（幂等 ✓，失败只警告 ✓）</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var dir = AddonsDirectory;
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
                LightLogger.Log($"[CosmicAddons] 目录不存在，已创建：{dir}（把插件 zip 放进去即可 ✓）");
                return;
            }

            var zips = Directory.GetFiles(dir, "*.zip", SearchOption.TopDirectoryOnly);
            Array.Sort(zips, StringComparer.OrdinalIgnoreCase);      // ★ 排序保证"先加载的赢"是**确定**的 ✓

            int ok = 0, failed = 0;
            foreach (var zip in zips)
            {
                try
                {
                    var addon = LoadOne(zip);
                    if (addon == null) { failed++; continue; }
                    _addons.Add(addon);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LightLogger.LogWarning($"[CosmicAddons] 读取失败 {Path.GetFileName(zip)}：{ex.Message}");
                }
            }

            LogSummary(ok, failed);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[CosmicAddonLoader.Load]", ex);
        }
    }

    /// <summary>读一个 zip（**全程流式，不解压到磁盘** ✓）</summary>
    private static CosmicAddon? LoadOne(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var info = ReadJson<AddonInfoJson>(zip, "AddonInfo.json");
        if (info == null)
        {
            LightLogger.LogWarning($"[CosmicAddons] {Path.GetFileName(zipPath)} 缺少 AddonInfo.json → 跳过 ✓");
            return null;
        }

        string addonId = (info.Id ?? "").Trim();
        if (!IsValidId(addonId) || string.IsNullOrEmpty(addonId))
        {
            LightLogger.LogWarning($"[CosmicAddons] {Path.GetFileName(zipPath)} 的 Id「{addonId}」非法" +
                                   "（只允许 [a-z0-9_]）→ 跳过 ✓");
            return null;
        }

        if (_addons.Exists(a => a.Id == addonId))
        {
            // 用户指定：同 ID **警告 + 先加载的赢** ✓
            LightLogger.LogWarning($"[CosmicAddons] 插件 Id「{addonId}」重复 → 保留先加载的，" +
                                   $"忽略 {Path.GetFileName(zipPath)} ✓");
            return null;
        }

        var addon = new CosmicAddon
        {
            Id = addonId,
            Name = info.Name ?? addonId,
            Version = info.Version ?? "",
            Author = info.Author ?? "",
            Describe = info.Describe ?? "",
            ZipPath = zipPath,
        };

        var cosmic = ReadJson<CosmicInfoJson>(zip, "CosmicInfo.json");
        if (cosmic == null)
        {
            LightLogger.LogWarning($"[CosmicAddons] {addon.Name} 缺少 CosmicInfo.json → 该插件没有任何装扮 ✓");
            return addon;
        }

        AddCategory(zip, addon, "Hats", cosmic.Hats);
        AddCategory(zip, addon, "Skins", cosmic.Skins);
        AddCategory(zip, addon, "Visors", cosmic.Visors);
        AddCategory(zip, addon, "NamePlates", cosmic.NamePlates);

        LightLogger.Log($"[CosmicAddons] 已加载「{addon.Name}」v{addon.Version} by {addon.Author}" +
                        $"（{addon.TotalItems} 件：{CountText(addon)}）✓");
        return addon;
    }

    private static void AddCategory(ZipArchive zip, CosmicAddon addon, string category,
                                    Dictionary<string, CosmicItemJson>? source)
    {
        if (source == null || source.Count == 0) return;
        if (!addon.ItemsByCategory.TryGetValue(category, out var list))
        {
            list = new List<CosmicItem>();
            addon.ItemsByCategory[category] = list;
        }

        foreach (var kv in source)
        {
            try
            {
                string localId = (kv.Key ?? "").Trim();
                var src = kv.Value;

                if (!IsValidId(localId) || string.IsNullOrEmpty(localId))
                {
                    LightLogger.LogWarning($"[CosmicAddons] {addon.Name}/{category} 的 id「{localId}」非法" +
                                           "（只允许 [a-z0-9_]）→ 跳过该件 ✓");
                    continue;
                }
                if (src == null || string.IsNullOrWhiteSpace(src.Path))
                {
                    LightLogger.LogWarning($"[CosmicAddons] {addon.Name}/{category}/{localId} 没写 path → 跳过 ✓");
                    continue;
                }

                var item = new CosmicItem
                {
                    LocalId = localId,
                    AddonId = addon.Id,
                    Category = category,
                    Path = src.Path!.Replace('\\', '/'),
                    ColorMode = ParseColorMode(src.ColorMode),
                    Author = string.IsNullOrWhiteSpace(src.Author) ? addon.Author : src.Author!,
                    DisplayName = string.IsNullOrWhiteSpace(src.DisplayName) ? localId : src.DisplayName!,
                    OffsetX = src.Offset?.X ?? 0f,
                    OffsetY = src.Offset?.Y ?? 0f,
                    Scale = src.Scale is > 0f ? src.Scale.Value : 1f,
                    Frames = src.Frames is > 0 ? src.Frames.Value : 1,
                    FrameDelay = src.FrameDelay ?? 0,
                    FlipX = src.FlipX ?? false,
                    BehindBody = src.BehindBody ?? false,
                };

                // 全局 id 查重（跨插件 ✓）—— 同样是"先加载的赢" ✓
                string fullId = item.FullId;
                if (_byFullId.ContainsKey(fullId))
                {
                    LightLogger.LogWarning($"[CosmicAddons] 装扮 id「{fullId}」重复 → 保留先加载的 ✓");
                    continue;
                }

                // ★ 趁 zip 还开着把 png 读出来 ✓（之后不再依赖磁盘 ✓）
                var bytes = ReadEntryBytes(zip, item.Path);
                if (bytes == null || bytes.Length == 0)
                {
                    LightLogger.LogWarning($"[CosmicAddons] {fullId} 找不到贴图 Resources/{item.Path} → 跳过 ✓");
                    continue;
                }
                item.PngBytes = bytes;

                _byFullId[fullId] = item;
                list.Add(item);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[CosmicAddons] {addon.Name}/{category} 有一件解析失败：{ex.Message}");
            }
        }
    }

    /// <summary>`colorMode` 解析（默认 `player` ✓ —— 与原版帽子行为一致 ✓）</summary>
    private static CosmicColorMode ParseColorMode(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "fixed" => CosmicColorMode.Fixed,
            "player" => CosmicColorMode.Player,
            _ => CosmicColorMode.Player,
        };
    }

    /// <summary>
    /// 装扮 id 规范（用户 2026-10-06）：只允许 <c>[a-z0-9_]</c> ✓
    /// （大写/中文/连字符一律非法 → 警告并跳过 ✓）
    /// </summary>
    private static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (char c in id)
        {
            bool okChar = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!okChar) return false;
        }
        return true;
    }

    private static T? ReadJson<T>(ZipArchive zip, string entryName) where T : class
    {
        try
        {
            var entry = FindEntry(zip, entryName);
            if (entry == null) return null;

            using var s = entry.Open();
            return JsonSerializer.Deserialize<T>(s, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,     // 字段大小写不敏感（对作者宽容一点 ✓）
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicAddons] 解析 {entryName} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 找 entry（**大小写不敏感 + 分隔符不敏感** ✓）。
    ///
    /// ⚠️⚠️ 2026-10-10 日志实证的坑：Windows 的 `Compress-Archive` 打包时，
    ///    entry 名里的分隔符是**反斜杠**（`Resources\Hats\x.png`）✗，
    ///    而正常 zip（其它打包工具/跨平台）用**正斜杠** ✓。
    ///    原来只把"要找的名字"换成正斜杠 ✗，没规范 entry 名 → **一个都找不到** ✓
    ///    （日志：「找不到贴图 Resources/hats/example_hat.png → 跳过」✓）
    ///    → 现在两边都规范成 `/` 再比 ✓
    /// </summary>
    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name)
    {
        var want = Normalize(name);

        foreach (var e in zip.Entries)
            if (string.Equals(Normalize(e.FullName), want, StringComparison.OrdinalIgnoreCase)) return e;

        // 兜底：允许作者把文件放进一层子目录（如 `Addon/AddonInfo.json` ✓）
        foreach (var e in zip.Entries)
            if (Normalize(e.FullName).EndsWith("/" + want, StringComparison.OrdinalIgnoreCase)) return e;

        return null;
    }

    /// <summary>把路径规范成"正斜杠 + 无首尾斜杠"（zip 里反斜杠/正斜杠都可能出现 ✓）</summary>
    private static string Normalize(string path)
        => (path ?? "").Replace('\\', '/').Trim().TrimStart('/').TrimEnd('/');

    private static byte[]? ReadEntryBytes(ZipArchive zip, string relativePath)
    {
        try
        {
            var entry = FindEntry(zip, relativePath)
                        ?? FindEntry(zip, "Resources/" + relativePath);
            if (entry == null) return null;

            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static string CountText(CosmicAddon addon)
    {
        var parts = new List<string>();
        foreach (var kv in addon.ItemsByCategory)
            if (kv.Value.Count > 0) parts.Add($"{kv.Key} {kv.Value.Count}");
        return parts.Count > 0 ? string.Join(" / ", parts) : "无";
    }

    private static void LogSummary(int ok, int failed)
    {
        LightLogger.Log($"[CosmicAddons] 加载完成：插件 {ok} 个" +
                        (failed > 0 ? $"（{failed} 个失败）" : "") +
                        $"，装扮共 {ItemCount} 件 ✓  目录：{AddonsDirectory}");
    }
}
