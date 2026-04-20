using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using UI.Core;

namespace UI.RobotSelection
{
    /// <summary>
    /// 性能体系选择面板 — 兵种选择确认后弹出
    /// 根据兵种能力，展示射手体系 / 底盘体系 / 哨兵控制模式的选项
    /// 风格与 RobotSelectionPanel 统一（深蓝科技感 + skew 倾斜按钮）
    /// </summary>
    public class PerformanceSelectionPanel : MonoBehaviour
    {
        #region 单例与静态接口

        private static PerformanceSelectionPanel _instance;

        /// <summary>
        /// 性能选择结果
        /// </summary>
        public class PerformanceResult
        {
            public uint Shooter { get; set; }
            public uint Chassis { get; set; }
            public uint SentryControl { get; set; }
        }

        private static Action<PerformanceResult> _onCompleteCallback;
        private static RobotCapabilities.RobotProfile _profile;

        public static bool IsVisible => _instance != null && _instance.gameObject.activeSelf;

        /// <summary>显示体系选择面板</summary>
        public static void Show(RobotCapabilities.RobotProfile profile, Action<PerformanceResult> onComplete = null)
        {
            _profile = profile;
            _onCompleteCallback = onComplete;
            if (_instance == null)
                CreatePanel();
            else
                _instance.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private static void CreatePanel()
        {
            var go = new GameObject("PerformanceSelectionPanel");
            _instance = go.AddComponent<PerformanceSelectionPanel>();
            DontDestroyOnLoad(go);
        }

        #endregion

        #region 字段

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private float fadeProgress;

        private const float SKEW = 5f;

        // ─── 设置面板同款色板 ───
        private static readonly Color PanelBg    = new Color(0.04f, 0.05f, 0.10f, 0.96f);
        private static readonly Color TitleBarBg = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color ContentBg  = new Color(0.05f, 0.06f, 0.12f, 0.90f);
        private static readonly Color Accent     = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color BtnSave    = new Color(0.16f, 0.50f, 0.88f, 0.80f);
        private static readonly Color RowBg      = new Color(0.07f, 0.08f, 0.14f, 0.85f);

        // 射手体系
        private Button shooterBurst, shooterCooldown;
        private Image shooterBurstBg, shooterCooldownBg;

        // 底盘体系
        private Button chassisPower, chassisHealth;
        private Image chassisPowerBg, chassisHealthBg;

        // 哨兵控制
        private Button sentryAuto, sentrySemi;
        private Image sentryAutoBg, sentrySemiBg;

        // 确认
        private Button confirmBtn;
        private Image confirmBg;
        private TextMeshProUGUI statusText;

        // 当前选择
        private uint selectedShooter = 0;
        private uint selectedChassis = 0;
        private uint selectedSentry = 0;

        #endregion

        #region 生命周期

        void Awake()
        {
            _instance = this;
            BuildUI();
        }

        void Update()
        {
            if (fadeProgress < 1f)
            {
                fadeProgress = Mathf.MoveTowards(fadeProgress, 1f, Time.unscaledDeltaTime * 4f);
                if (canvasGroup) canvasGroup.alpha = fadeProgress;
            }
            HandleKeyboard();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        #endregion

        #region 键盘

        private void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                OnConfirm();

            // 快捷键: 1/2 射手, 3/4 底盘, 5/6 哨兵
            if (_profile.HasShooterPerf)
            {
                if (kb.digit1Key.wasPressedThisFrame) SelectShooter(1);
                if (kb.digit2Key.wasPressedThisFrame) SelectShooter(2);
            }
            if (_profile.HasChassisPerf)
            {
                if (kb.digit3Key.wasPressedThisFrame) SelectChassis(1);
                if (kb.digit4Key.wasPressedThisFrame) SelectChassis(2);
            }
            if (_profile.HasSentryControl)
            {
                if (kb.digit5Key.wasPressedThisFrame) SelectSentry(0);
                if (kb.digit6Key.wasPressedThisFrame) SelectSentry(1);
            }
        }

        #endregion

        #region UI 构建

        private void BuildUI()
        {
            canvas = UIFactory.CreateCanvas("PerfSelectionCanvas", 30100);
            canvas.transform.SetParent(transform, false);
            canvasGroup = UIFactory.EnsureCanvasGroup(canvas.gameObject);
            canvasGroup.alpha = 0f;
            fadeProgress = 0f;

            var root = canvas.transform;

            // 深色全屏遮罩
            var overlay = UIFactory.CreateFullScreenImage(root, "Overlay",
                new Color(0.01f, 0.01f, 0.03f, 0.82f));
            overlay.raycastTarget = true;

            // 主面板
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(root, false);

            // 根据选项数动态调整面板高度
            int sectionCount = 0;
            if (_profile.HasShooterPerf) sectionCount++;
            if (_profile.HasChassisPerf) sectionCount++;
            if (_profile.HasSentryControl) sectionCount++;
            float panelH = 280 + sectionCount * 180;
            UIFactory.SetAnchoredSize(panel, new Vector2(0, 10), new Vector2(1200, panelH));

            // 面板背景（PanelBg）
            var panelBgGo = new GameObject("Bg");
            panelBgGo.transform.SetParent(panel, false);
            var panelBgImg = panelBgGo.AddComponent<Image>();
            panelBgImg.sprite = UIShapeHelper.RoundedRect;
            panelBgImg.type = Image.Type.Sliced;
            panelBgImg.color = PanelBg;
            panelBgImg.raycastTarget = true;
            UIFactory.SetFullStretch(panelBgImg.rectTransform);

            // 标题条（TitleBarBg）
            float titleTopFrac = 1f - 90f / panelH;
            var titleBarGo = new GameObject("TitleBar");
            titleBarGo.transform.SetParent(panel, false);
            var titleBarImg = titleBarGo.AddComponent<Image>();
            titleBarImg.color = TitleBarBg;
            titleBarImg.raycastTarget = false;
            titleBarImg.rectTransform.anchorMin = new Vector2(0f, titleTopFrac);
            titleBarImg.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleBarImg.rectTransform.offsetMin = Vector2.zero;
            titleBarImg.rectTransform.offsetMax = Vector2.zero;

            // 标题
            string titleText = $"体 系 选 择  ·  {_profile.DisplayName}";
            var title = UIFactory.CreateText(panel, "Title", titleText, 40,
                TextAlignmentOptions.Center, Accent, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.05f, titleTopFrac + 0.02f);
            title.rectTransform.anchorMax = new Vector2(0.95f, 0.99f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            // 标题条下方细线
            float divY = titleTopFrac;
            var divider = UIFactory.CreateImage(panel, "Divider",
                UIColors.WithAlpha(Accent, 0.35f));
            divider.rectTransform.anchorMin = new Vector2(0f, divY - 0.003f);
            divider.rectTransform.anchorMax = new Vector2(1f, divY + 0.003f);
            divider.rectTransform.offsetMin = Vector2.zero;
            divider.rectTransform.offsetMax = Vector2.zero;

            // 内容区背景（ContentBg）
            var contentBg = new GameObject("ContentBg");
            contentBg.transform.SetParent(panel, false);
            var contentBgImg = contentBg.AddComponent<Image>();
            contentBgImg.color = ContentBg;
            contentBgImg.raycastTarget = false;
            contentBgImg.rectTransform.anchorMin = new Vector2(0.02f, 0.15f);
            contentBgImg.rectTransform.anchorMax = new Vector2(0.98f, divY - 0.01f);
            contentBgImg.rectTransform.offsetMin = Vector2.zero;
            contentBgImg.rectTransform.offsetMax = Vector2.zero;

            // 选项区域
            float sectionTop = divY - 0.03f;
            float sectionH = 0.22f;
            int sIdx = 0;

            if (_profile.HasShooterPerf)
            {
                float y1 = sectionTop - sIdx * (sectionH + 0.02f);
                float y0 = y1 - sectionH;
                BuildShooterSection(panel, y0, y1);
                sIdx++;
            }

            if (_profile.HasChassisPerf)
            {
                float y1 = sectionTop - sIdx * (sectionH + 0.02f);
                float y0 = y1 - sectionH;
                BuildChassisSection(panel, y0, y1);
                sIdx++;
            }

            if (_profile.HasSentryControl)
            {
                float y1 = sectionTop - sIdx * (sectionH + 0.02f);
                float y0 = y1 - sectionH;
                BuildSentrySection(panel, y0, y1);
                sIdx++;
            }

            // 确认按钮
            float confirmY0 = 0.03f;
            float confirmY1 = 0.12f;
            confirmBtn = UIFactory.CreateSkewedButton(panel, "Confirm", "确  认  [Enter]",
                new Color(0.08f, 0.10f, 0.16f, 0.55f), SKEW, 30, UIColors.White);
            var cRt = confirmBtn.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.30f, confirmY0);
            cRt.anchorMax = new Vector2(0.70f, confirmY1);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            confirmBg = confirmBtn.GetComponent<Image>();
            confirmBtn.onClick.AddListener(OnConfirm);

            // 状态提示
            statusText = UIFactory.CreateText(panel, "Status", "请完成体系选择", 22,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.7f));
            statusText.rectTransform.anchorMin = new Vector2(0.1f, confirmY1 + 0.01f);
            statusText.rectTransform.anchorMax = new Vector2(0.9f, confirmY1 + 0.07f);
            statusText.rectTransform.offsetMin = Vector2.zero;
            statusText.rectTransform.offsetMax = Vector2.zero;

            // 快捷键提示
            var hint = UIFactory.CreateText(panel, "Hint", BuildHintText(), 18,
                TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.5f));
            hint.rectTransform.anchorMin = new Vector2(0.05f, 0.005f);
            hint.rectTransform.anchorMax = new Vector2(0.95f, 0.03f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            // 默认选中第一个选项
            if (_profile.HasShooterPerf) SelectShooter(1);
            if (_profile.HasChassisPerf) SelectChassis(1);
            if (_profile.HasSentryControl) SelectSentry(0);
            UpdateStatus();
        }

