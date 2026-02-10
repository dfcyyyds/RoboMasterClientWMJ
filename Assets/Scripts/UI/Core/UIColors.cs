using UnityEngine;

namespace UI.Core
{
    /// <summary>
    /// 全局 UI 色彩常量 — 现代 FPS 风格，蓝色主色调
    /// </summary>
    public static class UIColors
    {
        // ─── 主题色 ───
        public static readonly Color BrightBlue   = new Color(91f/255f, 193f/255f, 250f/255f, 1f);
        public static readonly Color LightBlueBorder = new Color(165f/255f, 206f/255f, 216f/255f, 1f);
        public static readonly Color YellowBorder  = new Color(239f/255f, 229f/255f, 128f/255f, 1f);
        public static readonly Color Orange        = new Color(220f/255f, 186f/255f, 83f/255f, 1f);
        public static readonly Color BrightPurple  = new Color(172f/255f, 105f/255f, 228f/255f, 1f);
        public static readonly Color DarkPurple    = new Color(145f/255f, 88f/255f, 186f/255f, 1f);
        public static readonly Color DarkRed       = new Color(114f/255f, 37f/255f, 34f/255f, 1f);
        public static readonly Color Red           = new Color(231f/255f, 100f/255f, 72f/255f, 1f);
        public static readonly Color Silver        = new Color(204f/255f, 214f/255f, 229f/255f, 1f);
        public static readonly Color White         = new Color(251f/255f, 248f/255f, 254f/255f, 1f);

        // ─── 功能色 ───
        public static readonly Color TeamRed       = new Color(0.85f, 0.2f, 0.15f, 1f);
        public static readonly Color TeamBlue      = new Color(0.15f, 0.35f, 0.9f, 1f);
        public static readonly Color HealthGreen   = new Color(0.2f, 0.85f, 0.3f, 1f);
        public static readonly Color HeatYellow    = new Color(0.95f, 0.8f, 0.2f, 1f);
        public static readonly Color HeatRed       = Red;

        // ─── 背景/面板色 ───
        public static readonly Color PanelBg       = new Color(0.08f, 0.08f, 0.12f, 0.85f);
        public static readonly Color PanelBgDark   = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        public static readonly Color Overlay        = new Color(0f, 0f, 0f, 0.7f);

        // ─── 带透明度变体 ───
        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// 根据百分比在两色之间插值
        /// </summary>
        public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));
    }
}
