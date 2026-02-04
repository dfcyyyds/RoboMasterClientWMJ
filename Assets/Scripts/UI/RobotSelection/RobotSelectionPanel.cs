using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI.RobotSelection
{
    /// <summary>
    /// 兵种选择面板 View - 负责 UI 渲染和交互
    /// 使用方式: RobotSelectionPanel.Show(result => { ... });
    /// </summary>
    public class RobotSelectionPanel : MonoBehaviour
    {
        #region 单例与静态接口

        private static RobotSelectionPanel _instance;
        private static Action<RobotSelectionResult> _onCompleteCallback;

        /// <summary>
        /// 显示兵种选择面板
        /// </summary>
        /// <param name="onComplete">选择完成后的回调</param>
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

        /// <summary>
        /// 隐藏并销毁面板
        /// </summary>
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
            // 动态创建 Canvas 和 UI
            var panelGO = new GameObject("RobotSelectionPanel");
            _instance = panelGO.AddComponent<RobotSelectionPanel>();
            DontDestroyOnLoad(panelGO);
        }

        #endregion

        #region UI引用（动态创建）

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RobotSelectionViewModel viewModel;

        // 阵营按钮
        private Button redTeamButton;
        private Button blueTeamButton;
        private Image redTeamBg;
        private Image blueTeamBg;

        // 兵种按钮
        private Button[] robotButtons;
        private Image[] robotButtonBgs;

        // 确认按钮和状态
        private Button confirmButton;
        private TMP_Text statusText;
        private TMP_Text titleText;

        // 中文字体资源
        private TMP_FontAsset chineseFont;

        #endregion

        #region Unity生命周期

        void Awake()
        {
            _instance = this;
            viewModel = new RobotSelectionViewModel();
            viewModel.PropertyChanged += OnViewModelChanged;
            viewModel.SelectionCompleted += OnSelectionCompleted;

            LoadChineseFont();
            BuildUI();
        }

        void OnDestroy()
        {
            if (viewModel != null)
            {
                viewModel.PropertyChanged -= OnViewModelChanged;
                viewModel.SelectionCompleted -= OnSelectionCompleted;
            }
            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region 字体加载

        private void LoadChineseFont()
        {
            // 尝试从 Resources 加载中文字体
            // 方法1: 直接加载 TMP_FontAsset (如果已在 Unity 中创建)
            chineseFont = Resources.Load<TMP_FontAsset>("Fonts/ChineseFont SDF");

            if (chineseFont == null)
            {
                // 方法2: 加载 TTF 并动态创建 TMP_FontAsset
                var ttfFont = Resources.Load<Font>("Fonts/ChineseFont");
                if (ttfFont != null)
                {
                    chineseFont = TMP_FontAsset.CreateFontAsset(ttfFont);
                    if (chineseFont != null)
                    {
                        chineseFont.name = "ChineseFont Dynamic";
                        wmj.DebugTools.Info("[RobotSelectionPanel] 已从 TTF 动态创建中文字体", wmj.DebugTools.LogCategory.UI);
                    }
                }
            }

            if (chineseFont == null)
            {
                // 方法3: 使用 TMP 默认字体作为后备
                chineseFont = TMP_Settings.defaultFontAsset;
                wmj.DebugTools.Warn("[RobotSelectionPanel] 未找到中文字体，使用默认字体。请在 Unity 中创建 TMP 字体资源。", wmj.DebugTools.LogCategory.UI);
            }
        }

        #endregion

        #region UI构建

        private void BuildUI()
        {
            // 创建最高层级的 Canvas
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // 最高层级

            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();

            canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // 半透明背景遮罩
            var bgMask = CreateImage(transform, "BackgroundMask", new Color(0, 0, 0, 0.7f));
            SetFullStretch(bgMask.rectTransform);

            // 主面板
            var panelBg = CreateImage(transform, "PanelBackground", new Color(0.15f, 0.15f, 0.2f, 0.95f));
            panelBg.rectTransform.anchorMin = new Vector2(0.2f, 0.15f);
            panelBg.rectTransform.anchorMax = new Vector2(0.8f, 0.85f);
            panelBg.rectTransform.offsetMin = Vector2.zero;
            panelBg.rectTransform.offsetMax = Vector2.zero;

            // 标题
            titleText = CreateText(panelBg.transform, "Title", "兵 种 选 择", 48, TextAlignmentOptions.Center);
            titleText.rectTransform.anchorMin = new Vector2(0, 0.88f);
            titleText.rectTransform.anchorMax = new Vector2(1, 0.98f);
            titleText.rectTransform.offsetMin = Vector2.zero;
            titleText.rectTransform.offsetMax = Vector2.zero;
            titleText.color = Color.white;

            // 阵营选择区域
            var teamSection = CreateSection(panelBg.transform, "TeamSection", 0.75f, 0.88f);
            var teamLabel = CreateText(teamSection, "TeamLabel", "选择阵营", 28, TextAlignmentOptions.Left);
            teamLabel.rectTransform.anchorMin = new Vector2(0.05f, 0);
            teamLabel.rectTransform.anchorMax = new Vector2(0.25f, 1);

            redTeamButton = CreateTeamButton(teamSection, "RedTeam", "红 方", new Color(0.8f, 0.2f, 0.2f), 0.3f, 0.6f);
            blueTeamButton = CreateTeamButton(teamSection, "BlueTeam", "蓝 方", new Color(0.2f, 0.4f, 0.8f), 0.65f, 0.95f);
            redTeamBg = redTeamButton.GetComponent<Image>();
            blueTeamBg = blueTeamButton.GetComponent<Image>();

            redTeamButton.onClick.AddListener(() => viewModel.SelectRed());
            blueTeamButton.onClick.AddListener(() => viewModel.SelectBlue());

            // 兵种选择区域
            var robotSection = CreateSection(panelBg.transform, "RobotSection", 0.15f, 0.72f);
            var robotLabel = CreateText(robotSection, "RobotLabel", "选择兵种", 28, TextAlignmentOptions.TopLeft);
            robotLabel.rectTransform.anchorMin = new Vector2(0.05f, 0.85f);
            robotLabel.rectTransform.anchorMax = new Vector2(0.95f, 1f);

            // 兵种按钮网格 (3行3列)
            RobotType[] robotTypes = {
                RobotType.Hero, RobotType.Engineer, RobotType.Infantry3,
                RobotType.Infantry4, RobotType.Infantry5, RobotType.Aerial,
                RobotType.Sentry, RobotType.Dart, RobotType.Radar
            };
            string[] robotNames = {
                "英雄", "工程", "3号步兵",
                "4号步兵", "5号步兵", "空中",
                "哨兵", "飞镖", "雷达"
            };

            robotButtons = new Button[robotTypes.Length];
            robotButtonBgs = new Image[robotTypes.Length];

            for (int i = 0; i < robotTypes.Length; i++)
            {
                int row = i / 3;
                int col = i % 3;
                float x0 = 0.05f + col * 0.3f;
                float x1 = x0 + 0.28f;
                float y1 = 0.82f - row * 0.28f;
                float y0 = y1 - 0.25f;

                var btn = CreateRobotButton(robotSection, robotNames[i], robotTypes[i], x0, y0, x1, y1);
                robotButtons[i] = btn;
                robotButtonBgs[i] = btn.GetComponent<Image>();

                int index = i;
                RobotType type = robotTypes[i];
                btn.onClick.AddListener(() => viewModel.SelectRobot(type));
            }

            // 状态文本
            statusText = CreateText(panelBg.transform, "StatusText", viewModel.StatusText, 24, TextAlignmentOptions.Center);
            statusText.rectTransform.anchorMin = new Vector2(0.1f, 0.06f);
            statusText.rectTransform.anchorMax = new Vector2(0.5f, 0.12f);
            statusText.color = new Color(0.8f, 0.8f, 0.8f);

            // 确认按钮
            confirmButton = CreateButton(panelBg.transform, "ConfirmButton", "确认选择", new Color(0.2f, 0.6f, 0.2f), 0.6f, 0.03f, 0.9f, 0.13f);
            confirmButton.onClick.AddListener(() => viewModel.Confirm());

            // 初始渲染
            RenderAll();
        }

        private RectTransform CreateSection(Transform parent, string name, float yMin, float yMax)
        {
            var section = new GameObject(name).AddComponent<RectTransform>();
            section.SetParent(parent, false);
            section.anchorMin = new Vector2(0.02f, yMin);
            section.anchorMax = new Vector2(0.98f, yMax);
            section.offsetMin = Vector2.zero;
            section.offsetMax = Vector2.zero;
            return section;
        }

        private Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            // 确保透明度正确渲染
            img.raycastTarget = true;
            return img;
        }

        private TMP_Text CreateText(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;

            // 设置中文字体
            if (chineseFont != null)
            {
                tmp.font = chineseFont;
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return tmp;
        }

        private Button CreateTeamButton(Transform parent, string name, string text, Color bgColor, float x0, float x1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0.1f);
            rt.anchorMax = new Vector2(x1, 0.9f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var label = CreateText(go.transform, "Label", text, 32, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;

            return btn;
        }

        private Button CreateRobotButton(Transform parent, string text, RobotType type, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject($"Robot_{type}");
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.35f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var label = CreateText(go.transform, "Label", text, 24, TextAlignmentOptions.Center);

            return btn;
        }

        private Button CreateButton(Transform parent, string name, string text, Color bgColor, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var label = CreateText(go.transform, "Label", text, 28, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;

            return btn;
        }

        private void SetFullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion

        #region 渲染更新

        private void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            RenderAll();
        }

        private void RenderAll()
        {
            // Update team button highlighting
            if (redTeamBg != null)
            {
                redTeamBg.color = viewModel.IsRedSelected
                    ? new Color(1f, 0.3f, 0.3f)
                    : new Color(0.5f, 0.15f, 0.15f);
            }
            if (blueTeamBg != null)
            {
                blueTeamBg.color = viewModel.IsBlueSelected
                    ? new Color(0.3f, 0.5f, 1f)
                    : new Color(0.15f, 0.25f, 0.5f);
            }

            // Update robot button highlighting
            RobotType[] robotTypes = {
                RobotType.Hero, RobotType.Engineer, RobotType.Infantry3,
                RobotType.Infantry4, RobotType.Infantry5, RobotType.Aerial,
                RobotType.Sentry, RobotType.Dart, RobotType.Radar
            };
            for (int i = 0; i < robotButtons.Length && i < robotButtonBgs.Length; i++)
            {
                bool isSelected = viewModel.SelectedRobot.HasValue && viewModel.SelectedRobot.Value == robotTypes[i];
                Color teamColor = viewModel.IsRedSelected
                    ? new Color(0.8f, 0.3f, 0.3f)
                    : new Color(0.3f, 0.5f, 0.8f);
                robotButtonBgs[i].color = isSelected ? teamColor : new Color(0.3f, 0.3f, 0.35f);
            }

            // Update status text
            if (statusText != null)
            {
                statusText.text = viewModel.StatusText;
            }

            // Update confirm button
            if (confirmButton != null)
            {
                confirmButton.interactable = viewModel.CanConfirm;
                var bg = confirmButton.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = viewModel.CanConfirm
                        ? new Color(0.2f, 0.7f, 0.2f)
                        : new Color(0.3f, 0.3f, 0.3f);
                }
            }
        }

        private void OnSelectionCompleted(object sender, RobotSelectionEventArgs e)
        {
            _onCompleteCallback?.Invoke(e.Result);
            Hide();
        }

        #endregion
    }
}
