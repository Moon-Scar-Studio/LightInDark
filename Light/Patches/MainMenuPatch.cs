using AmongUs.Data;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Config;
using Light.UI;
using Light.UI.Help;
using Light.UI.HudUI;
using Light.UI.MainMenu;
using Light.UI.Window;
using Light.Utilities;
using LightInDark.Core;
using LightInDark.Events;
using LightInDark.Language;
using LightInDark.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Light.Patches;

/// <summary>
/// 定制主界面布局：重排左侧按钮、替换右侧面板、新增自定义屏幕与背景画廊等。
/// 注意：类级所有补丁属性均标注在方法上，因此必须在此提供类级 [HarmonyPatch]，
/// 否则 Harmony.PatchAll() 会跳过整个类（只扫描"类级带 [HarmonyPatch] 的类"）。
/// </summary>
[HarmonyPatch]
public static class MainMenuPatch
{
    private static bool _showingPanel;

    /// <summary>
    /// 让右侧面板滑走 / 回来。
    /// 「更换背景图」打开时要滑走（否则它会从我们的弹窗旁边露出一块框）；
    /// 关掉之后保持滑走状态（此时 LightScreen 也是关的，等于回到主界面）。
    /// </summary>
    public static void SetRightPanelVisible(bool visible) => _showingPanel = visible;
    private static GameObject? _rightPanel;
    private static Vector3 _rightPanelOp;
    private static GameObject? _lightScreen;
    private static GameObject? _lightSubScreen;
    /// <summary>「更换背景图」面板（原来的 ImageGalleryPanel 已废弃，见 SetupGalleryScreen 注释）。</summary>
    private static BackgroundPanel? _bgPanel;
    private static bool _bgMoved;
    private static float _bgMoveDelay;
    private static bool _updaterChecked;
    private static bool _bgInitialMoveDone;
    private static bool _hasBackground;

    private static bool _sbsbsb;
    private static GameObject? FindGO(string name) => GameObject.Find(name);

    /// <summary>安全取子物体，越界时返回 null，避免 GetChild 抛错。</summary>
    private static Transform? GetChild(Transform parent, int index)
    {
        if (parent == null || index < 0 || index >= parent.childCount) return null;
        return parent.GetChild(index);
    }

