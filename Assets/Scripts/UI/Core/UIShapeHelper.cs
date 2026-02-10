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
    }
}
