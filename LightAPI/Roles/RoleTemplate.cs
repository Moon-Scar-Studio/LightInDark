using System;
using System.Collections.Generic;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Documents;

namespace LightInDark.Roles
{
    /// <summary>
    /// 职业模板（定义侧）：一个职业一个类，全局只保留一个定义实例（约定 MyRole 单例）。
    /// 数据用虚属性声明，键支持 {CodeName} 占位符（如 "role.{CodeName}.intro"）。
    /// 运行时行为写在 <see cref="RuntimeRoleTemplate"/> 子类（由 <see cref="CreateRuntime"/> 返回）。
    /// 必须重写 <see cref="CodeName"/> 与 <see cref="CreateRuntime"/>。
    /// </summary>
    public abstract class RoleTemplate
    {
        // ---- 基础定义 ----

        /// <summary>唯一内部名（语言/配置键前缀）。必须重写。</summary>
        public abstract string CodeName { get; }

        /// <summary>职业颜色（名字/开场白/配置头着色）。</summary>
        public virtual LightInDark.Color Color => LightInDark.Color.White;

        /// <summary>阵营。</summary>
        public virtual RoleCategory RoleCategory => RoleCategory.Crewmate;

        /// <summary>
        /// 是否为**鬼魂职业**（对齐 Nebula 的 `GhostRole`，2026-10-06）。
        /// 鬼魂职业**不参与开局分配**，只在玩家死亡时由 `GhostRoleAllocator` 分配（见那里的说明）。
        /// </summary>
        public bool IsGhostRole => RoleCategory == RoleCategory.Ghost;

        /// <summary>
        /// 本职业**允许携带哪些修饰器**（按修饰器 Key 匹配；对齐 Nebula 的 `CanHaveExtraAssignable`）。
        ///
        /// ⚠️ 返回 **null 或空集合 = 不限制**（默认值）—— 所以现有职业**一行都不用改**，
        ///    行为与改动前完全一致（纯增量）。
        /// 声明了白名单的职业，只有列在里面的修饰器才能加给它（拒绝时会打日志说明原因）。
        /// </summary>
        public virtual IReadOnlyCollection<string>? AllowedModifiers => null;

