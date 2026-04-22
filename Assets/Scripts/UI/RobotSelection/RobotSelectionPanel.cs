using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using UI.Core;

namespace UI.RobotSelection
{
    /// <summary>
    /// 兵种选择面板 — 横排布局，选项按钮倾斜(skew)，容器不倾斜
    /// 透明背景 + 亮蓝容器 + 淡蓝边框
    /// </summary>
    public class RobotSelectionPanel : MonoBehaviour
    {
        #region 单例与静态接口

        private static RobotSelectionPanel _instance;
        private static Action<RobotSelectionResult> _onCompleteCallback;

        public static bool IsVisible => _instance != null && _instance.gameObject.activeSelf;

        public static void Show(Action<RobotSelectionResult> onComplete = null)
        {
            _onCompleteCallback = onComplete;
            if (_instance == null)
            {
                CreatePanel();
            }
            else
            {
                _instance.gameObject.SetActive(true);
            }
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
            var go = new GameObject("RobotSelectionPanel");
            _instance = go.AddComponent<RobotSelectionPanel>();
            DontDestroyOnLoad(go);
        }

        #endregion

        #region 字段

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RobotSelectionViewModel viewModel;

        private Button redTeamBtn, blueTeamBtn;
        private Image redTeamBg, blueTeamBg;
        private Button[] robotBtns;
        private Image[] robotBgs;
        private TextMeshProUGUI[] robotLabels;
        private TextMeshProUGUI[] robotKeyLabels;
        private Button confirmBtn;
        private Image confirmBg;
        private TextMeshProUGUI statusText;

        private float fadeProgress;

        private const float SKEW = 5f; // 选项倾斜角度

        // ─── 设置面板同款色板（深蓝导航风）───
        private static readonly Color PanelBg    = new Color(0.04f, 0.05f, 0.10f, 0.96f);
        private static readonly Color TitleBarBg = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color ContentBg  = new Color(0.05f, 0.06f, 0.12f, 0.90f);
        private static readonly Color Accent     = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color BtnSave    = new Color(0.16f, 0.50f, 0.88f, 0.80f);

        private static readonly RobotType[] Types = {
            RobotType.Hero, RobotType.Engineer, RobotType.Infantry3,
            RobotType.Infantry4, RobotType.Infantry5, RobotType.Aerial,
            RobotType.Sentry, RobotType.Dart, RobotType.Radar
        };

        private static readonly string[] Names = {
            "英雄", "工程", "步兵III",
            "步兵IV", "步兵V", "空中",
            "哨兵", "飞镖", "雷达"
        };

        #endregion

        #region 生命周期

