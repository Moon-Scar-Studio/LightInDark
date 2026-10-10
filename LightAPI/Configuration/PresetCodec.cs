using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LightInDark.Core;

namespace LightInDark.Configuration;

/// <summary>
/// <c>.lidpreset</c> 的**载荷编解码**。
///
/// ═══════════════════════════════════════════════════════════════════════
///  【设计要点：位置化存储】
///
///  预设里最占空间的**不是值，是 key**（`role.Caller.count` 这种一条就 18 字符）。
///  所以这里**只存值、不存 key**：把配置项按 <see cref="ConfigItem.Key"/> 排序后
///  按固定顺序写值，读的时候用**同样的顺序**还原。
///
///  代价是"顺序必须完全一致" —— 所以载荷里带一个 **key 指纹**
///  （所有 key 按序拼接后的 FNV-1a 哈希，base36）。
///  注册表增删了任何一项 → 指纹变 → 加载时直接判定"预设与当前版本不匹配"，
///  **而不是把值错配到别的项上**（那才是最可怕的失败方式）。
///
///  【每条的编码】
///  | 类型   | 编码                          | 例        |
///  |--------|-------------------------------|-----------|
///  | Bool   | `1` / `0`                     | `1`       |
///  | Int    | 带符号 base36                 | `-1a`     |
///  | Float  | round-trip 短串（无损）        | `0.4`     |
///  | String | **选项下标**的 base36          | `2`       |
///
///  String 存下标而不是文本是最大的一处压缩：`"从未出现"` 是 12 字节，
///  下标 `2` 是 1 字节；而且换语言/改文案都不会让旧预设失效。
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
public static class PresetCodec
{
    /// <summary>载荷格式版本。改了编码规则就 +1，旧文件会被判为不兼容而不是读错。</summary>
    public const int FormatVersion = 2;

    private const string Base36Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// **本版本注册表的 key 指纹**。按 key 的序数排序后拼接，取 FNV-1a 的 base36。
    /// 任何一项增删都会让它变化 —— 这是"值不会错配"的唯一保障。
    /// </summary>
    public static string RegistryKeyHash()
    {
        var keys = ConfigRegistry.All
            .Where(i => i != null && !string.IsNullOrEmpty(i.Key))
            .Select(i => i.Key)
            .OrderBy(k => k, StringComparer.Ordinal);

        return Fnv1aBase36(string.Join("\u0001", keys));
    }

