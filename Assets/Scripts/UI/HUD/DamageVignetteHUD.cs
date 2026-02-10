using UnityEngine;
using UnityEngine.UI;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 受击/低血量视觉效果 — 全屏不透明渐变晕影
    /// 使用单个全屏 Image + 运行时生成晕影纹理（避免四角重叠）
    /// 血量 < 60% 持续红色；受击时强烈闪烁；脱战后首次受击亮紫色
    /// </summary>
    public class DamageVignetteHUD : MonoBehaviour
    {
        private Image vignetteImage;
        private Texture2D vignetteTex;

        // 状态
        private float hitFlashTimer;
        private float hitFlashIntensity;
        private bool isFirstHitFlash; // 紫色闪烁
        private float hitFlashDuration;
        private Color currentFlashColor;
        private float lowHealthIntensity;

        private const int TEX_SIZE = 128;

        void Awake()
        {
            hitFlashDuration = UILayoutManager.Settings.hitFlashDuration;

            var rt = gameObject.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(rt);

            // 生成晕影纹理（中心透明，边缘不透明）
            vignetteTex = GenerateVignetteTexture();

            // 创建全屏晕影 Image
            vignetteImage = UIFactory.CreateFullScreenImage(transform, "Vignette", Color.clear);
            vignetteImage.raycastTarget = false;
            vignetteImage.sprite = Sprite.Create(vignetteTex,
                new Rect(0, 0, TEX_SIZE, TEX_SIZE),
                new Vector2(0.5f, 0.5f));
            vignetteImage.color = Color.clear;
        }

        void OnDestroy()
        {
            if (vignetteTex != null)
                Object.Destroy(vignetteTex);
        }

        /// <summary>生成径向渐变晕影纹理：中心完全透明，边缘完全不透明</summary>
        private Texture2D GenerateVignetteTexture()
        {
            var tex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[TEX_SIZE * TEX_SIZE];
            float cx = TEX_SIZE * 0.5f;
            float cy = TEX_SIZE * 0.5f;

            for (int y = 0; y < TEX_SIZE; y++)
            {
                for (int x = 0; x < TEX_SIZE; x++)
                {
                    // 归一化椭圆距离（水平稍宽）
                    float nx = (x - cx) / cx;
                    float ny = (y - cy) / cy;
                    float dist = Mathf.Sqrt(nx * nx * 0.8f + ny * ny); // 横向略扁

                    // 从中心(0) → 边缘(1) 的渐变，使用平滑步进
                    float t = Mathf.Clamp01((dist - 0.3f) / 0.7f);
                    t = t * t * (3f - 2f * t); // smoothstep

                    byte a = (byte)(t * 255);
                    pixels[y * TEX_SIZE + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        void Update()
        {
            // 受击闪烁衰减
            if (hitFlashTimer > 0f)
            {
                hitFlashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(hitFlashTimer / hitFlashDuration);

                // 使用受击强度调节亮度
                float alpha = t * hitFlashIntensity;
                SetColor(currentFlashColor, alpha);
            }
            else if (lowHealthIntensity > 0f)
            {
                // 低血量持续红色脉动
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                float alpha = lowHealthIntensity * (0.6f + 0.4f * pulse);
                SetColor(UIColors.DarkRed, alpha);
            }
            else
            {
                SetColor(Color.clear, 0f);
            }
        }

        /// <summary>受击时调用</summary>
        public void OnDamage(float dmgPct, float healthPct, bool firstHitAfterSafe)
        {
            hitFlashDuration = UILayoutManager.Settings.hitFlashDuration;
            hitFlashTimer = hitFlashDuration;
            isFirstHitFlash = firstHitAfterSafe;

            // 根据伤害比例调节闪烁强度
            hitFlashIntensity = Mathf.Clamp(0.4f + dmgPct * 3f, 0.4f, 0.9f);

            // 选择闪烁颜色
            if (firstHitAfterSafe)
                currentFlashColor = UIColors.BrightPurple;
            else if (healthPct < 0.3f)
                currentFlashColor = UIColors.Red;
            else
                currentFlashColor = new Color(0.9f, 0.2f, 0.1f, 1f);
        }

        /// <summary>每帧更新低血量持续提示</summary>
        public void UpdateLowHealth(float healthPct)
        {
            float threshold = UILayoutManager.Settings.lowHealthThreshold;
            if (healthPct < threshold)
            {
                float intensity = 1f - (healthPct / threshold);
                lowHealthIntensity = intensity * 0.55f;
            }
            else
            {
                lowHealthIntensity = 0f;
            }
        }

        private void SetColor(Color baseColor, float alpha)
        {
            if (vignetteImage == null) return;
            vignetteImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }
    }
}
