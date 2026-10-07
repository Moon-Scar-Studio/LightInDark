using System;
using LightInDark.Core;
using TMPro;
using UnityEngine;
using UColor = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace Light.UI.Window;

/// <summary>
/// 通用「详情提示框」（鼠标悬浮时弹出一段说明）。
///
/// 自己写 UI 时的用法：
///     DetailPopup.Show("这是说明文本", canPinByTab: true, anchor: someRowTransform);
///     DetailPopup.Hide();
///
/// 规则：
///  * text 为 null / 空 / 全空白 → **什么都不显示**（也不会建出提示框）。
///  * canPinByTab = true 时：正文下面空一行再追加 “可按tab固定介绍”，并按 Tab 可钉住 / 取消钉住。
///  * canPinByTab = false 时：不追加那行提示，也不响应 Tab。
///
/// 外观与尺寸（代码绘制，不用原版弹窗贴图）：
///  * 半透明灰底 + 淡金细边框（四条细条拼的，粗细/颜色见下面常量）。
///  * **文字直接克隆自 anchor（被悬浮那一行）身上的 TextMeshPro** —— 字号、字体、缩放与那一行完全一致；
///    anchor 上没有文字时才退回统一文本模板（这时用 <see cref="FallbackFontSize"/>）。
///  * 框的大小按 TMP 的 <c>textBounds</c>（**真正画出来的字形范围**）算，不用 preferredWidth —— 后者
///    受预制体的 rect / margin 影响，会比实际字形大好几倍（就是"框很大、字很小"的原因）。
/// </summary>
public static class DetailPopup
{
    /// <summary>
    /// 可按 Tab 固定时，原来会在正文下面补一行"可按tab固定介绍" ✗
    /// ⚠️ 用户 2026-10-06 要求：**功能保留、文字删掉** ✓ → 这里改成空串（`canPinByTab` 的固定逻辑照旧 ✓）
    /// </summary>
    private const string PinHint = "";

    /// <summary>锚点那一行没有文字时才用这个字号（× <see cref="DetailPopupStyle.FontScale"/>）</summary>
    private const float FallbackFontSize = 1.6f;

    // ── 尺寸 / 配色（用户 2026-10-06：颜色、大小、布局都要能自定义）───────────────
    //    原来这些是 `const` ✗ 改不了；现在收进 `DetailPopupStyle` ✓
    //    · 代码里改：`DetailPopup.SetStyle(new DetailPopupStyle { ... })` ✓
    //    · 游戏内改：配置块「详情框」（字号倍率 / 留白 / 最小尺寸 / 边框粗细 / 配色 / 对齐）✓

    /// <summary>详情框外观与布局。</summary>
    public struct DetailPopupStyle
    {
        /// <summary>内容填充色（用户要求：**淡灰** ✓）</summary>
        public UColor Fill;
        /// <summary>边框色（用户要求：**淡黑** ✓）</summary>
        public UColor Border;
        /// <summary>文字色（淡灰底上必须用深色字，否则看不清 ✗）</summary>
        public UColor Text;

        public float BorderThickness;   // 边框粗细
        public float PadX;              // 文字左右留白
        public float PadY;              // 文字上下留白
        public float MinWidth;          // 最小宽（0 = 不限制）
        public float MinHeight;         // 最小高（0 = 不限制）
        /// <summary>换行宽度（&gt;0 = 超过就换行、框高跟着变 ✓；0 = 单行不换行）</summary>
        public float MaxWidth;
        public float FontScale;         // 字号 = 锚点那一行的字号 × 本值
        public float ZOffset;           // 相对锚点的 z 偏移（负数 = 更靠前）
        public float BoldOutlineWidth;  // 加粗强度（越小越干净 ✓）

        /// <summary>对齐方式（影响多行时的排版 ✓）</summary>
        public TextAlignmentOptions Align;

        /// <summary>
        /// 默认样式：**只用中间填充 + 白字、不要边框**（用户 2026-10-06 看图后指定 ✓）。
        /// 填充色同时是那张材质图的**着色**（Unity 里 sprite color 是相乘 ✓）——
        /// 选这个深灰是为了"材质读不到时"也还能看清白字 ✓
        /// </summary>
        public static DetailPopupStyle Default => new DetailPopupStyle
        {
            Fill = new UColor(0.14f, 0.14f, 0.14f, 1f),         // 深灰（同时充当材质着色 ✓）
            Border = new UColor(0f, 0f, 0f, 0f),                // 边框：**不用** ✓
            Text = new UColor(1f, 1f, 1f, 1f),                  // ★ 白字 ✓

            BorderThickness = 0f,                               // ★ 0 = 不画那 4 条细条 ✓
            PadX = 0.20f,
            PadY = 0.12f,
            MinWidth = 0.30f,
            MinHeight = 0.20f,
            MaxWidth = 0f,
            FontScale = 1.0f,
            ZOffset = -1.0f,
            BoldOutlineWidth = 0.06f,
            Align = TextAlignmentOptions.Center,
        };

