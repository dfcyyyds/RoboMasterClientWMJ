using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using Debug = UnityEngine.Debug;

/// <summary>
/// 启动自检 — 在所有业务逻辑之前运行
/// 检查项: FFmpeg / 本机IP / 网络可达性 / 配置文件 / 硬件能力
/// 自检通过后才允许后续流程继续
/// </summary>
[DefaultExecutionOrder(-3000)] // 最早执行
public class StartupSelfCheck : MonoBehaviour
{
    public static StartupSelfCheck Instance { get; private set; }

    /// <summary>自检是否全部通过（无致命错误）</summary>
    public static bool Passed { get; private set; }

    /// <summary>自检是否已完成（不论通过与否）</summary>
    public static bool Completed { get; private set; }

    /// <summary>自检完成事件</summary>
    public static event Action<bool> OnCheckCompleted;

    // UI 元素
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform listRoot;
    private TextMeshProUGUI titleText;
    private Image bgOverlay;
    private Button continueBtn;
    private TextMeshProUGUI continueBtnText;

    private List<CheckItem> checkItems = new List<CheckItem>();
    private int currentCheck = 0;
    private bool hasFatalError = false;

    // 检查项数据
    private class CheckItem
    {
        public string Name;
        public Func<CheckResult> Check; // 同步检查
        public TextMeshProUGUI Label;
        public Image Icon;
        public CheckResult Result;
    }

    public enum CheckSeverity { OK, Warning, Fatal }
    public struct CheckResult
    {
        public CheckSeverity Severity;
        public string Message;
        public static CheckResult Ok(string msg = "") => new CheckResult { Severity = CheckSeverity.OK, Message = msg };
        public static CheckResult Warn(string msg) => new CheckResult { Severity = CheckSeverity.Warning, Message = msg };
        public static CheckResult Fatal(string msg) => new CheckResult { Severity = CheckSeverity.Fatal, Message = msg };
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        Debug.Log("[StartupSelfCheck] Awake — 开始构建自检 UI");
        RegisterChecks();
        BuildUI();
        Debug.Log($"[StartupSelfCheck] UI 构建完成, canvas={canvas != null}, checks={checkItems.Count}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        var go = new GameObject("[StartupSelfCheck]");
        go.AddComponent<StartupSelfCheck>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        StartCoroutine(RunChecksSequentially());
    }

