using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Light.Utilities;
using LightInDark.Core;
using TMPro;
using UnityEngine;

namespace Light.Patches;

/// <summary>
/// 自定义启动加载页（Windows 11 安装界面风格）。
/// - 背景：淡金色径向渐变光斑，偏左上分布，缓慢漂移/晃动/旋转；每个光斑周期性
///   分裂出一个小光斑飘散淡出（Fluent Bloom 流动感）。
/// - LOGO：淡入后持续上下浮动。
/// - 进度：多个加载步骤在固定锚点依次完成；步骤文字（含本步进度 (NN%) 同行显示）；
///   完成项缩小 0.5 并下移、旧完成项让位；全部完成后快速收拢淡出。
/// - 提示：固定 8 句随机取一句，每 2 秒更换且不重复，切换时淡出/淡入。
/// - 结束：绿色"加载成功！" 淡入→停留→下移出屏；点击任意处进入游戏（平滑呼吸闪烁）。
/// LOGO 浮动 / 背景光斑 / 提示轮换由独立协程每帧驱动，不随主流程 yield 卡顿。
/// 进入游戏仍走原接口：sceneChanger.AllowFinishLoadingScene() + startedSceneLoad = true。
/// </summary>
[HarmonyPriority(Priority.HigherThanNormal)]
[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
public static class LoadPatch
{
    // ======================= 可调参数 =======================

    // 总体
    private const float MinLoadTime = 6f;          // 加载动画至少 6 秒
    // 完成项间距按尺度区分：当前项是全尺寸大字，下移需更宽间距；
    // 完成项彼此都是缩小到 0.5 的小字，间距收紧避免空洞。
    private const float FirstRowGap = 0.9f;        // 当前项(全尺寸) → 第一完成项 的下移间距
    private const float RowGap = 0.32f;            // 完成项(缩小0.5) 彼此之间的下移间距
    private const float FadeInDuration = 0.25f;    // 当前进度项淡入时长
    private const float CompletedHold = 0.15f;     // 步骤完成后的停顿
    private const float MoveDuration = 0.15f;      // 完成项下移/缩放时长（摞文字速度快些）
    private const float SuccessFadeIn = 0.3f;      // "加载成功！"淡入时长
    private const float SuccessHold = 0.8f;        // "加载成功！"停留时长
    private const float TipInterval = 2f;          // 提示文字更换间隔
    private const float TipFade = 0.25f;           // 提示文字淡出/淡入时长

    // LOGO
    private const float LogoFadeIn = 0.6f;         // LOGO 淡入时长
    private const float LogoFloatCycle = 2f;       // 浮动周期
    private const float LogoFloatAmp = 0.025f;     // 浮动幅度（±，轻微）

    // 点击提示呼吸（平滑正弦，周期内无跳变）
    private const float ClickGlowCycle = 1.5f;     // 闪烁周期

    // 背景光斑
    private const int BlobCount = 4;
    private const float BlobMinCycle = 20f;        // 漂移最短周期
    private const float BlobMaxCycle = 40f;        // 漂移最长周期
    private const float BlobAlpha = 0.14f;         // 光斑透明度（低）
    private const float SplitMinInterval = 5f;     // 分裂最短间隔
    private const float SplitMaxInterval = 9f;     // 分裂最长间隔
    private const float SplitDuration = 3f;        // 一次分裂（子光斑飘散淡出）时长

    // 颜色
    private static readonly Color BlobColorA = new(0.96f, 0.83f, 0.54f, 1f);   // #F5D48A
    private static readonly Color BlobColorB = new(1.00f, 0.91f, 0.69f, 1f);   // #FFE7B0
    private static readonly Color ProgressGray = new(0.72f, 0.72f, 0.74f, 1f); // 进度文字初始灰白
    private static readonly Color ProgressGreen = new(0.486f, 0.988f, 0f, 1f); // #7CFC00
    private static readonly Color ClickGold = new(0.96f, 0.83f, 0.54f, 1f);     // #F5D48A 点击提示

    // 布局
    private static readonly Vector3 LogoPos = new(0f, 0.5f, -5f);
    private static readonly Vector3 StepAnchor = new(0f, -0.55f, -10f);   // 步骤文字锚点（LOGO 正下方）
    private static readonly Vector3 TipPos = new(0f, -3.6f, -10f);
    private static readonly Vector3 VersionPos = new(4.5f, -3.2f, -10f);
    private static readonly Vector3 ClickPos = new(0f, -1.0f, -10f);       // 点击进入提示（往上移）

    // ======================= 写死的文字（不本地化） =======================

    private static readonly string[] LoadStepTexts =
    [
        "正在加载资源...",
        "正在解压资源包...",
        "正在初始化模组核心...",
        "正在注册组件...",
        "正在配置 Harmony 补丁...",
        "正在加载语言数据...",
        "正在准备游戏环境...",
        "正在校准模组参数...",
        "正在建立通信管道...",
    ];

    private static readonly string[] TipTexts =
    [
        "提示：本模组与大多数模组不兼容",
        "提示：你可以在%userprofile%/AppData/LocalLow/Innersloth/Among Us/LightInDark更改部分模组配置",
        "提示：大厅中，按下Shift可以无视碰撞箱。按下左/右Ctrl时，可以关闭左/右引擎的火",
        "感谢您选择Light In Dark!",
        "不要相信T氏的话",
        "在主界面按下A什么也不会发生",
        "月痕制作组向你致意",
        "祝你好运！",
    ];

    private const string LoadSuccessTextCN = "加载成功！";
    private const string ClickToEnterTextCN = "-- 点击任意处进入游戏 --";

    // ======================= 运行时状态 =======================

    private static SpriteRenderer? logo;
    private static readonly List<SpriteRenderer> blobs = new();
    private static readonly List<SpriteRenderer> subBlobs = new();   // 分裂子光斑（与 blobs 同索引）
    private static readonly List<BlobAnim> blobAnims = new();

    private static TextMeshPro? currentStepText;   // 当前步骤文字（含百分比同行）
    private static TextMeshPro? tipText;
    private static TextMeshPro? successText;
    private static TextMeshPro? clickText;
    private static TextMeshPro? versionText;
    private static readonly List<TextMeshPro> completedSteps = new();
    private static readonly List<Tweener> moveTweens = new();         // 让位/移出动画

    // 独立环境协程共享状态
    private static bool ambientRunning;            // 环境协程是否在跑
    private static int tipState;                   // 0=空闲 1=淡出 2=淡入
    private static float tipFadeElapsed;
    private static float tipTimer;
    private static int lastTipIndex;

    private static bool loaded;
    private static bool cachedDoneLoadingRefData;

    /// <summary>外部设置当前进度文本（兼容旧接口）。</summary>
    public static string LoadingText
    {
        set
        {
            try
            {
                if (currentStepText != null) currentStepText.text = value;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[LoadPatch.set]", ex);
            }
        }
    }

    // ======================= 轻量动画辅助 =======================

    /// <summary>手写缓动：驱动一个值平稳到达目标（InOutSine）。</summary>
    private sealed class Tweener
    {
        public float From;
        public float To;
        public float Duration;
        public float Elapsed;
        public Action<float>? Set;    // 每帧写入
        public Action<float>? OnDone; // 完成回调（传入终值）

        public bool Update()
        {
            Elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(Elapsed / Mathf.Max(0.0001f, Duration));
            float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t); // InOutSine
            float v = Mathf.Lerp(From, To, eased);
            Set?.Invoke(v);
            if (t >= 1f)
            {
                OnDone?.Invoke(v);
                return true;
            }
            return false;
        }
    }

    private static void CleanupTweens()
    {
        for (int i = moveTweens.Count - 1; i >= 0; i--)
        {
            if (moveTweens[i].Update())
                moveTweens.RemoveAt(i);
        }
    }

    // ======================= Harmony =======================

    public static bool Prefix(SplashManager __instance)
    {
        // 设置中开启"跳过加载动画"时：不拦截，走原版静默加载
        if (LightPlugin.LightSettingsData.SkipLoadAnimation)
            return true;

        try
        {
            cachedDoneLoadingRefData |= __instance.doneLoadingRefdata;
            __instance.doneLoadingRefdata = false;

            if (cachedDoneLoadingRefData
                && !__instance.startedSceneLoad
                && Time.time - __instance.startTime > Mathf.Max(__instance.minimumSecondsBeforeSceneChange, 1f)
                && !loaded)
            {
                loaded = true;
                __instance.StartCoroutine(CoLoadLight(__instance).WrapToIl2Cpp());
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Postfix(SplashManager __instance)
    {
        try
        {
            __instance.doneLoadingRefdata = cachedDoneLoadingRefData;
        }
        catch (Exception)
        {
        }
    }

    // ======================= 独立环境协程（LOGO 浮动 + 光斑移动/分裂 + 提示轮换） =======================

    /// <summary>每帧驱动 LOGO 浮动、背景光斑漂移/晃动/分裂、提示文字轮换。
    /// 与主流程协程相互独立，任何 yield 都不影响它，保证移动平滑不卡顿。</summary>
    private static IEnumerator CoAmbient()
    {
        while (ambientRunning)
        {
            // LOGO 浮动
            if (logo != null)
            {
                logo.transform.localPosition = LogoPos + new Vector3(
                    0f,
                    Mathf.Sin(Time.time / LogoFloatCycle * Mathf.PI * 2f) * LogoFloatAmp,
                    0f);
            }

            // 背景光斑漂移/晃动/旋转 + 分裂子光斑
            for (int b = 0; b < blobAnims.Count; b++)
            {
                var a = blobAnims[b];
                float t = Time.time / a.cycle * Mathf.PI * 2f + a.phase;
                float scaleT = Time.time / a.scaleCycle * Mathf.PI * 2f + a.scalePhase;

                // 主漂移 + 不规则晃动（双频率叠加）
                var pos = a.origin + new Vector3(
                    Mathf.Sin(t) * 0.8f + Mathf.Sin(t * 1.7f + a.phase2) * 0.3f,
                    Mathf.Cos(t * 1.13f) * 0.5f + Mathf.Cos(t * 2.3f + a.phase2 * 2f) * 0.25f,
                    0f);
                blobs[b].transform.localPosition = pos;
                float s = a.scaleBase * (1f + Mathf.Sin(scaleT) * 0.15f);
                blobs[b].transform.localScale = Vector3.one * s;
                blobs[b].transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(t * 0.7f) * 14f + Mathf.Sin(t * 1.9f + a.phase2) * 6f);

                // 分裂计时
                a.splitTimer += Time.deltaTime;
                if (!a.splitting && a.splitTimer >= a.splitInterval)
                {
                    a.splitTimer = 0f;
                    a.splitting = true;
                    a.splitElapsed = 0f;
                    a.splitStart = pos;
                    var dir = UnityEngine.Random.insideUnitCircle;
                    if (dir.sqrMagnitude < 0.01f) dir = new Vector2(1f, 0.2f);
                    a.splitDir = dir.normalized;
                    a.splitDist = UnityEngine.Random.Range(0.8f, 1.6f);
                }

                // 分裂动画：子光斑从父光斑位置飘散、放大、淡入淡出
                if (a.splitting && subBlobs[b] != null)
                {
                    a.splitElapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(a.splitElapsed / SplitDuration);
                    var sp = a.splitStart + (Vector3)a.splitDir * (a.splitDist * Mathf.Sin(k * Mathf.PI * 0.5f));
                    subBlobs[b].transform.localPosition = sp;
                    subBlobs[b].transform.localScale = Vector3.one * (0.6f + k * 0.8f);
                    float sa = Mathf.Sin(k * Mathf.PI); // 0→1→0
                    subBlobs[b].color = new Color(a.color.r, a.color.g, a.color.b, a.color.a * sa * 0.8f);
                    if (k >= 1f) a.splitting = false;
                }
            }

            // 提示文字轮换（状态机，不 yield 暂停）
            if (tipText != null)
            {
                tipTimer += Time.deltaTime;
                if (tipState == 0 && tipTimer >= TipInterval && TipTexts.Length > 1)
                {
                    tipTimer = 0f;
                    tipState = 1;
                    tipFadeElapsed = 0f;
                }
                if (tipState == 1)
                {
                    tipFadeElapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(tipFadeElapsed / TipFade);
                    tipText.alpha = 0.8f * (1f - k);
                    if (k >= 1f)
                    {
                        int newIdx;
                        do { newIdx = UnityEngine.Random.Range(0, TipTexts.Length); } while (newIdx == lastTipIndex && TipTexts.Length > 1);
                        lastTipIndex = newIdx;
                        tipText.text = TipTexts[newIdx];
                        tipState = 2;
                        tipFadeElapsed = 0f;
                    }
                }
                else if (tipState == 2)
                {
                    tipFadeElapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(tipFadeElapsed / TipFade);
                    tipText.alpha = 0.8f * k;
                    if (k >= 1f) tipState = 0;
                }
            }

            yield return null;
        }
    }

    // ======================= 主协程 =======================

    private static IEnumerator CoLoadLight(SplashManager instance)
    {
        float startTime = Time.time;

        // ---- 1. 背景光斑（淡金 Bloom，偏左上 + 晃动）+ 分裂子光斑 ----
        var blobSprite = CreateBlobSprite(256);
        for (int i = 0; i < BlobCount; i++)
        {
            var sr = UnityHelper.CreateObject<SpriteRenderer>($"LightBlob{i}", null,
                new Vector3(
                    UnityEngine.Random.Range(-4.4f, -1.6f),
                    UnityEngine.Random.Range(0.6f, 2.8f),
                    -8f));
            sr.sprite = blobSprite;
            var blobColor = UnityEngine.Random.value < 0.5f ? BlobColorA : BlobColorB;
            float a = UnityEngine.Random.Range(0.12f, BlobAlpha);
            sr.color = new Color(
                blobColor.r * UnityEngine.Random.Range(0.9f, 1f),
                blobColor.g * UnityEngine.Random.Range(0.9f, 1f),
                blobColor.b * UnityEngine.Random.Range(0.9f, 1f),
                a);
            float scale = UnityEngine.Random.Range(3.2f, 5.5f);
            sr.transform.localScale = Vector3.one * scale;
            blobs.Add(sr);
            blobAnims.Add(new BlobAnim
            {
                origin = sr.transform.localPosition,
                cycle = UnityEngine.Random.Range(BlobMinCycle, BlobMaxCycle),
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                phase2 = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                scaleBase = scale,
                scaleCycle = UnityEngine.Random.Range(18f, 34f),
                scalePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                color = sr.color,
                splitInterval = UnityEngine.Random.Range(SplitMinInterval, SplitMaxInterval),
                splitTimer = UnityEngine.Random.Range(0f, SplitMaxInterval), // 初始错开
            });

            // 分裂子光斑（同纹理、更小、初始隐藏）
            var sub = UnityHelper.CreateObject<SpriteRenderer>($"LightBlobSub{i}", null, sr.transform.localPosition + new Vector3(0f, 0f, -0.05f));
            sub.sprite = blobSprite;
            sub.transform.localScale = Vector3.one * 0.6f;
            sub.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0f);
            subBlobs.Add(sub);
        }

        // ---- 2. LOGO ----
        logo = UnityHelper.CreateObject<SpriteRenderer>("LightLogo", null, LogoPos);
        // LOGO 缩放（调这里：0.35 原始大小 → 0.45 放大）
        logo.transform.localScale = Vector3.one * 0.5f;

        var texture = GraphicsHelper.LoadTextureFromResources("Light.Resources.Lobby.LightInDark.png");
        if (texture != null)
        {
            var logoSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            logo.sprite = logoSprite;
        }
        logo.color = new Color(1f, 1f, 1f, 0f);

        // ---- 3. 启动独立环境协程（LOGO 浮动 + 光斑 + 提示，全程平滑） ----
        ambientRunning = true;
        instance.StartCoroutine(CoAmbient().WrapToIl2Cpp());

        // LOGO 淡入
        yield return FadeAlpha(logo, 0f, 1f, LogoFadeIn);
        logo.color = Color.white;

        // ---- 4. 进度文本（锚点，含百分比同行）+ 提示 + 版本 ----
        currentStepText = CreateText(instance, "LoadStepText", StepAnchor, FontStyles.Bold, 1f, TextAlignmentOptions.Center);
        currentStepText.text = "";
        currentStepText.color = new Color(0f, 0f, 0f, 0f);

        tipText = CreateText(instance, "LoadTipText", TipPos, FontStyles.Italic, 0.7f, TextAlignmentOptions.Center);
        tipText.color = new Color(0.6f, 0.6f, 0.6f, 0f);

        versionText = CreateText(instance, "LoadVersionText", VersionPos, FontStyles.Italic, 0.55f, TextAlignmentOptions.BottomRight);
        versionText.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        versionText.text = $"{LightPlugin.VisualVersion}";

        // 提示首句随机并初始淡入
        lastTipIndex = UnityEngine.Random.Range(0, TipTexts.Length);
        tipText.text = TipTexts[lastTipIndex];
        tipText.alpha = 0f;
        tipState = 2; // 淡入中
        tipFadeElapsed = 0f;
        tipTimer = TipInterval;

        // ---- 5. 按顺序执行加载步骤 ----
        for (int stepIdx = 0; stepIdx < LoadStepTexts.Length; stepIdx++)
        {
            float stepDuration = StepDuration(stepIdx);
            currentStepText.text = LoadStepTexts[stepIdx];
            currentStepText.color = ProgressGray;
            yield return FadeAlpha(currentStepText, 0f, 1f, FadeInDuration);

            float elapsed = 0f;
            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float localProgress = Mathf.Clamp01(elapsed / stepDuration);
                int percent = Mathf.FloorToInt(localProgress * 100f);

                // 本步骤自身进度：文字（含百分比同行）颜色随其从灰白渐变到绿
                currentStepText.color = Color.Lerp(ProgressGray, ProgressGreen, localProgress);
                currentStepText.text = $"{LoadStepTexts[stepIdx]} ({percent}%)";

                CleanupTweens();
                yield return null;
            }

            // 当前项完成
            yield return new WaitForSeconds(CompletedHold);

            // 完成项缩小下移；旧完成项整体下移让位
            completedSteps.Add(currentStepText);
            AnimateCompletedStep(currentStepText);
            float moveElapsed = 0f;
            while (moveElapsed < MoveDuration)
            {
                moveElapsed += Time.deltaTime;
                CleanupTweens();
                yield return null;
            }
            CleanupTweens();

            // 若还有下一项，创建新锚点文本（淡入交给下一轮循环）
            if (stepIdx < LoadStepTexts.Length - 1)
            {
                currentStepText = CreateText(instance, "LoadStepText", StepAnchor, FontStyles.Bold, 1f, TextAlignmentOptions.Center);
                currentStepText.text = "";
                currentStepText.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        // 等真实加载完成（至少 MinLoadTime 已满足）
        while (!cachedDoneLoadingRefData || Time.time - startTime < MinLoadTime)
            yield return null;

        // ---- 6. 步骤整体同时上移并淡出（摞在一起的文字作为一个整体，不逐个） ----
        Vector3 vanishPos = StepAnchor; // 完成项消失处，加载成功将直接出现在这里
        if (completedSteps.Count > 0)
        {
            Vector3[] froms = new Vector3[completedSteps.Count];
            float[] scales = new float[completedSteps.Count];
            for (int i = 0; i < completedSteps.Count; i++)
            {
                froms[i] = completedSteps[i].transform.localPosition;
                scales[i] = completedSteps[i].transform.localScale.x;
            }
            float up = 0.9f; // 整体上移量（顶部回到锚点附近）
            float duration = 0.25f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < completedSteps.Count; i++)
                {
                    if (completedSteps[i] == null) continue;
                    completedSteps[i].transform.localPosition = Vector3.Lerp(froms[i], froms[i] + new Vector3(0f, up, 0f), t);
                    completedSteps[i].transform.localScale = Vector3.one * Mathf.Lerp(scales[i], 0.1f, t);
                    completedSteps[i].alpha = Mathf.Lerp(1f, 0f, t);
                }
                yield return null;
            }
            // 记录消失处（最新完成项即最上方项上移后的位置）
            if (completedSteps.Count > 0 && completedSteps[completedSteps.Count - 1] != null)
                vanishPos = completedSteps[completedSteps.Count - 1].transform.localPosition;
            foreach (var c in completedSteps) if (c != null) UnityEngine.Object.Destroy(c.gameObject);
            completedSteps.Clear();
        }

        // ---- 7. 绿色"加载成功！"：直接出现在完成项消失处 → 下移到点击位置 → 淡出 ----
        successText = CreateText(instance, "LoadSuccessText", vanishPos, FontStyles.Bold, 1.15f, TextAlignmentOptions.Center);
        successText.text = LoadSuccessTextCN;
        successText.color = ProgressGreen;
        successText.alpha = 0f;
        yield return FadeAlpha(successText, 0f, 1f, SuccessFadeIn);
        yield return new WaitForSeconds(SuccessHold);
        // 下移到"按下任意键继续"的位置（与现在一致 = ClickPos）
        yield return MoveText(successText, vanishPos, ClickPos, 0.5f);
        // 在该位置淡出
        yield return FadeAlpha(successText, 1f, 0f, 0.4f);
        UnityEngine.Object.Destroy(successText.gameObject);
        successText = null;

        // ---- 8. 点击进入 ----
        clickText = CreateText(instance, "LoadClickText", ClickPos, FontStyles.Bold, 0.9f, TextAlignmentOptions.Center);
        clickText.text = ClickToEnterTextCN;
        clickText.color = new Color(ClickGold.r, ClickGold.g, ClickGold.b, 0f);

        bool entered = false;
        while (!entered)
        {
            // 点击提示：平滑正弦呼吸（cos 参数连续增长，无取模跳变）
            float breathe = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Time.time / ClickGlowCycle);
            clickText.alpha = 0.4f + breathe * 0.6f;

            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
            {
                entered = true;
                break;
            }
            yield return null;
        }

        // ---- 9. 停止环境协程并整体淡出 ----
        ambientRunning = false;
        yield return null; // 等环境协程退出

        // 淡出顺序：光斑 → 提示 → 版本 → 点击提示（倒数第二）→ LOGO（最后）
        for (int i = 0; i < blobs.Count; i++)
            yield return FadeAlpha(blobs[i], blobs[i].color.a, 0f, 0.4f);
        yield return FadeAlpha(tipText, tipText.alpha, 0f, 0.4f);
        yield return FadeAlpha(versionText, versionText.alpha, 0f, 0.4f);
        yield return FadeAlpha(clickText, clickText.alpha, 0f, 0.4f);
        yield return FadeAlpha(logo, 1f, 0f, 0.4f);

        foreach (var b in blobs) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
        foreach (var s in subBlobs) if (s != null) UnityEngine.Object.Destroy(s.gameObject);
        blobs.Clear(); subBlobs.Clear(); blobAnims.Clear();
        if (logo != null) UnityEngine.Object.Destroy(logo.gameObject);
        if (tipText != null) UnityEngine.Object.Destroy(tipText.gameObject);
        if (versionText != null) UnityEngine.Object.Destroy(versionText.gameObject);
        if (clickText != null) UnityEngine.Object.Destroy(clickText.gameObject);
        foreach (var t in completedSteps) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
        completedSteps.Clear();
        if (currentStepText != null) UnityEngine.Object.Destroy(currentStepText.gameObject);
        logo = null; tipText = null; versionText = null; clickText = null; currentStepText = null;

        // 保留原有进入游戏的调用接口
        instance.sceneChanger.AllowFinishLoadingScene();
        instance.startedSceneLoad = true;
    }

    // ======================= 小工具 =======================

    private sealed class BlobAnim
    {
        public Vector3 origin;
        public float cycle;
        public float phase;
        public float phase2;
        public float scaleBase;
        public float scaleCycle;
        public float scalePhase;
        public Color color;             // 光斑颜色（子光斑继承）
        public float splitInterval;     // 分裂间隔
        public float splitTimer;
        public bool splitting;
        public float splitElapsed;
        public Vector3 splitStart;
        public Vector2 splitDir;
        public float splitDist;
    }

    private static int LoadStepCount => LoadStepTexts.Length;

    private static float StepDuration(int stepIdx)
    {
        float w = stepIdx == LoadStepCount - 1 ? 1.2f : 1f;
        return (w / (8f + 1.2f)) * MinLoadTime;
    }

    private static TextMeshPro CreateText(SplashManager instance, string name, Vector3 pos, FontStyles style, float fontSizeScale, TextAlignmentOptions align)
    {
        var tmp = UnityEngine.Object.Instantiate(instance.errorPopup.InfoText, null);
        tmp.name = name;
        tmp.transform.localPosition = pos;
        tmp.fontStyle = style;
        tmp.fontSize *= fontSizeScale;
        tmp.alignment = align;
        tmp.gameObject.SetActive(true);
        return tmp;
    }

    /// <summary>径向渐变光斑 Sprite（中心 1 → 边缘 0，柔和光晕）。</summary>
    private static Sprite? CreateBlobSprite(int size)
    {
        try
        {
            var tex = new Texture2D(size, size);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d >= 1f) { tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0f)); continue; }
                    float a = 1f - d;
                    a = a * a * (3f - 2f * a); // smoothstep，柔和边缘
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LoadPatch.CreateBlobSprite]", ex);
            return null;
        }
    }

    // ---- 动画片段 ----

    private static IEnumerator FadeAlpha(Component target, float from, float to, float duration)
    {
        if (target == null) yield break;
        var r = target as TextMeshPro;
        var s = target as SpriteRenderer;
        if (r != null)
        {
            r.alpha = from;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                r.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
            r.alpha = to;
        }
        else if (s != null)
        {
            var c = s.color;
            c.a = from;
            s.color = c;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                var cc = s.color;
                cc.a = Mathf.Lerp(from, to, t);
                s.color = cc;
                yield return null;
            }
            var fc = s.color; fc.a = to; s.color = fc;
        }
    }

    private static IEnumerator MoveText(TextMeshPro tmp, Vector3 from, Vector3 to, float duration)
    {
        if (tmp == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float e = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            tmp.transform.localPosition = Vector3.Lerp(from, to, e);
            yield return null;
        }
        tmp.transform.localPosition = to;
    }

    /// <summary>完成项：缩小 0.5 并下移；所有旧完成项整体下移让位。
    /// 间距按尺度区分：新完成项（刚从全尺寸缩下来）用 FirstRowGap 拉开与首行的距离；
    /// 旧完成项（都是缩小到 0.5 的小字）彼此用 RowGap 收紧，避免空洞。</summary>
    private static void AnimateCompletedStep(TextMeshPro done)
    {
        // 新完成项从当前位置下移（全尺寸 → 完成区，间距稍宽）
        AddTextMove(done, done.transform.localPosition + new Vector3(0f, -FirstRowGap, 0f));
        AddTextScale(done, 0.5f);

        // 旧完成项整体下移让位（小字之间间距收紧）
        for (int i = 0; i < completedSteps.Count - 1; i++)
        {
            var old = completedSteps[i];
            AddTextMove(old, old.transform.localPosition + new Vector3(0f, -RowGap, 0f));
        }
    }

    private static void AddTextMove(TextMeshPro? tmp, Vector3 target)
    {
        if (tmp == null) return;
        Vector3 from = tmp.transform.localPosition;
        moveTweens.Add(new Tweener
        {
            From = 0f, To = 1f, Duration = MoveDuration,
            Set = v => tmp.transform.localPosition = Vector3.Lerp(from, target, v),
            OnDone = _ => tmp.transform.localPosition = target,
        });
    }

    private static void AddTextScale(TextMeshPro? tmp, float targetScale)
    {
        if (tmp == null) return;
        float from = tmp.transform.localScale.x;
        moveTweens.Add(new Tweener
        {
            From = 0f, To = 1f, Duration = MoveDuration,
            Set = v => tmp.transform.localScale = Vector3.one * Mathf.Lerp(from, targetScale, v),
            OnDone = _ => tmp.transform.localScale = Vector3.one * targetScale,
        });
    }
}