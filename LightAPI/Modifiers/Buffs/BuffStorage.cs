using System;
using System.Collections.Generic;
using System.Linq;
using LightInDark.Core;
using LightInDark.Game;
using LightInDark.Modifiers.Buffs.Examples;

namespace LightInDark.Modifiers.Buffs
{
    /// <summary>
    /// **Buff 的总仓库**：所有 Buff 定义都放这里，一处就能看全 ✓
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// 【三种注册方式，从"最随意"到"最完整"】（2026-10-25 用户要求："加一个很简单的注册方式，耦合低一点"）
    ///
    /// ① **一行注册，连类都不用写**（最省事 ✓ 推荐给"只改一下外观/状态"这种）：
    ///    <code>
    ///    // 参数：key、显示名、颜色、时长、生效回调、结束回调（都可空 ✓）
    ///    public static readonly BuffDefinition Invisible = BuffStorage.Add(
    ///        "invisible", "隐身", LightInDark.Color.Gray, 8f,
    ///        onApply:  p =&gt; PlayerAppearance.SetAnonymousAll(true),
    ///        onExpire: p =&gt; PlayerAppearance.SetAnonymousAll(false));
    ///    </code>
    ///
    /// ② **一行注册一个已有模板的变体**（属性型 Buff 不用写类 ✓）：
    ///    <code>
    ///    public static readonly SpeedBuff Haste = BuffStorage.Add(new SpeedBuff(1.4f, 8f, "haste", "加速"));
    ///    public static readonly ShadowBuff ShortShadow = BuffStorage.Add(new ShadowBuff(8f));   // 小黑人 8 秒 ✓
    ///    </code>
    ///
    /// ③ **写一个完整定义类**（要 `OnTick` / 复杂状态就用这个 ✓）：
    ///    仿 `BuffTemplates.cs` 顶部注释；再 `BuffStorage.Add(MyBuff.Instance)` ✓
    ///
    /// ⚠️ **不用再手动改 `All`** ✓ —— `Add` 会自动加进 `All` 并登记进 `BuffRegistry`。
    ///    只有"手写类 + 完全不用 Add"才需要关心登记（那种情况 RPC 反查不到 ✗，别那么干）。
    ///
    /// 【用法】
    /// <code>
    /// player.GiveBuff(BuffStorage.SpeedUp, 8f, "haste");        // 类型 / 时长 / key ✓（时长可覆盖默认 ✓）
    /// player.GiveBuff(BuffStorage.Freeze, 3f);
    /// player.RemoveBuff("haste");                                // 按 key（推荐）✓
    ///
    /// BuffStorage.All;                    // 全部（**实时**：Add 之后再取也能看到 ✓）
    /// BuffStorage.Get("freeze");          // 按 key 反查 ✓
    /// BuffStorage.Get(typeof(SpeedBuff)); // 按类型反查 ✓
    /// </code>
    /// ═══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class BuffStorage
    {
        // ⚠️ 这两个必须**声明在最前面**：静态字段按**书写顺序**初始化，
        //    下面的模板字段会在初始化时调用 `Add` → 依赖 `_all` 已经存在 ✓
        private static readonly List<BuffDefinition> _all = new();

        /// <summary>全部已注册的 Buff（**实时列表**：之后再 `Add` 也能看到 ✓）</summary>
        public static IReadOnlyList<BuffDefinition> All => _all;

        // ─────────────────────────────────────────────────────────────────
        //  ① 速度类（SpeedBuff：影响 `MyPhysics.Speed`，多人可见 ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>加速：速度 ×1.5，10 秒 ✓</summary>
        public static readonly SpeedBuff SpeedUp = Add(new SpeedBuff(1.5f, 10f, "speed_up", "加速"));

        /// <summary>减速：速度 ×0.5，10 秒 ✓</summary>
        public static readonly SpeedBuff SlowDown = Add(new SpeedBuff(0.5f, 10f, "slow_down", "减速"));

        /// <summary>定身：速度 ×0，3 秒（= 完全不能动 ✓）</summary>
        public static readonly SpeedBuff Freeze = Add(new SpeedBuff(0f, 3f, "freeze", "定身"));

        /// <summary>示例版加速（`Examples\SpeedBoostBuff`：×1.3 / 10 秒 / 会议即结束 ✓）</summary>
        public static readonly SpeedBuff SpeedBoost = Add(SpeedBoostBuff.MyBuff);

        // ─────────────────────────────────────────────────────────────────
        //  ② 视野类（ViewDistanceBuff：**只影响本人**的灯光半径 ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>视野扩大：×1.5，10 秒 ✓</summary>
        public static readonly ViewDistanceBuff VisionUp = Add(new ViewDistanceBuff(1.5f, 10f, "vision_up", "视野扩大"));

        /// <summary>致盲：视野 ×0.35，8 秒 ✓</summary>
        public static readonly ViewDistanceBuff Blind = Add(new ViewDistanceBuff(0.35f, 8f, "blind", "致盲"));

        // ─────────────────────────────────────────────────────────────────
        //  ③ 体型类（ScaleBuff：别人也看得到 ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>缩小：体型 ×0.6，10 秒 ✓</summary>
        public static readonly ScaleBuff Shrink = Add(new ScaleBuff(0.6f, 10f, "shrink", "缩小"));

        /// <summary>巨大化：体型 ×1.6，10 秒 ✓</summary>
        public static readonly ScaleBuff Giant = Add(new ScaleBuff(1.6f, 10f, "giant", "巨大化"));

        // ─────────────────────────────────────────────────────────────────
        //  ④ 外观类（自己写了 OnApply/OnExpire 的定义 ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>小黑人：15 秒 ✓（**时长不再锁死** —— 用 `new ShadowBuff(8f)` 就能改 ✓）</summary>
        public static readonly ShadowBuff Shadow = Add(ShadowBuff.MyBuff);

        /// <summary>小黑人·短版：8 秒 ✓（= 用户要的"别锁死 15s"的现成例子 ✓）</summary>
        public static readonly ShadowBuff ShadowShort = Add(new ShadowBuff(8f, "shadow_short", "小黑人(8s)"));

        // ─────────────────────────────────────────────────────────────────
        //  ⑤ 纯标记类（没有属性效果，只表示状态 ✓ 供职业/系统判定）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>通用"被标记"：30 秒，跨会议保留 ✓（`player.HasBuff("marked")` 判状态 ✓）</summary>
        public static readonly MarkBuff Marked =
            Add(new MarkBuff("marked", "被标记", LightInDark.Color.ModGolden, 30f, canPassMeeting: true));

        /// <summary>通用"被通缉"：整局有效 ✓（时长 -1 = 永久 ✓）</summary>
        public static readonly MarkBuff Wanted =
            Add(new MarkBuff("wanted", "被通缉", LightInDark.Color.Red, -1f, canPassMeeting: true));

        // ─────────────────────────────────────────────────────────────────
        //  注册 / 查询（**新 Buff 用 Add 就行，不用碰上面的 All** ✓）
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// **注册一个 Buff 定义**（最常用的入口 ✓）：自动进 <see cref="All"/> + <see cref="BuffRegistry"/> ✓
        /// <para>返回传进来的那个实例 → 可以直接赋给静态字段 ✓</para>
        /// </summary>
        /// <param name="definition">要注册的定义（`new SpeedBuff(...)` / 自定义类实例 ✓）</param>
        /// <returns>同一个实例（方便 `public static readonly … = BuffStorage.Add(...)` ✓）</returns>
        public static T Add<T>(T definition) where T : BuffDefinition
        {
            if (definition == null) return null;

            try
            {
                if (!_all.Contains(definition)) _all.Add(definition);
                BuffRegistry.Register(definition);      // RPC 反查的前提 ✓
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[BuffStorage.Add] {definition.Key} 失败：{ex.Message}");
            }

            return definition;
        }

        /// <summary>
        /// **一行造一个 Buff**（连类都不用写 ✓）：用回调决定生效/结束时做什么。
        /// <para>适合"改外观 / 切状态 / 播个提示"这类简单效果 ✓；要每帧逻辑请写类或用 <paramref name="onTick"/></para>
        /// </summary>
        /// <param name="key">唯一键（RPC 同步用，全局唯一 ✓）</param>
        /// <param name="name">显示名 ✓</param>
        /// <param name="color">主题色（UI/日志 ✓）</param>
        /// <param name="duration">默认时长（秒）；`-1` = 整局 ✓</param>
        /// <param name="onApply">**生效时**的回调（参数 = 挂上这个 Buff 的玩家 ✓）</param>
        /// <param name="onExpire">**结束/被移除时**的回调 —— 必须把 <paramref name="onApply"/> 改的东西改回去 ✓</param>
        /// <param name="canPassMeeting">会议结束后是否保留（默认 false = 会议开始就结束 ✓）</param>
        /// <param name="description">说明文本（不传则用默认说明 ✓）</param>
        /// <returns>建好的定义（已自动注册 ✓）</returns>
        public static SimpleBuff Add(string key, string name, LightInDark.Color color, float duration,
                                     Action<Player> onApply = null, Action<Player> onExpire = null,
                                     bool canPassMeeting = false, string description = "")
            => Add(new SimpleBuff(key, name, color, duration, onApply, onExpire,
                                  canPassMeeting: canPassMeeting, description: description));

        /// <summary>注册一个**自定义** Buff（= `Add` 的别名，语义更明确 ✓）</summary>
        /// <param name="definition">Buff 定义 ✓</param>
        public static void Register(BuffDefinition definition) => Add(definition);

        /// <summary>按 key 反查定义（找不到 null ✓）</summary>
        /// <param name="key">`BuffDefinition.Key`（如 `"freeze"` ✓）</param>
        public static BuffDefinition Get(string key) => BuffRegistry.Get(key);

        /// <summary>按类型反查已登记的实例（找不到 null ✓）</summary>
        /// <param name="type">Buff 定义的类型（如 `typeof(SpeedBuff)` ✓）</param>
        public static BuffDefinition Get(Type type)
            => type == null ? null : _all.FirstOrDefault(b => b != null && b.GetType() == type);

        /// <summary>某前缀的全部模板 ✓</summary>
        /// <param name="keyPrefix">key 前缀（如 `"speed"` 会匹配 `speed_up` / `speed_x1.3` ✓）</param>
        public static IReadOnlyList<BuffDefinition> GetByPrefix(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) return Array.Empty<BuffDefinition>();
            return _all.Where(b => b != null && b.Key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        //  登记（兼容入口：RPC 初始化时会调一次 ✓）
        // ─────────────────────────────────────────────────────────────────

        private static bool _logged;

        /// <summary>
        /// 打印一次"当前登记了哪些 Buff"（幂等 ✓）。
        /// <para>由 `BuffSync.EnsureRegistered()`（RPC 初始化时）调用 ✓ —— 每个客户端都会跑 ✓</para>
        /// <para>⚠️ 现在**不需要**它来做登记（`Add` 已经登记过了 ✓），它只负责日志 ✓</para>
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_logged) return;
            _logged = true;

            try
            {
                // 兜底：万一有人手写了定义却忘了 Add，这里补登记（不污染 All 的顺序 ✓）
                foreach (var definition in _all.ToArray())
                {
                    if (BuffRegistry.Get(definition.Key) == null) BuffRegistry.Register(definition);
                }

                LightLogger.Log($"[BuffStorage] 已登记 Buff {_all.Count} 个 → " +
                                string.Join(", ", _all.Where(b => b != null).Select(b => b.Key)));
            }
            catch (Exception ex)
            {
                LightLogger.LogError("BuffStorage.EnsureRegistered", ex);
            }
        }
    }
}