    void Update()
    {
        // 检查按钮可用时，支持键盘 Enter / Space 进入客户端（Esc 留给退出确认）
        if (continueBtn != null && continueBtn.gameObject.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) ||
                Input.GetKeyDown(KeyCode.Space))
            {
                DismissAndContinue();
            }
        }
    }

    // ═══════════════════ 注册所有检查项 ═══════════════════

    private void RegisterChecks()
    {
        checkItems.Add(new CheckItem { Name = "配置文件", Check = CheckConfig });
        checkItems.Add(new CheckItem { Name = "硬件检测", Check = CheckHardware });
        checkItems.Add(new CheckItem { Name = "FFmpeg 解码器", Check = CheckFfmpeg });
        checkItems.Add(new CheckItem { Name = "本机 IP 地址", Check = CheckLocalIp });
        checkItems.Add(new CheckItem { Name = "服务器可达性", Check = CheckServerReachable });
    }

    // ═══════════════════ 逐项执行检查 ═══════════════════

    private IEnumerator RunChecksSequentially()
    {
        yield return null; // 等一帧让 UI 渲染

        for (int i = 0; i < checkItems.Count; i++)
        {
            currentCheck = i;
            var item = checkItems[i];

            // 设置为检查中状态
            SetItemStatus(item, "...", "检查中...", new Color(0.8f, 0.8f, 0.2f));
            yield return null; // 让 UI 更新

            // 执行检查（在主线程，但每项之间 yield）
            try
            {
                item.Result = item.Check();
            }
            catch (Exception ex)
            {
                item.Result = CheckResult.Fatal($"检查异常: {ex.Message}");
            }

            // 更新 UI
            switch (item.Result.Severity)
            {
                case CheckSeverity.OK:
                    SetItemStatus(item, "✅", item.Result.Message, new Color(0.3f, 0.9f, 0.4f));
                    break;
                case CheckSeverity.Warning:
                    SetItemStatus(item, "⚠️", item.Result.Message, new Color(1f, 0.8f, 0.2f));
                    break;
                case CheckSeverity.Fatal:
                    SetItemStatus(item, "❌", item.Result.Message, new Color(1f, 0.3f, 0.3f));
                    hasFatalError = true;
                    break;
            }

            yield return new WaitForSecondsRealtime(0.15f); // 小延迟让用户看到逐项过程
        }

        // 所有检查完成
        Passed = !hasFatalError;
        Completed = true;

        if (hasFatalError)
        {
            titleText.text = "⛔ 自检未通过 — 请修复以下问题后重启";
            titleText.color = new Color(1f, 0.4f, 0.4f);
            continueBtnText.text = "忽略并继续（可能无法正常使用）";
            continueBtnText.color = new Color(1f, 0.6f, 0.3f);
        }
        else
        {
            titleText.text = "✅ 自检通过";
            titleText.color = new Color(0.3f, 1f, 0.5f);
            continueBtnText.text = "进入客户端";
        }

        continueBtn.gameObject.SetActive(true);

        // 不再自动关闭——等待用户点击按钮（亦可观察自检结果）
        yield break;
    }

    private void DismissAndContinue()
    {
        wmj.Log.I($"[SelfCheck] 自检完成: passed={Passed}, fatal={hasFatalError}", wmj.Log.Tag.General);
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            if (canvasGroup != null) canvasGroup.alpha = 1f - t / 0.3f;
            yield return null;
        }

        OnCheckCompleted?.Invoke(Passed);

        if (canvas != null) Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    // ═══════════════════ 各项检查实现 ═══════════════════

    private CheckResult CheckConfig()
    {
        try
        {
            if (!ConfigLoader.IsLoaded) ConfigLoader.LoadConfig();
            if (ConfigLoader.config == null)
                return CheckResult.Fatal("params.json 加载失败");

            var gp = GameParamsConfig.Get;
            string mode = gp.isCompetitionMode ? "比赛模式" : "仿真模式";
            string ip = gp.isCompetitionMode ? gp.competitionServerIp : "127.0.0.1";
            int port = gp.isCompetitionMode ? gp.competitionServerPort : ConfigLoader.config.dataPort;
            string srcPath = GameParamsConfig.LoadedPath ?? "(unknown)";
            // 标记读取来源：persist=用户配置，stream=打包默认
            string srcTag = srcPath.Contains("StreamingAssets") ? "默认" : "用户";
            return CheckResult.Ok($"{mode} | 目标: {ip}:{port} | 源: {srcTag}");
        }
        catch (Exception ex)
        {
            return CheckResult.Fatal($"配置加载异常: {ex.Message}");
        }
    }

    private CheckResult CheckHardware()
    {
        try
        {
            string gpu = SystemInfo.graphicsDeviceName;
            int vram = SystemInfo.graphicsMemorySize;
            string api = SystemInfo.graphicsDeviceType.ToString();
            int cores = SystemInfo.processorCount;
            int ram = SystemInfo.systemMemorySize;

            string info = $"{gpu} ({api}), {vram}MB VRAM, {cores}核, {ram}MB RAM";

            if (vram < 512)
                return CheckResult.Warn($"显存过低 ({vram}MB) | {info}");

            return CheckResult.Ok(info);
        }
        catch (Exception ex)
        {
            return CheckResult.Warn($"硬件检测异常: {ex.Message}");
        }
    }

    private CheckResult CheckFfmpeg()
    {
        try
        {
            // 检查 NVDEC 原生插件是否可用
            bool hasNvdec = SystemInfo.graphicsDeviceName.ToLower().Contains("nvidia") ||
                            SystemInfo.graphicsDeviceName.ToLower().Contains("geforce") ||
                            SystemInfo.graphicsDeviceName.ToLower().Contains("rtx") ||
                            SystemInfo.graphicsDeviceName.ToLower().Contains("gtx");

            // 检查 ffmpeg
            bool ffmpegOk = false;
            string ffmpegVersion = "";
            try
            {
                var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = Framework.Video.FfmpegLocator.GetExecutablePath(),
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                proc.Start();
                ffmpegVersion = proc.StandardOutput.ReadLine() ?? "";
                proc.WaitForExit(3000);
                if (!proc.HasExited) proc.Kill();
                ffmpegOk = ffmpegVersion.Contains("ffmpeg");
            }
            catch { }

            if (hasNvdec && ffmpegOk)
                return CheckResult.Ok($"NVDEC + FFmpeg 可用");
            if (hasNvdec && !ffmpegOk)
                return CheckResult.Warn("NVDEC 可用，FFmpeg 未安装（回退解码不可用）");
            if (!hasNvdec && ffmpegOk)
                return CheckResult.Ok($"FFmpeg 软解模式");
            // 都没有
            return CheckResult.Fatal("未检测到 FFmpeg！视频解码不可用。\n请安装 ffmpeg 并加入系统 PATH");
        }
        catch (Exception ex)
        {
            return CheckResult.Fatal($"解码器检测异常: {ex.Message}");
        }
    }

    private CheckResult CheckLocalIp()
    {
        try
        {
            bool isCompetition = GameParamsConfig.Get.isCompetitionMode;
            if (!isCompetition)
                return CheckResult.Ok("仿真模式，无需配置本机 IP");

            // 比赛模式要求本机 IP 为 192.168.12.2
            string requiredIp = "192.168.12.2";
            string requiredSubnet = "192.168.12.";

            var localAddresses = new List<string>();
            bool found = false;

            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != OperationalStatus.Up) continue;
                    if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = addr.Address.ToString();
                        localAddresses.Add($"{ip} ({iface.Name})");
                        if (ip == requiredIp) found = true;
                    }
                }
            }
            catch
            {
                // Fallback: Dns 方式
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var addr in host.AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localAddresses.Add(addr.ToString());
                        if (addr.ToString() == requiredIp) found = true;
                    }
                }
            }

            if (found)
                return CheckResult.Ok($"本机 IP: {requiredIp} ✓");

            // 检查是否至少在同一子网
            bool sameSubnet = localAddresses.Any(a => a.StartsWith(requiredSubnet));
            string ips = string.Join(", ", localAddresses.Take(3));

            if (sameSubnet)
                return CheckResult.Warn($"未找到 {requiredIp}，但在同一子网 | 当前: {ips}");

            return CheckResult.Fatal($"本机未配置 {requiredIp}\n当前: {ips}\n请将网卡 IP 设为 {requiredIp}/24");
        }
        catch (Exception ex)
        {
            return CheckResult.Warn($"IP 检测异常: {ex.Message}");
        }
    }

    private CheckResult CheckServerReachable()
    {
        try
        {
            bool isCompetition = GameParamsConfig.Get.isCompetitionMode;
            string targetIp = isCompetition ? GameParamsConfig.Get.competitionServerIp : "127.0.0.1";
            int targetPort = isCompetition ? GameParamsConfig.Get.competitionServerPort : ConfigLoader.config.dataPort;

            // TCP 快速连接测试（超时 2 秒）
            try
            {
                using (var tcp = new TcpClient())
                {
                    var result = tcp.BeginConnect(targetIp, targetPort, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    if (connected && tcp.Connected)
                    {
                        tcp.EndConnect(result);
                        return CheckResult.Ok($"{targetIp}:{targetPort} 可达");
                    }
                }
            }
            catch { }

            // TCP 失败，尝试 ping
            try
            {
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = ping.Send(targetIp, 2000);
                    if (reply != null && reply.Status == IPStatus.Success)
                        return CheckResult.Warn($"{targetIp} 可 ping 通 ({reply.RoundtripTime}ms)，但端口 {targetPort} 未响应");
                }
            }
            catch { }

            if (!isCompetition)
                return CheckResult.Warn($"MockServer ({targetIp}:{targetPort}) 未运行，启动后将自动重连");

            return CheckResult.Fatal($"无法连接 {targetIp}:{targetPort}\n请检查网线连接和 IP 配置");
        }
        catch (Exception ex)
        {
            return CheckResult.Warn($"网络检测异常: {ex.Message}");
        }
    }

    // ═══════════════════ 构建 UI ═══════════════════

    private void SetItemStatus(CheckItem item, string icon, string message, Color color)
    {
        if (item.Label != null)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            // 上行：图标 + 名称；下行：较小的状态消息
            item.Label.text = $"<color=#{hex}><b>{icon} {item.Name}</b></color>\n<size=75%><color=#B8C2D6>{message}</color></size>";
        }
    }

    private void BuildUI()
    {
        // 全屏 Canvas
        var canvasGO = new GameObject("[SelfCheckCanvas]");
        DontDestroyOnLoad(canvasGO);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // 最高层（但在短整数范围内，避免渲染异常）
        canvas.targetDisplay = 0;
        canvas.pixelPerfect = false;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Debug.Log($"[StartupSelfCheck] Canvas 创建: enabled={canvas.enabled}, isRootCanvas={canvas.isRootCanvas}, " +
                  $"renderMode={canvas.renderMode}, sortingOrder={canvas.sortingOrder}, targetDisplay={canvas.targetDisplay}, " +
                  $"pixelRect={canvas.pixelRect}, screen={Screen.width}x{Screen.height}");

        // 半透明背景遮罩（更深、更明显的暗色）
        bgOverlay = UIFactory.CreateFullScreenImage(canvas.transform, "BgOverlay", new Color(0.02f, 0.03f, 0.06f, 0.92f));

        // ═══ 中央容器 — SettingsPanel 风格 ═══
        var container = new GameObject("Container", typeof(RectTransform));
        container.transform.SetParent(canvas.transform, false);
        var containerRT = container.GetComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 0.5f);
        containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.pivot = new Vector2(0.5f, 0.5f);
        containerRT.sizeDelta = new Vector2(900f, 620f);

        // 容器背景 — 使用 SettingsPanel 的 PanelBg 色
        var containerBg = container.AddComponent<Image>();
        containerBg.color = new Color(0.04f, 0.05f, 0.10f, 0.98f);
        UIFactory.ApplyRoundedCorners(containerBg, 64, 18);

        // ═══ 标题栏（TitleBarBg 色，顶部带深色条） ═══
        var titleBar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
        titleBar.transform.SetParent(container.transform, false);
        var titleBarImg = titleBar.GetComponent<Image>();
        titleBarImg.color = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        UIFactory.ApplyRoundedCorners(titleBarImg, 64, 18);
        var titleBarRT = titleBar.GetComponent<RectTransform>();
        titleBarRT.anchorMin = new Vector2(0f, 1f);
        titleBarRT.anchorMax = new Vector2(1f, 1f);
        titleBarRT.pivot = new Vector2(0.5f, 1f);
        titleBarRT.sizeDelta = new Vector2(0f, 72f);
        titleBarRT.anchoredPosition = Vector2.zero;

        // 标题文字
        titleText = UIFactory.CreateText(titleBar.transform, "Title", "启动自检",
            fontSize: 34, alignment: TextAlignmentOptions.Center);
        var titleRT = titleText.GetComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = new Vector2(24f, 0f);
        titleRT.offsetMax = new Vector2(-24f, 0f);
        titleText.color = new Color(0.35f, 0.72f, 0.98f, 1f); // Accent 色
        titleText.fontStyle = FontStyles.Bold;

        // ═══ 内容区（ContentBg） ═══
        var content = new GameObject("Content", typeof(RectTransform), typeof(Image));
        content.transform.SetParent(container.transform, false);
        var contentImg = content.GetComponent<Image>();
        contentImg.color = new Color(0.05f, 0.06f, 0.12f, 0.90f);
        UIFactory.ApplyRoundedCorners(contentImg, 64, 12);
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 0f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.offsetMin = new Vector2(20f, 90f);  // 底部留按钮空间
        contentRT.offsetMax = new Vector2(-20f, -84f); // 顶部留标题栏空间

        // 检查项列表容器
        var listGO = new GameObject("CheckList", typeof(RectTransform));
        listGO.transform.SetParent(content.transform, false);
        listRoot = listGO.GetComponent<RectTransform>();
        listRoot.anchorMin = Vector2.zero;
        listRoot.anchorMax = Vector2.one;
        listRoot.offsetMin = new Vector2(20f, 20f);
        listRoot.offsetMax = new Vector2(-20f, -20f);

        // 为每个检查项创建带背景的行
        float lineHeight = 68f;
        float lineSpacing = 8f;
        float startY = 0f;
        for (int i = 0; i < checkItems.Count; i++)
        {
            var item = checkItems[i];

            // 行背景 — 交替底色
            var rowGO = new GameObject($"Row_{i}", typeof(RectTransform), typeof(Image));
            rowGO.transform.SetParent(listRoot, false);
            var rowImg = rowGO.GetComponent<Image>();
            rowImg.color = (i % 2 == 0) ? new Color(0.07f, 0.08f, 0.14f, 0.60f)
                                         : new Color(0.05f, 0.06f, 0.11f, 0.45f);
            UIFactory.ApplyRoundedCorners(rowImg, 64, 8);
            var rowRT = rowGO.GetComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = new Vector2(0f, startY - i * (lineHeight + lineSpacing));
            rowRT.sizeDelta = new Vector2(0f, lineHeight);

            // 行文字 — 更大字号
            var label = UIFactory.CreateText(rowGO.transform, "Label",
                $"<b>○ {item.Name}</b>  <size=80%><color=#8892A6>等待检查...</color></size>",
                fontSize: 22, alignment: TextAlignmentOptions.MidlineLeft);
            var labelRT = label.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(24f, 6f);
            labelRT.offsetMax = new Vector2(-24f, -6f);
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = new Color(0.88f, 0.92f, 0.98f);
            item.Label = label;
        }

        // ═══ 底部按钮区 ═══
        continueBtn = UIFactory.CreateRoundedButton(container.transform, "ContinueBtn", "进入客户端 (Enter)",
            new Color(0.16f, 0.50f, 0.88f, 0.95f), fontSize: 24);
        continueBtn.onClick.AddListener(() => DismissAndContinue());
        var btnRT = continueBtn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0f);
        btnRT.anchorMax = new Vector2(0.5f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 22f);
        btnRT.sizeDelta = new Vector2(320f, 56f);
        continueBtnText = continueBtn.GetComponentInChildren<TextMeshProUGUI>();
        continueBtnText.fontStyle = FontStyles.Bold;
        continueBtn.gameObject.SetActive(false);
    }
}
