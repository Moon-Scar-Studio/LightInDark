using System;
using System.Collections.Generic;
using LightInDark.Core;
using UnityEngine;

namespace Light.Cosmic;

/// <summary>
/// **把 CosmicAddons 的 png 字节变成游戏能用的 Sprite**（第 2 步第 1 件 ✓）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【colorMode 语义 —— 用户 2026-10-06 明确定义 ✓】
///
///   `player`：**原图当遮罩** ✓
///       · **红通道 = 玩家颜色** ✓
///       · **蓝通道 = 阴影色** ✓（我用"玩家色变暗"来近似 ✓ —— 原版帽子就是这种双色调 ✓）
///       · 最终 = 玩家色 × 红 + 阴影色 × 蓝 ✓（逐像素算，**不需要自定义 shader** ✓✓）
///
///   `fixed`：**原样渲染** ✓（彩色渐变 / 纯黑描边这类素材 ✓，一个像素都不改 ✓）
///
/// 【为什么要自己逐像素算】
///   原版的 `HatParent.SetMaterialColor(color)` 是**整张图乘一个颜色** ✗ ——
///   那会把 `fixed` 的彩色素材也染掉 ✗，也做不出"红/蓝双通道"的效果 ✗。
///   自己算 = 语义完全可控 ✓，而且**按颜色缓存**之后开销只在第一次 ✓。
///
/// ⚠️ 缓存键 = (装扮全局 id, 玩家色) ✓ —— 12 种颜色最多算 12 张 ✓
/// ⚠️ 多帧（`frames`/`frameDelay`）本轮**先只取第 0 帧** ✓，逐帧动画留到后面一步 ✓
/// ═══════════════════════════════════════════════════════════════════════
/// </summary>
internal static class CosmicSpriteFactory
{
    private sealed class Key
    {
        public string FullId = "";
        public Color PlayerColor;

        public override bool Equals(object? obj)
            => obj is Key k && k.FullId == FullId && k.PlayerColor.Equals(PlayerColor);

        public override int GetHashCode() => HashCode.Combine(FullId, PlayerColor.GetHashCode());
    }

    private static readonly Dictionary<Key, Sprite?> Cache = new();
    private static readonly Dictionary<Key, Sprite[]?> FramesCache = new();

    /// <summary>取一件装扮的 Sprite（`player` 模式按颜色算 ✓；失败返回 null 并打日志 ✓）</summary>
    public static Sprite? Get(CosmicItem item, Color playerColor)
    {
        var frames = GetFrames(item, playerColor);
        return frames != null && frames.Length > 0 ? frames[0] : null;
    }

    /// <summary>
    /// **多帧素材的每一帧** ✓（JSON 的 `frames` ✓：贴图是**横向** sprite sheet ✓）。
    /// 单帧时返回长度 1 的数组 ✓；缓存键与 `Get` 同一套（按装扮 id + 颜色 ✓）。
    /// </summary>
    public static Sprite[]? GetFrames(CosmicItem item, Color playerColor)
    {
        try
        {
            if (item == null || item.PngBytes == null || item.PngBytes.Length == 0) return null;

            var key = new Key
            {
                FullId = item.FullId,
                PlayerColor = item.ColorMode == CosmicColorMode.Player ? playerColor : Color.white,
            };
            if (FramesCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = BuildTexture(item, playerColor);
            if (tex == null)
            {
                FramesCache[key] = null;
                return null;
            }

            int frames = Mathf.Max(1, item.Frames);
            // 贴图宽度必须能被帧数整除 ✓（不能就退化成单帧 ✓，免得切出歪图 ✗）
            if (frames > 1 && tex.width % frames != 0)
            {
                LightLogger.LogWarning($"[CosmicSprite] {item.FullId} 的贴图宽 {tex.width} 不能被帧数 {frames} 整除 → 按单帧处理 ✓");
                frames = 1;
            }

            float w = tex.width / (float)frames;
            var result = new Sprite[frames];
            string baseName = $"Cosmic_{item.AddonId}_{item.LocalId}" +
                              (item.ColorMode == CosmicColorMode.Player ? $"_{ColorUtility.ToHtmlStringRGB(playerColor)}" : "");

            for (int i = 0; i < frames; i++)
            {
                // ⚠️ 装扮贴图的 ppu 与原版帽子一致（100 ✓）；offset/scale 由使用处处理 ✓
                var s = Sprite.Create(tex, new Rect(i * w, 0f, w, tex.height),
                                      new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                s.name = frames > 1 ? $"{baseName}_f{i}" : baseName;
                result[i] = s;
            }

            FramesCache[key] = result;
            LightLogger.Log($"[CosmicSprite] 生成 {item.FullId}（{tex.width}×{tex.height} 帧数={frames} " +
                            $"模式={item.ColorMode} 颜色=#{ColorUtility.ToHtmlStringRGB(playerColor)}）✓");
            return result;
        }
        catch (Exception ex)
        {
            LightLogger.LogWarning($"[CosmicSpriteFactory.GetFrames] {ex.Message}");
            return null;
        }
    }

    /// <summary>解码 png + （`player` 模式）逐像素上色 ✓ —— 一幅装扮只算一次 ✓</summary>
    private static Texture2D? BuildTexture(CosmicItem item, Color playerColor)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(item.PngBytes))
        {
            LightLogger.LogWarning($"[CosmicSprite] {item.FullId} 的 png 解码失败 ✗");
            return null;
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;   // 装扮通常要缩小显示 → 双线性更细腻 ✓

        if (item.ColorMode == CosmicColorMode.Player)
            ApplyPlayerMask(tex, playerColor);

        return tex;
    }

    /// <summary>
    /// **`player` 模式的核心** ✓：原图当遮罩 —— 红通道取玩家色、蓝通道取阴影色 ✓。
    ///
    /// ⚠️ 阴影色我用"玩家色 × 0.62"近似 ✓（原版帽子的暗部就是这个观感 ✓）；
    ///    如果以后要精确对齐原版，把 `ShadowFactor` 换成读 `Palette` 的阴影色即可 ✓
    /// </summary>
    private const float ShadowFactor = 0.62f;

    private static void ApplyPlayerMask(Texture2D tex, Color playerColor)
    {
        var shadow = new Color(playerColor.r * ShadowFactor,
                               playerColor.g * ShadowFactor,
                               playerColor.b * ShadowFactor,
                               playerColor.a);

        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            var p = px[i];
            if (p.a == 0) continue;                       // 透明区保持透明 ✓

            float red = p.r / 255f;                       // 红通道 = 玩家色权重 ✓
            float blue = p.b / 255f;                      // 蓝通道 = 阴影色权重 ✓

            // 两个权重相加可能 >1（画得偏白的像素）→ 归一化，避免过曝 ✓
            float sum = red + blue;
            if (sum > 1f) { red /= sum; blue /= sum; }

            float r = playerColor.r * red + shadow.r * blue;
            float g = playerColor.g * red + shadow.g * blue;
            float b = playerColor.b * red + shadow.b * blue;

            px[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                p.a);
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
    }

    /// <summary>清缓存（换局不需要 ✗ —— 装扮是跨局的；换语言/改素材才需要 ✓）</summary>
    public static void ClearCache() => Cache.Clear();
}