        private string BuildHintText()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_profile.HasShooterPerf) parts.Add("1/2 射手体系");
            if (_profile.HasChassisPerf) parts.Add("3/4 底盘体系");
            if (_profile.HasSentryControl) parts.Add("5/6 控制模式");
            parts.Add("Enter 确认");
            return string.Join("  |  ", parts);
        }

        // ─── 射手体系区 ───

        private void BuildShooterSection(RectTransform panel, float y0, float y1)
        {
            // 区块标题
            var label = UIFactory.CreateText(panel, "ShooterLabel", "射 手 体 系", 28,
                TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0.06f, y1 - 0.06f);
            label.rectTransform.anchorMax = new Vector2(0.50f, y1);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            float btnY0 = y0 + 0.02f;
            float btnY1 = y1 - 0.08f;

            // 爆发优先
            shooterBurst = UIFactory.CreateSkewedButton(panel, "ShooterBurst", "",
                RowBg, SKEW, 24);
            shooterBurstBg = shooterBurst.GetComponent<Image>();
            var sbrRt = shooterBurst.GetComponent<RectTransform>();
            sbrRt.anchorMin = new Vector2(0.06f, btnY0);
            sbrRt.anchorMax = new Vector2(0.48f, btnY1);
            sbrRt.offsetMin = Vector2.zero;
            sbrRt.offsetMax = Vector2.zero;
            BuildOptionContent(sbrRt, "[1] 爆发优先", "射速↑  散热速度↓", UIColors.Orange);
            shooterBurst.onClick.AddListener(() => SelectShooter(1));

            // 冷却优先
            shooterCooldown = UIFactory.CreateSkewedButton(panel, "ShooterCool", "",
                RowBg, SKEW, 24);
            shooterCooldownBg = shooterCooldown.GetComponent<Image>();
            var scrRt = shooterCooldown.GetComponent<RectTransform>();
            scrRt.anchorMin = new Vector2(0.52f, btnY0);
            scrRt.anchorMax = new Vector2(0.94f, btnY1);
            scrRt.offsetMin = Vector2.zero;
            scrRt.offsetMax = Vector2.zero;
            BuildOptionContent(scrRt, "[2] 冷却优先", "散热速度↑  射速↓", UIColors.BrightBlue);
            shooterCooldown.onClick.AddListener(() => SelectShooter(2));
        }

        // ─── 底盘体系区 ───

        private void BuildChassisSection(RectTransform panel, float y0, float y1)
        {
            var label = UIFactory.CreateText(panel, "ChassisLabel", "底 盘 体 系", 28,
                TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0.06f, y1 - 0.06f);
            label.rectTransform.anchorMax = new Vector2(0.50f, y1);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            float btnY0 = y0 + 0.02f;
            float btnY1 = y1 - 0.08f;

            // 功率优先
            chassisPower = UIFactory.CreateSkewedButton(panel, "ChassisPower", "",
                RowBg, SKEW, 24);
            chassisPowerBg = chassisPower.GetComponent<Image>();
            var cpRt = chassisPower.GetComponent<RectTransform>();
            cpRt.anchorMin = new Vector2(0.06f, btnY0);
            cpRt.anchorMax = new Vector2(0.48f, btnY1);
            cpRt.offsetMin = Vector2.zero;
            cpRt.offsetMax = Vector2.zero;
            BuildOptionContent(cpRt, "[3] 功率优先", "最大功率↑  血量上限↓", UIColors.HeatYellow);
            chassisPower.onClick.AddListener(() => SelectChassis(1));

            // 血量优先
            chassisHealth = UIFactory.CreateSkewedButton(panel, "ChassisHP", "",
                RowBg, SKEW, 24);
            chassisHealthBg = chassisHealth.GetComponent<Image>();
            var chRt = chassisHealth.GetComponent<RectTransform>();
            chRt.anchorMin = new Vector2(0.52f, btnY0);
            chRt.anchorMax = new Vector2(0.94f, btnY1);
            chRt.offsetMin = Vector2.zero;
            chRt.offsetMax = Vector2.zero;
            BuildOptionContent(chRt, "[4] 血量优先", "血量上限↑  最大功率↓", UIColors.HealthGreen);
            chassisHealth.onClick.AddListener(() => SelectChassis(2));
        }

        // ─── 哨兵控制区 ───

        private void BuildSentrySection(RectTransform panel, float y0, float y1)
        {
            var label = UIFactory.CreateText(panel, "SentryLabel", "控 制 模 式", 28,
                TextAlignmentOptions.Left, Accent, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0.06f, y1 - 0.06f);
            label.rectTransform.anchorMax = new Vector2(0.50f, y1);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            float btnY0 = y0 + 0.02f;
            float btnY1 = y1 - 0.08f;

            // 全自动
            sentryAuto = UIFactory.CreateSkewedButton(panel, "SentryAuto", "",
                RowBg, SKEW, 24);
            sentryAutoBg = sentryAuto.GetComponent<Image>();
            var saRt = sentryAuto.GetComponent<RectTransform>();
            saRt.anchorMin = new Vector2(0.06f, btnY0);
            saRt.anchorMax = new Vector2(0.48f, btnY1);
            saRt.offsetMin = Vector2.zero;
            saRt.offsetMax = Vector2.zero;
            BuildOptionContent(saRt, "[5] 全自动", "血量高  热量高  功率高", UIColors.BrightPurple);
            sentryAuto.onClick.AddListener(() => SelectSentry(0));

            // 半自动
            sentrySemi = UIFactory.CreateSkewedButton(panel, "SentrySemi", "",
                RowBg, SKEW, 24);
            sentrySemiBg = sentrySemi.GetComponent<Image>();
            var ssRt = sentrySemi.GetComponent<RectTransform>();
            ssRt.anchorMin = new Vector2(0.52f, btnY0);
            ssRt.anchorMax = new Vector2(0.94f, btnY1);
            ssRt.offsetMin = Vector2.zero;
            ssRt.offsetMax = Vector2.zero;
            BuildOptionContent(ssRt, "[6] 半自动", "操作手自主控制  参数较低", UIColors.Orange);
            sentrySemi.onClick.AddListener(() => SelectSentry(1));
        }

        // ─── 选项内容填充 ───

        private void BuildOptionContent(RectTransform parent, string title, string desc, Color accentColor)
        {
            // 标题
            var titleTxt = UIFactory.CreateText(parent, "Title", title, 26,
                TextAlignmentOptions.Center, UIColors.White, FontStyles.Bold);
            titleTxt.rectTransform.anchorMin = new Vector2(0.05f, 0.50f);
            titleTxt.rectTransform.anchorMax = new Vector2(0.95f, 0.90f);
            titleTxt.rectTransform.offsetMin = Vector2.zero;
            titleTxt.rectTransform.offsetMax = Vector2.zero;

            // 描述
            var descTxt = UIFactory.CreateText(parent, "Desc", desc, 20,
                TextAlignmentOptions.Center, UIColors.WithAlpha(accentColor, 0.85f));
            descTxt.rectTransform.anchorMin = new Vector2(0.05f, 0.10f);
            descTxt.rectTransform.anchorMax = new Vector2(0.95f, 0.50f);
            descTxt.rectTransform.offsetMin = Vector2.zero;
            descTxt.rectTransform.offsetMax = Vector2.zero;
        }

        #endregion

        #region 选择逻辑

        private void SelectShooter(uint val)
        {
            selectedShooter = val;
            UpdateButtonVisual(shooterBurstBg, val == 1);
            UpdateButtonVisual(shooterCooldownBg, val == 2);
            UpdateStatus();
        }

        private void SelectChassis(uint val)
        {
            selectedChassis = val;
            UpdateButtonVisual(chassisPowerBg, val == 1);
            UpdateButtonVisual(chassisHealthBg, val == 2);
            UpdateStatus();
        }

        private void SelectSentry(uint val)
        {
            selectedSentry = val;
            UpdateButtonVisual(sentryAutoBg, val == 0);
            UpdateButtonVisual(sentrySemiBg, val == 1);
            UpdateStatus();
        }

        private void UpdateButtonVisual(Image bg, bool isSelected)
        {
            if (bg == null) return;
            bg.color = isSelected
                ? UIColors.WithAlpha(Accent, 0.55f)
                : RowBg;
        }

        private void UpdateStatus()
        {
            bool canConfirm = true;
            var parts = new System.Collections.Generic.List<string>();

            if (_profile.HasShooterPerf)
            {
                if (selectedShooter == 0)
                {
                    canConfirm = false;
                }
                else
                {
                    parts.Add(selectedShooter == 1 ? "射手:爆发" : "射手:冷却");
                }
            }
            if (_profile.HasChassisPerf)
            {
                if (selectedChassis == 0)
                {
                    canConfirm = false;
                }
                else
                {
                    parts.Add(selectedChassis == 1 ? "底盘:功率" : "底盘:血量");
                }
            }
            if (_profile.HasSentryControl)
            {
                parts.Add(selectedSentry == 0 ? "控制:全自动" : "控制:半自动");
            }

            if (canConfirm)
            {
                if (statusText) statusText.text = $"已选择: {string.Join("  ·  ", parts)}";
                if (confirmBg) confirmBg.color = BtnSave;
            }
            else
            {
                if (statusText) statusText.text = "请完成所有体系选择";
                if (confirmBg) confirmBg.color = new Color(0.08f, 0.10f, 0.16f, 0.55f);
            }
        }

        private void OnConfirm()
        {
            // 验证必选项
            if (_profile.HasShooterPerf && selectedShooter == 0) return;
            if (_profile.HasChassisPerf && selectedChassis == 0) return;

            var result = new PerformanceResult
            {
                Shooter = selectedShooter,
                Chassis = selectedChassis,
                SentryControl = selectedSentry
            };

            wmj.Log.I($"[PerfSelection] 体系选择完成: Shooter={result.Shooter}, " +
                $"Chassis={result.Chassis}, SentryCtrl={result.SentryControl}", wmj.Log.Tag.UI);

            Hide();
            _onCompleteCallback?.Invoke(result);
        }

        #endregion
    }
}
