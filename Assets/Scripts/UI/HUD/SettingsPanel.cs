using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UI.Core;
using UI.RobotSelection;
using System.Collections;
using System.Collections.Generic;

namespace UI.HUD
{
    /// <summary>
    /// 设置面板 — 现代 FPS 风格，深色毛玻璃面板 + 图标 + 分组滑块
    /// 视觉设计对标 BUFF/DEBUFF 风格：圆角条形、蓝色主题、动画反馈
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        private Canvas settingsCanvas;
        private GameObject panelRoot;
        private bool isOpen;
        private Button gearButton;

        // 存所有滑块引用以便重置
        private readonly List<SliderBinding> sliderBindings = new List<SliderBinding>();

        private struct SliderBinding
        {
            public Slider slider;
            public float defaultValue;
            public System.Action<float> onChange;
            public TextMeshProUGUI valueText;
            public string fmt, unit;
        }

        // ─── 设计常量 ───
        // 面板背景色（深蓝黑，高透明度）
        private static readonly Color PanelBgColor = new Color(0.04f, 0.05f, 0.10f, 0.94f);
        // 分组标题背景
        private static readonly Color SectionBgColor = new Color(0.08f, 0.10f, 0.18f, 0.85f);
        // 行背景（交替）
        private static readonly Color RowBgEven = new Color(0.06f, 0.07f, 0.13f, 0.55f);
        private static readonly Color RowBgOdd = new Color(0.04f, 0.05f, 0.10f, 0.40f);
        // 滑块颜色
        private static readonly Color SliderTrackColor = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        private static readonly Color SliderFillColor = new Color(0.22f, 0.55f, 0.95f, 0.70f);
        private static readonly Color SliderHandleColor = new Color(0.85f, 0.90f, 0.98f, 1f);
        // 按钮色
        private static readonly Color BtnSaveColor = new Color(0.16f, 0.50f, 0.88f, 0.80f);
        private static readonly Color BtnResetColor = new Color(0.80f, 0.25f, 0.20f, 0.65f);
        private static readonly Color BtnReselectColor = new Color(0.75f, 0.60f, 0.18f, 0.70f);
        // 标题强调色
        private static readonly Color AccentBlue = new Color(0.35f, 0.72f, 0.98f, 1f);

