using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 准星环 HUD — 大号准星 + 左侧弹药/热量条 + 右侧敌人信息
    /// 亮度 + 填充量双重编码：颜色越亮 = 数值越高/越危险
    /// </summary>
    public class CrosshairRingHUD : MonoBehaviour
    {
        // ─── 准星中心 ───
        private Image crosshairDot;
        private Image crossH, crossV;

        // ─── 左侧：弹药 + 热量 ───
        private TextMeshProUGUI ammoLabel;
        private TextMeshProUGUI ammoValueText;
        private Image ammoBarBg, ammoBarFill, ammoBarBorder;
        private TextMeshProUGUI heatLabel;
        private TextMeshProUGUI heatValueText;
        private Image heatBarBg, heatBarFill, heatBarBorder;

        // ─── 右侧：敌人信息 ───
        private TextMeshProUGUI enemyTitleText;
        private Image enemyHpBarBg, enemyHpBarFill, enemyHpBarBorder;
        private TextMeshProUGUI enemyHpValueText;
        private TextMeshProUGUI bulletsToKillText;

        private float ringRadius;
        private RectTransform rootRt;
        private float textAlpha;

        // 缓存数据
        private float curHeatPct;
        private float curAmmoPct = 1f;
        private uint curAmmoCount;

        void Awake()
        {
            var s = UILayoutManager.Settings;
            ringRadius = s.crosshairRingRadius;
            textAlpha = s.textOpacity;

            rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            rootRt.sizeDelta = new Vector2(ringRadius * 2 + 500, ringRadius * 2 + 300);

            BuildCrosshair();
            BuildLeftPanel();
            BuildRightPanel();
        }

        // ════════════════════ 准星十字 ════════════════════

        private void BuildCrosshair()
        {
            var s = UILayoutManager.Settings;
            float dotSize = s.crosshairDotSize;
            float lineLen = s.crosshairLineLength;
            float thick = s.crosshairRingThickness + 1;

            // 中心点
            crosshairDot = UIFactory.CreateImage(rootRt, "Dot",
                UIColors.WithAlpha(UIColors.White, 0.95f));
            UIFactory.SetAnchoredSize(crosshairDot.rectTransform, Vector2.zero,
                new Vector2(dotSize, dotSize));

            // 水平线
            crossH = UIFactory.CreateImage(rootRt, "CrossH",
                UIColors.WithAlpha(UIColors.White, 0.80f));
            UIFactory.SetAnchoredSize(crossH.rectTransform, Vector2.zero,
                new Vector2(lineLen, thick));

            // 垂直线
            crossV = UIFactory.CreateImage(rootRt, "CrossV",
                UIColors.WithAlpha(UIColors.White, 0.80f));
            UIFactory.SetAnchoredSize(crossV.rectTransform, Vector2.zero,
                new Vector2(thick, lineLen));
        }

        // ════════════════════ 左侧面板：弹药 + 热量 ════════════════════

        private void BuildLeftPanel()
        {
            var s = UILayoutManager.Settings;
            int fSize = s.crosshairFontSize;
            float x0 = -ringRadius - 60;  // 面板右边缘
            float barW = 140f;
            float barH = 12f;

            // ─── 弹药区 ───
            // 标签 "弹药"
            ammoLabel = UIFactory.CreateText(rootRt, "AmmoLabel", "弹药", fSize - 2,
                TextAlignmentOptions.Right, UIColors.WithAlpha(UIColors.Silver, 0.85f));
            SetLeftPos(ammoLabel.rectTransform, x0 - barW / 2, 42, barW, 22);

            // 弹药数值
            ammoValueText = UIFactory.CreateText(rootRt, "AmmoVal", "∞", fSize + 4,
                TextAlignmentOptions.Right, UIColors.BrightBlue,
                FontStyles.Bold);
            SetLeftPos(ammoValueText.rectTransform, x0 - barW / 2, 22, barW, 28);

            // 弹药条背景
            ammoBarBg = UIFactory.CreateImage(rootRt, "AmmoBg",
                new Color(0.06f, 0.06f, 0.10f, 0.75f));
            UIFactory.ApplyRoundedCorners(ammoBarBg, 32, 6);
            SetLeftPos(ammoBarBg.rectTransform, x0 - barW / 2, 4, barW, barH);

            // 弹药条边框
            ammoBarBorder = UIFactory.CreateImage(rootRt, "AmmoBorder",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.35f));
            UIFactory.ApplyRoundedCorners(ammoBarBorder, 32, 6);
            SetLeftPos(ammoBarBorder.rectTransform, x0 - barW / 2, 4, barW, barH);

            // 弹药条填充
            ammoBarFill = UIFactory.CreateImage(ammoBarBg.transform, "AmmoFill",
                UIColors.HealthGreen);
            UIFactory.ApplyRoundedCorners(ammoBarFill, 32, 6);
            ammoBarFill.rectTransform.anchorMin = Vector2.zero;
            ammoBarFill.rectTransform.anchorMax = Vector2.one;
            ammoBarFill.rectTransform.offsetMin = Vector2.zero;
            ammoBarFill.rectTransform.offsetMax = Vector2.zero;
            ammoBarFill.type = Image.Type.Filled;
            ammoBarFill.fillMethod = Image.FillMethod.Horizontal;
            ammoBarFill.fillOrigin = 0;
            ammoBarFill.fillAmount = 1f;

            // ─── 热量区 ───
            heatLabel = UIFactory.CreateText(rootRt, "HeatLabel", "热量", fSize - 2,
                TextAlignmentOptions.Right, UIColors.WithAlpha(UIColors.Silver, 0.85f));
            SetLeftPos(heatLabel.rectTransform, x0 - barW / 2, -16, barW, 22);

            heatValueText = UIFactory.CreateText(rootRt, "HeatVal", "0%", fSize,
                TextAlignmentOptions.Right, UIColors.HeatYellow,
                FontStyles.Bold);
            SetLeftPos(heatValueText.rectTransform, x0 - barW / 2, -34, barW, 24);

            // 热量条背景
            heatBarBg = UIFactory.CreateImage(rootRt, "HeatBg",
                new Color(0.06f, 0.06f, 0.10f, 0.75f));
            UIFactory.ApplyRoundedCorners(heatBarBg, 32, 6);
            SetLeftPos(heatBarBg.rectTransform, x0 - barW / 2, -50, barW, barH);

            heatBarBorder = UIFactory.CreateImage(rootRt, "HeatBorder",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.35f));
            UIFactory.ApplyRoundedCorners(heatBarBorder, 32, 6);
            SetLeftPos(heatBarBorder.rectTransform, x0 - barW / 2, -50, barW, barH);

            // 热量条填充
            heatBarFill = UIFactory.CreateImage(heatBarBg.transform, "HeatFill",
                UIColors.HeatYellow);
            UIFactory.ApplyRoundedCorners(heatBarFill, 32, 6);
            heatBarFill.rectTransform.anchorMin = Vector2.zero;
            heatBarFill.rectTransform.anchorMax = Vector2.one;
            heatBarFill.rectTransform.offsetMin = Vector2.zero;
            heatBarFill.rectTransform.offsetMax = Vector2.zero;
            heatBarFill.type = Image.Type.Filled;
            heatBarFill.fillMethod = Image.FillMethod.Horizontal;
            heatBarFill.fillOrigin = 0;
            heatBarFill.fillAmount = 0f;
        }

        // ════════════════════ 右侧面板：敌人信息 ════════════════════

        private void BuildRightPanel()
        {
            var s = UILayoutManager.Settings;
            int fSize = s.crosshairFontSize;
            float x0 = ringRadius + 60;
            float barW = 140f;
            float barH = 10f;

            // 标题
            enemyTitleText = UIFactory.CreateText(rootRt, "EnemyTitle", "敌方", fSize - 2,
                TextAlignmentOptions.Left, UIColors.WithAlpha(UIColors.Silver, 0.85f));
            SetRightPos(enemyTitleText.rectTransform, x0, 36, barW, 22);

            // 敌方血量值
            enemyHpValueText = UIFactory.CreateText(rootRt, "EnemyHpVal", "0 / 0", fSize + 2,
                TextAlignmentOptions.Left, UIColors.Red,
                FontStyles.Bold);
            SetRightPos(enemyHpValueText.rectTransform, x0, 16, barW + 20, 26);

            // 敌方血条背景
            enemyHpBarBg = UIFactory.CreateImage(rootRt, "EnemyHpBg",
                new Color(0.06f, 0.06f, 0.10f, 0.75f));
            UIFactory.ApplyRoundedCorners(enemyHpBarBg, 32, 5);
            SetRightPos(enemyHpBarBg.rectTransform, x0, 0, barW, barH);

            enemyHpBarBorder = UIFactory.CreateImage(rootRt, "EnemyHpBdr",
                UIColors.WithAlpha(UIColors.Red, 0.3f));
            UIFactory.ApplyRoundedCorners(enemyHpBarBorder, 32, 5);
            SetRightPos(enemyHpBarBorder.rectTransform, x0, 0, barW, barH);

            // 敌方血条填充
            enemyHpBarFill = UIFactory.CreateImage(enemyHpBarBg.transform, "EnemyFill",
                UIColors.Red);
            UIFactory.ApplyRoundedCorners(enemyHpBarFill, 32, 5);
            enemyHpBarFill.rectTransform.anchorMin = Vector2.zero;
            enemyHpBarFill.rectTransform.anchorMax = Vector2.one;
            enemyHpBarFill.rectTransform.offsetMin = Vector2.zero;
            enemyHpBarFill.rectTransform.offsetMax = Vector2.zero;
            enemyHpBarFill.type = Image.Type.Filled;
            enemyHpBarFill.fillMethod = Image.FillMethod.Horizontal;
            enemyHpBarFill.fillOrigin = 0;
            enemyHpBarFill.fillAmount = 0f;

            // 击杀所需弹量
            bulletsToKillText = UIFactory.CreateText(rootRt, "BtK", "击杀需 0 发", fSize - 2,
                TextAlignmentOptions.Left, UIColors.Orange,
                FontStyles.Bold);
            SetRightPos(bulletsToKillText.rectTransform, x0, -18, barW + 20, 24);
        }

        // ════════════════════ 定位辅助 ════════════════════

        private void SetLeftPos(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void SetRightPos(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // ════════════════════ 数据更新接口 ════════════════════

        public void UpdateAmmo(uint remaining, uint maxAmmo)
        {
            curAmmoCount = remaining;
            if (ammoValueText) ammoValueText.text = remaining.ToString();

            // 弹药百分比
            float pct = maxAmmo > 0 ? (float)remaining / maxAmmo : 1f;
            curAmmoPct = Mathf.Clamp01(pct);

            if (ammoBarFill)
            {
                ammoBarFill.fillAmount = curAmmoPct;
                // 亮度编码：满弹=亮绿 → 空弹=暗红
                Color barColor;
                if (curAmmoPct > 0.5f)
                    barColor = Color.Lerp(UIColors.HeatYellow, UIColors.HealthGreen,
                        (curAmmoPct - 0.5f) * 2f);
                else
                    barColor = Color.Lerp(
                        new Color(0.4f, 0.08f, 0.08f), UIColors.HeatYellow,
                        curAmmoPct * 2f);
                // 亮度随弹量降低而变暗
                float brightness = 0.3f + 0.7f * curAmmoPct;
                barColor = barColor * brightness;
                barColor.a = 1f;
                ammoBarFill.color = barColor;
            }

            // 弹药文字颜色
            if (ammoValueText)
                ammoValueText.color = curAmmoPct < 0.2f
                    ? UIColors.Red
                    : UIColors.BrightBlue;
        }

        public void UpdateHeat(float pct)
        {
            pct = Mathf.Clamp01(pct);
            curHeatPct = pct;

            if (heatValueText) heatValueText.text = $"{Mathf.RoundToInt(pct * 100)}%";

            if (heatBarFill)
            {
                heatBarFill.fillAmount = pct;
                // 亮度编码：低热=暗蓝 → 高热=亮红
                Color barColor;
                if (pct < 0.5f)
                    barColor = Color.Lerp(
                        new Color(0.1f, 0.15f, 0.3f), UIColors.HeatYellow,
                        pct * 2f);
                else
                    barColor = Color.Lerp(UIColors.HeatYellow, UIColors.HeatRed,
                        (pct - 0.5f) * 2f);
                // 亮度随热量升高而变亮
                float brightness = 0.3f + 0.7f * pct;
                barColor = barColor * brightness;
                barColor.a = 1f;
                heatBarFill.color = barColor;
            }

            // 热量文字颜色
            if (heatValueText)
                heatValueText.color = pct > 0.8f
                    ? UIColors.Red
                    : UIColors.HeatYellow;
        }

        public void UpdateEnemyInfo(uint health, uint maxHealth, int bulletsToKill)
        {
            if (maxHealth == 0 || health == 0)
            {
                // 未检测到敌方
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
                // 颜色: 高血=亮红, 低血=暗红
                float brightness = 0.35f + 0.65f * pct;
                enemyHpBarFill.color = new Color(
                    UIColors.Red.r * brightness,
                    UIColors.Red.g * brightness * 0.5f,
                    UIColors.Red.b * brightness * 0.3f, 1f);
            }
            if (bulletsToKillText)
                bulletsToKillText.text = $"击杀需 {bulletsToKill} 发";
        }
    }
}
