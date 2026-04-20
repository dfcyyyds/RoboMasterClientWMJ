using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using UI.Core;

namespace UI.HUD
{
    /// <summary>
    /// 退出应用确认面板 — 按 Esc 触发
    /// 键盘交互：Tab/←→ 切换选项，Enter 确认，Esc 取消，5 秒无操作默认取消
    /// 风格与 SettingsPanel 一致
    /// </summary>
    public class ExitConfirmPanel : MonoBehaviour
    {
        public static ExitConfirmPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.isOpen;

        // ─── 颜色（复刻 SettingsPanel 风格）───
        private static readonly Color PanelBg    = new Color(0.04f, 0.05f, 0.10f, 0.98f);
        private static readonly Color ContentBg  = new Color(0.05f, 0.06f, 0.12f, 0.92f);
        private static readonly Color TitleBarBg = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color Accent     = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color BtnConfirm = new Color(0.80f, 0.25f, 0.20f, 0.85f); // 退出=红
        private static readonly Color BtnCancel  = new Color(0.16f, 0.50f, 0.88f, 0.85f); // 取消=蓝
        private static readonly Color BtnFocused = new Color(1f, 0.85f, 0.30f, 0.95f);    // 焦点=亮黄
        private static readonly Color BtnNormal  = new Color(0.20f, 0.25f, 0.35f, 0.70f);
        private static readonly Color HintColor  = new Color(0.55f, 0.65f, 0.78f, 0.80f);

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private GameObject panelRoot;
        private Image confirmBtnBg, cancelBtnBg;
        private TextMeshProUGUI confirmText, cancelText, countdownText;
        private int focusIndex; // 0=退出, 1=取消 (默认取消)
        private float countdown;
        private bool isOpen;

        private const float AUTO_CANCEL_SECONDS = 5f;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUI();
            HidePanel();
        }