        /// <summary>该职业能否携带某个修饰器（<see cref="AllowedModifiers"/> 为空 = 不限制）。</summary>
        public bool CanHaveModifier(string modifierKey)
        {
            try
            {
                var allow = AllowedModifiers;
                if (allow == null || allow.Count == 0) return true;         // 默认不限制
                if (string.IsNullOrEmpty(modifierKey)) return false;

                foreach (var k in allow)
                    if (string.Equals(k, modifierKey, StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch
            {
                return true;   // 白名单本身出错时不要把修饰器系统整体锁死
            }
        }

        // ---- 职业块(配置界面里的职业按钮)外观 ----
        //  用户 2026-10-06 要求：职业块要有自己的底色，且"高光默认取对应阵营色，
        //  中立则取该职业自己的颜色"。

        /// <summary>
        /// **职业块的底色（可选）**。返回 null = 自动（按阵营取色，见
        /// <see cref="ResolveBlockColor"/>）。想给某个职业一个专属底色就重写它。
        /// </summary>
        public virtual LightInDark.Color? BlockColor => null;

        /// <summary>
        /// **职业块的悬浮/选中高光色**。返回 null = 自动（见 <see cref="ResolveBlockHighlight"/>）。
        /// </summary>
        public virtual LightInDark.Color? BlockHighlightColor => null;

        /// <summary>
        /// 职业块底色（已解析，一定非 null）。
        /// 规则：显式 <see cref="BlockColor"/> 优先；否则按阵营给一个低饱和底色。
        /// </summary>
        public LightInDark.Color ResolveBlockColor()
        {
            if (BlockColor.HasValue) return BlockColor.Value;

            // 底色刻意压暗/降饱和 —— 它是"块的面",不是"块的高光"，
            // 直接拿阵营色会太跳，一屏按钮会糊成一片。
            // ⚠️ RoleCategory 只有 3 个值（Crewmate/Impostor/Neutral）——
            //    Modifier / Ghost 是 ConfigCategory 才有的，这里不该出现。
            switch (RoleCategory)
            {
                case RoleCategory.Impostor: return new LightInDark.Color(0.30f, 0.10f, 0.12f, 0.92f);
                case RoleCategory.Neutral: return new LightInDark.Color(0.16f, 0.17f, 0.19f, 0.92f);
                default: return new LightInDark.Color(0.12f, 0.18f, 0.22f, 0.92f);   // 船员
            }
        }

        /// <summary>
        /// 职业块高光色（已解析，一定非 null）。
        /// 规则：显式 <see cref="BlockHighlightColor"/> 优先；
        /// 否则**中立阵营取该职业自己的 <see cref="Color"/>**，其余取阵营色。
        /// </summary>
        public LightInDark.Color ResolveBlockHighlight()
        {
            if (BlockHighlightColor.HasValue) return BlockHighlightColor.Value;

            // ⚠️ 中立必须用职业自己的颜色（用户明确要求）：
            //    中立里各职业差异极大(小丑/纵火犯/鹈鹕…)，统一给个灰色分不出来。
            if (RoleCategory == RoleCategory.Neutral) return Color;

            return RoleCategory switch
            {
                RoleCategory.Impostor => new LightInDark.Color(1.000f, 0.098f, 0.098f, 1f),   // #FF1919
                _ => new LightInDark.Color(0.549f, 1.000f, 1.000f, 1f),                      // #8CFFFF 船员
            };
        }

        /// <summary>注册序号（RPC 用）。</summary>
        public int Id { get; internal set; }

        // ---- 文案（翻译键，支持 {CodeName} 占位符）----

        /// <summary>职业名语言键。</summary>
        public virtual string NameKey => "role.{CodeName}.name";

        /// <summary>一句话简介语言键。</summary>
        public virtual string ShortDescribe => "role.{CodeName}.shortdes";

        /// <summary>职业描述语言键（按 <see cref="DocumentType"/> 渲染）。</summary>
        public virtual string Describe => "role.{CodeName}.des";

        /// <summary>开场白语言键。</summary>
        public virtual string Intro => "role.{CodeName}.intro";

        /// <summary>描述文档的渲染格式。</summary>
        public virtual RoleDocumentType DocumentType => RoleDocumentType.Normal;

        /// <summary>开场音效：相对路径 mp3（YouAreText 出现时播放），null 不播放。</summary>
        public virtual string IntroSFX => null;

        /// <summary>职业来源标注（如移植自某 MOD），null 不标。</summary>
        public virtual string From => null;

        // ---- 分配与能力 ----

        /// <summary>分配参数（默认不参与随机分配）。</summary>
        public virtual AllocationParameters Allocation => default;

        /// <summary>默认参数（分配时随 RPC 下发）。</summary>
        public virtual int[] DefaultArguments => Array.Empty<int>();

        /// <summary>能否被分配机随机分配（false = 仅作兜底/手动指定）。</summary>
        public virtual bool CanBeAssigned => true;

        /// <summary>任务数量（由原版游戏设置传入的占位值）。</summary>
        public virtual int TaskCount => 1;

        /// <summary>能否报告尸体（硬编码能力，不可被设置更改）。</summary>
        public virtual bool CanReport => true;

        /// <summary>能否召开紧急会议（硬编码能力，不可被设置更改）。</summary>
        public virtual bool CanCallEmergencyMeeting => true;

        /// <summary>职业专属配置项（键自动加 role.&lt;CodeName&gt;. 前缀；通用数量/概率由注册器附加）。</summary>
        public virtual RoleConfigItem[] RoleConfiguration => Array.Empty<RoleConfigItem>();

        /// <summary>职业图标（显示在职业按钮左侧），null 只显示文字。</summary>
        public virtual UnityEngine.Sprite IconImage => null;

        // =====================================================================
        //  职业立绘（职业详情右侧那张半透明大图）
        //
        //  参考 Nebula（用户 2026-10-06 给的示例）：
        //      ConfigurationHolder!.Illustration =
        //          NebulaAPI.AddonAsset.GetResource("BigPic/MaskedDancer.png")?.AsImage(115f);
        //  它那边叫 Illustration；**我们这边叫 RoleImage**（用户指定）。
        //  用法见 Nebula Help.cs:492
        //      outsideScreen.SetBackImage(assignable.ConfigurationHolder?.Illustration, 0.2f);
        //                                                    ↑ 0.2 = **半透明**（"半透明立绘"）
        //
        //  我们等价的做法：给一个**资源路径**，由本属性按需加载成 Sprite。
        //  路径相对 `Light.Resources`（即 `Light\Resources\` 下），例如 "BigPic/MaskedDancer.png"。
        //  ⚠️ 115f 是和 Nebula 一致的 pixelsPerUnit。
        // =====================================================================

        /// <summary>
        /// 职业立绘的资源路径（相对 <c>Light.Resources</c>，如 <c>"BigPic/MaskedDancer.png"</c>）。
        /// 返回 null / 空 = 该职业没有立绘。
        /// </summary>
        public virtual string? RoleImagePath => null;

        /// <summary>
        /// **立绘加载钩子** —— 由主插件在启动时接上。
        ///
        /// ⚠️ 为什么要有这个钩子：本类在 **API 程序集**（LightInDark）里，而实际读图用的
        ///    <c>Light.Utilities.ResourceHelper</c> 在**主插件程序集**（Light）里 ——
        ///    API 不能反向引用主插件（会循环依赖）。所以走委托注入，
        ///    和 <c>LightLogger.BepInExInfo</c> 是同一套做法。
        ///    参数 = 相对 Light.Resources 的路径；返回 null 表示没这张图。
        /// </summary>
        public static Func<string, UnityEngine.Sprite?>? RoleImageLoader;

        private UnityEngine.Sprite? _roleImage;
        private bool _roleImageTried;

        /// <summary>
        /// 职业立绘（懒加载并缓存）。加载失败返回 null，调用方自己判空。
        /// </summary>
        public UnityEngine.Sprite? RoleImage
        {
            get
            {
                // ⚠️ 2026-10-06 审查 #11：原来只看 `_roleImageTried` —— 一旦试过就永远返回缓存。
                //   而 `_roleImage` 是 Unity `Sprite`，主菜单卸载/换场景后会变成**假 null**
                //   （`== null` 为 true 但 C# 引用还在，AGENTS §4.6.1）→ 立绘**再也不显示且永不重试** ✗
                //   现在先用 `!=` 判活（走 UnityEngine.Object 的 == 重载），活着才用缓存；
                //   拿到假 null 时**清掉标志重试一次**。
                if (_roleImage != null) return _roleImage;
                if (_roleImageTried && !_roleImageWarnedNull)
                {
                    _roleImageTried = false;      // 缓存已失效 → 允许重载一次
                }
                if (_roleImageTried) return null;

                _roleImageTried = true;      // 只尝试一次，缺图不会每帧重试

                try
                {
                    var path = RoleImagePath;
                    if (!string.IsNullOrEmpty(path))
                        _roleImage = RoleImageLoader?.Invoke(path);
                }
                catch (Exception ex)
                {
                    LightInDark.Core.LightLogger.LogDebug($"[RoleTemplate] 立绘加载失败 {CodeName} / {RoleImagePath}: {ex.Message}");
                }

                if (_roleImage == null)
                {
                    _roleImageWarnedNull = true;
                    LightInDark.Core.LightLogger.LogDebug($"[RoleTemplate] {CodeName} 没有可用立绘（路径={RoleImagePath}），本次不再重试");
                }
                return _roleImage;
            }
        }

        private bool _roleImageWarnedNull;

        /// <summary>立绘的 pixelsPerUnit —— 和 Nebula 的 <c>AsImage(115f)</c> 保持一致。</summary>
        public const float RoleImagePPU = 115f;

        // ---- 文案解析 ----

        /// <summary>解析键中的 {CodeName} 占位符。</summary>
        public string ResolveKey(string key) => key?.Replace("{CodeName}", CodeName) ?? "";

        /// <summary>显示名（按语言键解析，缺省回退 CodeName）。</summary>
        public string Name => LightInDark.Language.Language.GetStringOrKey(ResolveKey(NameKey), CodeName);

        /// <summary>一句话简介（按语言键解析）。</summary>
        public string ShortDescribeText => LightInDark.Language.Language.GetStringOrKey(ResolveKey(ShortDescribe), "");

        /// <summary>职业描述（按语言键解析，仅 Normal 格式使用）。</summary>
        public string DescribeText => LightInDark.Language.Language.GetStringOrKey(ResolveKey(Describe), "");

        /// <summary>
        /// 职业描述全文（按 DocumentType 分发）：Normal 走语言键；
        /// Html/MarkDown 时 Describe 存嵌入资源路径（如 "./Resources/Docs/x.html"），经 RoleDocument 渲染。
        /// </summary>
        public string GetDocumentText() => DocumentType switch
        {
            RoleDocumentType.Html => Documents.RoleDocument.Load(ResolveKey(Describe), GetType().Assembly, Documents.RoleDocument.RenderHtml),
            RoleDocumentType.MarkDown => Documents.RoleDocument.Load(ResolveKey(Describe), GetType().Assembly, Documents.RoleDocument.RenderMarkdown),
            _ => DescribeText,
        };

        /// <summary>开场白（按语言键解析）。</summary>
        public string IntroText => LightInDark.Language.Language.GetStringOrKey(ResolveKey(Intro), "");

        // ---- 运行时创建 ----

        /// <summary>
        /// **这个职业现在到底出不出** —— 单一判定入口（2026-10-06 审查 #13）。
        ///
        /// 原来"能不能出"被拆成 5 处各自实现（`CanBeAssigned` / `Allocation.MaxCount` /
        /// 配置 `role.X.count` / `GuaranteedCount` / `Chance`），而且"配置优先、回退默认"这段
        /// 在 `StandardRoleAllocator` 与 `RolePinManager` 里**各写了一遍**（跨程序集，没法共用）→
        /// 任一处改口径就会不一致（例如配置里显示"已关闭"，预定却照样成功）✗
        ///
        /// Nebula 是单一入口 `IsSpawnable()`，并且 `catch { return false; }` ——
        /// **"配置没建好 = 不出"是安全方向** ✓ 这里取同样的语义。
        /// </summary>
        public bool IsSpawnable()
        {
            try
            {
                if (!CanBeAssigned) return false;

                var item = Configuration.ConfigRegistry.Get($"role.{CodeName}.count");
                int max = item != null ? item.GetInt() : Allocation.MaxCount;
                if (max < 0) max = 0;
                else if (max > 15) max = 15;          // 与分配器同一套夹紧口径
                return max > 0;
            }
            catch { return false; }                   // 读配置失败 → 当作不出（安全方向）
        }

        /// <summary>运行时创建</summary>
        public abstract RuntimeRoleTemplate CreateRuntime(global::PlayerControl owner);

        /// <summary>
        /// 框架内部创建入口：**只创建**运行时实例，**不激活**。
        ///
        /// ⚠️⚠️ 2026-10-06（审查 #12）：原来这里顺手 `runtime.Activate()`，
        ///   而调用方写的是 `Role = newRole.CreateRuntimeInternal(this);` —— **右侧先求值**，
        ///   于是 `OnActivated()` 执行时 `MyPlayer.Role` 还指着**刚被 Inactivate 的旧职业**（或 null）✗
        ///   职业在 `OnActivated` 里做 `MyPlayer.HasRole&lt;X&gt;()`、读旧职业状态、"从旧职业继承" 全是过期数据。
        ///   Nebula 的顺序是**先赋值再初始化**：`data.role = newRole` → `newRole.Initialize(player)` ✓
        ///   现在改成两步：调用方先 `Role = ...` 再 `runtime.Activate()`。
        /// </summary>
        internal RuntimeRoleTemplate CreateRuntimeFor(Game.Player player) => CreateRuntime(player.Control);
    }
}
