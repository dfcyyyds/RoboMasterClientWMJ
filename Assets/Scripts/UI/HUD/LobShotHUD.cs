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
    ///   - 360×540 H.264 解码 → SRGAN 2× 超分 → 720×1080 显示
    ///   - Unity Sentis GPU 推理，LobShot-SRGAN v3 模型 (67K 参数, ~6ms)
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

        // v2 H.264 参数 (360×540 RGB24, 竖屏 2:3)
        private const int V2_TEX_W = 360, V2_TEX_H = 540;
        private const int V2_BYTES_PER_PIXEL = 3; // RGB24

        // SR 输出尺寸 (720×1080, 2× 超分)
        private const int SR_TEX_W = 720, SR_TEX_H = 1080;

        // v1 二值化参数 (192×144 1bit) — 向后兼容
        private const int V1_TEX_W = 192, V1_TEX_H = 144;

        // 显示尺寸(屏幕像素) — 竖屏 2:3 比例，与屏幕等高
        private const int DISPLAY_W = 720, DISPLAY_H = 1080;

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
        private LobShotSuperResolution srModule;
        private bool srEnabled = true;     // SR 开关
        private Texture2D decodeTex;       // 解码纹理 360×540 (SR 输入)
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
        private const byte FT_HEARTBEAT = 0xFE;

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

        public void Initialize()
        {
            unitVM = new GlobalUnitStatusViewModel();
            unitVM.Initialize();

            h264Transport = new LobShotH264Transport();

            BuildCanvas();
            BuildBorder();
            BuildVisionDisplay();
            BuildCrosshairOverlay();
            BuildBaseHealthBar();

            // 订阅 CustomByteBlock 数据
            ProtobufManager.Instance.OnDataUpdated += OnProtoDataUpdated;

            // ─── 预初始化 SR 模块（不再同步 GPU 预热，改用逐帧异步协程）───
            // SR Worker 将在整个 LobShotHUD 生命周期内持久存在，不会随 Show/Hide 销毁重建。
            if (srEnabled)
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

            // ─── H.264 解码器 (360×540) ───
            // 吊射使用软件解码：360×540 分辨率 CPU 解码开销极低，
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

            wmj.Log.I($"[LobShotHUD] H.264 解码器已启动 ({V2_TEX_W}×{V2_TEX_H}, 彩色)", wmj.Log.Tag.UI);

            // ─── 超分辨率模块 ───
            // SR 模块在 Initialize() 中已预创建（持久存在），通常无需重建。
            // 仅在以下两种情况下重新创建：
            //   1. srEnabled 从 false 切换到 true（用户中途启用）
            //   2. 前一次初始化失败（srModule == null）
            if (srEnabled && srModule == null)
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
            frameTimeIndex = 0;
            for (int i = 0; i < recentFrameTimes.Length; i++) recentFrameTimes[i] = 0f;

            if (lobCanvas != null) lobCanvas.gameObject.SetActive(true);
            wmj.Log.I($"[LobShotHUD] 显示吊射画面 (v2 彩色 H.264, SR={srEnabled})", wmj.Log.Tag.UI);
        }

        public void Hide()
        {
            isShowing = false;
            LobShotUdpReceiver.ActiveH264Transport = null;
            DisposeDecoder();

            // 恢复主图传纹理上传
            VideoStreamService.Instance?.ResumeTextureUpload();

            // 排空残留的池化缓冲，归还 ArrayPool
            while (pendingFrames.TryDequeue(out var buf))
                ArrayPool<byte>.Shared.Return(buf.Data);

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

            // ─── 解码纹理 (360×540 RGBA32, SR 输入 / v2 兜底显示) ───
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
            rt.anchorMin = new Vector2(0.5f, 0.48f);
            rt.anchorMax = new Vector2(0.5f, 0.48f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(DISPLAY_W, DISPLAY_H);
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
            if (!isShowing) return;
            if (typeName != "CustomByteBlock") return;

            var block = data as CustomByteBlock;
            if (block == null || block.Data == null) return;
            if (block.Data.Length < 9) return;

            // 使用 ArrayPool 避免每帧 ToByteArray() 的 GC 分配
            int dataLen = block.Data.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(dataLen);
            block.Data.CopyTo(rented, 0);
            pendingFrames.Enqueue(new PooledBuffer { Data = rented, Length = dataLen });

            int fc = System.Threading.Interlocked.Increment(ref frameCount);
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
        ///   RGB24 (360×540) → RGBA32 (Y翻转) → decodeTex → [SR推理 | Bilinear] → 显示
        ///   GPU 压力级别决定 SR 行为：Normal=每帧SR, Light=隔帧SR, Heavy=仅双线性, Critical=跳过
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
            bool srAvailable = srModule != null && srModule.IsReady && srEnabled
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
            // 确保解码纹理尺寸正确 (360×540)
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
                }
            }

            // ─── 1. 处理挂起的协议帧 ───
            float t1 = updateStopwatch.ElapsedMilliseconds;
            ProcessPendingFrames();
            float t2 = updateStopwatch.ElapsedMilliseconds;

            // 时间预算检查：如果协议帧已超预算，跳过解码
            if (t2 > UPDATE_BUDGET_MS)
            {
                RecordPhaseTime("ProcessPendingFrames", t2 - t1, t2);
            }
            else
            {
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
                string freezeInfo = totalFreezeFrames > 0 ? $" 严重卡帧={totalFreezeFrames}" : "";
                wmj.Log.I($"[LobShotHUD] 诊断: 总帧={frameCount} v2解码={v2FrameCount} 模式={modeStr}{fuseStr}{pressureStr}" +
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

        /// <summary>切换 SR 开关</summary>
        public void ToggleSR() => SREnabled = !SREnabled;
    }
}
