using AmongUs.Data;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Config;
using Light.UI;
using Light.UI.Help;
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
            var bg = FindGO("BackgroundTexture");
            bg?.SetActive(!_bgInitialMoveDone);

            var btns = FindButtons();

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
            CloneTitleToMainMenu(__instance);
            if (leftPanel != null)
            {
                var mainButtons = leftPanel.transform.FindChild("Main Buttons");
                leftPanel.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
                for (int i = 0; i < leftPanel.transform.childCount; i++)
                    leftPanel.transform.GetChild(i).SetParent(leftPanel.transform.parent);
                if (mainButtons != null)
                    mainButtons.transform.localPosition = new Vector3(-4.0f, 0f, 0f);
                leftPanel.SetActive(false);
            }
            FindGO("Divider")?.SetActive(false);
            SetupExtraButtons(__instance);
            ApplyButtonEffects(__instance);
            SetupRightPanel(__instance);
#if !DEBUG
            RemoveFreePlayAndCenterHowToPlay(__instance);   // Release：去掉练习模式，玩法说明居中
#endif
            SetupLightScreen(__instance);
            SetupSubScreen(__instance);
            SetupGalleryScreen(__instance);
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
            foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>())
                if (obj.name is "FreePlayButton" or "HowToPlayButton")
                    obj.transform.localPosition = new Vector3(0f, -20f, 0f);

            LightLogger.LogWarning("[Light.UI] === 布局完成 ===");

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

    private static void CreateLightButton(MainMenuManager __instance, Dictionary<string, PassiveButton> btns, float height)
    {
        try
        {
            if (!btns.TryGetValue("SettingsButton", out var settings)) return;
            var clone = GameObject.Instantiate(settings.gameObject, settings.transform.parent);
            clone.name = "LightButton";
            clone.transform.localPosition =
                settings.transform.localPosition - new Vector3(0f, height, 0f);

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
                        icon.localScale = new Vector3(0.1f, 0.1f, 1f);
                        var sr = icon.GetComponent<SpriteRenderer>();
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
    private static void SetupGalleryScreen(MainMenuManager __instance)
    {
        try
        {
            BackgroundStore.EnsureExtracted();
            int count = BackgroundStore.Scan(force: true).Count;
            _hasBackground = count > 0;

            LightLogger.Log($"[Light] 背景素材 {count} 个（Image={BackgroundStore.ImageDir} / " +
                            $"Video={BackgroundStore.VideoDir}）");

            if (_bgPanel == null || !_bgPanel.IsAlive)
                _bgPanel = new BackgroundPanel();

            int layer = __instance.mainMenuUI != null
                ? __instance.mainMenuUI.layer
                : __instance.gameObject.layer;

            BackgroundRenderer.OnMainMenuStart(layer);
            BackgroundRenderer.LogDiagnostics();

            // 主界面按钮样式（MOD / 原版 / 亚克力 + 逐按钮颜色）
            MainMenuButtonStyler.Refresh(__instance);
            MainMenuButtonStyler.Apply(force: true);
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
        try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
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
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
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

            if (!_bgMoved)
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