    /// <summary>把 "v2" 这样的版本串解析成数字（解析不出来当 1 = 最老的格式 ✓）</summary>
    private static int ParseVersion(string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token)) return 1;
            if (token[0] == 'v' || token[0] == 'V') token = token[1..];
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 1;
        }
        catch { return 1; }
    }

    /// <summary>当前注册表里的配置项条数（用来把"不匹配"的原因说清楚）。</summary>
    public static int RegistryItemCount()
        => ConfigRegistry.All.Count(i => i != null && !string.IsNullOrEmpty(i.Key));

    /// <summary>把当前所有配置项编码成载荷文本（不含文件头）。</summary>
    public static string Encode()
    {
        // ⚠️ 顺序必须和 RegistryKeyHash 里**完全一样**（同一套排序），否则值会错位。
        var items = ConfigRegistry.All
            .Where(i => i != null && !string.IsNullOrEmpty(i.Key))
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder(items.Count * 2);
        sb.Append("v").Append(FormatVersion)
          .Append('|').Append(items.Count.ToString(CultureInfo.InvariantCulture))
          .Append('|').Append(RegistryKeyHash())
          .Append('|');

        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            // ⚠️ v2：值的**前面带上 key** ✓（原来是"只按位置存值" ✗）
            //    用户 2026-10-06 要求："当前配置项少于预设项时……" —— 要支持"旧预设项更少也能加载"，
            //    就必须能**按 key 对上号** ✓；只按位置的写法一旦两边项数不同就整体错位 ✗
            sb.Append(items[i].Key).Append('=').Append(EncodeOne(items[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 解码载荷并<b>应用</b>到配置项上。
    /// 返回 (成功, 说明) —— 失败时**不做任何修改**（宁可整个不加载，也不要写一半）。
    /// </summary>
    public static (bool Ok, string Message) DecodeAndApply(string? payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload))
                return (false, "载荷为空");

            // 切 4 段：v N | 条数 | 指纹 | 值表
            var head = payload.Split('|');
            if (head.Length < 4)
                return (false, $"载荷格式不对（只有 {head.Length} 段，至少要 4 段）");

            if (head[0] != "v" + FormatVersion)
                return (false, $"载荷版本是 {head[0]}，本版本只认 v{FormatVersion}");

            // ★★ 2026-10-06 用户要求放宽判定（原来"指纹一变就整份拒绝"太严 ✗）：
            //    新规则：
            //      · 预设的**格式版本比我们新** → 拒绝 ✓（未来版本，读不懂）
            //      · 预设记的**配置项比我们现在多** → 拒绝 ✓（例：预设 20 项、当前只有 8 项）
            //      · 其余情况**按 key 逐项应用** ✓，本版本没有的项**跳过**并报数 ✓
            if (!int.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                return (false, $"条数字段不是数字：'{head[1]}'");

            int fileVersion = ParseVersion(head[0]);

            if (fileVersion > FormatVersion)
                return (false, $"预设格式版本比本版本新（{head[0]} > v{FormatVersion}）—— 拒绝加载");

            var items = ConfigRegistry.All
                .Where(i => i != null && !string.IsNullOrEmpty(i.Key))
                .OrderBy(i => i.Key, StringComparer.Ordinal)
                .ToList();

            if (count > items.Count)
                return (false, $"预设里的配置项比当前版本多（文件 {count} 项，当前 {items.Count} 项）—— 拒绝加载");

            // ⚠️ 值表里**不能**再按 '|' 切 —— Float 的 round-trip 串不会含 '|'，
            //    但为了以后扩类型安全，这里用 '|' 之后**剩余的全部**再按 ',' 切。
            int barIdx = payload.IndexOf('|', payload.IndexOf('|', payload.IndexOf('|') + 1) + 1);
            var tail = barIdx >= 0 ? payload[(barIdx + 1)..] : "";
            var tokens = tail.Length == 0 ? Array.Empty<string>() : tail.Split(',');

            // ── v2：key=value，按 key 应用（缺的跳过 ✓）──
            if (fileVersion >= 2)
            {
                var byKey = new Dictionary<string, ConfigItem>(StringComparer.Ordinal);
                foreach (var it in items) byKey[it.Key] = it;

                var pending = new List<(ConfigItem Item, float Value)>();
                int skipped = 0;

                foreach (var token in tokens)
                {
                    if (string.IsNullOrEmpty(token)) continue;
                    int eq = token.IndexOf('=');
                    if (eq <= 0) return (false, $"值格式不对（缺少 key=value 形式）：'{token}'");

                    string key = token[..eq];
                    string raw = token[(eq + 1)..];

                    if (!byKey.TryGetValue(key, out var item)) { skipped++; continue; }   // 本版本没有 → 跳过 ✓

                    if (!TryDecodeOne(item, raw, out float v, out var why))
                        return (false, $"配置项 {key} 解析失败：{why}");

                    pending.Add((item, v));
                }

                for (int i = 0; i < pending.Count; i++)
                    pending[i].Item.SetValueSilently(pending[i].Value);

                return (true, skipped > 0
                    ? $"已应用 {pending.Count} 项，跳过 {skipped} 项（本版本没有的配置项）"
                    : $"已应用 {pending.Count} 项");
            }

            // ── v1：老格式，只能按位置应用（要求项数一致 ✓）──
            if (count != items.Count)
                return (false, $"旧格式预设（v1）只能整份加载：文件 {count} 项，当前 {items.Count} 项");

            if (tokens.Length != items.Count)
                return (false, $"值的个数不符：文件里是 {tokens.Length}，当前有 {items.Count}");

            // ★ 先全部解析成值，**确认都成功之后再统一写入** ——
            //   避免"写到一半发现格式错"留下半套脏配置。
            var parsed = new float[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!TryDecodeOne(items[i], tokens[i], out parsed[i], out var why))
                    return (false, $"第 {i + 1} 项（{items[i].Key}）解析失败：{why}");
            }

            for (int i = 0; i < items.Count; i++)
                items[i].SetValueSilently(parsed[i]);

            return (true, $"已应用 {items.Count} 项（旧格式 v1）");
        }
        catch (Exception ex)
        {
            return (false, $"解码异常：{ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    //  单项
    // ---------------------------------------------------------------------

    private static string EncodeOne(ConfigItem item)
    {
        switch (item.Type)
        {
            case ConfigType.Bool:
                return item.GetBool() ? "1" : "0";

            case ConfigType.Value:
            {
                // 存**选项下标**：最省空间，且换语言/改文案不影响旧预设。
                int idx = IndexOfSelection(item);
                return ToBase36(idx);
            }

            case ConfigType.Int:
                return ToBase36(item.GetInt());

            default: // Float / 其它
                // "R" = round-trip，保证 float → string → float 无损；
                // 对我们这些 0.4 / 100 之类的值，它输出就是 "0.4" / "100"，一样短。
                return item.GetFloat().ToString("R", CultureInfo.InvariantCulture);
        }
    }

    private static bool TryDecodeOne(ConfigItem item, string token, out float value, out string why)
    {
        value = 0f;
        why = "";
        token = token?.Trim() ?? "";

        switch (item.Type)
        {
            case ConfigType.Bool:
                if (token != "0" && token != "1") { why = $"Bool 只能是 0/1，收到 '{token}'"; return false; }
                value = token == "1" ? 1f : 0f;
                return true;

            case ConfigType.Value:
            {
                if (!TryParseBase36(token, out int idx)) { why = $"下标不是 base36：'{token}'"; return false; }

                var sel = item.Selections;
                if (sel == null || sel.Length == 0) { why = "该项没有选项列表"; return false; }
                if (idx < 0 || idx >= sel.Length) { why = $"下标 {idx} 越界（只有 {sel.Length} 个选项）"; return false; }

                value = idx;
                return true;
            }

            case ConfigType.Int:
                if (!TryParseBase36(token, out int iv)) { why = $"不是 base36 整数：'{token}'"; return false; }
                value = iv;
                return true;

            default:
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                { why = $"不是合法浮点数：'{token}'"; return false; }
                value = fv;
                return true;
        }
    }

    private static int IndexOfSelection(ConfigItem item)
    {
        var sel = item.Selections;
        if (sel == null || sel.Length == 0) return 0;

        int cur = item.GetInt();
        return (cur >= 0 && cur < sel.Length) ? cur : 0;
    }

    // ---------------------------------------------------------------------
    //  base36 / FNV-1a
    // ---------------------------------------------------------------------

    private static string ToBase36(int v)
    {
        if (v == 0) return "0";
        bool neg = v < 0;
        // ⚠️ int.MinValue 取负会溢出 → 先提到 long
        long n = Math.Abs((long)v);
        var buf = new StringBuilder(8);
        while (n > 0) { buf.Insert(0, Base36Digits[(int)(n % 36)]); n /= 36; }
        return neg ? "-" + buf : buf.ToString();
    }

    private static bool TryParseBase36(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;

        bool neg = s[0] == '-';
        int start = neg ? 1 : 0;
        if (start >= s.Length) return false;

        long acc = 0;
        for (int i = start; i < s.Length; i++)
        {
            int d = Base36Digits.IndexOf(char.ToLowerInvariant(s[i]));
            if (d < 0) return false;
            acc = acc * 36 + d;
            if (acc > int.MaxValue) return false;
        }
        value = (int)(neg ? -acc : acc);
        return true;
    }

    /// <summary>FNV-1a 32 位 → base36。只用来做"注册表变没变"的指纹，不需要抗碰撞强度。</summary>
    private static string Fnv1aBase36(string s)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint h = offset;
            foreach (char c in s)
            {
                h ^= (byte)(c & 0xFF);
                h *= prime;
                h ^= (byte)(c >> 8);
                h *= prime;
            }
            return ToBase36((int)(h & 0x7FFFFFFF));
        }
    }
}
