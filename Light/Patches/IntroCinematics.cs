using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LightInDark.Audio;
using LightInDark.Core;
using LightInDark.Roles;
using LightInDark.UI.Window;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Light.Patches;

/// <summary>
/// 开场演出：在原版角色揭示的 2.5 秒窗口内播放分镜。
/// 分三层时间线推进：蓄力（光点汇聚）→ 爆发（立绘炸开 + 冲击环 + 顿帧 + 震屏）→ 余韵（标题压缩）。
///
/// 当前版本已停用：<see cref="IntroPatch"/> 与 <see cref="IntroRoleTextDriver"/> 中的调用均已注释，
/// 需要恢复时取消那边三处注释即可，本类无需改动。
///
/// 关键约束：
///   · 计时一律用 unscaledDeltaTime，顿帧期间演出不冻结
///   · 总时长不超过 2.45s（原版只等 2.5s，超时会被切断）
///   · timeScale / 相机 / 文字缩放与字间距必须恢复，协程 finally 与 CleanupAll 双路径兜底
///   · 任何特效失败都只跳过该效果，不中断整段演出
/// </summary>
public static class IntroCinematics
{
    // =====================================================================
    //  时间线（秒，unscaled）
    // =====================================================================
    private const float StageIn = 0.30f;          // 舞台光淡入
    private const float ConvergeStart = 0.12f;    // 光点开始汇聚
    private const float PortraitStart = 0.58f;    // 立绘炸开
    private const float ImpactAt = 0.78f;         // ★ 高潮帧
    private const float HitStop = 0.07f;          // 顿帧
    private const float ShakeEnd = 1.06f;         // 震屏结束
    private const float WaveEnd = 1.16f;          // 色彩冲击波结束
    private const float TitleStart = 1.04f, TitleEnd = 1.50f;
    private const float FadeStart = 1.85f, TimelineEnd = 2.05f;

    // =====================================================================
    //  参数
    // =====================================================================
    private const float PortraitX = 0f;
    private const float PortraitY = 1.42f;      // 上半屏：给下方职业名留空间
    private const float PortraitZ = -18f;
    private const float PortraitTargetHeight = 2.55f;   // 立绘目标世界高度（按素材实际尺寸反算缩放）
    private const float PortraitMaxWidth = 3.4f;
    private const float CoreBaseScale = 3.2f;           // 无立绘时能量核心的基准尺寸
    private const float ShakeAmplitude = 0.12f;
    private const int ConvergeCount = 24;         // 蓄力光点
    private const int RayCount = 18;              // 放射光线
    private const int SparkCount = 14;            // 爆散光丝

    private const string SfxImpact = "Light.Resources.SFX.impact.wav";
    private const string SfxRiser = "Light.Resources.SFX.riser.wav";
    private const string SfxWhoosh = "Light.Resources.SFX.whoosh.wav";

    private static readonly List<GameObject> _temp = new();
    private static int _sequence;
    private static string _baseLayer = "";        // 对齐原版黑幕的 sorting layer

    // =====================================================================
    //  程序化贴图（一次生成，全局复用）
    // =====================================================================
    private static Sprite _glow, _vignette, _ring, _vFade, _dot;

    private static Sprite Glow => _glow != null ? _glow : (_glow = MakeRadial(256, 2.2f, false));
    private static Sprite Vignette => _vignette != null ? _vignette : (_vignette = MakeRadial(256, 1.0f, true));
    private static Sprite Ring => _ring != null ? _ring : (_ring = MakeRing(256));
    private static Sprite VFade => _vFade != null ? _vFade : (_vFade = MakeVerticalFade(8, 128));
    private static Sprite Dot => _dot != null ? _dot : (_dot = MakeRadial(64, 1.6f, false));