        /// <summary>Nebula 观感（深灰黑底、不透明 ✓）—— 抄自 `NebulaManager.MouseOverPopup` 的
        /// `background.color = new Color(0.14f, 0.14f, 0.14f, 1f)` ✓</summary>
        public static DetailPopupStyle NebulaDark => new DetailPopupStyle
        {
            Fill = new UColor(0.14f, 0.14f, 0.14f, 1f),          // Nebula 的底色 ✓
            Border = new UColor(0.14f, 0.14f, 0.14f, 1f),        // 同色 = 看起来无边（Nebula 就是一块底图 ✓）
            Text = new UColor(1f, 1f, 1f, 1f),
            BorderThickness = 0f,
            PadX = 0.20f,
            PadY = 0.12f,
            MinWidth = 0.30f,
            MinHeight = 0.20f,
            MaxWidth = 0f,
            FontScale = 1.0f,
            ZOffset = -1.0f,
            BoldOutlineWidth = 0.06f,
            Align = TextAlignmentOptions.Left,
        };

        /// <summary>旧样式（深灰底 + 淡金边框 + 辉光白字）</summary>
        public static DetailPopupStyle Classic => new DetailPopupStyle
        {
            Fill = new UColor(0.22f, 0.22f, 0.22f, 0.85f),
            Border = new UColor(1f, 0.90f, 0.63f, 0.95f),
            Text = new UColor(1f, 0.95f, 0.85f, 1f),
            BorderThickness = 0.020f,
            PadX = 0.20f,
            PadY = 0.12f,
            MinWidth = 0.30f,
            MinHeight = 0.20f,
            MaxWidth = 0f,
            FontScale = 1.0f,
            ZOffset = -1.0f,
            BoldOutlineWidth = 0.06f,
            Align = TextAlignmentOptions.Center,
        };
    }

    /// <summary>当前样式（代码可通过 <see cref="SetStyle"/> 覆盖；游戏内由配置块调整 ✓）</summary>
    public static DetailPopupStyle Style { get; private set; } = DetailPopupStyle.Default;

    /// <summary>代码里设置样式（会立刻应用到已建的框 ✓）</summary>
    public static void SetStyle(DetailPopupStyle style)
    {
        Style = style;
        ApplyStyleToExisting();
    }

    /// <summary>回到默认样式（淡灰 + 淡黑 ✓）</summary>
    public static void ResetStyle() => SetStyle(DetailPopupStyle.Default);

