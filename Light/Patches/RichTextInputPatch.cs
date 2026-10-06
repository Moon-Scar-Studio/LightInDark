using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using LightInDark.Core;

namespace Light.Patches;

/// <summary>
/// **让原版聊天框能输入富文本标签（`&lt;color=red&gt;` 这类）。**
///
/// ═══════════════════════════════════════════════════════════════════════
///  【拦路虎：输入过滤】
///
///  <c>TextBoxTMP.IsCharAllowed</c>（19.0 反编译源码 L296-303）：
/// <code>
///   return i == ' ' || (A-Z) || (a-z) || (0-9) || (À-ÿ) || (Ѐ-џ)
///       || ('\u3040'..'㆟') || ('ⱡ'..'힣')            // 中日韩
///       || (AllowSymbols &amp;&amp; SymbolChars.Contains(i))   // 只有 ?!,.'():;/\%%^&amp;-=¿？#
///       || (AllowEmail &amp;&amp; EmailChars.Contains(i));
/// </code>
///     · 富文本标签的 **`&lt;` `&gt;` 不在允许表里** → 打不进去；
///     · 中文/全角标点也有漏网的。
///     → 所以必须 patch 掉它（本文件的 <see cref="IsCharAllowedPatch"/>）。
///
///  ⚠️⚠️ **本文件原本还有"Unicode 表情包"那一半（从系统字体烘 emoji Font Asset 注册全局
///     fallback）—— 已按用户 2026-10-06 的要求整段删除** ✓ 原因：
///     `Segoe UI Emoji` 是**彩色位图字体（CBDT）**，TMP 烘出来是坏字形 → 满屏豆腐块。
///     现在 emoji 一律由 <c>IsUnsafe</c> **挡在输入层**（打不进来 = 不会出现豆腐块）✓
///     等以后有**离线烘好的 emoji Font Asset** 再考虑放开输入。
/// ═══════════════════════════════════════════════════════════════════════
public static class RichTextInputPatch
{
    // =====================================================================
    //  放开输入过滤（危险字符否决 + 原版结论 + 白名单补放行）
    // =====================================================================

