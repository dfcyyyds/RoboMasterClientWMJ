using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 六边形弹药购买提示弹窗 — 画面中央显示购买信息
    /// 
    /// 视觉风格：
    ///   - 六边形深色背景 + 亮色边框
    ///   - 居中显示购买信息文字
    ///   - 显示时长可在设置中配置（默认 0.5s）
    ///   - 淡入淡出动画
    /// </summary>
    public class HexPopupHUD : MonoBehaviour
    {
        // ─── UI 元素 ───
        private Canvas popupCanvas;
        private CanvasGroup canvasGroup;
        private RectTransform containerRt;
        private Image hexBgImage;
        private Image hexBorderImage;
        private TextMeshProUGUI infoText;
        private TextMeshProUGUI subText;

        // ─── 动画 ───
        private float displayTimer;
        private float totalDuration;
        private bool isShowing;

        private const float FADE_IN_TIME = 0.08f;
        private const float FADE_OUT_TIME = 0.15f;

        // ─── 颜色 ───
        private static readonly Color HexBgColor = new Color(0.04f, 0.06f, 0.12f, 0.88f);
        private static readonly Color HexBorderColor = new Color(0.30f, 0.70f, 0.95f, 0.75f);
        private static readonly Color InfoTextColor = new Color(0.90f, 0.95f, 1.0f, 1f);
        private static readonly Color SubTextColor = new Color(0.60f, 0.75f, 0.90f, 0.85f);

        // ─── 六边形 Sprite 缓存 ───
        private static Sprite _hexSprite;
        private static Sprite _hexBorderSprite;

        public void Initialize()
        {
            BuildUI();
            SetVisible(false);
        }

        private void BuildUI()
        {
            popupCanvas = UIFactory.CreateCanvas("HexPopupCanvas", 18000);
            popupCanvas.transform.SetParent(transform, false);
            canvasGroup = UIFactory.EnsureCanvasGroup(popupCanvas.gameObject);
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            var root = popupCanvas.transform;

            // 容器（屏幕中央偏上）
            var containerGo = new GameObject("HexContainer");
            containerGo.transform.SetParent(root, false);
            containerRt = containerGo.AddComponent<RectTransform>();
            containerRt.anchorMin = new Vector2(0.5f, 0.5f);
            containerRt.anchorMax = new Vector2(0.5f, 0.5f);
            containerRt.pivot = new Vector2(0.5f, 0.5f);
            containerRt.anchoredPosition = new Vector2(0, 60); // 略偏上
            containerRt.sizeDelta = new Vector2(420, 160);

            // 六边形背景
            hexBgImage = UIFactory.CreateImage(containerRt, "HexBg", HexBgColor);
            hexBgImage.sprite = GetHexSprite(false);
            hexBgImage.type = Image.Type.Simple;
            hexBgImage.preserveAspect = false;
            hexBgImage.raycastTarget = false;
            UIFactory.SetFullStretch(hexBgImage.rectTransform);

            // 六边形边框
            hexBorderImage = UIFactory.CreateImage(containerRt, "HexBorder", HexBorderColor);
            hexBorderImage.sprite = GetHexSprite(true);
            hexBorderImage.type = Image.Type.Simple;
            hexBorderImage.preserveAspect = false;
            hexBorderImage.raycastTarget = false;
            UIFactory.SetFullStretch(hexBorderImage.rectTransform);
            hexBorderImage.rectTransform.offsetMin = new Vector2(-2, -2);
            hexBorderImage.rectTransform.offsetMax = new Vector2(2, 2);

            // 装饰线条（左右）
            var leftLine = UIFactory.CreateImage(containerRt, "LeftLine",
                UIColors.WithAlpha(HexBorderColor, 0.5f));
            leftLine.rectTransform.anchorMin = new Vector2(0.05f, 0.45f);
            leftLine.rectTransform.anchorMax = new Vector2(0.15f, 0.55f);
            leftLine.rectTransform.offsetMin = Vector2.zero;
            leftLine.rectTransform.offsetMax = Vector2.zero;

            var rightLine = UIFactory.CreateImage(containerRt, "RightLine",
                UIColors.WithAlpha(HexBorderColor, 0.5f));
            rightLine.rectTransform.anchorMin = new Vector2(0.85f, 0.45f);
            rightLine.rectTransform.anchorMax = new Vector2(0.95f, 0.55f);
            rightLine.rectTransform.offsetMin = Vector2.zero;
            rightLine.rectTransform.offsetMax = Vector2.zero;

            // 主信息文字
            infoText = UIFactory.CreateText(containerRt, "InfoText", "", 30,
                TextAlignmentOptions.Center, InfoTextColor, FontStyles.Bold);
            infoText.rectTransform.anchorMin = new Vector2(0.08f, 0.35f);
            infoText.rectTransform.anchorMax = new Vector2(0.92f, 0.85f);
            infoText.rectTransform.offsetMin = Vector2.zero;
            infoText.rectTransform.offsetMax = Vector2.zero;

            // 副标题文字
            subText = UIFactory.CreateText(containerRt, "SubText", "", 18,
                TextAlignmentOptions.Center, SubTextColor);
            subText.rectTransform.anchorMin = new Vector2(0.10f, 0.08f);
            subText.rectTransform.anchorMax = new Vector2(0.90f, 0.38f);
            subText.rectTransform.offsetMin = Vector2.zero;
            subText.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 显示购买提示
        /// </summary>
        /// <param name="ammoCount">购买的弹丸数量</param>
        /// <param name="ammoType">弹种类型 ("17mm" / "42mm")</param>
        /// <param name="cost">花费金币</param>
        /// <param name="duration">显示时长（秒），≤0 则使用设置中的默认值</param>
        public void ShowPurchase(int ammoCount, string ammoType, uint cost, float duration = -1f)
        {
            if (duration <= 0f)
                duration = UILayoutManager.Settings.ammoPurchasePopupDuration;

            string typeLabel = ammoType == "42mm" ? "42mm 弹丸" : "17mm 弹丸";
            infoText.text = $"购买了 {ammoCount} 发{typeLabel}";
            subText.text = $"花费 {cost} 金币";

            // 根据弹种调整边框颜色
            Color accentColor = ammoType == "42mm"
                ? new Color(0.95f, 0.60f, 0.15f, 0.75f)  // 橙色 42mm
                : HexBorderColor;                           // 蓝色 17mm
            hexBorderImage.color = accentColor;

            totalDuration = duration;
            displayTimer = 0f;
            isShowing = true;
            SetVisible(true);
        }

        void Update()
        {
            if (!isShowing) return;

            displayTimer += Time.deltaTime;
            float totalWithFade = totalDuration + FADE_OUT_TIME;

            // 淡入
            if (displayTimer < FADE_IN_TIME)
            {
                float t = displayTimer / FADE_IN_TIME;
                canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t);
                float scale = Mathf.Lerp(0.85f, 1f, t);
                containerRt.localScale = Vector3.one * scale;
            }
            // 正常显示
            else if (displayTimer < totalDuration)
            {
                canvasGroup.alpha = 1f;
                containerRt.localScale = Vector3.one;
            }
            // 淡出
            else if (displayTimer < totalWithFade)
            {
                float t = (displayTimer - totalDuration) / FADE_OUT_TIME;
                canvasGroup.alpha = Mathf.SmoothStep(1f, 0f, t);
            }
            // 结束
            else
            {
                isShowing = false;
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (popupCanvas != null)
                popupCanvas.gameObject.SetActive(visible);
        }

        public void Shutdown()
        {
            if (popupCanvas != null) Destroy(popupCanvas.gameObject);
        }

        // ═══════════════════ 六边形 Sprite 生成 ═══════════════════

        /// <summary>
        /// 程序化生成六边形 Sprite（扁平顶六边形，水平拉伸）
        /// </summary>
        private static Sprite GetHexSprite(bool borderOnly)
        {
            if (!borderOnly && _hexSprite != null) return _hexSprite;
            if (borderOnly && _hexBorderSprite != null) return _hexBorderSprite;

            int texW = 256;
            int texH = 128;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[texW * texH];
            float cx = texW / 2f;
            float cy = texH / 2f;

            // 扁六边形顶点（flat-top hexagon）
            // 使用水平宽度 = texW * 0.48, 垂直高度 = texH * 0.46
            float hw = texW * 0.48f; // 半宽
            float hh = texH * 0.46f; // 半高
            float sideW = hw * 0.30f; // 左右斜边 X 偏移

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float px = x - cx;
                    float py = y - cy;

                    // 六边形 SDF（扁平顶）
                    float dist = HexSDF(px, py, hw, hh, sideW);

                    if (borderOnly)
                    {
                        // 仅边框：距边缘 2px 内
                        float borderDist = Mathf.Abs(dist) - 1.5f;
                        float a = Mathf.Clamp01(1f - borderDist);
                        byte ab = (byte)(a * 255);
                        pixels[y * texW + x] = new Color32(255, 255, 255, ab);
                    }
                    else
                    {
                        // 填充
                        float a = Mathf.Clamp01(-dist + 1f);
                        byte ab = (byte)(Mathf.Clamp01(a) * 255);
                        pixels[y * texW + x] = new Color32(255, 255, 255, ab);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = borderOnly ? "HexBorder" : "HexFill";

            if (borderOnly)
                _hexBorderSprite = sprite;
            else
                _hexSprite = sprite;

            return sprite;
        }

        /// <summary>
        /// 六边形有符号距离函数（SDF）
        /// 负值 = 内部, 正值 = 外部
        /// </summary>
        private static float HexSDF(float px, float py, float hw, float hh, float sideW)
        {
            // 利用对称性取绝对值
            float ax = Mathf.Abs(px);
            float ay = Mathf.Abs(py);

            // 六边形由三个约束定义：
            // 1. |y| <= hh
            // 2. |x| <= hw
            // 3. 斜边: ax * hh + ay * sideW <= hh * hw  (变形)
            //    简化为: ax / hw + ay / hh <= 1 + sideW/hw  对于六边形左右倾斜
            // 使用更直接的方式：
            // 对于 flat-top hex: 
            //   上下界: |y| <= hh
            //   左右界: |x| <= hw  
            //   斜边: |x| <= hw - (|y| - (hh - sideW_y)) * slope  当 |y| > flatY
            float flatY = hh * 0.50f; // 平顶部分的 Y 范围
            float maxDist = 0f;

            // 约束1: 垂直
            float d1 = ay - hh;
            maxDist = Mathf.Max(maxDist, d1);

            // 约束2: 水平
            float d2 = ax - hw;
            maxDist = Mathf.Max(maxDist, d2);

            // 约束3: 斜边（六边形的倾斜面）
            if (ay > flatY)
            {
                float slope = hw / (hh - flatY);
                float xLimit = hw - (ay - flatY) * slope;
                float d3 = ax - xLimit;
                maxDist = Mathf.Max(maxDist, d3);
            }

            return maxDist;
        }
    }
}