    /// <summary>
    /// **从配置读一遍样式**（配置块「详情框」✓）—— 每次 <see cref="Show"/> 都会调，
    /// 所以游戏里改完立刻生效 ✓（配置是字典查找，开销可忽略 ✓）
    /// </summary>
    public static void ApplyConfig()
    {
        try
        {
            // ⚠️ `ConfigRegistry` 是**静态类**，不能赋给变量 ✗（编译期就报 CS0119/CS0723）
            static float F(string key, float fallback)
            {
                try { return LightInDark.Configuration.ConfigRegistry.Get(key)?.GetFloat() ?? fallback; }
                catch { return fallback; }
            }
            static int I(string key, int fallback)
            {
                try { return LightInDark.Configuration.ConfigRegistry.Get(key)?.GetInt() ?? fallback; }
                catch { return fallback; }
            }

            int palette = I("lid.detail.palette", 0);
            var s = palette switch
            {
                1 => DetailPopupStyle.Classic,      // 深灰 + 淡金（旧观感）
                2 => CardStyle(),                   // 白卡 + 深边
                3 => DetailPopupStyle.NebulaDark,   // ★ Nebula 观感（深灰黑纯色底 ✓）
                _ => DetailPopupStyle.Default,      // 淡灰 + 淡黑（默认 ✓）
            };

            s.FontScale = F("lid.detail.fontScale", s.FontScale);
            s.PadX = F("lid.detail.padX", s.PadX);
            s.PadY = F("lid.detail.padY", s.PadY);
            s.MinWidth = F("lid.detail.minWidth", s.MinWidth);
            s.MinHeight = F("lid.detail.minHeight", s.MinHeight);
            s.MaxWidth = F("lid.detail.maxWidth", s.MaxWidth);
            s.BorderThickness = F("lid.detail.borderThickness", s.BorderThickness);

            s.Align = I("lid.detail.align", 0) switch
            {
                1 => TextAlignmentOptions.Left,
                2 => TextAlignmentOptions.Right,
                _ => TextAlignmentOptions.Center,
            };

            // 只在真的变了才写回（避免每帧重建/刷日志 ✓）
            if (!StyleEquals(Style, s))
            {
                Style = s;
                ApplyStyleToExisting();
                LightLogger.Log($"[DetailPopup] 样式已更新（配色={palette} 字号×{s.FontScale:0.##} " +
                                $"留白={s.PadX:0.##}/{s.PadY:0.##} 边框={s.BorderThickness:0.###} 对齐={s.Align}）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyConfig] {ex.Message}");
        }
    }

    /// <summary>白卡配色（配置档位 2）✓</summary>
    private static DetailPopupStyle CardStyle()
    {
        var s = DetailPopupStyle.Default;
        s.Fill = new UColor(0.97f, 0.97f, 0.97f, 0.96f);
        s.Border = new UColor(0.15f, 0.15f, 0.15f, 0.95f);
        s.Text = new UColor(0.05f, 0.05f, 0.05f, 1f);
        return s;
    }

    private static bool StyleEquals(DetailPopupStyle a, DetailPopupStyle b)
        => a.Fill == b.Fill && a.Border == b.Border && a.Text == b.Text
           && a.BorderThickness == b.BorderThickness && a.PadX == b.PadX && a.PadY == b.PadY
           && a.MinWidth == b.MinWidth && a.MinHeight == b.MinHeight && a.MaxWidth == b.MaxWidth
           && a.FontScale == b.FontScale && a.ZOffset == b.ZOffset
           && a.BoldOutlineWidth == b.BoldOutlineWidth && a.Align == b.Align;

    /// <summary>上次应用的换行宽度（变了才重排文字 ✓）</summary>
    private static float _lastWrapWidth = -1f;

    /// <summary>上一次实测的框宽（跟随鼠标定位要用 ✓，见 `FitBox` 里的赋值）</summary>
    private static float _boxWidth = 0.3f;

    /// <summary>
    /// 把鼠标屏幕坐标换算到**本框父物体**的局部坐标 ✓（抄 Nebula `MouseOverPopup` 的做法：
    /// 它 `ScreenToWorldPoint` 之后直接用世界坐标定位 ✓）
    /// 取不到相机/父物体时返回 false → 调用方退回"挂在行下面"的老写法 ✓
    /// </summary>
    private static bool TryFollowMouse(Transform? anchor, out Vector3 localPos)
    {
        localPos = Vector3.zero;
        try
        {
            if (_parent == null) return false;

            // 用画这个框的相机来换算（按 layer 的 cullingMask 找 ✓，AGENTS §4.3）
            int layerBit = 1 << (_root != null ? _root.layer : 5);
            Camera? cam = null;
            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                if ((c.cullingMask & layerBit) == 0) continue;
                if (cam == null || c.depth > cam.depth) cam = c;
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            float z = Mathf.Abs(cam.transform.position.z);
            if (z < 0.01f) z = 10f;
            var world = cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, z));

            localPos = _parent.InverseTransformPoint(world);
            // z 沿用"锚点行的 z"（已被证明能显示的深度 ✓，前后交给 sortingOrder ✓）
            localPos.z = anchor != null ? anchor.localPosition.z : localPos.z;
            return true;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.TryFollowMouse] {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 给 TMP 一个**受限的 rect 宽度**，否则 `enableWordWrapping` 是空转（TMP 不会换行 ✗）。
    /// `MaxWidth &lt;= 0` 时不限制（保持"单行、按字形算宽"的老行为 ✓）
    /// </summary>
    private static void ApplyWrapWidth()
    {
        try
        {
            if (_text == null) return;
            var rect = _text.rectTransform;
            if (rect == null) return;

            if (Style.MaxWidth > 0f)
            {
                rect.sizeDelta = new Vector2(Style.MaxWidth, rect.sizeDelta.y);
                _text.enableWordWrapping = true;
            }
            else
            {
                _text.enableWordWrapping = false;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyWrapWidth] {ex.Message}");
        }
    }

    /// <summary>
    /// 从**嵌入资源**载入一张**九宫格**图（`border` = 四边不拉伸的像素数 ✓）。
    /// ⚠️ 不能用 `ResourceHelper.LoadSpriteFromResource`（它不设 `Sprite.border` ✗）
    ///    —— 没有 border，`SpriteDrawMode.Sliced` 就会把整张图当普通图拉伸，边框糊掉 ✗
    /// </summary>
    private static Sprite? LoadSlicedSprite(string resourceName, float pixelsPerUnit, float border)
    {
        try
        {
            var asm = typeof(DetailPopup).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LightLogger.LogWarning($"[DetailPopup] 找不到嵌入资源 {resourceName}");
                return null;
            }

            var bytes = new byte[stream.Length];
            _ = stream.Read(bytes, 0, bytes.Length);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { LightLogger.LogWarning($"[DetailPopup] 解码失败 {resourceName}"); return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var rect = new Rect(0f, 0f, tex.width, tex.height);
            var b = new Vector4(border, border, border, border);
            return Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, b);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.LoadSlicedSprite] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 取**被参考行所在的世界 z**（AGENTS §4.3.2："z 直接用被参考行的 z，别猜方向" ✓）。
    /// 找不到参考渲染器时退回传入的 fallback ✓
    /// </summary>
    private static float GetReferenceWorldZ(Transform? anchor, float fallback)
    {
        try
        {
            var reference = FindReferenceRenderer(anchor);
            if (reference != null) return reference.transform.position.z;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.GetReferenceWorldZ] {ex.Message}");
        }
        return fallback;
    }