        void Update()
        {
            if (!isOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // 倒计时
            countdown -= Time.unscaledDeltaTime;
            if (countdownText != null)
                countdownText.text = $"<size=80%><color=#8892A6>{countdown:F1}s 后自动取消</color></size>";

            if (countdown <= 0f)
            {
                wmj.Log.I("[ExitConfirm] 5 秒无操作，自动取消", wmj.Log.Tag.UI);
                HidePanel();
                return;
            }

            // 任何键按下都重置倒计时
            bool anyKey = kb.anyKey.wasPressedThisFrame;

            // Tab / 左右箭头 — 切换焦点
            if (kb.tabKey.wasPressedThisFrame ||
                kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
            {
                focusIndex = 1 - focusIndex;
                UpdateFocus();
                countdown = AUTO_CANCEL_SECONDS;
                return;
            }

            // Enter — 确认当前选项
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
                kb.spaceKey.wasPressedThisFrame)
            {
                if (focusIndex == 0) ConfirmExit();
                else CancelExit();
                return;
            }

            // Esc — 直接取消
            if (kb.escapeKey.wasPressedThisFrame)
            {
                CancelExit();
                return;
            }

            // 任何其他键也重置倒计时
            if (anyKey) countdown = AUTO_CANCEL_SECONDS;
        }

        /// <summary>显示退出确认面板</summary>
        public void ShowPanel()
        {
            if (isOpen) return;
            isOpen = true;
            panelRoot.SetActive(true);
            focusIndex = 1; // 默认光标在"取消"
            countdown = AUTO_CANCEL_SECONDS;
            UpdateFocus();
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            wmj.Log.I("[ExitConfirm] 显示退出确认面板", wmj.Log.Tag.UI);
        }

        /// <summary>隐藏面板</summary>
        public void HidePanel()
        {
            isOpen = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void ConfirmExit()
        {
            wmj.Log.I("[ExitConfirm] ✅ 用户确认退出，正在关闭应用…", wmj.Log.Tag.UI);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            // 1) 主动停止需要显式退出的后台线程/重连协程
            try
            {
                if (NetworkManager.Instance != null)
                {
                    var field = typeof(NetworkManager).GetField("mqttService",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var svc = field?.GetValue(NetworkManager.Instance) as MqttClientService;
                    svc?.Shutdown();
                }
            }
            catch (System.Exception ex)
            {
                wmj.Log.W($"[ExitConfirm] MQTT Shutdown 异常: {ex.Message}", wmj.Log.Tag.UI);
            }

            // 2) 发送退出信号
            Application.Quit();

            // 3) 看门狗：若 2 秒内 Application.Quit 未能真正退出（网络线程阻塞等），直接强杀进程
            StartCoroutine(ExitWatchdog(2.0f));
#endif
        }

#if !UNITY_EDITOR
        private System.Collections.IEnumerator ExitWatchdog(float timeout)
        {
            yield return new WaitForSecondsRealtime(timeout);
            wmj.Log.W($"[ExitConfirm] 退出看门狗触发（{timeout}s）— 进程未能正常退出，强制终止", wmj.Log.Tag.UI);
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[ExitConfirm] Process.Kill 失败: {ex.Message}");
                System.Environment.Exit(0);
            }
        }
#endif

        private void CancelExit()
        {
            wmj.Log.I("[ExitConfirm] 取消退出", wmj.Log.Tag.UI);
            HidePanel();
        }

        private void UpdateFocus()
        {
            // 退出按钮
            if (confirmBtnBg != null)
                confirmBtnBg.color = (focusIndex == 0) ? BtnFocused : BtnConfirm;
            if (confirmText != null)
                confirmText.color = (focusIndex == 0) ? Color.black : Color.white;

            // 取消按钮
            if (cancelBtnBg != null)
                cancelBtnBg.color = (focusIndex == 1) ? BtnFocused : BtnCancel;
            if (cancelText != null)
                cancelText.color = (focusIndex == 1) ? Color.black : Color.white;
        }

        // ═══════════════════ UI 构建 ═══════════════════

        private void BuildUI()
        {
            canvas = UIFactory.CreateCanvas("ExitConfirmCanvas", 31500);
            Object.DontDestroyOnLoad(canvas.gameObject);
            canvas.targetDisplay = 0;
            canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;

            // 全屏半透明遮罩
            var overlay = UIFactory.CreateFullScreenImage(canvas.transform, "Overlay",
                new Color(0.02f, 0.03f, 0.06f, 0.80f));

            panelRoot = overlay.gameObject;

            // 中央容器
            var container = new GameObject("Container", typeof(RectTransform), typeof(Image));
            container.transform.SetParent(overlay.transform, false);
            var containerImg = container.GetComponent<Image>();
            containerImg.color = PanelBg;
            UIFactory.ApplyRoundedCorners(containerImg, 64, 16);
            var containerRT = container.GetComponent<RectTransform>();
            containerRT.anchorMin = new Vector2(0.5f, 0.5f);
            containerRT.anchorMax = new Vector2(0.5f, 0.5f);
            containerRT.pivot = new Vector2(0.5f, 0.5f);
            containerRT.sizeDelta = new Vector2(560f, 300f);

            // 顶部标题栏
            var titleBar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
            titleBar.transform.SetParent(container.transform, false);
            var titleBarImg = titleBar.GetComponent<Image>();
            titleBarImg.color = TitleBarBg;
            UIFactory.ApplyRoundedCorners(titleBarImg, 64, 16);
            var titleBarRT = titleBar.GetComponent<RectTransform>();
            titleBarRT.anchorMin = new Vector2(0f, 1f);
            titleBarRT.anchorMax = new Vector2(1f, 1f);
            titleBarRT.pivot = new Vector2(0.5f, 1f);
            titleBarRT.sizeDelta = new Vector2(0f, 60f);
            titleBarRT.anchoredPosition = Vector2.zero;

            var title = UIFactory.CreateText(titleBar.transform, "Title", "退出应用",
                fontSize: 28, alignment: TextAlignmentOptions.Center);
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = Vector2.zero;
            titleRT.anchorMax = Vector2.one;
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
            title.color = Accent;
            title.fontStyle = FontStyles.Bold;

            // 内容区
            var content = new GameObject("Content", typeof(RectTransform), typeof(Image));
            content.transform.SetParent(container.transform, false);
            var contentImg = content.GetComponent<Image>();
            contentImg.color = ContentBg;
            UIFactory.ApplyRoundedCorners(contentImg, 64, 12);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = new Vector2(16f, 92f);
            contentRT.offsetMax = new Vector2(-16f, -72f);

            // 提示文字
            var msg = UIFactory.CreateText(content.transform, "Message",
                "确定要退出应用吗？",
                fontSize: 22, alignment: TextAlignmentOptions.Center);
            var msgRT = msg.GetComponent<RectTransform>();
            msgRT.anchorMin = new Vector2(0f, 0.5f);
            msgRT.anchorMax = new Vector2(1f, 1f);
            msgRT.offsetMin = new Vector2(20f, 0f);
            msgRT.offsetMax = new Vector2(-20f, -10f);
            msg.color = Color.white;
            msg.fontStyle = FontStyles.Bold;

            // 快捷键提示
            var keyHint = UIFactory.CreateText(content.transform, "KeyHint",
                "<color=#8892A6>[Tab/左右] 切换   [Enter] 确认   [Esc] 取消</color>",
                fontSize: 14, alignment: TextAlignmentOptions.Center);
            var khRT = keyHint.GetComponent<RectTransform>();
            khRT.anchorMin = new Vector2(0f, 0.32f);
            khRT.anchorMax = new Vector2(1f, 0.48f);
            khRT.offsetMin = Vector2.zero;
            khRT.offsetMax = Vector2.zero;
            keyHint.richText = true;

            // 倒计时文字
            countdownText = UIFactory.CreateText(content.transform, "Countdown",
                "<size=80%><color=#8892A6>5.0s 后自动取消</color></size>",
                fontSize: 14, alignment: TextAlignmentOptions.Center);
            var cdRT = countdownText.GetComponent<RectTransform>();
            cdRT.anchorMin = new Vector2(0f, 0.15f);
            cdRT.anchorMax = new Vector2(1f, 0.32f);
            cdRT.offsetMin = Vector2.zero;
            cdRT.offsetMax = Vector2.zero;
            countdownText.richText = true;
            countdownText.color = HintColor;

            // 按钮区
            float btnW = 180f, btnH = 50f, spacing = 20f;

            // 退出按钮 (左)
            var confirmBtn = UIFactory.CreateRoundedButton(container.transform, "ConfirmBtn",
                "退出 (Enter)", BtnConfirm, fontSize: 20);
            confirmBtn.onClick.AddListener(ConfirmExit);
            confirmBtnBg = confirmBtn.GetComponent<Image>();
            confirmText = confirmBtn.GetComponentInChildren<TextMeshProUGUI>();
            confirmText.fontStyle = FontStyles.Bold;
            var cbRT = confirmBtn.GetComponent<RectTransform>();
            cbRT.anchorMin = new Vector2(0.5f, 0f);
            cbRT.anchorMax = new Vector2(0.5f, 0f);
            cbRT.pivot = new Vector2(1f, 0f);
            cbRT.anchoredPosition = new Vector2(-spacing / 2f, 14f);
            cbRT.sizeDelta = new Vector2(btnW, btnH);

            // 取消按钮 (右，默认焦点)
            var cancelBtn = UIFactory.CreateRoundedButton(container.transform, "CancelBtn",
                "取消 (Esc)", BtnCancel, fontSize: 20);
            cancelBtn.onClick.AddListener(CancelExit);
            cancelBtnBg = cancelBtn.GetComponent<Image>();
            cancelText = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
            cancelText.fontStyle = FontStyles.Bold;
            var cnRT = cancelBtn.GetComponent<RectTransform>();
            cnRT.anchorMin = new Vector2(0.5f, 0f);
            cnRT.anchorMax = new Vector2(0.5f, 0f);
            cnRT.pivot = new Vector2(0f, 0f);
            cnRT.anchoredPosition = new Vector2(spacing / 2f, 14f);
            cnRT.sizeDelta = new Vector2(btnW, btnH);
        }

        // ═══════════════════ 全局 Esc 监听器（自动挂载） ═══════════════════

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var go = new GameObject("[ExitConfirmPanel]");
            go.AddComponent<ExitConfirmPanel>();
            // 挂载全局 Esc 监听器
            go.AddComponent<GlobalEscListener>();
        }

        /// <summary>
        /// 全局 Esc 监听 — 当 SettingsPanel / RobotSelection / 其他模态面板未打开时，Esc 弹出退出确认
        /// </summary>
        private class GlobalEscListener : MonoBehaviour
        {
            void Update()
            {
                if (IsOpen) return; // 退出面板自己已经在处理输入
                var kb = Keyboard.current;
                if (kb == null) return;
                if (!kb.escapeKey.wasPressedThisFrame) return;

                // 检查是否有其他模态面板已打开（避免 Esc 冲突）
                if (IsAnyModalPanelOpen()) return;

                // 检查自检 UI 是否仍在（仍在则不触发退出）
                if (!StartupSelfCheck.Completed) return;

                Instance?.ShowPanel();
            }

            private bool IsAnyModalPanelOpen()
            {
                // SettingsPanel 打开时，Esc 归它处理
                if (SettingsPanel.Instance != null)
                {
                    var panelField = typeof(SettingsPanel).GetField("isOpen",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (panelField != null && (bool)panelField.GetValue(SettingsPanel.Instance))
                        return true;
                }
                // 兵种选择面板（RobotSelectionPanel / PerformanceSelectionPanel）
                var rsType = System.Type.GetType("UI.RobotSelection.RobotSelectionPanel");
                if (rsType != null)
                {
                    var instProp = rsType.GetProperty("Instance",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (instProp != null && instProp.GetValue(null) != null) return true;
                }
                return false;
            }
        }
    }
}
