using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;
using UI.Core;
using UI.RobotSelection;
using System.Collections;
using System.Collections.Generic;

// EventTrigger 已在 UnityEngine.EventSystems 中

namespace UI.HUD
{
    /// <summary>
    /// 设置面板 — 左侧多级侧边栏菜单
    /// 模式一：参数配置（滑块调节各 HUD 参数）
    /// 模式二：UI 布局自定义（缩略图预览 + 拖拽定位）
    /// 现代风格：渐变背景、柔和边框、阴影
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public static SettingsPanel Instance { get; private set; }

        private Canvas settingsCanvas;
        private GameObject panelRoot;
        private bool isOpen;
        private Button gearButton;

        // 侧边栏
        private readonly List<SidebarItem> sidebarItems = new List<SidebarItem>();
        private int activeSidebarIndex = -1;
        private RectTransform contentArea;

        // 参数页面
        private readonly List<SliderBinding> sliderBindings = new List<SliderBinding>();
        private int rowIndex;

        // 布局编辑器
        private RectTransform minimapRoot;
        private readonly List<LayoutHandle> layoutHandles = new List<LayoutHandle>();

        // 实时预览防抖
        private Coroutine previewCoroutine;
        private const float PREVIEW_DELAY = 0.6f;

        private struct SliderBinding
        {
            public Slider slider;
            public float defaultValue;
            public System.Action<float> onChange;
            public TextMeshProUGUI valueText;
            public string fmt, unit;
        }

        private class SidebarItem
        {
            public Button button;
            public Image bg;
            public Image accent;
            public TextMeshProUGUI label;
            public string pageId;
        }

        private class LayoutHandle
        {
            public string id;
            public RectTransform rt;
            public UIElementLayout layout;
        }

        // ─── 设计常量 ───
        private static readonly Color PanelBgColor = new Color(0.04f, 0.05f, 0.10f, 0.94f);
        private static readonly Color SidebarBgColor = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color SectionBgColor = new Color(0.08f, 0.10f, 0.18f, 0.85f);
        private static readonly Color RowBgEven = new Color(0.06f, 0.07f, 0.13f, 0.55f);
        private static readonly Color RowBgOdd = new Color(0.04f, 0.05f, 0.10f, 0.40f);
        private static readonly Color SliderTrackColor = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        private static readonly Color SliderFillColor = new Color(0.22f, 0.55f, 0.95f, 0.70f);
        private static readonly Color SliderHandleColor = new Color(0.85f, 0.90f, 0.98f, 1f);
        private static readonly Color BtnSaveColor = new Color(0.16f, 0.50f, 0.88f, 0.80f);
        private static readonly Color BtnResetColor = new Color(0.80f, 0.25f, 0.20f, 0.65f);
        private static readonly Color BtnReselectColor = new Color(0.75f, 0.60f, 0.18f, 0.70f);
        private static readonly Color AccentBlue = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color SidebarItemHover = new Color(0.08f, 0.12f, 0.22f, 0.70f);
        private static readonly Color SidebarItemActive = new Color(0.12f, 0.18f, 0.32f, 0.85f);
        private static readonly Color MinimapBg = new Color(0.06f, 0.07f, 0.12f, 0.80f);

        // 侧边栏菜单定义
        private static readonly string[] MenuIds = {
            "matchinfo", "notify", "aim", "hit", "crosshair", "health", "buff", "font", "layout"
        };
        private static readonly string[] MenuLabels = {
            "对局信息", "通知设置", "开镜设置", "受击提示", "准星设置", "血条设置", "BUFF设置", "字体大小", "UI 布局"
        };
        private static readonly string[] MenuIcons = {
            IconManager.ICON_INFORM, IconManager.ICON_INFORM, IconManager.ICON_PILL,
            IconManager.ICON_FATAL_WARNING, IconManager.ICON_PILL, IconManager.ICON_WARNING,
            IconManager.ICON_PILL, IconManager.ICON_SETTING, IconManager.ICON_PULL
        };

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

        // ═══════════════════ 齿轮按钮 ═══════════════════

