using System;
using System.Collections.Generic;
using UnityEngine;

namespace Light.UI.Window;

/// <summary>
/// **运行时用代码画出来的"圆角面板"九宫格图**（用户 2026-10-06：外部素材太难看，让代码画一份 ✓）。
///
/// 为什么这样做：
///   · 不依赖任何 png 素材 ✓（不用再找/再复制 Nebula 的图 ✗，那些图本来也不是给提示框用的 ✓）
///   · 圆角 + 发丝边框用**像素级距离场**画，边缘带一点抗锯齿 ✓（比"4 条硬边"好看得多 ✓）
///   · 生成结果带 `Sprite.border`（九宫格 ✓）→ 配 `SpriteDrawMode.Sliced` 任意拉伸，
///     **圆角不会被拉变形** ✓✓（这是关键：纯色块拉伸会把圆角拉歪 ✗）
///
/// ⚠️ 缓存键 = (填充色, 边框色, 圆角半径, 边框粗细) ✓ —— 参数一样就复用同一张图，
///    改配色时参数变了自然生成新图 ✓（旧的留在缓存里，量很小 ✓）
/// </summary>
internal static class RoundedPanelSprite
{
    private sealed class Key
    {
        public Color Fill, Border;
        public int Radius, BorderWidth;

        public override bool Equals(object? obj)
            => obj is Key k && k.Radius == Radius && k.BorderWidth == BorderWidth
               && k.Fill.Equals(Fill) && k.Border.Equals(Border);

        public override int GetHashCode()
            => HashCode.Combine(Fill.GetHashCode(), Border.GetHashCode(), Radius, BorderWidth);
    }

    private static readonly Dictionary<Key, Sprite> Cache = new();

    /// <summary>
    /// 取一张九宫格圆角面板图 ✓。
    /// </summary>
    /// <param name="radius">圆角半径（像素；九宫格边距就是它 ✓）</param>
    /// <param name="borderWidth">边框粗细（像素；0 = 不画边框）</param>
    public static Sprite? Get(Color fill, Color border, int radius = 10, int borderWidth = 1)
    {
        try
        {
            if (radius < 1) radius = 1;
            if (borderWidth < 0) borderWidth = 0;
            if (borderWidth > radius) borderWidth = radius;

            var key = new Key { Fill = fill, Border = border, Radius = radius, BorderWidth = borderWidth };
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            // 图尺寸：中间留 2 像素可拉伸的实心区（够 Sliced 用 ✓）
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // 圆角矩形的"有符号距离"：>0 在内部，=0 在边界上，<0 在外部
            // 内部填充色 + 靠外的 borderWidth 像素用边框色 ✓（再用 1 像素做抗锯齿过渡 ✓）
            float half = size * 0.5f;
            float r = radius;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;

                    // 到"圆角矩形边界"的距离（标准做法：先把点夹到内矩形，再算到圆心的距离 ✓）
                    float qx = Mathf.Abs(px) - (half - r);
                    float qy = Mathf.Abs(py) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;   // <0 = 里面 ✓

                    float inside = -dist;                                          // >0 = 里面 ✓
                    float alpha = Mathf.Clamp01(inside + 0.5f);                     // 1 像素抗锯齿 ✓
                    if (alpha <= 0f) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }

                    // 边框：距离边界 borderWidth 以内算边框 ✓
                    bool isBorder = borderWidth > 0 && inside < borderWidth;
                    var c = isBorder ? border : fill;

                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.a * alpha) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            var rect = new Rect(0f, 0f, size, size);
            var b = new Vector4(radius, radius, radius, radius);      // 九宫格边距 = 圆角半径 ✓
            var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, b);
            sprite.name = $"LightRoundedPanel_{radius}_{borderWidth}";

            Cache[key] = sprite;
            LightInDark.Core.LightLogger.Log($"[RoundedPanelSprite] 生成圆角面板图 {size}×{size}（圆角={radius} 边框={borderWidth}px ✓）");
            return sprite;
        }
        catch (Exception ex)
        {
            LightInDark.Core.LightLogger.LogWarning($"[RoundedPanelSprite.Get] {ex.Message}");
            return null;
        }
    }
}
