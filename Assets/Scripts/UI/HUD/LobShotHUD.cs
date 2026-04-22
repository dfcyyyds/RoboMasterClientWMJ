using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using UI.ViewModels;
using Framework.Network;
using Framework.Video;
using System.Buffers;
using System.Diagnostics;
using System.Collections;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace UI.HUD
{
    /// <summary>
    /// 吊射模式 HUD v3 — H.264 彩色视觉画面 + 超分辨率重建 + 美术边框 + 准心 + 敌方基地血条
    /// 独立 Canvas, 仅在吊射模式激活时显示
    /// 
    /// v3 超分辨率升级：
    ///   - 1024×512 H.264 解码 → 直接显示（v3.2.1 原生分辨率，默认不用 SR）
    ///   - Unity Sentis GPU 推理，LobShot-SRGAN v3 模型（仅支持 360×540，需重训）
    ///   - 全局残差学习：输出 = Bicubic(LR) + 网络高频残差
    ///   - SR 失败自动回退到 Bilinear 放大
    /// 
    /// 布局：
    ///   顶部：敌方基地血条（含护盾）+ 血量数值
    ///   中央：竖屏 2:3 画面 + 美术边框 + 准心叠加
    /// </summary>
    public class LobShotHUD : MonoBehaviour
    {
        // ─── UI 元素 ───
        private Canvas lobCanvas;
        private CanvasGroup canvasGroup;

        // 美术边框
        private Image borderTop, borderBottom, borderLeft, borderRight;
        private Image cornerTL, cornerTR, cornerBL, cornerBR;

        // 画面显示
        private RawImage visionDisplay;
        private Texture2D displayTex;

        // v2 H.264 参数 (1024×512 RGB24, 横屏 2:1, v3.2.1 原生采集分辨率)
        private const int V2_TEX_W = 1024, V2_TEX_H = 512;
        private const int V2_BYTES_PER_PIXEL = 3; // RGB24

        // SR 输出尺寸 (保留字段，v3.2.1 默认不启用 SR)
        private const int SR_TEX_W = 2048, SR_TEX_H = 1024;

        // v1 二值化参数 (192×144 1bit) — 向后兼容
        private const int V1_TEX_W = 192, V1_TEX_H = 144;

        // 显示尺寸(屏幕像素) — 横屏 2:1 比例，占中居中显示
        private const int DISPLAY_W = 1024, DISPLAY_H = 512;

        // 基地血条
        private RectTransform baseBarRoot;
        private Image baseBarBg, baseBarFill, baseBarShield;
        private TextMeshProUGUI baseHpText, baseTitleText;

        // ─── v2 H.264 管线 ───
        private LobShotH264Transport h264Transport;
        private FfmpegPipeDecoder h264Decoder;
        private byte[] v2PixelBuf; // RGB24 → RGBA32 展开缓冲
        private bool isV2Mode;     // 当前是否使用 v2 H.264 管线
        private int v2FrameCount;  // v2 已解码帧计数

        // ─── 超分辨率 (SR) ───
        // v3.2.1 起默认禁用：1024×512 已是原始采集分辨率，旧 SRGAN 模型仅针对 360×540 竖屏训练，
        // 不适用于横屏。若需重启 SR，需重训模型并修改 SR_TEX_W/H 。
        private LobShotSuperResolution srModule;
        private bool srEnabled = false;    // SR 开关（v3.2.1 默认关闭）
        private bool stretchToFullHD = false; // 是否拉伸显示（v3.2.1 默认关闭，1024×512 已足够大）
        private Texture2D decodeTex;       // 解码纹理 1024×512 (直接显示 / SR 输入)
        private RectTransform visionDisplayRt;
        private float lastSrInferTime;
        private const float SR_INFER_MIN_INTERVAL = 1f / 15f; // SR 最多 15FPS，避免 GPU 过载卡死

        // ─── v1 二值化兼容管线 ───
        private byte[] v1FrameBuf;   // 1bit 帧缓冲 (3456B)
        private byte[] v1PixelBuf;   // RGBA32 展开
        private byte[] v1ThumbBuf;   // 渐进 I 帧缩略帧
        private ushort v1LastPart1FrameId;

        // v1 帧类型常量
        private const byte FT_I_PART1 = 0x01;
        private const byte FT_I_PART2 = 0x02;
        private const byte FT_I_SINGLE = 0x03;
        private const byte FT_H264_STREAM = 0x04;
        private const byte FT_D_FRAME = 0x10;
        private const byte FT_D_EMPTY = 0x11;
        private const byte FT_TRAIL = 0x20;
        private const byte FT_TEXT_MSG = 0x30;
        private const byte FT_ARMOR_TARGETS = 0x40;
        private const byte FT_RADAR_MARK = 0x51;
        private const byte FT_CMD_REQUEST_I = 0xF0;
        private const byte FT_CMD_SET_PARAM = 0xF1;

        // ─── 弹道拖影（v3.2.1）───
        // 客户端可通过 TrailEnabled 属性操作：
        //  1) 本地立即隐藏/显示轨迹叠加层
        //  2) 上行发送 CMD_SET_PARAM(0xF1) param_id=0x05 到发送端，禁用后发送端不再打包 0x20 TRAIL 帧
        private bool trailEnabled = true;
        private const byte PARAM_ID_TRAIL_ENABLE = 0x05;
        private const byte PARAM_ID_I_INTERVAL = 0x01;
        private const byte PARAM_ID_TRAIL_BUFLEN = 0x02;
        private const byte PARAM_ID_BIN_THRESHOLD = 0x03;
        private const byte PARAM_ID_ROI_ENABLE = 0x04;
        private const byte PARAM_ID_VIDEO_FPS = 0x06;
        private const byte PARAM_ID_VIDEO_BITRATE = 0x07;
        private const byte PARAM_ID_VIDEO_RESOLUTION = 0x08;
        private const byte PARAM_ID_IDR_FORCE = 0x09;
        private const byte PARAM_ID_TRAIL_COLOR = 0x0A;
        private const string TRAIL_CTRL_TOPIC = "CustomControl"; // 上行控制频道
        private const byte FT_HEARTBEAT = 0xFE;

        // TRAIL 叠加层（v3.3.0）—— 解析 TRAIL_FRAME(0x20) 并在 visionDisplay 上绘点
        private const int TRAIL_MAX_POINTS = 64;
        private const float TRAIL_DOT_SIZE = 6f;
        private const float TRAIL_DOT_SIZE_MIN = 3f;
        private static readonly Color TRAIL_DOT_COLOR = new Color(1f, 0.85f, 0.2f, 0.95f);
        private RectTransform trailRoot;
        private Image[] trailDots;
        private int trailActiveCount;

        // v1 块常量
        private const int V1_THUMB_W = 96, V1_THUMB_H = 72;
        private const int V1_BLOCKS_X = 24, V1_BLOCKS_Y = 18;

        // ─── 准心叠加 (v2 用 UI Image 实现) ───
        private Image crosshairH, crosshairV, crosshairDot;

        // ─── 线程安全帧队列（零分配：使用 ArrayPool 缓冲） ───
        private struct PooledBuffer
        {
            public byte[] Data;
            public int Length;
        }
        private readonly System.Collections.Concurrent.ConcurrentQueue<PooledBuffer> pendingFrames
            = new System.Collections.Concurrent.ConcurrentQueue<PooledBuffer>();

        // ─── 数据 ───
        private GlobalUnitStatusViewModel unitVM;
        private bool isShowing;
        private int frameCount;
        private TextMeshProUGUI waitingHintText; // 兼容字段（入场 HUD 现已替代其职责，保留防止外部引用）

        // ─── 入场 HUD（Shift→首帧显示期间的进度卡片，风格与 SettingsPanel 一致） ───
        private CanvasGroup enterOverlayGroup;
        private GameObject enterOverlayGO;
        private TextMeshProUGUI enterTitleText;
        private TextMeshProUGUI enterFooterText;
        private RectTransform enterProgressFillRt;
        private Image enterProgressFill;
        private Image[] enterStageBadges;
        private TextMeshProUGUI[] enterStageLabels;
        private static readonly string[] EnterStageTitles = {
            "发送部署指令",
            "启动图传解码器",
            "等待机甲进入部署模式",
            "接收首帧图传",
        };
        private const int ENTER_STAGE_COUNT = 4;
        private int enterStageIndex;           // 当前活动阶段（0..4；4=全部完成）
        private float enterStageStartTime;     // 当前阶段开始时间
        private bool enterDecoderReady;        // 由 StartH264Decoder 完成后置位
        private float enterHideStartTime = -1f;// >=0 表示正在淡出
        private const float ENTER_FADE_OUT_SEC = 0.35f;

        // ─── 入场 HUD 配色（与 SettingsPanel 保持一致） ───
        private static readonly Color ENTER_PANEL_BG    = new Color(0.04f, 0.05f, 0.10f, 0.94f);
        private static readonly Color ENTER_TITLE_BG    = new Color(0.03f, 0.04f, 0.08f, 0.95f);
        private static readonly Color ENTER_ACCENT      = new Color(0.35f, 0.72f, 0.98f, 1f);
        private static readonly Color ENTER_TRACK_BG    = new Color(0.08f, 0.08f, 0.16f, 0.95f);
        private static readonly Color ENTER_FILL        = new Color(0.22f, 0.55f, 0.95f, 0.85f);
        private static readonly Color ENTER_BADGE_DONE  = new Color(0.22f, 0.80f, 0.55f, 0.95f);
        private static readonly Color ENTER_BADGE_ACTIVE= new Color(0.35f, 0.72f, 0.98f, 0.95f);
        private static readonly Color ENTER_BADGE_IDLE  = new Color(0.18f, 0.22f, 0.32f, 0.75f);
        private static readonly Color ENTER_TEXT_ACTIVE = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color ENTER_TEXT_IDLE   = new Color(0.55f, 0.62f, 0.75f, 0.85f);
        private static readonly Color ENTER_TEXT_DONE   = new Color(0.70f, 0.85f, 0.78f, 0.95f);


        // ─── 诊断 ───
        private int diagDecodeReject;
        private int diagDecodeSuccess;
        private float lastDiagLogTime;
        private const float DIAG_LOG_INTERVAL = 3f;

        // ─── GPU 压力动态监控 ───
        private enum GpuPressureLevel
        {
            Normal = 0,    // 正常：每帧 SR 推理
            Light = 1,     // 轻压力：隔帧 SR 推理
            Heavy = 2,     // 重压力：仅双线性插值，不做 SR
            Critical = 3   // 危急：跳过帧解码，最低功耗
        }
        private GpuPressureLevel gpuPressure = GpuPressureLevel.Normal;
        private const int FRAME_TIME_WINDOW = 30;  // 滑动窗口采样帧数
        private float[] recentFrameTimes = new float[FRAME_TIME_WINDOW];
        private int frameTimeIndex;
        private float avgFrameTimeMs;
        private int srSkipCounter;  // Light 模式下隔帧计数
        private float showStartTime;
        private const float SR_WARMUP_DELAY_SEC = 3f; // 进入吊射后延迟 SR，避免首帧冷启动卡死

        // ─── 多级熔断器（替代简单 srAutoDisabled） ───
        private int circuitBreakerLevel;              // 0=正常, 1-5=逐级严格
        private int consecutiveFuseTriggers;          // 连续触发熔断次数
        private float circuitBreakerRecoverSec = 10f; // 当前恢复等待时间（指数退避）
        private float circuitBreakerFuseTime;         // 最近一次触发熔断的时刻
        private const float CB_BACKOFF_FACTOR = 2f;   // 指数退避倍率
        private const float CB_MAX_RECOVER_SEC = 80f; // 最大恢复等待时间
        private const int CB_PERMANENT_THRESHOLD = 5; // 永久熔断触发阈值
        private bool circuitBreakerPermanent;         // 是否已永久熔断

        // ─── 帧率保护：检测到持续卡顿时自动降级 ───
        private float lastUpdateTime;
        private int slowFrameCount;          // 连续慢帧计数（Update 耗时 > 50ms）
        private const int SLOW_FRAME_FUSE = 5;  // 连续 5 帧慢则触发保护

        // ─── 解码管线拥塞保护（SR熔断触发器） ───
        private int consecutiveBudgetExceedFrames;      // 连续超出主线程预算帧数
        private int consecutivePipelineBacklogFrames;   // 连续管线积压帧数
        private const int BUDGET_EXCEED_FUSE = 8;       // 连续8帧超预算触发SR熔断
        private const int PIPELINE_BACKLOG_FUSE = 15;   // 连续15帧积压触发SR熔断
        private const int TRANSPORT_BACKLOG_THRESHOLD = 8; // H264Transport输出队列阈值
        private const int DECODER_BACKLOG_THRESHOLD = 3;   // FFmpeg解码队列阈值

        // ─── 主线程时间预算诊断 ───
        private readonly Stopwatch updateStopwatch = new Stopwatch();
        private float worstUpdateMs;       // 最近诊断周期内最慢的一帧
        private string worstUpdatePhase;   // 最慢帧是卡在哪个阶段
        private int totalFreezeFrames;     // 累计超 100ms 的帧数
        // 防卡逻辑：主线程时间预算耗尽后跳过后续步骤
        private const float UPDATE_BUDGET_MS = 12f; // 每帧允许的最大处理时间

        // ─── 颜色常量 ───
        private static readonly Color BORDER_COLOR = new Color(0.08f, 0.72f, 0.55f, 0.85f);
        private static readonly Color BORDER_GLOW = new Color(0.05f, 0.95f, 0.70f, 0.30f);
        private static readonly Color BASE_HP_COLOR = new Color(0.9f, 0.15f, 0.1f, 1f);
        private static readonly Color BASE_SHIELD_COLOR = new Color(0.2f, 0.6f, 1f, 0.6f);
        private static readonly Color CROSSHAIR_COLOR = new Color(0f, 1f, 0.4f, 0.9f);

        // ═══════════════════════════════ 生命周期 ═══════════════════════════════

        // 单例引用，供 LobShotUdpReceiver 等外部模块在收到队友吊射 UDP 数据时唤醒 HUD
        public static LobShotHUD Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                wmj.Log.W("[LobShotHUD] 检测到多实例，销毁后来者", wmj.Log.Tag.UI);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Initialize()
        {
            // 兜底：如果 Awake 因初始化顺序未触发，仍确保 Instance 可用
            if (Instance == null) Instance = this;

            ReloadDisplayConfigFromGameParams();

            unitVM = new GlobalUnitStatusViewModel();
            unitVM.Initialize();

            h264Transport = new LobShotH264Transport();

            BuildCanvas();
            BuildBorder();
            BuildVisionDisplay();
            BuildCrosshairOverlay();
            BuildTrailOverlay();
            BuildBaseHealthBar();
            BuildEnterOverlay();

            // 订阅 CustomByteBlock 数据
            ProtobufManager.Instance.OnDataUpdated += OnProtoDataUpdated;

            // ─── 预初始化 SR 模块（不再同步 GPU 预热，改用逐帧异步协程）───
            // SR Worker 将在整个 LobShotHUD 生命周期内持久存在，不会随 Show/Hide 销毁重建。
            if (srEnabled && stretchToFullHD)
            {
                srModule = new LobShotSuperResolution(V2_TEX_W, V2_TEX_H);
                if (!srModule.IsReady)
                {
                    wmj.Log.W("[LobShotHUD] SR 模块加载失败，将在首次进入吊射模式时重试", wmj.Log.Tag.UI);
                    srModule.Dispose();
                    srModule = null;
                }
                else
                {
                    // 启动异步逐帧预热：将 GPU Shader JIT 分摊到多帧，
                    // 消除之前 Schedule() 触发的 GPU Sync 导致主线程冻结 ~24 秒的问题。
                    StartCoroutine(SRWarmupCoroutine());
                }
            }

            Hide();
        }

        public void Shutdown()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnProtoDataUpdated;
            unitVM?.Dispose();
            // 显式释放持久 SR 模块（不在 DisposeDecoder 中销毁，仅在 Shutdown 时清理）
            if (srModule != null)
            {
                srModule.Dispose();
                srModule = null;
                wmj.Log.I("[LobShotHUD] SR 模块已释放（应用退出）", wmj.Log.Tag.UI);
            }
            DisposeDecoder();
            if (decodeTex != null) Destroy(decodeTex);
            if (displayTex != null) Destroy(displayTex);
            if (lobCanvas != null) Destroy(lobCanvas.gameObject);
        }

        /// <summary>启动 H.264 解码器 + 超分辨率模块（进入吊射模式时）</summary>
        public void StartH264Decoder()
        {
            if (h264Decoder != null) return;

            // ─── H.264 解码器 (1024×512, v3.2.1) ───
            // 吊射使用软件解码：比赛模式下码率仅 ≈100kbps，1024×512@10fps CPU 解码负担极低（<5%）
            // 避免与主图传争抢 NVDEC 硬件解码器 session（消费级 GPU 仅 2-3 个 session 上限）
            h264Decoder = new FfmpegPipeDecoder(
                inputCodec: "h264",
                useRawVideo: true,
                outputWidth: V2_TEX_W,
                outputHeight: V2_TEX_H,
                verboseFrameLogs: false,
                enableStderrLog: false,
                useHardwareDecode: false,
                forceHevc: false
            );
            h264Decoder.MaxQueueSize = 4;

            wmj.Log.I($"[LobShotHUD] H.264 解码器已启动 ({V2_TEX_W}×{V2_TEX_H}, 彩色, v3.2.1)", wmj.Log.Tag.UI);

            // ─── 超分辨率模块 ───
            // SR 模块在 Initialize() 中已预创建（持久存在），通常无需重建。
            // 仅在以下两种情况下重新创建：
            //   1. srEnabled 从 false 切换到 true（用户中途启用）
            //   2. 前一次初始化失败（srModule == null）
            if (srEnabled && stretchToFullHD && srModule == null)
            {
                srModule = new LobShotSuperResolution(V2_TEX_W, V2_TEX_H);
                if (!srModule.IsReady)
                {
                    wmj.Log.W("[LobShotHUD] SR 模块加载失败, 本次回退到 Bilinear 放大 (下次 Show 将重试)", wmj.Log.Tag.UI);
                    srModule.Dispose();
                    srModule = null;
                    // 不设置 srEnabled = false，允许后续 Show/Hide 周期重新尝试
                }
                else
                {
                    wmj.Log.I($"[LobShotHUD] SR 模块就绪 ({V2_TEX_W}×{V2_TEX_H} → {SR_TEX_W}×{SR_TEX_H})", wmj.Log.Tag.UI);
                }
            }
            else if (srModule != null && srModule.IsReady)
            {
                wmj.Log.I($"[LobShotHUD] SR 模块复用已预热实例 ({V2_TEX_W}×{V2_TEX_H} → {SR_TEX_W}×{SR_TEX_H})", wmj.Log.Tag.UI);
            }

            UpdateV2DisplayLayout();
            enterDecoderReady = true;
        }

        /// <summary>释放解码器资源（退出吊射模式时）。SR 模块持久，不在此释放。</summary>
        public void DisposeDecoder()
        {
            // SR 模块持久存在（已在 Initialize() 中创建，在 Shutdown() 时释放），
            // 此处仅重置 visionDisplay 纹理引用，防止悬挂到已停止更新的 SR 输出
            if (visionDisplay != null && decodeTex != null)
                visionDisplay.texture = decodeTex;

            if (h264Decoder != null)
            {
                // 在后台线程执行 Dispose 以避免 Task.Wait 阻塞主线程
                var decoder = h264Decoder;
                h264Decoder = null;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { decoder.Dispose(); }
                    catch (System.Exception ex)
                    {
                        wmj.Log.W($"[LobShotHUD] 解码器后台释放异常: {ex.Message}", wmj.Log.Tag.UI);
                    }
                });
                wmj.Log.I("[LobShotHUD] H.264 解码器已提交后台释放", wmj.Log.Tag.UI);
            }
        }

        /// <summary>获取 H.264 传输层引用（供 LobShotUdpReceiver 使用）</summary>
        public LobShotH264Transport GetH264Transport() => h264Transport;

        // ═══════════════════════════════ 显隐 ═══════════════════════════════

        public void Show()
        {
            isShowing = true;
            frameCount = 0;
            v2FrameCount = 0;
            isV2Mode = false; // 首帧到达后自动判断
            diagDecodeReject = 0;
            diagDecodeSuccess = 0;
            lastDiagLogTime = Time.realtimeSinceStartup;

            h264Transport?.Reset();
            lastSrInferTime = 0f;
            StartH264Decoder();
            showStartTime = Time.realtimeSinceStartup;

            // 仿真模式：将 H264Transport 注册到 UdpReceiver，使 UDP 数据直接走 H.264 管线
            LobShotUdpReceiver.ActiveH264Transport = h264Transport;

            // 完全暂停主图传纹理上传，释放 GPU 上传带宽给吊射 SR
            VideoStreamService.Instance?.PauseTextureUpload();
            // ⚠️ 不设置 maxQueuedFrames = 1：该值强制 CPU 在每帧结束时等待 GPU 完成
            // 与 NVDEC + SR Compute 着色器 + 正常渲染叠加后会导致 fps≈1.5，严重影响体验
            // 保持 Unity 默认值 2，允许 GPU 比 CPU 超前 1 帧，大幅降低 CPU 等待开销

            // 重置 GPU 压力监控及帧率保护计数
            // lastUpdateTime 置 0：清除上次退出吊射时的旧时间戳
            // 若保留旧值，第一帧 deltaMs≈4000ms，10 帧后 avgFrameTime 飙升触发 gpuPressure=Critical
            // 导致 ApplyDecodedFrame 直接 return，跳过所有 SR/纹理处理，画面静止
            lastUpdateTime = 0f;
            gpuPressure = GpuPressureLevel.Normal;
            circuitBreakerLevel = 0;
            srSkipCounter = 0;
            slowFrameCount = 0;
            consecutiveBudgetExceedFrames = 0;
            consecutivePipelineBacklogFrames = 0;
            frameTimeIndex = 0;
            for (int i = 0; i < recentFrameTimes.Length; i++) recentFrameTimes[i] = 0f;

            if (lobCanvas != null) lobCanvas.gameObject.SetActive(true);
            ShowEnterOverlay();
            wmj.Log.I($"[LobShotHUD] 显示吊射画面 (v2 彩色 H.264, 拉伸={stretchToFullHD}, SR={srEnabled})", wmj.Log.Tag.UI);
        }

        public void Hide()
        {
            isShowing = false;
            HideEnterOverlayImmediate();
            LobShotUdpReceiver.ActiveH264Transport = null;
            DisposeDecoder();

            // 恢复主图传纹理上传
            VideoStreamService.Instance?.ResumeTextureUpload();

            // 排空残留的池化缓冲，归还 ArrayPool
            while (pendingFrames.TryDequeue(out var buf))
                ArrayPool<byte>.Shared.Return(buf.Data);

            HideAllTrailDots();
            if (lobCanvas != null) lobCanvas.gameObject.SetActive(false);
            wmj.Log.I($"[LobShotHUD] 隐藏吊射画面 (共接收 {frameCount} 帧, v2解码 {v2FrameCount} 帧, 严重卡帧 {totalFreezeFrames} 次)", wmj.Log.Tag.UI);
        }

        // ═══════════════════════════════ SR 异步逐帧预热 ═══════════════════════════════

        /// <summary>
        /// 利用 ScheduleIterable 将 SR 模型的 GPU Compute Shader JIT 编译
        /// 分摊到多帧，避免单帧 GPU Sync 导致主线程冻结 ~24 秒。
        ///
        /// 原理：worker.ScheduleIterable() 一次调度一个模型层的计算工作，
        ///   每帧 yield return null 后进入下一层。这样 GPU JIT 工作被
        ///   分散到多个帧，每帧的 GPU Sync 开销极小，主线程不会阻塞。
        /// </summary>
        private IEnumerator SRWarmupCoroutine()
        {
            // 延迟两帧再开始，确保当前帧不受影响
            yield return null;
            yield return null;

            if (srModule == null || !srModule.IsReady)
            {
                wmj.Log.W("[LobShotHUD] SR 预热协程启动时 srModule 不可用，跳过", wmj.Log.Tag.UI);
                yield break;
            }

            wmj.Log.I("[LobShotHUD] SR 开始异步逐帧预热 (ScheduleIterable)...", wmj.Log.Tag.UI);
            var sw = Stopwatch.StartNew();
            int stepCount = 0;

            IEnumerator warmupIter = srModule.GetWarmupEnumerator();
            while (warmupIter.MoveNext())
            {
                stepCount++;

                // ─── 吊射激活期间暂停预热 ───
                // 每层 GPU Shader JIT 编译约 353ms，在吊射激活时执行会导致当帧卡顿影响操作
                // 等待退出吊射后再继续，第一次进入吊射使用 Bilinear 兜底，预热完成后自动启用 SR
                while (isShowing)
                    yield return null;

                yield return null; // 非吊射帧：执行一层 JIT 编译后让出主线程
            }

            srModule.MarkWarmedUp();
            sw.Stop();
            wmj.Log.I($"[LobShotHUD] SR 异步预热完成，共 {stepCount} 层，" +
                $"总耗时 {sw.ElapsedMilliseconds}ms（仅统计非吊射期间的帧）", wmj.Log.Tag.UI);
        }

        // ═══════════════════════════════ 构建 UI ═══════════════════════════════

        private void BuildCanvas()
        {
            lobCanvas = UIFactory.CreateCanvas("LobShotCanvas", 10500);
            lobCanvas.transform.SetParent(transform, false);

            var cg = lobCanvas.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            canvasGroup = cg;
        }

        private void BuildBorder()
        {
            var root = lobCanvas.transform;
            float bw = 4f;

            // 全屏不透明黑色背景
            var tint = UIFactory.CreateImage(root, "VisionTint",
                new Color(0f, 0f, 0f, 1f));
            var tintRt = tint.rectTransform;
            tintRt.anchorMin = Vector2.zero;
            tintRt.anchorMax = Vector2.one;
            tintRt.offsetMin = Vector2.zero;
            tintRt.offsetMax = Vector2.zero;

            float cx = 0.5f, cy = 0.48f;
            float hw = DISPLAY_W / 1920f / 2f;
            float hh = DISPLAY_H / 1080f / 2f;
            float marginPx = 6f;
            float mw = marginPx / 1920f, mh = marginPx / 1080f;

            borderTop = CreateBorderStrip(root, "BorderTop",
                cx - hw - mw, cy + hh, cx + hw + mw, cy + hh + bw / 1080f, BORDER_COLOR);
            borderBottom = CreateBorderStrip(root, "BorderBot",
                cx - hw - mw, cy - hh - bw / 1080f, cx + hw + mw, cy - hh, BORDER_COLOR);
            borderLeft = CreateBorderStrip(root, "BorderL",
                cx - hw - mw, cy - hh, cx - hw - mw + bw / 1920f, cy + hh, BORDER_COLOR);
            borderRight = CreateBorderStrip(root, "BorderR",
                cx + hw + mw - bw / 1920f, cy - hh, cx + hw + mw, cy + hh, BORDER_COLOR);

            float cs = 12f;
            cornerTL = CreateCornerDot(root, "CornerTL", cx - hw - mw, cy + hh, cs, BORDER_GLOW);
            cornerTR = CreateCornerDot(root, "CornerTR", cx + hw + mw, cy + hh, cs, BORDER_GLOW);
            cornerBL = CreateCornerDot(root, "CornerBL", cx - hw - mw, cy - hh, cs, BORDER_GLOW);
            cornerBR = CreateCornerDot(root, "CornerBR", cx + hw + mw, cy - hh, cs, BORDER_GLOW);
        }

        private void BuildVisionDisplay()
        {
            var root = lobCanvas.transform;

            // ─── 解码纹理 (1024×512 RGBA32, v3.2.1 原生采集分辨率) ───
            decodeTex = new Texture2D(V2_TEX_W, V2_TEX_H, TextureFormat.RGBA32, false);
            decodeTex.filterMode = FilterMode.Bilinear;
            decodeTex.wrapMode = TextureWrapMode.Clamp;

            // 初始化纯黑
            var initPixels = new byte[V2_TEX_W * V2_TEX_H * 4];
            for (int i = 0; i < initPixels.Length; i += 4)
            {
                initPixels[i + 3] = 255; // alpha
            }
            decodeTex.SetPixelData(initPixels, 0);
            decodeTex.Apply(false);

            // displayTex 由 SwitchToV1Texture 按需创建 (v1 二值模式用)
            // v2 模式直接使用 decodeTex 或 srModule.OutputTexture

            // RGBA 展开缓冲
            v2PixelBuf = new byte[V2_TEX_W * V2_TEX_H * 4];

            // v1 兼容缓冲
            v1FrameBuf = new byte[V1_TEX_W * V1_TEX_H / 8];
            v1PixelBuf = new byte[V1_TEX_W * V1_TEX_H * 4];
            v1ThumbBuf = new byte[V1_THUMB_W * V1_THUMB_H / 8];

            // RawImage 居中显示（竖屏 2:3 比例）
            var go = new GameObject("VisionDisplay");
            go.transform.SetParent(root, false);
            visionDisplay = go.AddComponent<RawImage>();
            visionDisplay.texture = decodeTex;
            visionDisplay.color = Color.white;

            var rt = go.GetComponent<RectTransform>();
            visionDisplayRt = rt;
            rt.anchorMin = new Vector2(0.5f, 0.48f);
            rt.anchorMax = new Vector2(0.5f, 0.48f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(DISPLAY_W, DISPLAY_H);
            UpdateV2DisplayLayout();

            // 旧版"等待数据..."提示由 EnterOverlay 取代（见 BuildEnterOverlay）
        }

        // ═══════════════════════════════ 入场 HUD（进度卡片） ═══════════════════════════════

        /// <summary>构建"进入吊射模式"进度卡片，风格与 SettingsPanel 一致</summary>
        private void BuildEnterOverlay()
        {
            var root = lobCanvas.transform;

            // 容器（居中固定尺寸）
            enterOverlayGO = new GameObject("EnterOverlay");
            enterOverlayGO.transform.SetParent(root, false);
            var rootRt = enterOverlayGO.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(460f, 300f);
            rootRt.anchoredPosition = Vector2.zero;
            enterOverlayGroup = enterOverlayGO.AddComponent<CanvasGroup>();
            enterOverlayGroup.alpha = 0f;
            enterOverlayGroup.interactable = false;
            enterOverlayGroup.blocksRaycasts = false;

            // 面板底色
            var bg = UIFactory.CreateImage(enterOverlayGO.transform, "Bg", ENTER_PANEL_BG);
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            // 顶部 AccentStrip（3px 蓝色条）
            var accent = UIFactory.CreateImage(enterOverlayGO.transform, "AccentStrip", ENTER_ACCENT);
            var accRt = accent.rectTransform;
            accRt.anchorMin = new Vector2(0f, 1f);
            accRt.anchorMax = new Vector2(1f, 1f);
            accRt.pivot = new Vector2(0.5f, 1f);
            accRt.sizeDelta = new Vector2(0f, 3f);
            accRt.anchoredPosition = Vector2.zero;

            // 顶部标题栏（深色）
            var titleBar = UIFactory.CreateImage(enterOverlayGO.transform, "TitleBar", ENTER_TITLE_BG);
            var tbRt = titleBar.rectTransform;
            tbRt.anchorMin = new Vector2(0f, 1f);
            tbRt.anchorMax = new Vector2(1f, 1f);
            tbRt.pivot = new Vector2(0.5f, 1f);
            tbRt.sizeDelta = new Vector2(0f, 46f);
            tbRt.anchoredPosition = new Vector2(0f, -3f);

            enterTitleText = UIFactory.CreateText(titleBar.transform, "Title",
                "进入吊射模式", 20, TextAlignmentOptions.Left, ENTER_TEXT_ACTIVE, FontStyles.Bold);
            var titleRt = enterTitleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(18f, 0f);
            titleRt.offsetMax = new Vector2(-18f, 0f);

            // 阶段列表
            enterStageBadges = new Image[ENTER_STAGE_COUNT];
            enterStageLabels = new TextMeshProUGUI[ENTER_STAGE_COUNT];
            float rowH = 34f;
            float listTop = -60f; // 距离容器顶部
            for (int i = 0; i < ENTER_STAGE_COUNT; i++)
            {
                var rowGO = new GameObject($"Stage{i}");
                rowGO.transform.SetParent(enterOverlayGO.transform, false);
                var rowRt = rowGO.AddComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, rowH);
                rowRt.anchoredPosition = new Vector2(0f, listTop - i * rowH);

                // 序号徽标（圆角方块）
                var badge = UIFactory.CreateImage(rowGO.transform, "Badge", ENTER_BADGE_IDLE);
                var bRt = badge.rectTransform;
                bRt.anchorMin = new Vector2(0f, 0.5f);
                bRt.anchorMax = new Vector2(0f, 0.5f);
                bRt.pivot = new Vector2(0f, 0.5f);
                bRt.sizeDelta = new Vector2(20f, 20f);
                bRt.anchoredPosition = new Vector2(24f, 0f);
                enterStageBadges[i] = badge;

                // 阶段文字
                var label = UIFactory.CreateText(rowGO.transform, "Label",
                    $"{i + 1}. {EnterStageTitles[i]}", 16, TextAlignmentOptions.Left,
                    ENTER_TEXT_IDLE, FontStyles.Normal);
                var lRt = label.rectTransform;
                lRt.anchorMin = new Vector2(0f, 0f);
                lRt.anchorMax = new Vector2(1f, 1f);
                lRt.offsetMin = new Vector2(56f, 0f);
                lRt.offsetMax = new Vector2(-24f, 0f);
                enterStageLabels[i] = label;
            }

            // 进度条容器
            float barY = listTop - ENTER_STAGE_COUNT * rowH - 18f;
            var track = UIFactory.CreateImage(enterOverlayGO.transform, "ProgressTrack", ENTER_TRACK_BG);
            var tRt = track.rectTransform;
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.sizeDelta = new Vector2(-48f, 4f);
            tRt.anchoredPosition = new Vector2(0f, barY);

            enterProgressFill = UIFactory.CreateImage(track.transform, "ProgressFill", ENTER_FILL);
            enterProgressFillRt = enterProgressFill.rectTransform;
            enterProgressFillRt.anchorMin = new Vector2(0f, 0f);
            enterProgressFillRt.anchorMax = new Vector2(0f, 1f);
            enterProgressFillRt.pivot = new Vector2(0f, 0.5f);
            enterProgressFillRt.sizeDelta = new Vector2(0f, 0f);
            enterProgressFillRt.anchoredPosition = Vector2.zero;

            // 底部脚注
            enterFooterText = UIFactory.CreateText(enterOverlayGO.transform, "Footer",
                "按 Shift 再次可取消 · 预计 5–8 秒", 13,
                TextAlignmentOptions.Center, ENTER_TEXT_IDLE, FontStyles.Normal);
            var fRt = enterFooterText.rectTransform;
            fRt.anchorMin = new Vector2(0f, 0f);
            fRt.anchorMax = new Vector2(1f, 0f);
            fRt.pivot = new Vector2(0.5f, 0f);
            fRt.sizeDelta = new Vector2(0f, 22f);
            fRt.anchoredPosition = new Vector2(0f, 14f);

            enterOverlayGO.SetActive(false);
        }

        /// <summary>开始入场流程：重置状态并显示卡片</summary>
        private void ShowEnterOverlay()
        {
            if (enterOverlayGO == null) return;
            enterStageIndex = 0;
            enterStageStartTime = Time.unscaledTime;
            enterDecoderReady = false;
            enterHideStartTime = -1f;
            if (enterProgressFillRt != null) enterProgressFillRt.sizeDelta = new Vector2(0f, 0f);
            for (int i = 0; i < ENTER_STAGE_COUNT; i++)
            {
                if (enterStageBadges[i] != null) enterStageBadges[i].color = ENTER_BADGE_IDLE;
                if (enterStageLabels[i] != null) enterStageLabels[i].color = ENTER_TEXT_IDLE;
            }
            if (enterTitleText != null) enterTitleText.text = "进入吊射模式";
            if (enterFooterText != null)
            {
                enterFooterText.text = "按 Shift 再次可取消 · 预计 5–8 秒";
                enterFooterText.color = ENTER_TEXT_IDLE;
            }
            if (enterOverlayGroup != null) enterOverlayGroup.alpha = 1f;
            enterOverlayGO.SetActive(true);
        }

        /// <summary>隐藏时无条件关闭（Hide() 调用）</summary>
        private void HideEnterOverlayImmediate()
        {
            if (enterOverlayGO != null) enterOverlayGO.SetActive(false);
            if (enterOverlayGroup != null) enterOverlayGroup.alpha = 0f;
            enterHideStartTime = -1f;
        }

        /// <summary>首包协议数据到达（CustomByteBlock 路径触发）</summary>
        private void OnEnterOverlayDataReceived()
        {
            // 阶段 2 "等待机甲进入部署模式" 视作完成，Tick 会推进
        }

        /// <summary>每帧更新入场 HUD 状态（由 Update 驱动）</summary>
        private void TickEnterOverlay()
        {
            if (enterOverlayGO == null || !enterOverlayGO.activeSelf) return;

            // ─── 淡出阶段 ───
            if (enterHideStartTime >= 0f)
            {
                float t = (Time.unscaledTime - enterHideStartTime) / ENTER_FADE_OUT_SEC;
                if (t >= 1f) { HideEnterOverlayImmediate(); return; }
                if (enterOverlayGroup != null) enterOverlayGroup.alpha = 1f - t;
                return;
            }

            // ─── 推进阶段 ───
            bool dataArrived = (h264Transport != null && h264Transport.TotalPacketsReceived > 0) || frameCount > 0;
            bool firstFrameDisplayed = v2FrameCount > 0 || frameCount > 0;

            // 阶段 0 → 1：指令发送的视觉确认时间
            if (enterStageIndex == 0 && Time.unscaledTime - enterStageStartTime >= 0.35f)
                AdvanceEnterStage();
            // 阶段 1 → 2：解码器启动完成
            if (enterStageIndex == 1 && enterDecoderReady && Time.unscaledTime - enterStageStartTime >= 0.25f)
                AdvanceEnterStage();
            // 阶段 2 → 3：第一包数据到达
            if (enterStageIndex == 2 && dataArrived)
                AdvanceEnterStage();
            // 阶段 3 → 完成：首帧已显示
            if (enterStageIndex == 3 && firstFrameDisplayed)
            {
                AdvanceEnterStage();
                // 标题切到"已建立"，短暂停留后淡出
                if (enterTitleText != null) enterTitleText.text = "吊射图传已建立";
                if (enterFooterText != null)
                {
                    enterFooterText.text = "连接完成，进入战斗视角…";
                    enterFooterText.color = ENTER_TEXT_DONE;
                }
                enterHideStartTime = Time.unscaledTime + 0.4f - ENTER_FADE_OUT_SEC; // 0.4s 停留 + 淡出
            }

            // ─── 刷新视觉 ───
            RefreshEnterOverlayVisuals();

            // ─── 保护：长时间卡在某阶段时提示 ───
            if (enterStageIndex < ENTER_STAGE_COUNT && enterFooterText != null)
            {
                float stuck = Time.unscaledTime - enterStageStartTime;
                if (enterStageIndex == 2 && stuck > 6f)
                    enterFooterText.text = "等待时间较长：请确认英雄已就位并启动吊射";
                else if (enterStageIndex == 3 && stuck > 4f)
                    enterFooterText.text = "解码器等待首帧…若持续无画面请检查网络";
            }
        }

        private void AdvanceEnterStage()
        {
            if (enterStageIndex >= ENTER_STAGE_COUNT) return;
            enterStageIndex++;
            enterStageStartTime = Time.unscaledTime;
        }

        private void RefreshEnterOverlayVisuals()
        {
            // 徽标 / 文字颜色
            for (int i = 0; i < ENTER_STAGE_COUNT; i++)
            {
                if (enterStageBadges[i] == null || enterStageLabels[i] == null) continue;
                if (i < enterStageIndex)
                {
                    enterStageBadges[i].color = ENTER_BADGE_DONE;
                    enterStageLabels[i].color = ENTER_TEXT_DONE;
                }
                else if (i == enterStageIndex)
                {
                    // 活动阶段：脉冲
                    float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.2f));
                    var c = ENTER_BADGE_ACTIVE; c.a = pulse;
                    enterStageBadges[i].color = c;
                    enterStageLabels[i].color = ENTER_TEXT_ACTIVE;
                }
                else
                {
                    enterStageBadges[i].color = ENTER_BADGE_IDLE;
                    enterStageLabels[i].color = ENTER_TEXT_IDLE;
                }
            }

            // 标题尾部 spinner（… 动画）
            if (enterTitleText != null && enterStageIndex < ENTER_STAGE_COUNT)
            {
                int dots = Mathf.FloorToInt(Time.unscaledTime * 2f) % 4;
                string suffix = dots == 0 ? "" : new string('·', dots);
                enterTitleText.text = $"进入吊射模式 {suffix}";
            }

            // 进度条：已完成阶段贡献满格，活动阶段按时间推入 70% 再等事件
            if (enterProgressFillRt != null)
            {
                float done = enterStageIndex;
                if (enterStageIndex < ENTER_STAGE_COUNT)
                {
                    // 每个阶段预期最长时间（仅用于视觉推进，不影响实际等待）
                    float expected = enterStageIndex switch
                    {
                        0 => 0.35f, 1 => 0.5f, 2 => 5f, 3 => 1.2f, _ => 1f,
                    };
                    float partial = Mathf.Clamp01((Time.unscaledTime - enterStageStartTime) / expected) * 0.85f;
                    done += partial;
                }
                float progress = done / ENTER_STAGE_COUNT;
                // 容器宽度由锚点填满；取 rect 宽度
                float barWidth = enterProgressFillRt.parent is RectTransform prt ? prt.rect.width : 380f;
                enterProgressFillRt.sizeDelta = new Vector2(barWidth * progress, 0f);
            }
        }

        private void BuildCrosshairOverlay()
        {
            var root = lobCanvas.transform;
            float cx = 0.5f, cy = 0.48f;

            // 准心水平线
            crosshairH = UIFactory.CreateImage(root, "CrosshairH", CROSSHAIR_COLOR);
            var rtH = crosshairH.rectTransform;
            rtH.anchorMin = new Vector2(cx, cy);
            rtH.anchorMax = new Vector2(cx, cy);
            rtH.pivot = new Vector2(0.5f, 0.5f);
            rtH.anchoredPosition = Vector2.zero;
            rtH.sizeDelta = new Vector2(40f, 2f);

            // 准心垂直线
            crosshairV = UIFactory.CreateImage(root, "CrosshairV", CROSSHAIR_COLOR);
            var rtV = crosshairV.rectTransform;
            rtV.anchorMin = new Vector2(cx, cy);
            rtV.anchorMax = new Vector2(cx, cy);
            rtV.pivot = new Vector2(0.5f, 0.5f);
            rtV.anchoredPosition = Vector2.zero;
            rtV.sizeDelta = new Vector2(2f, 40f);

            // 准心中心点
            crosshairDot = UIFactory.CreateImage(root, "CrosshairDot", CROSSHAIR_COLOR);
            var rtD = crosshairDot.rectTransform;
            rtD.anchorMin = new Vector2(cx, cy);
            rtD.anchorMax = new Vector2(cx, cy);
            rtD.pivot = new Vector2(0.5f, 0.5f);
            rtD.anchoredPosition = Vector2.zero;
            rtD.sizeDelta = new Vector2(6f, 6f);
        }

        private void BuildTrailOverlay()
        {
            // 以 visionDisplay 为父节点，实现轨迹点与图像同步缩放/移动。
            // 父节点 sizeDelta = DISPLAY_W × DISPLAY_H，与 TRAIL 帧坐标系 1024×512 一致，
            // 因此 anchorMin=(0,0) 的子节点 anchoredPosition 可直接用 (x, DISPLAY_H - y)。
            if (visionDisplayRt == null) return;
            var go = new GameObject("TrailOverlay");
            go.transform.SetParent(visionDisplayRt, false);
            trailRoot = go.AddComponent<RectTransform>();
            trailRoot.anchorMin = Vector2.zero;
            trailRoot.anchorMax = Vector2.one;
            trailRoot.pivot = new Vector2(0.5f, 0.5f);
            trailRoot.offsetMin = Vector2.zero;
            trailRoot.offsetMax = Vector2.zero;

            trailDots = new Image[TRAIL_MAX_POINTS];
            for (int i = 0; i < TRAIL_MAX_POINTS; i++)
            {
                var dot = UIFactory.CreateImage(trailRoot, "Dot" + i, TRAIL_DOT_COLOR);
                var rt = dot.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(TRAIL_DOT_SIZE, TRAIL_DOT_SIZE);
                dot.gameObject.SetActive(false);
                trailDots[i] = dot;
            }
            trailActiveCount = 0;
        }

        private void HideAllTrailDots()
        {
            if (trailDots == null) return;
            for (int i = 0; i < trailActiveCount; i++)
            {
                if (trailDots[i] != null) trailDots[i].gameObject.SetActive(false);
            }
            trailActiveCount = 0;
        }

        /// <summary>
        /// 解析 TRAIL_FRAME(0x20) v3.2.1/v3.3.0：
        ///   [point_count][oldest_age][flags]
        ///   points[] : (x_lo,x_hi,y_lo,y_hi) 每点 4B（has_radius=1 时 +1B 半径）
        /// 坐标系：1024×512 原始采集分辨率，与 visionDisplay sizeDelta 1:1。
        /// </summary>
        private void DecodeTrailFrame(byte[] p, int ps, int pl)
        {
            if (!trailEnabled)
            {
                HideAllTrailDots();
                return;
            }
            if (pl < 3 || trailDots == null) return;

            int count = p[ps];
            // int oldestAge = p[ps + 1]; // 暂未使用（可用于时间戳重建）
            byte flags = p[ps + 2];
            bool coordU16 = (flags & 0x01) != 0;
            bool hasRadius = (flags & 0x02) != 0;

            if (!coordU16) return; // v3.2.1 起固定为 uint16，若为 0 视为非法帧
            int stride = hasRadius ? 5 : 4;
            int bodyLen = count * stride;
            if (3 + bodyLen > pl) return;

            int render = count < TRAIL_MAX_POINTS ? count : TRAIL_MAX_POINTS;
            int cursor = ps + 3;
            for (int i = 0; i < render; i++)
            {
                int x = p[cursor] | (p[cursor + 1] << 8);
                int y = p[cursor + 2] | (p[cursor + 3] << 8);
                byte radius = hasRadius ? p[cursor + 4] : (byte)0;
                cursor += stride;

                var dot = trailDots[i];
                if (dot == null) continue;
                var rt = dot.rectTransform;
                // 图像坐标系 y 轴向下，UI y 轴向上 → 翻转
                rt.anchoredPosition = new Vector2(x, DISPLAY_H - y);
                if (hasRadius && radius > 0)
                {
                    float d = radius * 2f;
                    if (d < TRAIL_DOT_SIZE_MIN) d = TRAIL_DOT_SIZE_MIN;
                    rt.sizeDelta = new Vector2(d, d);
                }
                else
                {
                    rt.sizeDelta = new Vector2(TRAIL_DOT_SIZE, TRAIL_DOT_SIZE);
                }
                dot.gameObject.SetActive(true);
            }
            // 关闭多余的点
            for (int i = render; i < trailActiveCount; i++)
            {
                if (trailDots[i] != null) trailDots[i].gameObject.SetActive(false);
            }
            trailActiveCount = render;
        }

        // ═══════════════════════════════ v3.3.0 文字/装甲/雷达标记 ═══════════════════════════════

        /// <summary>TEXT_MSG(0x30) 到达时触发。severity: 0=INFO 1=WARN 2=ERROR。</summary>
        public static event System.Action<byte, byte, string> OnTextMessage;

        /// <summary>ARMOR_TARGETS(0x40) 到达时触发。</summary>
        public static event System.Action<ArmorTargetsFrame> OnArmorTargets;

        /// <summary>RADAR_MARK(0x51) 到达时触发。action: 0x01=MARK, 0x02=CANCEL。</summary>
        public static event System.Action<byte, byte> OnRadarMark;

        public struct ArmorTarget
        {
            public byte robotType;
            public byte armorId;
            public ushort x, y, w, h;
            public byte confidence;
        }

        public struct ArmorTargetsFrame
        {
            public ushort imgWidth;
            public ushort imgHeight;
            /// <summary>有效目标数量（订阅方应遍历 targets[0 .. count-1]）。</summary>
            public int count;
            /// <summary>复用缓冲区，长度固定 ARMOR_TARGETS_MAX；事件派发完成后不得保留引用。</summary>
            public ArmorTarget[] targets;
        }

        // 预分配缓冲（零 GC）：协议 §4.12 限制 count ≤ 20
        private const int ARMOR_TARGETS_MAX = 20;
        private readonly ArmorTarget[] armorTargetsBuf = new ArmorTarget[ARMOR_TARGETS_MAX];

        private void DecodeTextMsg(byte[] p, int ps, int pl)
        {
            if (pl < 3) return;
            byte msgId = p[ps];
            byte severity = p[ps + 1];
            byte textLen = p[ps + 2];
            string text = null;
            if (msgId == 0x00 && textLen > 0)
            {
                if (3 + textLen > pl) return;
                text = System.Text.Encoding.UTF8.GetString(p, ps + 3, textLen);
            }
            try { OnTextMessage?.Invoke(msgId, severity, text); }
            catch (System.Exception ex) { wmj.Log.E($"[LobShotHUD] OnTextMessage 异常: {ex.Message}", wmj.Log.Tag.UI); }
            wmj.Log.I($"[LobShotHUD] TEXT_MSG id=0x{msgId:X2} sev={severity} text=\"{text}\"", wmj.Log.Tag.UI);
        }

        private void DecodeArmorTargets(byte[] p, int ps, int pl)
        {
            if (pl < 5) return;
            ushort imgW = (ushort)(p[ps] | (p[ps + 1] << 8));
            ushort imgH = (ushort)(p[ps + 2] | (p[ps + 3] << 8));
            int count = p[ps + 4];
            if (count > ARMOR_TARGETS_MAX) return; // 协议 §4.12 硬上限
            const int ENTRY = 11;
            if (5 + count * ENTRY > pl) return;
            int cursor = ps + 5;
            for (int i = 0; i < count; i++)
            {
                armorTargetsBuf[i].robotType = p[cursor];
                armorTargetsBuf[i].armorId = p[cursor + 1];
                armorTargetsBuf[i].x = (ushort)(p[cursor + 2] | (p[cursor + 3] << 8));
                armorTargetsBuf[i].y = (ushort)(p[cursor + 4] | (p[cursor + 5] << 8));
                armorTargetsBuf[i].w = (ushort)(p[cursor + 6] | (p[cursor + 7] << 8));
                armorTargetsBuf[i].h = (ushort)(p[cursor + 8] | (p[cursor + 9] << 8));
                armorTargetsBuf[i].confidence = p[cursor + 10];
                cursor += ENTRY;
            }
            try
            {
                OnArmorTargets?.Invoke(new ArmorTargetsFrame
                {
                    imgWidth = imgW,
                    imgHeight = imgH,
                    count = count,
                    targets = armorTargetsBuf,
                });
            }
            catch (System.Exception ex) { wmj.Log.E($"[LobShotHUD] OnArmorTargets 异常: {ex.Message}", wmj.Log.Tag.UI); }
        }

        private void DecodeRadarMark(byte[] p, int ps, int pl)
        {
            if (pl < 2) return;
            byte targetId = p[ps];
            byte action = p[ps + 1];
            try { OnRadarMark?.Invoke(targetId, action); }
            catch (System.Exception ex) { wmj.Log.E($"[LobShotHUD] OnRadarMark 异常: {ex.Message}", wmj.Log.Tag.UI); }
            wmj.Log.I($"[LobShotHUD] RADAR_MARK target=0x{targetId:X2} action={(action == 0x01 ? "MARK" : action == 0x02 ? "CANCEL" : "?")}", wmj.Log.Tag.UI);
        }

        private void BuildBaseHealthBar()
        {
            var root = lobCanvas.transform;

            var barGO = new GameObject("BaseBar");
            barGO.transform.SetParent(root, false);
            baseBarRoot = barGO.AddComponent<RectTransform>();
            baseBarRoot.anchorMin = new Vector2(0.5f, 0.48f + DISPLAY_H / 1080f / 2f + 0.025f);
            baseBarRoot.anchorMax = baseBarRoot.anchorMin;
            baseBarRoot.pivot = new Vector2(0.5f, 0.5f);
            baseBarRoot.anchoredPosition = Vector2.zero;
            baseBarRoot.sizeDelta = new Vector2(DISPLAY_W, 40);

            baseTitleText = UIFactory.CreateText(baseBarRoot, "Title",
                "[ ENEMY BASE ]", 16, TextAlignmentOptions.Center,
                BORDER_COLOR, FontStyles.Bold);
            baseTitleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            baseTitleText.rectTransform.anchorMax = new Vector2(1f, 1.8f);
            baseTitleText.rectTransform.offsetMin = Vector2.zero;
            baseTitleText.rectTransform.offsetMax = Vector2.zero;

            baseBarBg = UIFactory.CreateImage(baseBarRoot, "BarBg",
                new Color(0.08f, 0.08f, 0.12f, 0.85f));
            UIFactory.ApplyRoundedCorners(baseBarBg, 64, 8);
            baseBarBg.rectTransform.anchorMin = Vector2.zero;
            baseBarBg.rectTransform.anchorMax = Vector2.one;
            baseBarBg.rectTransform.offsetMin = new Vector2(40, 2);
            baseBarBg.rectTransform.offsetMax = new Vector2(-40, -2);

            baseBarFill = UIFactory.CreateImage(baseBarBg.transform, "Fill", BASE_HP_COLOR);
            UIFactory.ApplyRoundedCorners(baseBarFill, 64, 8);
            baseBarFill.rectTransform.anchorMin = Vector2.zero;
            baseBarFill.rectTransform.anchorMax = Vector2.one;
            baseBarFill.rectTransform.offsetMin = new Vector2(2, 2);
            baseBarFill.rectTransform.offsetMax = new Vector2(-2, -2);
            baseBarFill.type = Image.Type.Filled;
            baseBarFill.fillMethod = Image.FillMethod.Horizontal;
            baseBarFill.fillOrigin = 0;

            baseBarShield = UIFactory.CreateImage(baseBarBg.transform, "Shield", BASE_SHIELD_COLOR);
            UIFactory.ApplyRoundedCorners(baseBarShield, 64, 8);
            baseBarShield.rectTransform.anchorMin = Vector2.zero;
            baseBarShield.rectTransform.anchorMax = Vector2.one;
            baseBarShield.rectTransform.offsetMin = new Vector2(2, 2);
            baseBarShield.rectTransform.offsetMax = new Vector2(-2, -2);
            baseBarShield.type = Image.Type.Filled;
            baseBarShield.fillMethod = Image.FillMethod.Horizontal;
            baseBarShield.fillOrigin = 0;
            baseBarShield.fillAmount = 0f;

            baseHpText = UIFactory.CreateText(baseBarBg.transform, "HpText",
                "5000 / 5000", 18, TextAlignmentOptions.Center,
                Color.white, FontStyles.Bold);
            baseHpText.rectTransform.anchorMin = Vector2.zero;
            baseHpText.rectTransform.anchorMax = Vector2.one;
            baseHpText.rectTransform.offsetMin = Vector2.zero;
            baseHpText.rectTransform.offsetMax = Vector2.zero;
            baseHpText.textWrappingMode = TextWrappingModes.NoWrap;
            baseHpText.overflowMode = TextOverflowModes.Overflow;
        }

        // ═══════════════════════════════ 数据接收 ═══════════════════════════════

        private void OnProtoDataUpdated(string typeName, object data)
        {
            if (typeName != "CustomByteBlock") return;

            var block = data as CustomByteBlock;
            if (block == null || block.Data == null) return;
            if (block.Data.Length < 9) return;

            // 未进入吊射模式时：
            //   - autoShowLobShotOnIncomingFrame = true → 自动 Show()（观察队友吊射画面）
            //   - autoShowLobShotOnIncomingFrame = false → 丢弃数据，保持原有行为
            if (!isShowing)
            {
                if (GameParamsConfig.Get.autoShowLobShotOnIncomingFrame)
                {
                    // 首帧自动弹出：不发送 deploy 命令（只是 UI 展示，不干扰本机机器人状态）
                    Show();
                    if (!isShowing) return; // Show 失败（未初始化）则跳过
                }
                else
                {
                    return;
                }
            }

            // 使用 ArrayPool 避免每帧 ToByteArray() 的 GC 分配
            int dataLen = block.Data.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(dataLen);
            block.Data.CopyTo(rented, 0);
            pendingFrames.Enqueue(new PooledBuffer { Data = rented, Length = dataLen });

            int fc = System.Threading.Interlocked.Increment(ref frameCount);

            // 首帧到达：推进入场 HUD 进度（淡出由 TickEnterOverlay 处理）
            if (fc == 1) OnEnterOverlayDataReceived();
            if (fc <= 5)
            {
                string hexHead = "";
                for (int i = 0; i < Mathf.Min(16, dataLen); i++)
                    hexHead += rented[i].ToString("X2") + " ";
                wmj.Log.I($"[LobShotHUD] 帧 #{fc} 到达, 长度={dataLen}, 头={hexHead}", wmj.Log.Tag.UI);
            }
            else if (fc % 50 == 0)
            {
                wmj.Log.D($"[LobShotHUD] 已收到 {fc} 帧", wmj.Log.Tag.UI);
            }
        }

        // ═══════════════════════════════ 帧分发 (v1/v2 自动识别) ═══════════════════════════════

        private void ProcessPendingFrames()
        {
            // 背压保护：队列过深时丢弃旧数据，归还 ArrayPool
            while (pendingFrames.Count > 256)
            {
                if (pendingFrames.TryDequeue(out var dropped))
                    ArrayPool<byte>.Shared.Return(dropped.Data);
            }

            int decoded = 0;
            while (pendingFrames.TryDequeue(out PooledBuffer buf))
            {
                decoded++;
                if (decoded > 32) // 每帧上限
                {
                    ArrayPool<byte>.Shared.Return(buf.Data);
                    break;
                }

                // 首先尝试 v2 H.264 管线（使用 buf.Length 而非 buf.Data.Length）
                if (h264Transport != null && h264Transport.ProcessProtocolPacket(buf.Data, buf.Length))
                {
                    ArrayPool<byte>.Shared.Return(buf.Data);
                    // 0x04 帧，已由 H.264 传输层处理
                    if (!isV2Mode)
                    {
                        isV2Mode = true;
                        SwitchToV2Texture();
                        wmj.Log.I("[LobShotHUD] 检测到 H.264 码流 (0x04), 切换到 v2 彩色管线", wmj.Log.Tag.UI);
                    }
                    continue;
                }

                // 非 0x04 帧 → 走 v1 二值化管线
                if (isV2Mode)
                {
                    isV2Mode = false;
                    SwitchToV1Texture();
                    wmj.Log.I("[LobShotHUD] 检测到二值帧, 回退到 v1 管线", wmj.Log.Tag.UI);
                }
                // v1 需要精确长度的数组（DecodeV1Frame 使用 packet.Length）
                // 创建临时副本 — v1 路径使用频率极低，不影响整体零分配目标
                if (buf.Data.Length != buf.Length)
                {
                    byte[] exact = new byte[buf.Length];
                    System.Buffer.BlockCopy(buf.Data, 0, exact, 0, buf.Length);
                    ArrayPool<byte>.Shared.Return(buf.Data);
                    DecodeV1Frame(exact);
                }
                else
                {
                    DecodeV1Frame(buf.Data);
                    ArrayPool<byte>.Shared.Return(buf.Data);
                }
            }
        }

        /// <summary>从 H.264 传输层取出重组完成的帧，推给解码器，取出解码结果更新纹理</summary>
        private void DrainH264Pipeline()
        {
            if (h264Transport == null || h264Decoder == null) return;

            // ─── 1. 仿真 UDP 批量路径：一次性推送所有累积的 AnnexB 数据 ───
            if (h264Transport.TryDrainSimUdpData(out var simBatch, out int simBatchLen))
            {
                h264Decoder.Push(simBatch, simBatchLen);
                ArrayPool<byte>.Shared.Return(simBatch); // 归还池化批量数组
            }

            // ─── 2. 比赛模式协议帧路径：每帧是完整的 H.264 Access Unit ───
            int pushed = 0;
            while (h264Transport.TryGetFrame(out var h264Frame) && pushed < 8)
            {
                h264Decoder.Push(h264Frame.AnnexBData, h264Frame.DataLength);
                ArrayPool<byte>.Shared.Return(h264Frame.AnnexBData); // 归还池化帧数据
                pushed++;
            }

            // 取出解码结果 → 只显示最新一帧（避免多次 SR 推理卡顿主线程）
            bool hasLatest = false;
            DecodedFrame latestDecoded = default;
            while (h264Decoder.TryGetFrame(out DecodedFrame decoded))
            {
                // 首次成功解码时自动切换到 v2 彩色模式
                if (!isV2Mode)
                {
                    isV2Mode = true;
                    SwitchToV2Texture();
                    wmj.Log.I("[LobShotHUD] 首帧 H.264 解码成功, 切换到 v2 彩色管线", wmj.Log.Tag.UI);
                }
                // 丢弃上一个中间帧，保留最新帧
                if (hasLatest)
                    latestDecoded.ReturnToPool();
                latestDecoded = decoded;
                hasLatest = true;
            }
            // 只对最新帧执行一次 SR 推理
            if (hasLatest)
            {
                ApplyDecodedFrame(latestDecoded);
                latestDecoded.ReturnToPool();
            }
        }

        /// <summary>
        /// 将解码后的 RGB24 帧数据写入纹理，并根据 GPU 压力动态决定超分/双线性策略
        /// 
        /// 管线:
        ///   RGB24 (1024×512) → RGBA32 (Y翻转) → decodeTex → [SR推理 | Bilinear] → 显示
        ///   GPU 压力级别决定 SR 行为：Normal=每帧SR, Light=隔帧SR, Heavy=仅双线性, Critical=跳过
        ///   (v3.2.1 起 SR 默认关闭，除非重训了 1024×512 模型)
        /// </summary>
        private void ApplyDecodedFrame(DecodedFrame frame)
        {
            if (frame.Pixels == null || frame.Width != V2_TEX_W || frame.Height != V2_TEX_H)
            {
                if (frame.Pixels != null)
                    wmj.Log.W($"[LobShotHUD] 解码帧尺寸不匹配: {frame.Width}×{frame.Height}, 期望 {V2_TEX_W}×{V2_TEX_H}", wmj.Log.Tag.UI);
                return;
            }

            // ─── Critical 压力：跳过帧处理 ───
            if (gpuPressure == GpuPressureLevel.Critical)
            {
                v2FrameCount++;
                return;
            }

            // ─── 1. RGB24 → RGBA32 (翻转 Y 轴, Unity 纹理 Y=0 在底部) ───
            int rowPixels = V2_TEX_W;
            for (int y = 0; y < V2_TEX_H; y++)
            {
                int srcY = V2_TEX_H - 1 - y;
                int srcRowStart = srcY * rowPixels * 3;
                int dstRowStart = y * rowPixels * 4;

                for (int x = 0; x < rowPixels; x++)
                {
                    int si = srcRowStart + x * 3;
                    int di = dstRowStart + x * 4;
                    v2PixelBuf[di] = frame.Pixels[si];     // R
                    v2PixelBuf[di + 1] = frame.Pixels[si + 1]; // G
                    v2PixelBuf[di + 2] = frame.Pixels[si + 2]; // B
                    v2PixelBuf[di + 3] = 255;                   // A
                }
            }

            // ─── 2. 写入解码纹理 ───
            float tBeforeApply = updateStopwatch.ElapsedMilliseconds;
            decodeTex.SetPixelData(v2PixelBuf, 0);
            decodeTex.Apply(false);
            float tAfterApply = updateStopwatch.ElapsedMilliseconds;
            float applyMs = tAfterApply - tBeforeApply;
            if (applyMs > 5f)
                RecordPhaseTime("Tex.Apply", applyMs, tAfterApply);

            // ─── 3. GPU 压力自适应超分辨率 ───
            float tBeforeSR = updateStopwatch.ElapsedMilliseconds;

            // 时间预算耗尽 → 跳过 SR
            if (tBeforeSR > UPDATE_BUDGET_MS && srModule != null)
            {
                if (visionDisplay.texture != decodeTex)
                    visionDisplay.texture = decodeTex;
                RecordPhaseTime("SR_skipped(budget)", 0, tBeforeSR);
                v2FrameCount++;
                return;
            }

            // 判断是否应执行 SR 推理（基于 GPU 压力 + 熔断器）
            bool srAvailable = stretchToFullHD && srModule != null && srModule.IsReady && srEnabled
                && !circuitBreakerPermanent && circuitBreakerLevel == 0;

            bool shouldDoSR = false;
            if (srAvailable)
            {
                float now = Time.realtimeSinceStartup;
                bool intervalOk = (now - lastSrInferTime) >= SR_INFER_MIN_INTERVAL;
                // 仅当 GPU 着色器预热完成（IsWarmedUp=true）后才允许推理。
                // 预热由 SRWarmupCoroutine 通过 ScheduleIterable 逐帧完成，不再依赖固定时间延迟。
                bool warmupReady = srModule.IsWarmedUp;

                switch (gpuPressure)
                {
                    case GpuPressureLevel.Normal:
                        shouldDoSR = warmupReady && intervalOk;
                        break;
                    case GpuPressureLevel.Light:
                        // 隔帧推理：减半 GPU 负载
                        srSkipCounter++;
                        shouldDoSR = warmupReady && intervalOk && (srSkipCounter % 2 == 0);
                        break;
                    case GpuPressureLevel.Heavy:
                    case GpuPressureLevel.Critical:
                        shouldDoSR = false; // 仅双线性
                        break;
                }
            }

            if (shouldDoSR)
            {
                try
                {
                    if (srModule.Infer(decodeTex))
                    {
                        lastSrInferTime = Time.realtimeSinceStartup;
                        if (visionDisplay.texture != srModule.OutputTexture)
                            visionDisplay.texture = srModule.OutputTexture;

                        // 检查推理耗时 → 触发熔断升级
                        float tAfterSR = updateStopwatch.ElapsedMilliseconds;
                        float srMs = tAfterSR - tBeforeSR;
                        if (srMs > 10f)
                            RecordPhaseTime("SR_Infer", srMs, tAfterSR);

                        if (srMs > 50f)
                        {
                            // 单次推理 >50ms → 触发熔断
                            TriggerCircuitBreaker($"SR 单次推理 {srMs:F1}ms, GPU 争抢严重");
                            if (visionDisplay != null && decodeTex != null)
                                visionDisplay.texture = decodeTex;
                        }
                        else if (srModule.ConsecutiveSlowInfers >= 3)
                        {
                            TriggerCircuitBreaker($"SR 连续慢推理({srModule.LastInferMs:F1}ms)");
                            visionDisplay.texture = decodeTex;
                        }
                    }
                    else
                    {
                        if (visionDisplay.texture != decodeTex)
                            visionDisplay.texture = decodeTex;
                    }
                }
                catch (System.Exception ex)
                {
                    TriggerCircuitBreaker($"SR 推理异常: {ex.Message}");
                    if (visionDisplay != null && decodeTex != null)
                        visionDisplay.texture = decodeTex;
                }
            }
            else
            {
                // 仅双线性放大
                if (visionDisplay.texture != decodeTex)
                    visionDisplay.texture = decodeTex;
            }

            v2FrameCount++;
        }

        /// <summary>触发熔断器（升级熔断级别，指数退避恢复）</summary>
        private void TriggerCircuitBreaker(string reason)
        {
            circuitBreakerLevel++;
            consecutiveFuseTriggers++;
            circuitBreakerFuseTime = Time.realtimeSinceStartup;

            // 指数退避恢复时间
            circuitBreakerRecoverSec = Mathf.Min(
                10f * Mathf.Pow(CB_BACKOFF_FACTOR, consecutiveFuseTriggers - 1),
                CB_MAX_RECOVER_SEC);

            // 永久熔断检查
            if (consecutiveFuseTriggers >= CB_PERMANENT_THRESHOLD)
            {
                circuitBreakerPermanent = true;
                wmj.Log.E($"[LobShotHUD] 🔴 SR 永久熔断（连续 {consecutiveFuseTriggers} 次触发）: {reason}",
                    wmj.Log.Tag.UI);
            }
            else
            {
                wmj.Log.W($"[LobShotHUD] ⚠️ SR 熔断升级 Lv{circuitBreakerLevel}" +
                    $"（{circuitBreakerRecoverSec:F0}s 后恢复）: {reason}", wmj.Log.Tag.UI);
            }
        }

        // ═══════════════════════════════ 纹理模式切换 ═══════════════════════════════

        private void SwitchToV2Texture()
        {
            // 确保解码纹理尺寸正确 (1024×512, v3.2.1)
            if (decodeTex == null || decodeTex.width != V2_TEX_W || decodeTex.height != V2_TEX_H)
            {
                if (decodeTex != null) Destroy(decodeTex);
                decodeTex = new Texture2D(V2_TEX_W, V2_TEX_H, TextureFormat.RGBA32, false);
                decodeTex.filterMode = FilterMode.Bilinear;
                decodeTex.wrapMode = TextureWrapMode.Clamp;
            }
            // SR 启用时 visionDisplay.texture 在 ApplyDecodedFrame 中动态切换
            // 默认先显示解码纹理
            visionDisplay.texture = decodeTex;
            UpdateV2DisplayLayout();
        }

        private void ReloadDisplayConfigFromGameParams()
        {
            var gp = GameParamsConfig.Get;
            stretchToFullHD = gp.lobShotStretchTo720x1080;
            srEnabled = gp.lobShotUseSrWhenStretched;
        }

        private void UpdateV2DisplayLayout()
        {
            if (visionDisplayRt == null) return;
            visionDisplayRt.sizeDelta = stretchToFullHD
                ? new Vector2(DISPLAY_W, DISPLAY_H)
                : new Vector2(V2_TEX_W, V2_TEX_H);
        }

        private void SwitchToV1Texture()
        {
            if (displayTex == null || displayTex.width != V1_TEX_W || displayTex.height != V1_TEX_H)
            {
                if (displayTex != null) Destroy(displayTex);
                displayTex = new Texture2D(V1_TEX_W, V1_TEX_H, TextureFormat.RGBA32, false);
                displayTex.filterMode = FilterMode.Point;
                displayTex.wrapMode = TextureWrapMode.Clamp;
                visionDisplay.texture = displayTex;
            }
        }

        // ═══════════════════════════════ v1 二值化兼容管线 ═══════════════════════════════

        private void DecodeV1Frame(byte[] packet)
        {
            if (packet.Length < 9) return;
            if (packet[0] != 0xA5 || packet[packet.Length - 1] != 0x5A) return;

            // XOR 校验
            byte xor = 0;
            for (int i = 0; i < packet.Length - 2; i++) xor ^= packet[i];
            if (xor != packet[packet.Length - 2]) return;

            byte frameType = packet[1];
            int payloadLen = packet[5] | (packet[6] << 8);
            int payloadStart = 7;

            switch (frameType)
            {
                case FT_I_PART1:
                    V1_DecodeIFramePart1(packet, payloadStart, payloadLen);
                    break;
                case FT_I_PART2:
                    V1_DecodeIFramePart2(packet, payloadStart, payloadLen);
                    break;
                case FT_I_SINGLE:
                    V1_DecodeIFrameSingle(packet, payloadStart, payloadLen);
                    break;
                case FT_D_FRAME:
                    V1_DecodeDFrame(packet, payloadStart, payloadLen);
                    break;
                case FT_D_EMPTY:
                    V1_UpdateTexture();
                    break;
                case FT_TRAIL:
                    DecodeTrailFrame(packet, payloadStart, payloadLen);
                    break;
                case FT_TEXT_MSG:
                    DecodeTextMsg(packet, payloadStart, payloadLen);
                    break;
                case FT_ARMOR_TARGETS:
                    DecodeArmorTargets(packet, payloadStart, payloadLen);
                    break;
                case FT_RADAR_MARK:
                    DecodeRadarMark(packet, payloadStart, payloadLen);
                    break;
                case FT_HEARTBEAT:
                    break;
            }
        }

        private void V1_DecodeIFrameSingle(byte[] p, int ps, int pl)
        {
            if (pl < 4) return;
            byte bgFill = p[ps + 2] == 0xFF ? (byte)0xFF : (byte)0x00;
            for (int i = 0; i < v1FrameBuf.Length; i++) v1FrameBuf[i] = bgFill;
            int count = p[ps + 3];
            V1_DecodeBlockEntries(p, ps + 4, count, p.Length - 2, false, bgFill);
            V1_UpdateTexture();
        }

        private void V1_DecodeIFramePart1(byte[] p, int ps, int pl)
        {
            if (pl < 3) return;
            byte bgColor = p[ps + 2];
            V1_DecodeRLEToBuf(p, ps + 3, pl - 3, bgColor, v1ThumbBuf, V1_THUMB_W, V1_THUMB_H);
            V1_UpsampleNN(v1ThumbBuf, V1_THUMB_W, V1_THUMB_H, v1FrameBuf, V1_TEX_W, V1_TEX_H);
            v1LastPart1FrameId = (ushort)(p[2] | (p[3] << 8));
            V1_UpdateTexture();
        }

        private void V1_DecodeIFramePart2(byte[] p, int ps, int pl)
        {
            if (pl < 3) return;
            int pos = ps + 2;
            int count = p[pos++];
            V1_DecodeBlockEntries(p, pos, count, p.Length - 2, true, 0x00);
            V1_UpdateTexture();
        }

        private void V1_DecodeDFrame(byte[] p, int ps, int pl)
        {
            if (pl < 1) return;
            int pos = ps;
            int end = p.Length - 2;
            int blockCount = p[pos++];
            for (int i = 0; i < blockCount && pos + 10 <= end; i++)
            {
                int bx = p[pos++], by = p[pos++];
                for (int row = 0; row < 8; row++)
                {
                    int offset = by * 8 * (V1_TEX_W / 8) + row * (V1_TEX_W / 8) + bx;
                    if (offset < v1FrameBuf.Length)
                        v1FrameBuf[offset] ^= p[pos + row];
                }
                pos += 8;
            }
            V1_UpdateTexture();
        }

        private void V1_DecodeBlockEntries(byte[] p, int startPos, int count, int end,
            bool applyXor, byte bgFill)
        {
            int pos = startPos;
            byte[] blockBuf = new byte[8];
            for (int i = 0; i < count && pos < end; i++)
            {
                if (pos + 3 > end) break;
                int bx = p[pos++], by = p[pos++];
                byte rleLenByte = p[pos++];
                if (rleLenByte == 0)
                {
                    byte fillVal = (byte)(bgFill ^ 0xFF);
                    for (int r = 0; r < 8; r++) blockBuf[r] = fillVal;
                }
                else if ((rleLenByte & 0x80) != 0)
                {
                    int rawLen = rleLenByte & 0x7F;
                    if (pos + rawLen > end) break;
                    for (int r = 0; r < 8; r++)
                        blockBuf[r] = (r < rawLen) ? p[pos + r] : (byte)0;
                    pos += rawLen;
                }
                else
                {
                    int rleLen = rleLenByte & 0x7F;
                    if (pos + rleLen > end) break;
                    V1_DecodeBlockRLE(p, pos, rleLen, blockBuf);
                    pos += rleLen;
                }
                for (int row = 0; row < 8; row++)
                {
                    int y = by * 8 + row;
                    int offset = y * (V1_TEX_W / 8) + bx;
                    if (offset >= 0 && offset < v1FrameBuf.Length)
                    {
                        if (applyXor) v1FrameBuf[offset] ^= blockBuf[row];
                        else v1FrameBuf[offset] = blockBuf[row];
                    }
                }
            }
        }

        private void V1_DecodeBlockRLE(byte[] data, int offset, int rleLen, byte[] blockBuf)
        {
            for (int i = 0; i < 8; i++) blockBuf[i] = 0;
            if (rleLen <= 0 || offset >= data.Length) return;
            bool cur = data[offset] == 0x01;
            int idx = offset + 1, end = offset + rleLen, px = 0;
            while (idx < end && idx < data.Length && px < 64)
            {
                int run = data[idx++];
                if (run == 0) { cur = !cur; continue; }
                for (int j = 0; j < run && px < 64; j++, px++)
                    if (cur) blockBuf[px / 8] |= (byte)(1 << (7 - px % 8));
                cur = !cur;
            }
        }

        private void V1_DecodeRLEToBuf(byte[] data, int offset, int rleLen, byte bgColor,
            byte[] buf, int w, int h)
        {
            int total = w * h, bytes = total / 8;
            byte fill = bgColor == 0xFF ? (byte)0xFF : (byte)0x00;
            for (int i = 0; i < bytes && i < buf.Length; i++) buf[i] = fill;
            if (rleLen <= 0 || offset >= data.Length) return;
            bool cur = data[offset] == 0x01;
            int idx = offset + 1, end = offset + rleLen, px = 0;
            while (idx < end && idx < data.Length && px < total)
            {
                int run = data[idx++];
                if (run == 0) { cur = !cur; continue; }
                for (int j = 0; j < run && px < total; j++, px++)
                {
                    int bi = px / 8, bit = 7 - px % 8;
                    if (bi < buf.Length)
                    {
                        if (cur) buf[bi] |= (byte)(1 << bit);
                        else buf[bi] &= (byte)~(1 << bit);
                    }
                }
                cur = !cur;
            }
        }

        private void V1_UpsampleNN(byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH)
        {
            for (int i = 0; i < dst.Length; i++) dst[i] = 0;
            for (int ty = 0; ty < srcH; ty++)
                for (int tx = 0; tx < srcW; tx++)
                {
                    int sb = ty * (srcW / 8) + tx / 8;
                    if (sb >= src.Length) continue;
                    if ((src[sb] & (1 << (7 - tx % 8))) == 0) continue;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int fx = tx * 2 + dx, fy = ty * 2 + dy;
                            int fb = fy * (dstW / 8) + fx / 8;
                            if (fb < dst.Length)
                                dst[fb] |= (byte)(1 << (7 - fx % 8));
                        }
                }
        }

        private void V1_UpdateTexture()
        {
            // 1bit → RGBA32
            for (int y = 0; y < V1_TEX_H; y++)
            {
                int srcY = V1_TEX_H - 1 - y;
                for (int x = 0; x < V1_TEX_W; x++)
                {
                    int srcIdx = srcY * (V1_TEX_W / 8) + x / 8;
                    bool white = (v1FrameBuf[srcIdx] & (1 << (7 - x % 8))) != 0;
                    int pi = (y * V1_TEX_W + x) * 4;
                    if (white)
                    {
                        v1PixelBuf[pi] = 180; v1PixelBuf[pi + 1] = 220;
                        v1PixelBuf[pi + 2] = 190; v1PixelBuf[pi + 3] = 255;
                    }
                    else
                    {
                        v1PixelBuf[pi] = 0; v1PixelBuf[pi + 1] = 0;
                        v1PixelBuf[pi + 2] = 0; v1PixelBuf[pi + 3] = 255;
                    }
                }
            }

            if (displayTex == null || displayTex.width != V1_TEX_W) SwitchToV1Texture();
            displayTex.SetPixelData(v1PixelBuf, 0);
            displayTex.Apply(false);
        }

        // ═══════════════════════════════ Update ═══════════════════════════════

        void Update()
        {
            if (!isShowing) return;

            TickEnterOverlay();

            updateStopwatch.Restart();
            float frameStart = Time.realtimeSinceStartup;

            // ─── GPU 压力动态监控（移动平均帧时间） ───
            if (lastUpdateTime > 0f)
            {
                float deltaMs = (frameStart - lastUpdateTime) * 1000f;
                recentFrameTimes[frameTimeIndex % FRAME_TIME_WINDOW] = deltaMs;
                frameTimeIndex++;

                // 计算移动平均帧时间
                int sampleCount = Mathf.Min(frameTimeIndex, FRAME_TIME_WINDOW);
                float sum = 0f;
                for (int i = 0; i < sampleCount; i++) sum += recentFrameTimes[i];
                avgFrameTimeMs = sum / sampleCount;

                // 帧率保护：连续慢帧检测
                if (deltaMs > 50f)
                {
                    slowFrameCount++;
                    if (slowFrameCount >= SLOW_FRAME_FUSE && srModule != null && circuitBreakerLevel == 0)
                    {
                        TriggerCircuitBreaker($"连续 {slowFrameCount} 帧超时({deltaMs:F0}ms)");
                        if (visionDisplay != null && decodeTex != null)
                            visionDisplay.texture = decodeTex;
                    }
                }
                else
                {
                    slowFrameCount = 0;
                }

                // 动态 GPU 压力级别判定（基于移动平均帧时间 + SR 推理耗时）
                float srLastMs = srModule?.LastInferMs ?? 0f;
                if (sampleCount >= 10) // 采样足够后才开始判定
                {
                    if (avgFrameTimeMs > 80f || deltaMs > 150f)
                        gpuPressure = GpuPressureLevel.Critical;
                    else if (avgFrameTimeMs > 40f || srLastMs > 30f)
                        gpuPressure = GpuPressureLevel.Heavy;
                    else if (avgFrameTimeMs > 25f || srLastMs > 15f)
                        gpuPressure = GpuPressureLevel.Light;
                    else
                        gpuPressure = GpuPressureLevel.Normal;
                }
            }
            lastUpdateTime = frameStart;

            // ─── 熔断器恢复（指数退避） ───
            if (circuitBreakerLevel > 0 && !circuitBreakerPermanent)
            {
                float elapsed = frameStart - circuitBreakerFuseTime;
                if (elapsed >= circuitBreakerRecoverSec)
                {
                    circuitBreakerLevel = 0;
                    wmj.Log.I($"[LobShotHUD] ✅ SR 熔断恢复（等待 {elapsed:F0}s），" +
                        $"GPU 压力={gpuPressure}，尝试重新启用推理", wmj.Log.Tag.UI);
                    slowFrameCount = 0;
                    consecutiveBudgetExceedFrames = 0;
                    consecutivePipelineBacklogFrames = 0;
                }
            }

            // ─── 解码管线拥塞监测：队列持续积压时提前熔断 SR，优先保证实时性 ───
            int transportBacklog = h264Transport?.PendingFrameCount ?? 0;
            int decoderBacklog = h264Decoder?.GetQueueCount() ?? 0;
            bool pipelineBacklogged = transportBacklog >= TRANSPORT_BACKLOG_THRESHOLD
                || decoderBacklog >= DECODER_BACKLOG_THRESHOLD;
            if (pipelineBacklogged)
            {
                consecutivePipelineBacklogFrames++;
                if (consecutivePipelineBacklogFrames >= PIPELINE_BACKLOG_FUSE
                    && srModule != null && stretchToFullHD
                    && circuitBreakerLevel == 0 && !circuitBreakerPermanent)
                {
                    TriggerCircuitBreaker($"解码管线积压 transport={transportBacklog}, decoder={decoderBacklog}");
                    if (visionDisplay != null && decodeTex != null)
                        visionDisplay.texture = decodeTex;
                }
            }
            else
            {
                consecutivePipelineBacklogFrames = 0;
            }

            // ─── 1. 处理挂起的协议帧 ───
            float t1 = updateStopwatch.ElapsedMilliseconds;
            ProcessPendingFrames();
            float t2 = updateStopwatch.ElapsedMilliseconds;

            // 时间预算检查：如果协议帧已超预算，跳过解码
            if (t2 > UPDATE_BUDGET_MS)
            {
                RecordPhaseTime("ProcessPendingFrames", t2 - t1, t2);
                consecutiveBudgetExceedFrames++;
                if (consecutiveBudgetExceedFrames >= BUDGET_EXCEED_FUSE
                    && srModule != null && stretchToFullHD
                    && circuitBreakerLevel == 0 && !circuitBreakerPermanent)
                {
                    TriggerCircuitBreaker($"主线程预算连续超限 {consecutiveBudgetExceedFrames} 帧({t2:F1}ms)");
                    if (visionDisplay != null && decodeTex != null)
                        visionDisplay.texture = decodeTex;
                }
            }
            else
            {
                consecutiveBudgetExceedFrames = 0;
                // ─── 2. H.264 解码管线 ───
                DrainH264Pipeline();
                float t3 = updateStopwatch.ElapsedMilliseconds;
                if (t3 - t2 > 8f)
                    RecordPhaseTime("DrainH264Pipeline", t3 - t2, t3);
            }

            // ─── 3. 诊断日志（含 GPU 压力 + 熔断器状态） ───
            if (Time.realtimeSinceStartup - lastDiagLogTime > DIAG_LOG_INTERVAL)
            {
                lastDiagLogTime = Time.realtimeSinceStartup;
                string transportDiag = h264Transport?.GetDiagnostics() ?? "N/A";
                string modeStr = isV2Mode ? "v2_H264" : "v1_binary";
                string srDiag = srModule?.GetDiagnostics() ?? "SR=OFF";
                string fuseStr = circuitBreakerPermanent ? " [SR永久熔断]"
                    : circuitBreakerLevel > 0 ? $" [SR熔断Lv{circuitBreakerLevel},{circuitBreakerRecoverSec:F0}s]"
                    : "";
                string pressureStr = $" GPU压力={gpuPressure}(avg={avgFrameTimeMs:F1}ms)";
                int transportBacklogDiag = h264Transport?.PendingFrameCount ?? 0;
                int decoderBacklogDiag = h264Decoder?.GetQueueCount() ?? 0;
                string freezeInfo = totalFreezeFrames > 0 ? $" 严重卡帧={totalFreezeFrames}" : "";
                wmj.Log.I($"[LobShotHUD] 诊断: 总帧={frameCount} v2解码={v2FrameCount} 模式={modeStr}{fuseStr}{pressureStr}" +
                    $" backlog(T={transportBacklogDiag},D={decoderBacklogDiag}) budgetEx={consecutiveBudgetExceedFrames} pipeEx={consecutivePipelineBacklogFrames}" +
                    $" 最慢帧={worstUpdateMs:F1}ms@{worstUpdatePhase}{freezeInfo}" +
                    $" | {transportDiag} | {srDiag}", wmj.Log.Tag.UI);
                worstUpdateMs = 0;
                worstUpdatePhase = null;
            }

            // ─── 4. 更新敌方基地血量 ───
            UpdateBaseHealth();

            // ─── 5. 护盾闪烁动画 ───
            if (baseBarShield != null && baseBarShield.fillAmount > 0)
            {
                float pulse = 0.4f + 0.2f * Mathf.Sin(Time.time * 4f);
                baseBarShield.color = new Color(
                    BASE_SHIELD_COLOR.r, BASE_SHIELD_COLOR.g,
                    BASE_SHIELD_COLOR.b, pulse);
            }

            // ─── 帧级别卡死检测 ───
            float totalMs = updateStopwatch.ElapsedMilliseconds;
            if (totalMs > 100f)
            {
                totalFreezeFrames++;
                wmj.Log.E($"[LobShotHUD] 🔴 Update 严重卡顿: {totalMs:F1}ms (帧#{Time.frameCount})", wmj.Log.Tag.UI);
            }
            else if (totalMs > 30f)
            {
                wmj.Log.W($"[LobShotHUD] ⚠️ Update 过慢: {totalMs:F1}ms (帧#{Time.frameCount})", wmj.Log.Tag.UI);
            }
            updateStopwatch.Stop();
        }

        /// <summary>记录最慢的阶段名称，用于诊断日志</summary>
        private void RecordPhaseTime(string phase, float phaseMs, float totalMs)
        {
            if (totalMs > worstUpdateMs)
            {
                worstUpdateMs = totalMs;
                worstUpdatePhase = $"{phase}({phaseMs:F1}ms)";
            }
        }

        private void UpdateBaseHealth()
        {
            if (unitVM == null) return;
            uint hp = unitVM.EnemyBaseHealth;
            uint maxHp = 5000;
            float pct = maxHp > 0 ? (float)hp / maxHp : 0f;

            if (baseBarFill != null) baseBarFill.fillAmount = pct;
            if (baseHpText != null) baseHpText.text = $"{hp} / {maxHp}";

            uint outpost = unitVM.EnemyOutpostHealth;
            if (baseBarShield != null)
                baseBarShield.fillAmount = outpost > 0 ? 1f : 0f;
        }

        // ═══════════════════════════════ 辅助 ═══════════════════════════════

        private Image CreateBorderStrip(Transform parent, string name,
            float xMin, float yMin, float xMax, float yMax, Color color)
        {
            var img = UIFactory.CreateImage(parent, name, color);
            img.rectTransform.anchorMin = new Vector2(xMin, yMin);
            img.rectTransform.anchorMax = new Vector2(xMax, yMax);
            img.rectTransform.offsetMin = Vector2.zero;
            img.rectTransform.offsetMax = Vector2.zero;
            return img;
        }

        private Image CreateCornerDot(Transform parent, string name,
            float ax, float ay, float size, Color color)
        {
            var img = UIFactory.CreateImage(parent, name, color);
            img.rectTransform.anchorMin = new Vector2(ax, ay);
            img.rectTransform.anchorMax = new Vector2(ax, ay);
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            img.rectTransform.anchoredPosition = Vector2.zero;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        // ═══════════════════════════════ SR 控制 ═══════════════════════════════

        /// <summary>SR 是否启用</summary>
        public bool SREnabled
        {
            get => srEnabled;
            set
            {
                if (srEnabled == value) return;
                srEnabled = value;
                wmj.Log.I($"[LobShotHUD] SR {(value ? "已启用" : "已禁用")}", wmj.Log.Tag.UI);

                // 切换显示纹理
                if (!srEnabled && visionDisplay != null && decodeTex != null)
                    visionDisplay.texture = decodeTex;
            }
        }

        /// <summary>应用吊射显示配置（支持设置面板实时更新）</summary>
        public void ApplyDisplaySettings(bool stretch, bool useSrWhenStretched)
        {
            stretchToFullHD = stretch;
            SREnabled = useSrWhenStretched;
            UpdateV2DisplayLayout();

            if (!stretchToFullHD && visionDisplay != null && decodeTex != null)
                visionDisplay.texture = decodeTex;

            wmj.Log.I($"[LobShotHUD] 显示设置已更新: 拉伸={stretchToFullHD}, 拉伸用SR={srEnabled}", wmj.Log.Tag.UI);
        }

        /// <summary>切换 SR 开关</summary>
        public void ToggleSR() => SREnabled = !SREnabled;

        // ═══════════════════════════════ 弹道拖影控制 (v3.2.1) ═══════════════════════════════

        /// <summary>
        /// 弹道拖影是否启用。写入时会：
        ///   1. 立即隐藏/显示本地轨迹叠加层（UI 即时生效）
        ///   2. 上行发送 CMD_SET_PARAM(0xF1) param_id=0x05 到发送端，
        ///      发送端收到后停止/恢复打包 TRAIL_FRAME(0x20)，释放/回收带宽
        /// </summary>
        public bool TrailEnabled
        {
            get => trailEnabled;
            set
            {
                if (trailEnabled == value) return;
                trailEnabled = value;
                wmj.Log.I($"[LobShotHUD] 弹道拖影 {(value ? "已启用" : "已禁用")} (客户端+上行命令)", wmj.Log.Tag.UI);

                // 1) 上行命令：通知发送端停止/恢复 TRAIL 打包
                SendTrailEnableCommand(value);

                // 2) 本地 UI 效果：禁用时立即清空叠加层；启用时等待下一帧 TRAIL 到达
                if (!value) HideAllTrailDots();
            }
        }

        /// <summary>切换弹道拖影开关</summary>
        public void ToggleTrail() => TrailEnabled = !TrailEnabled;

        /// <summary>构建并上行 CMD_SET_PARAM / trail_enable 指令</summary>
        private void SendTrailEnableCommand(bool enable)
        {
            SendSetParamCommand(PARAM_ID_TRAIL_ENABLE, new byte[] { (byte)(enable ? 1 : 0) });
        }

        /// <summary>
        /// 上行 CMD_SET_PARAM(0xF1)：通用参数设置接口（支持协议 §4.9 所有 param_id）。
        /// 载荷 = [param_id, param_len, param_val...]；上行裁判系统 0x0311 单帧 ≤ 30B，
        /// 扣除 9B 协议头尾后 param_val ≤ 19B。
        /// </summary>
        public void SendSetParamCommand(byte paramId, byte[] paramVal)
        {
            int valLen = paramVal != null ? paramVal.Length : 0;
            if (valLen > 19)
            {
                wmj.Log.W($"[LobShotHUD] CMD_SET_PARAM param_val 超过上行 19B 限制 (id=0x{paramId:X2}, len={valLen})", wmj.Log.Tag.Network);
                return;
            }
            int payloadLen = 2 + valLen;
            byte[] frame = new byte[7 + payloadLen + 2];
            ushort frameId = (ushort)(Time.frameCount & 0xFFFF);
            frame[0] = 0xA5;
            frame[1] = FT_CMD_SET_PARAM;
            frame[2] = (byte)(frameId & 0xFF);
            frame[3] = (byte)((frameId >> 8) & 0xFF);
            frame[4] = 0x00;
            frame[5] = (byte)(payloadLen & 0xFF);
            frame[6] = (byte)((payloadLen >> 8) & 0xFF);
            frame[7] = paramId;
            frame[8] = (byte)valLen;
            if (valLen > 0) System.Buffer.BlockCopy(paramVal, 0, frame, 9, valLen);
            byte xor = 0;
            int xorEnd = 7 + payloadLen;
            for (int i = 0; i < xorEnd; i++) xor ^= frame[i];
            frame[xorEnd] = xor;
            frame[xorEnd + 1] = 0x5A;
            PublishControlFrame(frame, $"CMD_SET_PARAM id=0x{paramId:X2} len={valLen}");
        }

        /// <summary>
        /// 上行 CMD_REQUEST_I(0xF0)：请求发送端立即补发一个完整 I 帧。
        /// 载荷为空，总帧长 9B，远低于上行 30B 限制。
        /// </summary>
        public void SendRequestIFrameCommand()
        {
            byte[] frame = new byte[9];
            ushort frameId = (ushort)(Time.frameCount & 0xFFFF);
            frame[0] = 0xA5;
            frame[1] = FT_CMD_REQUEST_I;
            frame[2] = (byte)(frameId & 0xFF);
            frame[3] = (byte)((frameId >> 8) & 0xFF);
            frame[4] = 0x00;
            frame[5] = 0x00;
            frame[6] = 0x00;
            byte xor = 0;
            for (int i = 0; i < 7; i++) xor ^= frame[i];
            frame[7] = xor;
            frame[8] = 0x5A;
            PublishControlFrame(frame, "CMD_REQUEST_I");
        }

        private void PublishControlFrame(byte[] frame, string tag)
        {
            try
            {
                var msg = new CustomControl { Data = Google.Protobuf.ByteString.CopyFrom(frame) };
                byte[] payload = Google.Protobuf.MessageExtensions.ToByteArray(msg);
                if (NetworkManager.Instance != null)
                {
                    NetworkManager.Instance.SendMqttMessage(TRAIL_CTRL_TOPIC, payload);
                    wmj.Log.I($"[LobShotHUD] 已发送上行指令: {tag} ({frame.Length}B)", wmj.Log.Tag.Network);
                }
                else
                {
                    wmj.Log.W($"[LobShotHUD] NetworkManager 未就绪，{tag} 未发送", wmj.Log.Tag.Network);
                }
            }
            catch (System.Exception ex)
            {
                wmj.Log.E($"[LobShotHUD] 发送 {tag} 失败: {ex.Message}", wmj.Log.Tag.Network);
            }
        }
    }
}
