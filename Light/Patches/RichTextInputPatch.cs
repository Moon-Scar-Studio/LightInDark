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
    /// ★★ 2026-10-06 「中文挑字吞」**决定性取证** —— 盯住唯一的字符入口。
    ///
    /// 原版 `TextBoxTMP.SetText(string input, string inputCompo = "")`（19.0 L216-243）是**所有**
    /// 输入字符进入输入框的唯一漏斗 ✓：
    /// <code>
    ///   foreach (char c2 in input) {
    ///       ...
    ///       if (!this.IsCharAllowed(c2)) this.AdjustCaretPosition(-1);   // ← 被拒：不追加，只挪光标
    ///       else this.tempTxt.Append(c2);
    ///   }
    /// </code>
    /// 所以只要看这一条日志，就能**一次分清**三种可能：
    ///   · `input` 里**有**那个字 → 字符到了 ✓ → 那就是我们再往下游（`IsCharAllowed` / TMP）的问题
    ///   · `input` 里没有、`compo` 里**有** → 输入法**还在合成**没提交 → 是输入法/系统侧
    ///   · 两个都**没有** → 字符压根没进游戏 → 输入法/系统侧（`Input.imeCompositionMode` 的锅）
    /// </summary>
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
        /// <summary>
        /// ★★ 2026-10-06 「中文挑字吞」**决定性取证** —— 盯住唯一的字符入口。
        ///
        /// 原版 `TextBoxTMP.SetText(string input, string inputCompo = "")`（19.0 L216-243）是**所有**
        /// 输入字符进入输入框的唯一漏斗 ✓：
        /// <code>
        ///   foreach (char c2 in input) {
        ///       ...
        ///       if (!this.IsCharAllowed(c2)) this.AdjustCaretPosition(-1);   // ← 被拒：不追加，只挪光标
        ///       else this.tempTxt.Append(c2);
        ///   }
        /// </code>
        /// 所以这一条日志就能**一次分清**三种可能：
        ///   · `input` 里**有**那个字 → 字符到了 ✓ → 问题在我们下游（`IsCharAllowed` / TMP）
        ///   · `input` 里没有、`compo` 里**有** → 输入法**还在合成**没提交 → 输入法/系统侧
        ///   · 两个都**没有** → 字符压根没进游戏 → 输入法/系统侧
        /// </summary>
        [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText))]
        public static class SetTextDiagnosePatch
        {
            private static readonly HashSet<int> _loggedHashes = new();
            private static int _count;

            [HarmonyPrefix]
            public static void Prefix(TextBoxTMP __instance, ref string input, string inputCompo)
            {
                try
                {
                    // ★★ 真正的过滤在这里做（2026-10-06 定案）★★
                    //    为什么搬到这里：`IsCharAllowed` 那个 hook 的 `char` 参数**高位被截断** ✗
                    //    （打"者" U+8005 → 收到 0x05），在那里**没法正确判断字符** ✗
                    //    而 `SetText` 的 `input` 是**完整正确的字符串** ✓（日志实证 ✓）
                    //    → 在这里清理：去掉会毁排版/光标的字符，**保留 `\b` `\r` `\n`** ✓
                    //      （退格与回车是原版 `SetText` / 聊天发送逻辑自己处理的，必须原样传下去 ✓）
                    if (input != null && input.Length > 0)
                        input = Sanitize(input, __instance);

                    if (_count >= 250) return;

                    string a = input ?? "";
                    string b = inputCompo ?? "";
                    int hash = a.GetHashCode() ^ (b.GetHashCode() * 397);
                    if (!_loggedHashes.Add(hash)) return;
                    _count++;

                    LightLogger.LogDebug($"[RichTextInput.SetText] input({a.Length})=[{Codepoints(a)}] " +
                                         $"compo({b.Length})=[{Codepoints(b)}] " +
                                         $"imeMode={SafeImeMode()} imeSelected={SafeImeSelected()}");   // 只读，不写 ✓
                }
                catch { }
            }

            /// <summary>
            /// **字符串级清理**（唯一可靠的过滤点 ✓）。
            ///
            /// 去掉：零宽/方向控制符、BOM、变体选择符、**代理对的一半**（emoji）、
            ///      以及**除退格/回车/换行之外**的控制字符 ✓
            /// 保留：`\b`（原版删字分支要用 ✓）、`\r` `\n`（聊天发送要用 ✓）、其余一切可显示字符（含中文 ✓）
            ///
            /// ⚠️ 原来那套"在 `IsCharAllowed` 里按 char 判断"的做法已废弃 ✗ ——
            ///    那个 hook 的 char 参数被截断，正是"挑字吞"（我/饿/者 打不进去）的根源 ✗
            /// </summary>
            internal static string Sanitize(string input, TextBoxTMP box)
            {
                try
                {
                    bool ip = box != null && box.IpMode;
                    var sb = new System.Text.StringBuilder(input.Length);

                    for (int i = 0; i < input.Length; i++)
                    {
                        char c = input[i];

                        // ★★ 退格：**我们自己执行删除，并且绝不把 `\b` 传给原版** ✓✓
                        //
                        // ⚠️⚠️ 2026-10-06 用户报「退格豆腐块复发」的原因就是这里：
                        //    我们把 `IsCharAllowed` 改成"一律放行"之后，原版 `SetText` 的循环变成
                        //      `if (c2 == '\b') { tempTxt.Length--; }   // 先删一个 ✓
                        //       if (!IsCharAllowed('\b')) { ... }      // 原版在这里返回 false → 丢弃 ✓
                        //       else { tempTxt.Append(c2); }           // 我们放行 → `\b` **被塞进文本** ✗ = 豆腐块
                        //    → 现在：在这层直接把它"消费"掉（删掉前一个已收集的字符），
                        //      于是字符串里**再也看不到 `\b`**，原版那条分支不会触发 ✓
                        //
                        // ⚠️ 语义为什么等价：原版是 `text.Insert(caretPos, input)` 之后，
                        //    在 `\b` 处删掉**它前面那个字符** —— 而 `\b` 在字符串里的前一个字符，
                        //    正是光标前那个字符 ✓ 所以"删掉已收集的最后一个字符"= 原版行为 ✓
                        //    （顺带修正了原版 `\b` 会多算一次长度导致的 caret 偏移 ✓）
                        if (c == '\b')
                        {
                            if (sb.Length > 0) sb.Length--;     // 删掉前一个已收集字符 ✓
                            continue;                           // ★ 绝不把 `\b` 传下去 ✓
                        }

                        if (c == '\r' || c == '\n') { sb.Append(c); continue; }   // 回车/换行：聊天发送要用 ✓

                        if (ip) { sb.Append(c); continue; }                       // IP 输入框不清理

                        if (char.IsSurrogate(c)) continue;                        // emoji 半截 → 去掉 ✓
                        if (char.IsControl(c)) continue;                          // 其余控制字符 → 去掉 ✓
                        if (c == '\u00AD') continue;                              // 软连字符
                        if (c >= '\u200B' && c <= '\u200F') continue;             // 零宽 / 方向标记
                        if (c >= '\u2028' && c <= '\u202E') continue;             // 行分隔 / 双向控制
                        if (c >= '\u2060' && c <= '\u206F') continue;             // 词连接符 / 双向隔离
                        if (c == '\uFEFF') continue;                              // BOM
                        if (c >= '\uFE00' && c <= '\uFE0F') continue;             // 变体选择符

                        sb.Append(c);
                    }

                    return sb.ToString();
                }
                catch { return input; }
            }

            private static string Codepoints(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var sb = new System.Text.StringBuilder(s.Length * 7);
                for (int i = 0; i < s.Length && i < 40; i++)
                    sb.Append("U+").Append(((int)s[i]).ToString("X4")).Append(' ');
                return sb.ToString().TrimEnd();
            }

            private static string SafeImeMode()
            {
                try { return UnityEngine.Input.imeCompositionMode.ToString(); } catch { return "?"; }
            }

            private static string SafeImeSelected()
            {
                try { return UnityEngine.Input.imeIsSelected.ToString(); } catch { return "?"; }
            }
        }

        /// <summary>
        /// ⚠️⚠️ **已废弃/已删除**：这里原来有一个"打开聊天框时把全局输入法归一到 `Auto`"的修复尝试，
        ///    实测**又把中文输入法害死了**（用户 2026-10-06）✗
        ///
        /// 【真正的根因与正确做法】（读原版 `TextBoxTMP` 才看清）：
        /// <code>
        ///   TextBoxTMP.GiveFocus():  Input.imeCompositionMode = 1;   // On
        ///   TextBoxTMP.LoseFocus():  Input.imeCompositionMode = 2;   // Off
        /// </code>
        /// **原版自己就是所有者** ✓ —— `Off` 是正常状态 ✓
        /// 所以任何"我们帮它还原旧值 / 归一 Auto / 每帧自愈"的写法都会把游戏刚设好的状态覆盖掉 ✗
        /// → 我们**只在自己聚焦时设 `On`**（`GUITextField.ApplyImeOnFocus` ✓），**失焦什么都不写** ✓
        /// </summary>
        // internal static void NormalizeImeOnChatOpen(bool opening) { ... }   ← 已删除

        /// <summary>
        /// ★ 2026-10-06：「挑字吞」取证 —— 把**每一个被问过的字符**及其结论记一次（每个码位只记一次 ✓）。
        ///
        /// 判读（下次出现吞字时看日志）：
        ///   · 日志里**没有**那个字 → 字符根本没到过滤层 → 是**输入法/输入侧**的问题（与我们无关 ✓）
        ///   · 日志里**有**那个字且 `原版=True` → 我们（或原版）把它否决了 → 看 `危险字符` 标记
        ///   · 日志里**有**但 `危险字符=True` → 命中了 `IsUnsafe` → 按码位精准修 ✓
        /// </summary>
        [HarmonyPrefix]
        public static void LogEveryChar_Prefix(TextBoxTMP __instance, char i)
        {
            try
            {
                if (_loggedChars.Count >= 400) return;
                if (!_loggedChars.Add(i)) return;

                // ⚠️ 注意：`i` 是 Harmony/Interop 传进来的参数，**高位被截断**（实测 U+8005 → 0x05）✗
                //    所以这里**不能**据它判断"危不危险"——只打印**参数本身**与截断提示 ✓
                //    真正的危险判定以 `IsCharAllowed` 之后的 `__result`（原版拿完整字符算出来的）为准 ✓
                bool truncated = i >= (char)0x80 && char.IsControl(i);
                LightLogger.LogDebug($"[RichTextInput.字符询问] 参数值=U+{((int)i):X4} " +
                                     $"（截断提示={truncated} 控制={char.IsControl(i)} 代理={char.IsSurrogate(i)}）" +
                                     (truncated ? " ← 高位被截断，不代表真实字符危险 ✗" : ""));
            }
            catch { }
        }

        private static readonly HashSet<char> _loggedChars = new();

        [HarmonyPostfix]
        public static void Postfix(TextBoxTMP __instance, char i, ref bool __result)
        {
            try
            {
                if (__instance != null && __instance.IpMode) return;   // IP 输入框不干预

                // ⚠️⚠️⚠️ 2026-10-06 结论（日志铁证，绕了三轮才看清）：
                //
                //   ① 这个 hook 的 `char i` 参数**高位被截断**（实测：打"者" U+8005 → 我们收到 0x05）✗
                //      → **在这里无法正确判断字符** ✗（`char.IsControl(0x05)` 会把汉字判成控制符）
                //   ② 而且日志证明：即使我们"以原版结论为准"，`者` 仍然进不去输入框
                //      （`[SetText] input(1)=[U+8005]` 有 ✓ 但输入框文本**没变** ✗）
                //      → 说明再纠结这个 hook 已经没有意义 ✗
                //
                //   ✅ **改为：这里一律放行**（不否决任何字符），真正的过滤搬到
                //      `TextBoxTMP.SetText` 的 Prefix —— 那里拿到的是**完整正确的字符串** ✓
                //      （日志已证明：`input(1)=[U+8005]` ✓ 干净的字都在 ✓）
                //      在那里做"控制字符/零宽/代理对"的清理，**同时保留 `\b` `\r` `\n`**
                //      （退格与回车是原版逻辑自己处理的，必须原样传下去 ✓）
                __result = true;
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