        void Awake()
        {
            Debug.Log("[RobotSelectionPanel] ===== Awake() 开始 =====");
            _instance = this;
            viewModel = new RobotSelectionViewModel();
            viewModel.PropertyChanged += OnVMChanged;
            viewModel.SelectionCompleted += OnSelectionDone;
            try
            {
                Debug.Log("[RobotSelectionPanel] 开始 BuildUI...");
                BuildUI();
                Debug.Log("[RobotSelectionPanel] BuildUI 完成，UI 初始化成功");
                wmj.Log.I("[RobotSelectionPanel] UI 初始化完成", wmj.Log.Tag.UI);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RobotSelectionPanel] BuildUI() 异常: {ex}");
                wmj.Log.E($"[RobotSelectionPanel] BuildUI() 异常: {ex}", wmj.Log.Tag.UI);
            }
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
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= OnVMChanged;
                viewModel.SelectionCompleted -= OnSelectionDone;
            }
            if (_instance == this) _instance = null;
        }

        #endregion

        #region 键盘输入

        private static readonly Key[] AlphaKeys = {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };
        private static readonly Key[] NumpadKeys = {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5,
            Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
        };

        private void HandleKeyboard()
        {
            // 热更新面板显示期间禁止本面板响应键盘（遮罩只拦截鼠标射线，键盘轮询需手动门控）
            if (!StartupUpdateChecker.Completed) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tabKey.wasPressedThisFrame)
            {
                if (viewModel.IsRedSelected) viewModel.SelectBlue();
                else viewModel.SelectRed();
            }
            for (int i = 0; i < 9; i++)
            {
                if (kb[AlphaKeys[i]].wasPressedThisFrame || kb[NumpadKeys[i]].wasPressedThisFrame)
                    viewModel.SelectRobot(Types[i]);
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                viewModel.Confirm();
        }

        #endregion

        #region UI 构建

        private void BuildUI()
        {
            canvas = UIFactory.CreateCanvas("SelectionCanvas", 30000);
            canvas.transform.SetParent(transform, false);
            canvasGroup = UIFactory.EnsureCanvasGroup(canvas.gameObject);
            canvasGroup.alpha = 0f;
            fadeProgress = 0f;

            var root = canvas.transform;

            // 深色全屏遮罩（设置面板同款，突出弹出内容）
            var overlay = UIFactory.CreateFullScreenImage(root, "Overlay",
                new Color(0.01f, 0.01f, 0.03f, 0.82f));
            overlay.raycastTarget = true;

            // ── 主容器面板 ──
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(root, false);
            UIFactory.SetAnchoredSize(panel, new Vector2(0, 10), new Vector2(1750, 780));

            // 面板背景（深蓝导航风，圆角）
            var panelBgGo = new GameObject("Bg");
            panelBgGo.transform.SetParent(panel, false);
            var panelBgImg = panelBgGo.AddComponent<Image>();
            panelBgImg.sprite = UIShapeHelper.RoundedRect;
            panelBgImg.type = Image.Type.Sliced;
            panelBgImg.color = PanelBg;
            panelBgImg.raycastTarget = true;
            UIFactory.SetFullStretch(panelBgImg.rectTransform);

            // 顶部标题条（TitleBarBg）
            var titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(panel, false);
            var titleBarImg = titleBar.AddComponent<Image>();
            titleBarImg.color = TitleBarBg;
            titleBarImg.raycastTarget = false;
            var tbRt = titleBarImg.rectTransform;
            tbRt.anchorMin = new Vector2(0f, 0.83f);
            tbRt.anchorMax = new Vector2(1f, 1f);
            tbRt.offsetMin = Vector2.zero;
            tbRt.offsetMax = Vector2.zero;

            // 标题条下方细线（Accent）
            var titleDiv = UIFactory.CreateImage(panel, "TitleDiv",
                UIColors.WithAlpha(Accent, 0.35f));
            titleDiv.rectTransform.anchorMin = new Vector2(0f, 0.828f);
            titleDiv.rectTransform.anchorMax = new Vector2(1f, 0.832f);
            titleDiv.rectTransform.offsetMin = Vector2.zero;
            titleDiv.rectTransform.offsetMax = Vector2.zero;

            // ── 标题 ──
            var title = UIFactory.CreateText(panel, "Title", "兵 种 选 择", 46,
                TextAlignmentOptions.Center, Accent, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.05f, 0.84f);
            title.rectTransform.anchorMax = new Vector2(0.95f, 0.99f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;

            // 内容区背景（ContentBg）
            var contentBg = new GameObject("ContentBg");
            contentBg.transform.SetParent(panel, false);
            var contentBgImg = contentBg.AddComponent<Image>();
            contentBgImg.color = ContentBg;
            contentBgImg.raycastTarget = false;
            var cbRt = contentBgImg.rectTransform;
            cbRt.anchorMin = new Vector2(0.02f, 0.13f);
            cbRt.anchorMax = new Vector2(0.98f, 0.82f);
            cbRt.offsetMin = Vector2.zero;
            cbRt.offsetMax = Vector2.zero;

            // ── 阵营选择行 ──
            var teamRow = new GameObject("TeamRow").AddComponent<RectTransform>();
            teamRow.SetParent(panel, false);
            teamRow.anchorMin = new Vector2(0.25f, 0.66f);
            teamRow.anchorMax = new Vector2(0.75f, 0.78f);
            teamRow.offsetMin = Vector2.zero;
            teamRow.offsetMax = Vector2.zero;

            // 红方按钮（skew 倾斜）
            redTeamBtn = UIFactory.CreateSkewedButton(teamRow, "Red", "红 方 [Tab]",
                UIColors.TeamRed, SKEW, 30);
            var redRt = redTeamBtn.GetComponent<RectTransform>();
            UIFactory.SetAnchors(redRt, 0f, 0.05f, 0.48f, 0.95f);
            redTeamBg = redTeamBtn.GetComponent<Image>();
            redTeamBtn.onClick.AddListener(() => viewModel.SelectRed());

            // 蓝方按钮（skew 倾斜）
            blueTeamBtn = UIFactory.CreateSkewedButton(teamRow, "Blue", "蓝 方 [Tab]",
                UIColors.TeamBlue, SKEW, 30);
            var blueRt = blueTeamBtn.GetComponent<RectTransform>();
            UIFactory.SetAnchors(blueRt, 0.52f, 0.05f, 1f, 0.95f);
            blueTeamBg = blueTeamBtn.GetComponent<Image>();
            blueTeamBtn.onClick.AddListener(() => viewModel.SelectBlue());

            // ── 兵种按钮（横排 3×3 或 9×1，这里用 9 横排）──
            robotBtns = new Button[9];
            robotBgs = new Image[9];
            robotLabels = new TextMeshProUGUI[9];
            robotKeyLabels = new TextMeshProUGUI[9];

            // 兵种区域容器
            var robotArea = new GameObject("RobotArea").AddComponent<RectTransform>();
            robotArea.SetParent(panel, false);
            robotArea.anchorMin = new Vector2(0.03f, 0.18f);
            robotArea.anchorMax = new Vector2(0.97f, 0.62f);
            robotArea.offsetMin = Vector2.zero;
            robotArea.offsetMax = Vector2.zero;

            float gap = 0.008f;
            float cellW = (1f - gap * 10) / 9f;

            for (int i = 0; i < 9; i++)
            {
                float x0 = gap + i * (cellW + gap);
                float x1 = x0 + cellW;

                // 按钮容器
                var cellGo = new GameObject($"Robot_{Types[i]}");
                cellGo.transform.SetParent(robotArea, false);
                var cellRt = cellGo.AddComponent<RectTransform>();
                cellRt.anchorMin = new Vector2(x0, 0.05f);
                cellRt.anchorMax = new Vector2(x1, 0.95f);
                cellRt.offsetMin = Vector2.zero;
                cellRt.offsetMax = Vector2.zero;

                // 背景（圆角 + skew 倾斜）— 设置面板 RowOdd 风
                var bg = cellGo.AddComponent<Image>();
                bg.sprite = UIShapeHelper.RoundedRect;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0.07f, 0.08f, 0.14f, 0.85f);
                bg.raycastTarget = true;

                // 施加 skew
                UIFactory.ApplySkew(cellRt, SKEW);

                // 按键提示（顶部）
                var keyLabel = UIFactory.CreateText(cellRt, "Key", (i + 1).ToString(), 22,
                    TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.LightBlueBorder, 0.7f),
                    FontStyles.Bold);
                keyLabel.rectTransform.anchorMin = new Vector2(0f, 0.78f);
                keyLabel.rectTransform.anchorMax = new Vector2(1f, 0.98f);
                keyLabel.rectTransform.offsetMin = Vector2.zero;
                keyLabel.rectTransform.offsetMax = Vector2.zero;

                // 兵种名称（居中）
                var label = UIFactory.CreateText(cellRt, "Label", Names[i], 26,
                    TextAlignmentOptions.Center, UIColors.Silver, FontStyles.Bold);
                label.rectTransform.anchorMin = new Vector2(0f, 0.2f);
                label.rectTransform.anchorMax = new Vector2(1f, 0.75f);
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;

                // Button 组件
                var btn = cellGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.transition = Selectable.Transition.None;

                int idx = i;
                btn.onClick.AddListener(() => viewModel.SelectRobot(Types[idx]));

                robotBtns[i] = btn;
                robotBgs[i] = bg;
                robotLabels[i] = label;
                robotKeyLabels[i] = keyLabel;
            }

            // ── 快捷键提示 ──
            var hint = UIFactory.CreateText(panel, "Hint",
                "数字键 1-9 选择兵种  |  Tab 切换阵营  |  Enter 确认",
                20, TextAlignmentOptions.Center, UIColors.WithAlpha(UIColors.Silver, 0.6f));
            hint.rectTransform.anchorMin = new Vector2(0.1f, 0.10f);
            hint.rectTransform.anchorMax = new Vector2(0.9f, 0.17f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            // ── 状态文本 ──
            statusText = UIFactory.CreateText(panel, "Status", viewModel.StatusText, 26,
                TextAlignmentOptions.Center, UIColors.Silver);
            statusText.rectTransform.anchorMin = new Vector2(0.2f, 0.02f);
            statusText.rectTransform.anchorMax = new Vector2(0.55f, 0.10f);
            statusText.rectTransform.offsetMin = Vector2.zero;
            statusText.rectTransform.offsetMax = Vector2.zero;

            // ── 确认按钮（skew 倾斜，设置面板保存按钮同款配色）──
            confirmBtn = UIFactory.CreateSkewedButton(panel, "Confirm", "确认选择 [Enter]",
                BtnSave, SKEW, 28, UIColors.White);
            var confirmRt = confirmBtn.GetComponent<RectTransform>();
            confirmRt.anchorMin = new Vector2(0.60f, 0.02f);
            confirmRt.anchorMax = new Vector2(0.85f, 0.10f);
            confirmRt.offsetMin = Vector2.zero;
            confirmRt.offsetMax = Vector2.zero;
            confirmBg = confirmBtn.GetComponent<Image>();
            confirmBtn.onClick.AddListener(() => viewModel.Confirm());

            RenderAll();
        }

        #endregion

        #region 渲染

        private void OnVMChanged(object sender, PropertyChangedEventArgs e) => RenderAll();

        private void RenderAll()
        {
            // 阵营高亮
            if (redTeamBg)
                redTeamBg.color = viewModel.IsRedSelected
                    ? UIColors.WithAlpha(UIColors.TeamRed, 0.85f)
                    : UIColors.WithAlpha(UIColors.TeamRed, 0.2f);
            if (blueTeamBg)
                blueTeamBg.color = viewModel.IsBlueSelected
                    ? UIColors.WithAlpha(UIColors.TeamBlue, 0.85f)
                    : UIColors.WithAlpha(UIColors.TeamBlue, 0.2f);

            Color teamAccent = viewModel.IsRedSelected ? UIColors.TeamRed : UIColors.BrightBlue;

            // 兵种按钮
            for (int i = 0; i < 9; i++)
            {
                bool sel = viewModel.SelectedRobot.HasValue && viewModel.SelectedRobot.Value == Types[i];
                robotBgs[i].color = sel
                    ? UIColors.WithAlpha(teamAccent, 0.65f)
                    : new Color(0.07f, 0.08f, 0.14f, 0.85f);
                robotLabels[i].color = sel ? UIColors.White : UIColors.Silver;
                robotKeyLabels[i].color = sel
                    ? UIColors.WithAlpha(UIColors.White, 0.9f)
                    : UIColors.WithAlpha(UIColors.LightBlueBorder, 0.7f);
                robotBtns[i].transform.localScale = sel ? Vector3.one * 1.05f : Vector3.one;
            }

            // 状态
            if (statusText) statusText.text = viewModel.StatusText;

            // 确认按钮
            bool ok = viewModel.CanConfirm;
            if (confirmBtn) confirmBtn.interactable = ok;
            if (confirmBg)
                confirmBg.color = ok
                    ? BtnSave
                    : new Color(0.08f, 0.10f, 0.16f, 0.55f);
        }

        private void OnSelectionDone(object sender, RobotSelectionEventArgs e)
        {
            _onCompleteCallback?.Invoke(e.Result);
            Hide();
        }

        #endregion
    }
}
