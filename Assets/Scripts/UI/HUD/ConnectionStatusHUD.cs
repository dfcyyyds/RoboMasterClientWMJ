using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;

/// <summary>
/// 右下角自检状态 HUD — 显示网络连接/自检进度
/// 连接成功后自动淡出隐藏，断线时重新显示
/// </summary>
public class ConnectionStatusHUD : MonoBehaviour
{
    private Canvas canvas;
    private Image bgPanel;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI detailText;
    private Image indicator;
    private CanvasGroup canvasGroup;

    // 动画
    private float targetAlpha = 1f;
    private float fadeSpeed = 2f;
    private float autoHideTimer = -1f;
    private const float AutoHideDelay = 5f; // 连接成功后 5 秒自动淡出

    // 状态追踪
    private bool mqttConnected;
    private bool udpStarted;
    private string currentStatus = "";
    private int mqttRetryCount;
    private float lastMqttFailTime;

    void Awake()
    {
        Debug.Log("[ConnectionStatusHUD] Awake — 开始构建 HUD UI");
        BuildUI();
        SubscribeEvents();
        Debug.Log($"[ConnectionStatusHUD] HUD 构建完成, canvas={canvas != null}");
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    // ═══════════════════ 公共接口 ═══════════════════

    /// <summary>设置状态文字（由 NetworkManager 调用）</summary>
    public void SetStatus(string message)
    {
        currentStatus = message;
        if (statusText != null)
            statusText.text = message;

        // 显示面板
        Show();

        // 连接成功时启动自动隐藏
        if (message.Contains("✅"))
        {
            autoHideTimer = AutoHideDelay;
            UpdateIndicatorColor(new Color(0.2f, 0.9f, 0.3f)); // 绿色
        }
        else if (message.Contains("❌"))
        {
            autoHideTimer = -1f;
            UpdateIndicatorColor(new Color(0.9f, 0.2f, 0.2f)); // 红色
        }
        else
        {
            autoHideTimer = -1f;
            UpdateIndicatorColor(new Color(1f, 0.8f, 0.2f)); // 黄色（进行中）
        }
    }

    // ═══════════════════ 内部逻辑 ═══════════════════

    private void SubscribeEvents()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMqttConnected += OnMqttConnected;
            NetworkManager.Instance.OnMqttDisconnected += OnMqttDisconnected;
            NetworkManager.Instance.OnUdpStarted += OnUdpStarted;
        }
    }

    private void UnsubscribeEvents()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMqttConnected -= OnMqttConnected;
            NetworkManager.Instance.OnMqttDisconnected -= OnMqttDisconnected;
            NetworkManager.Instance.OnUdpStarted -= OnUdpStarted;
        }
    }

    private void OnMqttConnected()
    {
        mqttConnected = true;
        mqttRetryCount = 0;
        UpdateDetail();
    }

    private void OnMqttDisconnected()
    {
        mqttConnected = false;
        mqttRetryCount++;
        lastMqttFailTime = Time.realtimeSinceStartup;
        UpdateDetail();
        Show();
        autoHideTimer = -1f;
        UpdateIndicatorColor(new Color(1f, 0.5f, 0.1f)); // 橙色
    }

    private void OnUdpStarted()
    {
        udpStarted = true;
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        if (detailText == null) return;
        string mqtt = mqttConnected ? "<color=#4CFF4C>已连接</color>" : $"<color=#FF6644>断开</color>";
        string udp = udpStarted ? "<color=#4CFF4C>监听中</color>" : "<color=#AAAAAA>未启动</color>";
        if (mqttRetryCount > 0 && !mqttConnected)
            mqtt += $" (重试 {mqttRetryCount} 次)";
        detailText.text = $"MQTT: {mqtt}  |  UDP: {udp}";
    }

    private void Show()
    {
        targetAlpha = 1f;
    }

    private void UpdateIndicatorColor(Color c)
    {
        if (indicator != null)
            indicator.color = c;
    }

    void Update()
    {
        if (canvasGroup == null) return;

        // 自动隐藏计时
        if (autoHideTimer > 0f)
        {
            autoHideTimer -= Time.unscaledDeltaTime;
            if (autoHideTimer <= 0f)
            {
                targetAlpha = 0f;
            }
        }

        // 平滑淡入淡出
        if (!Mathf.Approximately(canvasGroup.alpha, targetAlpha))
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
        }
    }

    // ═══════════════════ 构建 UI ═══════════════════

    private void BuildUI()
    {
        // 独立 Canvas，sortingOrder 极高以覆盖其他 UI
        canvas = UIFactory.CreateCanvas("ConnectionStatusCanvas", 31000);
        // 注意：ScreenSpaceOverlay Canvas 不能作为子物体，否则不渲染
        Object.DontDestroyOnLoad(canvas.gameObject);
        canvas.targetDisplay = 0;
        var root = canvas.transform;

        Debug.Log($"[ConnectionStatusHUD] Canvas 创建: enabled={canvas.enabled}, isRootCanvas={canvas.isRootCanvas}, " +
                  $"renderMode={canvas.renderMode}, sortingOrder={canvas.sortingOrder}, screen={Screen.width}x{Screen.height}");

        canvasGroup = UIFactory.EnsureCanvasGroup(canvas.gameObject);
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // 背景面板 - 右下角 (SettingsPanel 风格)
        var panelGO = new GameObject("StatusPanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(root, false);
        bgPanel = panelGO.GetComponent<Image>();
        bgPanel.color = new Color(0.04f, 0.05f, 0.10f, 0.96f); // PanelBg
        UIFactory.ApplyRoundedCorners(bgPanel, 64, 12);

        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(1f, 0f);
        panelRT.anchoredPosition = new Vector2(-20f, 20f);
        panelRT.sizeDelta = new Vector2(520f, 96f);

        // 状态指示灯（圆点，更大）
        var indicatorGO = new GameObject("Indicator", typeof(RectTransform), typeof(Image));
        indicatorGO.transform.SetParent(panelGO.transform, false);
        indicator = indicatorGO.GetComponent<Image>();
        indicator.color = new Color(1f, 0.8f, 0.2f); // 默认黄色
        UIFactory.ApplyRoundedCorners(indicator, 32, 16);

        var indRT = indicatorGO.GetComponent<RectTransform>();
        indRT.anchorMin = new Vector2(0f, 0.5f);
        indRT.anchorMax = new Vector2(0f, 0.5f);
        indRT.pivot = new Vector2(0.5f, 0.5f);
        indRT.anchoredPosition = new Vector2(22f, 0f);
        indRT.sizeDelta = new Vector2(16f, 16f);

        // 主状态文字 — 字号加大
        statusText = UIFactory.CreateText(panelGO.transform, "StatusText", "初始化中...",
            fontSize: 20, alignment: TextAlignmentOptions.MidlineLeft);
        var stRT = statusText.GetComponent<RectTransform>();
        stRT.anchorMin = new Vector2(0f, 0.5f);
        stRT.anchorMax = new Vector2(1f, 1f);
        stRT.offsetMin = new Vector2(44f, 0f);
        stRT.offsetMax = new Vector2(-14f, -6f);
        statusText.color = Color.white;
        statusText.fontStyle = FontStyles.Bold;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.overflowMode = TextOverflowModes.Ellipsis;

        // 详细状态（MQTT/UDP）— 字号加大
        detailText = UIFactory.CreateText(panelGO.transform, "DetailText", "MQTT: -- | UDP: --",
            fontSize: 16, alignment: TextAlignmentOptions.MidlineLeft);
        var dtRT = detailText.GetComponent<RectTransform>();
        dtRT.anchorMin = new Vector2(0f, 0f);
        dtRT.anchorMax = new Vector2(1f, 0.5f);
        dtRT.offsetMin = new Vector2(44f, 6f);
        dtRT.offsetMax = new Vector2(-14f, 0f);
        detailText.color = new Color(0.55f, 0.65f, 0.78f, 0.95f); // HintColor
        detailText.richText = true;
        detailText.textWrappingMode = TextWrappingModes.NoWrap;
        detailText.overflowMode = TextOverflowModes.Ellipsis;
    }
}
