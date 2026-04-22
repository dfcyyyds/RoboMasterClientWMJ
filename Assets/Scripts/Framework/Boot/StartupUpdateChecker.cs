using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

/// <summary>
/// WMJ 客户端启动更新检测 + 进度下载器
/// ─────────────────────────────────────────────────────────
/// 在 StartupSelfCheck 之前执行（-3500 < -3000）
/// 流程：
///   1. 拉远端 manifest.json，与本地 manifest.local.json 比较
///   2. 无差异：静默放行
///   3. 有差异：弹出 SettingsPanel 风格更新提示
///        [立即更新 U] / [跳过本次 S] / [禁用检测 D]
///   4. 用户选立即更新 → 切到下载进度页面：
///        - 在 Unity 内逐个 GET 变更文件
///        - 写入 <installDir>/.update_staging/<rel>（避免占用运行中文件）
///        - 每个文件 SHA-256 校验通过后保留
///        - 显示总进度 / 当前文件 / 速率
///   5. 全部完成 → 写 swap 脚本，Application.Quit()，脚本接管原子替换并重启
///
/// 与 launcher 的关系：launcher 仅用于"裸机首次安装"。客户端运行后均走此 UI。
/// 绝不重复下载：依据 (path, size, sha256) 三元组短路。
/// 绝不重复拷贝：staging → 原位 mv -f 单次原子替换。
/// </summary>
[DefaultExecutionOrder(-3500)]
public class StartupUpdateChecker : MonoBehaviour
{
    public static StartupUpdateChecker Instance { get; private set; }
    public static bool Completed { get; private set; }

    // ═══ 严格沿用 SettingsPanel.cs 的配色 ═══
    private static readonly Color PanelBg      = new Color(0.04f, 0.05f, 0.10f, 0.96f);
    private static readonly Color SidebarBg    = new Color(0.03f, 0.04f, 0.08f, 0.98f);
    private static readonly Color ContentBg    = new Color(0.05f, 0.06f, 0.12f, 0.90f);
    private static readonly Color TitleBarBg   = new Color(0.03f, 0.04f, 0.08f, 0.95f);
    private static readonly Color RowEven      = new Color(0.07f, 0.08f, 0.14f, 0.60f);
    private static readonly Color RowOdd       = new Color(0.05f, 0.06f, 0.11f, 0.45f);
    private static readonly Color RowFocused   = new Color(0.15f, 0.25f, 0.45f, 0.80f);
    private static readonly Color SliderTrack  = new Color(0.08f, 0.08f, 0.16f, 0.95f);
    private static readonly Color SliderFill   = new Color(0.22f, 0.55f, 0.95f, 0.70f);
    private static readonly Color Accent       = new Color(0.35f, 0.72f, 0.98f, 1f);
    private static readonly Color BtnSave      = new Color(0.16f, 0.50f, 0.88f, 0.80f);
    private static readonly Color BtnReset     = new Color(0.80f, 0.25f, 0.20f, 0.65f);
    private static readonly Color BtnClose     = new Color(0.35f, 0.35f, 0.40f, 0.60f);
    private static readonly Color HintColor    = new Color(0.55f, 0.65f, 0.78f, 0.80f);
    private static readonly Color TextWhite    = new Color(0.95f, 0.96f, 1.00f, 1f);
    private static readonly Color Divider      = new Color(0.35f, 0.72f, 0.98f, 0.30f);

    private const string SLOGAN = "百铸千炼 · 我亦为剑";
    private const string DEFAULT_BASE = "https://antientropy.xin/robomaster/";
    private const string MANIFEST_NAME = "manifest.json";
    private const string LOCAL_MANIFEST = "manifest.local.json";
    // 更新后用的待提交 manifest 名：swap 脚本在替换完所有其他文件后，
    // 才将其原子重命名为 manifest.local.json，避免部分替换成功导致的状态错乱
    private const string LOCAL_MANIFEST_PENDING = "manifest.local.json.pending";
    private const string STAGING_DIR = ".update_staging";
    private const string DISABLE_FLAG = ".wmj_disable_auto_check";
    private const int HTTP_TIMEOUT = 12;

    [Serializable] private class ManifestFile { public string path; public long size; public string sha256; public bool exec; }
    [Serializable] private class Manifest {
        public string version;
        public string build_time;
        public string source_sha;
        public string platform;
        public List<ManifestFile> files;
    }

    private string installDir;
    private string platform;
    private string updateBase;
    private string filesBase;     // updateBase + "files/"
    private Manifest remoteManifest;
    private Manifest localManifest;
    private string remoteManifestRaw;
    private List<ManifestFile> needDownload;
    private long totalNeedBytes;

    private Canvas canvas;
    private GameObject promptPage;
    private GameObject progressPage;
    // EventSystem 在 BeforeSceneLoad 时场景内通常还不存在，需自己创建以保证按钮可点击
    private GameObject ownedEventSystem;
    // 若需要附着到现有 EventSystem 上以保证鼠标点击有效，记录下来在 OnDestroy 时回滚
    private EventSystem attachedEventSystem;
    private InputSystemUIInputModule attachedInputModule;
    private readonly List<BaseInputModule> disabledInputModules = new List<BaseInputModule>();

    // 进度 UI 引用
    private TextMeshProUGUI tProgPctTxt;
    private Image           progressFill;
    private TextMeshProUGUI tCurrentFile;
    private TextMeshProUGUI tStats;
    private TextMeshProUGUI tSpeed;
    private Button          cancelBtn;

    private bool downloading;
    private bool cancelled;

    // ─── 轮播相册 ───
    private const int ALBUM_COUNT = 8;
    private const float ALBUM_INTERVAL = 5.5f;
    private const float ALBUM_FADE = 1.2f;
    private const string ALBUM_URL = "https://antientropy.xin/robomaster/album/";
    private Texture2D[] albumTextures;
    private RawImage albumImgA, albumImgB;
    private CanvasGroup albumGrpA, albumGrpB;
    private int albumIndex;
    private Coroutine albumRoutine;
    private TextMeshProUGUI albumCounterTxt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (IsDisabledByEnv())     { Completed = true; Debug.Log("[WMJ-Update] 已禁用（环境变量）"); return; }
        try
        {
            var instDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (File.Exists(Path.Combine(instDir, DISABLE_FLAG)))
            { Completed = true; Debug.Log("[WMJ-Update] 已禁用（flag 文件）"); return; }
        }
        catch { }

