using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 底部血条 — 大号居中显示
    /// 外发光 + 粗边框 + 大字体 + 百分比 + 颜色渐变
    /// </summary>
    public class HealthBarHUD : MonoBehaviour
    {
        private Image bgImage;
        private Image fillImage;
        private Image borderImage;
        private Image glowImage;
        private TextMeshProUGUI hpText;
        private TextMeshProUGUI hpPercentText;
        private RectTransform barRoot;

        private float displayPct = 1f;
        private float targetPct = 1f;

        void Awake()
        {
            var s = UILayoutManager.Settings;
            float barW = Mathf.Max(s.healthBarWidth, 600f);
            float barH = Mathf.Max(s.healthBarHeight, 28f);
            int fSize = s.healthBarFontSize;

            barRoot = gameObject.AddComponent<RectTransform>();
            barRoot.anchorMin = new Vector2(0.5f, 0f);
            barRoot.anchorMax = new Vector2(0.5f, 0f);
            barRoot.pivot = new Vector2(0.5f, 0f);
            barRoot.anchoredPosition = new Vector2(0, 50);
            barRoot.sizeDelta = new Vector2(barW, barH);

            // 外发光层
            glowImage = UIFactory.CreateImage(barRoot, "Glow",
                UIColors.WithAlpha(UIColors.BrightBlue, 0.10f));
            UIFactory.ApplyRoundedCorners(glowImage, 64, 16);
            glowImage.rectTransform.anchorMin = Vector2.zero;
            glowImage.rectTransform.anchorMax = Vector2.one;
            glowImage.rectTransform.offsetMin = new Vector2(-8, -6);
            glowImage.rectTransform.offsetMax = new Vector2(8, 6);

            // 深色背景
            bgImage = UIFactory.CreateImage(barRoot, "Bg",
                new Color(0.04f, 0.04f, 0.08f, 0.80f));
            UIFactory.ApplyRoundedCorners(bgImage);
            UIFactory.SetFullStretch(bgImage.rectTransform);
            bgImage.raycastTarget = false;

            // 填充
            fillImage = UIFactory.CreateImage(barRoot, "Fill", UIColors.HealthGreen);
            UIFactory.ApplyRoundedCorners(fillImage);
            fillImage.rectTransform.anchorMin = Vector2.zero;
            fillImage.rectTransform.anchorMax = Vector2.one;
            fillImage.rectTransform.offsetMin = new Vector2(2, 2);
            fillImage.rectTransform.offsetMax = new Vector2(-2, -2);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillAmount = 1f;

            // 粗边框
            borderImage = UIFactory.CreateImage(barRoot, "Border",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.75f));
            UIFactory.ApplyRoundedCorners(borderImage);
            UIFactory.SetFullStretch(borderImage.rectTransform);

            // 血量文字（血条内部）
            hpText = UIFactory.CreateText(barRoot, "HPText", "", fSize,
                TextAlignmentOptions.Center, UIColors.White,
                FontStyles.Bold);
            UIFactory.SetFullStretch(hpText.rectTransform);

            // 百分比文字（右侧）
            hpPercentText = UIFactory.CreateText(barRoot, "HPPercent", "100%", fSize + 4,
                TextAlignmentOptions.Left, UIColors.BrightBlue,
                FontStyles.Bold);
            hpPercentText.rectTransform.anchorMin = new Vector2(1f, 0f);
            hpPercentText.rectTransform.anchorMax = new Vector2(1f, 1f);
            hpPercentText.rectTransform.pivot = new Vector2(0f, 0.5f);
            hpPercentText.rectTransform.anchoredPosition = new Vector2(10, 0);
            hpPercentText.rectTransform.sizeDelta = new Vector2(80, barH);
        }

        void Update()
        {
            if (!Mathf.Approximately(displayPct, targetPct))
            {
                displayPct = Mathf.MoveTowards(displayPct, targetPct, Time.deltaTime * 2.5f);
                ApplyFill();
            }
        }

        public void UpdateHealth(float pct, uint current, uint max)
        {
            targetPct = Mathf.Clamp01(pct);
            if (hpText) hpText.text = $"{current} / {max}";
            if (hpPercentText) hpPercentText.text = $"{Mathf.RoundToInt(targetPct * 100)}%";
        }

        private void ApplyFill()
        {
            if (fillImage == null) return;

            fillImage.fillAmount = displayPct;

            // 颜色渐变：绿 → 黄 → 红
            Color fillColor;
            if (displayPct > 0.6f)
                fillColor = UIColors.HealthGreen;
            else if (displayPct > 0.3f)
                fillColor = Color.Lerp(UIColors.HeatYellow, UIColors.HealthGreen,
                    (displayPct - 0.3f) / 0.3f);
            else
                fillColor = Color.Lerp(UIColors.Red, UIColors.HeatYellow,
                    displayPct / 0.3f);
            fillImage.color = fillColor;

            // 边框变色
            if (borderImage)
            {
                if (displayPct < 0.25f)
                    borderImage.color = UIColors.WithAlpha(UIColors.Red, 0.85f);
                else if (displayPct < 0.5f)
                    borderImage.color = UIColors.WithAlpha(UIColors.HeatYellow, 0.65f);
                else
                    borderImage.color = UIColors.WithAlpha(UIColors.LightBlueBorder, 0.75f);
            }

            // 外发光变色
            if (glowImage)
            {
                glowImage.color = displayPct < 0.25f
                    ? UIColors.WithAlpha(UIColors.Red, 0.18f)
                    : UIColors.WithAlpha(UIColors.BrightBlue, 0.10f);
            }

            // 百分比文字颜色
            if (hpPercentText)
                hpPercentText.color = displayPct < 0.3f
                    ? UIColors.Red
                    : UIColors.BrightBlue;
        }
    }
}
