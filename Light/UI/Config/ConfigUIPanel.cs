using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Light.Config;
using Light.Patches;
using Light.UI.HudUI;          // HudUIButton
using Light.UI.Window;
using LightInDark.Configuration;
using LightInDark.Core;
using LightInDark.Language;
using LightInDark.UI.Window;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UColor = UnityEngine.Color;

namespace Light.UI.Config
{
    /// <summary>
    /// 配置项 UI —— 在现有「MOD 设置」页签里，用**原版控件**渲染配置块。
    ///
    /// 结构（每个块）：
    ///   金色分类头（克隆原版 <see cref="CategoryHeaderMasked"/>，可调色）
    ///     └ 若干配置行
    ///        · Bool  → 克隆原版 ToggleOption（勾选框）
    ///        · Int/Float → 克隆原版 NumberOption（- 值 +）
    ///        · Value/Filter → 克隆原版 StringOption（循环切换）
    ///
    /// ⚠️ 关键坑（必须遵守，否则行会被原版逻辑覆盖）：
    ///   1. 原版 NumberOption/ToggleOption/StringOption 都是**每帧/每 FixedUpdate**
    ///      从 <c>data.GetValueString(Value)</c>、<c>data.GetValue()</c> 回写显示，
    ///      而自定义配置项没有 <c>BaseGameSetting</c> → 必须
    ///      <c>option.enabled = false</c> 停掉它们的 Update/FixedUpdate，
    ///      再由我们自己的 <see cref="ConfigRowDriver"/> 驱动显示。
    ///   2. 克隆出来的 <c>PassiveButton.OnClick</c> **自带原版点击逻辑**，
    ///      必须 <c>new ButtonClickedEvent()</c> 整体替换（不是 AddListener 追加）。
    ///   3. <c>z = -2f</c>（与原版行一致），分类头 <c>localScale = 0.63</c>。
    /// </summary>
    public static class ConfigUIPanel
    {
        // ---- 版面对齐原版 GameOptionsMenu.CreateSettings 的常量 ----
        private const float StartY = 0.713f;
        private const float RowX = 0.952f;
        private const float HeaderX = -0.903f;

        /// <summary>
        /// **单职业模式下各元素的左对齐基准**（用户 2026-10-06："怎么好看怎么来"）。
        ///
        /// ⚠️ 用**绝对值**而不是"普通页 + 偏移"：
        ///    分类头和配置行的**内部布局不同** ——
        ///    分类头的文字相对它自己的中心偏左约 0.97，配置行的标题相对行原点偏左约 1.11，
        ///    所以"同一个偏移量"不可能让两者同时对齐（实测就是这么偏的）。
        ///
        /// 实测数据（用户截图反推，120px = 1 单位，local x=0 ↔ 屏幕 816）：
        /// <code>
        ///   元素              改前左边缘   改后
        ///   返回按钮          -3.18       -3.00
        ///   职业名            -2.43(压住按钮！)  -1.80
        ///   职业配置(Head)    -2.22       -3.00
        ///   配置行「数量」     -0.51       -3.00
        /// </code>
        ///
        /// ⚠️ 普通分类页**完全不受影响**（那些仍走 <see cref="RowX"/> / <see cref="HeaderX"/>）。
        /// </summary>
        private const float SingleRowX = -1.84f;      // 用户 2026-10-06："让加号到我画的地方" → 整行右移 1.04（+ 从屏幕 728 → 850）
        private const float SingleHeaderX = -2.03f;   // 分类头中心（其文字左边缘 ≈ -3.00）

        /// <summary>配置行当前该用的 x。</summary>
        private static float CurRowX => _singleBlock != null ? SingleRowX : RowX;

        /// <summary>分类头当前该用的 x。</summary>
        private static float CurHeaderX => _singleBlock != null ? SingleHeaderX : HeaderX;
        private const float HeaderHeight = 0.63f;
        private const float SpacingY = 0.45f;
        private const int MaskLayer = 20;
        private const float RowZ = -2f;

        /// <summary>本页承载的容器（挂在 MOD 设置页下）。</summary>
        private static GameObject _page;
        private static Transform _container;
        /// <summary>当前分类过滤（null = 全部显示）。</summary>
        private static ConfigCategory[]? _categoryFilter;
        /// <summary>单职业模式：只渲染这一个块（职业配置页）。</summary>
        private static ConfigBlock? _singleBlock;
        /// <summary>单职业模式：返回按钮的回调（回职业列表页）。</summary>
        private static Action? _onBack;
        private static readonly List<GameObject> _spawned = new();
        private static readonly Dictionary<ConfigItem, ConfigRowDriver> _drivers = new();
        /// <summary>真正建成功的行数（不是 _spawned.Count，那个在失败时也会加）。</summary>
        private static int _rowCount;

        public static bool Built => _page != null;

        /// <summary>按分类过滤显示配置块（切换 MOD 设置页的标签时调用）。</summary>
        public static void Show(ConfigCategory[] categories, Transform parent)
        {
            _categoryFilter = categories;
            _singleBlock = null;
            _onBack = null;
            _lastVisibilitySig = "";      // 换了页 → 签名作废，必须重新建 ✓（并让滚动回到顶部 ✓）
            Rebuild(parent);
            ResetScrollToTop();
        }

        /// <summary>单职业模式：只渲染一个职业块，顶部带返回按钮（点击回职业列表）。</summary>
        public static void ShowRole(ConfigBlock block, Transform parent, Action onBack)
        {
            Clear();
            _categoryFilter = null;
            _singleBlock = block;
            _onBack = onBack;
            _lastVisibilitySig = "";      // 同上 ✓
            Build(parent);
            ResetScrollToTop();
        }

        /// <summary>
        /// 把托管 MonoBehaviour 注册进 IL2CPP。
        ///
        /// ⚠️⚠️ 这一步**必须有**，否则 <c>AddComponent&lt;ConfigRowDriver&gt;()</c> 直接抛：
        ///   System.TypeInitializationException:
        ///     The type initializer for 'MethodInfoStoreGeneric_AddComponent_Public_T_0`1' threw
        ///   ---> System.NullReferenceException
        ///   at UnityEngine.GameObject.AddComponent[T]()
        /// 托管类型没有对应的 IL2CPP 类 → interop 查不到 method info → 泛型静态构造炸掉。
        /// 表现极具迷惑性：**物体建出来了、层级位置全对、activeInHierarchy 也是 true，
        /// 但部件一个都不显示**（因为异常让后续 Bind/RefreshVisual 全都没跑）。
        /// 本工程其它自定义 MonoBehaviour 都走同样做法：Dispatcher / MetaScreen /
        /// TextFieldBehaviour / LightUtils.AttachComponent。
        /// </summary>
        private static bool _driverRegistered;

        private static void EnsureDriverRegistered()
        {
            if (_driverRegistered) return;
            _driverRegistered = true;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ConfigRowDriver>();
            }
            catch (Exception ex)
            {
                // 已注册过会抛，忽略即可
                LightLogger.Log($"[ConfigUIPanel] ConfigRowDriver 注册（可能已注册）：{ex.Message}");
            }
        }