    /// <summary>找渲染这个父物体所在层的 **UI 相机**（按 layer 的 cullingMask 判定 ✓，AGENTS §4.3）</summary>
    private static Camera? FindUiCameraFor(Transform parent)
    {
        try
        {
            int layerBit = 1 << parent.gameObject.layer;
            Camera? best = null;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null) continue;
                if ((cam.cullingMask & layerBit) == 0) continue;
                if (best == null || cam.depth > best.depth) best = cam;   // 取 depth 最大的那台（UI 相机 ✓）
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>把当前样式写回已建出来的渲染器/文字（颜色、字号、加粗强度）✓</summary>
    private static void ApplyStyleToExisting()
    {
        try
        {
            if (_fill != null) _fill.color = Style.Fill;
            if (_top != null) _top.color = Style.Border;
            if (_bottom != null) _bottom.color = Style.Border;
            if (_left != null) _left.color = Style.Border;
            if (_right != null) _right.color = Style.Border;

            if (_text != null)
            {
                _text.color = Style.Text;
                _text.alignment = Style.Align;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyStyle] {ex.Message}");
        }
    }

    private static GameObject? _root;
    private static Transform? _parent;
    private static TextMeshPro? _text;
    private static Sprite? _fallbackWhite;                            // 自建 1×1 白图（自己持有，不会被场景切换带走）
    private static SpriteRenderer? _fill;
    /// <summary>
    /// 九宫格边框图（复制自 Nebula `NebulaPluginNova\Resources\GUI\Background_Frame.png` ✓，
    /// 内填充是 `Background_Inner.png` ✓）—— 用户 2026-10-06："那个材质，你复制一下呢" ✓
    /// </summary>
    private static SpriteRenderer? _frame;
    private static Sprite? _frameSprite;
    private static Sprite? _innerSprite;
    private static SpriteRenderer? _top;
    private static SpriteRenderer? _bottom;
    private static SpriteRenderer? _left;
    private static SpriteRenderer? _right;

    private static float _boxHeight = 0.20f;                   // 当前框高（把框挂在行的下面用）
    private static float _loggedW = -1f;                              // 上次记录的框宽/高（只在变化时打日志）
    private static float _loggedH = -1f;
    private static bool _showing;                                     // 鼠标当前停在带 detail 的行上
    private static bool _pinned;                                      // 被 Tab 钉住
    private static string _currentText = "";

    /// <summary>显示提示框。</summary>
    public static void Show(string? text, bool canPinByTab = true, Transform? anchor = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }

            var parent = anchor != null ? anchor.parent : null;
            if (parent == null)
            {
                return;
            }

            Ensure(parent, anchor);
            if (_root == null || _text == null) return;

            // ① 每次显示都校验一次背景贴图：主菜单卸载后缓存的 WhiteSprite 会变成"假 null"，
            //    那样 SpriteRenderer 的 sprite 为空 → 框和背景整个不渲染（只有 TMP 文字还在）。
            RefreshPartSprites();

            // ①' 从配置读一遍样式（配色/字号/留白/尺寸/边框/对齐）—— 游戏里改完立刻生效 ✓
            ApplyConfig();

            // ② 渲染设置：GameObject.layer（相机 cullingMask）+ sortingLayer/Order 都跟着行走
            ApplyRenderSettings(anchor);

            string body = text!.Trim();
            if (canPinByTab) body += PinHint;              // ← 可按 Tab 固定时，空一行补提示

            // ★ 换行宽度：开了 `MaxWidth` 才给 TMP 一个受限的 rect 宽度（否则 TMP 不会换行 ✗）
            ApplyWrapWidth();

            if (_currentText != body || _lastWrapWidth != Style.MaxWidth)
            {
                _text.text = body;

                // ⚠️⚠️ 2026-10-06 用户报「鼠标从 A 移到 B，框的大小还是 A 的，再放一次才对」——
                //    根因：`Ensure()` 里 `_root.SetActive(false)` ✓，而这里是**先 FitBox 再 SetActive(true)** ✗
                //    → `ForceMeshUpdate()` 在**未激活**的对象上**不生效** ✗
                //      → `textBounds` 还是**上一次**那份 → 框尺寸慢一拍 ✓✓
                //    TMP 的第一个参数就是治这个的：`ignoreActiveState = true` ✓
                //    （第二个 `forceTextReparsing = true` 保证这次一定重排 ✓）
                _text.ForceMeshUpdate(true, true);

                _currentText = body;
                _lastWrapWidth = Style.MaxWidth;
            }
            FitBox();

            // ⚠️ 用户报「复盘按钮和设置按钮重叠」那次同款坑：位置会被原版布局组件每帧算回去 ✗
            //    （这里是**我们自己的**位置计算，所以只需按 Nebula 的做法**跟随鼠标**即可 ✓）
            //    抄 Nebula `MouseOverPopup`（`NebulaManager.cs:133-137`）：
            //      鼠标在**右半屏** → 框摆到光标左侧；在**下半屏** → 框摆到光标上方（自动避让边缘 ✓）
            Vector3 pos;
            if (TryFollowMouse(anchor, out var mousePos))
            {
                // ★★ 照搬 Nebula `MouseOverPopup`（`NebulaManager.cs:133-155`）：
                //    ① 按鼠标所在**象限**决定把框摆到哪一侧（`isLeft/isLower` ✓）
                //    ② 位置 = 光标位置 − **对角锚点** → 框整个落在光标"反方向"那一侧 ✓
                //    ③ 再夹进屏幕内（Nebula 的四边修正 ✓）
                bool mouseOnLeft = Input.mousePosition.x < Screen.width * 0.5f;
                bool mouseOnLower = Input.mousePosition.y < Screen.height * 0.5f;

                float boxW = Mathf.Max(Style.MinWidth, _boxWidth);
                float boxH = Mathf.Max(Style.MinHeight, _boxHeight);

                const float gap = 0.15f;      // Nebula 的 0.15 ✓
                // 锚点 = 框的"靠近光标的那条边"（鼠标在左 → 框摆在光标右侧 → 锚点取左边界 ✓）
                float anchorX = mouseOnLeft ? -boxW * 0.5f - gap : boxW * 0.5f + gap;
                float anchorY = mouseOnLower ? boxH * 0.5f + gap : -boxH * 0.5f - gap;

                pos = mousePos + new Vector3(anchorX, anchorY, 0f);
            }
            else
            {
                // 拿不到鼠标空间时退回老写法（挂在被悬浮行下面 ✓）
                pos = anchor != null ? anchor.localPosition : Vector3.zero;
                pos.y -= _boxHeight * 0.5f + 0.10f;
            }

            pos.x = Mathf.Clamp(pos.x, -2.2f, 2.2f);       // 简单夹紧，别跑出面板
            pos.y = Mathf.Clamp(pos.y, -2.4f, 2.4f);

            _root.transform.localPosition = pos;

            // ⚠️⚠️ 2026-10-06 用户报「层级还是有问题」—— 日志实证根因在 **z**，不在排序号 ✗：
            //     [诊断] 参考行 : sorting=0(Default)/0    world=(…, …, **-910.00**)
            //     [诊断] 框-底  : sorting=0(Default)/2000 world=(…, …,     **0.00**)
            //   挂到 UI 相机下之后位置是 `ScreenToWorldPoint` 算出来的 → z 变成 0 ✗，
            //   而设置菜单整体在 z = -910 ✓ → 前后关系完全错位 ✓
            //   ★ AGENTS §4.3.2 的规矩正是治这个的：
            //     "**z 直接用被参考行的 z**，前后关系交给 sortingOrder，**别猜方向**" ✓
            //   → 把框搬到"行自己的 z 再往前 5"（往前一点，保证不被同一排序层的邻居压住 ✓）
            float refZ = GetReferenceWorldZ(anchor, _root.transform.position.z);
            var wp = _root.transform.position;
            _root.transform.position = new Vector3(wp.x, wp.y, refZ - 5f);

            _showing = true;
            _pinned = false;                               // 每次悬停都是"未固定"状态
            _root.SetActive(true);

            LogDiagnostics(anchor);                        // 一次性诊断（最多 6 次）
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[DetailPopup.Show]", ex);
        }
    }

    /// <summary>隐藏提示框（被 Tab 钉住时不隐藏）。</summary>
    public static void Hide()
    {
        try
        {
            _showing = false;
            if (_pinned) return;
            if (_root != null) _root.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.Hide] {ex.Message}");
        }
    }

    /// <summary>每帧调用（挂在设置菜单的 Update 上）：处理 Tab 固定 / 取消固定。</summary>
    public static void Update()
    {
        try
        {
            if (_root == null || !_root.activeSelf) return;
            if (!Input.GetKeyDown(KeyCode.Tab)) return;

            _pinned = !_pinned;
            if (!_pinned && !_showing && _root != null)
            {
                _root.SetActive(false);                    // 取消固定时鼠标已移开 → 直接收起
            }
            LightLogger.Log($"[DetailPopup] Tab → {(_pinned ? "已固定介绍" : "取消固定")}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.Update] {ex.Message}");
        }
    }

    /// <summary>重置状态（设置菜单重建 / 关闭时调用），并收起提示框。</summary>
    public static void Reset()
    {
        _showing = false;
        _pinned = false;
        _currentText = "";
        if (_root != null) _root.SetActive(false);
    }

    // ────────────────────────────────────────────────────────────────

    private static void Ensure(Transform parent, Transform? anchor)
    {
        if (_root != null && _parent == parent) return;    // 已建好且父物体没变（Unity 假 null 会自动判为需要重建）

        Destroy();

        _parent = parent;
        _root = new GameObject("LightDetailPopup");

        // ★★ 照搬 Nebula `MouseOverPopup.Awake/SetWidgetOld`（`NebulaManager.cs:131`）：
        //    `transform.SetParent(FindCamera(uiLayer).transform)` —— **挂到 UI 相机下面** ✓
        //    为什么必须这样：挂在被悬浮那一行的父物体下时，原版布局组件（`AspectPosition` 等）
        //    会**每帧**把位置算回去（AGENTS §4.4）→ 框"跟不动/位置乱跳" ✗
        //    （用户 2026-10-06 报的按钮重叠就是同一个坑 ✓）
        var cam = FindUiCameraFor(parent);
        if (cam != null) _root.transform.SetParent(cam.transform, false);
        else _root.transform.SetParent(parent);            // 找不到相机就退回原父物体 ✓

        _root.transform.localScale = Vector3.one;

        var white = GetWhiteSprite();

        // 半透明灰底
        _fill = MakePart("Fill", Vector3.zero, Style.Fill, white);

        // ★ 复制来的 Nebula 材质：**只用中间填充图** ✓
        //   用户 2026-10-06 看图后明确要求："改成白字，把边框扔掉，只要中间填充物" ✓
        //   → 边框（`DetailFrame` 那张九宫格 + 4 条细条）**一律不用** ✗
        _innerSprite = LoadSlicedSprite("Light.Resources.UI.DetailInner.png", 100f, 8);
        if (_innerSprite != null && _fill != null)
        {
            _fill.sprite = _innerSprite;
            _fill.drawMode = SpriteDrawMode.Sliced;
        }
        HidePart(_frame);   // 边框不要 ✓

        // 淡金细边框（四条细条；z 略小 = 更靠前）
        _top = MakePart("BorderTop", Vector3.zero, Style.Border, white);
        _bottom = MakePart("BorderBottom", Vector3.zero, Style.Border, white);
        _left = MakePart("BorderLeft", Vector3.zero, Style.Border, white);
        _right = MakePart("BorderRight", Vector3.zero, Style.Border, white);

        // 文字：优先克隆 anchor（那一行）自己的文字，字号/字体/缩放就和那一行完全一致
        _text = CreateText(_root.transform, anchor);

        _root.SetActive(false);
        LightLogger.Log($"[DetailPopup] 提示框已创建（灰底+淡金边框；文字来源={(_textSourceIsRowText ? "行内文字" : "统一文本模板")}）");
    }

    private static bool _textSourceIsRowText;

    private static TextMeshPro? CreateText(Transform parent, Transform? anchor)
    {
        _textSourceIsRowText = false;

        // ① 用那一行自己的文字当模板（字号/字体/缩放最准，也避开标准预制体的大 rect/margin）
        try
        {
            var source = anchor != null ? anchor.GetComponentInChildren<TextMeshPro>(true) : null;
            if (source != null)
            {
                var clone = Object.Instantiate(source, parent);
                clone.name = "DetailText";
                clone.transform.localPosition = Vector3.zero;
                clone.text = "";

                clone.enableAutoSizing = false;                    // 必须关：否则改 fontSize 不生效
                clone.fontSize = source.fontSize * Style.FontScale;      // 比那一行文字再大一点
                clone.alignment = Style.Align;
                clone.enableWordWrapping = Style.MaxWidth > 0f;                  // 不换行：宽高由我们按字形算
                clone.overflowMode = TextOverflowModes.Overflow;   // 别被原版省略号截断
                clone.raycastTarget = false;
                clone.color = Style.Text;

                var tr = clone.GetComponent<TextTranslatorTMP>();
                if (tr != null) tr.enabled = false;                // 别让翻译器改写我们的文本

                ApplyBold(clone);
                _textSourceIsRowText = true;
                return clone;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup] 克隆行内文字失败，退回统一模板：{ex.Message}");
        }

        // ② 退回统一文本模板（主界面字体 + 辉光白）
        var fallback = MenuTextTemplate.Create(parent, Vector3.zero, "", FallbackFontSize * Style.FontScale, Style.Text);
        ApplyBold(fallback);
        return fallback;
    }

    /// <summary>
    /// 加粗：用**材质描边**（描边颜色 = 文字颜色）把笔画加粗。
    /// 不用 &lt;b&gt; / FontStyles.Bold —— 那是顶点偏移式伪粗体，笔画容易糊在一起；
    /// <c>fontMaterial</c> 是实例材质，只影响这一处文字，不会污染共享材质。
    /// </summary>
    private static void ApplyBold(TextMeshPro? tmp)
    {
        try
        {
            if (tmp == null) return;

            var mat = tmp.fontMaterial;
            if (mat == null) return;

            mat.SetFloat("_OutlineWidth", Style.BoldOutlineWidth);
            mat.SetColor("_OutlineColor", Style.Text);
            mat.SetFloat("_OutlineSoftness", 0f);      // 必须为 0：柔化会把笔画糊开（"糊"的元凶）
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyBold] {ex.Message}");
        }
    }

    /// <summary>
    /// 画框用的 1×1 白图。
    ///
    /// 为什么不用 <c>VanillaAsset.WhiteSprite</c>：它是懒加载缓存的原版资源，
    /// **主菜单卸载后会被销毁成"假 null"**，那时 SpriteRenderer.sprite 变空 →
    /// 框和背景整个不渲染（只有 TMP 文字还在，因为文字走的是字体材质）。
    /// 大厅里"框和背景消失"就是这个原因。
    /// 这里自己造一张并自己持有引用，不受场景切换影响。
    /// </summary>
    private static Sprite? GetWhiteSprite()
    {
        try
        {
            if (_fallbackWhite != null) return _fallbackWhite;

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, UColor.white);
            tex.Apply();
            _fallbackWhite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            LightLogger.Log("[DetailPopup] 已创建自建白图（1×1）");
            return _fallbackWhite;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[DetailPopup.GetWhiteSprite]", ex);
            return null;
        }
    }

    /// <summary>确保 5 个部件的贴图都有效（失效就重新赋上）。每次显示前调用，很便宜。</summary>
    private static void RefreshPartSprites()
    {
        try
        {
            var white = GetWhiteSprite();
            if (white == null) return;

            int fixedCount = 0;
            foreach (var sr in new[] { _fill, _top, _bottom, _left, _right })
            {
                if (sr == null) continue;
                if (sr.sprite != null) continue;       // 有效贴图 → 不动（被销毁的对象在 Unity 里判为 null，会走下面重赋）
                sr.sprite = white;
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                LightLogger.Log($"[DetailPopup] 重新赋予背景贴图 {fixedCount} 个部件（原贴图已失效）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.RefreshPartSprites] {ex.Message}");
        }
    }

    /// <summary>取一个"确实在显示"的参考渲染器：优先行上的 ToggleButtonBehaviour.Background，其次行内任意激活的 SpriteRenderer。</summary>
    private static SpriteRenderer? FindReferenceRenderer(Transform? anchor)
    {
        try
        {
            if (anchor == null) return null;

            var tbb = anchor.GetComponent<ToggleButtonBehaviour>();
            if (tbb != null && tbb.Background != null && tbb.Background.gameObject.activeInHierarchy)
            {
                return tbb.Background;
            }

            foreach (var sr in anchor.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null) continue;
                if (!sr.gameObject.activeInHierarchy) continue;
                return sr;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 让提示框和"被悬浮行"用同一套渲染设置：**GameObject layer**（相机 cullingMask 按它过滤）、
    /// sortingLayer、以及明显更高的 sortingOrder（+10 / 文字 +11）。
    ///
    /// 为什么需要：行是从原版预制体克隆来的，自带正确的 layer / sorting；
    /// 提示框的部件是我们运行时 new GameObject + AddComponent 出来的 —— 默认 layer 0、Default 排序层、order 0，
    /// 在不同菜单（尤其大厅）里可能既被相机剔除、又被面板挡住。
    /// </summary>
    private static void ApplyRenderSettings(Transform? anchor)
    {
        try
        {
            // ⚠️ 用户 2026-10-06 报「层级」：框被选项行**盖住**了 ✗ —— 原来只比那一行高 10 不够 ✓
            //    直接抬到 +2000：提示框本来就该盖住菜单里的一切 ✓（Nebula 也是把弹窗放到很前面 ✓）
            const int margin = 2000;

            int layerId = 0;              // sorting layer
            int order = 0;
            int goLayer = 0;              // GameObject.layer（相机 cullingMask）
            bool hasReference = false;

            var reference = FindReferenceRenderer(anchor);
            if (reference != null)
            {
                layerId = reference.sortingLayerID;
                order = reference.sortingOrder;
                goLayer = reference.gameObject.layer;
                hasReference = true;
            }
            else
            {
                LightLogger.LogWarning("[DetailPopup] 行上找不到可用的 SpriteRenderer，渲染设置沿用默认值（大厅里可能看不见）");
            }

            bool changed = false;

            foreach (var sr in new[] { _fill, _top, _bottom, _left, _right })
            {
                if (sr == null) continue;
                changed |= SetRenderSettings(sr.gameObject, sr, layerId, order + margin, goLayer, hasReference);
            }

            // 文字：TMP 在 interop 里不是 Renderer，直接设它自己的 layer/sorting 属性
            if (_text != null)
            {
                if (hasReference && _text.gameObject.layer != goLayer)
                {
                    _text.gameObject.layer = goLayer;
                    changed = true;
                }
                if (_text.sortingLayerID != layerId)
                {
                    _text.sortingLayerID = layerId;
                    changed = true;
                }
                if (_text.sortingOrder != order + margin + 1)
                {
                    _text.sortingOrder = order + margin + 1;
                    changed = true;
                }
            }

            if (changed)
            {
                string spriteState = _fill != null && _fill.sprite != null ? "有" : "空";
                LightLogger.Log($"[DetailPopup] 渲染设置已同步：layer(排序)={layerId} 参考order={order}（框 {order + margin} / 文字 {order + margin + 1}），GameObject.layer={goLayer}，背景图={spriteState}");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.ApplyRenderSettings] {ex.Message}");
        }
    }

    /// <summary>把 layer / sorting 设置套到某个部件上；返回是否发生了修改。</summary>
    private static bool SetRenderSettings(GameObject go, Renderer renderer, int sortingLayerId, int sortingOrder, int goLayer, bool setGoLayer)
    {
        bool changed = false;

        if (setGoLayer && go.layer != goLayer)
        {
            go.layer = goLayer;
            changed = true;
        }

        if (renderer.sortingLayerID != sortingLayerId)
        {
            renderer.sortingLayerID = sortingLayerId;
            changed = true;
        }

        if (renderer.sortingOrder != sortingOrder)
        {
            renderer.sortingOrder = sortingOrder;
            changed = true;
        }

        return changed;
    }

    // ── 一次性诊断（排查"大厅里看不见"用，最多打 6 次）─────────────────

    private static int _diagLogs;

    private static void LogDiagnostics(Transform? anchor)
    {
        if (_diagLogs >= 6) return;
        _diagLogs++;

        try
        {

            var cam = Camera.main;


            DumpRenderer("[DetailPopup][诊断] 参考行", FindReferenceRenderer(anchor), cam);
            DumpRenderer("[DetailPopup][诊断] 框-底 ", _fill, cam);
            DumpRenderer("[DetailPopup][诊断] 框-上 ", _top, cam);


        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.LogDiagnostics] {ex.Message}");
        }
    }

    private static void DumpRenderer(string tag, SpriteRenderer? sr, Camera? cam)
    {
        try
        {
            if (sr == null) { LightLogger.Log($"{tag}: (null)"); return; }

            var b = sr.bounds;
            string vp = cam != null ? cam.WorldToViewportPoint(sr.transform.position).ToString() : "?";
            string layerName = SortingLayer.IDToName(sr.sortingLayerID);
            string spriteInfo = sr.sprite != null ? $"{sr.sprite.name}/{sr.sprite.bounds.size}" : "(null)";

            LightLogger.Log($"{tag}: layer={sr.gameObject.layer} active={sr.gameObject.activeInHierarchy} enabled={sr.enabled} sprite={spriteInfo} size={sr.size} color={sr.color} sorting={sr.sortingLayerID}({layerName})/{sr.sortingOrder} world={sr.transform.position} bounds={b.size} vp={vp} lossyScale={sr.transform.lossyScale} mat={sr.sharedMaterial?.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"{tag} 输出失败：{ex.Message}");
        }
    }

    private static SpriteRenderer? MakePart(string name, Vector3 localPos, UColor color, Sprite? sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root!.transform);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.color = color;
        return sr;
    }

    /// <summary>
    /// 按**真正画出来的字形范围**摆放灰底与四条边框，并把文字居中到框心。
    /// 用 textBounds（局部坐标）而不是 preferredWidth/Height —— 后者会带上预制体的 rect/margin，
    /// 导致"框很大、字很小"。
    /// </summary>
    private static void FitBox()
    {
        try
        {
            if (_text == null) return;

            Vector3 size = Vector3.zero;
            Vector3 center = Vector3.zero;

            try
            {
                var b = _text.textBounds;
                size = b.size;
                center = b.center;
            }
            catch { }

            // textBounds 拿不到（或为 0）时退回 preferred 尺寸
            float glyphW = size.x;
            float glyphH = size.y;
            if (glyphW <= 0.001f || glyphH <= 0.001f)
            {
                try
                {
                    glyphW = _text.preferredWidth;
                    glyphH = _text.preferredHeight;
                    center = Vector3.zero;
                }
                catch { }
            }

            float width = Mathf.Max(Style.MinWidth, glyphW + Style.PadX * 2f);
            _boxWidth = width;   // ★ 记下实测宽度（跟随鼠标定位要用 ✓）
            float height = Mathf.Max(Style.MinHeight, glyphH + Style.PadY * 2f);
            _boxHeight = height;

            // 只在尺寸变化时记一行，方便排查"框大/字小"
            if (Mathf.Abs(width - _loggedW) > 0.001f || Mathf.Abs(height - _loggedH) > 0.001f)
            {
                _loggedW = width;
                _loggedH = height;
                LightLogger.Log($"[DetailPopup] 字形 {glyphW:F3}x{glyphH:F3} → 框 {width:F3}x{height:F3}，文字来源={(_textSourceIsRowText ? "行内文字" : "模板")}，字号={(_text != null ? _text.fontSize : 0f):F2}，缩放={(_text != null ? _text.transform.lossyScale.x : 0f):F3}");
            }

            // 把文字居中到框心：文字局部坐标 = -字形中心
            _text.transform.localPosition = new Vector3(-center.x, -center.y, -0.03f);

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float t = Style.BorderThickness;

            if (_fill != null)
            {
                _fill.transform.localPosition = Vector3.zero;
                _fill.size = new Vector2(width, height);
            }

            // ★★ 照搬 Nebula `MouseOverPopup` 的做法：
            //    背景 = **一整块 Sliced 底图**（`background.size = 内容尺寸 + 0.22/0.1` ✓
            //      —— `NebulaManager.cs:183` 的 `UpdateArea` 就是这个 ✓）
            //    我们原来是"填充 + 4 条边框"四个渲染器 ✗ → 现在：
            //      · 填充就是整块底（Sliced 拉伸 ✓，AGENTS §4.3.3 同款）
            //      · 4 条边框只在 `BorderThickness > 0` 时才画（Nebula 那档是 0 = 无边 ✓）
            Set(_fill, Vector3.zero, new Vector2(width, height));
            if (_fill != null)
            {
                _fill.drawMode = SpriteDrawMode.Sliced;
                _fill.tileMode = SpriteTileMode.Continuous;
            }

            // ★ 边框一律不画（用户 2026-10-06："把边框扔掉，只要中间填充物" ✓）
            HidePart(_frame);
            HidePart(_top); HidePart(_bottom); HidePart(_left); HidePart(_right);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[DetailPopup.FitBox] {ex.Message}");
        }
    }

    /// <summary>隐藏一条边框（`BorderThickness = 0` 时用 ✓）</summary>
    private static void HidePart(SpriteRenderer? sr)
    {
        try { if (sr != null && sr.gameObject.activeSelf) sr.gameObject.SetActive(false); } catch { }
    }

    private static void Set(SpriteRenderer? sr, Vector3 localPos, Vector2 size)
    {
        if (sr == null) return;
        sr.transform.localPosition = localPos;
        sr.size = size;
    }

    private static void Destroy()
    {
        try
        {
            if (_root != null) Object.Destroy(_root);
        }
        catch { }

        _root = null;
        _parent = null;
        _text = null;
        _fill = null;
        _top = null;
        _bottom = null;
        _left = null;
        _right = null;
        _currentText = "";
    }
}
