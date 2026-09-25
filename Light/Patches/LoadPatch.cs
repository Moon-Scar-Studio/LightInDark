using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using LightInDark.Core;
using Light.Utilities;
using System.Collections;
using TMPro;
using UnityEngine;
using System;

namespace Light.Patches;

[HarmonyPriority(Priority.HigherThanNormal)]
[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
public static class LoadPatch
{
    private static readonly string[] Quotes =
    [
        "\"在战争中，没有什么比胜利更令人振奋的了。\" —— 温斯顿·丘吉尔",
        "\"我们将在海滩作战，我们将在登陆点作战，我们将在田野和街头作战。\" —— 温斯顿·丘吉尔",
        "\"我所能奉献的唯有热血、辛劳、眼泪和汗水。\" —— 温斯顿·丘吉尔",
        "\"胜利越来之不易，胜利的荣耀就越伟大。\" —— 温斯顿·丘吉尔",
        "\"在战争与屈辱面前，你选择了屈辱，但将来你还得面对战争。\" —— 温斯顿·丘吉尔",
        "\"闪电战的本质是以最快的速度集中最强力量打击敌人最弱点。\" —— 海因茨·古德里安",
        "\"老兵永不死，只是渐凋零。\" —— 道格拉斯·麦克阿瑟",
        "\"我们唯一的恐惧就是恐惧本身。\" —— 富兰克林·D·罗斯福",
        "\"昨天，1941年12月7日——将永远成为耻辱的象征。\" —— 富兰克林·D·罗斯福",
        "\"租借法案是美国对捍卫自由事业的最大贡献。\" —— 富兰克林·D·罗斯福",
        "\"战争的目的不是为国捐躯，而是让敌人为国捐躯。\" —— 乔治·S·巴顿",
        "\"勇气就是面对恐惧时依然坚持到底。\" —— 乔治·S·巴顿",
        "\"在俄国广阔的草原上，德国的闪电战终于碰上了它的对手。\" —— 格奥尔吉·朱可夫",
        "\"如果我知道德国会入侵，我会在一周前就动员军队。\" —— 约瑟夫·斯大林",
        "\"这场战争也是全人类的战争，是正义与邪恶的较量。\" —— 德怀特·D·艾森豪威尔"
    ];

    private const float MinLoadTime = 2f;
    private const float QuoteInterval = 4f;
    // 等待原版 Among Us 水獭图标展示完成后再显示模组 Logo
    private const float SplashDisplayTime = 4.5f;

    private static Sprite? logoSprite;
    private static SpriteRenderer? logo;
    private static SpriteRenderer? logoGlow;
    private static TextMeshPro? loadText;
    private static TextMeshPro? quoteText;
    private static TextMeshPro? versionText;
    private static int currentQuoteIndex;
    private static float quoteTimer;
    private static float loadStageTimer;
    private static float loadDotTimer;

    private static bool loaded;
    private static bool cachedDoneLoadingRefData;

    // 粒子系统
    private class Particle
    {
        public SpriteRenderer? renderer;
        public Vector2 velocity;
        public float life;
        public float maxLife;
        public float startScale;
    }
    private static List<Particle>? particles;
    private static Sprite? particleSprite;

    public static string LoadingText
    {
        set
        {
            try
            {
                if (loadText != null) loadText.text = value;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("[LoadPatch.set]", ex);
            }
        }
    }

    private static string GetNextLoadStage(float elapsed)
    {
        try
        {
            return elapsed switch
            {
                < 0.1f => "正在加载资源...",
                < 0.3f => "正在解压资源包...",
                < 0.5f => "正在初始化模组核心...",
                < 0.7f => "正在注册组件...",
                < 0.9f => "正在配置 Harmony 补丁...",
                < 1.2f => "正在加载语言数据...",
                < 1.5f => "正在准备游戏环境...",
                < 1.8f => "正在校准模组参数...",
                < 2.0f => "正在建立通信管道...",
                _ => "准备就绪..."
            };
        }
        catch (Exception ex)
        {
            LightLogger.LogError("[LoadPatch.GetNextLoadStage]", ex); return default;
        }
    }

    private static IEnumerator CoLoadLight(SplashManager instance)
    {
        // ======= 创建 Logo =======
        logo = UnityHelper.CreateObject<SpriteRenderer>("LightLogo", null, new Vector3(0, 0.5f, -5f));
        logo.transform.localScale = Vector3.one * 0.35f;

        // 创建 Logo 发光层
        logoGlow = UnityHelper.CreateObject<SpriteRenderer>("LightLogoGlow", null, new Vector3(0, 0.5f, -4.8f));
        logoGlow.transform.localScale = Vector3.one * 0.35f;

        // 加载 Logo 纹理
        var texture = GraphicsHelper.LoadTextureFromResources("Light.Resources.Lobby.LightInDark.png");
        if (texture != null)
        {
            logoSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            logo.sprite = logoSprite;
            logoGlow.sprite = logoSprite;
        }

        // ======= 创建粒子纹理（小圆点） =======
        var particleTex = new Texture2D(8, 8);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                float dx = (x - 3.5f) / 3.5f;
                float dy = (y - 3.5f) / 3.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                particleTex.SetPixel(x, y, dist < 1f ? new Color(1, 1, 1, 1f - dist) : Color.clear);
            }
        particleTex.Apply();
        particleSprite = Sprite.Create(particleTex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f);

        // ======= 创建粒子 =======
        particles = new List<Particle>();
        for (int i = 0; i < 12; i++)
        {
            var sr = UnityHelper.CreateObject<SpriteRenderer>($"Particle{i}", null,
                new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-1.5f, 2.5f), -4.5f));
            sr.sprite = particleSprite;
            sr.color = new Color(1, 1, 1, UnityEngine.Random.Range(0.1f, 0.35f));
            float s = UnityEngine.Random.Range(0.3f, 0.7f);
            sr.transform.localScale = Vector3.one * s;
            particles.Add(new Particle
            {
                renderer = sr,
                velocity = new Vector2(UnityEngine.Random.Range(-0.08f, 0.08f), UnityEngine.Random.Range(-0.06f, 0.06f)),
                life = UnityEngine.Random.Range(0f, 3f),
                maxLife = UnityEngine.Random.Range(3f, 5f),
                startScale = s
            });
        }

        // ======= Logo 缩放淡入动画 =======
        float p = 1f;
        while (p > 0f)
        {
            p -= Time.deltaTime * 2.8f;
            float alpha = 1f - p;
            logo.color = new Color(1f, 1f, 1f, alpha);
            // 发光层：比 Logo 稍大，透明度先快速提升再缓慢降低
            float glowAlpha = Mathf.Min(1f, alpha * (p * 2f + 0.3f));
            logoGlow.color = new Color(1f, 1f, 1f, glowAlpha * 0.5f);
            logo.transform.localScale = Vector3.one * (p * p * 0.012f + 1f) * 0.35f;
            logoGlow.transform.localScale = Vector3.one * (p * p * 0.015f + 1.04f) * 0.35f;
            yield return null;
        }
        logo.color = UnityEngine.Color.white;
        logo.transform.localScale = Vector3.one * 0.35f;
        // 发光层继续淡入维持
        logoGlow.color = new Color(1f, 1f, 1f, 0.45f);
        logoGlow.transform.localScale = Vector3.one * 1.04f * 0.35f;

        // ======= 创建加载进度文字 =======
        loadText = UnityEngine.Object.Instantiate(instance.errorPopup.InfoText, null);
        loadText.transform.localPosition = new Vector3(0f, -0.8f, -10f);
        loadText.fontStyle = FontStyles.Bold;
        loadText.text = "正在加载资源...";
        loadText.color = new Color(1f, 1f, 1f, 0.3f);

        // ======= 创建引用文字（放到底部） =======
        quoteText = UnityEngine.Object.Instantiate(instance.errorPopup.InfoText, null);
        quoteText.transform.localPosition = new Vector3(0f, -3.8f, -10f);
        quoteText.fontStyle = FontStyles.Italic;
        quoteText.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        quoteText.fontSize *= 0.7f;
        quoteText.text = Quotes[0];

        // ======= 创建版本号（右下角） =======
        versionText = UnityEngine.Object.Instantiate(instance.errorPopup.InfoText, null);
        versionText.transform.localPosition = new Vector3(4.5f, -3.2f, -10f);
        versionText.fontStyle = FontStyles.Italic;
        versionText.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        versionText.fontSize *= 0.55f;
        versionText.alignment = TextAlignmentOptions.BottomRight;
        versionText.text = $"{LightPlugin.VisualVersion}";

        // ======= 加载主循环 =======
        loadStageTimer = 0f;
        currentQuoteIndex = 0;
        quoteTimer = 0f;
        loadDotTimer = 0f;
        float quoteFadeTimer = 0f;
        bool quoteFading = false;
        string? nextQuote = null;
        float totalLoadTime = 0f;

        while (!cachedDoneLoadingRefData || loadStageTimer < MinLoadTime)
        {
            if (cachedDoneLoadingRefData)
            {
                loadStageTimer += Time.deltaTime;
                totalLoadTime += Time.deltaTime;
            }

            quoteTimer += Time.deltaTime;
            loadDotTimer += Time.deltaTime;

            // 轮播名言（带淡入淡出过渡）
            if (quoteTimer >= QuoteInterval && !quoteFading)
            {
                quoteFading = true;
                quoteFadeTimer = 0f;
                nextQuote = Quotes[(currentQuoteIndex + 1) % Quotes.Length];
            }

            if (quoteFading)
            {
                quoteFadeTimer += Time.deltaTime;
                float fadeP = Mathf.Clamp01(quoteFadeTimer / 0.5f);
                if (fadeP < 0.5f)
                {
                    // 淡出旧名言
                    float outA = 0.8f * (1f - fadeP * 2f);
                    quoteText.color = new Color(0.6f, 0.6f, 0.6f, outA);
                }
                else
                {
                    // 切换并淡入新名言
                    if (nextQuote != null && fadeP < 0.55f)
                    {
                        quoteText.text = nextQuote;
                        currentQuoteIndex = (currentQuoteIndex + 1) % Quotes.Length;
                    }
                    float inA = 0.8f * ((fadeP - 0.5f) * 2f);
                    quoteText.color = new Color(0.6f, 0.6f, 0.6f, inA);
                    if (fadeP >= 1f)
                    {
                        quoteFading = false;
                        quoteTimer = 0f;
                        quoteText.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    }
                }
            }

            // 加载阶段文字 + 动态点
            string stage = GetNextLoadStage(loadStageTimer);
            int dotCount = ((int)(loadDotTimer * 4f) % 4);
            loadText.text = stage + new string('.', dotCount) + new string(' ', 3 - dotCount);

            // 发光层脉冲呼吸
            float breathe = Mathf.Sin(Time.time * 1.5f) * 0.12f + 0.35f;
            logoGlow.color = new Color(1f, 1f, 1f, breathe);

            // Logo 微微旋转摆动
            float rotZ = Mathf.Sin(Time.time * 0.3f) * 1.5f;
            logo.transform.localEulerAngles = new Vector3(0f, 0f, rotZ);
            logoGlow.transform.localEulerAngles = new Vector3(0f, 0f, rotZ);

            // 粒子更新
            if (particles != null)
            {
                for (int i = particles.Count - 1; i >= 0; i--)
                {
                    var pt = particles[i];
                    pt.life += Time.deltaTime;

                    // 粒子飘动
                    var pos = pt.renderer.transform.localPosition;
                    pos.x += pt.velocity.x * Time.deltaTime;
                    pos.y += pt.velocity.y * Time.deltaTime + Mathf.Sin(Time.time + i) * 0.003f;
                    pt.renderer.transform.localPosition = pos;

                    // 粒子透明度呼吸
                    float alpha = Mathf.Sin(pt.life * 1.2f + i) * 0.15f + 0.2f;
                    pt.renderer.color = new Color(1, 1, 1, alpha);

                    // 循环边界
                    if (pos.x > 3.5f) pos.x = -3.5f;
                    if (pos.x < -3.5f) pos.x = 3.5f;
                    if (pos.y > 2.8f) pos.y = -1.8f;
                    if (pos.y < -1.8f) pos.y = 2.8f;
                }
            }

            yield return null;
        }

        // 加载完成闪烁
        loadText.text = "加载完成";
        for (int i = 0; i < 3; i++)
        {
            loadText.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.03f);
            loadText.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.03f);
        }

        // 粒子淡出
        if (particles != null)
        {
            p = 1f;
            while (p > 0f)
            {
                p -= Time.deltaTime * 2f;
                foreach (var pt in particles)
                    pt.renderer.color = new Color(1, 1, 1, pt.renderer.color.a * 0.9f);
                yield return null;
            }
            foreach (var pt in particles)
                UnityEngine.Object.Destroy(pt.renderer.gameObject);
            particles.Clear();
        }

        // 销毁 UI 元素
        UnityEngine.Object.Destroy(loadText.gameObject);
        UnityEngine.Object.Destroy(quoteText.gameObject);
        UnityEngine.Object.Destroy(versionText.gameObject);

        // 发光层淡出
        p = 0f;
        while (p < 1f)
        {
            p += Time.deltaTime * 2f;
            logoGlow.color = new Color(1f, 1f, 1f, 0.45f * (1f - p));
            yield return null;
        }
        UnityEngine.Object.Destroy(logoGlow.gameObject);

        // Logo 淡出
        p = 1f;
        while (p > 0f)
        {
            p -= Time.deltaTime * 1.2f;
            logo.color = new UnityEngine.Color(1f, 1f, 1f, p);
            yield return null;
        }
        logo.color = Color.clear;

        instance.sceneChanger.AllowFinishLoadingScene();
        instance.startedSceneLoad = true;
    }

    public static bool Prefix(SplashManager __instance)
    {
        try
        {
            cachedDoneLoadingRefData |= __instance.doneLoadingRefdata;
            __instance.doneLoadingRefdata = false;

            if (cachedDoneLoadingRefData
                && !__instance.startedSceneLoad
                && Time.time - __instance.startTime > Mathf.Max(__instance.minimumSecondsBeforeSceneChange, SplashDisplayTime)
                && !loaded)
            {
                loaded = true;
                __instance.StartCoroutine(CoLoadLight(__instance).WrapToIl2Cpp());
            }

            return false;
        }
        catch (System.Exception)
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
        catch (System.Exception)
        {
        }
    }
}
