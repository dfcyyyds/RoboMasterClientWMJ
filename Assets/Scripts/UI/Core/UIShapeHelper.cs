using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Core
{
    /// <summary>
    /// 运行时生成圆角矩形 Sprite（9-sliced），支持缓存复用
    /// </summary>
    public static class UIShapeHelper
    {
        private static readonly Dictionary<int, Sprite> _cache = new Dictionary<int, Sprite>();

        /// <summary>
        /// 默认圆角矩形精灵（64×64, r=12），可直接用于 Image.type = Sliced
        /// </summary>
        public static Sprite RoundedRect => GetRoundedRectSprite(64, 12);

        /// <summary>
        /// 获取指定尺寸与圆角半径的 9-sliced Sprite（缓存复用）
        /// </summary>
        public static Sprite GetRoundedRectSprite(int texSize = 64, int radius = 12)
        {
            int key = texSize * 1000 + radius;
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[texSize * texSize];
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float a = CalcAlpha(x, y, texSize, texSize, radius);
                    byte ab = (byte)(Mathf.Clamp01(a) * 255);
                    pixels[y * texSize + x] = new Color32(255, 255, 255, ab);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true); // makeNoLongerReadable 节省内存

            float border = radius;
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, texSize, texSize),
                new Vector2(0.5f, 0.5f),
                100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border)
            );
            sprite.name = $"RoundedRect_{texSize}_{radius}";

            _cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 计算像素在圆角矩形中的 alpha（带抗锯齿）
        /// </summary>
        private static float CalcAlpha(int px, int py, int w, int h, int r)
        {
            // 只有四个角需要特殊处理
            int cx, cy;
            if (px < r && py < r) { cx = r; cy = r; }         // 左下
            else if (px >= w - r && py < r) { cx = w - r - 1; cy = r; }         // 右下
            else if (px < r && py >= h - r) { cx = r; cy = h - r - 1; } // 左上
            else if (px >= w - r && py >= h - r) { cx = w - r - 1; cy = h - r - 1; } // 右上
            else return 1f; // 矩形内部

            float dx = px - cx;
            float dy = py - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            if (dist <= r - 1f) return 1f;
            if (dist >= r) return 0f;
            return r - dist; // 1px 抗锯齿渐变
        }

        // ─── 环形 (Ring / Arc) Sprite 生成 ───

        private static readonly Dictionary<long, Sprite> _ringCache = new Dictionary<long, Sprite>();

        /// <summary>
        /// 生成环形（圆弧）Sprite，用于准星周围的热量/弹药环
        /// </summary>
        /// <param name="texSize">纹理尺寸（正方形）</param>
        /// <param name="outerRadius">外圆半径（像素）</param>
        /// <param name="thickness">环宽度（像素）</param>
        /// <param name="startAngleDeg">起始角度（0=右，逆时针为正）</param>
        /// <param name="endAngleDeg">结束角度</param>
        /// <returns>白色环形 Sprite</returns>
        public static Sprite GetRingSprite(int texSize = 128, float outerRadius = -1,
            float thickness = 8f, float startAngleDeg = 0f, float endAngleDeg = 360f)
        {
            if (outerRadius < 0) outerRadius = texSize / 2f - 2f;
            long key = (long)texSize * 100000000L + (long)(outerRadius * 100) * 10000L
                       + (long)(thickness * 100) * 100L + (long)startAngleDeg + (long)endAngleDeg * 1000000000L;

            if (_ringCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float innerRadius = outerRadius - thickness;
            float cx = texSize / 2f;
            float cy = texSize / 2f;
            float startRad = startAngleDeg * Mathf.Deg2Rad;
            float endRad = endAngleDeg * Mathf.Deg2Rad;

            var pixels = new Color32[texSize * texSize];
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 径向抗锯齿
                    float outerAlpha = Mathf.Clamp01(outerRadius - dist + 1f);
                    float innerAlpha = Mathf.Clamp01(dist - innerRadius + 1f);
                    float radialAlpha = Mathf.Min(outerAlpha, innerAlpha);

                    if (radialAlpha <= 0)
                    {
                        pixels[y * texSize + x] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    // 角度检查（完整圆跳过角度检测）
                    float finalAlpha = radialAlpha;
                    if (!Mathf.Approximately(endAngleDeg - startAngleDeg, 360f))
                    {
                        float angle = Mathf.Atan2(dy, dx);
                        if (angle < 0) angle += Mathf.PI * 2;

                        float normStart = startRad;
                        float normEnd = endRad;
                        while (normStart < 0) normStart += Mathf.PI * 2;
                        while (normEnd < 0) normEnd += Mathf.PI * 2;

                        bool inArc;
                        if (normEnd > normStart)
                            inArc = angle >= normStart && angle <= normEnd;
                        else
                            inArc = angle >= normStart || angle <= normEnd;

                        if (!inArc) finalAlpha = 0;
                    }

                    byte ab = (byte)(Mathf.Clamp01(finalAlpha) * 255);
                    pixels[y * texSize + x] = new Color32(255, 255, 255, ab);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, texSize, texSize),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = $"Ring_{texSize}_{outerRadius:F0}_{thickness:F0}";

            _ringCache[key] = sprite;
            return sprite;
        }
    }
}