    private static Dictionary<string, PassiveButton> FindButtons()
    {
        try
        {
            var dict = new Dictionary<string, PassiveButton>();
            var leftPanel = GameObject.Find("LeftPanel");
            if (leftPanel == null) return dict;
            foreach (var b in leftPanel.GetComponentsInChildren<PassiveButton>(true))
                if (b != null && !dict.ContainsKey(b.name))
                    dict[b.name] = b;
            LightLogger.LogWarning($"[Light.UI] 找到 {dict.Count} 个按钮");
            return dict;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.FindButtons]", ex);
            return new Dictionary<string, PassiveButton>();
        }
    }
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPrefix]
    public static void Prefix(MainMenuManager __instance)
    {
        var plugin = IL2CPPChainloader.Instance.Plugins.Values.FirstOrDefault(p=>p.Metadata.Name== "MalumMenu");
        if (plugin != null)
        {
            Harmony.UnpatchAll();
            return;
        }
        int pluginCount = IL2CPPChainloader.Instance.Plugins.Count;
        if(pluginCount != 2)
        {
            LightUtils.ShowCustomDisconnectWindow("<b><color=red>警告</color></b><br>检测到<b>超过 2 </b>的Plugin数!<br>LID本身与绝大多数模组不兼容，除非你安装的模组特殊说明！");
        }
        return;
    }
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPostfix]
    public static void Postfix(MainMenuManager __instance)
    {
        try
        {
            // 清理上次创建的克隆按钮（场景切换后旧的被销毁但缩放器列表里仍有死引用）
            var oldScaler = Object.FindObjectOfType<SlicedAspectScaler>();
            if (oldScaler != null)
            {
                for (int i = oldScaler.objectsToScale.Count - 1; i >= 0; i--)
                {
                    var item = oldScaler.objectsToScale[i];
                    if (item == null || item.gameObject == null ||
                        item.gameObject.name.StartsWith("LightButton") ||
                        item.gameObject.name.StartsWith("CustomButton"))
                    {
                        oldScaler.objectsToScale.RemoveAt(i);
                    }
                }
            }
            // 清理可能残留的旧 GameObject
            for (int i = 0; i < 4; i++)
            {
                var old = FindGO($"CustomButton{i}");
                if (old != null) Object.Destroy(old);
            }
            var oldLight = FindGO("LightButton");
            if (oldLight != null) Object.Destroy(oldLight);

            _showingPanel = false;
            _rightPanel = null;
            _lightScreen = null;
            _lightSubScreen = null;
            _bgPanel = null;
            LightLogger.LogWarning("开始布局");

            // ★ 2026-11-03 用户："第一次进主界面贴图不替换" ✓
            //   根因：`MainMenuButtonSpritePatch.Suppress` 是**静态字段** ✓ ——
            //   上一次会话切成「原版」后它是 true ✗，下次启动第一帧仍为 true ✗ →
            //   贴图替换被整体跳过（要等 `MainMenuButtonStyler.Apply` 再跑一次才恢复 ✗）。
            //   → 布局一开始就按**存档里的样式**重置它 ✓（与 `Apply` 里的判断完全一致 ✓）
            try
            {
                Patches.MainMenuButtonSpritePatch.Suppress =
                    AppearanceSettings.ButtonStyle != MainButtonStyle.Mod;
                LightLogger.Log($"[Light] 贴图替换开关按存档样式初始化 → Suppress={Patches.MainMenuButtonSpritePatch.Suppress}" +
                                $"（样式={AppearanceSettings.ButtonStyle}）");
            }
            catch { }

            // ═══════════════════════════════════════════════════════════════
            //  ★ 2026-11-03 **解耦**（用户："把 MainMenuPatch 解耦开，
            //   让原版就是和原版一样，只是多了大标题和按钮" ✓）
            //
            //  「原版」样式 = **一个字节的原版布局都不碰** ✓：
            //    ✗ 不搬 LeftPanel / 不隐藏 Divider / 不挪 BackgroundTexture
            //    ✗ 不建 LightButton / LightScreen / 子屏 / 画廊
            //    ✗ 不做右侧面板滑入、不改 ModStamp
            //  只做两件事 ✓：① 建那四个按钮  ② 摆大标题 + 署名（`MainMenuStyleLayout` ✓）
            // ═══════════════════════════════════════════════════════════════
            if (AppearanceSettings.ButtonStyle == MainButtonStyle.Vanilla)
            {
                try
                {
                    // ★ 用户："背景图只在原版样式砍掉" ✓
                    Light.UI.MainMenu.BackgroundRenderer.Suppressed = true;
                    Light.UI.MainMenu.BackgroundRenderer.Shutdown();

                    // ★ 用户："就算是原版样式，也应和 NOS 原来添加 Nebula 按钮相同，添加 Light 按钮" ✓
                    // ★ 用户 2026-11-03："原版要建 LightScreen…跟 NOS 一样" ✓ → 原版也建 LightScreen ✓
                    //   （所以 LightButton 走**默认分支** ✓ = ResetScreen + 打开 LightScreen ✓，和 NOS 一致 ✓）
                    SetupLightScreen(__instance);
                    SetupSubScreen(__instance);

                    try
                    {
                        var vb = FindButtons();
                        float vh = 0.7f;
                        if (vb.TryGetValue("NewsButton", out var vNews) && vb.TryGetValue("AcountButton", out var vAcct))
                            vh = vNews.transform.localPosition.y - vAcct.transform.localPosition.y;
                        CreateLightButton(__instance, vb, vh);   // 默认 → 打开 LightScreen ✓（NOS 同款 ✓）
                    }
                    catch (Exception lex)
                    {
                        LightLogger.LogWarning($"[MainMenuPatch] 原版样式创建 LightButton 失败：{lex.Message}");
                    }

                    // ★★ 用户 2026-11-03 两次强调："看 NOS" / "左边的按钮偏了，开始按钮超出 panel"
                    //   → **完整照 Nova `MainMenuPatch.cs:54-78`**（缺一步就会溢出 ✗）：
                    //     ① auLogo：`PercentWidth = 0.14` + `DoSetUp()` + 位置 (-0.8, 0.25) ✓
                    //     ② **`ReworkedLeftPanel`**：复制 sprite/tile/drawMode、`size + (0, 0.5)`、
                    //        位置 `(0, height*0.5)`、登记进 Scaler ✓，旧面板 renderer 关掉 ✓
                    //        ★ 这一步就是"按钮移上去了、但面板没变高 → 开始按钮溢出"的解药 ✓✓
                    //     ③ 主按钮上移 height（`|x|<0.1` 的才移）✓  ④ Divider 上移 height ✓
                    try
                    {
                        ApplyNovaLeftPanel(__instance);
                    }
                    catch (Exception bex)
                    {
                        LightLogger.LogWarning($"[MainMenuPatch] 原版样式 Nova 左栏失败：{bex.Message}");
                    }

                    SetupExtraButtons(__instance);
                    MainMenuStyleLayout.Apply(__instance);
                    LightLogger.Log("[Light] 原版样式：跳过主界面改造 + 建 LightButton/LightScreen（NOS 同款 ✓）+ 按钮上移（Nova 同款 ✓）+ 标题/署名/四按钮 ✓");
                }
                catch (Exception ex)
                {
                    LightLogger.LogError("[MainMenuPatch] 原版样式布局失败", ex);
                }
                return;
            }

            // 其它样式：自定义背景照常显示 ✓（"背景图只在原版样式砍掉" ✓）
            Light.UI.MainMenu.BackgroundRenderer.Suppressed = false;

            VanillaAsset.Preload();
            var modStamp = FindGO("ModStamp");
            if (modStamp != null)
            {
                modStamp.SetActive(true);
                modStamp.transform.localScale = Vector3.one * 0.06f;
                var sr = modStamp.GetComponent<SpriteRenderer>();
                sr?.sprite = ResourceHelper.LoadSpriteFromResource("Light.Resources.ModStamp.png");
            }
            _bgMoved = _bgInitialMoveDone;
            _bgMoveDelay = 0f;
            // ★ 2026-10-25 诊断：整个布局（进主菜单时跑一次）到底花了多少 ms
            //    —— 如果还卡，就用这个数判断是我们这段还是原版/别的东西 ✓
            var layoutSw = System.Diagnostics.Stopwatch.StartNew();
            // ⚠️ 2026-11-03：分段计时（用户报"进主界面非常卡"，旧日志只有总数 1635ms ✗ 看不出花在哪）
            long markFind = 0, markPanel = 0;
            var bg = FindGO("BackgroundTexture");
            bg?.SetActive(!_bgInitialMoveDone);

            var btns = FindButtons();
            markFind = layoutSw.ElapsedMilliseconds;   // ← 找按钮/面板 用时

            // 分段计时：每个阶段结束调一次（只在布局路径上跑，一次一条，成本可忽略 ✓）
            var phaseSb = new System.Text.StringBuilder(64);
            long phaseLast = markFind;
            void Phase(string name)
            {
                long now = layoutSw.ElapsedMilliseconds;
                phaseSb.Append(name).Append('=').Append(now - phaseLast).Append("ms ");
                phaseLast = now;
                markPanel = now;
            }

            var leftPanel = FindGO("LeftPanel");
            if (leftPanel != null)
            {
                var sizer = leftPanel.transform.FindChild("Sizer");
                if (sizer != null)
                {
                    var auLogo = sizer.GetComponent<AspectSize>();
                    if (auLogo != null)
                    {
                        auLogo.PercentWidth = 0.14f;
                        auLogo.DoSetUp();
                        auLogo.transform.localPosition += new Vector3(-0.8f, 0.25f, 0f);
                    }
                }
            }
            float height = 0.7f;
            if (btns.TryGetValue("NewsButton", out var news) && btns.TryGetValue("AcountButton", out var acct))
                height = news.transform.localPosition.y - acct.transform.localPosition.y;

            foreach (var kvp in btns)
            {
                var btn = kvp.Value;
                if (btn != null && Mathf.Abs(btn.transform.localPosition.x) < 0.1f)
                    btn.transform.localPosition += new Vector3(0f, height, 0f);
            }
            var divider = leftPanel?.transform.FindChild("Main Buttons")?.FindChild("Divider");
            divider?.localPosition += new Vector3(0f, height, 0f);
            if (leftPanel != null)
            {
                var reworked = UnityHelper.CreateObject<SpriteRenderer>(
                    "ReworkedLeftPanel", leftPanel.transform, new Vector3(0f, height * 0.5f, 0f));
                var oldSr = leftPanel.GetComponent<SpriteRenderer>();
                if (oldSr != null)
                {
                    reworked.sprite = oldSr.sprite;
                    reworked.tileMode = oldSr.tileMode;
                    reworked.drawMode = oldSr.drawMode;
                    reworked.size = oldSr.size + new Vector2(0f, 0.5f);
                    oldSr.enabled = false;
                }
            }
            var onlineRoot = __instance.mainMenuUI.transform
                .FindChild("AspectScaler")?.FindChild("Online Buttons");
            if (onlineRoot != null)
            {
                for (int i = 0; i < onlineRoot.childCount; i++)
                {
                    var child = onlineRoot.GetChild(i);
                    var scaler = child.Find("Scaler") ?? child;
                    if (scaler == null) continue;
                    for (int j = 0; j < scaler.childCount; j++)
                    {
                        var btn = scaler.GetChild(j);
                        var btnName = btn.name.ToLowerInvariant();
                        if (btnName.Contains("createlobby") || btnName.Contains("host"))
                            btn.localPosition = new(-1f, 0.5f, 0f);
                        else if (btnName.Contains("joingame") || btnName.Contains("join"))
                            btn.localPosition = new(1.5f, 0.5f, 0f);
                        else if (btnName.Contains("findgame"))
                            btn.localPosition = new(0f, -20f, 0f);
                        else if (btnName.Contains("line") || btnName.Contains("divider"))
                            btn.localPosition = new(0f, -20f, 0f);
                    }
                }
            }
            CreateLightButton(__instance, btns, height);
            AdjustIcons();
            ColorAllButtons();
            Phase("按钮样式");
            CloneTitleToMainMenu(__instance);

            // ═══════════════════════════════════════════════════════════════
            //  左栏：两种观感 ✓（用户 2026-11-03："左栏要 Nova 版本" ✓）
            //   · MOD     → 老做法：把左栏拆空 + 隐藏（我们自己的观感 ✓）
            //   · 美化版   → **照 Nova**（`MainMenuPatch.cs:54-78` ✓）：
            //        ① 保留 LeftPanel ✓
            //        ② 用 `ReworkedLeftPanel` 复制原面板 sprite / tileMode / drawMode，
            //           位置 `y = height*0.5` ✓、`size + (0, 0.5)` ✓、旧面板 renderer `enabled = false` ✓
            //        ③ 主按钮整体上移 `height` ✓（Credits/Quit 除外：|x| < 0.1 的才移 ✓）
            //        ④ `Main Buttons/Divider` 也上移 `height` ✓（**不是隐藏** ✗）
            // ═══════════════════════════════════════════════════════════════
            // ⚠️ 2026-11-03：美化版已删除 ✓ → 左栏恢复唯一的 MOD 做法（拆空 + 隐藏 ✓）
            if (leftPanel != null)
            {
                var mainButtonsRoot = leftPanel.transform.FindChild("Main Buttons");
                leftPanel.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
                for (int i = 0; i < leftPanel.transform.childCount; i++)
                    leftPanel.transform.GetChild(i).SetParent(leftPanel.transform.parent);
                if (mainButtonsRoot != null)
                    mainButtonsRoot.transform.localPosition = new Vector3(-4.0f, 0f, 0f);
                leftPanel.SetActive(false);
            }
            FindGO("Divider")?.SetActive(false);
            SetupExtraButtons(__instance);
            ApplyButtonEffects(__instance);
            Phase("额外按钮");
            SetupRightPanel(__instance);
            Phase("右侧面板");
#if !DEBUG
            RemoveFreePlayAndCenterHowToPlay(__instance);   // Release：去掉练习模式，玩法说明居中
#endif
            SetupLightScreen(__instance);
            SetupSubScreen(__instance);
            SetupGalleryScreen(__instance);
            Phase("Light屏/画廊");
            MoveScreenTint(__instance);
            var decoTex = new Texture2D(1, 1);
            decoTex.SetPixel(0, 0, Color.white);
            decoTex.Apply();
            var decoSpr = Sprite.Create(decoTex, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 100f);
            var deco = UnityHelper.CreateObject<SpriteRenderer>("LightDecoLine",
                __instance.mainMenuUI.transform, new Vector3(0f, -3.2f, -2f));
            deco.sprite = decoSpr;
            deco.drawMode = SpriteDrawMode.Sliced;
            deco.size = new Vector2(8f, 0.02f);
            deco.color = new Color(1f, 1f, 1f, 0.12f);
            // ★★ 2026-10-25 **性能修复**（用户："进主菜单太卡，大头应该是找 freeplay 直接扫整个场景"）
            //   原来这里是 `Resources.FindObjectsOfTypeAll<GameObject>()` ✗
            //   —— 它枚举**所有已加载对象（含资源/预制体）**，实测动辄上万次，且每次进主菜单都要跑
            //     → **一进主菜单就卡一下** ✓（正是用户说的那个大头）
            //   原版本来就有现成字段（AGENTS §5.1：`freePlayButton` / `howToPlayButton`）→ 直接引用，零扫描 ✓
#if !DEBUG
            // Release：FreePlay 的移除 + HowToPlay 的居中已由
            //          `RemoveFreePlayAndCenterHowToPlay(__instance)` 用同一对字段完成 ✓
            //          这里**不能再动**（挪到 -20 会把居中后的按钮藏掉 ✗）
#else
            // Debug：保留旧观感（两个按钮都挪到屏幕外）
            try
            {
                if (__instance.freePlayButton != null)
                    __instance.freePlayButton.transform.localPosition = new Vector3(0f, -20f, 0f);
                if (__instance.howToPlayButton != null)
                    __instance.howToPlayButton.transform.localPosition = new Vector3(0f, -20f, 0f);
            }
            catch (Exception ex) { LightLogger.LogWarning($"[MainMenuPatch] 挪 FreePlay/HowToPlay 失败：{ex.Message}"); }
#endif

            layoutSw.Stop();
            // ⚠️ 2026-11-03：带上分段耗时（找按钮 / 各阶段 / 收尾），一眼定位大头 ✓
            LightLogger.LogWarning($"[Light.UI] === 布局完成 === 本次耗时 {layoutSw.ElapsedMilliseconds}ms " +
                                   $"（找按钮 {markFind}ms / {phaseSb} / 收尾 {layoutSw.ElapsedMilliseconds - markPanel}ms）" +
                                   "（用于判断进主菜单卡是不是我们这段；>100ms 值得继续优化 ✓）");

            // 制作人员/退出
            if (btns.TryGetValue("LightButton", out var lBtn) && lBtn != null &&
                btns.TryGetValue("AcountButton", out var aBtn) && aBtn != null &&
                btns.TryGetValue("CreditsButton", out var cBtn) && cBtn != null)
            {
                float spacingY = Mathf.Abs(lBtn.transform.position.y - aBtn.transform.position.y);
                float targetY = lBtn.transform.position.y - spacingY;
                var bounds = FindGO("BottomButtonBounds");
                Transform anchor = bounds != null ? bounds.transform : cBtn.transform.parent;
                if (anchor != null)
                {
                    float deltaY = targetY - cBtn.transform.position.y;
                    anchor.position += new Vector3(0f, deltaY, 0f);
                    LightLogger.LogWarning(
                        $"[Light.UI][diag] 制作人员/退出调整 {deltaY:F3}，目标Y={targetY:F3}");
                }
            }

            Application.targetFrameRate = LightPlugin.LightSettingsData.MaxFPS;
            if (!_updaterChecked)
            {
                _updaterChecked = true;
                __instance.StartCoroutine(CoCheckUpdater().WrapToIl2Cpp());
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning("[Light.UI] 异常: " + ex);
        }
    }

    private static IEnumerator CoCheckUpdater()
    {
        yield return null;
        yield return null;
        yield return null;
        try
        {
            string updaterPath = VersionMaker.UpdaterExePath;
            if (!File.Exists(updaterPath))
            {
                LightLogger.LogWarning($"未找到更新脚本：{updaterPath}");
                LightUtils.ShowCustomDisconnectWindow(
                    $"未找到更新脚本 {VersionMaker.UpdaterExeName}！\n" +
                    $"请确认它位于：\n{Light.Tools.LightToolManager.ToolsDir}\n" +
                    "（正常启动游戏时加载页会自动下载它）");
            }
        }
        catch { }
    }

    // ⚠️ 2026-11-03：美化版已删除 ✓ → `ApplyNovaLeftPanel`（Nova 左栏重做）随之删除 ✗
    //   如果以后要做"左栏美化"，照 Nova `MainMenuPatch.cs:54-78` 重写即可 ✓

    /// <summary>
    /// **完整照 Nova**（`Patches/Old/MainMenuPatch.cs:54-78` ✓）处理左栏 ✓：
    ///   ① auLogo：`PercentWidth = 0.14` → `DoSetUp()` → 位置 `(-0.8, 0.25)` ✓
    ///   ② 新建 `ReworkedLeftPanel`：复制原面板 sprite / tileMode / drawMode ✓，
    ///      位置 `(0, height*0.5)` ✓、`size + (0, 0.5)` ✓、登记进 `SlicedAspectScaler` ✓
    ///   ③ 旧面板 renderer `enabled = false` ✓（对象还在 ✓）
    ///   ④ 主按钮上移 `height`（`|x| < 0.1` 的才移 ✓ = Credits/Quit 不动 ✓）
    ///   ⑤ `Main Buttons/Divider` 也上移 `height` ✓
    /// ⚠️ ② 不能省 ✗ —— 只上移按钮、不把面板变高，就会"开始按钮超出 panel" ✓（用户截图 ✓）
    /// </summary>
    private static void ApplyNovaLeftPanel(MainMenuManager __instance)
    {
        try
        {
            var leftPanel = FindGO("LeftPanel");
            if (leftPanel == null) return;

            // ① auLogo
            try
            {
                var sizer = leftPanel.transform.FindChild("Sizer");
                var auLogo = sizer != null ? sizer.GetComponent<AspectSize>() : null;
                if (auLogo != null)
                {
                    auLogo.PercentWidth = 0.14f;
                    auLogo.DoSetUp();
                    auLogo.transform.localPosition += new Vector3(-0.8f, 0.25f, 0f);
                }
            }
            catch { }

            float height = 0.7f;
            try
            {
                var btns = FindButtons();
                if (btns.TryGetValue("NewsButton", out var news) && btns.TryGetValue("AcountButton", out var acct))
                    height = news.transform.localPosition.y - acct.transform.localPosition.y;
            }
            catch { }

            // ② ReworkedLeftPanel（★ 关键：面板变高 0.5 ✓）
            try
            {
                var oldPanel = leftPanel.GetComponent<SpriteRenderer>();
                if (oldPanel != null)
                {
                    var reworked = UnityHelper.CreateObject<SpriteRenderer>(
                        "ReworkedLeftPanel", leftPanel.transform, new Vector3(0f, height * 0.5f, 0f));
                    reworked.sprite = oldPanel.sprite;
                    reworked.tileMode = oldPanel.tileMode;
                    reworked.drawMode = oldPanel.drawMode;
                    reworked.size = oldPanel.size + new Vector2(0f, 0.5f);

                    var scalerList = Object.FindObjectOfType<SlicedAspectScaler>();
                    var scaled = leftPanel.GetComponent<AspectScaledAsset>();
                    if (scalerList != null && scaled != null) scalerList.objectsToScale.Add(scaled);

                    oldPanel.enabled = false;   // ③
                }
            }
            catch (Exception ex) { LightLogger.LogWarning($"[MainMenuPatch] ReworkedLeftPanel 失败：{ex.Message}"); }

            // ④ 主按钮上移
            try
            {
                foreach (var button in __instance.mainButtons)
                {
                    if (button == null) continue;
                    if (Math.Abs(button.transform.localPosition.x) < 0.1f)
                        button.transform.localPosition += new Vector3(0f, height, 0f);
                }
            }
            catch { }

            // ⑤ Divider 上移
            try
            {
                var mainButtons = leftPanel.transform.FindChild("Main Buttons");
                var divider = mainButtons != null ? mainButtons.FindChild("Divider") : null;
                if (divider != null) divider.transform.localPosition += new Vector3(0f, height, 0f);
            }
            catch { }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.ApplyNovaLeftPanel]", ex);
        }
    }

    private static void CreateLightButton(MainMenuManager __instance, Dictionary<string, PassiveButton> btns, float height,
                                          Action? onClickOverride = null)
    {
        try
        {
            if (!btns.TryGetValue("SettingsButton", out var settings)) return;
            var clone = GameObject.Instantiate(settings.gameObject, settings.transform.parent);
            clone.name = "LightButton";
            clone.transform.localPosition =
                settings.transform.localPosition - new Vector3(0f, height, 0f);

            // ⚠️ 2026-11-03 修：图标缩放别写死 0.1 ✗ —— 先记下设置按钮图标的**原始缩放** ✓
            //   （写死会让图标在大分辨率下变成一个小点 ✓，用户说"Light 按钮也不对"就是这个 ✓）
            Vector3? iconScale = null;

            var tex = GraphicsHelper.LoadTextureFromResources("Light.Resources.Lobby.LightInDark.png");
            if (tex != null)
            {
                var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                for (int i = 0; i < clone.transform.childCount; i++)
                {
                    var child = clone.transform.GetChild(i);
                    var icon = child.FindChild("Icon");
                    if (icon != null)
                    {
                        // ⚠️⚠️ 2026-11-03 用户："Light 按钮上面的标题变得莫名其妙的大" ✓
                        //   Nova 里 `icon.localScale = (1,1,1)` 成立，是因为**它的图标本来就是小图** ✓；
                        //   我们的 `LightInDark.png` 是 **1200×900 的大 logo** ✗
                        //   → 直接 (1,1,1) 就铺满整个左栏 ✓✓（截图里那两处叠在一起的大标题就是它 ✓）
                        //   ★ 修法：按「原图标高度 / 新图高度」等比换算 ✓
                        //     这样不管以后换成什么尺寸的图，图标视觉大小都与原设置按钮一致 ✓
                        var sr = icon.GetComponent<SpriteRenderer>();
                        if (sr != null && sr.sprite != null)
                        {
                            float oldH = sr.sprite.bounds.size.y;
                            float newH = spr.bounds.size.y;
                            if (newH > 0.0001f && oldH > 0.0001f)
                                icon.localScale = icon.localScale * (oldH / newH);
                        }
                        if (sr != null) sr.sprite = spr;
                    }
                }
            }
            var passive = clone.GetComponent<PassiveButton>();
            if (passive != null)
            {
                passive.OnClick = new Button.ButtonClickedEvent();
                passive.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    // ⚠️ 2026-11-03：原版样式下 `_lightScreen` **不存在** ✗（那是 MOD 布局造的 ✓）
                    //   → 原版样式的 LightButton 改成打开「主界面样式」选择器 ✓
                    //     （不然点了什么都不发生 ✗ —— 这就是"Light 按钮也不对" ✓）
                    if (onClickOverride != null)
                    {
                        onClickOverride();
                        return;
                    }

                    __instance.ResetScreen();
                    _showingPanel = true;
                    if (_lightSubScreen != null) _lightSubScreen.SetActive(false);
                    if (_bgPanel != null) _bgPanel.Hide();
                    if (_lightScreen != null) _lightScreen.SetActive(true);
                }));
            }

            var fp = clone.transform.FindChild("FontPlacer");
            if (fp != null && fp.childCount > 0)
            {
                DateTime now = DateTime.Now;
                int m = now.Month;
                int d = now.Day;
                var tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
                if (tmp != null)
                {
                    if (m == 4 && d == 1) tmp.text = "A JOKE.";
                    else tmp.text = "LIGHT";
                }
                var trans = fp.GetChild(0).GetComponent<TextTranslatorTMP>();
                if (trans != null) trans.enabled = false;
            }
            // ★ 照 Nova `MainMenuPatch.cs:108` ✓：把自建按钮登记进原版 `mainButtons`
            //   （不登记的话：面板切换/重置时它不会跟着原版按钮一起处理 ✓）
            try
            {
                if (__instance.mainButtons != null)
                    __instance.mainButtons.Add(clone.GetComponent<PassiveButton>());
            }
            catch (Exception ex)
            {
                LightLogger.LogWarning($"[MainMenuPatch] mainButtons.Add 失败：{ex.Message}");
            }

            var scalerList = Object.FindObjectOfType<SlicedAspectScaler>();
            if (scalerList != null)
            {
                var scaled = clone.GetComponent<AspectScaledAsset>();
                if (scaled != null) scalerList.objectsToScale.Add(scaled);
            }
            if (passive != null) btns["LightButton"] = passive;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.CreateLightButton]", ex);
        }
    }

    private static void AdjustIcons()
    {
        try
        {
            var leftPanel = FindGO("LeftPanel");
            if (leftPanel == null) return;
            foreach (var btn in leftPanel.GetComponentsInChildren<PassiveButton>(true))
            {
                if (btn == null || btn.activeSprites == null) continue;
                var name = btn.name;
                bool shouldRotate = name != "LightButton" && name != "Inventory Button";
                bool shouldMove = name != "LightButton";
                var icon = btn.activeSprites.transform.FindChild("Icon");
                if (icon == null) continue;
                if (shouldRotate) icon.localEulerAngles -= new Vector3(0f, 0f, 10f);
                if (name != "LightButton") icon.localScale += new Vector3(0.12f, 0.12f, 0f);
                if (shouldMove)
                {
                    var asp = icon.GetComponent<AspectPosition>();
                    if (asp != null) { asp.DistanceFromEdge += new Vector3(-0.02f, 0.1f, 0f); asp.AdjustPosition(); }
                    else icon.localPosition += new Vector3(-0.02f, 0.1f, 0f);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.AdjustIcons]", ex);
        }
    }

    private static void ColorAllButtons()
    {
        try
        {
            var leftPanel = FindGO("LeftPanel");
            if (leftPanel == null) return;
            var pink = new Color32(255, 192, 203, 255);
            var purple = new Color32(148, 112, 219, 255);
            var clear = new Color(0f, 0f, 0f, 0f);

            foreach (var btn in leftPanel.GetComponentsInChildren<PassiveButton>(true))
            {
                if (btn == null) continue;
                var n = btn.name;
                if (n is "NewsButton" or "AcountButton" or "SettingsButton" or "LightButton")
                    FormatBtn(btn, pink, clear, Color.white, Color.white);
                else if (n is "CreditsButton" or "ExitGameButton")
                    FormatBtn(btn, purple, clear, Color.white, Color.white);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.ColorAllButtons]", ex);
        }
    }

    private static void FormatBtn(PassiveButton btn, Color inactive, Color active,
        Color inactText, Color actText)
    {
        try
        {
            HideShine(btn.activeSprites);
            HideShine(btn.inactiveSprites);
            if (btn.activeSprites != null)
            {
                var sr = btn.activeSprites.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = active.a == 0f
                        ? new Color(inactive.r, inactive.g, inactive.b, 1f) : active;
            }
            if (btn.inactiveSprites != null)
            {
                var sr = btn.inactiveSprites.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = inactive;
            }
            btn.activeTextColor = actText;
            btn.inactiveTextColor = inactText;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.FormatBtn]", ex);
        }
    }

    private static void HideShine(GameObject? parent)
    {
        try
        {
            if (parent == null) return;
            parent.transform.FindChild("Shine")?.gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.HideShine]", ex);
        }
    }

    private static void ApplyButtonEffects(MainMenuManager __instance)
    {
        try
        {
            ButtonBreathEffect.Init();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.ApplyButtonEffects]", ex);
        }
    }

    private static void SetupRightPanel(MainMenuManager __instance)
    {
        try
        {
            _rightPanel = FindGO("RightPanel");
            if (_rightPanel == null) return;

            var asp = _rightPanel.GetComponent<AspectPosition>();
            asp?.enabled = false;

            _rightPanelOp = _rightPanel.transform.localPosition;
            _rightPanel.transform.localPosition = _rightPanelOp + new Vector3(10f, 0f, 0f);

            var sr = _rightPanel.GetComponent<SpriteRenderer>();
            sr?.color = new Color32(173, 214, 255, 255);

        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.SetupRightPanel]", ex);
        }
    }