    /// <summary>
    /// 让输入框能打进「原版会拒、但我们能显示」的字符（中文、全角标点、富文本标点），
    /// 同时**否决**会毁排版的字符（哪怕原版放行）。
    ///
    /// ⚠️ 为什么必须"既能补放行、也能否决"：
    ///   · 只补放行 → 原版放行的零宽字符 / **代理对的一半**会漏进来；
    ///     删字时留下孤立代理或零宽字符 → 冒出一个方块、之后光标与输入整体错乱（用户报的症状）。
    ///   · 前缀式"全盘接管" → 又会让中文被吞。
    ///   → 所以：先否决危险字符，再把原版结论交回，最后按白名单补放行。
    /// </summary>
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.IsCharAllowed))]
    public static class IsCharAllowedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TextBoxTMP __instance, char i, ref bool __result)
        {
            try
            {
                if (__instance != null && __instance.IpMode) return;   // IP 输入框不干预

                // ① 危险字符：直接否决
                if (IsUnsafe(i)) { ReportSwallowed(i, "危险字符"); __result = false; return; }

                // ② 原版放行的照旧放行
                if (__result) return;

                // ③ 原版拒绝的，按白名单补放行（中文 / 全角标点 / 富文本标点）
                if (IsPrintable(i)) { __result = true; return; }

                // ④ 原版拒绝、我们也不放行 —— 若是个"看得出用户想输入"的字符，记一条日志
                //    （用来判定"吞字"到底是不是我们造成的）
                ReportSwallowed(i, "原版拒绝且未补放行");
            }
            catch { }
        }
    }

    /// <summary>
    /// 会让排版 / 光标出问题的字符：无字形（豆腐块）、零宽、**代理对的一半**、私有使用区等。
    /// 其中代理对最要命 —— 删字只删掉一半就留下孤立代理，后续输入直接报废。
    /// </summary>
    private static bool IsUnsafe(char c)
    {
        // ⚠️⚠️ 退格（U+0008）**必须判定为"不可插入"**。
        //   日志实证：`[ChatRichText.诊断] 输入框出现可疑字符 U+0008 … U+0031 U+0032 U+0008`
        //   —— Among Us 的输入循环是「先问 IsCharAllowed，再处理退格」，
        //   原版对 '\b' 返回 false 才会落到删除分支；我们一旦放行，'\b' 就被当普通字符塞进文本，
        //   渲染成方块并污染 TMP 的字符/光标索引（＝用户报的「删字冒方块、之后输入全乱」）。
        //   （char.IsControl 已覆盖 '\b'，这里不再为它开任何例外。）
        if (char.IsControl(c)) return true;                  // 控制字符（含退格、换行等）—— 唯一有日志实证必须挡的

        // ⚠️ 以下只挡**看不见**的字符：它们画不出来，挡掉不会让用户"打不进字"，
        //    却能防住"删字留下零宽字符 → 光标/索引错乱"。
        if (c == '\u00AD') return true;                      // 软连字符
        if (c >= '\u200B' && c <= '\u200F') return true;     // 零宽空格 / 方向标记
        if (c >= '\u2028' && c <= '\u202E') return true;     // 行分隔符 / 双向控制
        if (c >= '\u2060' && c <= '\u206F') return true;     // 词连接符 / 不可见运算符 / 双向隔离
        if (c == '\uFEFF') return true;                      // BOM / 零宽不换行空格
        if (c >= '\uFE00' && c <= '\uFE0F') return true;     // 变体选择符（单独出现无意义）

        // ⚠️⚠️ 2026-10-06 用户报「聊天框吞字，有一些字打不进去」—— 根因就是这里**原来还挡了可见字符**：
        //   `\u2600-\u27BF`（★☆♥♪♠…）、`\u2B00-\u2BFF`（⭐⬛…）、`\uE000-\uF8FF`（私有使用区）、
        //   `\uFFF0` 以上、以及 **所有代理对（emoji）**。
        //   那些范围是当初"怕出现豆腐块"时**猜**着加的，而日志真正证明的元凶只有 '\b' 一个 ✗
        //   → 现在**放行**这些可见符号：用户能打进去了 ✓
        //     （字体缺字形时显示成方块 = **原版行为**，不是本模组引入的问题；
        //       而且用户已明确要求删掉 emoji 字体回退那套逻辑。）
        //
        // ⚠️ 代理对（emoji）**仍然挡着**，这是刻意的：
        //   · 原版 `TextBoxTMP.IsCharAllowed` 本来也不允许它们 → 不算功能倒退 ✓
        //   · 放开后"删字只删一半"会留下孤立代理 → 正是用户之前报的「删字冒方块」 ✗
        //   要放开 emoji 的话，必须同时做"删除时的孤立代理清理"，那需要另开一轮 ✓
        if (char.IsSurrogate(c)) return true;                // emoji 等非 BMP（见上面说明）

        return false;
    }

    /// <summary>能正常显示的字符（中文、全角标点、富文本标点等）。</summary>
    private static bool IsPrintable(char c) => !IsUnsafe(c);

    // ---- 输入序列诊断：每次文本变化打一条（有上限，避免刷屏）----
    private static string _lastSeenText = "";
    private static int _textChangeLogs;

    /// <summary>
    /// 记录聊天框文本的每一次变化（带码位）。用来定位「第几个字被吞」：
    /// 打几个字却少一个，码位序列里会直接空出来。
    /// </summary>
    public static void LogInputChange(FreeChatInputField field)
    {
        try
        {
            if (_textChangeLogs >= 300) return;

            string text = "";
            try { text = field.Text ?? ""; } catch { }
            if (text == _lastSeenText) return;
            _lastSeenText = text;

            _textChangeLogs++;
            var sb = new System.Text.StringBuilder(64 + text.Length * 7);
            sb.Append("[RichTextInput.输入诊断] 文本变化（").Append(text.Length).Append(" 字）：");
            for (int i = 0; i < text.Length; i++)
                sb.Append("U+").Append(((int)text[i]).ToString("X4")).Append(' ');
            LightLogger.LogDebug(sb.ToString());
        }
        catch { }
    }

    /// <summary>已报告过的"被吞"码位（每种只报一次）。</summary>
    private static readonly HashSet<int> _reportedSwallowed = new();

    /// <summary>
    /// 记录一个被判定为"不可输入"的字符。
    ///
    /// ⚠️ 只在它看起来是用户**想输入**的字符时记录（字母/数字/标点/符号）：
    ///    控制字符、代理对被吞是**预期行为**（它们画不出来，见 `IsUnsafe`）。
    ///    用途：判定「中文吞字」到底是不是本模组的过滤造成的 ——
    ///    若复现吞字时日志里出现这条，说明是我们拦截了它，按码位精准修；
    ///    若日志始终不出现，说明字符压根没进到过滤这一层，得往输入法/游戏侧查。
    /// </summary>
    private static void ReportSwallowed(char c, string why)
    {
        try
        {
            if (char.IsControl(c) || char.IsSurrogate(c)) return;   // 预期被吞，不报
            bool intended = char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c);
            if (!intended) return;
            if (!_reportedSwallowed.Add(c)) return;                 // 每种码位只报一次

            LightLogger.LogWarning($"[RichTextInput.吞字诊断] 字符被拦截：'{c}' U+{((int)c):X4}" +
                                   $"（原因：{why}；字母={char.IsLetter(c)} 数字={char.IsDigit(c)} 标点={char.IsPunctuation(c)}）");
        }
        catch { }
    }

}