        private void BuildGearButton()
        {
            var c = UIFactory.CreateCanvas("GearBtnCanvas", 20000);
            c.transform.SetParent(transform, false);

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

            var btnBorder = UIFactory.CreateImage(btnGo.transform, "Border",
                UIColors.WithAlpha(UIColors.LightBlueBorder, 0.70f));
            UIFactory.ApplyRoundedCorners(btnBorder, 64, 18);
            UIFactory.SetFullStretch(btnBorder.rectTransform);
            btnBorder.rectTransform.offsetMin = new Vector2(-2, -2);
            btnBorder.rectTransform.offsetMax = new Vector2(2, 2);
            btnBorder.raycastTarget = false;

            gearButton = btnGo.AddComponent<Button>();
            gearButton.targetGraphic = btnBg;
            gearButton.transition = Selectable.Transition.ColorTint;
            var gc = gearButton.colors;
            gc.normalColor = UIColors.WithAlpha(UIColors.BrightBlue, 0.85f);
            gc.highlightedColor = UIColors.WithAlpha(UIColors.BrightBlue, 1f);
            gc.pressedColor = new Color(0.55f, 0.85f, 1f, 1f);
            gc.fadeDuration = 0.10f;
            gearButton.colors = gc;
            gearButton.onClick.AddListener(ToggleSettings);

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

        // ═══════════════════ 主面板（侧边栏 + 内容区） ═══════════════════

        private void ShowPanel()
        {
            if (panelRoot != null) { panelRoot.SetActive(true); return; }

            sliderBindings.Clear();
            sidebarItems.Clear();
            layoutHandles.Clear();
            rowIndex = 0;
            activeSidebarIndex = -1;

            settingsCanvas = UIFactory.CreateCanvas("SettingsCanvas", 25000);
            settingsCanvas.transform.SetParent(transform, false);
            panelRoot = settingsCanvas.gameObject;
            var root = settingsCanvas.transform;

            // 半透明遮罩
            var overlay = UIFactory.CreateFullScreenImage(root, "Overlay",
                new Color(0.0f, 0.0f, 0.02f, 0.55f));
            overlay.raycastTarget = true;
            var ob = overlay.gameObject.AddComponent<Button>();
            ob.transition = Selectable.Transition.None;
            ob.onClick.AddListener(ToggleSettings);

            // 整体面板容器
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = new Vector2(0.08f, 0.04f);
            panel.anchorMax = new Vector2(0.92f, 0.96f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelBgColor;
            UIFactory.ApplyRoundedCorners(bg, 64, 16);
            bg.raycastTarget = true;

            // 外发光
            var glow = UIFactory.CreateImage(panel, "Glow",
                UIColors.WithAlpha(AccentBlue, 0.10f));
            UIFactory.ApplyRoundedCorners(glow, 64, 18);
            UIFactory.SetFullStretch(glow.rectTransform);
            glow.rectTransform.offsetMin = new Vector2(-2, -2);
            glow.rectTransform.offsetMax = new Vector2(2, 2);

            // ── 标题栏 ──
            BuildTitleBar(panel);

            // ── 左侧边栏 ──
            BuildSidebar(panel);

            // ── 右侧内容区 ──
            BuildContentArea(panel);

            // ── 底部操作栏 ──
            BuildBottomBar(panel);

            // 默认选中第一项
            SelectSidebarItem(0);
        }

        // ═══════════════════ 标题栏 ═══════════════════

        private void BuildTitleBar(RectTransform panel)
        {
            var titleBar = new GameObject("TitleBar").AddComponent<RectTransform>();
            titleBar.SetParent(panel, false);
            titleBar.anchorMin = new Vector2(0, 0.93f);
            titleBar.anchorMax = new Vector2(1, 1);
            titleBar.offsetMin = Vector2.zero;
            titleBar.offsetMax = Vector2.zero;

            var titleBg = titleBar.gameObject.AddComponent<Image>();
            titleBg.color = SectionBgColor;
            UIFactory.ApplyRoundedCorners(titleBg, 64, 16);
            titleBg.raycastTarget = false;

            // 图标
            var iconSprite = IconManager.Get(IconManager.ICON_SETTING);
            if (iconSprite != null)
            {
                var iconImg = UIFactory.CreateImage(titleBar, "TitleIcon", AccentBlue);
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.rectTransform.anchorMin = new Vector2(0.015f, 0.15f);
                iconImg.rectTransform.anchorMax = new Vector2(0.04f, 0.85f);
                iconImg.rectTransform.offsetMin = Vector2.zero;
                iconImg.rectTransform.offsetMax = Vector2.zero;
            }

            var title = UIFactory.CreateText(titleBar, "Title", "系 统 设 置", 34,
                TextAlignmentOptions.Left, AccentBlue, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.05f, 0f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            // 关闭按钮
            var closeGo = new GameObject("CloseBtn");
            closeGo.transform.SetParent(titleBar, false);
            var closeRt = closeGo.AddComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(0.955f, 0.15f);
            closeRt.anchorMax = new Vector2(0.99f, 0.85f);
            closeRt.offsetMin = Vector2.zero;
            closeRt.offsetMax = Vector2.zero;

            var closeBg = closeGo.AddComponent<Image>();
            closeBg.color = UIColors.WithAlpha(BtnResetColor, 0.45f);
            UIFactory.ApplyRoundedCorners(closeBg, 32, 10);

            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeBg;
            closeBtn.transition = Selectable.Transition.ColorTint;
            var ccb = closeBtn.colors;
            ccb.normalColor = UIColors.WithAlpha(BtnResetColor, 0.45f);
            ccb.highlightedColor = UIColors.WithAlpha(BtnResetColor, 0.75f);
            ccb.pressedColor = UIColors.WithAlpha(BtnResetColor, 1f);
            ccb.fadeDuration = 0.10f;
            closeBtn.colors = ccb;
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
                var xt = UIFactory.CreateText(closeGo.transform, "X", "✕", 24,
                    TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
                UIFactory.SetFullStretch(xt.rectTransform);
            }

            // 分隔线
            var div = UIFactory.CreateImage(panel, "TitleDiv",
                UIColors.WithAlpha(AccentBlue, 0.30f));
            div.rectTransform.anchorMin = new Vector2(0.01f, 0.925f);
            div.rectTransform.anchorMax = new Vector2(0.99f, 0.928f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;
        }

        // ═══════════════════ 左侧侧边栏 ═══════════════════

        private void BuildSidebar(RectTransform panel)
        {
            // 侧边栏容器
            var sidebarGo = new GameObject("Sidebar");
            sidebarGo.transform.SetParent(panel, false);
            var sidebarRt = sidebarGo.AddComponent<RectTransform>();
            sidebarRt.anchorMin = new Vector2(0.005f, 0.085f);
            sidebarRt.anchorMax = new Vector2(0.18f, 0.920f);
            sidebarRt.offsetMin = Vector2.zero;
            sidebarRt.offsetMax = Vector2.zero;

            var sidebarBg = sidebarGo.AddComponent<Image>();
            sidebarBg.color = SidebarBgColor;
            UIFactory.ApplyRoundedCorners(sidebarBg, 48, 12);
            sidebarBg.raycastTarget = false;

            // 菜单项
            float itemH = 1f / MenuIds.Length;
            for (int i = 0; i < MenuIds.Length; i++)
            {
                var item = CreateSidebarItem(sidebarRt, i, MenuLabels[i], MenuIcons[i],
                    itemH * (MenuIds.Length - 1 - i), itemH * (MenuIds.Length - i));
                item.pageId = MenuIds[i];
                sidebarItems.Add(item);
            }

            // 垂直分隔线
            var vdiv = UIFactory.CreateImage(panel, "SidebarDiv",
                UIColors.WithAlpha(AccentBlue, 0.20f));
            vdiv.rectTransform.anchorMin = new Vector2(0.185f, 0.09f);
            vdiv.rectTransform.anchorMax = new Vector2(0.188f, 0.92f);
            vdiv.rectTransform.offsetMin = Vector2.zero;
            vdiv.rectTransform.offsetMax = Vector2.zero;
        }

        private SidebarItem CreateSidebarItem(RectTransform parent, int index,
            string label, string iconName, float yMin, float yMax)
        {
            var go = new GameObject($"Menu_{index}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, yMin + 0.005f);
            rt.anchorMax = new Vector2(0.96f, yMax - 0.005f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var itemBg = go.AddComponent<Image>();
            itemBg.color = new Color(0, 0, 0, 0);
            UIFactory.ApplyRoundedCorners(itemBg, 32, 8);
            itemBg.raycastTarget = true;

            // 左侧选中指示条
            var accent = UIFactory.CreateImage(rt, "Accent", AccentBlue);
            accent.rectTransform.anchorMin = new Vector2(0, 0.1f);
            accent.rectTransform.anchorMax = new Vector2(0.025f, 0.9f);
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;
            accent.gameObject.SetActive(false);

            // 图标
            float textStart = 0.06f;
            var iconSprite = IconManager.Get(iconName);
            if (iconSprite != null)
            {
                var iconImg = UIFactory.CreateImage(rt, "Icon",
                    UIColors.WithAlpha(UIColors.Silver, 0.7f));
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.rectTransform.anchorMin = new Vector2(0.05f, 0.18f);
                iconImg.rectTransform.anchorMax = new Vector2(0.22f, 0.82f);
                iconImg.rectTransform.offsetMin = Vector2.zero;
                iconImg.rectTransform.offsetMax = Vector2.zero;
                textStart = 0.26f;
            }

            // 文字
            var lbl = UIFactory.CreateText(rt, "Label", label, 22,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(textStart, 0f);
            lbl.rectTransform.anchorMax = new Vector2(1f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = itemBg;
            btn.transition = Selectable.Transition.ColorTint;
            var sbc = btn.colors;
            sbc.normalColor = new Color(0, 0, 0, 0);
            sbc.highlightedColor = SidebarItemHover;
            sbc.pressedColor = SidebarItemActive;
            sbc.selectedColor = new Color(0, 0, 0, 0);
            sbc.fadeDuration = 0.12f;
            btn.colors = sbc;
            int idx = index;
            btn.onClick.AddListener(() => SelectSidebarItem(idx));

            return new SidebarItem { button = btn, bg = itemBg, accent = accent, label = lbl };
        }

        private void SelectSidebarItem(int index)
        {
            if (index == activeSidebarIndex) return;
            activeSidebarIndex = index;

            // 更新侧边栏视觉
            for (int i = 0; i < sidebarItems.Count; i++)
            {
                bool active = i == index;
                sidebarItems[i].bg.color = active ? SidebarItemActive : new Color(0, 0, 0, 0);
                sidebarItems[i].accent.gameObject.SetActive(active);
                sidebarItems[i].label.color = active ? AccentBlue : UIColors.Silver;
            }

            // 清理旧内容
            if (contentArea != null)
            {
                for (int i = contentArea.childCount - 1; i >= 0; i--)
                    Destroy(contentArea.GetChild(i).gameObject);
            }

            sliderBindings.Clear();
            layoutHandles.Clear();
            rowIndex = 0;

            // 构建对应页面
            string pageId = sidebarItems[index].pageId;
            switch (pageId)
            {
                case "matchinfo": BuildMatchInfoPage(); break;
                case "notify": BuildNotifyPage(); break;
                case "aim": BuildAimPage(); break;
                case "hit": BuildHitPage(); break;
                case "crosshair": BuildCrosshairPage(); break;
                case "health": BuildHealthPage(); break;
                case "buff": BuildBuffPage(); break;
                case "font": BuildFontPage(); break;
                case "layout": BuildLayoutPage(); break;
            }
        }

        // ═══════════════════ 右侧内容区框架 ═══════════════════

        private void BuildContentArea(RectTransform panel)
        {
            var areaGo = new GameObject("ContentArea");
            areaGo.transform.SetParent(panel, false);
            contentArea = areaGo.AddComponent<RectTransform>();
            contentArea.anchorMin = new Vector2(0.192f, 0.085f);
            contentArea.anchorMax = new Vector2(0.995f, 0.920f);
            contentArea.offsetMin = Vector2.zero;
            contentArea.offsetMax = Vector2.zero;

            // 裁剪遮罩确保内容不会溢出
            var clipMask = areaGo.AddComponent<Image>();
            clipMask.color = new Color(0, 0, 0, 0.01f);
            areaGo.AddComponent<Mask>().showMaskGraphic = false;
        }

        // ─── 创建带滚动的参数页面容器 ───
        private Transform CreateParamScrollContent(string pageName)
        {
            var scrollGo = new GameObject($"Scroll_{pageName}");
            scrollGo.transform.SetParent(contentArea, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            // 填满整个内容区
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            // 不再重复添加 Mask（contentArea 已有）

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            // 设置初始宽度跟随父级
            contentRt.sizeDelta = new Vector2(0, 0);

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 3;
            vlg.padding = new RectOffset(4, 4, 4, 20);
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

            return contentGo.transform;
        }

        // ═══════════════════ 参数页面 ═══════════════════

        private void BuildMatchInfoPage()
        {
            var c = CreateParamScrollContent("matchinfo");
            var s = UILayoutManager.Settings;

            AddSectionHeader(c, "对 局 信 息 显 示", IconManager.ICON_INFORM);
            AddToggleRow(c, "显示比赛阶段", s.showMatchStage,
                v => { s.showMatchStage = v; ScheduleLivePreview(); });
            AddToggleRow(c, "显示倒计时", s.showMatchTimer,
                v => { s.showMatchTimer = v; ScheduleLivePreview(); });
            AddToggleRow(c, "显示轮次", s.showMatchRound,
                v => { s.showMatchRound = v; ScheduleLivePreview(); });
            AddToggleRow(c, "显示比分", s.showMatchScore,
                v => { s.showMatchScore = v; ScheduleLivePreview(); });
            AddToggleRow(c, "显示经济", s.showMatchEconomy,
                v => { s.showMatchEconomy = v; ScheduleLivePreview(); });
        }

        private void BuildNotifyPage()
        {
            var c = CreateParamScrollContent("notify");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "通 知 设 置", IconManager.ICON_INFORM);
            AddSliderRow(c, "通知显示时长", "s",
                s.notificationDuration, d.notificationDuration, 0.5f, 10f,
                v => s.notificationDuration = v);
            AddSliderRow(c, "最大通知数", "",
                s.maxNotifications, d.maxNotifications, 1f, 10f,
                v => s.maxNotifications = Mathf.RoundToInt(v));
        }

        private void BuildAimPage()
        {
            var c = CreateParamScrollContent("aim");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "开 镜 设 置", IconManager.ICON_PILL);
            AddToggleRow(c, "启用射击自动开镜", s.aimZoomEnabled,
                v => { s.aimZoomEnabled = v; ScheduleLivePreview(); });
            AddSliderRow(c, "开镜倍率", "x",
                s.aimZoomFactor, d.aimZoomFactor, 1f, 4f,
                v => s.aimZoomFactor = v);
            AddSliderRow(c, "聚焦速度", "",
                s.aimZoomSpeed, d.aimZoomSpeed, 2f, 20f,
                v => s.aimZoomSpeed = v);
            AddSliderRow(c, "关镜延迟", "s",
                s.aimZoomCloseDelay, d.aimZoomCloseDelay, 0.5f, 8f,
                v => s.aimZoomCloseDelay = v);
        }

        private void BuildHitPage()
        {
            var c = CreateParamScrollContent("hit");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "受 击 提 示", IconManager.ICON_FATAL_WARNING);
            AddSliderRow(c, "闪烁持续时间", "s",
                s.hitFlashDuration, d.hitFlashDuration, 0.1f, 1f,
                v => s.hitFlashDuration = v);
            AddSliderRow(c, "低血量阈值", "%",
                s.lowHealthThreshold * 100f, d.lowHealthThreshold * 100f, 10f, 90f,
                v => s.lowHealthThreshold = v / 100f);
        }

        private void BuildCrosshairPage()
        {
            var c = CreateParamScrollContent("crosshair");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "准 星 设 置", IconManager.ICON_PILL);
            AddSliderRow(c, "准星环半径", "px",
                s.crosshairRingRadius, d.crosshairRingRadius, 40f, 200f,
                v => s.crosshairRingRadius = v);
            AddSliderRow(c, "环线宽度", "px",
                s.crosshairRingThickness, d.crosshairRingThickness, 3f, 16f,
                v => s.crosshairRingThickness = v);
            AddSliderRow(c, "环间距", "px",
                s.crosshairHeatRingGap, d.crosshairHeatRingGap, 2f, 20f,
                v => s.crosshairHeatRingGap = v);
            AddSliderRow(c, "准星点大小", "px",
                s.crosshairDotSize, d.crosshairDotSize, 4f, 20f,
                v => s.crosshairDotSize = v);
            AddSliderRow(c, "准星线长度", "px",
                s.crosshairLineLength, d.crosshairLineLength, 20f, 80f,
                v => s.crosshairLineLength = v);
        }

        private void BuildHealthPage()
        {
            var c = CreateParamScrollContent("health");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "血 条 设 置", IconManager.ICON_WARNING);
            AddSliderRow(c, "血条宽度", "px",
                s.healthBarWidth, d.healthBarWidth, 400f, 1400f,
                v => s.healthBarWidth = v);
            AddSliderRow(c, "血条高度", "px",
                s.healthBarHeight, d.healthBarHeight, 20f, 60f,
                v => s.healthBarHeight = v);
        }

        private void BuildBuffPage()
        {
            var c = CreateParamScrollContent("buff");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "BUFF 设 置", IconManager.ICON_PILL);
            AddSliderRow(c, "单列可见数", "",
                s.buffMaxVisible, d.buffMaxVisible, 2f, 8f,
                v => s.buffMaxVisible = Mathf.RoundToInt(v));
            AddSliderRow(c, "列宽度", "px",
                s.buffColumnWidth, d.buffColumnWidth, 150f, 350f,
                v => s.buffColumnWidth = v);
        }

        private void BuildFontPage()
        {
            var c = CreateParamScrollContent("font");
            var s = UILayoutManager.Settings;
            var d = HUDSettings.Defaults();

            AddSectionHeader(c, "字 体 大 小", IconManager.ICON_SETTING);
            AddSliderRow(c, "准星区域字体", "pt",
                s.crosshairFontSize, d.crosshairFontSize, 20f, 60f,
                v => s.crosshairFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "血条字体", "pt",
                s.healthBarFontSize, d.healthBarFontSize, 16f, 48f,
                v => s.healthBarFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "通知字体", "pt",
                s.notificationFontSize, d.notificationFontSize, 20f, 48f,
                v => s.notificationFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "BUFF字体", "pt",
                s.buffFontSize, d.buffFontSize, 18f, 42f,
                v => s.buffFontSize = Mathf.RoundToInt(v));
            AddSliderRow(c, "文字透明度", "%",
                s.textOpacity * 100f, d.textOpacity * 100f, 30f, 100f,
                v => s.textOpacity = v / 100f);
        }

        // ═══════════════════ UI 布局编辑器页面 ═══════════════════

        private void BuildLayoutPage()
        {
            // 缩略图预览区
            var minimapGo = new GameObject("Minimap");
            minimapGo.transform.SetParent(contentArea, false);
            minimapRoot = minimapGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(minimapRoot);

            // 缩略图背景
            var mmBg = minimapGo.AddComponent<Image>();
            mmBg.color = MinimapBg;
            UIFactory.ApplyRoundedCorners(mmBg, 48, 12);
            mmBg.raycastTarget = true;

            // 16:9 边框参考线
            var refBorder = UIFactory.CreateImage(minimapRoot, "RefBorder",
                UIColors.WithAlpha(AccentBlue, 0.20f));
            UIFactory.ApplyRoundedCorners(refBorder, 48, 8);
            UIFactory.SetFullStretch(refBorder.rectTransform);
            refBorder.rectTransform.offsetMin = new Vector2(4, 4);
            refBorder.rectTransform.offsetMax = new Vector2(-4, -4);

            // 提示文字
            var hint = UIFactory.CreateText(minimapRoot, "Hint",
                "拖拽方块可调整 HUD 元素位置  ·  位置实时同步", 20,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.5f));
            hint.rectTransform.anchorMin = new Vector2(0, 0.92f);
            hint.rectTransform.anchorMax = new Vector2(1, 1f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            // 添加可拖拽的 HUD 元素方块
            AddLayoutElement("HealthBar", "血条", UIColors.HealthGreen, 0.5f, 0.06f, 0.25f, 0.04f);
            AddLayoutElement("CrosshairRing", "准星", UIColors.BrightBlue, 0.5f, 0.5f, 0.12f, 0.12f);
            AddLayoutElement("BuffStatus", "BUFF", new Color(0.22f, 0.55f, 0.95f, 1f), 0.08f, 0.5f, 0.10f, 0.15f);
            AddLayoutElement("Notifications", "通知", UIColors.Orange, 0.5f, 0.88f, 0.18f, 0.06f);
            AddLayoutElement("MatchInfo", "对局信息", AccentBlue, 0.5f, 0.97f, 0.45f, 0.05f);
        }

        private void AddLayoutElement(string id, string label, Color color,
            float defX, float defY, float defW, float defH)
        {
            var layout = UILayoutManager.GetElement(id, defX, defY, defW * 1920, defH * 1080);

            var go = new GameObject($"LE_{id}");
            go.transform.SetParent(minimapRoot, false);
            var rt = go.AddComponent<RectTransform>();

            // 使用归一化坐标在缩略图中定位
            float nx = layout.anchorX;
            float ny = layout.anchorY;
            rt.anchorMin = new Vector2(nx - defW / 2, ny - defH / 2);
            rt.anchorMax = new Vector2(nx + defW / 2, ny + defH / 2);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 背景色块
            var bg = go.AddComponent<Image>();
            bg.color = UIColors.WithAlpha(color, 0.40f);
            UIFactory.ApplyRoundedCorners(bg, 32, 8);
            bg.raycastTarget = true;

            // 边框
            var border = UIFactory.CreateImage(rt, "Border",
                UIColors.WithAlpha(color, 0.70f));
            UIFactory.ApplyRoundedCorners(border, 32, 8);
            UIFactory.SetFullStretch(border.rectTransform);
            border.raycastTarget = false;

            // 标签
            var lbl = UIFactory.CreateText(rt, "Label", label, 18,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(lbl.rectTransform);

            // 拖拽功能
            var dragger = go.AddComponent<LayoutElementDragger>();
            dragger.Initialize(id, layout, minimapRoot);

            layoutHandles.Add(new LayoutHandle { id = id, rt = rt, layout = layout });
        }

        // ═══════════════════ 分组标题（复用） ═══════════════════

        private void AddSectionHeader(Transform content, string text, string iconName)
        {
            var go = new GameObject($"Section_{text}");
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 44;
            le.flexibleWidth = 1;

            var rt = go.AddComponent<RectTransform>();

            var bg = go.AddComponent<Image>();
            bg.color = SectionBgColor;
            UIFactory.ApplyRoundedCorners(bg, 32, 8);
            bg.raycastTarget = false;

            var accent = UIFactory.CreateImage(rt, "Accent", AccentBlue);
            accent.rectTransform.anchorMin = new Vector2(0.005f, 0.12f);
            accent.rectTransform.anchorMax = new Vector2(0.012f, 0.88f);
            accent.rectTransform.offsetMin = Vector2.zero;
            accent.rectTransform.offsetMax = Vector2.zero;

            var iconSprite = IconManager.Get(iconName);
            float textStart = 0.025f;
            if (iconSprite != null)
            {
                var iconImg = UIFactory.CreateImage(rt, "Icon",
                    UIColors.WithAlpha(AccentBlue, 0.85f));
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.rectTransform.anchorMin = new Vector2(0.020f, 0.15f);
                iconImg.rectTransform.anchorMax = new Vector2(0.065f, 0.85f);
                iconImg.rectTransform.offsetMin = Vector2.zero;
                iconImg.rectTransform.offsetMax = Vector2.zero;
                textStart = 0.075f;
            }

            var label = UIFactory.CreateText(rt, "Label", text, 26,
                TextAlignmentOptions.Left, AccentBlue, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(textStart, 0f);
            label.rectTransform.anchorMax = new Vector2(0.95f, 1f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            rowIndex = 0;
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
            le.preferredHeight = 50;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            Color rowColor = (rowIndex % 2 == 0) ? RowBgEven : RowBgOdd;
            rowBg.color = rowColor;
            UIFactory.ApplyRoundedCorners(rowBg, 32, 6);
            rowBg.raycastTarget = true;
            rowIndex++;

            // 悬停高亮
            var rowTrigger = go.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ =>
            {
                if (rowBg) rowBg.color = new Color(
                Mathf.Min(rowColor.r + 0.06f, 1f), Mathf.Min(rowColor.g + 0.06f, 1f),
                Mathf.Min(rowColor.b + 0.10f, 1f), Mathf.Min(rowColor.a + 0.20f, 1f));
            });
            rowTrigger.triggers.Add(enterEntry);
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => { if (rowBg) rowBg.color = rowColor; });
            rowTrigger.triggers.Add(exitEntry);

            var rowRt = go.GetComponent<RectTransform>();

            // 标签
            var lbl = UIFactory.CreateText(rowRt, "Label", label, 22,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.30f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            // Slider
            var sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(rowRt, false);
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0.32f, 0.22f);
            sliderRt.anchorMax = new Vector2(0.74f, 0.78f);
            sliderRt.offsetMin = Vector2.zero;
            sliderRt.offsetMax = Vector2.zero;

            var sliderBg = sliderGo.AddComponent<Image>();
            sliderBg.color = SliderTrackColor;
            UIFactory.ApplyRoundedCorners(sliderBg, 32, 10);
            sliderBg.raycastTarget = true;

            // Fill
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

            // Handle
            var handleAreaGo = new GameObject("HandleArea");
            handleAreaGo.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
            UIFactory.SetFullStretch(handleAreaRt);
            handleAreaRt.offsetMin = new Vector2(8, 0);
            handleAreaRt.offsetMax = new Vector2(-8, 0);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(16, 0);
            handleRt.anchorMin = new Vector2(0, 0.05f);
            handleRt.anchorMax = new Vector2(0, 0.95f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = SliderHandleColor;
            UIFactory.ApplyRoundedCorners(handleImg, 24, 8);
            handleImg.raycastTarget = true;

            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            // 数值
            var valText = UIFactory.CreateText(rowRt, "Value",
                $"{value.ToString(fmt)}{unit}", 22,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            valText.rectTransform.anchorMin = new Vector2(0.76f, 0f);
            valText.rectTransform.anchorMax = new Vector2(0.89f, 1f);
            valText.rectTransform.offsetMin = Vector2.zero;
            valText.rectTransform.offsetMax = Vector2.zero;

            // 重置
            var resetGo = new GameObject("ResetBtn");
            resetGo.transform.SetParent(rowRt, false);
            var resetRt = resetGo.AddComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0.91f, 0.15f);
            resetRt.anchorMax = new Vector2(0.98f, 0.85f);
            resetRt.offsetMin = Vector2.zero;
            resetRt.offsetMax = Vector2.zero;

            var resetBg = resetGo.AddComponent<Image>();
            resetBg.color = UIColors.WithAlpha(UIColors.Orange, 0.25f);
            UIFactory.ApplyRoundedCorners(resetBg, 32, 8);

            var resetBtn = resetGo.AddComponent<Button>();
            resetBtn.targetGraphic = resetBg;
            resetBtn.transition = Selectable.Transition.ColorTint;
            var rbc = resetBtn.colors;
            rbc.normalColor = UIColors.WithAlpha(UIColors.Orange, 0.25f);
            rbc.highlightedColor = UIColors.WithAlpha(UIColors.Orange, 0.55f);
            rbc.pressedColor = UIColors.WithAlpha(UIColors.Orange, 0.85f);
            rbc.fadeDuration = 0.10f;
            resetBtn.colors = rbc;

            var resetLabel = UIFactory.CreateText(resetGo.transform, "Lbl", "↺", 20,
                TextAlignmentOptions.Center, UIColors.Orange, FontStyles.Bold);
            UIFactory.SetFullStretch(resetLabel.rectTransform);

            // 事件
            string localFmt = fmt, localUnit = unit;
            slider.onValueChanged.AddListener(v =>
            {
                onChange?.Invoke(v);
                if (valText) valText.text = $"{v.ToString(localFmt)}{localUnit}";
                ScheduleLivePreview();
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

        // ═══════════════════ 开关行（Toggle） ═══════════════════

        private void AddToggleRow(Transform content, string label, bool value,
            System.Action<bool> onChange)
        {
            var go = new GameObject($"Toggle_{label}");
            go.transform.SetParent(content, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 50;
            le.flexibleWidth = 1;

            var rowBg = go.AddComponent<Image>();
            Color rowColor = (rowIndex % 2 == 0) ? RowBgEven : RowBgOdd;
            rowBg.color = rowColor;
            UIFactory.ApplyRoundedCorners(rowBg, 32, 6);
            rowBg.raycastTarget = true;
            rowIndex++;

            // 悬停高亮
            var rowTrigger = go.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ =>
            {
                if (rowBg) rowBg.color = new Color(
                    Mathf.Min(rowColor.r + 0.06f, 1f), Mathf.Min(rowColor.g + 0.06f, 1f),
                    Mathf.Min(rowColor.b + 0.10f, 1f), Mathf.Min(rowColor.a + 0.20f, 1f));
            });
            rowTrigger.triggers.Add(enterEntry);
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => { if (rowBg) rowBg.color = rowColor; });
            rowTrigger.triggers.Add(exitEntry);

            var rowRt = go.GetComponent<RectTransform>();

            // 标签
            var lbl = UIFactory.CreateText(rowRt, "Label", label, 22,
                TextAlignmentOptions.Left, UIColors.Silver);
            lbl.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            lbl.rectTransform.anchorMax = new Vector2(0.60f, 1f);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;

            // 开关按钮
            var toggleGo = new GameObject("ToggleBtn");
            toggleGo.transform.SetParent(rowRt, false);
            var toggleRt = toggleGo.AddComponent<RectTransform>();
            toggleRt.anchorMin = new Vector2(0.72f, 0.18f);
            toggleRt.anchorMax = new Vector2(0.88f, 0.82f);
            toggleRt.offsetMin = Vector2.zero;
            toggleRt.offsetMax = Vector2.zero;

            var toggleBg = toggleGo.AddComponent<Image>();
            UIFactory.ApplyRoundedCorners(toggleBg, 32, 10);
            toggleBg.raycastTarget = true;

            var statusLabel = UIFactory.CreateText(toggleGo.transform, "Status", "", 20,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(statusLabel.rectTransform);

            // 设置初始状态
            bool currentVal = value;
            UpdateToggleVisual(toggleBg, statusLabel, currentVal);

            var toggleBtn = toggleGo.AddComponent<Button>();
            toggleBtn.targetGraphic = toggleBg;
            toggleBtn.transition = Selectable.Transition.ColorTint;
            var tbc = toggleBtn.colors;
            tbc.fadeDuration = 0.10f;
            toggleBtn.colors = tbc;
            toggleBtn.onClick.AddListener(() =>
            {
                currentVal = !currentVal;
                UpdateToggleVisual(toggleBg, statusLabel, currentVal);
                onChange?.Invoke(currentVal);
            });
        }

        private void UpdateToggleVisual(Image bg, TextMeshProUGUI label, bool isOn)
        {
            if (isOn)
            {
                bg.color = UIColors.WithAlpha(AccentBlue, 0.65f);
                label.text = "显示";
                label.color = UIColors.White;
            }
            else
            {
                bg.color = new Color(0.15f, 0.15f, 0.20f, 0.50f);
                label.text = "隐藏";
                label.color = UIColors.WithAlpha(UIColors.Silver, 0.6f);
            }
        }

        // ═══════════════════ 底部操作栏 ═══════════════════

        private void BuildBottomBar(RectTransform panel)
        {
            var barGo = new GameObject("BottomBar");
            barGo.transform.SetParent(panel, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(1, 0.080f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            var barBg = barGo.AddComponent<Image>();
            barBg.color = new Color(0.03f, 0.04f, 0.08f, 0.80f);
            UIFactory.ApplyRoundedCorners(barBg, 64, 16);
            barBg.raycastTarget = false;

            var div = UIFactory.CreateImage(panel, "BottomDiv",
                UIColors.WithAlpha(AccentBlue, 0.20f));
            div.rectTransform.anchorMin = new Vector2(0.01f, 0.080f);
            div.rectTransform.anchorMax = new Vector2(0.99f, 0.083f);
            div.rectTransform.offsetMin = Vector2.zero;
            div.rectTransform.offsetMax = Vector2.zero;

            CreateBottomButton(barRt, "Reselect", "重新选择兵种",
                BtnReselectColor, 0.01f, 0.12f, 0.22f, 0.88f, OnReselectClicked);

            CreateBottomButton(barRt, "Save", "保存并预览",
                BtnSaveColor, 0.26f, 0.12f, 0.58f, 0.88f, () =>
                {
                    UILayoutManager.Save();
                    // 实时预览：重建 HUD 使修改即时生效
                    if (BattleHUD.Instance != null)
                    {
                        BattleHUD.Instance.RebuildHUD();
                    }
                    wmj.Log.I("[Settings] 设置已保存并应用", wmj.Log.Tag.UI);
                });

            CreateBottomButton(barRt, "ResetAll", "全部重置",
                BtnResetColor, 0.62f, 0.12f, 0.80f, 0.88f, OnResetAllClicked);

            CreateBottomButton(barRt, "Close", "关  闭",
                new Color(0.35f, 0.35f, 0.40f, 0.60f), 0.84f, 0.12f, 0.99f, 0.88f,
                ToggleSettings);
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
            btn.transition = Selectable.Transition.ColorTint;
            var bc = btn.colors;
            bc.normalColor = bgColor;
            bc.highlightedColor = new Color(
                Mathf.Min(bgColor.r + 0.12f, 1f),
                Mathf.Min(bgColor.g + 0.12f, 1f),
                Mathf.Min(bgColor.b + 0.12f, 1f),
                Mathf.Min(bgColor.a + 0.10f, 1f));
            bc.pressedColor = new Color(
                Mathf.Min(bgColor.r + 0.22f, 1f),
                Mathf.Min(bgColor.g + 0.22f, 1f),
                Mathf.Min(bgColor.b + 0.22f, 1f), 1f);
            bc.selectedColor = bgColor;
            bc.fadeDuration = 0.10f;
            btn.colors = bc;
            btn.onClick.AddListener(onClick);

            var txt = UIFactory.CreateText(go.transform, "Label", label, 22,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            UIFactory.SetFullStretch(txt.rectTransform);

            return btn;
        }

        // ═══════════════════ 实时预览 ═══════════════════

        /// <summary>
        /// 防抖触发 HUD 实时预览：滑块停止拖动 PREVIEW_DELAY 秒后自动重建 HUD
        /// </summary>
        private void ScheduleLivePreview()
        {
            if (previewCoroutine != null) StopCoroutine(previewCoroutine);
            previewCoroutine = StartCoroutine(DebouncedPreview());
        }

        private IEnumerator DebouncedPreview()
        {
            yield return new WaitForSeconds(PREVIEW_DELAY);
            previewCoroutine = null;
            UILayoutManager.Save();
            if (BattleHUD.Instance != null)
                BattleHUD.Instance.RebuildHUD();
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

    // ═══════════════════ 布局拖拽组件 ═══════════════════

    /// <summary>
    /// 缩略图中可拖拽的 HUD 元素方块
    /// </summary>
    public class LayoutElementDragger : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private string elementId;
        private UIElementLayout layout;
        private RectTransform parentRt;
        private RectTransform myRt;
        private Image myBg;
        private Color normalColor;

        public void Initialize(string id, UIElementLayout layoutData, RectTransform parent)
        {
            elementId = id;
            layout = layoutData;
            parentRt = parent;
            myRt = GetComponent<RectTransform>();
            myBg = GetComponent<Image>();
            if (myBg) normalColor = myBg.color;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (myBg) myBg.color = UIColors.WithAlpha(normalColor, normalColor.a + 0.25f);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (parentRt == null) return;

            // 将屏幕位置转换为缩略图归一化坐标
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRt, eventData.position, eventData.pressEventCamera, out var localPos);

            float w = parentRt.rect.width;
            float h = parentRt.rect.height;
            float nx = Mathf.Clamp01((localPos.x + w * 0.5f) / w);
            float ny = Mathf.Clamp01((localPos.y + h * 0.5f) / h);

            // 更新锚点位置
            float halfW = (myRt.anchorMax.x - myRt.anchorMin.x) * 0.5f;
            float halfH = (myRt.anchorMax.y - myRt.anchorMin.y) * 0.5f;
            myRt.anchorMin = new Vector2(nx - halfW, ny - halfH);
            myRt.anchorMax = new Vector2(nx + halfW, ny + halfH);
            myRt.offsetMin = Vector2.zero;
            myRt.offsetMax = Vector2.zero;

            // 同步到布局数据
            layout.anchorX = nx;
            layout.anchorY = ny;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (myBg) myBg.color = normalColor;
            // 自动保存并实时重建 HUD
            UILayoutManager.Save();
            if (BattleHUD.Instance != null)
                BattleHUD.Instance.RebuildHUD();
            wmj.Log.I($"[Layout] {elementId} 位置已更新并同步: ({layout.anchorX:F2}, {layout.anchorY:F2})",
                wmj.Log.Tag.UI);
        }
    }
}