#if !DEBUG
    private static void RemoveFreePlayAndCenterHowToPlay(MainMenuManager __instance)
    {
        try
        {
            var free = __instance.freePlayButton;
            var how = __instance.howToPlayButton;

            if (how == null)
            {
                LightLogger.LogWarning("[MainMenuPatch] 找不到 howToPlayButton，跳过 Release 布局调整");
                return;
            }

            // 先算中点（必须趁 freePlay 还没被隐藏时取它的位置）
            var howPos = how.transform.localPosition;
            float centerX = howPos.x;
            if (free != null && free.transform.parent == how.transform.parent)
            {
                centerX = (howPos.x + free.transform.localPosition.x) * 0.5f;
            }

            // 去掉练习模式按钮（连带它的弹窗，避免残留）
            if (free != null)
            {
                free.gameObject.SetActive(false);
                LightLogger.Log("[MainMenuPatch] Release：已移除练习模式按钮");
            }
            var popover = FindGO("FreeplayPopover");
            if (popover != null) popover.SetActive(false);

            // 原版 OpenGameModeMenu() 是 gameModeButtons.SetActive(true)，打开面板时子物体的 OnEnable 会跑，
            // AspectPosition 会趁机按锚点把位置摆回去 —— 所以按钮自身若挂了 AspectPosition 先禁用，
            // 面板打开后再由 OpenGameModeMenu_Postfix 纠正一次。
            var asp = how.GetComponent<AspectPosition>();
            if (asp != null) asp.enabled = false;

            _howToPlayBtn = how;
            _howToPlayTargetX = centerX;

            how.transform.localPosition = new Vector3(centerX, howPos.y, howPos.z);
            // 关键：呼吸效果（ButtonBreathEffect）每帧都会把 localPosition 写回它记下的 BasePos，
            // 不刷新基准位置的话，这里改完下一帧就被摆回原地 —— 看起来就是"完全没动"。
            ButtonBreathEffect.RebasePosition(how.gameObject);
            LightLogger.Log($"[MainMenuPatch] Release：玩法说明已居中，x={centerX:F2}（原 x={howPos.x:F2}）父物体={how.transform.parent?.name}");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.RemoveFreePlayAndCenterHowToPlay]", ex);
        }
    }

    private static PassiveButton? _howToPlayBtn;
    private static float _howToPlayTargetX;
    private static int _howToPlayFixLogs;

    /// <summary>
    /// 面板每次打开都会跑一遍子物体 OnEnable，AspectPosition 会趁机把位置摆回锚点，
    /// 所以打开面板后立刻再纠正一次。
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu))]
    [HarmonyPostfix]
    public static void OpenGameModeMenu_Postfix()
    {
        EnforceHowToPlayX("面板打开");
    }

    /// <summary>兜底：面板开着时每帧盯一次位置，真有东西反复改也能压住。</summary>
    [HarmonyPatch(typeof(MainMenuManager), "LateUpdate")]
    [HarmonyPostfix]
    public static void HowToPlayLateUpdate_Postfix()
    {
        if (_howToPlayBtn == null) return;
        try
        {
            if (!_howToPlayBtn.gameObject.activeInHierarchy) return;
        }
        catch { return; }

        EnforceHowToPlayX("每帧");
    }

    private static GameObject? _ejectMenu;
    private static bool _ejectMenuLoggedHide;

    [HarmonyPatch(typeof(MainMenuManager), "LateUpdate")]
    [HarmonyPostfix]
    public static void SuppressEjectMenu_Postfix(MainMenuManager __instance)
    {
        try
        {
            if (_ejectMenu == null)
            {
                // ⚠️⚠️ 2026-11-03 性能修复（用户："进入主界面非常卡"）——
                //   原来这里**每帧**执行 `ui.GetComponentsInChildren<Transform>(true)` ✗：
                //   它把 MainUI **整棵层级**（数百个 Transform，含未激活）扫一遍 ✓
                //   一旦"没找到"（层级变了 / 名字改了 / 还没生成 ✗）就会**每帧**重扫 ✗✗
                //   → 主界面稳定掉帧，而且日志里什么都看不到（因为它是静默的 ✗）
                //
                //   ★ 现在：① 每 30 帧才扫一次 ✓ ② 最多扫 40 次就**放弃并记一行日志** ✓
                //     （真找不到时不该无限消耗，且要能一眼看出"没找到"✗）
                if (Time.frameCount - _ejectScanFrame < EjectScanInterval) return;
                _ejectScanFrame = Time.frameCount;

                if (_ejectScanTries >= EjectScanMaxTries)
                {
                    if (!_ejectScanGaveUp)
                    {
                        _ejectScanGaveUp = true;
                        LightLogger.LogWarning("[MainMenuPatch] 扫描 40 次仍未找到 EjectButtonMenu，" +
                                               "已停止扫描（原版可能换了层级；弹射按钮不做屏蔽了）");
                    }
                    return;
                }
                _ejectScanTries++;

                var ui = __instance.mainMenuUI;
                if (ui == null) return;

                // 递归查找（FindChild 取不到未激活的深层对象）；路径 MainUI/AspectScaler/EjectButtonMenu
                foreach (var tr in ui.GetComponentsInChildren<Transform>(true))
                {
                    if (tr == null || tr.name != "EjectButtonMenu") continue;
                    _ejectMenu = tr.gameObject;
                    LightLogger.Log("[MainMenuPatch] 已锁定 EjectButtonMenu，将永久隐藏");
                    break;
                }

                if (_ejectMenu == null) return;
            }

            if (!_ejectMenu.activeSelf) return;

            _ejectMenu.SetActive(false);
            if (!_ejectMenuLoggedHide)
            {
                _ejectMenuLoggedHide = true;
                LightLogger.Log("[MainMenuPatch] EjectButtonMenu 被原版激活 → 已同帧隐藏（不会显示出来）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuPatch.SuppressEjectMenu] {ex.Message}");
        }
    }

    // 弹射按钮扫描节流（见 SuppressEjectMenu_Postfix 的注释 ✓）
    private const int EjectScanInterval = 30;
    private const int EjectScanMaxTries = 40;
    private static int _ejectScanFrame = -100000;
    private static int _ejectScanTries;
    private static bool _ejectScanGaveUp;

    /// <summary>把玩法说明拉回中间；只有真的被改动时才写日志（最多 5 条，避免刷屏）。</summary>
    private static void EnforceHowToPlayX(string when)
    {
        try
        {
            if (_howToPlayBtn == null) return;

            var t = _howToPlayBtn.transform;
            var p = t.localPosition;
            if (Mathf.Abs(p.x - _howToPlayTargetX) < 0.001f) return;

            t.localPosition = new Vector3(_howToPlayTargetX, p.y, p.z);
            if (_howToPlayFixLogs < 5)
            {
                _howToPlayFixLogs++;
                LightLogger.Log($"[MainMenuPatch] Release：玩法说明位置被改回({when})，已重新居中 x={_howToPlayTargetX:F2}（当时 x={p.x:F2}）");
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuPatch] EnforceHowToPlayX: {ex.Message}");
        }
    }
#endif

    private static void SetupLightScreen(MainMenuManager __instance)
    {
        try
        {
            _lightScreen = Object.Instantiate(__instance.accountButtons,
                __instance.accountButtons.transform.parent);
            _lightScreen.name = "LightScreen";
            GetChild(_lightScreen.transform,1)?.gameObject.SetActive(true); // tint
            GetChild(_lightScreen.transform, 4)?.gameObject.SetActive(false);// 兑换奖励
            var titleEntry = GetChild(_lightScreen.transform, 0)?.GetChild(0);
            if (titleEntry != null)
            {
                var titleText = titleEntry.GetComponent<TextMeshPro>();
                if (titleText != null)
                {
                    titleText.text = "Light In The Dark";
                    titleText.fontSize = 4.5f;
                }
                var titleTrans = titleEntry.GetComponent<TextTranslatorTMP>();
                titleTrans?.enabled = false;
            }

            var child4 = GetChild(_lightScreen.transform, 4);
            if (child4 != null) Object.Destroy(child4.gameObject);

            HideButtonByText(_lightScreen, "兑换");

            var temp = GetChild(_lightScreen.transform, 3);
            if (temp == null) return;
            int index = 0;
            var mine = new List<GameObject>();

            void SetUpBtn(string text, System.Action clickAction)
            {
                GameObject obj = temp.gameObject;
                if (index > 0) obj = GameObject.Instantiate(obj, obj.transform.parent);
                var label = GetChild(obj.transform, 0)?.GetChild(0);
                if (label != null)
                {
                    var tmp = label.GetComponent<TextMeshPro>();
                    tmp?.text = text;
                    var tr = label.GetComponent<TextTranslatorTMP>();
                    tr?.enabled = false;
                }
                var pb = obj.GetComponent<PassiveButton>();
                if (pb != null)
                {
                    pb.OnClick = new Button.ButtonClickedEvent();
                    pb.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => clickAction()));
                }
                obj.transform.localPosition = new Vector3(
                    (index % 2 == 0) ? -1.45f : 1.45f,
                    0.98f - (index / 2) * 0.59f, 0f);
                obj.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
                mine.Add(obj);
                index++;
            }

            SetUpBtn("模组设置", () => LightLogger.LogWarning("[Light] 模组设置 - 待实现"));
            SetUpBtn("关于模组", () => HelpScreen.TryOpenHelpScreen());
            SetUpBtn("成就", () => LightLogger.LogWarning("[Light] 成就 - 待实现"));
            SetUpBtn("Discord", () => Application.OpenURL("https://discord.gg/"));

            // ★ 2026-11-03 用户要求：LightScreen 里一个「主界面样式」按钮 → 三选一窗口 ✓
            SetUpBtn("主界面样式", () => OpenMainMenuStyleWindow());
            SetUpBtn("更换背景图", () =>
            {
                if (_lightScreen != null) _lightScreen.SetActive(false);
                _bgPanel?.Show();
            });

            SetUpBtn("更多功能", () =>
            {
                if (_lightScreen != null) _lightScreen.SetActive(false);
                if (_lightSubScreen != null) _lightSubScreen.SetActive(true);
            });

            HideExtraButtons(_lightScreen, mine);

            var scalerList = Object.FindObjectOfType<SlicedAspectScaler>();
            if (scalerList != null)
                foreach (var asset in _lightScreen.GetComponentsInChildren<AspectScaledAsset>())
                    scalerList.objectsToScale.Add(asset);

            _lightScreen.SetActive(false);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.SetupLightScreen]", ex);
        }
    }

    private static void HideExtraButtons(GameObject root, List<GameObject> mine)
    {
        try
        {
            if (root == null) return;
            int hidden = 0;
            foreach (var pb in root.GetComponentsInChildren<PassiveButton>(true))
            {
                if (pb == null) continue;
                var go = pb.gameObject;
                if (mine.Contains(go)) continue;      // 我们自己建的
                if (!go.activeSelf) continue;         // 本来就关着

                string label = "";
                try
                {
                    var tmp = go.GetComponentInChildren<TextMeshPro>(true);
                    if (tmp != null) label = tmp.text;
                }
                catch { }

                go.SetActive(false);
                hidden++;
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuPatch.HideExtraButtons] {ex.Message}");
        }
    }

    private static void SetupSubScreen(MainMenuManager __instance)
    {
        try
        {
            var panel = new LightPanel(__instance, "LightSubScreen", "更多功能");

            panel.AddButton("功能B", () => LightLogger.Log("[Light] 功能B - 待实现"));
            panel.AddButton("功能C", () => LightLogger.Log("[Light] 功能C - 待实现"));
            panel.AddButton("功能D", () => LightLogger.Log("[Light] 功能D - 待实现"));

            if (_lightScreen != null)
                panel.AddBackButton(_lightScreen);

            panel.RegisterToScaler();
            panel.Hide();

            _lightSubScreen = panel.Panel;
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.SetupSubScreen]", ex);
        }
    }

    /// <summary>
    /// 主界面背景系统初始化。
    ///
    /// ⚠️ 已**弃用** <c>ImageGalleryPanel</c>（那个类还在，但不再被调用），原因见
    /// <see cref="BackgroundRenderer"/> 的类注释：
    ///   · 它用 <c>DontDestroyOnLoad</c> → 对象活过所有场景 → **背景外泄**；
    ///   · 它把位置写死成世界坐标 <c>z=520</c> → 相机一挪就跑出视锥 → **莫名其妙消失**。
    ///
    /// 现在素材来自磁盘目录（可放自己的图/视频），并使用新的渲染器 + 面板。
    /// </summary>
    /// <summary>
    /// 切换主界面样式（用户 2026-11-03 要求 ✓）：
    /// **先弹确认框** → 确定才写设置并**重载 MainMenu 场景**（取消则什么都不做 ✓）。
    ///
    /// ⚠️ 为什么要重载场景 ✗→✓：这两套样式（原版署名 / 美化版）对原版控件做了**破坏性**改动
    ///    （销毁 `AspectPosition`、重挂 `screenTint`、搬运 `LeftPanel` 子物体… ✗），
    ///    在同一个场景里"切回来"根本还原不干净 ✗ —— 用户要的就是"重载后按新样式重新搭一遍" ✓。
    ///    重载前必须先 <see cref="PrepareForSceneReload"/> 把持有场景对象的静态字段清空 ✓，
    ///    否则下一帧就会访问已销毁对象 ✗（AGENTS §4.6.1 的假 null 坑 ✓）。
    /// </summary>
    /// <summary>
    /// 打开「主界面样式」三选一窗口 ✓
    /// （主菜单 LightScreen 里那个按钮、以及**原版样式的 LightButton** 都调它 ✓）
    /// </summary>
    private static void OpenMainMenuStyleWindow()
    {
        try
        {
            if (_lightScreen != null) _lightScreen.SetActive(false);

            // ⚠️ 2026-11-03 用户："美化版你删了吧，bug 太多了" ✓ → 只剩 MOD / 原版 两项 ✓
            var w = HudUI.OpenButtonWindow("主界面样式",
                new HudUI.ButtonOption("MOD 样式（换贴图）", () => ApplyMainMenuStyle(MainButtonStyle.Mod)),
                new HudUI.ButtonOption("原版 + 署名（左侧大标题）", () => ApplyMainMenuStyle(MainButtonStyle.Vanilla)));

            // 窗口字体统一成 Mod 字体（Noto Sans SC ✓）
            w?.ApplyCjkFont();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.OpenMainMenuStyleWindow]", ex);
        }
    }

    /// <summary>
    /// **开子面板就藏装饰、回主屏再显示** ✓ —— 完全照 ToH
    /// （`TownOfHost/Patches/MainMenuManagerPatch.cs:100-122` ✓）：
    ///   · `OpenGameModeMenu / OpenOnlineMenu / OpenEnterCodeMenu / ClickBackOnline /
    ///      OpenAccountMenu / OpenCredits` 的 Postfix → 藏 ✓
    ///   · `ResetScreen` 的 Postfix → 显示 ✓
    /// ★ 这同时解决用户报的两件事 ✓：
    ///   ① "右侧层级还是错的" ✓（装饰不再压在面板上 ✓）
    ///   ② "打开 RightPanel 那几个按钮甚至还能点" ✓（面板打开时装饰是 SetActive(false) ✓，点不到 ✓）
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenEnterCodeMenu))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ClickBackOnline))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenAccountMenu))]
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenCredits))]
    [HarmonyPostfix]
    public static void HideDecorPostfix() => MainMenuStyleLayout.SetDecorVisible(false);

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ResetScreen))]
    [HarmonyPostfix]
    public static void ShowDecorPostfix() => MainMenuStyleLayout.SetDecorVisible(true);

    /// <summary>
    /// **诊断：设置菜单 `Open()` 的 NRE**（用户 2026-11-03 报 "点设置会抛异常" ✓）
    /// `OptionsMenuBehaviour.Open()` 里会直接用 `JoystickButton / TouchButton / JoystickSizeSlider /
    /// SoundSlider / MusicSlider / CensorChatButton` ✓ —— 依次判空并打日志 ✓，
    /// 这样日志里能直接看到**到底哪个字段是 null** ✓（而不是只看到一句 NRE 堆栈 ✗）。
    /// 只在真的有空字段时才打 ✓，平时零输出 ✓。
    /// </summary>
    /// <summary>
    /// **设置菜单 `ResetText()` 的 NRE 兜底** ✓
    ///
    /// 原版 `OptionsMenuBehaviour.ResetText()`（19.0）第一行就是：
    /// <code>
    ///   this.JoystickButton.transform.parent.GetComponentInChildren&lt;TextMeshPro&gt;().text = …;
    /// </code>
    /// —— **没有判空** ✗ → 只要那个 TMP 拿不到就 NRE ✗，而它被 `Open()` 调用 ✓
    ///   （用户 2026-11-03 报的"点设置抛 NullReferenceException，堆栈指向 Open()" ✓ 就是这个 ✓）
    ///
    /// 我们的处理 ✓：**能跑就照跑** ✓；只有真的缺目标时才 `return false` 跳过 ✗→✓
    ///   （跳过只影响那两行操作提示的本地化文字 ✓，不影响设置功能 ✓）
    /// </summary>
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.ResetText))]
    [HarmonyPrefix]
    public static bool SafeResetText(OptionsMenuBehaviour __instance)
    {
        try
        {
            var jb = __instance.JoystickButton;
            var tb = __instance.TouchButton;
            if (jb == null || tb == null) return false;

            var jTmp = jb.transform.parent != null ? jb.transform.parent.GetComponentInChildren<TextMeshPro>(true) : null;
            var tTmp = tb.transform.parent != null ? tb.transform.parent.GetComponentInChildren<TextMeshPro>(true) : null;

            if (jTmp == null || tTmp == null)
            {
                if (!_resetTextLogged)
                {
                    _resetTextLogged = true;
                    LightLogger.LogWarning($"[OptionsMenu] ResetText 的目标 TMP 缺失（joystick={jTmp != null} / touch={tTmp != null}）" +
                                           "→ 已跳过原版这一步，避免 NRE ✓");
                }
                return false;
            }

            return true;   // 正常路径：照原版跑 ✓
        }
        catch
        {
            return false;
        }
    }

    private static bool _resetTextLogged;

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Open))]
    [HarmonyPrefix]
    public static void DiagnoseOptionsOpen(OptionsMenuBehaviour __instance)
    {
        try
        {
            var missing = new List<string>();
            try { if (__instance.JoystickButton == null) missing.Add("JoystickButton"); } catch { missing.Add("JoystickButton(throw)"); }
            try { if (__instance.TouchButton == null) missing.Add("TouchButton"); } catch { missing.Add("TouchButton(throw)"); }
            try { if (__instance.JoystickSizeSlider == null) missing.Add("JoystickSizeSlider"); } catch { missing.Add("JoystickSizeSlider(throw)"); }
            try { if (__instance.SoundSlider == null) missing.Add("SoundSlider"); } catch { missing.Add("SoundSlider(throw)"); }
            try { if (__instance.MusicSlider == null) missing.Add("MusicSlider"); } catch { missing.Add("MusicSlider(throw)"); }
            try { if (__instance.CensorChatButton == null) missing.Add("CensorChatButton"); } catch { missing.Add("CensorChatButton(throw)"); }
            try { if (__instance.Tabs == null) missing.Add("Tabs"); } catch { missing.Add("Tabs(throw)"); }
            try { if (__instance.MenuButton == null) missing.Add("MenuButton"); } catch { missing.Add("MenuButton(throw)"); }

            if (missing.Count > 0)
                LightLogger.LogWarning($"[OptionsMenu.Diag] Open() 之前这些字段为 null → {string.Join(", ", missing)}" +
                                       $"（这基本就是 NRE 的来源 ✓；样式={AppearanceSettings.ButtonStyle}）");
        }
        catch { }
    }

    private static void ApplyMainMenuStyle(MainButtonStyle style)
    {
        try
        {
            string name = style switch
            {
                MainButtonStyle.Vanilla => "原版 + 署名",
                _ => "MOD 样式",
            };

            HudUI.OpenConfirmDialog(
                $"切换到「{name}」样式？\n\n确定后**重新加载主界面**才能生效 ✓",
                () => CommitMainMenuStyle(style));
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.ApplyMainMenuStyle]", ex);
        }
    }

    /// <summary>确认之后：落盘 → 清理静态引用 → 重载主界面场景 ✓</summary>
    private static void CommitMainMenuStyle(MainButtonStyle style)
    {
        try
        {
            AppearanceSettings.ButtonStyle = style;
            try { AppearanceSettings.Save(); }
            catch (Exception ex) { LightLogger.LogWarning($"[MainMenu] 保存样式失败：{ex.Message}"); }

            LightLogger.Log($"[MainMenu] 主界面样式 → {style}，即将重载 MainMenu 场景");
            PrepareForSceneReload();

            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.CommitMainMenuStyle]", ex);
        }
    }

    /// <summary>
    /// 公开入口：切样式后重载主界面 ✓（设置页签里的选择器要调它 ✓，
    /// 因为那个页面在**大厅设置**里，不在主菜单对象上 ✓）
    /// </summary>
    public static void ReloadForStyleChange()
    {
        try
        {
            PrepareForSceneReload();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.ReloadForStyleChange]", ex);
        }
    }

    /// <summary>
    /// 重载场景前把"指向场景对象"的静态字段全部清空 ✓。
    /// ⚠️ 漏一个就会出现 `MissingReferenceException` 刷屏 ✗（假 null 不是 null ✓）。
    /// </summary>
    private static void PrepareForSceneReload()
    {
        try { MainMenuStyleLayout.Clear(); } catch { }
        try { MainMenuButtonStyler.ResetForSceneChange(); } catch { }
        try { BackgroundPanel.ResetIfSceneChanged(); } catch { }

        _rightPanel = null;
        _lightScreen = null;
        _lightSubScreen = null;
        _bgPanel = null;
        _bgMoved = false;
        _bgMoveDelay = 0f;
        _bgInitialMoveDone = false;
        _hasBackground = false;
        _showingPanel = false;

        // ⚠️ 这几个不在本类作用域（弹射菜单的扫描状态 / Release 专用的玩法说明按钮）✗
        //    → 不清也没问题 ✓：重载后它们指向已销毁对象 ✓，而代码里一律用
        //      `== null` 判断 ✓（Unity 假 null 会被识别成 null ✓）→ 自然会重新扫描/重新找 ✓。

        _lastTickFrame = -1;
        _cachedSceneHandle = int.MinValue;
        _cachedSceneName = "";

        LightLogger.Log("[MainMenu] 已清理静态引用（重载前 ✓）");
    }

    private static void SetupGalleryScreen(MainMenuManager __instance)
    {
        try
        {
            // ⚠️ 2026-11-03：细分计时（MenuTimer 对照实验已证明这整段占 1558ms ✗，这里再往下切一刀）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long m0, m1;

            BackgroundStore.EnsureExtracted();
            int count = BackgroundStore.Scan(force: true).Count;
            _hasBackground = count > 0;
            m0 = sw.ElapsedMilliseconds;

            LightLogger.Log($"[Light] 背景素材 {count} 个（Image={BackgroundStore.ImageDir} / " +
                            $"Video={BackgroundStore.VideoDir}）");

            if (_bgPanel == null || !_bgPanel.IsAlive)
                _bgPanel = new BackgroundPanel();

            int layer = __instance.mainMenuUI != null
                ? __instance.mainMenuUI.layer
                : __instance.gameObject.layer;

            m1 = sw.ElapsedMilliseconds;
            BackgroundRenderer.OnMainMenuStart(layer);
            BackgroundRenderer.LogDiagnostics();
            long m2 = sw.ElapsedMilliseconds;

            // 主界面按钮样式（MOD / 原版 / 亚克力 + 逐按钮颜色）
            MainMenuButtonStyler.Refresh(__instance);
            MainMenuButtonStyler.Apply(force: true);
            long m3 = sw.ElapsedMilliseconds;

            // ★ 2026-11-03：三套样式（MOD / 原版+署名 / 美化版）的**布局**部分 ✓
            //   （贴图/配色由 MainMenuButtonStyler 管 ✓；标题、署名、四个按钮的排列在这里 ✓）
            MainMenuStyleLayout.Apply(__instance);
            long m4 = sw.ElapsedMilliseconds;

            LightLogger.Log($"[Light] 画廊阶段耗时：素材扫描 {m0}ms / 面板+相机准备 {m1 - m0}ms / " +
                            $"背景 {m2 - m1}ms / **按钮贴图+样式 {m3 - m2}ms** / 三态布局 {m4 - m3}ms / 合计 {m4}ms");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.SetupGalleryScreen]", ex);
        }
    }

    /// <summary>
    /// 按**显示文字**隐藏面板里的某个按钮（往上找带 PassiveButton 的那一层）。
    ///
    /// 为什么需要：面板是从原版 <c>accountButtons</c> 克隆的，里面自带一个「兑换奖励」按钮。
    /// 原代码靠 <c>GetChild(transform, 4)</c> 定位它，但**子物体序号会错位**，
    /// 而且越界时 <c>GetChild</c> 返回 null、上面两层空引用保护会静默跳过 →
    /// 那个按钮就留在界面上，和我们新加的按钮重叠。
    /// 按文字找不依赖序号，最稳。
    /// </summary>
    private static void HideButtonByText(GameObject root, string needle)
    {
        try
        {
            if (root == null || string.IsNullOrEmpty(needle)) return;
            foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp == null) continue;
                var s = tmp.text;
                if (string.IsNullOrEmpty(s) || !s.Contains(needle)) continue;

                // 从文字往上找按钮本体
                Transform? t = tmp.transform;
                for (int i = 0; i < 6 && t != null; i++)
                {
                    if (t.GetComponent<PassiveButton>() != null) break;
                    t = t.parent;
                }

                var go = (t != null && t.GetComponent<PassiveButton>() != null)
                    ? t.gameObject
                    : tmp.transform.parent?.gameObject;

                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    LightLogger.Log($"[Light] 已隐藏面板里多余的按钮「{needle}」（{go.name}）");
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[MainMenuPatch.HideButtonByText] {ex.Message}");
        }
    }

    private class LightPanel
    {
        private readonly GameObject _panel;
        private readonly Transform? _buttonTemplate;
        private int _index;

        public LightPanel(MainMenuManager instance, string panelName, string title)
        {
            try
            {
                _panel = GameObject.Instantiate(instance.accountButtons,
                    instance.accountButtons.transform.parent);
                _panel.name = panelName;

                var titleEntry = GetChild(_panel.transform, 0)?.GetChild(0);
                if (titleEntry != null)
                {
                    var titleText = titleEntry.GetComponent<TextMeshPro>();
                    titleText?.text = title;
                    var titleTrans = titleEntry.GetComponent<TextTranslatorTMP>();
                    titleTrans?.enabled = false;
                }

                var child4 = GetChild(_panel.transform, 4);
                if (child4 != null) Object.Destroy(child4.gameObject);

                _buttonTemplate = GetChild(_panel.transform, 3);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MainMenuPatch.LightPanel]", ex);
            }
        }

        public void AddButton(string text, System.Action clickAction, Vector3? position = null)
        {
            try
            {
                if (_buttonTemplate == null) return;
                GameObject obj = _buttonTemplate.gameObject;
                if (_index > 0) obj = Object.Instantiate(obj, obj.transform.parent);

                var label = GetChild(obj.transform, 0)?.GetChild(0);
                if (label != null)
                {
                    var tmp = label.GetComponent<TextMeshPro>();
                    tmp?.text = text;
                    var tr = label.GetComponent<TextTranslatorTMP>();
                    if (tr != null) tr.enabled = false;
                }

                var pb = obj.GetComponent<PassiveButton>();
                if (pb != null)
                {
                    pb.OnClick = new Button.ButtonClickedEvent();
                    pb.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => clickAction()));
                }

                obj.transform.localPosition = position ?? new Vector3(
                    (_index % 2 == 0) ? -1.45f : 1.45f,
                    0.98f - (_index / 2) * 0.59f, 0f);
                obj.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
                _index++;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MainMenuPatch.AddButton]", ex);
            }
        }

        public void AddBackButton(GameObject targetPanel, Vector3? position = null)
        {
            try
            {
                AddButton("返回", () =>
                {
                    _panel.SetActive(false);
                    targetPanel.SetActive(true);
                }, position ?? new Vector3(0f, -1.5f, 0f));
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MainMenuPatch.AddBackButton]", ex);
            }
        }

        public void RegisterToScaler()
        {
            try
            {
                var scalerList = Object.FindObjectOfType<SlicedAspectScaler>();
                if (scalerList != null)
                    foreach (var asset in _panel.GetComponentsInChildren<AspectScaledAsset>())
                        scalerList.objectsToScale.Add(asset);
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[MainMenuPatch.RegisterToScaler]", ex);
            }
        }

        public GameObject Panel => _panel;
        public void Show() => _panel.SetActive(true);
        public void Hide() => _panel.SetActive(false);
    }

    private static void MoveScreenTint(MainMenuManager __instance)
    {
        try
        {
            SpriteRenderer? tint = __instance.screenTint;
            if (tint == null)
            {
                var go = (FindGO("ScreenTint") ?? __instance.transform.FindChild("ScreenTint")?.gameObject) ?? (__instance.mainMenuUI.transform.FindChild("Tint")?.gameObject);
                if (go != null) tint = go.GetComponent<SpriteRenderer>();
            }

            if (tint != null && _rightPanel != null)
            {
                tint.transform.SetParent(_rightPanel.transform);
                tint.transform.localPosition = new Vector3(-0.0824f, 0.0513f,
                    tint.transform.localPosition.z);
                tint.transform.localScale = new Vector3(1f, 1.4f, 1f);
                tint.drawMode = SpriteDrawMode.Sliced;
                tint.size = new Vector2(5f, 4f);
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.MoveScreenTint]", ex);
        }
    }

    /// <summary>
    /// 每帧驱动入口 —— **由两个驱动源共同调用**：
    ///   ① 本方法（挂在 <c>MainMenuManager.LateUpdate</c> 的 Postfix）—— **只在 MainMenu 场景有效**
    ///   ② <see cref="Light.Utilities.LightTicker"/>（DontDestroyOnLoad 常驻）—— 任何场景都在
    ///
    /// ⚠️⚠️ **为什么必须加第二个驱动源**（2026-10-05 实机定位）：
    ///   `MainMenuManager` **只存在于 MainMenu 场景** —— MatchMaking（"本地"）的主控是
    ///   `MMOnlineManager`，FindAGame（"搜索游戏"）又是另一个。所以只靠这个补丁的话，
    ///   下面 `TickAll` 里那些 `sceneName == "MatchMaking"` / `"FindAGame"` 判断
    ///   **全是死代码**，在那两个场景里 `Tick()` 根本不会被调用 ——
    ///   用户报的「打开本地/搜索游戏后没有背景图、也没有音频」就是这么来的：
    ///   **不是被关掉了，是从没启动过。**
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), "LateUpdate")]
    [HarmonyPostfix]
    public static void LateUpdate() => TickAll("MainMenuManager.LateUpdate");

    /// <summary>上一帧跑过的帧号（"同一帧只跑一次"的守卫）。</summary>
    private static int _lastTickFrame = -1;

    // ⚠️ 2026-11-03 性能优化（用户："进入主界面非常卡"）：
    //   `LightTicker` 在**所有场景**每帧都调 `TickAll` ✗ → 原来每帧都做
    //   `SceneManager.GetActiveScene().name` ✗，而 `.name` 每次都**新建一个 string** ✗
    //   （每秒 60 次 GC 垃圾 ✓）。改成按 **scene.handle** 缓存 ✓：只有真换场景才取名字 ✓。
    private static int _cachedSceneHandle = int.MinValue;
    private static string _cachedSceneName = "";

    private static string SafeActiveSceneNameCached()
    {
        try
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.handle != _cachedSceneHandle)
            {
                _cachedSceneHandle = scene.handle;
                _cachedSceneName = scene.name;
            }
            return _cachedSceneName;
        }
        catch { return ""; }
    }

    // =====================================================================
    //  诊断（2026-10-05 用户报「本地 / 搜索游戏没有背景图」时加的）
    //  ⚠️ 目的：让用户跑一次就能从 LightLog.log 看出
    //    ① 这个每帧入口到底有没有被调用；② 是哪个驱动源调用的；③ 场景名对不对。
    //  节流：每个场景最多 6 条、每条至少间隔 300 帧（≈5 秒）。
    // =====================================================================
    private const int TickDiagMaxPerScene = 6;
    private const int TickDiagFrameGap = 300;
    private static string _tickDiagScene = "";
    private static int _tickDiagCount;
    private static int _tickDiagLastFrame = -100000;

    /// <summary>节流地打一行「TickAll 被调用了」。绝不能抛异常（它自己在每帧路径上）。</summary>
    private static void DiagTickAll(string driver, bool skippedByFrameGuard)
    {
        try
        {
            string scene = SafeActiveSceneName();

            // 换场景 → 计数归零并**立刻**打一条（保证"进这个场景了"一定能看到）
            bool sceneChanged = scene != _tickDiagScene;
            if (sceneChanged)
            {
                _tickDiagScene = scene;
                _tickDiagCount = 0;
                _tickDiagLastFrame = -100000;
            }

            if (!sceneChanged)
            {
                if (_tickDiagCount >= TickDiagMaxPerScene) return;
                if (UnityEngine.Time.frameCount - _tickDiagLastFrame < TickDiagFrameGap) return;
            }

            _tickDiagLastFrame = UnityEngine.Time.frameCount;
            _tickDiagCount++;

            var sceneName = scene;
            bool isMainOrMatch = sceneName == "MainMenu" || sceneName == "MatchMaking";

            LightLogger.Log($"[TickAll#{_tickDiagCount}/{TickDiagMaxPerScene}] driver={driver} " +
                            $"scene='{sceneName}' isMainOrMatch={isMainOrMatch} " +
                            $"frame={UnityEngine.Time.frameCount} skippedByFrameGuard={skippedByFrameGuard} " +
                            $"tickerAlive={Light.Utilities.LightTicker.IsRunning} " +
                            $"bgSelected='{BackgroundStore.Selected}' bgFail='{BackgroundRenderer.LastFailReason}'");
        }
        catch { }
    }

    /// <summary>当前场景名（读不到就返回 "?"，绝不抛）。</summary>
    private static string SafeActiveSceneName()
    {
        // ⚠️ 2026-11-03：改走缓存版（原来每帧 `.name` 都新建 string ✗，见 SafeActiveSceneNameCached 的说明）
        try { return SafeActiveSceneNameCached(); }
        catch { return "?"; }
    }

    /// <summary>真正干活的每帧入口（两个驱动源共用）。</summary>
    /// <param name="driver">谁调进来的（只用于诊断日志）。</param>
    public static void TickAll(string driver = "unknown")
    {
        // ⚠️ 同一帧只跑一次：MainMenu 场景里补丁和 LightTicker 都会调进来，
        //    不去重的话 TickInput() 那种鼠标轮询会被处理两次 → 一次点击算两下。
        if (UnityEngine.Time.frameCount == _lastTickFrame)
        {
            DiagTickAll(driver, skippedByFrameGuard: true);
            return;
        }
        _lastTickFrame = UnityEngine.Time.frameCount;
        DiagTickAll(driver, skippedByFrameGuard: false);

        try
        {
            var sceneName = SafeActiveSceneNameCached();
            // ⚠️ 这三个场景是"主界面家族"：MainMenu（主菜单）、MatchMaking（本地/连线区选择）、
            //    FindAGame（搜索游戏）。**背景图在这三个里都要显示**（用户要求），
            //    而且音频要连续 —— 见 BackgroundRenderer.IsInMenuScene() 的注释。
            //    注意这条判断在 TickAll 里是**死代码**的历史已经结束：现在由
            //    LightTicker（DontDestroyOnLoad 常驻）驱动，任何场景都会调进来。
            bool isMainOrMatch = sceneName == "MainMenu" || sceneName == "MatchMaking";

            // 自定义背景（新实现）。
            // 旧实现靠"DontDestroyOnLoad + 世界坐标写死 z=520"，
            // 于是必须在这里补一堆"进别的场景就 SetActive(false) + 缩到 0.0001"的补丁去藏，
            // 而且相机一挪动它就跑出视锥 → "莫名其妙消失"。
            // 现在 BackgroundRenderer 不用 DontDestroyOnLoad（场景卸载自动销毁，不可能外泄），
            // 并且每帧跟随相机（永远在视野里、永远在所有 UI 后面）。见该类注释。
            if (!isMainOrMatch)
            {
                BackgroundRenderer.Shutdown();
                _showingPanel = false;
                BackgroundPanel.ResetIfSceneChanged();
                MainMenuButtonStyler.ResetForSceneChange();
                return;
            }

            // ① 背景：三个场景都要（这是用户要的那一件事）
            BackgroundRenderer.Tick();

            // ② 下面这些是**主界面专有**的改造 —— 右侧面板滑入 / Light 屏 / 按钮贴图 /
            //    呼吸效果 /「更换背景图」面板 / 把原版 BackgroundTexture 推走。
            //
            //    ⚠️⚠️ 为什么在这里**刻意收窄**（2026-10-04 的实测教训，别删这段注释）：
            //    在 MatchMaking 里每帧跑这些曾导致「创建游戏连线区失败」——
            //    原版建房流程是「发 HostGame → 等服务器回 GameId，15 秒超时」
            //    （InnerNetClient.WaitWithTimeout），主线程被拖住就可能等不到回包
            //    → GameId 恒为 0 → LastCustomDisconnect = "创建游戏连线区失败…"。
            //    在那两个场景里这些调用本来也全是空转（面板不存在、按钮集合为空、
            //    呼吸效果没注册过任何按钮），所以收窄既不影响功能、又不会重蹈覆辙。
            if (sceneName != "MainMenu") return;

            BackgroundPanel.Active?.TickInput();

            // 按钮样式：设置变了才真的重刷（Apply 内部有变化检测，每帧调代价极小）
            MainMenuButtonStyler.Apply();

            // 模态遮罩：把不属于我们窗口的原版控件临时禁用（见 UiModalGuard 的长注释，
            // 里面有"为什么碰撞盒没用"和"为什么不能用 Harmony prefix"的完整证据）。
            UiModalGuard.Sweep();

            ButtonBreathEffect.Update();

            // ⚠️⚠️ 这里**绝对不能**调 _bgPanel?.Hide()。
            //   「更换背景图」面板显示期间我们故意把 _showingPanel 置 false
            //   （好让右侧面板滑走腾地方），如果这里每帧再 Hide 一次，
            //   面板刚打开就会被立刻关掉。
            //   要关面板请走 ShowRightPanel / HideRightPanel / LightButton 那几处**显式**调用。
            if (!_showingPanel && (_bgPanel == null || !_bgPanel.IsShown))
            {
                if (_lightScreen != null && _lightScreen.activeSelf)
                    _lightScreen.SetActive(false);
                if (_lightSubScreen != null && _lightSubScreen.activeSelf)
                    _lightSubScreen.SetActive(false);
            }

            if (_rightPanel != null)
            {
                var target = _rightPanelOp + new Vector3(_showingPanel ? 0f : 10f, 0f, 0f);
                var lerp = Vector3.Lerp(_rightPanel.transform.localPosition, target,
                    Time.deltaTime * (_showingPanel ? 3f : 2f));
                if (_showingPanel
                    ? _rightPanel.transform.localPosition.x > _rightPanelOp.x
                    : _rightPanel.transform.localPosition.x < _rightPanelOp.x + 9f)
                    _rightPanel.transform.localPosition = lerp;
            }

            // ★ 2026-11-03 用户："原版情况下这个背景不该划出去" ✓
            //   → 「原版」样式下**不许再挪原版背景**（那段上移逻辑只属于 MOD / 美化版的观感 ✓）
            // ★ 2026-11-03 用户："LightScreen 你没关 logo" ✓
            //   每帧根据 LightScreen / 子屏是否可见来开关装饰 ✓（覆盖所有打开/关闭路径 ✓，不用到处埋调用 ✓）
            try
            {
                bool screenOpen = (_lightScreen != null && _lightScreen.activeSelf)
                               || (_lightSubScreen != null && _lightSubScreen.activeSelf)
                               || (_bgPanel != null && _bgPanel.IsShown);
                MainMenuStyleLayout.SetDecorVisible(!screenOpen);
            }
            catch { }

            if (!_bgMoved && AppearanceSettings.ButtonStyle != MainButtonStyle.Vanilla)
            {
                var bg = FindGO("BackgroundTexture");
                if (bg != null && bg.activeSelf)
                {
                    _bgMoveDelay += Time.deltaTime;
                    if (_bgMoveDelay > 3f)
                    {
                        var pos = bg.transform.position;
                        bg.transform.position = Vector3.Lerp(pos,
                            new Vector3(pos.x, 7.1f, pos.z), Time.deltaTime * 1.4f);
                        if (pos.y > 7f)
                        {
                            _bgMoved = true;
                            _bgInitialMoveDone = true;
                            bg.SetActive(false);
                        }
                    }
                }
            }

            if (_bgMoved && _bgPanel != null && !_bgPanel.IsAlive)
                _bgPanel = new BackgroundPanel();
        }
        catch (System.Exception)
        {
            _rightPanel = null;
            _lightScreen = null;
            _lightSubScreen = null;
            _bgPanel = null;
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), "OpenGameModeMenu")]
    [HarmonyPatch(typeof(MainMenuManager), "OpenAccountMenu")]
    [HarmonyPatch(typeof(MainMenuManager), "OpenCredits")]
    [HarmonyPrefix, HarmonyPriority(0)]
    public static void ShowRightPanel()
    {
        try
        {
            _showingPanel = true;
            if (_lightScreen != null) _lightScreen.SetActive(false);
            if (_lightSubScreen != null) _lightSubScreen.SetActive(false);
            if (_bgPanel != null) _bgPanel.Hide();
        }
        catch (System.Exception)
        {
        }
    }

    [HarmonyPatch(typeof(OptionsMenuBehaviour), "Open")]
    [HarmonyPatch(typeof(AnnouncementPopUp), "Show")]
    [HarmonyPrefix, HarmonyPriority(0)]
    public static void HideRightPanel()
    {
        try
        {
            _showingPanel = false;
            if (_lightScreen != null) _lightScreen.SetActive(false);
            if (_lightSubScreen != null) _lightSubScreen.SetActive(false);
            _bgPanel?.Hide();
            DestroyableSingleton<AccountManager>.Instance
                ?.transform.FindChild("AccountTab/AccountWindow")?.gameObject.SetActive(false);
        }
        catch (System.Exception)
        {
        }
    }

    private static void SetupExtraButtons(MainMenuManager __instance)
    {
        try
        {
            if (__instance.quitButton == null) return;
            var template = __instance.quitButton.gameObject;
            var parent = template.transform.parent;

            for (int i = 0; i < 4; i++)
            {
                var old = FindGO($"ExtraButton{i}");
                if (old != null) Object.Destroy(old);
            }

            var defs = new (string label, Action action)[]
            {
                ("检查更新", CheckForUpdate),
                ("Github", () => Application.OpenURL("https://github.com/Moon-Scar-Studio/LightInDark")),
                ("模组官网", () => Application.OpenURL("https://lid.moonscar.cn")),
                (DataManager.Settings.Language.CurrentLanguage==SupportedLangs.SChinese 
                ||
                DataManager.Settings.Language.CurrentLanguage==SupportedLangs.TChinese
                ?
                "QQ群"
                :
                "Discord", 
                () => 
                {
                    bool isQQGroup = DataManager.Settings.Language.CurrentLanguage==SupportedLangs.SChinese || 
                    DataManager.Settings.Language.CurrentLanguage==SupportedLangs.TChinese;
                    if (isQQGroup)
                    {
                        Application.OpenURL("https://qm.qq.com/q/mRsF2k5sUE");
                    }
                    else
                    {
                        LightUtils.ShowCustomDisconnectWindow("TODO : No DC");
                    }
                }),
            };
            for (int i = 0; i < defs.Length; i++)
            {
                var button = Object.Instantiate(template, parent);
                button.name = $"ExtraButton{i}";
                button.SetActive(true);
                var condHide = button.GetComponent<ConditionalHide>();
                if (condHide != null) Object.Destroy(condHide);

                var fp = button.transform.FindChild("FontPlacer");
                if (fp != null && fp.childCount > 0)
                {
                    var tmp = fp.GetChild(0).GetComponent<TextMeshPro>();
                    tmp?.text = defs[i].label;
                    var tr = fp.GetChild(0).GetComponent<TextTranslatorTMP>();
                    tr?.enabled = false;
                }

                var pb = button.GetComponent<PassiveButton>();
                if (pb != null)
                {
                    int itemIndex = i;
                    pb.OnClick = new Button.ButtonClickedEvent();
                    pb.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => defs[itemIndex].action()));
                }

                var asp = button.GetComponent<AspectPosition>();
                if (asp != null)
                {
                    int col = i % 2;
                    int row = i / 2 + 1;
                    asp.anchorPoint = new Vector2(col == 0 ? 0.42f : 0.58f, 0.5f - 0.08f * row);
                    asp.AdjustPosition();
                }

                var scalerList = Object.FindObjectOfType<SlicedAspectScaler>();
                if (scalerList != null)
                {
                    var scaled = button.GetComponent<AspectScaledAsset>();
                    if (scaled != null) scalerList.objectsToScale.Add(scaled);
                }
            }
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.SetupExtraButtons]", ex);
        }
    }

    private static void CheckForUpdate()
    {
        string result = VersionMaker.CheckForUpdate();
        switch (result)
        {
            case "need update":
                VersionMaker.StartUpdateProcess();
                break;
            case "no need":
                LightUtils.ShowCustomDisconnectWindow("当前已是最新版本。");
                break;
            case "not installed":
                LightUtils.ShowCustomDisconnectWindow("模组未安装。你咋点的检查更新并且看到这个窗口？\n也有可能是你把文件改名了，请改回去。");
                break;
            case "check error":
                LightUtils.ShowCustomDisconnectWindow("更新检查失败。\n请将游戏目录下的Light.log发送给开发者或者QQ群中。\n不要直接将此界面截图/拍照给其他人。");
                break;
            case "path error":
                LightUtils.ShowCustomDisconnectWindow($"未找到更新检查器！\n请检查这个路径下有没有 {VersionMaker.UpdaterExeName} ：\n{VersionMaker.UpdaterExePath}");
                break;
            case "github error":
                LightUtils.ShowCustomDisconnectWindow("无法访问GitHub来检查版本！请检查您的网络状况。\n当然，最坏的结果是我们删仓跑路了。");
                break;
            default:
                LightUtils.ShowCustomDisconnectWindow("未知返回值。\n请将游戏目录下的Light.log发送给开发者或者QQ群中。\n不要直接将此界面截图/拍照给其他人。");
                break;
        }
    }

    /// <summary>监听场景切换事件（自动注册为 SceneChangedEvent 监听者）。
    ///  ⚠️ 背景的"跨场景外泄"已由 <see cref="BackgroundRenderer"/> 从根上解决
    ///  （不用 DontDestroyOnLoad，对象随场景销毁），这里只负责收尾清状态。</summary>
    public static void OnSceneChanged(SceneChangedEvent ev)
    {
        try
        {
            if (ev == null) return;
            // 和 TickAll 保持同一套"主界面家族"判断（MainMenu / MatchMaking / FindAGame），
            // 否则切到 FindAGame 时会把刚建好的背景 Shutdown 掉。
            bool isMainOrMatch = ev.NextSceneName == "MainMenu" || ev.NextSceneName == "MatchMaking";

            if (!isMainOrMatch)
            {
                // 离开主菜单：把背景与面板全部拆掉（对象本来就会随场景销毁，这里是双保险）
                BackgroundRenderer.Shutdown();
                _bgPanel?.Hide();
                _bgPanel = null;
                _showingPanel = false;
                // 遮罩一定要清，否则会把新场景的按钮全锁死
                UiModalGuard.Clear();
                return;
            }

            // 进入主菜单：清掉上一次残留的遮罩登记
            UiModalGuard.Clear();
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[MainMenuPatch.OnSceneChanged]", ex);
        }
    }

    /// <summary>场景切换监听（使用 SceneChangedEvent 事件）：检测切换后的场景是否为 MatchMaking。</summary>
    /// <remarks>方法体只保留检测，其余请在此方法内自行补充。</remarks>
    public static void OnMatchMakingScene(SceneChangedEvent ev)
    {
        bool isMatchMaking = ev != null && ev.NextSceneName == "MatchMaking";
        // ========== 以下由你填写：isMatchMaking 为 true 时执行 MatchMaking 相关逻辑 ==========
    }

    /// <summary>克隆主界面临时标题（LOGO-AU）挂到主菜单根，替换为模组 Logo。在隐藏 LeftPanel 前调用。</summary>
    private static void CloneTitleToMainMenu(MainMenuManager __instance)
    {
        try
        {
            var leftPanel = FindGO("LeftPanel");
            var sizer = leftPanel?.transform.FindChild("Sizer");
            var logo = sizer?.FindChild("LOGO-AU");
            if (logo == null)
            {
                LightLogger.LogWarning("[Light] 未找到 LOGO-AU，跳过标题克隆");
                return;
            }

            var title = Object.Instantiate(logo.gameObject, __instance.transform);
            title.name = "ModTitle";
            foreach (var trans in title.GetComponentsInChildren<AspectSize>(true))
                Object.Destroy(trans);
            foreach (var trans in title.GetComponentsInChildren<AspectPosition>(true))
                Object.Destroy(trans);

            var sr = title.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var logoSp = ResourceHelper.LoadSpriteFromResource("Light.Resources.Logo.LightInDark.png");
                if (logoSp != null) sr.sprite = logoSp;
            }

            title.transform.SetParent(__instance.transform, false);
            title.transform.localPosition = new Vector3(-3.3f, 1.98f, -4);
            title.transform.localScale = new Vector3(0.25f, 0.25f, 1f);

            LightLogger.Log("[Light] 已克隆主标题并挂到主界面");
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[Light] CloneTitleToMainMenu", ex);
        }
    }

}
