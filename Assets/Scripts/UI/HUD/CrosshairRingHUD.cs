using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 准星环 HUD — 环形热量/弹药指示器 + 合并信息面板
    /// 热量环（黄→红）环绕准星内圈，弹药环（亮蓝）在外侧
    /// 弹药与热量数值合并在环下方单个面板内，整齐对齐
    /// </summary>
    public class CrosshairRingHUD : MonoBehaviour
    {
        // ─── 准星中心 ───
        private Image crosshairDot;
        private Image crossH, crossV;

        // ─── 环形指示器 ───
        private Image heatRingBg, heatRingFill;
        private Image ammoRingBg, ammoRingFill;

        // ─── 合并信息面板（环下方） ───
        private Image infoPanel;
        private TextMeshProUGUI ammoLabel, ammoValueText;
        private TextMeshProUGUI heatLabel, heatValueText;

        // ─── 右侧：敌人信息面板 ───
        private Image enemyPanel;
        private TextMeshProUGUI enemyTitleText;
        private Image enemyHpBarBg, enemyHpBarFill;
        private TextMeshProUGUI enemyHpValueText, bulletsToKillText;

        private float heatRingRadius, ammoRingRadius, ringThickness;
        private RectTransform rootRt;

        private float curHeatPct;
        private float curAmmoPct = 1f;

        private const int RING_TEX = 256;

        void Awake()
        {
            var s = UILayoutManager.Settings;
            ringThickness = s.crosshairRingThickness;
            heatRingRadius = s.crosshairRingRadius;
            ammoRingRadius = heatRingRadius + ringThickness + s.crosshairHeatRingGap;

            rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            float total = (ammoRingRadius + ringThickness + 10) * 2 + 400;
            rootRt.sizeDelta = new Vector2(total, total);

            BuildCrosshair();
            BuildHeatRing();
            BuildAmmoRing();
            BuildInfoPanel();
            BuildEnemyPanel();
        }

        // ════════════════════ 准星十字 ════════════════════

        private void BuildCrosshair()
        {
            var s = UILayoutManager.Settings;
            float dotSize = s.crosshairDotSize;
            float lineLen = s.crosshairLineLength;

            crosshairDot = UIFactory.CreateImage(rootRt, "Dot",
                UIColors.WithAlpha(UIColors.White, 0.95f));
            UIFactory.SetAnchoredSize(crosshairDot.rectTransform, Vector2.zero,
                new Vector2(dotSize, dotSize));

            crossH = UIFactory.CreateImage(rootRt, "CrossH",
                UIColors.WithAlpha(UIColors.White, 0.80f));
            UIFactory.SetAnchoredSize(crossH.rectTransform, Vector2.zero,
                new Vector2(lineLen, 2.5f));

            crossV = UIFactory.CreateImage(rootRt, "CrossV",
                UIColors.WithAlpha(UIColors.White, 0.80f));
            UIFactory.SetAnchoredSize(crossV.rectTransform, Vector2.zero,
                new Vector2(2.5f, lineLen));
        }

        // ════════════════════ 热量环（内环，黄→红） ════════════════════

        private void BuildHeatRing()
        {
            float ringSize = heatRingRadius * 2 + ringThickness * 2;
            float texThick = ringThickness * (RING_TEX / ringSize);
            var spr = UIShapeHelper.GetRingSprite(RING_TEX, RING_TEX / 2f - 4f, texThick);

            heatRingBg = UIFactory.CreateImage(rootRt, "HeatRingBg",
                new Color(0.15f, 0.12f, 0.05f, 0.25f));
            heatRingBg.sprite = spr;
            heatRingBg.preserveAspect = true;
            UIFactory.SetAnchoredSize(heatRingBg.rectTransform, Vector2.zero,
                new Vector2(ringSize, ringSize));

            heatRingFill = UIFactory.CreateImage(rootRt, "HeatRingFill", UIColors.HeatYellow);
            heatRingFill.sprite = spr;
            heatRingFill.type = Image.Type.Filled;
            heatRingFill.fillMethod = Image.FillMethod.Radial360;
            heatRingFill.fillOrigin = (int)Image.Origin360.Bottom;
            heatRingFill.fillClockwise = true;
            heatRingFill.fillAmount = 0f;
            heatRingFill.preserveAspect = true;
            UIFactory.SetAnchoredSize(heatRingFill.rectTransform, Vector2.zero,
                new Vector2(ringSize, ringSize));
        }

        // ════════════════════ 弹药环（外环，亮蓝） ════════════════════

        private void BuildAmmoRing()
        {
            float ringSize = ammoRingRadius * 2 + ringThickness * 2;
            float texThick = ringThickness * (RING_TEX / ringSize);
            var spr = UIShapeHelper.GetRingSprite(RING_TEX, RING_TEX / 2f - 4f, texThick);

            ammoRingBg = UIFactory.CreateImage(rootRt, "AmmoRingBg",
                new Color(0.05f, 0.10f, 0.18f, 0.25f));
            ammoRingBg.sprite = spr;
            ammoRingBg.preserveAspect = true;
            UIFactory.SetAnchoredSize(ammoRingBg.rectTransform, Vector2.zero,
                new Vector2(ringSize, ringSize));

            ammoRingFill = UIFactory.CreateImage(rootRt, "AmmoRingFill", UIColors.BrightBlue);
            ammoRingFill.sprite = spr;
            ammoRingFill.type = Image.Type.Filled;
            ammoRingFill.fillMethod = Image.FillMethod.Radial360;
            ammoRingFill.fillOrigin = (int)Image.Origin360.Bottom;
            ammoRingFill.fillClockwise = true;
            ammoRingFill.fillAmount = 1f;
            ammoRingFill.preserveAspect = true;
            UIFactory.SetAnchoredSize(ammoRingFill.rectTransform, Vector2.zero,
                new Vector2(ringSize, ringSize));
        }

        // ════════════════════ 合并信息面板（弹药+热量，环下方居中） ════════════════════

        private void BuildInfoPanel()
        {
            int fSize = UILayoutManager.Settings.crosshairFontSize;
            float panelY = -(ammoRingRadius + ringThickness + 14);
            float panelW = 240f;
            float panelH = 52f;

            infoPanel = UIFactory.CreateImage(rootRt, "InfoPanel",
                new Color(0.04f, 0.06f, 0.12f, 0.70f));
            UIFactory.ApplyRoundedCorners(infoPanel, 48, 12);
            infoPanel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            infoPanel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            infoPanel.rectTransform.pivot = new Vector2(0.5f, 1f);
            infoPanel.rectTransform.anchoredPosition = new Vector2(0, panelY);
            infoPanel.rectTransform.sizeDelta = new Vector2(panelW, panelH);

            var prt = infoPanel.rectTransform;

            // 分隔线
            var divider = UIFactory.CreateImage(prt, "Div",
                UIColors.WithAlpha(UIColors.Silver, 0.20f));
            divider.rectTransform.anchorMin = new Vector2(0.50f, 0.12f);
            divider.rectTransform.anchorMax = new Vector2(0.505f, 0.88f);
            divider.rectTransform.offsetMin = Vector2.zero;
            divider.rectTransform.offsetMax = Vector2.zero;

            // ── 左半：弹药 ──
            ammoLabel = UIFactory.CreateText(prt, "AmmoLbl", "弹药", fSize - 8,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.75f));
            ammoLabel.rectTransform.anchorMin = new Vector2(0.02f, 0.58f);
            ammoLabel.rectTransform.anchorMax = new Vector2(0.48f, 0.95f);
            ammoLabel.rectTransform.offsetMin = Vector2.zero;
            ammoLabel.rectTransform.offsetMax = Vector2.zero;

            ammoValueText = UIFactory.CreateText(prt, "AmmoVal", "∞", fSize,
                TextAlignmentOptions.Center, UIColors.BrightBlue, FontStyles.Bold);
            ammoValueText.rectTransform.anchorMin = new Vector2(0.02f, 0.05f);
            ammoValueText.rectTransform.anchorMax = new Vector2(0.48f, 0.62f);
            ammoValueText.rectTransform.offsetMin = Vector2.zero;
            ammoValueText.rectTransform.offsetMax = Vector2.zero;

            // ── 右半：热量 ──
            heatLabel = UIFactory.CreateText(prt, "HeatLbl", "热量", fSize - 8,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.75f));
            heatLabel.rectTransform.anchorMin = new Vector2(0.52f, 0.58f);
            heatLabel.rectTransform.anchorMax = new Vector2(0.98f, 0.95f);
            heatLabel.rectTransform.offsetMin = Vector2.zero;
            heatLabel.rectTransform.offsetMax = Vector2.zero;

            heatValueText = UIFactory.CreateText(prt, "HeatVal", "0%", fSize,
                TextAlignmentOptions.Center, UIColors.HeatYellow, FontStyles.Bold);
            heatValueText.rectTransform.anchorMin = new Vector2(0.52f, 0.05f);
            heatValueText.rectTransform.anchorMax = new Vector2(0.98f, 0.62f);
            heatValueText.rectTransform.offsetMin = Vector2.zero;
            heatValueText.rectTransform.offsetMax = Vector2.zero;
        }

        // ════════════════════ 敌人信息面板（右侧） ════════════════════

        private void BuildEnemyPanel()
        {
            int fSize = UILayoutManager.Settings.crosshairFontSize;
            float px = ammoRingRadius + 50;

            enemyPanel = UIFactory.CreateImage(rootRt, "EnemyPanel",
                new Color(0.04f, 0.06f, 0.12f, 0.65f));
            UIFactory.ApplyRoundedCorners(enemyPanel, 48, 12);
            enemyPanel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            enemyPanel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            enemyPanel.rectTransform.pivot = new Vector2(0f, 0.5f);
            enemyPanel.rectTransform.anchoredPosition = new Vector2(px, 30);
            enemyPanel.rectTransform.sizeDelta = new Vector2(190, 105);
            var prt = enemyPanel.rectTransform;

            enemyTitleText = UIFactory.CreateText(prt, "Title", "敌方", fSize - 4,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.85f));
            enemyTitleText.rectTransform.anchorMin = new Vector2(0.05f, 0.74f);
            enemyTitleText.rectTransform.anchorMax = new Vector2(0.95f, 0.96f);
            enemyTitleText.rectTransform.offsetMin = Vector2.zero;
            enemyTitleText.rectTransform.offsetMax = Vector2.zero;

            enemyHpValueText = UIFactory.CreateText(prt, "HpVal", "0 / 0", fSize - 2,
                TextAlignmentOptions.Center, UIColors.Red, FontStyles.Bold);
            enemyHpValueText.rectTransform.anchorMin = new Vector2(0.05f, 0.52f);
            enemyHpValueText.rectTransform.anchorMax = new Vector2(0.95f, 0.74f);
            enemyHpValueText.rectTransform.offsetMin = Vector2.zero;
            enemyHpValueText.rectTransform.offsetMax = Vector2.zero;
            enemyHpValueText.enableWordWrapping = false;
            enemyHpValueText.overflowMode = TextOverflowModes.Overflow;

            enemyHpBarBg = UIFactory.CreateImage(prt, "HpBg",
                new Color(0.06f, 0.06f, 0.10f, 0.8f));
            UIFactory.ApplyRoundedCorners(enemyHpBarBg, 32, 5);
            enemyHpBarBg.rectTransform.anchorMin = new Vector2(0.06f, 0.34f);
            enemyHpBarBg.rectTransform.anchorMax = new Vector2(0.94f, 0.48f);
            enemyHpBarBg.rectTransform.offsetMin = Vector2.zero;
            enemyHpBarBg.rectTransform.offsetMax = Vector2.zero;

            enemyHpBarFill = UIFactory.CreateImage(enemyHpBarBg.transform, "Fill", UIColors.Red);
            UIFactory.ApplyRoundedCorners(enemyHpBarFill, 32, 5);
            enemyHpBarFill.rectTransform.anchorMin = Vector2.zero;
            enemyHpBarFill.rectTransform.anchorMax = Vector2.one;
            enemyHpBarFill.rectTransform.offsetMin = Vector2.zero;
            enemyHpBarFill.rectTransform.offsetMax = Vector2.zero;
            enemyHpBarFill.type = Image.Type.Filled;
            enemyHpBarFill.fillMethod = Image.FillMethod.Horizontal;
            enemyHpBarFill.fillOrigin = 0;
            enemyHpBarFill.fillAmount = 0f;

            bulletsToKillText = UIFactory.CreateText(prt, "BtK", "击杀需 0 发", fSize - 4,
                TextAlignmentOptions.Center, UIColors.Orange, FontStyles.Bold);
            bulletsToKillText.rectTransform.anchorMin = new Vector2(0.05f, 0.04f);
            bulletsToKillText.rectTransform.anchorMax = new Vector2(0.95f, 0.30f);
            bulletsToKillText.rectTransform.offsetMin = Vector2.zero;
            bulletsToKillText.rectTransform.offsetMax = Vector2.zero;
            bulletsToKillText.enableWordWrapping = false;
            bulletsToKillText.overflowMode = TextOverflowModes.Overflow;
        }

        // ════════════════════ 数据更新接口 ════════════════════

        public void UpdateAmmo(uint remaining, uint maxAmmo)
        {
            if (ammoValueText) ammoValueText.text = remaining.ToString();
            float pct = maxAmmo > 0 ? (float)remaining / maxAmmo : 1f;
            curAmmoPct = Mathf.Clamp01(pct);

            if (ammoRingFill)
            {
                ammoRingFill.fillAmount = curAmmoPct;
                float b = 0.4f + 0.6f * curAmmoPct;
                ammoRingFill.color = new Color(
                    UIColors.BrightBlue.r * b, UIColors.BrightBlue.g * b,
                    UIColors.BrightBlue.b * b, 0.9f);
            }
            if (ammoValueText)
                ammoValueText.color = curAmmoPct < 0.2f ? UIColors.Red : UIColors.BrightBlue;
        }

        public void UpdateHeat(float pct)
        {
            pct = Mathf.Clamp01(pct);
            curHeatPct = pct;
            if (heatValueText) heatValueText.text = $"{Mathf.RoundToInt(pct * 100)}%";

            if (heatRingFill)
            {
                heatRingFill.fillAmount = pct;
                Color c = pct < 0.5f
                    ? UIColors.HeatYellow
                    : Color.Lerp(UIColors.HeatYellow, UIColors.Red, (pct - 0.5f) * 2f);
                float b = 0.5f + 0.5f * pct;
                c = c * b; c.a = 0.9f;
                heatRingFill.color = c;
            }
            if (heatValueText)
                heatValueText.color = pct > 0.8f ? UIColors.Red : UIColors.HeatYellow;
        }

        public void UpdateEnemyInfo(uint health, uint maxHealth, int bulletsToKill)
        {
            if (maxHealth == 0 || health == 0)
            {
                if (enemyHpValueText) enemyHpValueText.text = "0 / 0";
                if (enemyHpBarFill) enemyHpBarFill.fillAmount = 0f;
                if (bulletsToKillText) bulletsToKillText.text = "击杀需 0 发";
                return;
            }
            float pct = Mathf.Clamp01((float)health / maxHealth);
            if (enemyHpValueText) enemyHpValueText.text = $"{health} / {maxHealth}";
            if (enemyHpBarFill)
            {
                enemyHpBarFill.fillAmount = pct;
                float b = 0.35f + 0.65f * pct;
                enemyHpBarFill.color = new Color(
                    UIColors.Red.r * b, UIColors.Red.g * b * 0.5f,
                    UIColors.Red.b * b * 0.3f, 1f);
            }
            if (bulletsToKillText)
                bulletsToKillText.text = $"击杀需 {bulletsToKill} 发";
        }
    }
}