        private int rowIndex;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildGearButton();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!isOpen) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                ToggleSettings();
        }

        // ═══════════════════ 齿轮按钮（带图标） ═══════════════════

        private void BuildGearButton()
        {
            var c = UIFactory.CreateCanvas("GearBtnCanvas", 20000);
            c.transform.SetParent(transform, false);

            // 圆角按钮容器
            var btnGo = new GameObject("GearBtn");
            btnGo.transform.SetParent(c.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0, 1);
            btnRt.anchorMax = new Vector2(0, 1);
            btnRt.pivot = new Vector2(0, 1);
            btnRt.anchoredPosition = new Vector2(16, -16);
            btnRt.sizeDelta = new Vector2(56, 56);

            var btnBg = btnGo.AddComponent<Image>();
            btnBg.color = UIColors.WithAlpha(UIColors.BrightBlue, 0.85f);
            UIFactory.ApplyRoundedCorners(btnBg, 64, 16);

            // 蓝色边框
            var btnBorder = UIFactory.CreateImage(btnGo.transform, "Border",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.70f));
            UIFactory.ApplyRoundedCorners(btnBorder, 64, 18);
            UIFactory.SetFullStretch(btnBorder.rectTransform);
            btnBorder.rectTransform.offsetMin = new Vector2(-2, -2);
            btnBorder.rectTransform.offsetMax = new Vector2(2, 2);
            btnBorder.raycastTarget = false;

            gearButton = btnGo.AddComponent<Button>();
            gearButton.targetGraphic = btnBg;
            gearButton.transition = Selectable.Transition.None;
            gearButton.onClick.AddListener(ToggleSettings);

            // 图标
            var iconSprite = IconManager.Get(IconManager.ICON_SETTING);
            if (iconSprite != null)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(btnGo.transform, false);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.color = UIColors.White;
                iconImg.raycastTarget = false;
                iconImg.preserveAspect = true;
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.15f, 0.15f);
                iconRt.anchorMax = new Vector2(0.85f, 0.85f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
            }
            else
            {
                // 回退：文字
                var txt = UIFactory.CreateText(btnGo.transform, "Label", "⚙", 28,
                    TextAlignmentOptions.Center, AccentBlue, FontStyles.Bold);
                UIFactory.SetFullStretch(txt.rectTransform);
            }
        }

        private void ToggleSettings()
        {
            isOpen = !isOpen;
            if (isOpen) ShowPanel(); else HidePanel();
        }

        // ═══════════════════ 主面板 ═══════════════════

        private void ShowPanel()
        {
            if (panelRoot != null) { panelRoot.SetActive(true); return; }

            sliderBindings.Clear();
            rowIndex = 0;

            settingsCanvas = UIFactory.CreateCanvas("SettingsCanvas", 25000);
            settingsCanvas.transform.SetParent(transform, false);
            panelRoot = settingsCanvas.gameObject;
            var root = settingsCanvas.transform;

            // ── 半透明遮罩 ──
            var overlay = UIFactory.CreateFullScreenImage(root, "Overlay",
                new Color(0.0f, 0.0f, 0.02f, 0.55f));
            overlay.raycastTarget = true;
            var ob = overlay.gameObject.AddComponent<Button>();
            ob.transition = Selectable.Transition.None;
            ob.onClick.AddListener(ToggleSettings);

            // ── 面板容器 ──
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = new Vector2(0.22f, 0.04f);
            panel.anchorMax = new Vector2(0.78f, 0.96f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            // 面板背景
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelBgColor;
            UIFactory.ApplyRoundedCorners(bg, 64, 16);
            bg.raycastTarget = true;

            // 外发光边框
            var borderGlow = UIFactory.CreateImage(panel, "BorderGlow",
                UIColors.WithAlpha(AccentBlue, 0.12f));
            UIFactory.ApplyRoundedCorners(borderGlow, 64, 18);
            UIFactory.SetFullStretch(borderGlow.rectTransform);
            borderGlow.rectTransform.offsetMin = new Vector2(-2, -2);
            borderGlow.rectTransform.offsetMax = new Vector2(2, 2);

            // ── 标题栏 ──
            BuildTitleBar(panel);

            // ── 滚动内容区 ──
            BuildScrollContent(panel);

            // ── 底部操作栏 ──
            BuildBottomBar(panel);
        }

        // ═══════════════════ 标题栏 ═══════════════════

        private void BuildTitleBar(RectTransform panel)
        {
            // 标题容器
            var titleBar = new GameObject("TitleBar").AddComponent<RectTransform>();
            titleBar.SetParent(panel, false);
            titleBar.anchorMin = new Vector2(0, 0.92f);
            titleBar.anchorMax = new Vector2(1, 1);
            titleBar.offsetMin = Vector2.zero;
            titleBar.offsetMax = Vector2.zero;

            // 标题背景（上部圆角融合）
            var titleBg = titleBar.gameObject.AddComponent<Image>();
            titleBg.color = SectionBgColor;
            UIFactory.ApplyRoundedCorners(titleBg, 64, 16);
            titleBg.raycastTarget = false;

            // 设置图标
            var iconSprite = IconManager.Get(IconManager.ICON_SETTING);
            if (iconSprite != null)
            {
                var iconImg = UIFactory.CreateImage(titleBar, "TitleIcon", AccentBlue);
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.rectTransform.anchorMin = new Vector2(0.02f, 0.15f);
                iconImg.rectTransform.anchorMax = new Vector2(0.06f, 0.85f);
                iconImg.rectTransform.offsetMin = Vector2.zero;
                iconImg.rectTransform.offsetMax = Vector2.zero;
            }

            // 标题文本
            var title = UIFactory.CreateText(titleBar, "TitleText", "系 统 设 置", 38,
                TextAlignmentOptions.Left, AccentBlue, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.07f, 0f);
            title.rectTransform.anchorMax = new Vector2(0.6f, 1f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            // 关闭按钮（右侧X图标）
            var closeGo = new GameObject("CloseBtn");
            closeGo.transform.SetParent(titleBar, false);
            var closeRt = closeGo.AddComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(0.92f, 0.15f);
            closeRt.anchorMax = new Vector2(0.98f, 0.85f);
            closeRt.offsetMin = Vector2.zero;
            closeRt.offsetMax = Vector2.zero;

            var closeBg = closeGo.AddComponent<Image>();
            closeBg.color = UIColors.WithAlpha(BtnResetColor, 0.45f);
            UIFactory.ApplyRoundedCorners(closeBg, 32, 10);

            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            closeBtn.transition = Selectable.Transition.None;
            closeBtn.onClick.AddListener(ToggleSettings);

            var cancelIcon = IconManager.Get(IconManager.ICON_CANCEL);
            if (cancelIcon != null)
            {
                var ci = UIFactory.CreateImage(closeGo.transform, "X", UIColors.White);
                ci.sprite = cancelIcon;
                ci.preserveAspect = true;
                ci.raycastTarget = false;
                ci.rectTransform.anchorMin = new Vector2(0.2f, 0.2f);
                ci.rectTransform.anchorMax = new Vector2(0.8f, 0.8f);
                ci.rectTransform.offsetMin = Vector2.zero;
                ci.rectTransform.offsetMax = Vector2.zero;
            }
            else
            {
                var xt = UIFactory.CreateText(closeGo.transform, "X", "✕", 26,
                    TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
                UIFactory.SetFullStretch(xt.rectTransform);
            }

            // 底部分隔线
            var div = UIFactory.CreateImage(panel, "TitleDiv", UIColors.WithAlpha(AccentBlue, 0.35f));
            div.rectTransform.anchorMin = new Vector2(0.02f, 0.915f);
            div.rectTransform.anchorMax = new Vector2(0.98f, 0.918f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;
        }

        // ═══════════════════ 滚动内容 ═══════════════════

        private void BuildScrollContent(RectTransform panel)
        {
            // ScrollView
            var scrollGo = new GameObject("ScrollView");
            scrollGo.transform.SetParent(panel, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.015f, 0.095f);
            scrollRt.anchorMax = new Vector2(0.985f, 0.910f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var maskImg = scrollGo.AddComponent<Image>();
            maskImg.color = new Color(0, 0, 0, 0.01f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 2;
            vlg.padding = new RectOffset(8, 8, 6, 20);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 45f;
            scroll.viewport = scrollRt;

            // ────── 填充设置项 ──────
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            // ▌通知设置
            AddSectionHeader(contentGo.transform, "通 知 设 置", IconManager.ICON_INFORM);
            AddSliderRow(contentGo.transform, "通知显示时长", "s",
                s.notificationDuration, d.notificationDuration, 0.5f, 10f,
                v => s.notificationDuration = v);

            // ▌开镜设置
            AddSectionHeader(contentGo.transform, "开 镜 设 置", IconManager.ICON_PILL);
            AddSliderRow(contentGo.transform, "开镜倍率", "x",
                s.aimZoomFactor, d.aimZoomFactor, 1f, 4f,
                v => s.aimZoomFactor = v);

            // ▌受击提示
            AddSectionHeader(contentGo.transform, "受 击 提 示", IconManager.ICON_FATAL_WARNING);
            AddSliderRow(contentGo.transform, "闪烁持续时间", "s",
                s.hitFlashDuration, d.hitFlashDuration, 0.1f, 1f,
                v => s.hitFlashDuration = v);
            AddSliderRow(contentGo.transform, "低血量阈值", "%",
                s.lowHealthThreshold * 100f, d.lowHealthThreshold * 100f, 10f, 90f,
                v => s.lowHealthThreshold = v / 100f);

            // ▌准星设置
            AddSectionHeader(contentGo.transform, "准 星 设 置", IconManager.ICON_PILL);
            AddSliderRow(contentGo.transform, "准星环半径", "px",
                s.crosshairRingRadius, d.crosshairRingRadius, 80f, 300f,
                v => s.crosshairRingRadius = v);
            AddSliderRow(contentGo.transform, "准星点大小", "px",
                s.crosshairDotSize, d.crosshairDotSize, 4f, 20f,
                v => s.crosshairDotSize = v);
            AddSliderRow(contentGo.transform, "准星线长度", "px",
                s.crosshairLineLength, d.crosshairLineLength, 20f, 80f,
                v => s.crosshairLineLength = v);

            // ▌血条设置
            AddSectionHeader(contentGo.transform, "血 条 设 置", IconManager.ICON_WARNING);
            AddSliderRow(contentGo.transform, "血条宽度", "px",
                s.healthBarWidth, d.healthBarWidth, 300f, 1200f,
                v => s.healthBarWidth = v);
            AddSliderRow(contentGo.transform, "血条高度", "px",
                s.healthBarHeight, d.healthBarHeight, 14f, 60f,
                v => s.healthBarHeight = v);

            // ▌字体大小
            AddSectionHeader(contentGo.transform, "字 体 大 小", IconManager.ICON_SETTING);
            AddSliderRow(contentGo.transform, "准星区域字体", "pt",
                s.crosshairFontSize, d.crosshairFontSize, 20f, 60f,
                v => s.crosshairFontSize = Mathf.RoundToInt(v));
            AddSliderRow(contentGo.transform, "血条字体", "pt",
                s.healthBarFontSize, d.healthBarFontSize, 16f, 48f,
                v => s.healthBarFontSize = Mathf.RoundToInt(v));
            AddSliderRow(contentGo.transform, "通知字体", "pt",
                s.notificationFontSize, d.notificationFontSize, 20f, 48f,
                v => s.notificationFontSize = Mathf.RoundToInt(v));
            AddSliderRow(contentGo.transform, "BUFF字体", "pt",
                s.buffFontSize, d.buffFontSize, 18f, 42f,
                v => s.buffFontSize = Mathf.RoundToInt(v));
        }

        // ═══════════════════ 分组标题 ═══════════════════

        private void AddSectionHeader(Transform content, string text, string iconName)
        {
            var go = new GameObject($"Section_{text}");
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 44;
            le.flexibleWidth = 1;

            var rt = go.AddComponent<RectTransform>();

            // 背景条
            var bg = go.AddComponent<Image>();
            bg.color = SectionBgColor;
            UIFactory.ApplyRoundedCorners(bg, 32, 8);
            bg.raycastTarget = false;

            // 左侧蓝色竖条
            var accent = UIFactory.CreateImage(rt, "Accent", AccentBlue);
            accent.rectTransform.anchorMin = new Vector2(0.005f, 0.12f);
            accent.rectTransform.anchorMax = new Vector2(0.012f, 0.88f);
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;

            // 图标
            var iconSprite = IconManager.Get(iconName);
            float textStart = 0.025f;
            if (iconSprite != null)
            {
                var iconImg = UIFactory.CreateImage(rt, "Icon",
                    UIColors.WithAlpha(AccentBlue, 0.85f));
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.rectTransform.anchorMin = new Vector2(0.020f, 0.15f);
                iconImg.rectTransform.anchorMax = new Vector2(0.055f, 0.85f);
                iconImg.rectTransform.offsetMin = Vector2.zero;
                iconImg.rectTransform.offsetMax = Vector2.zero;
                textStart = 0.062f;
            }

            // 标题文本
            var label = UIFactory.CreateText(rt, "Label", text, 28,
                TextAlignmentOptions.Left, AccentBlue, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(textStart, 0f);
            label.rectTransform.anchorMax = new Vector2(0.95f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            rowIndex = 0; // 重置行计数
        }

        // ═══════════════════ 滑块行 ═══════════════════

        private void AddSliderRow(Transform content, string label, string unit,
            float value, float defaultVal, float min, float max,
            System.Action<float> onChange)
        {
            string fmt = (max - min) > 50 ? "F0" : "F1";

            var go = new GameObject($"Row_{label}");
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 52;
            le.flexibleWidth = 1;

            // 行背景（交替色）
            var rowBg = go.AddComponent<Image>();
            rowBg.color = (rowIndex % 2 == 0) ? RowBgEven : RowBgOdd;
            UIFactory.ApplyRoundedCorners(rowBg, 32, 6);
            rowBg.raycastTarget = false;
            rowIndex++;

            var rowRt = go.GetComponent<RectTransform>();

            // ── 标签 (3% ~ 28%) ──
            var lbl = UIFactory.CreateText(rowRt, "Label", label, 24,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.03f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.28f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            // ── Slider (30% ~ 72%) ──
            var sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(rowRt, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.30f, 0.22f);
            sliderRt.anchorMax = new Vector2(0.72f, 0.78f);
            sliderRt.offsetMin = Vector2.zero;
            sliderRt.offsetMax = Vector2.zero;

            // 滑块轨道背景
            var sliderBg = sliderGo.AddComponent<Image>();
            sliderBg.color = SliderTrackColor;
            UIFactory.ApplyRoundedCorners(sliderBg, 32, 10);
            sliderBg.raycastTarget = true;

            // Fill Area + Fill
            var fillAreaGo = new GameObject("FillArea");
            fillAreaGo.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillAreaGo.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.10f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.90f);
            fillAreaRt.offsetMin = new Vector2(4, 0);
            fillAreaRt.offsetMax = new Vector2(-4, 0);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = SliderFillColor;
            UIFactory.ApplyRoundedCorners(fillImg, 32, 8);

            // Handle Area + Handle
            var handleAreaGo = new GameObject("HandleArea");
            handleAreaGo.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(handleAreaRt);
            handleAreaRt.offsetMin = new Vector2(8, 0);
            handleAreaRt.offsetMax = new Vector2(-8, 0);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18, 0);
            handleRt.anchorMin = new Vector2(0, 0.05f);
            handleRt.anchorMax = new Vector2(0, 0.95f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = SliderHandleColor;
            UIFactory.ApplyRoundedCorners(handleImg, 24, 8);
            handleImg.raycastTarget = true;

            // Slider 组件
            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            // ── 数值 (74% ~ 88%) ──
            var valText = UIFactory.CreateText(rowRt, "Value",
                $"{value.ToString(fmt)}{unit}", 24,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            valText.rectTransform.anchorMin = new Vector2(0.74f, 0f);
            valText.rectTransform.anchorMax = new Vector2(0.88f, 1f);
            valText.rectTransform.offsetMin = Vector2.zero;
            valText.rectTransform.offsetMax = Vector2.zero;

            // ── 重置按钮 (90% ~ 98%) ──
            var resetGo = new GameObject("ResetBtn");
            resetGo.transform.SetParent(rowRt, false);
            var resetRt = resetGo.AddComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0.90f, 0.15f);
            resetRt.anchorMax = new Vector2(0.98f, 0.85f);
            resetRt.offsetMin = Vector2.zero;
            resetRt.offsetMax = Vector2.zero;

            var resetBg = resetGo.AddComponent<Image>();
            resetBg.color = UIColors.WithAlpha(UIColors.Orange, 0.25f);
            UIFactory.ApplyRoundedCorners(resetBg, 32, 8);

            var resetBtn = resetGo.AddComponent<Button>();
            resetBtn.targetGraphic = resetBg;
            resetBtn.transition = Selectable.Transition.None;

            // 重置图标 — 用文字 "↺"
            var resetLabel = UIFactory.CreateText(resetGo.transform, "Lbl", "↺", 22,
                TextAlignmentOptions.Center, UIColors.Orange, FontStyles.Bold);
            UIFactory.SetFullStretch(resetLabel.rectTransform);

            // ── 事件绑定 ──
            string localFmt = fmt;
            string localUnit = unit;
            slider.onValueChanged.AddListener(v =>
            {
                onChange?.Invoke(v);
                if (valText) valText.text = $"{v.ToString(localFmt)}{localUnit}";
            });

            float localDefault = defaultVal;
            resetBtn.onClick.AddListener(() =>
            {
                slider.value = localDefault;
                onChange?.Invoke(localDefault);
                if (valText) valText.text = $"{localDefault.ToString(localFmt)}{localUnit}";
                if (resetBg) StartCoroutine(FlashColor(resetBg,
                    UIColors.WithAlpha(UIColors.HealthGreen, 0.55f)));
            });

            sliderBindings.Add(new SliderBinding
            {
                slider = slider,
                defaultValue = defaultVal,
                onChange = onChange,
                valueText = valText,
                fmt = localFmt,
                unit = localUnit
            });
        }

        // ═══════════════════ 底部操作栏 ═══════════════════

        private void BuildBottomBar(RectTransform panel)
        {
            // 底部条背景
            var barGo = new GameObject("BottomBar");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(1, 0.090f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            var barBg = barGo.AddComponent<Image>();
            barBg.color = new Color(0.03f, 0.04f, 0.08f, 0.80f);
            UIFactory.ApplyRoundedCorners(barBg, 64, 16);
            barBg.raycastTarget = false;

            // 顶部分隔线
            var div = UIFactory.CreateImage(panel, "BottomDiv",
                UIColors.WithAlpha(AccentBlue, 0.20f));
            div.rectTransform.anchorMin = new Vector2(0.02f, 0.090f);
            div.rectTransform.anchorMax = new Vector2(0.98f, 0.093f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            // 重新选择兵种
            CreateBottomButton(barRt, "Reselect", "重新选择兵种",
                BtnReselectColor, 0.02f, 0.12f, 0.28f, 0.88f,
                OnReselectClicked);

            // 保存设置
            CreateBottomButton(barRt, "Save", "保  存  设  置",
                BtnSaveColor, 0.32f, 0.12f, 0.68f, 0.88f,
                () =>
                {
                    UILayoutManager.Save();
                    wmj.Log.I("[Settings] 设置已保存", wmj.Log.Tag.UI);
                });

            // 全部重置
            CreateBottomButton(barRt, "ResetAll", "全部重置",
                BtnResetColor, 0.72f, 0.12f, 0.98f, 0.88f, OnResetAllClicked);
        }

        private Button CreateBottomButton(RectTransform parent, string name, string label,
            Color bgColor, float x0, float y0, float x1, float y1,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = go.AddComponent<Image>();
            bg.color = bgColor;
            UIFactory.ApplyRoundedCorners(bg, 32, 10);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);

            var txt = UIFactory.CreateText(go.transform, "Label", label, 24,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(txt.rectTransform);

            return btn;
        }

        // ═══════════════════ 辅助 ═══════════════════

        private IEnumerator FlashColor(Image img, Color flashColor)
        {
            var original = img.color;
            img.color = flashColor;
            yield return new WaitForSeconds(0.35f);
            if (img) img.color = original;
        }

        private void OnResetAllClicked()
        {
            UILayoutManager.Data.hudSettings = new HUDSettings();
            UILayoutManager.Data.elements.Clear();
            UILayoutManager.Save();

            // 重建面板
            HidePanel();
            if (panelRoot != null) Destroy(panelRoot);
            panelRoot = null;
            isOpen = false;
            ToggleSettings();
        }

        private void OnReselectClicked()
        {
            ToggleSettings();
            if (BattleHUD.Instance != null) BattleHUD.Instance.Shutdown();
            RobotSelectionBootstrap.ResetSelection();
            RobotSelectionPanel.Show(result =>
            {
                RobotSelectionBootstrap.ApplySelection(result);
                wmj.Log.I($"[Settings] 重新选择完成: {result}", wmj.Log.Tag.UI);
            });
        }

        private void HidePanel()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