        var go = new GameObject("[StartupUpdateChecker]");
        go.AddComponent<StartupUpdateChecker>();
        DontDestroyOnLoad(go);
    }

    private static bool IsDisabledByEnv()
    {
        return Environment.GetEnvironmentVariable("WMJ_DISABLE_AUTO_CHECK") == "1"
            || Environment.GetEnvironmentVariable("ROBOMASTER_SKIP_UPDATE") == "1";
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        installDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        platform = Application.platform == RuntimePlatform.WindowsPlayer ? "Windows64" : "Linux64";
        updateBase = (Environment.GetEnvironmentVariable("ROBOMASTER_UPDATE_URL")
                      ?? (DEFAULT_BASE + platform + "/")).TrimEnd('/') + "/";
        filesBase = updateBase + "files/";
    }

    void Start() { StartCoroutine(CheckRoutine()); }

    // ═══════════════════ 检测阶段 ═══════════════════
    private IEnumerator CheckRoutine()
    {
        string url = updateBase + MANIFEST_NAME + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = HTTP_TIMEOUT;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[WMJ-Update] 拉取 manifest 失败: {req.error}");
                Completed = true; yield break;
            }
            remoteManifestRaw = req.downloadHandler.text;
            try { remoteManifest = JsonUtility.FromJson<Manifest>(remoteManifestRaw); }
            catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] manifest 解析失败: {ex.Message}"); Completed = true; yield break; }
        }

        if (remoteManifest == null || remoteManifest.files == null)
        { Completed = true; yield break; }

        localManifest = LoadLocalManifest();
        ComputeDiff();

        Debug.Log($"[WMJ-Update] remote={remoteManifest.version} local={localManifest?.version ?? "(none)"} " +
                  $"need={needDownload.Count} ({totalNeedBytes/1024f/1024f:F1}MB)");

        if (needDownload.Count == 0)
        { Debug.Log("[WMJ-Update] ✓ 已是最新"); Completed = true; yield break; }

        BuildCanvas();
        BuildPromptPage();
    }

    private Manifest LoadLocalManifest()
    {
        string path = Path.Combine(installDir, LOCAL_MANIFEST);
        if (!File.Exists(path)) return null;
        try { return JsonUtility.FromJson<Manifest>(File.ReadAllText(path, Encoding.UTF8)); }
        catch { return null; }
    }

    private void ComputeDiff()
    {
        var localMap = new Dictionary<string, ManifestFile>();
        if (localManifest?.files != null)
            foreach (var e in localManifest.files) localMap[e.path] = e;

        needDownload = new List<ManifestFile>();
        totalNeedBytes = 0;
        foreach (var r in remoteManifest.files)
        {
            if (localMap.TryGetValue(r.path, out var l) && l.size == r.size && l.sha256 == r.sha256)
                continue;

            // 兜底：本地 manifest 缺失或不匹配时，再按实际磁盘文件 size+sha256 比对一次。
            // 这样即使 manifest 由于 swap 中途失败而未能提交，只要文件内容与 remote 一致，就不会触发"幽灵更新"。
            // 仅对小于 32MB 的文件做此昂贵比对，避免大包上多算 sha。
            try
            {
                string abs = Path.Combine(installDir, r.path);
                if (File.Exists(abs))
                {
                    var fi = new FileInfo(abs);
                    if (fi.Length == r.size && r.size <= 32L * 1024 * 1024)
                    {
                        if (Sha256(abs) == r.sha256) continue;
                    }
                }
            }
            catch { /* ignore */ }

            needDownload.Add(r);
            totalNeedBytes += r.size;
        }

        // 若经过磁盘校验确认所有文件完好，但 manifest.local.json 仍不一致，此处提前补写 manifest，
        // 彻底避免下次启动再触发更新提示（对"总是错误显示有可用更新"这类残留态非常必要）。
        if (needDownload.Count == 0 && !string.IsNullOrEmpty(remoteManifestRaw))
        {
            try
            {
                string path = Path.Combine(installDir, LOCAL_MANIFEST);
                File.WriteAllText(path, remoteManifestRaw, new UTF8Encoding(false));
                Debug.Log("[WMJ-Update] 磁盘内容与远端一致，已同步 manifest.local.json");
            }
            catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] 回写 manifest 失败: {ex.Message}"); }
        }
    }

    // ═══════════════════ Canvas / 通用样式 ═══════════════════
    private void BuildCanvas()
    {
        EnsureEventSystem();

        var go = new GameObject("[WMJ-UpdateCanvas]");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        s.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var overlay = NewImage(go.transform, "Overlay", new Color(0, 0, 0, 0.70f));
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = true;
    }

    /// <summary>确保存在可用的 EventSystem + InputSystemUIInputModule。
    /// 在 BeforeSceneLoad 之后我们的 UI 往往与场景 EventSystem 共存；
    /// 若场景 EventSystem 只有旧版 StandaloneInputModule，在使用 "Input System Package (New)"
    /// 的项目下鼠标事件不会路由——此时我们动态禁用旧模块并补上 InputSystemUIInputModule。</summary>
    private void EnsureEventSystem()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            var go = new GameObject("[WMJ-UpdateEventSystem]");
            go.transform.SetParent(transform, false);
            es = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            ownedEventSystem = go;
            Debug.Log("[WMJ-Update] 已临时创建 EventSystem（场景无 EventSystem）");
            return;
        }

        // 已有 EventSystem：若没有 InputSystemUIInputModule，就禁用其它 BaseInputModule 并补一个
        var ism = es.GetComponent<InputSystemUIInputModule>();
        if (ism == null)
        {
            foreach (var m in es.GetComponents<BaseInputModule>())
            {
                if (!m.enabled) continue;
                m.enabled = false;
                disabledInputModules.Add(m);
            }
            ism = es.gameObject.AddComponent<InputSystemUIInputModule>();
            attachedEventSystem = es;
            attachedInputModule = ism;
            Debug.Log($"[WMJ-Update] 已在现有 EventSystem 上启用 InputSystemUIInputModule（禁用旧模块 {disabledInputModules.Count} 个）");
        }
    }

    /// <summary>构造 SettingsPanel 风格的主面板骨架（标题栏 + Logo + 主体 + 帮助栏），返回内容区父 RectTransform</summary>
    private (GameObject panel, RectTransform content, RectTransform helpBar) BuildPanelShell(string subtitle, Vector2 size)
    {
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvas.transform, false);
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = PanelBg;
        var prt = panelImg.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = size;
        prt.anchoredPosition = Vector2.zero;
        panelImg.raycastTarget = true;

        // 顶部细边
        var topLine = NewImage(prt, "TopAccent", Accent);
        var tlrt = topLine.rectTransform;
        tlrt.anchorMin = new Vector2(0, 1); tlrt.anchorMax = new Vector2(1, 1);
        tlrt.pivot = new Vector2(0.5f, 1f);
        tlrt.sizeDelta = new Vector2(0, 2);
        tlrt.anchoredPosition = Vector2.zero;

        // 标题栏
        var tb = NewImage(prt, "TitleBar", TitleBarBg);
        var tbrt = tb.rectTransform;
        tbrt.anchorMin = new Vector2(0, 1); tbrt.anchorMax = new Vector2(1, 1);
        tbrt.pivot = new Vector2(0.5f, 1f);
        tbrt.sizeDelta = new Vector2(0, 76);
        tbrt.anchoredPosition = new Vector2(0, -2);

        // Logo
        var logoTex = Resources.Load<Texture2D>("Branding/WMJ_LOGO");
        if (logoTex != null)
        {
            var logoGo = new GameObject("Logo");
            logoGo.transform.SetParent(tbrt, false);
            var li = logoGo.AddComponent<RawImage>();
            li.texture = logoTex;
            li.raycastTarget = false;
            var lrt = li.rectTransform;
            lrt.anchorMin = new Vector2(0, 0.5f); lrt.anchorMax = new Vector2(0, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            float h = 56f;
            float w = h * ((float)logoTex.width / logoTex.height);
            lrt.sizeDelta = new Vector2(w, h);
            lrt.anchoredPosition = new Vector2(20, 0);
        }

        // 标题文字（左侧）
        var titleStack = new GameObject("TitleStack");
        titleStack.transform.SetParent(tbrt, false);
        var tsrt = titleStack.AddComponent<RectTransform>();
        tsrt.anchorMin = new Vector2(0, 0); tsrt.anchorMax = new Vector2(1, 1);
        tsrt.pivot = new Vector2(0, 0.5f);
        tsrt.offsetMin = new Vector2(120, 0); tsrt.offsetMax = new Vector2(-20, 0);
        var vlg = titleStack.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlHeight = vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
        vlg.spacing = 2;
        vlg.padding = new RectOffset(0, 0, 12, 12);

        var t1 = NewText(tsrt, "Main", "WMJ 自定义客户端", 22, FontStyles.Bold, TextWhite, TextAlignmentOptions.MidlineLeft);
        var t2 = NewText(tsrt, "Sub",  subtitle,           13, FontStyles.Normal, HintColor, TextAlignmentOptions.MidlineLeft);
        t1.GetComponent<LayoutElement>().preferredHeight = 26;
        t2.GetComponent<LayoutElement>().preferredHeight = 16;

        // 标题栏分隔线
        var div = NewImage(prt, "TitleDivider", Divider);
        var drt = div.rectTransform;
        drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.sizeDelta = new Vector2(-20, 1);
        drt.anchoredPosition = new Vector2(0, -78);

        // 内容区
        var content = NewImage(prt, "Content", ContentBg);
        var crt = content.rectTransform;
        crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 1);
        crt.offsetMin = new Vector2(8, 50); crt.offsetMax = new Vector2(-8, -82);

        // 帮助栏
        var help = NewImage(prt, "HelpBar", TitleBarBg);
        var hrt = help.rectTransform;
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.sizeDelta = new Vector2(0, 42);
        hrt.anchoredPosition = Vector2.zero;

        // 帮助栏顶部分隔线
        var hd = NewImage(prt, "HelpDivider", Divider);
        var hdrt = hd.rectTransform;
        hdrt.anchorMin = new Vector2(0, 0); hdrt.anchorMax = new Vector2(1, 0);
        hdrt.pivot = new Vector2(0.5f, 0f);
        hdrt.sizeDelta = new Vector2(-20, 1);
        hdrt.anchoredPosition = new Vector2(0, 42);

        return (panelGo, crt, hrt);
    }

    // ═══════════════════ 提示页 ═══════════════════
    private void BuildPromptPage()
    {
        var (panelGo, content, helpBar) = BuildPanelShell(SLOGAN, new Vector2(960, 580));
        promptPage = panelGo;

        // 信息行
        float y = -16;
        const float rowH = 44;
        y -= AddRow(content, y, rowH, "当前版本",
            string.IsNullOrEmpty(localManifest?.version) ? "(本地无缓存)" : localManifest.version,
            RowEven, accentVal: false);
        y -= AddRow(content, y, rowH, "最新版本",
            remoteManifest.version, RowOdd, accentVal: true);
        y -= AddRow(content, y, rowH, "构建时间",
            remoteManifest.build_time ?? "—", RowEven, accentVal: false);
        y -= AddRow(content, y, rowH, "平台",
            remoteManifest.platform ?? platform, RowOdd, accentVal: false);
        y -= AddRow(content, y, rowH, "需更新文件",
            $"<b>{needDownload.Count}</b> 个", RowEven, accentVal: true);
        y -= AddRow(content, y, rowH, "下载体积",
            $"<b>{FormatBytes(totalNeedBytes)}</b>", RowOdd, accentVal: true);

        // 描述区
        y -= 12;
        var hint = NewText(content, "Hint",
            "更新将直接在客户端内完成下载，自动校验 SHA-256 并原子替换。\n" +
            "完成后客户端会自动重启。",
            14, FontStyles.Normal, HintColor, TextAlignmentOptions.TopLeft);
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.sizeDelta = new Vector2(-40, 60);
        hrt.anchoredPosition = new Vector2(0, y);
        Destroy(hint.GetComponent<LayoutElement>());

        // 按钮栏
        var btnRow = new GameObject("BtnRow");
        btnRow.transform.SetParent(content, false);
        var brrt = btnRow.AddComponent<RectTransform>();
        brrt.anchorMin = new Vector2(0, 0); brrt.anchorMax = new Vector2(1, 0);
        brrt.pivot = new Vector2(0.5f, 0f);
        brrt.sizeDelta = new Vector2(-32, 56);
        brrt.anchoredPosition = new Vector2(0, 14);
        var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlHeight = hlg.childControlWidth = false;

        AddButton(brrt, "禁用自动检测  [D]", BtnReset,  220, 44, OnDisableAutoCheck);
        AddButton(brrt, "跳过本次  [S]",     BtnClose,  160, 44, OnSkip);
        AddButton(brrt, "立即更新  [U]",     BtnSave,   180, 44, OnUpdate);

        // 帮助栏文字
        var helpTxt = NewText(helpBar, "T",
            "[U] 立即更新     [S] 跳过本次     [D] 禁用自动检测     [Esc] 跳过",
            13, FontStyles.Normal, HintColor, TextAlignmentOptions.Center);
        var htrt = helpTxt.rectTransform;
        htrt.anchorMin = Vector2.zero; htrt.anchorMax = Vector2.one;
        htrt.offsetMin = htrt.offsetMax = Vector2.zero;
        Destroy(helpTxt.GetComponent<LayoutElement>());
    }

    // ═══════════════════ 进度页（全屏轮播 + 底部进度横条） ═══════════════════
    private void BuildProgressPage()
    {
        if (promptPage != null) Destroy(promptPage);

        progressPage = new GameObject("ProgressPage");
        progressPage.transform.SetParent(canvas.transform, false);
        Stretch(progressPage.AddComponent<RectTransform>());

        // ── 顶部强调线 (2px) ──
        var topLine = NewImage(progressPage.transform, "TopAccent", Accent);
        var tlrt = topLine.rectTransform;
        tlrt.anchorMin = new Vector2(0, 1); tlrt.anchorMax = new Vector2(1, 1);
        tlrt.pivot = new Vector2(0.5f, 1f); tlrt.sizeDelta = new Vector2(0, 2);

        // ── 标题栏 (72px) ──
        var tb = NewImage(progressPage.transform, "TitleBar", TitleBarBg);
        var tbrt = tb.rectTransform;
        tbrt.anchorMin = new Vector2(0, 1); tbrt.anchorMax = new Vector2(1, 1);
        tbrt.pivot = new Vector2(0.5f, 1f);
        tbrt.sizeDelta = new Vector2(0, 72); tbrt.anchoredPosition = new Vector2(0, -2);

        var logoTex = Resources.Load<Texture2D>("Branding/WMJ_LOGO");
        if (logoTex != null)
        {
            var logoGo = new GameObject("Logo");
            logoGo.transform.SetParent(tbrt, false);
            var li = logoGo.AddComponent<RawImage>();
            li.texture = logoTex; li.raycastTarget = false;
            var lrt = li.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            float lh = 48, lw = lh * ((float)logoTex.width / logoTex.height);
            lrt.sizeDelta = new Vector2(lw, lh);
            lrt.anchoredPosition = new Vector2(16, 0);
        }

        var tStack = new GameObject("TitleStack");
        tStack.transform.SetParent(tbrt, false);
        var tsrt = tStack.AddComponent<RectTransform>();
        tsrt.anchorMin = Vector2.zero; tsrt.anchorMax = Vector2.one;
        tsrt.offsetMin = new Vector2(110, 0); tsrt.offsetMax = new Vector2(-16, 0);
        var tvl = tStack.AddComponent<VerticalLayoutGroup>();
        tvl.childAlignment = TextAnchor.MiddleLeft;
        tvl.childControlHeight = tvl.childControlWidth = true;
        tvl.childForceExpandHeight = false; tvl.childForceExpandWidth = true;
        tvl.spacing = 2; tvl.padding = new RectOffset(0, 0, 10, 10);
        var tm = NewText(tsrt, "M", "WMJ 自定义客户端", 20, FontStyles.Bold, TextWhite, TextAlignmentOptions.MidlineLeft);
        var tsu = NewText(tsrt, "S", $"正在更新到 {remoteManifest.version}", 12, FontStyles.Normal, HintColor, TextAlignmentOptions.MidlineLeft);
        tm.GetComponent<LayoutElement>().preferredHeight = 24;
        tsu.GetComponent<LayoutElement>().preferredHeight = 16;

        // ── 舞台区（轮播相册，top:74 → bottom:120） ──
        var stage = new GameObject("Stage");
        stage.transform.SetParent(progressPage.transform, false);
        var srt = stage.AddComponent<RectTransform>();
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(0, 120);  // dlbar(88) + helpbar(32)
        srt.offsetMax = new Vector2(0, -74);  // topline(2) + titlebar(72)
        stage.AddComponent<UnityEngine.UI.RectMask2D>();

        var stageBg = NewImage(srt, "Bg", new Color(0.02f, 0.03f, 0.06f, 1f));
        Stretch(stageBg.rectTransform);

        // 双缓冲相册图层（RawImage + CanvasGroup 实现交叉淡入淡出）
        var goA = new GameObject("AlbumA");
        goA.transform.SetParent(srt, false);
        albumImgA = goA.AddComponent<RawImage>();
        albumImgA.color = Color.white; albumImgA.raycastTarget = false;
        Stretch(albumImgA.rectTransform);
        albumGrpA = goA.AddComponent<CanvasGroup>(); albumGrpA.alpha = 0;

        var goB = new GameObject("AlbumB");
        goB.transform.SetParent(srt, false);
        albumImgB = goB.AddComponent<RawImage>();
        albumImgB.color = Color.white; albumImgB.raycastTarget = false;
        Stretch(albumImgB.rectTransform);
        albumGrpB = goB.AddComponent<CanvasGroup>(); albumGrpB.alpha = 0;

        // 相册编号（左下角）
        albumCounterTxt = NewText(srt, "Counter", "", 13, FontStyles.Normal,
            new Color(1, 1, 1, 0.7f), TextAlignmentOptions.BottomLeft);
        var acrt = albumCounterTxt.rectTransform;
        acrt.anchorMin = acrt.anchorMax = Vector2.zero;
        acrt.pivot = Vector2.zero;
        acrt.sizeDelta = new Vector2(260, 32);
        acrt.anchoredPosition = new Vector2(16, 8);
        Destroy(albumCounterTxt.GetComponent<LayoutElement>());

        // 底部渐变遮罩（舞台→进度条过渡）
        var grad = NewImage(srt, "BottomGrad", new Color(0.04f, 0.05f, 0.10f, 0.60f));
        var grt = grad.rectTransform;
        grt.anchorMin = new Vector2(0, 0); grt.anchorMax = new Vector2(1, 0);
        grt.pivot = new Vector2(0.5f, 0); grt.sizeDelta = new Vector2(0, 60);

        // ── 下载进度横条 (88px，距底 32px) ──
        var dlbar = NewImage(progressPage.transform, "DlBar", new Color(0.04f, 0.05f, 0.10f, 0.94f));
        var dlrt = dlbar.rectTransform;
        dlrt.anchorMin = new Vector2(0, 0); dlrt.anchorMax = new Vector2(1, 0);
        dlrt.pivot = new Vector2(0.5f, 0);
        dlrt.sizeDelta = new Vector2(0, 88); dlrt.anchoredPosition = new Vector2(0, 32);

        // 进度细线 (4px，dlbar 顶部)
        var ptrack = NewImage(dlrt, "Track", SliderTrack);
        var pkrt = ptrack.rectTransform;
        pkrt.anchorMin = new Vector2(0, 1); pkrt.anchorMax = new Vector2(1, 1);
        pkrt.pivot = new Vector2(0.5f, 1); pkrt.sizeDelta = new Vector2(0, 4);

        progressFill = NewImage(pkrt.GetComponent<RectTransform>(), "Fill", SliderFill);
        var frt = progressFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(0, 1);
        frt.pivot = new Vector2(0, 0.5f); frt.sizeDelta = Vector2.zero;

        // 横条内容：左=状态 | 中=百分比 | 右=统计+速率+取消
        tCurrentFile = NewText(dlrt, "State", "准备下载...", 14, FontStyles.Bold, TextWhite, TextAlignmentOptions.MidlineLeft);
        var cfrt = tCurrentFile.rectTransform;
        cfrt.anchorMin = new Vector2(0, 0); cfrt.anchorMax = new Vector2(0.28f, 1);
        cfrt.offsetMin = new Vector2(20, 8); cfrt.offsetMax = new Vector2(0, -12);
        Destroy(tCurrentFile.GetComponent<LayoutElement>());

        tProgPctTxt = NewText(dlrt, "Pct", "0.0%", 34, FontStyles.Bold, TextWhite, TextAlignmentOptions.Center);
        var pprt = tProgPctTxt.rectTransform;
        pprt.anchorMin = new Vector2(0.28f, 0); pprt.anchorMax = new Vector2(0.48f, 1);
        pprt.offsetMin = new Vector2(0, 4); pprt.offsetMax = new Vector2(0, -8);
        Destroy(tProgPctTxt.GetComponent<LayoutElement>());

        tStats = NewText(dlrt, "Stats", "0 MB / 0 MB", 13, FontStyles.Normal, HintColor, TextAlignmentOptions.MidlineLeft);
        var ssrt = tStats.rectTransform;
        ssrt.anchorMin = new Vector2(0.48f, 0.5f); ssrt.anchorMax = new Vector2(0.72f, 1);
        ssrt.offsetMin = new Vector2(8, 4); ssrt.offsetMax = new Vector2(0, -8);
        Destroy(tStats.GetComponent<LayoutElement>());

        tSpeed = NewText(dlrt, "Speed", "— MB/s", 13, FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        var sprt = tSpeed.rectTransform;
        sprt.anchorMin = new Vector2(0.72f, 0.5f); sprt.anchorMax = new Vector2(0.88f, 1);
        sprt.offsetMin = new Vector2(8, 4); sprt.offsetMax = new Vector2(0, -8);
        Destroy(tSpeed.GetComponent<LayoutElement>());

        cancelBtn = AddButton(dlrt, "取消", BtnClose, 72, 44, OnCancelDownload);
        var cbrt = cancelBtn.GetComponent<RectTransform>();
        cbrt.anchorMin = new Vector2(1, 0.5f); cbrt.anchorMax = new Vector2(1, 0.5f);
        cbrt.pivot = new Vector2(1, 0.5f);
        cbrt.sizeDelta = new Vector2(72, 44);
        cbrt.anchoredPosition = new Vector2(-16, 0);

        // ── 帮助栏 (32px，最底部) ──
        var helpBar = NewImage(progressPage.transform, "HelpBar", TitleBarBg);
        var hbrt = helpBar.rectTransform;
        hbrt.anchorMin = new Vector2(0, 0); hbrt.anchorMax = new Vector2(1, 0);
        hbrt.pivot = new Vector2(0.5f, 0); hbrt.sizeDelta = new Vector2(0, 32);

        var helpTxt = NewText(hbrt, "T", $"WMJ · RoboMaster 2026     {SLOGAN}",
            12, FontStyles.Normal, HintColor, TextAlignmentOptions.Center);
        Stretch(helpTxt.rectTransform);
        Destroy(helpTxt.GetComponent<LayoutElement>());
    }

    // ═══════════════════ 相册轮播 ═══════════════════
    private IEnumerator LoadAlbumCoroutine()
    {
        albumTextures = new Texture2D[ALBUM_COUNT];
        bool started = false;
        for (int i = 0; i < ALBUM_COUNT; i++)
        {
            string url = ALBUM_URL + $"wmj_{(i + 1):D2}.jpg";
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    albumTextures[i] = DownloadHandlerTexture.GetContent(req);
                    if (!started && albumImgA != null)
                    {
                        started = true;
                        albumRoutine = StartCoroutine(CarouselCoroutine());
                    }
                }
            }
        }
    }

    private IEnumerator CarouselCoroutine()
    {
        while (albumTextures == null) yield return null;
        int idx = -1;
        for (int i = 0; i < ALBUM_COUNT; i++)
            if (albumTextures[i] != null) { idx = i; break; }
        if (idx < 0) yield break;

        albumIndex = idx;
        var front = albumImgA;  var fGrp = albumGrpA;
        var back  = albumImgB;  var bGrp = albumGrpB;

        front.texture = albumTextures[idx];
        front.rectTransform.localScale = Vector3.one;
        fGrp.alpha = 1; bGrp.alpha = 0;
        UpdateAlbumUI();

        while (true)
        {
            // 显示阶段：Ken Burns 缩放
            float display = ALBUM_INTERVAL - ALBUM_FADE;
            float t = 0;
            while (t < display)
            {
                t += Time.unscaledDeltaTime;
                front.rectTransform.localScale = Vector3.one * (1f + 0.06f * (t / ALBUM_INTERVAL));
                yield return null;
            }

            // 寻找下一张有效照片
            int next = albumIndex;
            for (int i = 1; i <= ALBUM_COUNT; i++)
            {
                int c = (albumIndex + i) % ALBUM_COUNT;
                if (albumTextures[c] != null) { next = c; break; }
            }

            // 交叉淡入淡出
            back.texture = albumTextures[next];
            back.rectTransform.localScale = Vector3.one;
            bGrp.alpha = 0;
            t = 0;
            while (t < ALBUM_FADE)
            {
                t += Time.unscaledDeltaTime;
                float p = t / ALBUM_FADE;
                float smooth = p * p * (3f - 2f * p); // smoothstep
                fGrp.alpha = 1f - smooth;
                bGrp.alpha = smooth;
                front.rectTransform.localScale = Vector3.one * (1f + 0.06f * ((display + t) / ALBUM_INTERVAL));
                back.rectTransform.localScale = Vector3.one * (1f + 0.06f * (t / ALBUM_INTERVAL));
                yield return null;
            }

            // 交换图层
            albumIndex = next;
            var tmpI = front; front = back; back = tmpI;
            var tmpG = fGrp; fGrp = bGrp; bGrp = tmpG;
            fGrp.alpha = 1; bGrp.alpha = 0;
            UpdateAlbumUI();
        }
    }

    private void UpdateAlbumUI()
    {
        if (albumCounterTxt != null)
            albumCounterTxt.text = $"<size=18><b>{(albumIndex + 1):D2}</b></size>" +
                $"<color=#ffffffaa> / {ALBUM_COUNT:D2}   WMJ 战队精选</color>";
    }

    private void CleanupAlbum()
    {
        if (albumRoutine != null) { StopCoroutine(albumRoutine); albumRoutine = null; }
        if (albumTextures != null)
            for (int i = 0; i < albumTextures.Length; i++)
                if (albumTextures[i] != null) { Destroy(albumTextures[i]); albumTextures[i] = null; }
    }

    void OnDestroy()
    {
        CleanupAlbum();
        if (ownedEventSystem != null)
        {
            Destroy(ownedEventSystem);
            ownedEventSystem = null;
        }
        // 回滚我们对现有 EventSystem 做的改动（禁用的旧模块恢复、移除我们添加的新模块）
        if (attachedInputModule != null)
        {
            Destroy(attachedInputModule);
            attachedInputModule = null;
        }
        for (int i = 0; i < disabledInputModules.Count; i++)
        {
            if (disabledInputModules[i] != null) disabledInputModules[i].enabled = true;
        }
        disabledInputModules.Clear();
        attachedEventSystem = null;
    }

    // ═══════════════════ 按键 ═══════════════════
    void Update()
    {
        if (canvas == null) return;
        if (!downloading)
        {
            if (Input.GetKeyDown(KeyCode.U)) OnUpdate();
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Escape)) OnSkip();
            else if (Input.GetKeyDown(KeyCode.D)) OnDisableAutoCheck();
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Escape)) OnCancelDownload();
        }
    }

    // ═══════════════════ 动作 ═══════════════════
    private void OnSkip()
    {
        Debug.Log("[WMJ-Update] 用户跳过本次");
        Completed = true;
        Destroy(gameObject);
    }

    private void OnDisableAutoCheck()
    {
        if (downloading) return;
        Debug.Log("[WMJ-Update] 用户选择禁用自动检测");
        try
        {
            File.WriteAllText(Path.Combine(installDir, DISABLE_FLAG),
                $"WMJ 客户端自动更新已禁用\n创建时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                "删除此文件即可重新启用\n");
        }
        catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] 写 flag 失败: {ex.Message}"); }
        Completed = true;
        Destroy(gameObject);
    }

    private void OnUpdate()
    {
        if (downloading) return;
        downloading = true;
        BuildProgressPage();
        StartCoroutine(LoadAlbumCoroutine());
        StartCoroutine(DownloadRoutine());
    }

    private void OnCancelDownload()
    {
        if (cancelled) return;
        cancelled = true;
        if (cancelBtn != null) cancelBtn.interactable = false;
        if (tCurrentFile != null) tCurrentFile.text = "正在取消，请稍候...";
        Debug.Log("[WMJ-Update] 用户取消下载");
    }

    // ═══════════════════ 下载主流程 ═══════════════════
    private IEnumerator DownloadRoutine()
    {
        string staging = Path.Combine(installDir, STAGING_DIR);
        try { Directory.CreateDirectory(staging); }
        catch (Exception ex)
        {
            ShowFatal($"无法创建暂存目录: {ex.Message}");
            yield break;
        }

        long doneBytes = 0;
        int  doneCount = 0;
        int  totalCount = needDownload.Count;
        var  failed = new List<ManifestFile>();
        var  startTime = DateTime.Now;

        // 速率窗口
        long lastBytes = 0;
        var  lastTick  = DateTime.Now;

        for (int i = 0; i < needDownload.Count; i++)
        {
            if (cancelled) break;
            var entry = needDownload[i];
            string targetStaged = Path.Combine(staging, entry.path);
            try { Directory.CreateDirectory(Path.GetDirectoryName(targetStaged)); }
            catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] mkdir 失败 {entry.path}: {ex.Message}"); failed.Add(entry); continue; }

            // 已存在且 SHA 匹配 → 跳过（断点续传场景）
            if (File.Exists(targetStaged) && new FileInfo(targetStaged).Length == entry.size && Sha256(targetStaged) == entry.sha256)
            {
                doneBytes += entry.size; doneCount++;
                UpdateProgress(doneBytes, doneCount, totalCount, entry.path, "已缓存");
                continue;
            }

            tCurrentFile.text = $"<color=#5BB8FA>▸</color> {entry.path}";
            UpdateProgress(doneBytes, doneCount, totalCount, entry.path, "下载中");

            string url = filesBase + Uri.EscapeUriString(entry.path);
            string tmpPath = targetStaged + ".tmp.download";
            // 删除残留 tmp（避免 SHA 错乱）
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerFile(tmpPath);
                req.timeout = 0;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (cancelled) { req.Abort(); break; }

                    // 单文件内进度估算
                    long curFileBytes = (long)(req.downloadedBytes);
                    long shownBytes = doneBytes + Math.Min(curFileBytes, entry.size);
                    UpdateProgress(shownBytes, doneCount, totalCount, entry.path, "下载中");

                    // 速率（每 ~0.5s 更新）
                    var now = DateTime.Now;
                    var dt = (now - lastTick).TotalSeconds;
                    if (dt >= 0.5)
                    {
                        long delta = shownBytes - lastBytes;
                        double bps = delta / dt;
                        tSpeed.text = FormatBytes((long)bps) + "/s";
                        lastBytes = shownBytes;
                        lastTick = now;
                    }

                    yield return null;
                }

                if (cancelled) break;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[WMJ-Update] 下载失败 {entry.path}: {req.error}");
                    failed.Add(entry);
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    continue;
                }
            }

            // SHA 校验
            string actual;
            try { actual = Sha256(tmpPath); }
            catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] SHA 计算失败 {entry.path}: {ex.Message}"); failed.Add(entry); continue; }

            if (actual != entry.sha256)
            {
                Debug.LogWarning($"[WMJ-Update] SHA 不匹配 {entry.path}: got={actual} want={entry.sha256}");
                failed.Add(entry);
                try { File.Delete(tmpPath); } catch { }
                continue;
            }

            // 重命名为最终名
            try
            {
                if (File.Exists(targetStaged)) File.Delete(targetStaged);
                File.Move(tmpPath, targetStaged);
            }
            catch (Exception ex) { Debug.LogWarning($"[WMJ-Update] 重命名失败 {entry.path}: {ex.Message}"); failed.Add(entry); continue; }

            doneBytes += entry.size; doneCount++;
            UpdateProgress(doneBytes, doneCount, totalCount, entry.path, "完成");
        }

        if (cancelled)
        {
            tCurrentFile.text = "已取消，下次启动可继续。";
            tSpeed.text = "—";
            yield return new WaitForSecondsRealtime(0.8f);
            Completed = true;
            Application.Quit();
            yield break;
        }

        // 失败重试一轮
        if (failed.Count > 0)
        {
            tCurrentFile.text = $"<color=#EFE580>正在重试 {failed.Count} 个失败文件...</color>";
            yield return new WaitForSecondsRealtime(1.0f);
            var retry = new List<ManifestFile>(failed);
            failed.Clear();
            foreach (var entry in retry)
            {
                if (cancelled) break;
                string targetStaged = Path.Combine(staging, entry.path);
                string url = filesBase + Uri.EscapeUriString(entry.path);
                string tmpPath = targetStaged + ".tmp.download";
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                tCurrentFile.text = $"<color=#EFE580>↻</color> {entry.path}";
                using (var req = UnityWebRequest.Get(url))
                {
                    req.downloadHandler = new DownloadHandlerFile(tmpPath);
                    req.timeout = 0;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { failed.Add(entry); continue; }
                }
                string actual;
                try { actual = Sha256(tmpPath); } catch { failed.Add(entry); continue; }
                if (actual != entry.sha256) { failed.Add(entry); continue; }
                try
                {
                    if (File.Exists(targetStaged)) File.Delete(targetStaged);
                    File.Move(tmpPath, targetStaged);
                }
                catch { failed.Add(entry); continue; }
                doneBytes += entry.size; doneCount++;
                UpdateProgress(doneBytes, doneCount, totalCount, entry.path, "完成");
            }
        }

        if (failed.Count > 0)
        {
            ShowFatal($"有 {failed.Count} 个文件下载失败，请稍后重试。\n首项: {failed[0].path}");
            yield break;
        }

        // 先以 .pending 名义写入 staging，current 名请 swap 脚本在所有其他文件替换完毕后原子重命名
        try { File.WriteAllText(Path.Combine(staging, LOCAL_MANIFEST_PENDING), remoteManifestRaw, new UTF8Encoding(false)); }
        catch (Exception ex) { ShowFatal($"写 {LOCAL_MANIFEST_PENDING} 失败: {ex.Message}"); yield break; }

        tCurrentFile.text = $"<color=#6ACB7E>✓ 下载完成</color>   {FormatBytes(totalNeedBytes)}   用时 {(DateTime.Now - startTime).TotalSeconds:F1}s";
        tSpeed.text = "准备替换并重启...";

        yield return new WaitForSecondsRealtime(1.0f);

        // 写 swap 脚本并启动
        try { LaunchSwapAndRestart(staging); }
        catch (Exception ex) { ShowFatal($"启动替换脚本失败: {ex.Message}"); yield break; }

        yield return new WaitForSecondsRealtime(0.4f);
        Completed = true;
        Application.Quit();
    }

    private void UpdateProgress(long doneBytes, int doneCount, int totalCount, string curRel, string status)
    {
        if (progressFill == null) return;
        float pct = totalNeedBytes > 0 ? Mathf.Clamp01((float)doneBytes / totalNeedBytes) : 1f;
        progressFill.rectTransform.anchorMax = new Vector2(pct, 1);
        tProgPctTxt.text = $"{pct * 100:F1}%";
        tStats.text = $"{FormatBytes(doneBytes)} / {FormatBytes(totalNeedBytes)}   ·   {doneCount}/{totalCount} 个文件";
    }

    private void ShowFatal(string msg)
    {
        if (tCurrentFile != null) tCurrentFile.text = $"<color=#E86B63>❌ {msg}</color>";
        if (tSpeed != null) tSpeed.text = "失败";
        if (cancelBtn != null)
        {
            // 改成"关闭"
            var lbl = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = "关闭  [Esc]";
            cancelBtn.onClick.RemoveAllListeners();
            cancelBtn.onClick.AddListener(() =>
            {
                Completed = true;
                if (canvas != null) Destroy(canvas.gameObject);
                Destroy(gameObject);
            });
            cancelBtn.interactable = true;
        }
        downloading = false;
        Debug.LogError("[WMJ-Update] " + msg);
    }

    // ═══════════════════ Swap 脚本 ═══════════════════
    private void LaunchSwapAndRestart(string staging)
    {
        bool isWin = Application.platform == RuntimePlatform.WindowsPlayer;
        string scriptPath;
        ProcessStartInfo psi;

        if (isWin)
        {
            scriptPath = Path.Combine(installDir, ".wmj_swap.bat");
            string content =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                "setlocal EnableDelayedExpansion\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"cd /d \"{installDir}\"\r\n" +
                $"set LOG=\"{installDir}\\.wmj_swap.log\"\r\n" +
                "echo [WMJ-Swap] %DATE% %TIME% 开始替换 >%LOG%\r\n" +
                // 第 1 步：先排除 pending 文件同步其他所有文件（robocopy 的 /XF 可排除）
                $"robocopy \"{STAGING_DIR}\" . /E /MOVE /NFL /NDL /NJH /NJS /NC /NS /NP /XF \"{LOCAL_MANIFEST_PENDING}\" >>%LOG%\r\n" +
                "set RCERR=%errorlevel%\r\n" +
                // robocopy 退出码 <8 均视为成功；>=8 视为失败，不提交 manifest，启动旧版让用户下次重试
                "if %RCERR% GEQ 8 ( echo [WMJ-Swap] robocopy 失败 errorlevel=%RCERR% 跳过 manifest 提交 >>%LOG% & goto :restart )\r\n" +
                // 第 2 步：所有其他文件已落地，最后原子重命名 manifest——此步做到才算更新成功
                $"if exist \"{STAGING_DIR}\\{LOCAL_MANIFEST_PENDING}\" (\r\n" +
                $"  move /Y \"{STAGING_DIR}\\{LOCAL_MANIFEST_PENDING}\" \"{LOCAL_MANIFEST}\" >>%LOG%\r\n" +
                "  if errorlevel 1 ( echo [WMJ-Swap] manifest 提交失败 >>%LOG% ) else ( echo [WMJ-Swap] manifest 已提交 >>%LOG% )\r\n" +
                $") else ( echo [WMJ-Swap] 警告: 未找到 {LOCAL_MANIFEST_PENDING} >>%LOG% )\r\n" +
                $"rmdir /s /q \"{STAGING_DIR}\" 2>nul\r\n" +
                ":restart\r\n" +
                "echo [WMJ-Swap] 重新启动客户端... >>%LOG%\r\n" +
                "set ROBOMASTER_SKIP_UPDATE=1\r\n" +
                "if exist launch.bat ( call launch.bat ) else ( start \"\" RoboMasterClient.exe )\r\n";
            File.WriteAllText(scriptPath, content, new UTF8Encoding(false));
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = installDir,
            };
        }
        else
        {
            scriptPath = Path.Combine(installDir, ".wmj_swap.sh");
            // ⚠️ 不使用 `find | while` 管道——它的 while 体在子 shell 中，`exit` 无法终止主脚本，
            //    任意 mv 失败会被静默放过然后依然提交 manifest，造成"看似更新成功实则缺文件"。
            //    改用 process substitution `< <(find ...)` + ok 标志，仅在全部成功后才提交 manifest 并重启。
            //    另外显式给 RoboMasterClient / launch.sh 打 +x（DownloadHandlerFile 写出的文件默认无执行位）。
            string sb =
                "#!/bin/bash\n" +
                "set -u\n" +
                $"LOG=\"{installDir}/.wmj_swap.log\"\n" +
                "exec >>\"$LOG\" 2>&1\n" +
                "echo \"[WMJ-Swap] $(date -Iseconds) 启动交换脚本\"\n" +
                "sleep 2\n" +
                $"cd \"{installDir}\" || {{ echo '[WMJ-Swap] cd installDir 失败'; exit 1; }}\n" +
                "ok=1\n" +
                "moved=0\n" +
                // process substitution 让 while 运行在主 shell，ok=0 能真正中断后续提交步骤
                "while IFS= read -r -d '' f; do\n" +
                "  rel=\"${f#./}\"\n" +
                $"  if [ \"$rel\" = \"{LOCAL_MANIFEST_PENDING}\" ]; then continue; fi\n" +
                "  dir=\"$(dirname \"$rel\")\"\n" +
                "  if ! mkdir -p \"$dir\"; then\n" +
                "    echo \"[WMJ-Swap] mkdir 失败: $dir\"; ok=0; break\n" +
                "  fi\n" +
                $"  if ! mv -f \"{STAGING_DIR}/$rel\" \"$rel\"; then\n" +
                "    echo \"[WMJ-Swap] mv 失败: $rel\"; ok=0; break\n" +
                "  fi\n" +
                "  moved=$((moved+1))\n" +
                $"done < <(cd \"{STAGING_DIR}\" && find . -type f -print0)\n" +
                "echo \"[WMJ-Swap] 移动文件数=$moved ok=$ok\"\n" +
                // 无论 ok 如何，尝试给主执行档补上执行位（下载出来默认没有 +x，否则双击启动不起来）
                "for bin in RoboMasterClient launch.sh; do\n" +
                "  if [ -f \"$bin\" ]; then chmod +x \"$bin\" 2>/dev/null || true; fi\n" +
                "done\n" +
                // 仅当所有文件都成功落位时才提交 manifest 并清理 staging
                "if [ \"$ok\" = \"1\" ]; then\n" +
                $"  if [ -f \"{STAGING_DIR}/{LOCAL_MANIFEST_PENDING}\" ]; then\n" +
                $"    if mv -f \"{STAGING_DIR}/{LOCAL_MANIFEST_PENDING}\" \"{LOCAL_MANIFEST}\"; then\n" +
                "      echo \"[WMJ-Swap] manifest 已提交\"\n" +
                "    else\n" +
                "      echo \"[WMJ-Swap] manifest 提交失败\"; ok=0\n" +
                "    fi\n" +
                "  else\n" +
                $"    echo \"[WMJ-Swap] 警告: 未找到 {LOCAL_MANIFEST_PENDING}\"; ok=0\n" +
                "  fi\n" +
                $"  rm -rf \"{STAGING_DIR}\"\n" +
                "else\n" +
                $"  echo \"[WMJ-Swap] 保留 {STAGING_DIR}，下次启动可断点续传\"\n" +
                "fi\n" +
                "echo \"[WMJ-Swap] 重新启动客户端... (ok=$ok)\"\n" +
                "export ROBOMASTER_SKIP_UPDATE=1\n" +
                "if [ -x ./launch.sh ]; then\n" +
                "  exec ./launch.sh\n" +
                "elif [ -x ./RoboMasterClient ]; then\n" +
                "  exec ./RoboMasterClient\n" +
                "else\n" +
                "  echo \"[WMJ-Swap] 警告: 找不到可执行的启动入口\"\n" +
                "fi\n";
            File.WriteAllText(scriptPath, sb, new UTF8Encoding(false));
            try
            {
                var p = new Process();
                p.StartInfo.FileName = "/bin/chmod";
                p.StartInfo.Arguments = $"+x \"{scriptPath}\"";
                p.StartInfo.UseShellExecute = false;
                p.Start(); p.WaitForExit(2000);
            }
            catch { }

            // setsid 让脚本脱离 Unity 进程组，Unity 退出不影响它
            psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/setsid",
                Arguments = $"/bin/bash \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // 兜底：找不到 setsid 时直接 nohup
            if (!File.Exists("/usr/bin/setsid"))
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"-c \"nohup /bin/bash '{scriptPath}' >/dev/null 2>&1 &\"";
            }
        }

        Process.Start(psi);
        Debug.Log($"[WMJ-Update] ▸ swap 脚本已派发: {scriptPath}");
    }

    // ═══════════════════ 工具 ═══════════════════
    private static string Sha256(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var fs = File.OpenRead(path))
        {
            var hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private static string FormatBytes(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024f).ToString("F1") + " KB";
        if (b < 1073741824) return (b / 1048576f).ToString("F1") + " MB";
        return (b / 1073741824f).ToString("F2") + " GB";
    }

    // ═══════════════════ UI helpers ═══════════════════
    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        var cf = UI.Core.UIFactory.CachedFont;
        if (cf != null) t.font = cf;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        return t;
    }

    private float AddRow(RectTransform parent, float y, float h, string label, string value, Color bg, bool accentVal)
    {
        var img = NewImage(parent, "Row_" + label, bg);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-40, h);
        rt.anchoredPosition = new Vector2(0, y);

        var l = NewText(rt, "L", label, 14, FontStyles.Normal, HintColor, TextAlignmentOptions.MidlineLeft);
        var lrt = l.rectTransform;
        lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1);
        lrt.pivot = new Vector2(0, 0.5f);
        lrt.sizeDelta = new Vector2(180, 0);
        lrt.anchoredPosition = new Vector2(18, 0);
        Destroy(l.GetComponent<LayoutElement>());

        var v = NewText(rt, "V", value, 15, FontStyles.Bold, accentVal ? Accent : TextWhite, TextAlignmentOptions.MidlineLeft);
        var vrt = v.rectTransform;
        vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(1, 1);
        vrt.pivot = new Vector2(0, 0.5f);
        vrt.offsetMin = new Vector2(210, 0); vrt.offsetMax = new Vector2(-18, 0);
        Destroy(v.GetComponent<LayoutElement>());

        return h + 4;
    }

    private Button AddButton(Transform parent, string label, Color bg, float w, float h, Action onClick)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h;
        le.minWidth = w; le.minHeight = h;

        var img = go.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick?.Invoke());

        var t = NewText(go.transform, "L", label, 14, FontStyles.Bold, TextWhite, TextAlignmentOptions.Center);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        Destroy(t.GetComponent<LayoutElement>());

        return btn;
    }
}