        /// <summary>安全挂组件：类型未注册时补注册重试一次。</summary>
        private static T? AddComponentSafe<T>(GameObject go) where T : MonoBehaviour
        {
            try
            {
                return go.AddComponent<T>();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel] AddComponent<{typeof(T).Name}> 失败：{ex.Message}，补注册后重试");
                try { ClassInjector.RegisterTypeInIl2Cpp<T>(); } catch { }
                try { return go.AddComponent<T>(); }
                catch (Exception ex2)
                {
                    LightLogger.LogError($"[ConfigUIPanel] AddComponent<{typeof(T).Name}> 重试仍失败", ex2);
                    return null;
                }
            }
        }

        // =====================================================================
        //  外部 API —— 供其它模块往"原版设置页"里叠自己的配置 UI
        //  【借鉴 TONE】ToN 把所有设置项抽象成 OptionItem 列表，再由
        //  GameOptionsMenuPatch 统一铺到原版 settingsContainer 里
        //  （TONE\Patches\GameOptionsMenuPatch.cs:54-185）。
        //  这里给出等价的对外接口，外部只需注册 ConfigBlock 即可，不用碰 UI。
        // =====================================================================

        /// <summary>
        /// 本面板当前是否应该接手某个 <see cref="GameOptionsMenu"/> 的铺行。
        ///
        /// 判定依据：这个 menu 是否就是"MOD 设置"页签背后的那个。
        /// 我们把它记在 <see cref="_hostMenu"/>（由 GameSettingMenuPatch 在切页时登记）。
        /// 未登记 → 返回 false → 原版页签完全不受影响。
        /// </summary>
        public static bool ShouldHandleCreateSettings(GameOptionsMenu menu)
        {
            if (menu == null) return false;
            if (_hostMenu == null) return false;
            return menu.GetInstanceID() == _hostMenu.GetInstanceID();
        }

        /// <summary>登记"MOD 设置"页签背后的那个 GameOptionsMenu（切页时调用）。</summary>
        public static void SetHostMenu(GameOptionsMenu? menu)
        {
            _hostMenu = menu;
        }

        /// <summary>
        /// 【对外主入口】把当前过滤条件下的配置块铺进原版 <c>settingsContainer</c>。
        ///
        /// 由 <c>ConfigRowPatches.CreateSettingsPrefix</c> 在原版铺行时机调用。
        /// 外部模块**不需要**调用这个 —— 只要 <see cref="ConfigRegistry"/> 里注册了
        /// <see cref="ConfigBlock"/>，切到对应页签时就会自动出现。
        /// </summary>
        public static void BuildIntoVanilla(GameOptionsMenu menu)
        {
            try
            {
                // ⚠️ 必须用 DestroySpawned（会真的销毁物体），不能用 _spawned.Clear()：
                //    后者只清列表 → 行变成孤儿留在容器里 → 和 Build() 那条路径叠成两份。
                DestroySpawned();

                // ⚠️⚠️⚠️ 2026-10-10 用户报「还是外泄」——根因就在这里 ✗✗：
                //    这里**直接用 `menu.settingsContainer`**，**没走 `ResolveRowContainer`** ✗
                //    而 `settingsContainer` 在菜单没初始化时指向 `SliderInner`（一个滑块 ✗）——
                //    `ResolveRowContainer` 里那两处"拒绝 SliderInner"的检查**完全没被用上** ✗✗
                //    日志实证：
                //      [ConfigUIPanel] 已铺入原版容器 'SliderInner'：行 27 个   ← 27 行塞进滑块内部 ✓ = 外泄
                //    → 改走同一套解析（它会拒绝坏容器并退回自建容器 ✓）
                var fallback = _page != null ? _page.transform : menu.settingsContainer;
                var container = ResolveRowContainer(fallback);
                if (container == null)
                {
                    LightLogger.LogWarning("[ConfigUIPanel] 解析不到行容器，无法铺行");
                    return;
                }

                _container = container;

                // 先清掉原版自己会画的东西（地图预览 + 原版设置行 + 分类头），否则两层叠在一起
                ClearVanillaContent(menu);

                // y 从原版起点开始（可能已收回地图预览的高度，见 StartYFor）
                float startY = StartYFor(menu);
                float y = startY;

                foreach (var block in ConfigRegistry.Blocks)
                {
                    if (!MatchesFilter(block)) continue;
                    y = BuildBlock(block, y);
                }

                LightLogger.Log($"[ConfigUIPanel] 已铺入原版容器 '{container.name}'：行 {_rowCount} 个");
                SnapshotBuiltKeys();     // 同上：记录实际建出的行
                LogContainerState(menu, "BuildIntoVanilla");

                UpdateScrollBounds(menu, startY, y);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.BuildIntoVanilla]", ex);
            }
        }

        /// <summary>
        /// 【自动检测滚动条】按"内容高度 vs 可视高度"设置滚动范围。
        ///
        /// 原版 Scroller 自己就会隐藏滚动条（Scroller.cs:310-320）：
        ///     if (!showY || ContentYBounds.min >= ContentYBounds.max) ScrollbarY.Toggle(false);
        /// 条件是 **min >= max**。
        ///
        /// 我之前的实现只调了 SetYBoundsMax，**从来没设过 min** →
        /// ContentYBounds 保持字段默认值 `new FloatRange(-10f, 10f)` 的 min = -10，
        /// 于是永远 min &lt; max → 滚动条永远显示（用户："这个滚动条就不应该有"）。
        ///
        /// 现在：
        ///   · 内容装得下 → min = max = 0 → 原版自动隐藏，且滚不动；
        ///   · 内容装不下 → min = 0、max = 溢出高度 → 正常滚动。
        /// 可视高度从 menu.MaskArea（原版的遮罩矩形）量出来，不靠猜。
        /// </summary>
        private static void UpdateScrollBounds(GameOptionsMenu menu, float startY, float lastY)
        {
            try
            {
                var sb = menu.scrollBar;
                if (sb == null) return;

                // ★★ 2026-10-06 用户报「配置项的滚动条没用，所有我们加的都是没用的」——
                //    根因（读原版 `Scroller.Update` L176-183 得到）：
                //    <code>
                //      if (this.MouseMustBeOverToScroll &amp;&amp; this.ClickMask)
                //          this.mouseOver = this.ClickMask.OverlapPoint(...);
                //      if (!this.MouseMustBeOverToScroll || this.mouseOver)   // ← 否则**滚轮被忽略** ✗
                //          { var v = Input.mouseScrollDelta * ScrollWheelSpeed; ScrollRelative(v); }
                //    </code>
                //    也就是：滚轮要生效，必须"鼠标压在 ClickMask 上（且 ClickMask 非空）"，
                //    否则整段跳过 ✗ —— 我们铺进去的行不在原版那套 collider 范围里 ✗ → 滚不动 ✓
                //    → 两条都补上：
                //      ① `MouseMustBeOverToScroll = false`（本页是 MOD 设置页，滚轮随时可用 ✓）
                //      ② 顺手把 `ClickMask` 接上原版点击遮罩（双保险 ✓，也让拖动滚动条正常 ✓）
                try
                {
                    sb.MouseMustBeOverToScroll = false;
                    var mask = menu.ButtonClickMask;
                    if (mask != null) sb.ClickMask = mask;

                    // ★ 保险：`Scroller` 继承自原版的 UI 元素基类，**组件被禁用或物体没激活时
                    //   它的 `Update` 根本不会跑** ✗ → 滚轮和拖动全都无响应 ✓（正是用户报的现象 ✓）
                    if (!sb.enabled) { sb.enabled = true; LightLogger.Log("[ConfigUIPanel] Scroller 原本是禁用的 → 已启用 ✓"); }
                    if (sb.gameObject != null && !sb.gameObject.activeSelf)
                    {
                        sb.gameObject.SetActive(true);
                        LightLogger.Log("[ConfigUIPanel] Scroller 所在物体原本未激活 → 已激活 ✓");
                    }

                    // 一次性诊断：把"滚轮能不能生效"的每个条件都打出来 ✓（下次一跑就知道卡在哪 ✓）
                    if (_scrollDiagLogs < 3)
                    {
                        _scrollDiagLogs++;
                        var yBar = sb.ScrollbarY;
                        LightLogger.Log($"[ConfigUIPanel][滚动取证] enabled={sb.enabled} active={sb.gameObject.activeSelf} " +
                                        $"MouseMustBeOverToScroll={sb.MouseMustBeOverToScroll} ClickMask={(sb.ClickMask != null ? "有" : "null ✗")} " +
                                        $"Inner={(sb.Inner != null ? "有" : "null ✗")} ScrollbarY={(yBar != null ? (yBar.gameObject.activeSelf ? "有/显示" : "有/隐藏") : "null ✗")} " +
                                        $"bounds=({sb.ContentYBounds.min:F2},{sb.ContentYBounds.max:F2}) 滚轮速度={sb.ScrollWheelSpeed}");
                    }
                }
                catch (Exception ex)
                {
                    LightLogger.LogWarning($"[ConfigUIPanel.UpdateScrollBounds] 设置滚轮可用性失败：{ex.Message}");
                }

                // ⚠️⚠️ 2026-10-10 用户报「还是不能滚动」——新日志实证：`bounds` 在跳 ✗
                //      (0,7.81) → (0,**0.00**) → (0,10.72)   ← max=0 那一刻 = 判定"装得下" → 滚不动 ✗✗
                //   根因：原来 `contentHeight` 是**照注册表"数"出来的**（只数 `IsVisible` 的项 ✓），
                //   而行是**增量增删**的 ✓ → 中间态数不准 ✗ → 偶尔算出"装得下" ✓
                //   ★ 改成按**行的真实位置**算（地面真相 ✓）：取所有已建行里最低的那个 y ✓
                //     行就在场上，量它不会有中间态 ✓✓
                float contentHeight = MeasureContentHeightFromRows(startY);
                if (contentHeight < 0f) contentHeight = Mathf.Max(0f, startY - lastY) + 0.35f;   // 取不到行才退回原算法 ✓
                float viewport = MeasureViewportHeight(menu);

                // ★ 2026-10-10 用户："那个滚动条底下留一点空间，现在这个滑到底最下面的配置项想按到非常艰难" ✓
                //   → 能滚的时候，把可滚范围**多加一段底部留白** ✓
                //     这样最后一行能滚到"离开底边一点"的位置，点得动 ✓
                //   ⚠️ 判定"要不要滚动条"仍用**真实内容高** ✓ —— 装得下就绝不显示滚动条 ✗
                const float BottomPadding = 1.30f;
                bool needBar = contentHeight > viewport + 0.01f;
                float overflow = needBar ? Mathf.Max(0f, contentHeight + BottomPadding - viewport) : 0f;

                sb.SetYBoundsMin(0f);
                sb.SetYBoundsMax(needBar ? overflow : 0f);

                // ⚠️⚠️ 2026-10-06 用户报「滚动还没生效，拖动和滑轮都用不了」——根因就在这里 ✗✗：
                //    原来**每次**调用都把 `Inner.localPosition.y` 写回 0 ✗，
                //    而 `UpdateScrollBounds` 会被 Build / Relayout / 增量可见性 / Refresh 链反复调用 ✓
                //    → 玩家刚滚一点，下一帧就被拽回顶部 ✓ = "滚不动" ✓✓
                //    ★ 正确做法（状态差量）：**只有内容高度真的变了**（换页/增删行）才归零 ✓，
                //      否则**一个字都不写** ✓ —— 让原版 Scroller 自己管 Inner 的位置 ✓
                bool contentChanged = Mathf.Abs(contentHeight - _lastContentHeight) > 0.01f;
                _lastContentHeight = contentHeight;

                // ⚠️⚠️ 2026-10-10 用户报「还是不能滚动」——日志给出了真因 ✗：
                //     增量可见性**每帧抖一次**（+1/-1 交替），内容高度就在 11.42 ↔ 10.97 之间跳 ✓
                //     → `contentChanged` **每帧都为真** ✗ → `Inner` 每帧被归零 → 永远滚不动 ✓✓
                //   ★ 所以这里**只负责范围（min/max）**，**绝不碰 `Inner` 位置** ✓
                //     归零改到"换页/重开面板"时做（`ResetScrollToTop()` ✓），滚轮/拖动由原版全权处理 ✓
                sb.UpdateScrollBars();   // 让原版按 min/max 决定 Toggle

                // ★★ 2026-10-10 「还是无法滚动」的**根治** ✓✓：
                //    原版 `Scroller` 滚的是**它自己的 `Inner`** ✓
                //    （`ScrollRelative` → `Inner.localPosition` ✓，滚轮/拖动/滚动条最后都落到这一句 ✓）
                //    而我们的行铺在**自建容器**（`_container` = `LightConfigPage` 或它的子物体 ✓）里 ✗
                //    → 原版挪的是另一个物体 → **滚了等于没滚** ✓✓（日志实证：
                //      "Scroller.Inner 指向了 'SliderInner'（已知坏容器），拒绝使用，退回自建容器" ✓
                //       —— 拒绝是对的 ✓，但拒绝之后没把 `Inner` 改成我们的容器 ✗）
                //    → 现在把 `Inner` 指向我们真正的行容器 ✓，之后滚轮/拖动/滚动条**全部交给原版** ✓
                //      （离开页签时 `RestoreVanillaState()` 会还原 ✓）
                if (_container != null && sb.Inner != _container)
                {
                    if (_originalInner == null) _originalInner = sb.Inner;   // 只记第一次的原值 ✓
                    sb.Inner = _container;
                    LightLogger.Log($"[ConfigUIPanel] Scroller.Inner → '{_container.name}' ✓（滚轮/拖动由此生效）");
                }

                LightLogger.Log($"[ConfigUIPanel] 滚动条自动检测：内容高 {contentHeight:F2} / 可视高 {viewport:F2} " +
                                $"→ {(needBar ? $"需要（溢出 {overflow:F2}）" : "不需要，已隐藏")}");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.UpdateScrollBounds] {ex.Message}");
            }
        }

        /// <summary>
        /// **按行的真实位置**量内容高度（返回 &lt;0 表示量不到，调用方退回旧算法 ✓）。
        ///
        /// ⚠️ 为什么不用"照注册表数一遍"（那是原实现 ✗）：
        ///   行是**增量**增删的 ✓ → 在"注册表说该显示 / 行还没建出来"的中间态里数出来会偏小 ✗
        ///   → `overflow = 0` → `SetYBoundsMax(0)` → **滚动被禁用**（用户："还是不能滚动" ✓✓）
        ///   行本身就在场上 ✓，量它的 y 是**地面真相** ✓ 不会抖 ✓
        /// </summary>
        private static float MeasureContentHeightFromRows(float startY)
        {
            try
            {
                if (_drivers.Count == 0) return -1f;

                float lowest = float.MaxValue;
                int counted = 0;

                foreach (var kv in _drivers)
                {
                    var drv = kv.Value;
                    if (drv == null) continue;
                    var go = drv.gameObject;
                    if (go == null || !go.activeSelf) continue;      // 只量显示中的行 ✓

                    float y = go.transform.localPosition.y;
                    if (y < lowest) lowest = y;
                    counted++;
                }

                if (counted == 0 || lowest == float.MaxValue) return -1f;

                // 分类头也在场，但它们的 y 通常不小于行；用"最低的行"已经足够 ✓
                return Mathf.Max(0f, startY - lowest) + 0.35f;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.MeasureContentHeightFromRows] {ex.Message}");
                return -1f;
            }
        }

        /// <summary>
        /// 量出滚动可视区高度（容器局部单位）。
        /// 用原版的遮罩 sprite `MaskArea` 的高度作为可视高度；取不到就退回一个保守值。
        /// </summary>
        private static float MeasureViewportHeight(GameOptionsMenu menu)
        {
            try
            {
                var mask = menu.MaskArea;
                if (mask == null || mask.sprite == null) return 3.2f;   // 保守兜底

                float hWorld = mask.bounds.size.y;
                var inner = menu.scrollBar?.Inner;
                if (inner != null && hWorld > 0.0001f)
                {
                    // 世界高度 → 容器局部高度（容器没有缩放时两者相同）
                    float hLocal = Mathf.Abs(inner.InverseTransformVector(new Vector3(0f, hWorld, 0f)).y);
                    if (hLocal > 0.01f && hLocal < 30f) return hLocal;
                }
                return hWorld > 0.01f ? hWorld : 3.2f;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.MeasureViewportHeight] {ex.Message}");
                return 3.2f;
            }
        }

        /// <summary>
        /// 起始 y：把被我们藏掉的**地图预览**占的高度收回来。
        ///
        /// 用户反馈："上面会空一大块，可能是原版选地图的地方"。
        /// 就是它 —— 原版把 MapPicker 放在设置列表最上面（GameOptionsMenu.cs:85
        /// `Children.Add(this.MapPicker)`），我们为了不重叠把它 SetActive(false) 了，
        /// 那块空间就空着。这里量出它的高度并补回起始高度，让内容顶上来。
        /// （量不到就维持原版起点，不会更糟。）
        /// </summary>
        private static float StartYFor(GameOptionsMenu menu)
        {
            try
            {
                // ⚠️ 只量一次并缓存。
                // 重建时地图预览已经被我们 SetActive(false)，未激活渲染器的 bounds 可能量到 0
                // → 偏移丢失 → 顶上那块空缺又回来（用户报的正是这个）。
                // 地图预览的高度是预制体属性，量一次就够了。
                if (_mapPickerHeight < 0f)
                    _mapPickerHeight = MeasureMapPickerHeight(menu);

                if (_mapPickerHeight <= 0.01f) return StartY;

                float startY = StartY + _mapPickerHeight;
                return startY;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.StartYFor] {ex.Message}");
                return StartY;
            }
        }

        /// <summary>缓存的地图预览高度（&lt;0 表示还没量过）。</summary>
        private static float _mapPickerHeight = -1f;

        /// <summary>量出地图预览占的高度（容器局部单位）。必须在隐藏它**之前**调用。</summary>
        private static float MeasureMapPickerHeight(GameOptionsMenu menu)
        {
            try
            {
                var mp = menu.MapPicker;
                if (mp == null) return 0f;

                // 取地图预览里最高的渲染器高度
                float h = 0f;
                foreach (var sr in mp.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null || sr.sprite == null) continue;
                    float sy = sr.bounds.size.y;
                    if (sy > h) h = sy;
                }
                if (h <= 0.01f) return 0f;

                var inner = menu.scrollBar?.Inner;
                float hLocal = h;
                if (inner != null)
                    hLocal = Mathf.Abs(inner.InverseTransformVector(new Vector3(0f, h, 0f)).y);

                // 合理性检查：0.2~3 之间才采用（避免量错把内容顶飞）
                if (hLocal < 0.2f || hLocal > 3f) return 0f;

                LightLogger.Log($"[ConfigUIPanel] 收回地图预览高度 {hLocal:F2} → 起始 y = {StartY + hLocal:F2}");
                return hLocal;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.MeasureMapPickerHeight] {ex.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// 一次性诊断：打印容器里"我们的"子物体数量，用于验证行有没有重复。
        /// 若这个数 &gt; _spawned.Count，说明有孤儿行没被销毁。
        /// </summary>
        private static void LogContainerState(GameOptionsMenu menu, string where)
        {
            try
            {
                var inner = menu.scrollBar?.Inner ?? menu.settingsContainer;
                if (inner == null) return;

                int ours = 0;
                for (int i = 0; i < inner.childCount; i++)
                {
                    var c = inner.GetChild(i);
                    if (c != null && c.name.StartsWith(OurPrefix, StringComparison.Ordinal)) ours++;
                }

                LightLogger.Log($"[ConfigUIPanel.Diag] {where}：容器 '{inner.name}' 子物体 {inner.childCount} 个，" +
                                $"其中我们的 {ours} 个（登记 {_spawned.Count} 个）{(ours > _spawned.Count ? " ← ⚠️ 有孤儿行！" : "")}");
            }
            catch { }
        }

        /// <summary>
        /// 【唯一销毁入口】彻底清掉我们建过的一切（行、分类头、返回按钮、页面对象）。
        ///
        /// ⚠️ 存在的意义（这是"行重复"的根因）：
        ///   之前有三处各自清理，其中 <see cref="BuildIntoVanilla"/> 里只写了
        ///   <c>_spawned.Clear()</c> —— **只清列表、不销毁物体**！
        ///   于是那些行变成没人引用的孤儿留在容器里，再也清不掉。
        ///   而我们的页签有**两条建行路径**：
        ///     ① 原版 CreateSettings 时机 → CreateSettingsPrefix → BuildIntoVanilla
        ///     ② 切页签 Show() → Rebuild() → Build()
        ///   两条都往同一个 settingsContainer 里加行 → 用户看到的就是"行重复"
        ///   （"正常 4 行、现在 6 行"），并且第二次之后可见性判定被污染
        ///   （"只有第一次打勾会出现附带项"）。
        ///
        ///   现在所有清理都走这一个函数，并且 <see cref="Build"/> 开头**无条件**先调它，
        ///   所以无论从哪条路径进来，容器里永远只有一套我们的行。
        /// </summary>
        private static void DestroySpawned()
        {
            try
            {
                foreach (var go in _spawned)
                {
                    if (go == null) continue;

                    // ⚠️ 必须**立刻**销毁，不能只靠 Object.Destroy。
                    //
                    // Unity 的 Object.Destroy 是**延迟到帧末**执行的。而我们的流程是
                    // "同一帧内先销毁旧行、紧接着又建新行"（Rebuild→Clear→Build），
                    // 于是那一帧里新旧两套行**同时存在于容器中**：
                    //   · 原版按 ChildCount/层级重建导航时会把两套都算进去；
                    //   · 新行还没走完 Start→Initialize 时旧行仍在响应点击。
                    // 用户看到的就是"复选框成对出现 [] [x] [x] []"和"点两下才生效"。
                    // DestroyImmediate 已经存在，用它保证容器里同帧只有一套行。
                    Object.DestroyImmediate(go);
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.DestroySpawned] {ex.Message}");
            }

            _spawned.Clear();
            _drivers.Clear();
            _rowCount = 0;
            RowMap.Clear();

            if (_page != null) { Object.DestroyImmediate(_page); _page = null; _container = null; }
        }

        /// <summary>宿主 GameOptionsMenu（"MOD 设置"页签背后的那个）。</summary>
        private static GameOptionsMenu? _hostMenu;

        /// <summary>
        /// 只清掉已生成的行，保留面板对象与宿主登记。
        /// 切页签时用：让下一次 CreateSettings 在干净的容器里重铺。
        /// </summary>
        public static void ClearRowsOnly() => DestroySpawned();

        /// <summary>
        /// 我们为了不叠层而**关掉的原版物体**（GameObject → 它原来的 activeSelf ✓）。
        ///
        /// ⚠️ 为什么必须记：MOD 页签与"游戏设置"页签**共用同一个 `GameOptionsMenu`** ✓
        ///    我们单向 `SetActive(false)` 之后不还原 ✗ → 切回原版页签就是**一片空白** ✓
        ///    （用户 2026-10-06 报的"游戏设置里面的原版内容咋还被隐藏了" ✓）
        /// </summary>
        private static readonly List<(GameObject Go, bool WasActive)> _hiddenVanilla = new();

        /// <summary>
        /// 供 `GameSettingMenuPatch` 记录"我们压住的原版页" ✓
        /// —— 压住时必须记账 ✓，否则切回原版页签时它们放不回来（"游戏设置"空白 ✗）
        /// </summary>
        internal static void RememberHiddenForRestore(GameObject go) => RememberHidden(go);

        /// <summary>记下一个被我们关掉的原版物体（同一物体只记一次 ✓）</summary>
        private static void RememberHidden(GameObject go)
        {
            try
            {
                if (go == null) return;
                for (int i = 0; i < _hiddenVanilla.Count; i++)
                    if (_hiddenVanilla[i].Go == go) return;

                _hiddenVanilla.Add((go, true));      // 只有 activeSelf == true 的才会走到这里 ✓
            }
            catch { }
        }

        /// <summary>
        /// **把我们关过的原版物体原样恢复** ✓（离开 MOD 页签时调用 ✓）。
        /// 这是"原版页签被清空"的根治点 ✓ —— 只恢复还活着的对象，死了的跳过 ✓
        /// </summary>
        private static void RestoreHiddenVanilla()
        {
            try
            {
                if (_hiddenVanilla.Count == 0) return;

                int restored = 0;
                foreach (var (go, wasActive) in _hiddenVanilla)
                {
                    if (go == null) continue;                 // Unity 假 null 会被 == 正确识别 ✓
                    if (go.activeSelf != wasActive) go.SetActive(wasActive);
                    restored++;
                }
                _hiddenVanilla.Clear();

                if (restored > 0)
                    LightLogger.Log($"[ConfigUIPanel] 已恢复 {restored} 个原版物体（离开 MOD 页签 ✓）");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.RestoreHiddenVanilla] {ex.Message}");
            }
        }
        ///
        /// 【借鉴 TONE】ToN 的做法是把行挂到**原版 GameOptionsMenu 自己的 settingsContainer**
        /// （TONE\Patches\GameOptionsMenuPatch.cs:129-135），而不是自己新建一个容器。
        /// 这样做的原因（实测踩到）：
        ///   · settingsContainer 的原版层级关系、缩放、遮罩、以及它父级的 z 都是对的；
        ///   · 我们自己 new 一个物体挂在 ROLES TAB 下时，父链 z 会累加偏离，
        ///     结果被菜单自己的不透明背景盖住 → 表现为"构建日志一切正常、屏幕上一片空白"。
        ///
        /// 因此：能用原版容器就用原版容器，取不到才退回自建容器。
        /// </summary>
        /// <summary>
        /// **强制把行铺进调用方传入的容器**，跳过原版宿主与模板宿主。
        ///
        /// 用途：职业详情的**独立窗口**（<c>RoleDetailWindow</c>）。
        /// 窗口没有原版的 Scroller，行必须铺进窗口自己的节点，
        /// 否则会被 <see cref="ResolveRowContainer"/> 送回原版菜单 —— 表现是"窗口是空的"。
        ///
        /// ⚠️ 用完必须关掉（<c>SetForceOwnContainer(false)</c>），否则原版规则编辑界面也会跑到自建容器里。
        /// </summary>
        public static void SetForceOwnContainer(bool value) => _forceOwnContainer = value;

        /// <summary>
        /// 藏掉单职业模式顶部那个「&lt; 返回」按钮。
        /// 用途：独立窗口用右上角的 X 关窗，不需要再来一个返回（用户 2026-10-06 要求）。
        /// </summary>
        public static void SetHideBackButton(bool value) => _hideBackButton = value;

        private static bool _hideBackButton;

        private static bool _forceOwnContainer;

        /// <summary>
        /// 实际用的行容器（诊断用）。
        /// </summary>
        internal static string ContainerName => _container != null ? _container.name : "(null)";

        /// <summary>
        /// 行容器解析。优先原版 settingsContainer（TONE 同做法），见下方注释。
        /// 因此：能用原版容器就用原版容器，取不到才退回自建容器。
        /// </summary>
        private static Transform ResolveRowContainer(Transform fallback)
        {
            // ⚠️⚠️ **独立窗口模式必须直接用自己的容器**（2026-10-06）。
            //
            //  用户截图：二级职业窗口打开了、尺寸黑幕都对，**但里面是空的**。
            //  原因就是这里 —— 我只把 `_hostMenu` 摘成 null，但这个方法是
            //  `_hostMenu ?? _templates`，**`_templates` 还在**（它是原版菜单的 GameOptionsMenu），
            //  于是行被铺回了**原版菜单的滚动容器**，根本不在窗口里。
            //
            //  所以窗口模式要一个更强的开关：连 `_templates` 也跳过，直接用传入的 fallback。
            if (_forceOwnContainer) return fallback;

            try
            {
                var host = _hostMenu ?? _templates;

                // ⚠️ 必须用 Scroller.Inner，**不能**直接读 settingsContainer 字段。
                //
                // 原版 GameOptionsMenu.settingsContainer 是 [SerializeField] 私有字段，
                // 在**没跑过 Start/Initialize 的克隆体**上读它拿到的是错的
                // （实测拿到 'SliderInner'——一个滑块，childCount=22，完全不是行容器）。
                // 而 Scroller.Inner 是同一个物体的**公开字段**，任何克隆体上都能正确读到。
                // 原版自己也是把行 Instantiate 到 settingsContainer（== Scroller.Inner）下的。
                var inner = TryGetScrollerInner(host);

                // ⚠️⚠️⚠️ **已知坏容器：名字是 "SliderInner"** —— 拒绝它。
                //
                //  2026-10-06 日志实证（这就是"第一次进完全是歪的，之后一直是第二次的样子"的根因）：
                // <code>
                //  14:18:27  模式=单职业，容器=SliderInner，      累计实例化=2   ← 第一次，歪的
                //  14:18:28  模式=单职业，容器=LightConfigPage，  累计实例化=4   ← 第二次起，正的
                //  行容器使用 Scroller.Inner 'SliderInner' (localPos=(0.00, 0.00, -4.00), childCount=22)
                // </code>
                //  那个 `childCount=22` 和下面第 525 行注释里记的坏容器**一模一样** ——
                //  它是个滑块，不是行容器；行铺进去当然整个是歪的。
                //
                //  ⚠️ 下面那段注释原来断言"`Scroller.Inner` 是公开字段，任何克隆体上都能正确读到"，
                //     这条**在第一次调用时不成立**：菜单还没跑过 Start/Initialize，`scrollBar.Inner`
                //     指向的还不是行容器。第二次起菜单已初始化，才指向正确的那个。
                //     （用名字判定不优雅，但这是目前唯一能区分"初始化前/后"的稳定特征，
                //       而且这个名字已经被本文件的注释当成"坏容器"记下来了。）
                if (inner != null && inner.name == "SliderInner")
                {
                    LightLogger.LogWarning("[ConfigUIPanel] Scroller.Inner 指向了 'SliderInner'（已知坏容器，是个滑块）" +
                                           "，拒绝使用，退回自建容器（这是第一次进页面时的正常现象）");
                    inner = null;
                }

                if (inner != null && inner.gameObject.activeInHierarchy)
                {
                    LightLogger.Log($"[ConfigUIPanel] 行容器使用 Scroller.Inner '{inner.name}' " +
                                    $"(localPos={inner.localPosition}, childCount={inner.childCount})");
                    return inner;
                }
                if (inner != null)
                    LightLogger.LogWarning($"[ConfigUIPanel] Scroller.Inner '{inner.name}' 未激活，继续找备选");

                var hostSc = host?.settingsContainer;

                // ⚠️⚠️ 这里**也要拦 'SliderInner'** —— 2026-10-06 日志实证：
                //    上面那条 `Scroller.Inner` 的拦截生效之后，代码紧接着落到这个备选分支，
                //    而 `settingsContainer` **指向的又是同一个 'SliderInner'**：
                // <code>
                //   [ConfigUIPanel] Scroller.Inner 指向了 'SliderInner'（已知坏容器）...拒绝使用
                //   [ConfigUIPanel] 行容器使用 settingsContainer 'SliderInner' (childCount=22)   ← 又中招
                // </code>
                //    原版这两个字段在"菜单还没初始化"时都会指向那个滑块，所以**两处都要拦**。
                if (hostSc != null && hostSc.name == "SliderInner")
                {
                    LightLogger.LogWarning("[ConfigUIPanel] settingsContainer 也指向了 'SliderInner'（同上），拒绝使用，退回自建容器");
                    hostSc = null;
                }

                if (hostSc != null && hostSc.gameObject.activeInHierarchy)
                {
                    LightLogger.Log($"[ConfigUIPanel] 行容器使用 settingsContainer '{hostSc.name}' " +
                                    $"(childCount={hostSc.childCount})");
                    return hostSc;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.ResolveRowContainer] {ex.Message}");
            }
            return fallback;
        }

        /// <summary>从 GameOptionsMenu 的 Scroller 上取 Inner（行真正该挂的容器）。</summary>
        private static Transform? TryGetScrollerInner(GameOptionsMenu? menu)
        {
            try
            {
                var scroller = menu?.scrollBar;
                return scroller?.Inner;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.TryGetScrollerInner] {ex.Message}");
                return null;
            }
        }

        /// <summary>在给定父级下构建配置面板（幂等：已建则先彻底清掉再重铺）。</summary>
        public static void Build(Transform parent)
        {
            try
            {
                // ⚠️ 开头**无条件**清一次，而不是 `if (_page != null) { Refresh(); return; }`。
                //
                // 原来那个提前返回有个致命组合：BuildIntoVanilla（原版 CreateSettings 路径）
                // 不设 _page，所以它铺完行之后 _page 仍是 null → Build 认为"没建过" → 又铺一遍
                // → 同一个容器里出现两套行（用户看到的"行重复"）。
                // 现在 Build 永远从一个干净的容器开始，两条路径谁先谁后都只留一套。
                DestroySpawned();

                EnsureDriverRegistered();

                _page = NewUIObject("LightConfigPage", parent, new Vector3(0f, 0f, RowZ));

                // 行容器优先用原版 settingsContainer（TONE 同做法，见 ResolveRowContainer 注释）
                _container = ResolveRowContainer(_page.transform);

                // ⚠️ 顺序很重要：**先量地图预览的高度，再隐藏它**。
                //    反过来（先隐藏再量）在重建时会量到 0 → 收回的偏移丢失 →
                //    用户看到的"点击一次配置项后那块被地图占的空缺又出现了"。
                var hostForClean = _hostMenu ?? _templates;
                float startY = StartY;
                if (hostForClean != null)
                {
                    startY = StartYFor(hostForClean);
                    ClearVanillaContent(hostForClean);
                }

                float lastY;
                if (_singleBlock != null)
                {
                    // ---- 单职业模式 ----
                    //
                    // ⚠️ 2026-10-06 用户："返回放我圈的位置" + "这个详情页上面是不是又被隐藏的地图
                    //    选项顶了？" —— 两个问题同源，都在这里：
                    //
                    //    · 原来 `AddBackButton(0.8f)`，返回按钮在 y=0.8，
                    //      而它返回 0.8-0.38-0.06 = **0.36** 当内容起点 → 整块被压得很低
                    //      （普通页的内容起点是 StartYFor = 1.313，低了约 0.95 ≈ 114px）。
                    //    · 单职业模式**不需要** StartYFor 那个 +0.60 ——
                    //      那是给原版"地图预览"留的高度，而这一页根本不显示地图预览
                    //      （ClearVanillaContent 已经把它隐藏了）。
                    //
                    //    现在：返回按钮放高一点（用户圈的位置 ≈ 本地 y 1.05），
                    //    内容紧接在它下面开始。
                    const float backY = 1.05f;
                    var roleTpl = RoleListPage.FindRole(_singleBlock);

                    if (!_hideBackButton) AddBackButton(backY);
                    AddRoleNameLabel(backY, roleTpl);     // ★ 职业名（放返回键旁边）

                    float y = backY - BackBtnH - 0.14f;   // 内容从返回按钮下方开始
                    startY = y;

                    // ★ 单职业模式**不再建"写职业名的分类头"**（用户："把原来那个上面写职业名的 Head 删掉"）。
                    //   改成：立绘区 → 「职业配置」头（原色）→ 配置项
                    AddRoleArt(roleTpl);                  // 立绘 → 固定右下角，不占排版空间
                    y = AddCategoryHeader(_singleBlock, y, "职业配置", useOriginalColor: true);

                    foreach (var item in _singleBlock.Items)
                    {
                        if (!item.IsVisible) continue;
                        y = AddConfigRow(item, y);
                    }
                    lastY = y;
                }
                else
                {
                    // 渲染所有已注册配置块（按分类过滤：调试块/职业块均在 ConfigRegistry 中）
                    float y = startY;
                    foreach (var block in ConfigRegistry.Blocks)
                    {
                        if (!MatchesFilter(block)) continue;
                        y = BuildBlock(block, y);
                    }
                    lastY = y;
                }

                LightLogger.Log($"[ConfigUIPanel] 已构建配置面板：行 {_rowCount} 个，子物体 {_spawned.Count} 个" +
                                $"，模式={(_singleBlock != null ? "单职业" : "分类")}" +
                                $"，startY={startY:F3}，lastY={lastY:F3}" +
                                $"，容器={(_container != null ? _container.name : "null")}" +
                                $"，本会话累计实例化={_instantiated}");   // ★ 诊断"第一次/第二次不一样"
                SnapshotBuiltKeys();     // 记录"实际建出了哪些行"，供 Refresh 做集合比较
                if (hostForClean != null)
                {
                    LogContainerState(hostForClean, "Build");
                    UpdateScrollBounds(hostForClean, startY, lastY);
                }
                LogDiagnostics(parent);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Build]", ex);
            }
        }

        /// <summary>
        /// 打印原版**可见**设置行的实际位置，作为我们内容的对齐基准。
        /// 这是"我们的行看不见"最直接的对照物：同样的相机、同样的 layer，
        /// 原版行能看见 → 差别只可能在 position / 父容器 / sortingOrder。
        /// </summary>
        private static void DumpVisibleVanillaRow(Camera uiCam)
        {
            try
            {
                var menu = _templates;
                if (menu != null)
                {
                    var sc = menu.settingsContainer;
                    if (sc != null)
                    {
                        var vp = uiCam.WorldToViewportPoint(sc.position);
                    }

                    var children = menu.Children;
                    if (children != null && children.Count > 0)
                    {
                        for (int i = 0; i < children.Count && i < 3; i++)
                        {
                            var ob = children[i];
                            if (ob == null) continue;
                            var vp = uiCam.WorldToViewportPoint(ob.transform.position);
                            var sr = ob.GetComponentInChildren<SpriteRenderer>(true);
                        }
                    }
                    else
                    {
                        LightLogger.Log("[ConfigUIPanel.Diag] 原版 GameOptionsMenu.Children 为空（它还没建过行）");
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.DumpVisibleVanillaRow] {ex.Message}");
            }
        }

        /// <summary>找绘制 UI 层(5)的那台相机。</summary>
        private static Camera FindUICamera()
        {
            try
            {
                int uiLayer = LayerExpansion.GetUILayer();
                Camera best = null;
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    if ((cam.cullingMask & (1 << uiLayer)) == 0) continue;
                    if (best == null || cam.depth > best.depth) best = cam;
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>
        /// 递归打印层级 + 每个渲染器状态。
        /// 排查"物体在、但画不出来"必须看到这一层：渲染器到底存不存在、
        /// sprite 是否为 null（原版资源被卸载会变假 null）、sortingOrder 是否被盖。
        /// </summary>
        private static void DumpHierarchy(Transform t, int depth)
        {
            try
            {
                if (t == null || depth > 4) return;
                string pad = new string(' ', depth * 2);

                var srs = t.GetComponents<SpriteRenderer>();
                foreach (var sr in srs)
                {
                }

                var tmps = t.GetComponents<TextMeshPro>();
                foreach (var tmp in tmps)
                {
                    var mr = tmp.GetComponent<MeshRenderer>();
                }

                for (int i = 0; i < t.childCount; i++)
                    DumpHierarchy(t.GetChild(i), depth + 1);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.DumpHierarchy] {ex.Message}");
            }
        }

        /// <summary>
        /// 一次性诊断：把"看不见"相关的关键事实全打出来（照 AGENTS.md §4.8 的做法，别猜）。
        /// 打印层级 / 父链 / activeInHierarchy / layer / 渲染器状态 / 相机 cullingMask。
        /// </summary>
        private static void LogDiagnostics(Transform parent)
        {
            try
            {
                if (_page == null) { LightLogger.Log("[ConfigUIPanel.Diag] _page 为 null"); return; }

                var uiCam = FindUICamera();
                if (uiCam != null)
                {
                    var vp = uiCam.WorldToViewportPoint(_page.transform.position);

                    // 对照：原版角色页自己的内容在哪（同相机下）
                    if (_templates != null)
                    {
                        var vpT = uiCam.WorldToViewportPoint(_templates.transform.position);
                    }

                    DumpVisibleVanillaRow(uiCam);
                }

                try
                {
                    var parentRoot = _page.transform.root;
                    var peers = parentRoot.GetComponentsInChildren<SpriteRenderer>(true);
                    int shown = 0;
                    var list = new List<(float z, string info)>();
                    foreach (var sr in peers)
                    {
                        if (sr == null) continue;
                        var wp = sr.transform.position;
                        list.Add((wp.z, $"{sr.name}@{sr.transform.parent?.name} z={wp.z:F2} sprite={(sr.sprite == null ? "null" : sr.sprite.name)} order={sr.sortingOrder}"));
                    }
                    list.Sort((a, b) => a.z.CompareTo(b.z));
                    foreach (var e in list)
                    {
                        if (++shown >= 30) break;
                    }
                }
                catch (Exception ex2)
                {
                    LightLogger.LogWarning($"[ConfigUIPanel.Diag] 同层SR枚举失败：{ex2.Message}");
                }

                // 父链 4 层
                var cur = _page.transform;
                for (int d = 0; cur != null && d < 5; d++, cur = cur.parent)


                // 逐个打印我们建的子物体
                for (int i = 0; i < _page.transform.childCount; i++)
                {
                    var c = _page.transform.GetChild(i);
                    if (c == null) continue;
                    var sr = c.GetComponent<SpriteRenderer>();
                    var tmp = c.GetComponentInChildren<TextMeshPro>(true);

                    // 递归打印行的完整层级（含每个渲染器）—— 定位"为什么画不出来"
                    DumpHierarchy(c, 1);
                }

                // 相机 cullingMask（layer 5 是否被渲染）
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    bool has5 = (cam.cullingMask & (1 << 5)) != 0;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.LogDiagnostics] {ex.Message}");
            }
        }

        /// <summary>块是否通过当前分类过滤。</summary>
        private static bool MatchesFilter(ConfigBlock block)
        {
            if (_categoryFilter == null || _categoryFilter.Length == 0) return true;
            foreach (var c in _categoryFilter)
                if (block.Category == c) return true;
            return false;
        }

        /// <summary>清空重建（值变化导致可见性变化时调用）。</summary>
        public static void Rebuild(Transform parent)
        {
            // 诊断：把调用栈打出来，定位"谁在反复触发重建"。
            // 已经排查了三轮都是靠推测，这次直接看调用者。
            LogRebuildCaller();

            Clear();
            Build(parent);
        }

        private static int _rebuildCount;

        private static void LogRebuildCaller()
        {
            try
            {
                _rebuildCount++;
                if (_rebuildCount > 12) return;    // 只看前几次，避免刷屏

                var st = new System.Diagnostics.StackTrace(2, false);
                var sb = new System.Text.StringBuilder();
                int frames = 0;
                for (int i = 0; i < st.FrameCount && frames < 6; i++)
                {
                    var m = st.GetFrame(i)?.GetMethod();
                    if (m == null) continue;
                    sb.Append(m.DeclaringType?.Name).Append('.').Append(m.Name).Append(" < ");
                    frames++;
                }
                LightLogger.Log($"[ConfigUIPanel.Diag] 第 {_rebuildCount} 次 Rebuild，调用链：{sb}");
            }
            catch { }
        }

        /// <summary>清空（销毁一切并复原状态）。</summary>
        public static void Clear()
        {
            DestroySpawned();

            // ★★ 2026-10-10 用户报「游戏设置又没了」——根因就在这里 ✗：
            //    `ClearVanillaContent` 会**隐藏**原版设置行/分类头（记进 `_hiddenVanilla` ✓），
            //    而 `RestoreHiddenVanilla()` **从来没有被调用** ✗ → 切回"游戏设置"页签时一片空白 ✓
            //    `Clear()` 正是"离开本页签"的唯一出口（`GameSettingMenuPatch.ShowTabConfig` 各处都调它 ✓）
            //    → 还原原版内容 + 把滚动容器还回去 ✓
            RestoreVanillaState();
        }

        /// <summary>
        /// 离开 MOD 页签时**把原版状态还回去** ✓：
        ///   ① 我们隐藏过的原版行/分类头 → 原样恢复 ✓（不然"游戏设置"页签是空的 ✗）
        ///   ② 被我们指向自建容器的 `Scroller.Inner` → 还原成原来那个 ✓
        /// </summary>
        private static void RestoreVanillaState()
        {
            try
            {
                RestoreHiddenVanilla();

                var host = _hostMenu ?? _templates;
                var sb = host != null ? host.scrollBar : null;
                if (sb != null && _originalInner != null)
                {
                    if (sb.Inner != _originalInner)
                    {
                        sb.Inner = _originalInner;
                        LightLogger.Log($"[ConfigUIPanel] 已把 Scroller.Inner 还原成 '{_originalInner.name}' ✓");
                    }
                    _originalInner = null;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.RestoreVanillaState] {ex.Message}");
            }
        }

        /// <summary>`Scroller.Inner` 原本指向谁（我们要把它改成自建容器才能滚动 ✓，离开时还原 ✓）</summary>
        private static Transform? _originalInner;

        // =====================================================================
        //  构建
        // =====================================================================

        private static float BuildBlock(ConfigBlock block, float startY = StartY)
        {
            if (block == null) return startY;

            float y = startY;
            y = AddCategoryHeader(block, y);

            foreach (var item in block.Items)
            {
                if (!item.IsVisible) continue;         // 依赖项未满足 → 不建行
                y = AddConfigRow(item, y);
            }

            return y;   // 返回最后用掉到哪个 y，供滚动条算高度
        }

        /// <summary>
        /// 单职业模式**左上角**的返回按钮，返回下一个 y。
        ///
        /// ⚠️ 2026-10-06 第三次改（用户："返回的 X 删掉，改成左上角放一个返回按钮，
        ///    返回用 HudUI，label：返回"）：
        ///    · 第一版是**手画**的圆角块 + 「&lt; 返回」文字；
        ///    · 第二版克隆 `GameSettingMenu.BackButton` —— **出来的是个 X 图标按钮**，不是想要的；
        ///    · 现在用 **HudUI 的按钮**（<c>HudUIButton.Create</c>），label 就是「返回」。
        /// </summary>
        private static float AddBackButton(float y)
        {
            try
            {
                var btn = Light.UI.HudUI.HudUIButton.Create(
                    _container, "返回", new Vector2(BackBtnW, BackBtnH),
                    () => _onBack?.Invoke());

                if (btn != null && btn.GameObject != null)
                {
                    btn.SetPosition(new Vector3(BackBtnX, y, RowZ));
                    _spawned.Add(btn.GameObject);

                    // 用户规矩（AGENTS.md §12.1）：除特殊说明所有字都 Bold
                    if (btn.Text != null)
                    {
                        try { MenuTextTemplate2.ApplyFont(btn.Text); } catch { }
                        btn.Text.fontStyle = FontStyles.Bold;
                    }

                    LightLogger.Log($"[ConfigUIPanel] 返回按钮已用 HudUI 建好（左上角 {BackBtnX}, {y}）");
                    return y - BackBtnH - 0.06f;
                }

                LightLogger.LogWarning("[ConfigUIPanel] HudUIButton.Create 返回 null，退回手画");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.AddBackButton] HudUI 返回按钮失败: {ex.Message}");
            }

            // ---- 保底：手画一个（老实现，保留） ----
            var go = NewUIObject("LightConfigBack", _container, new Vector3(BackBtnX, y, RowZ));
            _spawned.Add(go);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetRoundedSprite();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(BackBtnW, BackBtnH);
            sr.color = new UColor(0.2f, 0.2f, 0.2f, 0.9f);

            MenuTextTemplate.Create(go.transform, new Vector3(0f, 0f, -0.1f), "返回", 0.8f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(BackBtnW, BackBtnH);

            var pbf = go.SetUpButton(true, null, null, null, false);
            pbf.OnClick.AddListener((UnityAction)(() => _onBack?.Invoke()));

            return y - BackBtnH - 0.06f;
        }

        // ---- 返回按钮的尺寸与位置（用户要"左上角"，偏左放；嫌偏直接改这两个数）----
        private const float BackBtnW = 0.95f;
        private const float BackBtnH = 0.38f;
        private const float BackBtnX = -2.525f;  // 中心；左边缘 = -3.00（和分类头/配置行统一左对齐）

        // =====================================================================
        //  单职业模式专用件（用户 2026-10-06 设计图）
        // =====================================================================

        /// <summary>职业名放返回键旁边（复用右侧面板那套：职业自己的颜色 + 大字 + Bold）。</summary>
        private static void AddRoleNameLabel(float y, LightInDark.Roles.RoleTemplate? role)
        {
            try
            {
                if (role == null) return;

                // ⚠️⚠️ **位置要按"文字左边缘"算，不能直接把 pos.x 当左边缘。**
                //
                //  `MenuTextTemplate2.Create` 建出来的 TMP 用的是**模板自带的 rect + 居中 pivot**，
                //  而我们把对齐设成 Left → 文字是从 `pos.x - rectWidth/2` 开始画的。
                //  所以直接传"想让它出现的 x"会**往左偏半个 rect 宽** ——
                //  用户截图里职业名压到返回按钮上，就是这个原因
                //  （和 RoleInfoPanel 早先 PlaceIntro 的坑同源，见 AGENTS.md §4.7）。
                //
                //  正确做法：先定 rect 宽，再反推 pos.x = 目标左边缘 + 宽/2。
                var tmp = MenuTextTemplate2.Create(_container,
                    new Vector3(NameLeft + NameW * 0.5f, y, RowZ - 0.1f),
                    role.Name, RoleNameFontSize,
                    LightInDark.ColorHelper.ToUnityColor(role.Color));
                if (tmp == null) return;

                // 固定 rect 宽（这样上面的反推才成立）
                tmp.rectTransform.sizeDelta = new Vector2(NameW, tmp.rectTransform.sizeDelta.y);
                tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);

                // ⚠️ 必须关 autoSizing 并锁死字号，否则 fontSize 完全不生效（AGENTS.md §12.2）
                tmp.enableAutoSizing = false;
                tmp.fontSize = RoleNameFontSize;
                tmp.fontSizeMin = RoleNameFontSize;
                tmp.fontSizeMax = RoleNameFontSize;

                tmp.fontStyle = FontStyles.Bold;                       // §12.1 所有字默认 Bold
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;         // 名字不能裁（§12.3）

                _spawned.Add(tmp.gameObject);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.AddRoleNameLabel] {ex.Message}");
            }
        }

        /// <summary>
        /// 立绘区（设计图里那个白框）。
        /// 贴图取 <c>RoleTemplate.RoleImage</c>（职业自己重写 RoleImagePath 提供）；
        /// 没配图的职业**不占位**，直接返回原 y，后面的内容自动上移。
        /// 透明度用 Nebula 的值（Help.cs:492 <c>SetBackImage(..., 0.2f)</c>）。
        /// </summary>
        /// <summary>
        /// 职业立绘 —— 固定在**右下角**，而且**不占排版空间**。
        ///
        /// ⚠️ 2026-10-06 用户："更怪了。立绘放右下角吧"。
        ///    前一版它是跟着排版流走的（返回职业名→立绘→职业配置→配置项），
        ///    结果夹在中间、还把「职业配置」挤下去，整体很怪。
        ///    现在改成**绝对定位**：固定落在右下角，配置项从返回按钮下面直接开始排，
        ///    互不干扰。（也不再有 y 的进进出出，调用处不用接返回值。）
        /// </summary>
        private static void AddRoleArt(LightInDark.Roles.RoleTemplate? role)
        {
            try
            {
                var sprite = role?.RoleImage;
                if (sprite == null) return;      // 没配图的职业不显示，也不占位

                var go = NewUIObject("LightRoleArt", _container,
                    new Vector3(RoleArtX, RoleArtY, RowZ + 0.25f));
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;

                // ⚠️⚠️⚠️ **必须设 drawMode + size**（2026-10-06 用户："立绘歪了"）。
                //
                //  只设 sprite 的话，SpriteRenderer 按**贴图的原始像素尺寸**渲染：
                //  立绘画布通常是 1024×1024，而我们的 PPU = 115
                //  → 8.9 个世界单位，**直接把大半个屏幕盖住**（实测就是这样，
                //    截图里立绘从内容区一直铺到右侧滚动条外面）。
                //
                //  本工程画"任意尺寸的图"一律用这套：`Sliced` + `size`
                //  （见 AGENTS.md §4.3.3；RoleListPage / BackgroundPanel / MusicPlayerWindow 同款）。
                sr.drawMode = SpriteDrawMode.Sliced;

                // ★★ **按原图宽高比缩放**（用户 2026-10-06："立绘压的不成样子"）。
                //
                //  原来直接 `size = (RoleArtW, RoleArtH)` 是**硬拉伸** ——
                //  原图什么样都会被拉进那个矩形：横的更长、竖的更扁，脸都变形。
                //
                //  现在把 RoleArtW × RoleArtH 当作**上限框**：
                //  先按最大宽算高，装不下就反过来按最大高算宽 → 等比缩放到框内。
                //  ⚠️ 所以这两个常量现在是"最多占多大"，**不是**强制尺寸。
                float aspect = 1f;
                try
                {
                    var b = sprite.bounds.size;
                    if (b.y > 0.001f && b.x > 0.001f) aspect = b.x / b.y;
                }
                catch { }

                float artW = RoleArtW, artH = RoleArtW / aspect;
                if (artH > RoleArtH) { artH = RoleArtH; artW = RoleArtH * aspect; }

                sr.size = new Vector2(artW, artH);
                LightLogger.LogDebug($"[ConfigUIPanel] 立绘 size={artW:F2}×{artH:F2}（原图比例 {aspect:F3}，框 {RoleArtW}×{RoleArtH}）");

                sr.color = new Color(1f, 1f, 1f, RoleArtAlpha);
                _spawned.Add(go);
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.AddRoleArt] {ex.Message}");
            }
        }

        /// <summary>只写分类头文字、**不改颜色**（用户要的"用原色"）。</summary>
        private static void SetHeaderTextRaw(CategoryHeaderMasked header, string text)
        {
            try
            {
                if (header == null) return;
                var tmp = header.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null)
                {
                    tmp.text = text;
                    tmp.fontStyle = FontStyles.Bold;      // §12.1
                    tmp.ForceMeshUpdate();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.SetHeaderTextRaw] {ex.Message}");
            }
        }

        // ---- 单职业模式的尺寸（嫌不对直接改这几个）----
        private const float RoleNameFontSize = 2.40f;
        /// <summary>职业名文字左边缘（返回按钮右边留 0.25 的间隙）。</summary>
        private const float NameLeft = -1.80f;
        /// <summary>职业名的 rect 宽（左对齐定宽，位置靠它反推）。</summary>
        private const float NameW = 2.60f;
        private const float RoleArtX = 1.70f;      // 立绘中心 x —— 略左一点，给放大后留出右边空间
        private const float RoleArtH = 4.30f;
        /// <summary>立绘显示宽（⚠️ 必须给，否则按 PNG 原始像素尺寸渲染，会盖满屏幕）。</summary>
        private const float RoleArtW = 3.50f;      // 用户："立绘再大点"（上限框，实际尺寸按原图比例）
        private const float RoleArtY = -1.23f;     // 立绘中心 y —— 右列竖长条（用户红框：屏幕 y 258~730）
        private const float RoleArtAlpha = 0.20f;    // Nebula Help.cs:492 用的 0.2f
        /// <summary>金色分类头：克隆原版 CategoryHeaderMasked。</summary>
        /// <param name="labelOverride">覆盖要显示的标题文字；null = 用 block.DisplayName。</param>
        /// <param name="useOriginalColor">true = **不染成职业色**，保持原版分类头的本色（用户："用原色"）。</param>
        private static float AddCategoryHeader(ConfigBlock block, float y,
            string? labelOverride = null, bool useOriginalColor = false)
        {
            try
            {
                var origin = FindHeaderTemplate();
                if (origin == null)
                {
                    LightLogger.LogWarning("[ConfigUIPanel] 找不到原版分类头模板(CategoryHeaderMasked)，跳过分类头");
                    return y;
                }

                var header = Object.Instantiate(origin, Vector3.zero, Quaternion.identity, _container);

                // 分类头同理：它是 CategoryHeaderMasked 克隆体，也可能带 AspectPosition
                DisableAspectPositionOn(header.transform);
                header.name = $"LightConfigHeader_{block.Key}";
                header.transform.localScale = Vector3.one * HeaderHeight;
                header.transform.localPosition = new Vector3(CurHeaderX, y, RowZ);
                header.gameObject.SetActive(true);
                _spawned.Add(header.gameObject);

                // 头文字：SetHeader 走 StringNames，我们用翻译槽位塞自定义文本
                if (useOriginalColor)
                {
                    // "用原色"：只写文字，**不调 SetHeaderColor** —— 保持原版分类头本来的颜色。
                    SetHeaderTextRaw(header, labelOverride ?? block.DisplayName);
                }
                else
                {
                    SetHeaderText(header, block);
                }

                y -= HeaderHeight;
                return y;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.AddCategoryHeader]", ex);
                return y;
            }
        }

        /// <summary>
        /// 设置分类头文字与颜色。
        /// 原版 SetHeader 只接受 StringNames，所以我们直接写它的 Title 文本
        /// （比挪用翻译槽位更直接，且不会影响别处）。
        /// </summary>
        private static void SetHeaderText(CategoryHeaderMasked header, ConfigBlock block)
        {
            try
            {
                header.SetHeader(StringNames.None, MaskLayer);   // 先走一遍原版：设 mask/stencil

                // 再覆盖文字
                var tmp = header.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null)
                {
                    var tr = tmp.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;          // 别被翻译器改回去
                    tmp.text = block.DisplayName;
                }

                // 调色：分类头的背景/分隔线是 SpriteRenderer
                if (block.HeaderColor.HasValue)
                {
                    var color = block.HeaderColor.Value;
                    foreach (var sr in header.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr == null) continue;
                        sr.color = color;
                    }
                    if (tmp != null) tmp.color = color;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.SetHeaderText] {ex.Message}");
            }
        }

        internal static readonly Dictionary<int, ConfigItem> RowMap = new();

        private static GameOptionsMenu _templates;

        /// <summary>模板源是否已就绪。</summary>
        public static bool TemplatesReady => _templates != null;

        internal static void SetTemplateSource(GameOptionsMenu menu)
        {
            if (menu == null) return;
            bool wasNull = _templates == null;
            _templates = menu;

            if (wasNull && _page != null && RowMap.Count == 0)
            {
                var parent = _container != null ? _container.parent : null;
                LightLogger.Log("[ConfigUIPanel] 模板源到位，重建配置面板");
                if (parent != null) Rebuild(parent);
            }
        }

        /// <summary>
        /// 合成一个原版 BaseGameSetting，喂给 SetUpFromData。
        ///
        /// ⚠️ 这是 ToN 的核心做法，也是之前"行建了但什么都不显示"的关键：
        ///   原版 ToggleOption/NumberOption/StringOption 的一切显示都由 <c>Data</c> 驱动
        ///   （Initialize/FixedUpdate 读 <c>data.GetValueString(...)</c>、<c>data.GetValue()</c>）。
        ///   只 Instantiate 而不 SetUpFromData → <c>Data</c> 为 null → 原版逻辑要么空引用、
        ///   要么一个部件都不点亮 → 界面上什么都看不到。
        ///   做法：ScriptableObject.CreateInstance&lt;XxxGameSetting&gt;() 造一个真 setting 交出去，
        ///   之后**不需要**任何 prefix 拦截，原版自己就会正确显示。
        /// </summary>
        private static BaseGameSetting? BuildSetting(ConfigItem item)
        {
            try
            {
                BaseGameSetting setting = item.Type switch
                {
                    ConfigType.Bool => ScriptableObject.CreateInstance<CheckboxGameSetting>(),
                    ConfigType.Value or ConfigType.Filter => ScriptableObject.CreateInstance<StringGameSetting>(),
                    _ => item.Type == ConfigType.Float
                        ? ScriptableObject.CreateInstance<FloatGameSetting>()
                        : (BaseGameSetting)ScriptableObject.CreateInstance<IntGameSetting>(),
                };

                if (setting == null) return null;

                // ⚠️ Title 用**我们借来的翻译槽位**，不要用 StringNames.Accept。
                // 之前写 Accept 导致点开关时原版 Toggle() 回查 `Accept` 这个 title，
                // 触发 "Could not update value of Accept" + ToggleOption.Toggle 的 NRE。
                setting.Title = ConfigTranslationPatch.SlotFor(item);

                switch (setting)
                {
                    case CheckboxGameSetting cb:
                        cb.Type = OptionTypes.Checkbox;
                        // ⚠️ OptionName **保持 Invalid**（不要给 VisualTasks 之类）。
                        // 之前为了消 "Could not update value of ..." 日志塞了
                        // `BoolOptionNames.VisualTasks`，结果被原版 Initialize 用来
                        // 去**真实游戏规则**里读值 → 勾选框显示的是"可视任务"的真值，
                        // 不是我们的配置（用户报的"要点两次才能变成未选中"）。
                        // 现在 UpdateValue 已被 Prefix 跳过，日志问题不存在了，
                        // 这里就不再借用任何真实选项名，避免误读误写真实规则。
                        break;

                    case IntGameSetting i:
                        i.Type = OptionTypes.Int;
                        i.Value = item.GetInt();
                        i.Increment = (int)Math.Max(1f, item.Step);
                        i.ValidRange = new IntRange((int)item.Min, (int)item.Max);
                        i.ZeroIsInfinity = false;
                        i.FormatString = item.SuffixText();
                        i.SuffixType = ToSuffix(item.SuffixText());
                        break;

                    case FloatGameSetting f:
                        f.Type = OptionTypes.Float;
                        f.Value = item.GetFloat();
                        f.Increment = item.Step;
                        f.ValidRange = new FloatRange(item.Min, item.Max);
                        f.ZeroIsInfinity = false;
                        f.FormatString = item.SuffixText();
                        f.SuffixType = ToSuffix(item.SuffixText());
                        break;

                    case StringGameSetting s:
                        s.Type = OptionTypes.String;
                        int n = item.Selections != null && item.Selections.Length > 0 ? item.Selections.Length : 1;
                        s.Values = new StringNames[n];
                        s.Index = Mathf.Clamp(item.GetInt(), 0, n - 1);
                        break;
                }

                return setting;
            }
            catch (Exception ex)
            {
                LightLogger.LogError($"[ConfigUIPanel.BuildSetting] {item.Key}", ex);
                return null;
            }
        }

        /// <summary>
        /// 后缀 → NumberSuffixes。
        /// ⚠️ 19.0 的枚举只有 None / Multiplier / Seconds（**没有 Percent**）。
        /// 其余后缀（"%"/"次" 等）由 FormatString 负责，原版只认这两个。
        /// </summary>
        private static NumberSuffixes ToSuffix(string suffix) => suffix switch
        {
            "s" => NumberSuffixes.Seconds,
            "x" => NumberSuffixes.Multiplier,
            _ => NumberSuffixes.None,
        };

        /// <summary>
        /// 清掉克隆菜单里**原版自己**会画的内容，只留我们的行。
        ///
        /// 用户反馈："原版的界面会叠在上面"。原因是克隆出来的 GameOptionsMenu
        /// 自带原版的整套设置（地图预览 MapPicker + 原版所有设置行），
        /// 我们只是在它上面又加了自己的行 → 两层叠在一起。
        ///
        /// 照 TONE 的做法（GameOptionsMenuPatch.cs:30-33）：
        ///   · MapPicker 直接关掉
        ///   · Children 清空（原版就是按这个列表铺行与刷新的）
        /// </summary>
        private static void ClearVanillaContent(GameOptionsMenu menu)
        {
            try
            {
                // ① 地图预览（TONE 同做法：GameOptionsMenuPatch.cs:32）
                var mapPicker = menu.MapPicker;
                if (mapPicker != null && mapPicker.gameObject.activeSelf)
                {
                    mapPicker.gameObject.SetActive(false);
                    LightLogger.Log("[ConfigUIPanel] 已关闭克隆菜单的 MapPicker");
                }

                // ② 原版设置行：Children 里记的都是原版行，全部隐藏
                //    ★★ 2026-10-06 用户报「游戏设置里面的原版内容咋还被隐藏了」——
                //       根因：这里是**单向**的 `SetActive(false)` ✗，而 MOD 页签用的**就是同一个
                //       `GameOptionsMenu`**（本文件注释："自带原版的整套设置…我们只是在它上面又加了自己的行" ✓）
                //       → 切回"游戏设置"页签时原版行还是关着的 → 整页空白 ✓✓
                //    → 现在**记下我们关过谁、原来的 active 是什么**，离开我们的页签时**原样恢复** ✓
                //      （`_hiddenVanilla` + `Show()` 时清空、`ClearRowsOnly()` 时还原 ✓）
                var children = menu.Children;
                if (children != null)
                {
                    int hidden = 0;
                    for (int i = 0; i < children.Count; i++)
                    {
                        var ch = children[i];
                        if (ch == null) continue;
                        if (ch.gameObject.activeSelf)
                        {
                            RememberHidden(ch.gameObject);     // ★ 记下来，之后要还原 ✓
                            ch.gameObject.SetActive(false);
                            hidden++;
                        }
                    }
                    if (hidden > 0)
                        LightLogger.Log($"[ConfigUIPanel] 已隐藏 {hidden} 个原版设置行（离开页签时会还原 ✓）");
                }

                // ③ ★★ 关键修复：把 settingsContainer 里**所有不是我们的**子物体全部隐藏。
                //
                //  为什么只清 Children 不够（用户反馈"header 还是会叠"）：
                //    原版 GameOptionsMenu.CreateSettings（GameOptionsMenu.cs:22-26）
                //    创建分类头（"伪装者"/"任务"那些）时是这样的：
                //        Instantiate(categoryHeaderOrigin, ..., this.settingsContainer);
                //        categoryHeaderMasked.transform.localPosition = ...;
                //    它**只 Instantiate 进 settingsContainer，从不 Add 到 Children**；
                //    只有设置行才 Add 到 Children（:37/:46/:56/:65）。
                //    所以 Children 里根本没有分类头 → 只清 Children 永远清不掉它们。
                //
                //  按名字前缀"LightConfig"排除我们自己的行/分类头（AddConfigRow/AddCategoryHeader
                //  都以此开头），其余一律隐藏 —— 这样无论是分类头、地图预览还是将来
                //  原版新增的任何东西，都不会再叠在我们的内容上。
                int foreign = HideForeignChildren(TryGetScrollerInner(menu));
                foreign += HideForeignChildren(menu.settingsContainer);
                if (foreign > 0)
                    LightLogger.Log($"[ConfigUIPanel] 已隐藏 {foreign} 个非本模组的原版子物体（含分类头）");

                // ④ ⚠️ 原版背景板/遮罩：克隆体自带原版整个视觉外壳
                HideByName(menu.transform, "Background");
                HideByName(menu.transform, "BG_Gradient");
                HideByName(menu.transform, "LabelBackground");

                // ⑤ 原版"什么情况？"说明区 + 返回按钮也属于原版外壳
                var back = menu.BackButton;
                if (back != null && back.gameObject.activeSelf)
                {
                    back.gameObject.SetActive(false);
                    LightLogger.Log("[ConfigUIPanel] 已关闭克隆菜单的 BackButton");
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.ClearVanillaContent] {ex.Message}");
            }
        }

        /// <summary>我们的行/分类头统一用 "LightConfig" 前缀命名（见 AddConfigRow / AddCategoryHeader）。</summary>
        internal const string OurPrefix = "LightConfig";

        /// <summary>
        /// 隐藏容器里所有**不是我们创建的**子物体（原版分类头、原版设置行、地图预览等），
        /// 保留我们自己以 <see cref="OurPrefix"/> 开头命名的行与分类头。
        /// </summary>
        private static int HideForeignChildren(Transform? container)
        {
            if (container == null) return 0;

            int hidden = 0;
            try
            {
                // 倒序遍历：隐藏不改变顺序，但倒序是改层级时的安全习惯
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i);
                    if (child == null) continue;
                    if (child.name.StartsWith(OurPrefix, StringComparison.Ordinal)) continue;   // 我们自己的
                    if (!child.gameObject.activeSelf) continue;

                    child.gameObject.SetActive(false);
                    hidden++;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.HideForeignChildren] {ex.Message}");
            }
            return hidden;
        }

        /// <summary>
        /// 供 GameSettingMenuPatch 在**每次切页签**时调用（不依赖面板是否重建）。
        /// 克隆菜单每次 SetActive(true) 都会走 OnEnable→Initialize，原版内容可能回来，
        /// 所以清理必须挂在切页动作上，而不只是挂在 Build 上。
        /// </summary>
        internal static void CleanMenu(GameOptionsMenu? menu)
        {
            if (menu == null) return;
            ClearVanillaContent(menu);
        }

        /// <summary>按名字（含直接子级递归一层）隐藏物体。</summary>
        private static void HideByName(Transform root, string name)
        {
            try
            {
                var t = root.Find(name);
                if (t != null && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    LightLogger.Log($"[ConfigUIPanel] 已隐藏原版外壳 '{name}'");
                }
            }
            catch { }
        }

        /// <summary>
        /// 行内布局调整 —— 照抄 TONE 的 <c>OptionBehaviourSetSizeAndPosition</c>
        /// （TONE\Patches\GameOptionsMenuPatch.cs:217-260）。
        ///
        /// 为什么要做：原版这些行是给**原版那种窄列**设计的，
        /// 直接放到我们更宽的一页里会「挤在左边一小块」。
        /// TONE 的做法是：
        ///   · LabelBackground 拉宽（localScale.x +1、y −0.2）并左移 −0.6
        ///   · Title Text 左移 −0.7，sizeDelta 设成 (5.7, 0.37)，左对齐 + 加粗 + 描边
        ///   · Checkbox 的 Toggle 挪到 (1.46, −0.042)
        ///   · 数值行的 +/− 与数值框各自右移
        ///
        /// ⚠️ 这些增量是 TONE 的实测值，**照抄**即可；不要再自己推算
        ///    （之前"猜部件名 + 硬编码尺寸"就是因为没照抄才做坏的）。
        /// </summary>
        private static void ApplyRowLayout(OptionBehaviour row, ConfigItem item,
                                           ToggleOption? realToggle = null,
                                           NumberOption? realNumber = null,
                                           StringOption? realString = null)
        {
            try
            {
                var t = row.transform;

                // 诊断：确认真实组件类型（一次性）。
                if (!_layoutLogged.Contains(item.Key))
                {
                    _layoutLogged.Add(item.Key);
                    LightLogger.Log($"[ConfigUIPanel.Diag] ApplyRowLayout 进入 {item.Key} " +
                                    $"rowType={row.GetType().Name} " +
                                    $"toggle={(realToggle != null)} number={(realNumber != null)} " +
                                    $"string={(realString != null)}");
                }

                // ① 灰色标签底：**只把高度收一点点**，让相邻两行之间留出一条缝。
                //
                // 用户反馈："原版布局旁边那个灰色条在一起不好看，给每个配置项之间稍微加一点点距离，
                //            上一轮就是那样"。
                // 上一轮之所以有缝，是因为 TONE 的那句 `localScale += (1, -0.2, 0)` 把标签底
                // 压矮了 0.2；我上一轮为了修"减号穿模"把整句删掉，于是标签底恢复原高、
                // 两行的灰条就贴在一起了。
                // 现在只保留**垂直**方向的收缩（-0.2），水平方向与位置一律不动 ——
                // 这样既有缝，又不会像之前那样加宽后盖住左侧减号。
                var labelBg = row.LabelBackground != null
                    ? row.LabelBackground.transform
                    : t.Find("LabelBackground");
                if (labelBg != null)
                    labelBg.localScale += new Vector3(0f, -0.2f, 0f);

                // ② 字体与文字：只改这些，绝不改位置/尺寸。
                //
                // 用户反馈过两次布局问题："数字那个框是歪的，加减号也是"、
                // "加减号位置还是错的，减号跑到左边穿模了"。
                // 根因都是之前照抄 TONE 的 OptionBehaviourSetSizeAndPosition：
                //     LabelBackground 水平 +1 且左移 -0.6
                //     Title 左移 -0.7 且 sizeDelta = (5.7, 0.37)
                //     PlusButton +1.7 / MinusButton +0.9 / ValueBox +1.3
                //   —— 全是 TONE 为**它自己**的行宽校准的数值。
                //      我们的行就是原版预制体、原版缩放、原版坐标，套上去就歪/穿模。
                // ② 复选框：**修复 CheckMark 为 null 的问题**
                //
                // 实测（日志）：克隆出来的 ToggleOption 上
                //     Bind 后 lid.debug.enabled itemBool=False CheckMark=null
                // CheckMark 字段是 **null** —— 于是：
                //   · ConfigRowDriver.RefreshVisual 的 `if (CheckMark != null)` 静默跳过
                //     → 我们永远写不进初始状态；
                //   · 界面上看到的那个"勾"来自**预制体自带的精灵**，与 CheckMark 无关，
                //     所以它恒亮 → 表现为"配置是关的，但框里一直有勾"；
                //   · 原版 Toggle() 读写的也是这个 null 字段 → 状态彻底对不上，
                //     就是用户说的"要点两次才能变成未选中"。
                //
                // 修法：CheckMark 为空时，在行的子物体里找出真正的勾选渲染器补上去。
                // 判定顺序（从最可能到兜底）：
                //   1) 名字含 "CheckMark" / "Check" 的 SpriteRenderer
                //   2) 名为 "CheckBox" 的物体下第一个 SpriteRenderer
                //   3) 不补 —— 宁可不动，也不要乱抓一个渲染器当勾
                if (realToggle != null && realToggle.CheckMark == null)
                {
                    var found = FindCheckMarkRenderer(t);
                    if (found != null)
                    {
                        realToggle.CheckMark = found;
                        LightLogger.Log($"[ConfigUIPanel] 已为行 {item.Key} 补上 CheckMark：" +
                                        $"{found.name}@{PathOf(found.transform)}");
                    }
                    else
                    {
                        LightLogger.LogWarning($"[ConfigUIPanel] 行 {item.Key} 找不到勾选渲染器" +
                                               $"（CheckMark 仍为 null，勾选状态将无法显示）");
                    }
                }

                // 标题文本：用**真实组件**的强类型字段（row 的静态类型是基类，用 is 判断会全 false）
                TextMeshPro? tmp = realToggle?.TitleText
                                   ?? realNumber?.TitleText
                                   ?? realString?.TitleText;
                if (tmp == null)
                {
                    var title = t.Find("Title Text");
                    if (title != null) tmp = title.GetComponent<TextMeshPro>();
                }

                if (tmp != null)
                {
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.outlineWidth = 0.17f;

                    var menuFont = MenuTextTemplate.MenuFont;   // 辉光白那套字体
                    if (menuFont != null) tmp.font = menuFont;
                    tmp.color = MenuTextTemplate.GlowWhite;
                    tmp.text = item.DisplayName ?? item.Key;
                    tmp.ForceMeshUpdate();
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.ApplyRowLayout] {ex.Message}");
            }
        }

        /// <summary>打印一个 Transform 的层级路径（诊断用）。</summary>
        private static string PathOf(Transform t)
        {
            if (t == null) return "null";
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 在行内找出真正的"勾选"渲染器，用来修补 ToggleOption.CheckMark == null。
        ///
        /// 判定顺序：
        ///   1) 名字含 "CheckMark" 的 SpriteRenderer（原版命名）
        ///   2) 名为 "CheckBox" 的物体下的第一个 SpriteRenderer
        ///   3) 返回 null（宁可不动，也不乱抓）
        ///
        /// ⚠️ 只找 SpriteRenderer：复选框的勾是精灵，文字是 TMP，别混。
        /// </summary>
        private static SpriteRenderer? FindCheckMarkRenderer(Transform row)
        {
            // ① 名字含 CheckMark / Check 的
            var all = row.GetComponentsInChildren<SpriteRenderer>(true);
            if (all != null)
            {
                foreach (var sr in all)
                {
                    if (sr == null) continue;
                    string n = sr.name;
                    if (n.IndexOf("CheckMark", StringComparison.OrdinalIgnoreCase) >= 0)
                        return sr;
                }
                // ② CheckBox 物体下的第一个
                foreach (var sr in all)
                {
                    if (sr == null) continue;
                    var chain = PathOf(sr.transform);
                    if (chain.IndexOf("CheckBox", StringComparison.OrdinalIgnoreCase) >= 0)
                        return sr;
                }
            }
            return null;
        }

        // 【已删除】ShiftValueBox / Shift 两个辅助方法。        // 它们把数值框 +1.3、加号 +1.7、减号 +0.9 硬挪，是"数字框/加减号是歪的"的根因；
        // 现在一律保留原版位置，故不再需要。
        // 说明：TONE 的 OptionBehaviourSetSizeAndPosition 里有这些位移，但那是为
        // TONE 自己的行宽校准的，直接套到原版行上就会歪 —— 不要照抄坐标。

        /// <summary>
        /// 一次性诊断：打印标题/数值 TMP 的实例 ID 与所在物体名。
        /// 用途：验证"闪字"是否因为两者指向同一个 TMP（原版写值 → 标题被顶掉）。
        /// 只打一次（_loggedRows），避免刷屏。
        /// </summary>
        private static readonly HashSet<string> _loggedRows = new();

        private static void LogRowTextOnce(OptionBehaviour row, ConfigItem item)
        {
            try
            {
                if (!_loggedRows.Add(item.Key)) return;

                string titleInfo = "无", valueInfo = "无", same = "n/a";

                // ⚠️ 用 GetComponent 而不是 is —— 静态类型是基类，is 判断会全 false
                var t = row.GetComponent<ToggleOption>()?.TitleText
                        ?? row.GetComponent<NumberOption>()?.TitleText
                        ?? row.GetComponent<StringOption>()?.TitleText;

                TextMeshPro? v = row.GetComponent<NumberOption>()?.ValueText
                                 ?? row.GetComponent<StringOption>()?.ValueText;

                if (t != null) titleInfo = $"{t.name}#{t.GetInstanceID()} text='{t.text}'";
                if (v != null)
                {
                    valueInfo = $"{v.name}#{v.GetInstanceID()} text='{v.text}'";
                    same = (t != null && t.GetInstanceID() == v.GetInstanceID()) ? "★同一个TMP！" : "不同对象";
                }

                LightLogger.Log($"[ConfigUIPanel.Diag] 行 {item.Key} 标题TMP={titleInfo} | " +
                                $"数值TMP={valueInfo} | {same}");
            }
            catch { }
        }

        /// <summary>驱动器挂不上时的兜底：至少把标题写对。</summary>
        private static void FixTitleFallback(OptionBehaviour row, ConfigItem item)
        {
            try
            {
                TextMeshPro? title =
                    row.GetComponent<ToggleOption>()?.TitleText
                    ?? row.GetComponent<NumberOption>()?.TitleText
                    ?? row.GetComponent<StringOption>()?.TitleText;

                if (title == null) return;

                var tr = title.GetComponent<TextTranslatorTMP>();
                if (tr != null) tr.enabled = false;
                title.text = item.DisplayName ?? item.Key;
                if (item.NameColor.HasValue) title.color = item.NameColor.Value;
            }
            catch { }
        }

        /// <summary>建一行配置项（克隆原版控件 + 合成 setting），返回下一个 y。</summary>
        /// <summary>
        /// 禁掉该物体及最近 3 层祖先上的 <see cref="AspectPosition"/>。
        ///
        /// ⚠️⚠️ **原版设置行必须禁掉它**（2026-10-06 修"独立窗口里内容不跟着窗口走"）。
        ///
        ///  原版行是 <c>OptionBehaviour</c> 克隆体，**自带 <c>AspectPosition</c>** ——
        ///  它每帧按**屏幕比例**重算**世界坐标**（`updateAlways`），所以我们把它挂到哪个
        ///  父物体下都没用：它会被拽回"原版菜单里的那个屏幕位置"。
        ///
        ///  症状极具辨识度：**窗口框动了、里面的行纹丝不动** ——
        ///  因为窗口是我们 `new` 的物体（跟着父物体走），行是原版克隆体（被 AspectPosition 拽住）。
        ///
        ///  为什么在原版容器里看不出问题：那时 AspectPosition 算出来的**就是**我们想要的位置
        ///  （行本来就该在菜单里那个地方），所以一直没暴露。
        ///
        ///  我们的行位置全是自己算的（见 <c>ApplyRowLayout</c> / <c>Relayout</c>），
        ///  所以**两种场合都该禁**。
        ///  只往上查 3 层：再往上就是整个菜单的锚点，动它会破坏菜单自身的自适应。
        ///  （同 <c>SettingsTabPatch.DisableAspectPosition</c>）
        /// </summary>
        private static void DisableAspectPositionOn(Transform? t)
        {
            // ⚠️ **只在"行铺进我们自己的容器"时才禁**。
            //
            //  就地嵌入模式（默认）下，行还铺在**原版菜单的滚动容器**里 ——
            //  那时 AspectPosition 算出来的**正好就是**我们想要的位置（我们那套
            //  RowX / StartY / SpacingY 常量本来就是照原版 GameOptionsMenu.CreateSettings 抄的），
            //  禁掉反而等于改了既有行为、有搞坏滚动/布局的风险。
            //
            //  只有独立窗口模式（_forceOwnContainer）才必须禁 —— 否则行会被拽回原版位置，
            //  表现是"窗口框动了、里面的行纹丝不动"。
            if (!_forceOwnContainer) return;

            try
            {
                var cur = t;
                for (int depth = 0; cur != null && depth < 3; depth++, cur = cur.parent)
                {
                    var ap = cur.GetComponent<AspectPosition>();
                    if (ap == null || !ap.enabled) continue;
                    ap.enabled = false;
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.DisableAspectPositionOn] {ex.Message}");
            }
        }

        private static float AddConfigRow(ConfigItem item, float y)
        {
            try
            {
                if (_templates == null)
                {
                    LightLogger.LogWarning($"[ConfigUIPanel] 模板源未就绪，跳过 {item.Key}");
                    return y;
                }

                // 按类型挑模板
                OptionBehaviour? origin;
                switch (item.Type)
                {
                    case ConfigType.Bool:      origin = _templates.checkboxOrigin;     break;
                    case ConfigType.Value:
                    case ConfigType.Filter:    origin = _templates.stringOptionOrigin; break;
                    default:                   origin = _templates.numberOptionOrigin; break;
                }

                if (origin == null)
                {
                    LightLogger.LogWarning($"[ConfigUIPanel] 预制体 {item.Type} 为空，跳过 {item.Key}");
                    return y;
                }

                var clone = Object.Instantiate(origin, Vector3.zero, Quaternion.identity, _container);
                clone.gameObject.name = $"LightConfigRow_{item.Key}";
                _instantiated++;   // 诊断：本次会话一共实例化过多少行

                // ⚠️⚠️ **原版行自带 AspectPosition，必须禁掉**（2026-10-06 修"窗口里内容不跟着走"）。
                //    它每帧按**屏幕比例**重算**世界坐标**（updateAlways），所以我们把行挂到哪个
                //    父物体下都没用 —— 它会被拽回"原版菜单里的那个屏幕位置"。
                //    症状极具辨识度：**窗口框动了、里面的行纹丝不动**
                //    （窗口是我们 new 的物体跟着父物体走，行是原版克隆体被 AspectPosition 拽住）。
                //    在原版容器里看不出问题，是因为那时它算出来的**正好**是想要的位置。
                DisableAspectPositionOn(clone.transform);

                clone.transform.localPosition = new Vector3(CurRowX, y, RowZ);

                // ⚠️⚠️ 实测（日志）：
                //     ApplyRowLayout 进入 lid.debug.enabled rowType=OptionBehaviour isToggle=False
                // Instantiate 的静态返回类型是 **OptionBehaviour（基类）**，
                // 于是后面所有 `row is ToggleOption` / `is NumberOption` / `is StringOption`
                // **全部为 false**，一连串代码静默走空：
                //   · ApplyRowLayout 里"补 CheckMark"分支不执行 → CheckMark 恒为 null；
                //   · ConfigRowDriver.GetTitleText() 退回 GetComponentInChildren 兜底；
                //   · RefreshVisual 走 else 分支去写 GetValueText()，而基类没有 ValueText
                //     → 返回 null → **什么都不写** → 数值框里残留的正是原版写的标题文字。
                //     ← 这就是用户反复报告的"数值框里显示的是标题"！
                //
                // 修法：不要信 Instantiate 的静态类型，用 **GetComponent** 取真实组件，
                // 运行时类型才是对的。下面统一换成 real* 三个变量。
                var realToggle = clone.GetComponent<ToggleOption>();
                var realNumber = clone.GetComponent<NumberOption>();
                var realString = clone.GetComponent<StringOption>();

                if (!_typeLogged.Contains(item.Key))
                {
                    _typeLogged.Add(item.Key);
                    LightLogger.Log($"[ConfigUIPanel.Diag] 行 {item.Key} 真实组件：" +
                                    $"ToggleOption={(realToggle != null)} " +
                                    $"NumberOption={(realNumber != null)} " +
                                    $"StringOption={(realString != null)} " +
                                    $"（Instantiate 静态类型={clone.GetType().Name}）");
                }

                // ⚠️ 绝对不要把 localScale 设成 one！
                // 原版行预制体（GameOption_Number(Clone) 等）自身是 0.60 的缩放，
                // 强制设成 1 会让行**放大 1.67 倍** → 用户看到的"设置项太大了"。
                // TONE 从头到尾都没碰过行的 localScale（GameOptionsMenuPatch.cs 里只改部件）。
                // 这里保留预制体自带缩放，只在它异常（0 或 1.0 明显不对）时才不动它。
                // （不写任何 localScale 赋值 = 保留 Instantiate 出来的原值。）

                clone.gameObject.SetActive(true);
                _spawned.Add(clone.gameObject);

                // 合成 setting 并交给原版初始化（这一步让原版部件全部就位）
                var setting = BuildSetting(item);
                if (setting != null)
                {
                    clone.SetClickMask(_templates.ButtonClickMask);
                    clone.SetUpFromData(setting, MaskLayer);
                }
                else
                {
                    LightLogger.LogWarning($"[ConfigUIPanel] {item.Key} 未能合成 BaseGameSetting");
                }

                // 登记：只用于我们自己的显示刷新与悬浮，不再靠它拦截原版逻辑
                RowMap[clone.GetInstanceID()] = item;

                // ⚠️ NRE 根因：原版 ToggleOption.Toggle() 最后一行是
                //      this.OnValueChanged(this);
                //    克隆体的 OnValueChanged 是 **null** → 点一下就 NRE。
                //    TONE 在 GameOptionsMenuPatch.cs:179 显式赋值：
                //      optionBehaviour.OnValueChanged = new Action<OptionBehaviour>(__instance.ValueChanged);
                //    我们不需要原版的联动，但**必须给它一个非 null 的委托**。
                clone.OnValueChanged = new Action<OptionBehaviour>(_ => { });

                // 清掉克隆自带的原版点击逻辑（AGENTS.md §4.5：必须整体替换）
                var pb = clone.GetComponent<PassiveButton>();
                if (pb != null) pb.OnClick = new Button.ButtonClickedEvent();

                // 行内布局照抄 TONE 的 OptionBehaviourSetSizeAndPosition：
                // 拉宽标签底、标题左对齐加粗、右侧控件右移，并统一换成辉光白字体
                ApplyRowLayout(clone, item, realToggle, realNumber, realString);

                // 驱动器：修正标题文字（原版会按 setting.Title 写）
                var driver = AddComponentSafe<ConfigRowDriver>(clone.gameObject);
                if (driver != null)
                {
                    driver.Bind(item, clone);
                    _drivers[item] = driver;
                    driver.RefreshVisual();

                    // 诊断：驱动器写完之后的即时状态。
                    // 若这里是 False 而之后变成 True，说明有别的写入者（原版 Start/Initialize
                    // 或预制体自身）在我们之后覆盖了 CheckMark。
                    if (item.Type == ConfigType.Bool && _bindLogs < 10)
                    {
                        _bindLogs++;
                        LightLogger.Log($"[ConfigUIPanel.Diag] Bind 后 {item.Key} " +
                                        $"itemBool={item.GetBool()} " +
                                        $"CheckMark={(realToggle?.CheckMark == null ? "null" : realToggle.CheckMark.enabled.ToString())} " +
                                        $"go.active={clone.gameObject.activeInHierarchy}");
                    }
                }
                else
                {
                    // 驱动器失败也要保证标题正确，否则会显示原版的 "Confirm Ejects?" 之类
                    FixTitleFallback(clone, item);
                }

                // 一次性诊断：确认"标题 TMP"与"数值 TMP"是否是同一个对象。
                // 若是同一个，原版往数值框写值就会把标题顶掉（表现为"闪字"）——
                // 这是我对用户报告的一个假设，先用日志证实/证伪再继续改。
                LogRowTextOnce(clone, item);

                WireHover(clone.gameObject, item);

                // ★ 用户建议的兜底：在行上盖一个**透明触发层**，用它的悬停驱动说明文字。
                //   原版行自己的 PassiveButton 悬停没能触发（用户："还是没显示"），
                //   与其继续猜它为什么不动，不如自己放一个完全可控的。
                //   ⚠️ 给它一个**空的 OnClick**，这样它只负责"悬停"，
                //      点击仍然由行自己的 +/- 按钮处理（PassiveButtonManager 不看 z，两个都会收到）。
                AddHoverTrigger(clone, item);

                _rowCount++;
                return y - SpacingY;
            }
            catch (Exception ex)
            {
                LightLogger.LogError($"[ConfigUIPanel.AddConfigRow] {item.Key}", ex);
                return y;
            }
        }

        /// <summary>
        /// 悬浮显示详情（走本工程已有的 DetailPopup）。
        /// 原版行自带 BoxCollider2D + PassiveButton，所以这里只追加监听，不新增碰撞区。
        /// </summary>
        // =====================================================================
        //  原版左上角那块描述文字（GameSettingMenu.MenuDescriptionText）
        //
        //  用户 2026-10-06："看我截图画的地方，那里的文字能改吗？如果可以，那么这么做：
        //  鼠标放到某个配置项上时，显示一段文字，这个文字定义时写好，是可选形参，
        //  如果没有那就让原版显示。"
        //
        //  · "那块文字" = GameSettingMenu 的 MenuDescriptionText（原版 private 字段，
        //    interop 里是 public 属性，能直接读写）。
        //  · "可选形参" = ConfigItem.AddConfiguration(..., detail:) —— **已经有了**，不用新增。
        //  · TONE 的用法见参考源码 GameOptionsMenuPatch.cs:710。
        // =====================================================================

        /// <summary>我们最后写进去的文字（null = 当前不是我们在显示）。</summary>
        private static string? _ourDesc;

        /// <summary>被我们顶掉的原版文字，移出时还回去。</summary>
        private static string? _vanillaDesc;

        private static TextMeshPro? DescText
        {
            get
            {
                try
                {
                    var menu = GameSettingMenu.Instance;
                    return menu != null ? menu.MenuDescriptionText : null;
                }
                catch { return null; }
            }
        }

        /// <summary>鼠标悬停某行：把该行的 detail 写到原版描述文字里。<paramref name="text"/> 空则**什么都不做**。</summary>
        private static void ShowRowDescription(string? text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;    // 没写 detail → 让原版显示

                var tmp = DescText;
                if (tmp == null)
                {
                    // 一次性诊断：区分"菜单拿不到"还是"字段读不到"
                    // （用户反馈"显示文字并没出现"，没有这条就只能猜）
                    LightLogger.LogWarning("[ConfigUIPanel.ShowRowDescription] 拿不到 MenuDescriptionText" +
                                           $"（GameSettingMenu.Instance={(GameSettingMenu.Instance != null ? "有" : "null")}）");
                    return;
                }

                // 先备份"当前显示的不是我们写的"那段 —— 那才是原版文字。
                // （不能只备份一次：切页签时原版会换文字。）
                var cur = tmp.text ?? "";
                if (_ourDesc == null || cur != _ourDesc) _vanillaDesc = cur;

                _ourDesc = text;
                tmp.SetText(text);
                tmp.fontStyle = FontStyles.Bold;               // §12.1
                tmp.ForceMeshUpdate();
            }
            catch (Exception ex)
            {
                LightLogger.LogDebug($"[ConfigUIPanel.ShowRowDescription] {ex.Message}");
            }
        }

        /// <summary>鼠标移出：把原版文字还回去。</summary>
        private static void RestoreRowDescription()
        {
            try
            {
                if (_ourDesc == null) return;      // 没顶过，别乱动
                var tmp = DescText;
                if (tmp != null && _vanillaDesc != null) tmp.SetText(_vanillaDesc);
                _ourDesc = null;
            }
            catch (Exception ex)
            {
                LightLogger.LogDebug($"[ConfigUIPanel.RestoreRowDescription] {ex.Message}");
            }
        }
        /// <summary>
        /// 在配置行上盖一层**透明悬停触发器**（用户 2026-10-06 的建议："在上面放一个隐藏的按钮，
        /// 咱们用那个触发"）。
        ///
        /// 为什么需要它：原版行自己的 <c>PassiveButton.OnMouseOver</c> 挂了监听但没触发
        /// （用户："还是没显示"）。与其继续猜原版那边为什么不动，不如自己放一个完全可控的。
        ///
        /// ⚠️ 给它**空的 OnClick** —— 它只负责"悬停"，点击仍旧由行自己的 −/＋ 按钮处理。
        ///    `PassiveButtonManager` **完全不看 z**（AGENTS.md §4.3），所以两层都会收到事件；
        ///    我们这层什么都不做，就不会抢走点击。
        ///
        /// ⚠️ 尺寸取行的碰撞盒；拿不到就用一个够大的兜底值。
        /// </summary>
        private static void AddHoverTrigger(OptionBehaviour row, ConfigItem item)
        {
            try
            {
                if (row == null || item == null) return;
                if (string.IsNullOrWhiteSpace(item.Detail)) return;   // 没写 detail → 不用触发器

                var go = new GameObject("LightRowHover");
                go.layer = LayerExpansion.GetUILayer();
                go.transform.SetParent(row.transform, false);
                go.transform.localScale = Vector3.one;

                // ★★ **排在行内元素的后面（z 更大 = 更深）** —— 用户 2026-10-06 的方案：
                //    "你这个会把减号挡住按不了。你给他层级往下排点还能检测到吗，如果能那就这么干。"
                //
                //    ✅ 能，原版源码（PassiveButtonManager.cs）给了确切机制：
                //      · Update() 先按 CachedZ **升序排序**（L49-56），即"由前到后"；
                //      · HandleMouseOver(L245) 只跟 currentOver 比：
                //          `if (button.z > currentOver.z) return;`  ← 更深的一律跳过；
                //      · `currentOver` 是**单个字段** → 同一时刻只有一个按钮被悬停。
                //    所以排到后面之后：
                //      · 鼠标压在 −/＋/数值框上 → 那些更靠前，它们赢，我们的层不触发（不再挡操作）
                //      · 鼠标在行内其它地方      → 只有我们在鼠标下 → 我们赢 → 显示说明
                //
                //    点击也不受影响：L58 那个循环对**每个**碰撞盒重叠的按钮都调 CheckDrag，
                //    并不是"只给最靠前那个"。
                //
                //    z 取 +0.6：行根自身在 RowZ，行内控件一般在其附近；排在它们后面即可。
                go.transform.localPosition = new Vector3(0f, 0f, 0.6f);

                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                var src = row.GetComponent<BoxCollider2D>();
                col.size = (src != null && src.size.x > 0.1f) ? src.size : new Vector2(4.6f, 0.62f);

                var pb = go.AddComponent<PassiveButton>();
                pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();   // 空的：不抢点击
                pb.OnMouseOver = new UnityEngine.Events.UnityEvent();
                pb.OnMouseOut = new UnityEngine.Events.UnityEvent();

                var detail = item.Detail;
                pb.OnMouseOver.AddListener((UnityAction)(() => ShowRowDescription(detail)));
                pb.OnMouseOut.AddListener((UnityAction)(() => RestoreRowDescription()));

                // 诊断：把涉及到的 z 全打出来（谁在谁前面一目了然，下轮不用再猜）
                string zInfo;
                try
                {
                    zInfo = $"行根z={row.transform.position.z:F2} 本层z={go.transform.position.z:F2}";
                    foreach (var t in row.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "MinusButton") zInfo += $" 减号z={t.position.z:F2}";
                        else if (t.name == "PlusButton") zInfo += $" 加号z={t.position.z:F2}";
                    }
                }
                catch { zInfo = "(z 读取失败)"; }

                LightLogger.Log($"[ConfigUIPanel] 已为 {item.Key} 建悬停触发器（size={col.size}，detail 长度={detail?.Length ?? 0}）| {zInfo}");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.AddHoverTrigger] {ex.Message}");
            }
        }

        private static void WireHover(GameObject row, ConfigItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Detail)) return;

            try
            {
                var pb = row.GetComponent<PassiveButton>();
                if (pb == null) return;

                pb.OnMouseOver ??= new Button.ButtonClickedEvent();
                pb.OnMouseOut ??= new Button.ButtonClickedEvent();

                var text = item.Detail;

                // ⚠️ 2026-10-06 改：悬停时把说明写进**原版左上角那块描述文字**
                //    （用户设计图上圈的就是它；TONE 也是这么做的 ——
                //     `GameSettingMenu.Instance.MenuDescriptionText.SetText(info)`，
                //      见 TONE 的 GameOptionsMenuPatch.cs:710）。
                //    Detail 是可选形参（ConfigItem.AddConfiguration 的 detail:），
                //    **没写就不碰那块文字**（原版自己显示什么就显示什么）——
                //    正是用户要的"如果没有那就让原版显示"。
                pb.OnMouseOver.AddListener((UnityAction)(() => ShowRowDescription(text)));
                pb.OnMouseOut.AddListener((UnityAction)(() => RestoreRowDescription()));
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.WireHover] {ex.Message}");
            }
        }

        // =====================================================================
        //  刷新
        // =====================================================================

        /// <summary>
        /// 【Nebula 风格】每次值变化都重新查一遍可见性。
        ///
        /// ⚠️ 重要：**只有真正需要时**才重建，并且判定必须用"可见集合"而不是逐项配对。
        ///
        /// 实测（日志）：每次点击复选框都会触发一次整页重建
        /// （`会话内共实例化` 从 1 一路涨到 9，实例 ID 每次点击都变），
        /// 表现就是用户说的"还是要点两下" —— 点一下页面就被重建，
        /// 看到的勾选态来自"值还没生效时建出来的新行"。
        ///
        /// 原因：判定写成 `item.IsVisible != _drivers.ContainsKey(item)`，
        /// 而 `IsVisible` 里可能带 lambda（`() => Enabled.GetBool()`），
        /// 它读的是 **ConfigItem 的值**；点击时 CheckMark 已翻转，但 ConfigItem 的值
        /// 要等 AfterChange 才写回 → 这一刻两者必然"不一致" → 每次点击都重建。
        ///
        /// 修法：先把"应当可见的项集合"和"已建出来的项集合"都收集完再比较，
        /// 并且**先同步值再判定**（见 AfterChange 的调用顺序）。
        /// </summary>
        public static void Refresh()
        {
            try
            {
                // ★★ 2026-10-10 用户报「职业页签外泄游戏设置」「调数量外泄模组设置」——
                //    真因：我们的 `ChangeTabPrefix` 吃掉了原版 `ChangeTab`（`return false` ✓），
                //    虽然接管代码里做过一次互斥显隐 ✓，但**原版之后又会把某个页签点亮** ✗
                //    （工程注释已记："原版 OpenMenu/OpenChancesTab 会把 RoleChancesSettings 重新 SetActive(true)" ✓）
                //    → 每帧压一次（状态差量 ✓，没漏时一个字节都不写 ✓）
                Light.Patches.GameSettingMenuPatch.TickTabGuard();

                // ★★ 2026-10-10：泄漏取证（一次性，最多 3 次 ✓）——
                //    把"当前 MOD 页签上**还激活**的原版选项行"是谁打印出来 ✓
                //    （连改三轮都靠猜 ✗ → 按工程规矩：先把事实打出来 ✓）
                DumpLeakingOptionObjectsOnce();

                // ★★ 2026-10-10 用户报「原版"游戏设置"的行**漏进我们的页**」——
                //    方向我说反了 ✗：不是我们的浮窗跑到原版页，而是**原版的行回到了我们的页** ✓
                //    根因工程注释里早就写着（见 `CleanMenu` 那段的 L1564-1567）：
                //      "克隆菜单每次 `SetActive(true)` 都会走 `OnEnable→Initialize`，**原版内容可能回来**" ✗
                //    → 只隐藏一次不够 ✓，在我们的**每帧刷新**里补一道"再隐藏" ✓
                //      （状态差量：没漏出来时**一个字节都不写** ✓，开销可忽略）
                RehideVanillaIfLeaked();

                // 诊断：把 Refresh 的输入和判定结果打出来。
                // 之前几轮都是"改了但不知道有没有跑到"，这次把每次 Refresh 都记下来。
                if (_refreshLogs < 20)
                {
                    _refreshLogs++;
                    var en = ConfigRegistry.Get(EnabledKey);
                    var dc = ConfigRegistry.Get(DebugKey);
                    LightLogger.Log($"[ConfigUIPanel.Diag] Refresh 被调用 | " +
                                    $"enabled 值={(en == null ? "null" : en.GetBool().ToString())} | " +
                                    $"dummyCount.VisibleWhen={(dc?.VisibleWhen == null ? "NULL_LAMBDA" : "有")} " +
                                    $"IsVisible={dc?.IsVisible ?? false} | built=[{string.Join(",", _builtKeys)}]");
                }

                if (NeedRebuild())
                {
                    // ★★ 2026-10-10 **防抖**（用户报「还是不能滚动 / 还是会外泄」的真因 ✓）：
                    //    日志实证：`lid.debug.dummyCount` 的可见性**每帧抖一次** ✗ →
                    //      `增量更新可见性：+1 行 / -0 行` ↔ `+0 行 / -1 行` 每帧交替 ✓
                    //    后果有两个，而且是同一个原因：
                    //      ① 内容高度每帧在 11.42 ↔ 10.97 之间跳 → 滚动位置被反复归零 → **滚不动** ✗
                    //      ② 那一行被反复删了又建 → 位置错乱 → **看起来像"外泄"** ✗
                    //    → 这里按"**可见行集合的签名**"防抖：签名没变就**不做任何结构改动** ✓
                    //      （值的变化照旧走下面的 RefreshVisual ✓，不受影响）
                    string sig = VisibilitySignature();
                    if (sig == _lastVisibilitySig)
                    {
                        foreach (var kv in _drivers) kv.Value.RefreshVisual();
                        return;
                    }
                    _lastVisibilitySig = sig;

                    // ⚠️【增量优先】依赖项(如"启用调试模式")改变可见性时，
                    //   **不要**整页销毁重建 —— 实测每次切换都会 Rebuild 一次，
                    //   行实例计数 0→1→3→4→6→7… 无限增长。
                    //   整页重建会重新走 ClearVanillaContent / UpdateScrollBounds /
                    //   地图预览回收，正是"点一次配置项那块空缺又出现"和闪动的来源。
                    //
                    //   这里先尝试**只补/只删变化的那几行**；成功就返回，
                    //   失败（结构对不上）才退回整页重建。
                    if (TryIncrementalVisibility())
                        return;

                    var parent = _container != null ? _container.parent : null;
                    if (parent != null)
                    {
                        Rebuild(parent);
                        return;
                    }

                    // ★ 兜底：`_container.parent` 取不到时**不能什么都不做** ✗（原来就是这样静默走空 ✓）
                    //   用 `_page` 的父物体当退路 ✓
                    var alt = _page != null ? _page.transform.parent : null;
                    if (alt != null)
                    {
                        LightLogger.LogWarning("[ConfigUIPanel] _container.parent 为空 → 改用 _page 的父物体重建 ✓");
                        Rebuild(alt);
                        return;
                    }
                    LightLogger.LogWarning("[ConfigUIPanel] 找不到可用的父物体 → 本次不做结构改动（行可能滞留 ✗）");
                }

                foreach (var kv in _drivers) kv.Value.RefreshVisual();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Refresh]", ex);
            }
        }

        /// <summary>把某一行对应的驱动器刷新一次（值写回后立即同步显示）。</summary>
        internal static void RefreshRow(OptionBehaviour behaviour)
        {
            try
            {
                if (behaviour == null) return;
                var item = ItemOf(behaviour.GetInstanceID());
                if (item == null) return;

                // 诊断：确认这一行的值在 RefreshRow 时到底是什么
                if (_rowRefreshLogs < 12)
                {
                    _rowRefreshLogs++;
                    LightLogger.Log($"[ConfigUIPanel.Diag] RefreshRow 行 key={item.Key} " +
                                    $"type={item.Type} value={item.Value} bool={item.GetBool()} " +
                                    $"有驱动器={_drivers.ContainsKey(item)}");
                }

                if (_drivers.TryGetValue(item, out var drv) && drv != null)
                    drv.RefreshVisual();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.RefreshRow] {ex.Message}");
            }
        }

        private static int _rowRefreshLogs;
        private static int _bindLogs;
        private static readonly HashSet<string> _layoutLogged = new();
        private static readonly HashSet<string> _typeLogged = new();

        /// <summary>是否需要重建：比较"应当建出的行键集合"与"已建出的行键集合"。</summary>
        private static bool NeedRebuild()
        {
            bool changed = false;
            _wantKeys.Clear();

            if (_singleBlock != null)
            {
                CollectWantKeys(_singleBlock);
            }
            else
            {
                foreach (var block in ConfigRegistry.Blocks)
                {
                    if (block == null || !MatchesFilter(block)) continue;
                    CollectWantKeys(block);
                }
            }

            // 应当有的没建出来 → 重建
            string? reason = null;
            foreach (var key in _wantKeys)
                if (!_builtKeys.Contains(key)) { changed = true; reason = $"应建却没建：{key}"; break; }

            // 已建的已经不该有 → 重建
            if (!changed)
                foreach (var key in _builtKeys)
                    if (!_wantKeys.Contains(key)) { changed = true; reason = $"已建却不该建：{key}"; break; }

            // 诊断：只在确实要重建时打印原因（能直接指出是哪一项在反复触发）
            if (changed && _rebuildLogs < 12)
            {
                _rebuildLogs++;
                LightLogger.Log($"[ConfigUIPanel.Diag] NeedRebuild=true 原因：{reason} | " +
                                $"want=[{string.Join(",", _wantKeys)}] built=[{string.Join(",", _builtKeys)}]");
            }

            return changed;
        }

        private static int _rebuildLogs;
        private static int _refreshLogs;

        /// <summary>行布局取证（最多 8 次 ✓，用户报"配置项外泄"时用）</summary>
        /// <summary>
        /// **漏出来的原版行到底是谁** —— 一次性取证（最多 3 次 ✓）。
        ///
        /// ⚠️⚠️ 为什么要它（2026-10-10）：用户连报"职业页签漏游戏设置""调数量漏模组设置"，
        ///    我连着改了三轮"该关掉谁"**全都是猜** ✗ —— 日志证明：
        ///      · `TickTabGuard` 一条都没触发（说明不是页签 GameObject 还亮着 ✗）
        ///      · `又冒出来了` 也没触发（说明不在当前宿主菜单的 `Children` 里 ✗）
        ///    → 只能**把事实打出来** ✓：找出场上所有**还激活**的原版选项行，
        ///      打印它们的**类型 + 名字 + 父链 4 层** ✓ —— 一次就能看出该关谁 ✓
        /// ⚠️ 用 `Resources.FindObjectsOfTypeAll` 找（一次性的，最多 3 次 ✓，不影响帧率 ✓）
        /// </summary>
        internal static void DumpLeakingOptionObjectsOnce()
        {
            try
            {
                if (_leakDumpLogs >= 3) return;
                if (!ModTabActive) return;
                _leakDumpLogs++;

                var sb = new System.Text.StringBuilder(512);
                sb.Append("[ConfigUIPanel][泄漏取证] 当前 MOD 页签上**还激活**的原版选项行：");

                int found = 0;
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<OptionBehaviour>();
                if (all != null)
                {
                    foreach (var ob in all)
                    {
                        if (ob == null) continue;
                        var go = ob.gameObject;
                        if (go == null || !go.activeInHierarchy) continue;
                        if (go.name.StartsWith(OurPrefix, StringComparison.Ordinal)) continue;   // 我们自己的不算 ✓

                        found++;
                        if (found > 12) continue;   // 只打前 12 个，够定位了 ✓

                        sb.Append("\n   · ").Append(ob.GetType().Name).Append(" '").Append(go.name).Append("' 父链: ");
                        var tr = go.transform;
                        for (int d = 0; d < 4 && tr != null; d++)
                        {
                            sb.Append(tr.name).Append(d < 3 ? " ← " : "");
                            tr = tr.parent;
                        }
                    }
                }

                sb.Append($"\n   共 {found} 个（只列前 12 个 ✓）");
                LightLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                LightLogger.LogDebug($"[ConfigUIPanel.DumpLeakingOptionObjectsOnce] {ex.Message}");
            }
        }

        private static int _leakDumpLogs;

        /// <summary>行布局取证计数（最多 8 次 ✓）</summary>
        private static int _rowLayoutLogs;


        /// <summary>上次的内容高度（只在**真的变了**时才把滚动位置归零 ✓，见 UpdateScrollBounds）</summary>
        private static float _lastContentHeight = -1f;

        /// <summary>滚动取证日志计数（最多 3 条 ✓）</summary>
        private static int _scrollDiagLogs;

        /// <summary>
        /// **原版内容又冒出来了 → 再隐藏一次** ✓（用户 2026-10-10："原版的行漏进我们的页" ✓）。
        ///
        /// ⚠️ 为什么必须每帧查：
        ///   工程注释（`CleanMenu` 那段）早就写明 —— "克隆菜单每次 `SetActive(true)` 都会走
        ///   `OnEnable→Initialize`，**原版内容可能回来**" ✗
        ///   所以"进页时隐藏一次"是不够的 ✓：菜单随后初始化，会把原版行**重新显示** ✗
        ///   （这正是用户截图里"伪装者数 / 击杀冷却时间 / 伪装者视野 / 击杀范围"出现在我们页上的原因 ✓）
        ///
        /// ⚠️ 状态差量：**只在真的数到"漏出来的行"时才动手** ✓（否则每帧一个字节都不写 ✓）
        /// </summary>
        private static void RehideVanillaIfLeaked()
        {
            try
            {
                var menu = _hostMenu ?? _templates;
                if (menu == null) return;

                // ⚠️⚠️ 2026-10-10 用户报「原版行还是漏进来」——我上一版这里判断的是
                //    `ConfigUIPanel._page` ✗，而**角色页签用的是 `RoleListPage._page`** ✗✗
                //    → 在角色页签上这个函数**永远提前返回** ✓✓（日志实证：整局没有一条"又冒出来了"）
                //    → 改成看**全局的"当前是不是 MOD 页签"** ✓（由 `ShowTabConfig` 维护 ✓）
                if (!ModTabActive) return;

                // ★ 每帧也压一次"克隆菜单自带的原版行" ✓
                //   （原版 `OnEnable→Initialize` 会重建它们 ✗ → 只清一次不够 ✓）
                SuppressForeignOptionRows(menu);

                int leaked = CountActiveVanillaRows(menu);
                if (leaked <= 0) return;

                ClearVanillaContent(menu);
                LightLogger.Log($"[ConfigUIPanel] 原版内容又冒出来了（{leaked} 行）→ 已再次隐藏 ✓" +
                                "（克隆菜单 OnEnable→Initialize / 上一个页签的还原都会把它们放回来 ✗）");
            }
            catch (Exception ex)
            {
                LightLogger.LogDebug($"[ConfigUIPanel.RehideVanillaIfLeaked] {ex.Message}");
            }
        }

        /// <summary>
        /// **当前是不是停在 MOD 页签**（由 `GameSettingMenuPatch.ShowTabConfig` 维护 ✓）。
        ///
        /// ⚠️ 为什么不能再用 `_page != null` 判断：MOD 页签有三条完全不同的页面路径 ——
        ///   `ConfigUIPanel._page`（普通配置页）、`RoleListPage._page`（职业列表页）、
        ///   `RoleDetailWindow`（独立窗口）✗ → 只看其中一个必然漏 ✓✓
        /// </summary>
        internal static bool ModTabActive { get; set; }

        /// <summary>取当前宿主菜单（给 `GameSettingMenuPatch` 切页签时用 ✓）</summary>
        internal static GameOptionsMenu? CurrentHostMenu => _hostMenu ?? _templates;

        /// <summary>
        /// **把宿主菜单的原版内容清干净**（含：给原版"滚动到选中项"的组件断电 ✓）。
        /// `GameSettingMenuPatch` 在**每个 MOD 页签**上都要调一次 ✓
        /// </summary>
        internal static void CleanCurrentHost()
        {
            try
            {
                var menu = _hostMenu ?? _templates;
                if (menu == null) return;

                ClearVanillaContent(menu);
                DisableVanillaScrollToSelection(menu);

                // ★★ 2026-10-10 用户截图实证：**克隆菜单里自带的原版行根本没被清掉** ✗
                //    （"伪装者数/击杀冷却时间/伪装者视野/击杀范围"就叠在我们的行上 ✓）
                //    前三轮我都在猜"它们挂在哪个容器/哪个字段里" ✗ —— 全错 ✓
                //    → 换做法：**不看容器，直接把宿主菜单下所有"不是我们的"选项行压住** ✓✓
                //      （`OptionBehaviour` 是原版所有选项行的基类 ✓；
                //        我们的行名字是 `LightConfigRow_*` ✓，排除它们即可 ✓）
                SuppressForeignOptionRows(menu);

                // ★ 同帧内把 `MainArea` 里"不是我们的页"压住 ✓
                //   （不然要等下一帧的 `TickTabGuard` ✓ → 会闪一帧 ✗）
                Light.Patches.GameSettingMenuPatch.SuppressNonModPagesNow();
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.CleanCurrentHost] {ex.Message}");
            }
        }

        /// <summary>
        /// **把宿主菜单下"不是我们的"原版选项行全部压住** ✓（状态差量 ✓，记账 ✓ 可还原）。
        ///
        /// ⚠️ 为什么用"遍历 + 排除名字"而不是"按容器找"（前三轮的教训 ✗）：
        ///   克隆菜单里自带的原版行**不一定**在 `Children` 里 ✗、也不一定在 `Scroller.Inner` 里 ✗
        ///   （实测截图：它们就叠在我们的行上 ✓，而我按容器清的三种写法全都没碰到它们 ✗）
        ///   → 用**类型**找（`OptionBehaviour` = 原版所有选项行的基类 ✓）+
        ///     **名字**排除我们自己的（`LightConfigRow_*` / 在我们页里的 ✓）✓✓
        /// </summary>
        private static void SuppressForeignOptionRows(GameOptionsMenu menu)
        {
            try
            {
                var opts = menu.GetComponentsInChildren<OptionBehaviour>(true);
                if (opts == null) return;

                int hidden = 0;
                foreach (var ob in opts)
                {
                    if (ob == null) continue;
                    var go = ob.gameObject;
                    if (go == null) continue;

                    if (!go.activeSelf) continue;                                  // 已经关着 → 不管 ✓
                    if (go.name.StartsWith(OurPrefix, StringComparison.Ordinal)) continue;   // 我们的行 ✓
                    if (_page != null && go.transform.IsChildOf(_page.transform)) continue;  // 在我们页里的 ✓

                    RememberHidden(go);          // ★ 记账 → 切回原版页签时能还原 ✓
                    go.SetActive(false);
                    hidden++;

                    if (_foreignRowLogs < 10)
                    {
                        _foreignRowLogs++;
                        LightLogger.Log($"[ConfigUIPanel] 压住了克隆菜单自带的原版行 '{go.name}' ✓" +
                                        "（它会叠在我们的行上 ✗）");
                    }
                }

                if (hidden > 0)
                    LightLogger.Log($"[ConfigUIPanel] 本轮共压住 {hidden} 个原版行 ✓");
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.SuppressForeignOptionRows] {ex.Message}");
            }
        }

        private static int _foreignRowLogs;

        /// <summary>
        /// 关掉原版的 **`ScrollToSelection`** ✓。
        ///
        /// ⚠️⚠️ 2026-10-10 用户报「空引用」，日志实证：
        /// <code>
        ///   NullReferenceException
        ///     UnityEngine.Transform.get_localPosition ()
        ///     ScrollToSelection.LateUpdate ()          ← 原版组件 ✗
        /// </code>
        ///   它每帧去读"当前选中项"的 `localPosition` ✗ —— 而我们**隐藏/换页**把它的目标弄没了 ✗
        ///   → 每帧抛 NRE ✓✓（我们不需要"滚动到选中项"这个功能 ✓，直接断电 ✓）
        /// </summary>
        private static void DisableVanillaScrollToSelection(GameOptionsMenu menu)
        {
            try
            {
                var comps = menu.GetComponentsInChildren<ScrollToSelection>(true);
                if (comps == null) return;

                int disabled = 0;
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    if (!c.enabled) continue;
                    c.enabled = false;
                    disabled++;
                }
                if (disabled > 0)
                    LightLogger.Log($"[ConfigUIPanel] 已关掉 {disabled} 个原版 ScrollToSelection ✓（它每帧读已隐藏的选中项 → 空引用 ✗）");
            }
            catch (Exception ex)
            {
                LightLogger.LogDebug($"[ConfigUIPanel.DisableVanillaScrollToSelection] {ex.Message}");
            }
        }

        /// <summary>数一数原版行里**当前显示着**的有几个（&gt;0 就说明漏出来了 ✓）</summary>
        private static int CountActiveVanillaRows(GameOptionsMenu menu)
        {
            int n = 0;
            var children = menu.Children;
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    var ch = children[i];
                    if (ch == null) continue;
                    if (ch.gameObject.activeSelf) n++;
                }
            }
            return n;
        }

        /// <summary>上一次"可见行集合"的签名（用来防抖，见 Refresh ✓）</summary>
        private static string _lastVisibilitySig = "";

        /// <summary>
        /// 把"当前应显示的行 + 实际建出的行"算成一个签名 ✓ ——
        /// 签名没变 = 结构不需要动 ✓（防止每帧反复删建导致滚动位置被归零 ✗）
        /// </summary>
        private static string VisibilitySignature()
        {
            try
            {
                var sb = new System.Text.StringBuilder(256);
                foreach (var block in ConfigRegistry.Blocks)
                {
                    if (block == null) continue;
                    if (_singleBlock != null && block != _singleBlock) continue;
                    if (!MatchesFilter(block)) continue;

                    foreach (var item in block.Items)
                    {
                        if (item == null) continue;
                        sb.Append(item.Key).Append(item.IsVisible ? '+' : '-').Append(';');
                    }
                }
                sb.Append("built:");
                foreach (var kv in _drivers)
                    if (kv.Key != null) sb.Append(kv.Key.Key).Append(';');

                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>
        /// 把滚动位置归零（换页/重开面板时调用 ✓）。
        /// ⚠️ **绝不能**放在每帧路径里 ✗ —— 那正是"滚不动"的根因（见 UpdateScrollBounds 注释 ✓）
        /// </summary>
        private static void ResetScrollToTop()
        {
            try
            {
                var host = _hostMenu ?? _templates;
                var sb = host != null ? host.scrollBar : null;
                if (sb == null) return;

                var inner = sb.Inner;
                if (inner == null) return;

                var lp = inner.localPosition;
                inner.localPosition = new Vector3(lp.x, 0f, lp.z);
                _lastContentHeight = -1f;      // 让下一次范围计算重新记一遍 ✓
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.ResetScrollToTop] {ex.Message}");
            }
        }

        /// <summary>
        /// 打印**每一行**的 y 与容器可视范围 ✓ —— 用来判定"外泄"到底是
        /// ①该隐藏的没隐藏 ✗ 还是 ②行跑到窗口外面去了 ✗
        /// </summary>
        private static void LogRowLayoutOnce()
        {
            try
            {
                if (_rowLayoutLogs >= 8) return;
                _rowLayoutLogs++;

                var host = _hostMenu ?? _templates;
                float top = host != null ? StartYFor(host) : 0f;
                float viewport = host != null ? MeasureViewportHeight(host) : 0f;
                float bottom = top - viewport;

                var sb = new System.Text.StringBuilder(256);
                sb.Append($"[ConfigUIPanel][布局取证] 可视区 y=[{bottom:F2}, {top:F2}]（高 {viewport:F2}）行={_drivers.Count}：");

                foreach (var kv in _drivers)
                {
                    var it = kv.Key;
                    var drv = kv.Value;
                    if (it == null || drv == null || drv.gameObject == null) continue;

                    float y = drv.gameObject.transform.localPosition.y;
                    bool outside = y > top + 0.01f || y < bottom - 0.01f;
                    sb.Append(' ').Append(it.Key)
                      .Append("(y=").Append(y.ToString("F2"))
                      .Append(it.IsVisible ? "" : " **不该显示**")
                      .Append(outside ? " **超出可视区**" : "")
                      .Append(')');
                }

                LightLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.LogRowLayoutOnce] {ex.Message}");
            }
        }
        private const string DebugKey = "lid.debug.dummyCount";
        private const string EnabledKey = "lid.debug.enabled";

        private static readonly HashSet<string> _wantKeys = new();
        private static readonly HashSet<string> _builtKeys = new();

        private static void CollectWantKeys(ConfigBlock block)
        {
            foreach (var item in block.Items)
            {
                if (item == null) continue;
                if (!item.IsVisible) continue;      // 与 BuildBlock 的判定完全一致
                _wantKeys.Add(item.Key);
            }
        }

        /// <summary>把当前实际建出的行键记录下来（Build 末尾调用）。</summary>
        private static void SnapshotBuiltKeys()
        {
            _builtKeys.Clear();
            foreach (var kv in _drivers)
                if (kv.Key != null) _builtKeys.Add(kv.Key.Key);
        }

        /// <summary>
        /// 当前过滤条件下，"应显示"与"已建出来"是否不一致（不一致 → 需要重建）。
        /// 以注册表为准，所以"尚未被建出来的项变可见"也能被发现。
        /// </summary>
        private static bool VisibilityChanged()
        {
            // 单职业模式：只看那一个块，别把其它分类的项也算进来（否则会误判重建）
            if (_singleBlock != null)
                return BlockNeedsRebuild(_singleBlock);

            foreach (var block in ConfigRegistry.Blocks)
            {
                if (block == null || !MatchesFilter(block)) continue;
                if (BlockNeedsRebuild(block)) return true;
            }
            return false;
        }

        private static bool BlockNeedsRebuild(ConfigBlock block)
        {
            foreach (var item in block.Items)
            {
                if (item == null) continue;
                if (item.IsVisible != _drivers.ContainsKey(item)) return true;
            }
            return false;
        }

        /// <summary>取某行对应的配置项（供原版控件的 prefix 使用）。</summary>
        internal static ConfigItem ItemOf(int instanceId)
            => ItemOf(instanceId, out var it) ? it : null;

        /// <summary>带命中判定的版本（用于诊断"为什么没认出我们的行"）。</summary>
        internal static bool ItemOf(int instanceId, out ConfigItem? item)
        {
            if (RowMap.TryGetValue(instanceId, out var it)) { item = it; return true; }
            item = null;
            return false;
        }

        /// <summary>当前行容器（诊断用）。</summary>
        internal static Transform? CurrentContainer => _container;

        /// <summary>本次会话累计实例化过多少行（诊断用，用于发现重复建行）。</summary>
        internal static int InstantiatedCount => _instantiated;

        private static int _instantiated;

        /// <summary>
        /// 重排所有已建行（可见性变化后调用）：只改 active 与 y，不销毁重建。
        /// 顺序与 Build 一致，保证 y 的计算完全对应。
        /// </summary>
        public static void Relayout()
        {
            try
            {
                if (_container == null) return;

                // ⚠️ 必须用 StartYFor(host) 而不是裸 StartY！
                //   Build() 用的是 StartYFor（= StartY + 地图预览高度 0.60），
                //   而 Relayout 原来从 StartY 起算 → 比 Build 低 0.60 →
                //   行整体下沉，顶部就空出"那块被地图占的地方"。
                //   这正是用户报的"点击一次配置项后那块空缺又出现"，
                //   在走增量路径（TryIncrementalVisibility → Relayout）时尤其明显。
                var host = _hostMenu ?? _templates;
                float y = host != null ? StartYFor(host) : StartY;
                foreach (var block in ConfigRegistry.Blocks)
                {
                    if (block == null || !MatchesFilter(block)) continue;

                    // 块内一项都不可见时，分类头也一起藏起来
                    bool anyVisible = false;
                    foreach (var it in block.Items)
                        if (it.IsVisible) { anyVisible = true; break; }

                    var headerGo = FindSpawned($"LightConfigHeader_{block.Key}");
                    if (!anyVisible)
                    {
                        if (headerGo != null) headerGo.SetActive(false);
                        continue;
                    }

                    if (headerGo != null)
                    {
                        headerGo.SetActive(true);
                        headerGo.transform.localPosition = new Vector3(CurHeaderX, y, RowZ);
                    }
                    y -= HeaderHeight;

                    foreach (var item in block.Items)
                    {
                        var rowGo = FindSpawned($"LightConfigRow_{item.Key}");
                        if (rowGo == null) continue;

                        bool vis = item.IsVisible;
                        rowGo.SetActive(vis);
                        if (!vis) continue;

                        rowGo.transform.localPosition = new Vector3(CurRowX, y, RowZ);
                        y -= SpacingY;
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigUIPanel.Relayout]", ex);
            }
        }

        private static GameObject FindSpawned(string name)
        {
            foreach (var go in _spawned)
                if (go != null && go.name == name) return go;
            return null;
        }

        /// <summary>
        /// 【增量可见性】只处理"该建却没建"与"已建却不该建"的那几行，
        /// 成功返回 true（调用方直接返回，不再整页重建）。
        ///
        /// 为什么需要它：依赖项（`visibleWhen`）一变化就会走 Refresh → NeedRebuild → Rebuild，
        /// 而 Rebuild 会 DestroySpawned + 重走 ClearVanillaContent / 地图预览回收 /
        /// UpdateScrollBounds。实测每次切换都整页重建一次，行实例计数
        /// 0→1→3→4→6→7… 无限增长，也正是"点一次那块空缺又出现"的来源。
        ///
        /// 这里只做三件事：
        ///   ① 销毁不该再显示的行（该行已不可见）
        ///   ② 为刚变可见、但还没建出来的行**新建**（复用 AddConfigRow，位置稍后由 Relayout 定）
        ///   ③ Relayout() 重排 + 更新滚动条
        ///
        /// 任何一步不确定（容器没了、块结构变了）就返回 false 让调用方整页重建 —— 宁慢勿错。
        /// </summary>
        private static bool TryIncrementalVisibility()
        {
            try
            {
                // 容器必须还在，否则无从下手
                if (_container == null || _page == null) return false;

                // ---- ① 收集"已建却不该建"的行，销毁 ----
                var toRemove = new List<ConfigItem>();
                foreach (var kv in _drivers)
                {
                    var it = kv.Key;
                    if (it == null) continue;
                    // 只处理**当前过滤条件下属于本页**的项；不在本页的交给整页重建
                    if (!ItemBelongsToCurrentPage(it)) return false;
                    if (!it.IsVisible) toRemove.Add(it);
                }

                foreach (var it in toRemove)
                {
                    if (_drivers.TryGetValue(it, out var drv) && drv != null)
                    {
                        var go = drv.gameObject;
                        if (go != null)
                        {
                            RowMap.Remove(go.GetInstanceID());
                            _spawned.Remove(go);
                            Object.DestroyImmediate(go);
                        }
                    }
                    _drivers.Remove(it);
                    _rowCount = Mathf.Max(0, _rowCount - 1);
                }

                // ---- ② 收集"应建却没建"的行，新建 ----
                var toAdd = new List<ConfigItem>();
                foreach (var block in ConfigRegistry.Blocks)
                {
                    if (block == null || !MatchesFilter(block)) continue;
                    foreach (var item in block.Items)
                    {
                        if (item == null) continue;
                        if (!item.IsVisible) continue;
                        if (_drivers.ContainsKey(item)) continue;   // 已经建过
                        if (FindSpawned($"LightConfigRow_{item.Key}") != null) continue;
                        toAdd.Add(item);
                    }
                }

                // 新行一律先塞在 0 位置，紧接着 Relayout() 会把它们摆正
                foreach (var item in toAdd)
                    AddConfigRow(item, StartY);

                // ---- ③ 重排 + 滚动条 ----
                Relayout();
                var host = _hostMenu ?? _templates;
                if (host != null)
                {
                    float startY = StartYFor(host);
                    float lastY = LastRowY();      // Relayout 后的真实末尾
                    UpdateScrollBounds(host, startY, lastY);
                }

                SnapshotBuiltKeys();

                LightLogger.Log($"[ConfigUIPanel] 增量更新可见性：+{toAdd.Count} 行 / -{toRemove.Count} 行" +
                                $"（现共 {_drivers.Count} 行，未整页重建）");

                // ★ 2026-10-06 取证（用户报「职业详情页里减少最大数量 → 一些配置项**外泄**」）：
                //   我目前**分不清**是"该隐藏的行没隐藏"还是"行跑出了窗口范围" ✗，
                //   所以把**每行的 y 与容器可视范围**打出来 ✓ —— 一看便知 ✓
                //   （按工程规矩：先取证，别猜 ✓）
                LogRowLayoutOnce();
                return true;
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[ConfigUIPanel.TryIncrementalVisibility] {ex.Message} → 退回整页重建");
                return false;
            }
        }

        /// <summary>该项是否属于"当前这一页"（单块模式只看那块；多块模式看过滤是否通过）。</summary>
        private static bool ItemBelongsToCurrentPage(ConfigItem item)
        {
            if (item == null) return false;
            if (_singleBlock != null) return ReferenceEquals(item.Block, _singleBlock);

            var b = item.Block;
            return b != null && MatchesFilter(b);
        }

        /// <summary>算一遍当前可见行的末尾 y（给滚动条用），与 Relayout 的推进方式保持一致。</summary>
        private static float LastRowY()
        {
            // 同样必须带上地图预览的偏移，否则内容高度算少 0.60 → 滚动条判定不准
            var host = _hostMenu ?? _templates;
            float y = host != null ? StartYFor(host) : StartY;
            foreach (var block in ConfigRegistry.Blocks)
            {
                if (block == null || !MatchesFilter(block)) continue;

                bool anyVisible = false;
                foreach (var it in block.Items) if (it.IsVisible) { anyVisible = true; break; }
                if (!anyVisible) continue;

                y -= HeaderHeight;
                foreach (var item in block.Items)
                {
                    if (!item.IsVisible) continue;
                    y -= SpacingY;
                }
            }
            return y;
        }

        // =====================================================================
        //  模板查找
        // =====================================================================

        private static Sprite? _roundedSprite;

        /// <summary>圆角长方形 sprite（带 9 宫格边框，Sliced 任意拉伸；列表/返回按钮共用）。</summary>
        internal static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;

            const int w = 64, h = 32, r = 10;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 像素到圆角矩形边缘的距离 → 1px 抗锯齿
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - r), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - r), 0f);
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new UColor(1f, 1f, 1f, a));
                }
            }
            tex.Apply();

            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _roundedSprite;
        }

        /// <summary>
        /// 分类头模板：优先用 GameOptionsMenu 的私有预制体字段 categoryHeaderOrigin；
        /// 取不到再退回场景里现成的分类头（例如角色设置页的）。
        /// </summary>
        private static CategoryHeaderMasked FindHeaderTemplate()
        {
            try
            {
                var origin = _templates?.categoryHeaderOrigin;
                if (origin != null) return origin;

                var menu = GameSettingMenu.Instance;
                if (menu != null)
                {
                    var h = menu.GetComponentInChildren<CategoryHeaderMasked>(true);
                    if (h != null) return h;
                }
                return Object.FindObjectOfType<CategoryHeaderMasked>(true);
            }
            catch { return null; }
        }

        private static GameObject NewUIObject(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.layer = LayerExpansion.GetUILayer();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one;
            return go;
        }
    }

    /// <summary>
    /// 一行原版控件的驱动器：把配置值写进原版控件的显示部件。
    /// 原版控件自身的 Initialize/FixedUpdate/UpdateValue 由 <c>ConfigRowPatches</c> 拦截，
    /// 显示完全由这里负责。
    /// </summary>
    public class ConfigRowDriver : MonoBehaviour
    {
        private ConfigItem _item;
        private OptionBehaviour _behaviour;

        /// <summary>上一次的可见性（用于检测是否需要重建面板）。</summary>
        public bool WasVisible { get; private set; }

        /// <summary>绑定一行原版控件。</summary>
        public void Bind(ConfigItem item, OptionBehaviour behaviour)
        {
            _item = item;
            _behaviour = behaviour;
            WasVisible = item.IsVisible;
        }

        /// <summary>按当前值刷新所有显示部件。</summary>
        public void RefreshVisual()
        {
            try
            {
                if (_item == null || _behaviour == null) return;

                // 标题：我们的显示名（原版走翻译键，这里直接写）
                var title = GetTitleText();
                if (title != null)
                {
                    var tr = title.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;
                    title.text = _item.DisplayName ?? _item.Key;
                    if (_item.NameColor.HasValue) title.color = _item.NameColor.Value;
                }

                var toggle = GetToggle();
                if (toggle != null)
                {
                    // Bool：勾选状态就是值
                    if (toggle.CheckMark != null) toggle.CheckMark.enabled = _item.GetBool();
                }
                else
                {
                    var valueText = GetValueText();
                    if (valueText != null)
                    {
                        valueText.text = _item.GetValueText();

                        // 诊断：确认"数值框里显示标题"到底是同一个 TMP 还是写错了对象。
                        // 只对数值行打一次。
                        if (_diagLogged.Add(_item.Key))
                        {
                            var t = GetTitleText();
                            bool same = t != null && t.GetInstanceID() == valueText.GetInstanceID();
                            LightLogger.Log(
                                $"[ConfigRowDriver.Diag] 行 {_item.Key} " +
                                $"标题TMP={(t == null ? "null" : t.name + "#" + t.GetInstanceID())} " +
                                $"数值TMP={valueText.name}#{valueText.GetInstanceID()} " +
                                $"同一个={same} | 标题文字='{(t == null ? "" : t.text)}' " +
                                $"数值文字='{valueText.text}' | path={PathOf(valueText.transform)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigRowDriver.RefreshVisual]", ex);
            }
        }

        /// <summary>增减一步并同步。</summary>
        public void Step(int dir)
        {
            try
            {
                if (_item == null) return;

                if (_item.Type == ConfigType.Bool) _item.Toggle();
                else if (dir > 0) _item.Increase();
                else _item.Decrease();

                RefreshVisual();
                ConfigSync.RaiseAndSync(_item);

                // 值变了可能影响别的项的可见性（如"启用调试模式"控制数量项）
                ConfigUIPanel.Refresh();
                ConfigUIPanel.Relayout();
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[ConfigRowDriver.Step]", ex);
            }
        }

        /// <summary>取标题文本部件。
        /// ⚠️ 不靠 `is` 判断：Instantiate 出来的对象静态类型是基类 OptionBehaviour，
        ///    必须用 GetComponent 取真实组件，否则所有类型判断都会静默为 false。</summary>
        private TextMeshPro GetTitleText()
        {
            var tog = _behaviour.GetComponent<ToggleOption>();
            if (tog != null) return tog.TitleText;

            var num = _behaviour.GetComponent<NumberOption>();
            if (num != null) return num.TitleText;

            var str = _behaviour.GetComponent<StringOption>();
            if (str != null) return str.TitleText;

            return _behaviour != null ? _behaviour.GetComponentInChildren<TextMeshPro>(true) : null;
        }

        /// <summary>取数值文本部件（Bool 行没有）。同样用 GetComponent 取真实组件。</summary>
        private TextMeshPro GetValueText()
        {
            var num = _behaviour.GetComponent<NumberOption>();
            if (num != null) return num.ValueText;

            var str = _behaviour.GetComponent<StringOption>();
            if (str != null) return str.ValueText;

            return null;
        }

        /// <summary>取真实的 ToggleOption（Bool 行才有）。</summary>
        private ToggleOption GetToggle() => _behaviour != null ? _behaviour.GetComponent<ToggleOption>() : null;

        private static readonly HashSet<string> _diagLogged = new();

        /// <summary>打印一个 Transform 的层级路径（诊断用）。</summary>
        private static string PathOf(Transform t)
        {
            if (t == null) return "null";
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }
    }
}