    /// <summary>径向渐变（中心亮、边缘透明）；invert=true 时反相（暗角：中心透明、边缘实）。</summary>
    private static Sprite MakeRadial(int n, float power, bool invert)
    {
        try
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var buf = new Il2CppStructArray<Color32>(n * n);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), power);
                    if (invert) a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.85f, r));
                    else a = Mathf.SmoothStep(0f, 1f, a);           // 软过渡，去硬边
                    buf[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(buf);
            tex.Apply();
            // PPU = 像素数 → sprite 世界尺寸恰为 1×1，后续 scale 直接等于世界尺寸
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroCinematics] 径向贴图生成失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>柔边圆环（冲击环用）。</summary>
    private static Sprite MakeRing(int n)
    {
        try
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var buf = new Il2CppStructArray<Color32>(n * n);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 环带中心 0.82，带宽 0.10，两侧柔化
                    float a = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.10f, 2f) * 2.2f);
                    a *= Mathf.Clamp01(1f - r) * 4f;                 // 外缘收干净，避免方角
                    buf[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(buf);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroCinematics] 圆环贴图生成失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>竖向渐变（两端透明，中段实）——光线 / 光丝 / 下划线都用它，天然无硬边。</summary>
    private static Sprite MakeVerticalFade(int w, int h)
    {
        try
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var buf = new Il2CppStructArray<Color32>(w * h);
            for (int y = 0; y < h; y++)
            {
                float p = (y + 0.5f) / h;                            // 0 底 → 1 顶
                float a = Mathf.Sin(p * Mathf.PI);                   // 正弦包络：中段最亮，两端 0
                a = Mathf.Pow(a, 1.4f);
                for (int x = 0; x < w; x++)
                    buf[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(buf);
            tex.Apply();
            // pivot 放底部，便于"从一点向上生长"；PPU = 高度 → sprite 高恰为 1 世界单位
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), h);
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroCinematics] 渐变贴图生成失败: {ex.Message}");
            return null;
        }
    }

    // =====================================================================
    //  公开入口
    // =====================================================================

    public static IEnumerator PlayRoleReveal(IntroCutscene inst, RuntimeRoleTemplate role)
    {
        if (inst == null || role == null) yield break;

        int seq = ++_sequence;
        var color = LightInDark.ColorHelper.ToUnityColor(role.Color);
        var savedRoleScale = inst.RoleText != null ? inst.RoleText.transform.localScale : Vector3.one;

        // 对齐原版黑幕所在的 sorting layer，否则所有演出层都会被开场画面盖住
        try
        {
            var fm = inst.FrontMost;
            if (fm != null) _baseLayer = fm.sortingLayerName;
        }
        catch { }

        LightLogger.Log($"[IntroCinematics] 演出开始：{role.CodeName} 色={role.Color} " +
                        $"立绘={(role.Role?.RoleImage != null ? "有" : "无")} 层={_baseLayer}");

        // ---- 构建场景层 ----
        var vignette = MakeFullScreen("LidVignette", Vignette, new Color(0f, 0f, 0f, 0.78f), 29900, seq);
        var stageGlow = MakeSprite("LidStageGlow", Glow, 29800, seq);
        if (stageGlow != null)
        {
            stageGlow.localPosition = new Vector3(PortraitX, PortraitY, PortraitZ + 2f);
            stageGlow.localScale = Vector3.one * 7.5f;
            var sr = stageGlow.GetComponent<SpriteRenderer>();
            sr.color = new Color(color.r, color.g, color.b, 0f);
        }
        var art = MakePortrait(inst, role, color, seq);
        bool hasArt = art != null;
        float pScale;
        Transform subject;

        if (hasArt)
        {
            subject = art;
            pScale = PortraitTargetHeight;
            try
            {
                var b = art.GetComponent<SpriteRenderer>().sprite.bounds.size;
                pScale = PortraitTargetHeight / Mathf.Max(0.01f, b.y);
                if (b.x * pScale > PortraitMaxWidth) pScale = PortraitMaxWidth / Mathf.Max(0.01f, b.x);
            }
            catch { }
        }
        else
        {
            // 没有立绘的职业：用多层光环组成的能量核心当主体，避免画面上什么都没有
            subject = MakeCore(color, seq);
            pScale = CoreBaseScale;
        }
        if (subject != null) subject.localScale = Vector3.one * (pScale * 0.85f);

        // 轮廓辉光：仅立绘需要（叠一张放大的同图）
        Transform rim = null;
        if (hasArt)
        {
            rim = MakePortrait(inst, role, color, seq);
            if (rim != null)
            {
                rim.localScale = Vector3.one * (pScale * 1.04f);
                rim.GetComponent<SpriteRenderer>().sortingOrder = 29999;
                rim.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f);
            }
        }

        var converge = MakeConverge(color, seq);
        var ring1 = MakeSprite("LidRing1", Ring, 32000, seq);
        var ring2 = MakeSprite("LidRing2", Ring, 32001, seq);
        foreach (var r in new[] { ring1, ring2 })
            if (r != null) { r.localPosition = new Vector3(PortraitX, PortraitY, -45f); r.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f); }
        var rays = MakeRays(color, seq);
        var sparks = MakeSparks(color, seq);
        var flash = MakeFullScreen("LidFlash", Light.UI.Window.VanillaAsset.FullScreenSprite,
            new Color(1f, 1f, 1f, 0f), 33000, seq);
        var underline = MakeSprite("LidUnderline", VFade, 32050, seq);
        if (underline != null)
        {
            underline.localRotation = Quaternion.Euler(0f, 0f, -90f);   // 竖向贴图转横
            underline.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f);
        }

        // 标题字间距（从"散"收到"紧"）
        float spacingOrigin = 0f;
        try { spacingOrigin = inst.RoleText != null ? inst.RoleText.characterSpacing : 0f; } catch { }

        Camera cam = null;
        Vector3 camOrigin = Vector3.zero;
        try { cam = Camera.main; if (cam != null) camOrigin = cam.transform.localPosition; } catch { }

        var convergeStart = new Vector3[ConvergeCount];
        var sparkVel = new Vector3[SparkCount];
        try
        {
            var center = ScreenCenterWorld();
            for (int i = 0; i < ConvergeCount; i++)
            {
                float ang = i * Mathf.PI * 2f / ConvergeCount + UnityEngine.Random.Range(-0.15f, 0.15f);
                convergeStart[i] = center + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * UnityEngine.Random.Range(5.5f, 7.5f);
            }
            for (int i = 0; i < SparkCount; i++)
                sparkVel[i] = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(3.5f, 7f);
        }
        catch { }

        bool impactFired = false, hitStopDone = false, flashWhite = false, flashTint = false, titleFired = false;
        float stageAngle = 0f;

        float t = 0f;
        try
        {
            while (t < TimelineEnd)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                if (seq != _sequence) yield break;

                // ── 0. 暗角：开场压暗 → 冲击瞬间收紧 → 缓慢放开 ──
                if (vignette != null)
                {
                    float v = Mathf.Lerp(0.78f, 0.45f, Mathf.Clamp01(t / StageIn));
                    if (t >= ImpactAt) v = Mathf.Lerp(0.85f, 0.40f, Mathf.Clamp01((t - ImpactAt) / 0.55f));
                    var c = vignette.GetComponent<SpriteRenderer>().color;
                    c.a = v;
                    vignette.GetComponent<SpriteRenderer>().color = c;
                }

                // ── 1. 舞台光：淡入 + 缓慢旋转 + 呼吸脉动 ──
                if (stageGlow != null)
                {
                    float a = Mathf.Clamp01(t / StageIn) * 0.55f;
                    if (t >= ImpactAt) a = Mathf.Lerp(0.55f, 0.85f, Mathf.Clamp01((t - ImpactAt) / 0.20f))
                        * (0.92f + Mathf.Sin((t - ImpactAt) * 4.2f) * 0.08f);
                    var c = stageGlow.GetComponent<SpriteRenderer>().color;
                    c.a = a;
                    stageGlow.GetComponent<SpriteRenderer>().color = c;
                    stageAngle += dt * 8f;
                    stageGlow.localRotation = Quaternion.Euler(0f, 0f, stageAngle);
                    stageGlow.localScale = Vector3.one * (7.5f + Mathf.Sin(t * 2.2f) * 0.30f);
                }

                // ── 2. 蓄力：光点从四周汇聚到中心（冲击瞬间消失）──
                if (converge != null && t >= ConvergeStart && t < ImpactAt)
                {
                    float p = Mathf.Clamp01((t - ConvergeStart) / (ImpactAt - ConvergeStart));
                    float eased = p * p * (3f - 2f * p);                  // SmoothStep
                    var center = new Vector3(PortraitX, PortraitY, -46f);
                    for (int i = 0; i < converge.Count && i < ConvergeCount; i++)
                    {
                        var go = converge[i];
                        if (go == null) continue;
                        go.transform.position = Vector3.Lerp(convergeStart[i], center, eased);
                        var c = go.GetComponent<SpriteRenderer>().color;
                        c.a = Mathf.Lerp(0.15f, 0.95f, eased);
                        go.GetComponent<SpriteRenderer>().color = c;
                        go.transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 0.22f, eased);
                    }
                }
                else if (converge != null && t >= ImpactAt)
                {
                    foreach (var go in converge) HideOnce(go);
                }

                // ── 3. 主体（立绘或能量核心）：从中心炸开 0.85 → 1.06 → 1.0 ──
                if (subject != null)
                {
                    float alphaK;
                    if (t < PortraitStart)
                    {
                        alphaK = 0f;
                        subject.localScale = Vector3.one * (pScale * 0.85f);
                    }
                    else if (t < ImpactAt)
                    {
                        float p = Mathf.Clamp01((t - PortraitStart) / (ImpactAt - PortraitStart));
                        alphaK = Mathf.Clamp01(p * 1.6f);
                        subject.localScale = Vector3.one * (pScale * Mathf.Lerp(0.85f, 1.06f, p * p));
                        // 落位前色差抖动（两次小幅左右位移）
                        float jitter = (p > 0.75f) ? Mathf.Sin(p * 90f) * 0.035f : 0f;
                        subject.localPosition = new Vector3(PortraitX + jitter, PortraitY, PortraitZ);
                    }
                    else
                    {
                        alphaK = 1f;
                        if (t < ShakeEnd)
                        {
                            float p = Mathf.Clamp01((t - ImpactAt) / (ShakeEnd - ImpactAt));
                            subject.localScale = Vector3.one * (pScale * Mathf.Lerp(1.06f, 1.0f, Mathf.SmoothStep(0f, 1f, p)));
                            subject.localPosition = new Vector3(PortraitX, PortraitY, PortraitZ);
                        }
                        else
                        {
                            // 余韵：极缓呼吸
                            float s = 1f + Mathf.Sin((t - ShakeEnd) * 3.4f) * 0.010f;
                            subject.localScale = Vector3.one * (pScale * s);
                        }
                    }

                    if (hasArt)
                    {
                        var sr = subject.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.color = new Color(color.r, color.g, color.b, alphaK);
                    }
                    else
                    {
                        SetCoreAlpha(subject, color, alphaK);
                        subject.localRotation = Quaternion.Euler(0f, 0f, t * 50f);   // 光环缓慢旋转
                    }

                    // 轮廓辉光跟随（仅立绘）
                    if (rim != null)
                    {
                        var rc = rim.GetComponent<SpriteRenderer>();
                        rim.localPosition = subject.localPosition;
                        rim.localScale = subject.localScale * 1.03f;
                        float ra = t >= ImpactAt
                            ? Mathf.Lerp(0.85f, 0.30f, Mathf.Clamp01((t - ImpactAt) / 0.6f))
                            : Mathf.Clamp01(t / ImpactAt) * 0.9f;
                        rc.color = new Color(color.r, color.g, color.b, ra);
                    }
                }

                // ── 4. 高潮帧 ──
                if (!impactFired && t >= ImpactAt)
                {
                    impactFired = true;
                    PlaySfx(SfxImpact, 1f);
                    Time.timeScale = 0.05f;
                }
                if (impactFired && !hitStopDone && t >= ImpactAt + HitStop)
                {
                    hitStopDone = true;
                    Time.timeScale = 1f;
                }

                // 震屏
                if (cam != null)
                {
                    if (impactFired && t < ShakeEnd)
                    {
                        float amp = ShakeAmplitude * (1f - (t - ImpactAt) / (ShakeEnd - ImpactAt));
                        cam.transform.localPosition = camOrigin + UnityEngine.Random.insideUnitSphere * amp;
                    }
                    else if (impactFired) cam.transform.localPosition = camOrigin;
                }

                // 闪光：白 1 帧 → 职业色 2 帧 → 淡出
                if (flash != null)
                {
                    var sr = flash.GetComponent<SpriteRenderer>();
                    if (!impactFired) sr.color = new Color(1f, 1f, 1f, 0f);
                    else if (!flashWhite) { flashWhite = true; sr.color = new Color(1f, 1f, 1f, 0.62f); }
                    else if (!flashTint) { flashTint = true; sr.color = new Color(color.r, color.g, color.b, 0.48f); }
                    else if (t < ShakeEnd)
                    {
                        var c = sr.color;
                        c.a = Mathf.Lerp(0.48f, 0f, Mathf.Clamp01((t - ImpactAt - HitStop) / 0.20f));
                        sr.color = c;
                    }
                }

                // 双冲击环（错峰：快环 + 慢环）
                UpdateRing(ring1, t, ImpactAt, 0.00f, 0.30f, 3.0f, 13f, 0.95f, color);
                UpdateRing(ring2, t, ImpactAt, 0.07f, 0.46f, 2.0f, 8.5f, 0.65f, color);

                // 放射光线（两端透明，从中心向外炸开）
                UpdateRays(rays, t, ImpactAt, color);

                // 爆散光丝
                UpdateSparks(sparks, sparkVel, t, ImpactAt, color, dt);

                // 色彩冲击波（_Rad 反向）
                if (t >= ImpactAt && t <= WaveEnd)
                    UpdateShockwave(inst, Mathf.Clamp01((t - ImpactAt) / (WaveEnd - ImpactAt)));

                // ── 5. 标题：字间距压缩 + 缩放 + 下划线展开 ──
                if (inst.RoleText != null)
                {
                    if (!titleFired && t >= TitleStart)
                    {
                        titleFired = true;
                        TryStyleTitle(inst.RoleText);
                    }
                    if (t >= TitleStart && t <= TitleEnd)
                    {
                        float p = Mathf.Clamp01((t - TitleStart) / (TitleEnd - TitleStart));
                        float e = 1f - Mathf.Pow(1f - p, 3f);              // EaseOutCubic
                        inst.RoleText.transform.localScale = savedRoleScale * Mathf.Lerp(1.22f, 1.0f, e);
                        try { inst.RoleText.characterSpacing = Mathf.Lerp(14f, spacingOrigin, e); } catch { }
                    }
                    else if (t > TitleEnd)
                    {
                        inst.RoleText.transform.localScale = savedRoleScale;
                    }
                }
                if (underline != null)
                {
                    if (t >= TitleStart && t <= TitleEnd)
                    {
                        float p = Mathf.Clamp01((t - TitleStart) / (TitleEnd - TitleStart));
                        float w = Mathf.Lerp(0.2f, 3.4f, Mathf.SmoothStep(0f, 1f, p));
                        // 竖向贴图已旋转 -90°：localScale.x 是粗细，y 是长度
                        underline.localScale = new Vector3(0.055f, w, 1f);
                        underline.localPosition = new Vector3(0f, PortraitY - 1.55f, -20f);
                        var c = underline.GetComponent<SpriteRenderer>().color;
                        c.a = Mathf.Lerp(0f, 0.85f, p) * (t > FadeStart ? 0f : 1f);
                        underline.GetComponent<SpriteRenderer>().color = c;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            Time.timeScale = 1f;
            try
            {
                if (cam != null) cam.transform.localPosition = camOrigin;
                if (inst != null && inst.RoleText != null)
                {
                    inst.RoleText.transform.localScale = savedRoleScale;
                    inst.RoleText.characterSpacing = spacingOrigin;
                }
            }
            catch { }
        }
    }

    /// <summary>团队阶段扫光（一次性，自有对象）。</summary>
    public static IEnumerator PlayTeamSweep(IntroCutscene inst)
    {
        if (inst == null) yield break;
        int seq = ++_sequence;
        PlaySfx(SfxWhoosh, 0.5f);

        var bar = MakeSprite("LidTeamSweep", VFade, 31000, seq);
        if (bar == null) yield break;

        var (w, h) = ScreenWorldSize();
        bar.localRotation = Quaternion.Euler(0f, 0f, 75f);
        // 竖向贴图已旋转 75°：localScale.x 是粗细，y 是长度
        bar.localScale = new Vector3(0.30f, h * 1.6f, 1f);
        bar.localPosition = new Vector3(-w * 1.2f, 0f, -50f);
        var sr = bar.GetComponent<SpriteRenderer>();
        sr.color = new Color(1f, 1f, 1f, 0.22f);

        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.unscaledDeltaTime;
            if (seq != _sequence || bar == null) yield break;
            float p = Mathf.Clamp01(t / 0.45f);
            bar.localPosition = new Vector3(Mathf.Lerp(-w * 1.2f, w * 1.2f, Mathf.SmoothStep(0f, 1f, p)), 0f, -50f);
            sr.color = new Color(1f, 1f, 1f, 0.22f * Mathf.Sin(p * Mathf.PI));
            yield return null;
        }
        DestroyNamed("LidTeamSweep");
    }

    /// <summary>销毁兜底（IntroCutscene 销毁时调用）。</summary>
    internal static void CleanupAll()
    {
        _sequence++;
        Time.timeScale = 1f;
        foreach (var go in _temp)
        {
            try { if (go != null) Object.Destroy(go); } catch { }
        }
        _temp.Clear();
    }

    // =====================================================================
    //  各层构造
    // =====================================================================

    private static Transform MakeSprite(string name, Sprite sprite, int order, int seq)
    {
        if (sprite == null) return null;
        try
        {
            var go = new GameObject(name);
            Track(go);
            go.layer = LayerExpansion.GetUILayer();
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // 必须与原版黑幕同一个 sorting layer，否则整层会被开场画面盖住
            if (!string.IsNullOrEmpty(_baseLayer)) sr.sortingLayerName = _baseLayer;
            sr.sortingOrder = order;
            return go.transform;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroCinematics] {name} 创建失败: {ex.Message}");
            return null;
        }
    }

    private static Transform MakeFullScreen(string name, Sprite sprite, Color color, int order, int seq)
    {
        // 贴图生成失败就跳过这一层，不用白图兜底（否则整屏发白）
        if (sprite == null) return null;
        var tr = MakeSprite(name, sprite, order, seq);
        if (tr == null) return null;
        try
        {
            var (w, h) = ScreenWorldSize();
            tr.localScale = new Vector3(w * 1.3f, h * 1.3f, 1f);
            tr.localPosition = new Vector3(0f, 0f, -50f);
            tr.GetComponent<SpriteRenderer>().color = color;
        }
        catch { }
        return tr;
    }

    private static Transform MakePortrait(IntroCutscene inst, RuntimeRoleTemplate role, Color color, int seq)
    {
        try
        {
            var sprite = role.Role?.RoleImage;
            if (sprite == null) return null;

            var go = new GameObject("LidPortrait");
            Track(go);
            go.layer = LayerExpansion.GetUILayer();
            go.transform.SetParent(inst.transform, false);
            go.transform.localPosition = new Vector3(PortraitX, PortraitY, PortraitZ);
            go.transform.localScale = Vector3.one * PortraitTargetHeight;   // 实际缩放由调用方按素材尺寸反算

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 30000;
            sr.color = new Color(color.r, color.g, color.b, 0f);
            return go.transform;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[IntroCinematics] 立绘跳过: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 无立绘职业的主体：外光晕 + 旋转光环 + 内光晕 + 亮核，四层叠出"能量核心"。
    /// 返回的根节点带 SpriteRenderer，可直接参与主体的缩放/透明动画。
    /// </summary>
    private static Transform MakeCore(Color color, int seq)
    {
        var root = MakeSprite("LidCore", Glow, 29950, seq);
        if (root == null) return null;

        root.localPosition = new Vector3(PortraitX, PortraitY, PortraitZ);
        root.localScale = Vector3.one * CoreBaseScale;
        root.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0.30f);

        AddCoreLayer(root, Ring, color, 0.78f, 0.58f, 29951);   // 旋转光环
        AddCoreLayer(root, Glow, color, 0.46f, 0.85f, 29952);   // 内光晕
        AddCoreLayer(root, Dot, color, 0.14f, 1.00f, 29953);    // 亮核
        return root;
    }

    private static void AddCoreLayer(Transform parent, Sprite sprite, Color color, float relScale, float alpha, int order)
    {
        try
        {
            if (sprite == null) return;

            var go = new GameObject(parent.name + "_" + order);
            Track(go);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * relScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (!string.IsNullOrEmpty(_baseLayer)) sr.sortingLayerName = _baseLayer;
            sr.sortingOrder = order;
            sr.color = new Color(color.r, color.g, color.b, alpha);
        }
        catch { }
    }

    private static readonly float[] CoreAlphas = { 0.30f, 0.58f, 0.85f, 1.00f };

    /// <summary>能量核心统一透明度（根 + 3 层子物体按权重各自乘 k）。</summary>
    private static void SetCoreAlpha(Transform root, Color color, float k)
    {
        try
        {
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(color.r, color.g, color.b, CoreAlphas[0] * k);

            int n = Mathf.Min(root.childCount, CoreAlphas.Length - 1);
            for (int i = 0; i < n; i++)
            {
                var child = root.GetChild(i);
                var csr = child.GetComponent<SpriteRenderer>();
                if (csr != null) csr.color = new Color(color.r, color.g, color.b, CoreAlphas[i + 1] * k);
            }
        }
        catch { }
    }

    private static List<GameObject> MakeConverge(Color color, int seq)
    {
        var list = new List<GameObject>();
        try
        {
            for (int i = 0; i < ConvergeCount; i++)
            {
                var tr = MakeSprite("LidConverge", Dot, 31900, seq);
                if (tr == null) continue;
                tr.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f);
                tr.localScale = Vector3.one * 0.5f;
                list.Add(tr.gameObject);
            }
        }
        catch { }
        return list;
    }

    private static List<GameObject> MakeRays(Color color, int seq)
    {
        var list = new List<GameObject>();
        try
        {
            for (int i = 0; i < RayCount; i++)
            {
                var tr = MakeSprite("LidRay", VFade, 31700, seq);
                if (tr == null) continue;
                float ang = i * 360f / RayCount + UnityEngine.Random.Range(-6f, 6f);
                tr.localRotation = Quaternion.Euler(0f, 0f, ang - 90f + 90f);   // 贴图本身向上生长
                tr.localPosition = new Vector3(PortraitX, PortraitY, -47f);
                tr.localScale = new Vector3(0.10f, 0f, 1f);
                tr.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f);
                list.Add(tr.gameObject);
            }
        }
        catch { }
        return list;
    }

    private static List<GameObject> MakeSparks(Color color, int seq)
    {
        var list = new List<GameObject>();
        try
        {
            for (int i = 0; i < SparkCount; i++)
            {
                var tr = MakeSprite("LidSpark", VFade, 31800, seq);
                if (tr == null) continue;
                tr.localPosition = new Vector3(PortraitX, PortraitY, -46f);
                tr.localScale = new Vector3(0.06f, 0f, 1f);
                tr.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0f);
                list.Add(tr.gameObject);
            }
        }
        catch { }
        return list;
    }

    // =====================================================================
    //  通道推进
    // =====================================================================

    private static void UpdateRing(Transform ring, float t, float impactAt, float delay,
                                   float life, float fromScale, float toScale, float maxAlpha, Color color)
    {
        if (ring == null) return;
        float lt = t - impactAt - delay;
        if (lt < 0f) return;

        float p = Mathf.Clamp01(lt / life);
        float s = Mathf.Lerp(fromScale, toScale, 1f - Mathf.Pow(1f - p, 2.4f));   // EaseOutQuad
        ring.localScale = Vector3.one * s;
        var c = ring.GetComponent<SpriteRenderer>().color;
        c.a = maxAlpha * Mathf.Pow(1f - p, 1.6f);
        ring.GetComponent<SpriteRenderer>().color = c;
    }

    private static void UpdateRays(List<GameObject> rays, float t, float impactAt, Color color)
    {
        if (rays == null || rays.Count == 0) return;
        float lt = t - impactAt;
        if (lt < 0f) return;

        float p = Mathf.Clamp01(lt / 0.34f);
        float len = Mathf.Lerp(0.4f, 4.2f, 1f - Mathf.Pow(1f - p, 3f));
        float alpha = 0.55f * Mathf.Pow(1f - p, 2f);

        foreach (var go in rays)
        {
            if (go == null) continue;
            var tr = go.transform;
            tr.localScale = new Vector3(0.09f, len, 1f);
            var c = go.GetComponent<SpriteRenderer>().color;
            c.a = alpha;
            go.GetComponent<SpriteRenderer>().color = c;
        }
    }

    private static void UpdateSparks(List<GameObject> sparks, Vector3[] vel, float t, float impactAt, Color color, float dt)
    {
        if (sparks == null || sparks.Count == 0) return;
        float lt = t - impactAt;
        if (lt < 0f) return;

        float p = Mathf.Clamp01(lt / 0.55f);
        if (p >= 1f) { foreach (var go in sparks) HideOnce(go); return; }

        for (int i = 0; i < sparks.Count && i < vel.Length; i++)
        {
            var go = sparks[i];
            if (go == null) continue;

            var tr = go.transform;
            vel[i].y -= 5.0f * dt;                                    // 重力
            var pos = new Vector3(PortraitX, PortraitY, -46f) + (Vector3)vel[i] * lt;
            tr.localPosition = pos;
            tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(vel[i].y, vel[i].x) * Mathf.Rad2Deg - 90f);
            tr.localScale = new Vector3(0.05f, Mathf.Min(1.1f, vel[i].magnitude * 0.20f), 1f);

            var c = go.GetComponent<SpriteRenderer>().color;
            c.a = 0.95f * (1f - p * p);
            go.GetComponent<SpriteRenderer>().color = c;
        }
    }

    private static void UpdateShockwave(IntroCutscene inst, float p)
    {
        try
        {
            var mat = inst.Foreground != null ? inst.Foreground.material : null;
            if (mat == null || !mat.HasProperty("_Rad")) return;
            mat.SetFloat("_Rad", inst.ForegroundRadius.ExpOutLerp(1f - p));
        }
        catch { }
    }

    /// <summary>标题质感：描边加深 + 轻微发光（实例材质，不影响其它文字）。</summary>
    private static void TryStyleTitle(TMP_Text text)
    {
        try
        {
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(0, 0, 0, 200);
            var mat = text.fontMaterial;
            if (mat != null && mat.HasProperty("_GlowPower"))
            {
                mat.EnableKeyword("GLOW_ON");
                mat.SetFloat("_GlowPower", 0.35f);
                mat.SetColor("_GlowColor", text.color);
            }
        }
        catch { }
    }

    private static void HideOnce(GameObject go)
    {
        if (go == null) return;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null) return;
        if (sr.color.a <= 0f) return;
        var c = sr.color;
        c.a = 0f;
        sr.color = c;
    }

    // =====================================================================
    //  工具
    // =====================================================================

    private static (float Width, float Height) ScreenWorldSize()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return (10f, 6f);
            float h = cam.orthographicSize * 2f;
            return (h * cam.aspect, h);
        }
        catch { return (10f, 6f); }
    }

    private static Vector3 ScreenCenterWorld()
    {
        try
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
        }
        catch { }
        return Vector3.zero;
    }

    private static void PlaySfx(string path, float volume)
    {
        try { if (SfxManager.ResourceExists(path)) SfxManager.Play(path, volume); }
        catch { }
    }

    private static void Track(GameObject go)
    {
        if (go != null && !_temp.Contains(go)) _temp.Add(go);
    }

    private static void DestroyNamed(string name)
    {
        for (int i = _temp.Count - 1; i >= 0; i--)
        {
            var go = _temp[i];
            if (go != null && go.name == name)
            {
                try { Object.Destroy(go); } catch { }
                _temp.RemoveAt(i);
            }
        }
    }
}
