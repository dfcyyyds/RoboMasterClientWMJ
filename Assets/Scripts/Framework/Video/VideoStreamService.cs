using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Scripting;
using Framework.Video;
using Framework.Boot;

/// 视频流服务：订阅UDP帧，后台组装/解码，在主线程分发纹理
public class VideoStreamService : MonoBehaviour
{
    public static VideoStreamService Instance { get; private set; }

    // 暴露当前纹理，便于挂载显示
    public Texture2D CurrentTexture { get; private set; }
    // 双缓冲：避免在GPU渲染时更新纹理导致撕裂（暂时禁用以排查问题）
    private Texture2D backBuffer;
    private bool useDoubleBuffer = false;  // 暂时禁用双缓冲
    // 新纹理事件已弃用（改为视图在Update轮询CurrentTexture）

    // 编辑器可见的传输模式开关
    public enum TransportMode { UdpAnnexB = 0, RtspSkeleton = 1 }
    public enum DecodeBackend { FfmpegPipe = 0, NativeNvdec = 1 }
    [Tooltip("仿真/适配开关：UDP AnnexB（仿真）或 RTSP 骨架（官方兼容适配入口）")]
    [SerializeField] private TransportMode transportMode = TransportMode.UdpAnnexB;
    [Tooltip("RTSP 源地址（仅在 RtspSkeleton 模式下使用）")]
    [SerializeField] private string rtspUrl = "rtsp://127.0.0.1/test";
    public enum RtspTransportProto { Auto, Tcp, Udp }
    public enum RtspCodec { Auto, Hevc, H264 }
    [Tooltip("RTSP 传输协议（Auto/TCP/UDP）")]
    [SerializeField] private RtspTransportProto rtspTransportProto = RtspTransportProto.Tcp;
    [Tooltip("RTSP 编解码器（Auto/HEVC/H264），用于选择正确的 AnnexB 过滤器")]
    [SerializeField] private RtspCodec rtspCodec = RtspCodec.Auto;
    [Header("Decode Backend")]
    [Tooltip("解码后端：Ffmpeg 管道或原生 NVDEC（可零拷贝输出纹理）")]
    [SerializeField] private DecodeBackend decodeBackend = DecodeBackend.NativeNvdec;

    private IVideoTransport transport;
    private IVideoDecoder decoder;
    // 统计
    private int statSlicesIn;
    private int statFramesAssembled;
    private int statFramesDecoded;
    private int statTexturesApplied;
    private float statLastReport;
    [Tooltip("限制主线程纹理上传的频率(帧/秒)，降低卡顿")]
    [SerializeField] private int maxApplyFps = 120;  // 提升到120fps以消除上传瓶颈
    [Tooltip("是否启用逐帧日志（大量IO，默认关闭）")]
    [SerializeField] private bool verbosePerFrameLog = false;
    private float lastApplyTime;
    private float startTime;
    private float lastDiagTime;
    // 原生解码输出的纹理句柄跟踪
    private int nativeTextureId;
    private int nativeTexWidth;
    private int nativeTexHeight;
    private float nvdecStartTime;
    private float nvdecLastStatLog;
    private bool nvdecFallbackTriggered;
    // 动态应急：若持续出现 applied≈1 且 decoded 正常，短期取消限频以排查外部限制
    private bool applyOverrideUnlimited;
    private float applyOverrideUntil;
    // 自适应追帧：记录自上次 Apply 以来已解码但未应用的帧数
    private int decodedSinceLastApply;
    // 调参与门控：提升可调性
    [Header("Gate & Catch-up Tuning")]
    [Tooltip("未检测到IDR时的超时放开门控秒数（>0）")]
    [SerializeField] private float gateTimeoutSec = 0.8f;
#pragma warning disable CS0414 // Inspector-exposed tuning fields, code-side usage pending
    [Tooltip("追帧最小积压帧数阈值")]
    [SerializeField] private int backlogMinFrames = 2;  // 降低阈值，更早追帧
    [Tooltip("追帧阈值分母（effectiveFps/分母）")]
    [SerializeField] private int backlogDivisor = 10;
    [Tooltip("临时取消限频的窗口秒数")]
    [SerializeField] private float overrideWindowSec = 1.5f;
#pragma warning restore CS0414
    [Tooltip("每次Update最多消耗解码帧数")]
    [SerializeField] private int maxDrainPerUpdate = 16;  // 大幅提升以确保队列不积压
    // 记住解码器配置，便于在运行期切换回退
    private string decoderCodec;
    private int decoderOutW;
    private int decoderOutH;
    private int decoderQueueLimit;
    // 入场与看门狗控制
    private bool hasEntered;
    private bool gateNotified;
    private int watchdogAttempts;
    private float lastWatchdogSend;
    // NVDEC 回退专用：累计已组帧数（不随统计窗口重置）
    private int totalFramesAssembled;
    // 增量GC辅助：每隔一定帧数在Update末尾触发低开销GC
    private int framesSinceLastGcHint;
    private const int GC_HINT_INTERVAL_FRAMES = 60; // 每60帧（约1秒@60fps）发一次GC提示
    private float lastFullGcTime;
    private const float FULL_GC_INTERVAL_SEC = 10f; // 每10秒尝试一次低优先级完整GC

    void Awake()
    {
        if (Instance != null)
        {
            wmj.Log.W("[VideoStreamService] 检测到重复实例，销毁自身", wmj.Log.Tag.Video);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // 参数化主线程解码帧数
        maxDrainPerUpdate = ConfigLoader.config.maxDrainPerUpdate > 0 ? ConfigLoader.config.maxDrainPerUpdate : 1;

        // 根据硬件能力自动选择解码后端
        var detection = HardwareCapabilityDetector.Detect();
        if (decodeBackend == DecodeBackend.NativeNvdec && detection.Accel != HardwareCapabilityDetector.RecommendedAccel.NvdecCuda)
        {
            decodeBackend = DecodeBackend.FfmpegPipe;
            wmj.Log.W("[VideoStreamService] 当前硬件不支持 NVDEC，已自动切换到 ffmpeg 解码", wmj.Log.Tag.Video);
        }

        // 根据配置设置纹理上传上限（避免低配过高负载）
        if (ConfigLoader.config.targetFrameRate > 0)
        {
            maxApplyFps = Mathf.Max(30, ConfigLoader.config.targetFrameRate);
        }

        // 若 Inspector 中序列化的 maxApplyFps 过低（被误设为1），强制抬升到合理下限
        if (maxApplyFps < 30)
        {
            int oldFps = maxApplyFps;
            maxApplyFps = 60;
            wmj.Log.W("[VideoStreamService] 检测到过低的纹理上传帧率上限(" + oldFps + "), 已提升至 " + maxApplyFps, wmj.Log.Tag.Video);
        }

        // 选择传输模式
        switch (transportMode)
        {
            case TransportMode.UdpAnnexB:
                // 适度组帧超时以平衡延迟和完整性
                // 提升容错：给予足够时间接收大帧（2K HEVC 可达 250KB/180分片）
                transport = new UdpAnnexBTransport(timeoutSec: 0.22f, maxBufferedFrames: 24, verbose: false);
                // 统计兼容：仍订阅 UDP 分片事件以记录 slices 指标
                NetworkManager.Instance.OnUdpVideoFrame += (slice) => statSlicesIn++;
                break;
            case TransportMode.RtspSkeleton:
                string proto = rtspTransportProto == RtspTransportProto.Auto ? null : (rtspTransportProto == RtspTransportProto.Tcp ? "tcp" : "udp");
                string codec = rtspCodec == RtspCodec.Auto ? null : (rtspCodec == RtspCodec.Hevc ? "hevc" : "h264");
                transport = new RtspTransport(rtspUrl, proto, codec);
                break;
        }

        // 根据所选传输的编解码器决定解码器输入格式（默认 hevc，遵循官方协议）
        decoderCodec = "hevc";
        if (transportMode == TransportMode.RtspSkeleton)
        {
            if (rtspCodec == RtspCodec.H264) decoderCodec = "h264";
            else if (rtspCodec == RtspCodec.Hevc || rtspCodec == RtspCodec.Auto) decoderCodec = "hevc";
        }
        // 默认使用 2K 分辨率 (2560x1440)，可通过配置文件覆盖
        decoderOutW = ConfigLoader.config.decoderOutputWidth > 0 ? ConfigLoader.config.decoderOutputWidth : 2560;
        decoderOutH = ConfigLoader.config.decoderOutputHeight > 0 ? ConfigLoader.config.decoderOutputHeight : 1440;
        decoderQueueLimit = ConfigLoader.config.decoderQueueSize > 0 ? ConfigLoader.config.decoderQueueSize : 3;

        // 原生 NVDEC 初始化延迟到 Start() 执行，确保 Unity 图形设备完全就绪
        // Awake() 阶段图形设备可能处于 PlayMode 转换中，此时调用 CUDA 会导致 double fault

        if (decodeBackend == DecodeBackend.FfmpegPipe)
        {
            decoder = new FfmpegPipeDecoder(decoderCodec, useRawVideo: true, outputWidth: decoderOutW, outputHeight: decoderOutH, verboseFrameLogs: false, enableStderrLog: false, useHardwareDecode: true, forceHevc: true);
            if (decoder is FfmpegPipeDecoder ff)
                ff.MaxQueueSize = decoderQueueLimit;
        }

        transport.OnAnnexBFrame += OnAnnexBFrame;
        transport.Start();
        wmj.Log.I("[VideoStreamService] 初始化完成，已订阅UDP帧并启动解码循环 (输出=rawvideo 1280x720)", wmj.Log.Tag.Video);
        startTime = Time.realtimeSinceStartup;
        lastDiagTime = startTime;
        hasEntered = false;
        gateNotified = false;
        watchdogAttempts = 0;
        lastWatchdogSend = startTime;
    }

    // 标记 NVDEC 是否已延迟初始化
    private bool nvdecDelayedInitDone = false;

    void Start()
    {
        // 延迟初始化原生 NVDEC：确保 Unity 图形设备在 PlayMode 下完全就绪
        // Awake() 阶段调用 CUDA/Vulkan 初始化会导致 double fault 崩溃
        if (decodeBackend == DecodeBackend.NativeNvdec && NativeVideoBridge.Available && !nvdecDelayedInitDone)
        {
            InitializeNativeNvdec();
        }
    }

    private void InitializeNativeNvdec()
    {
        if (nvdecDelayedInitDone) return;
        nvdecDelayedInitDone = true;

        nativeTexWidth = decoderOutW;
        nativeTexHeight = decoderOutH;
        wmj.Log.I("[VideoStreamService] 正在初始化原生 NVDEC (延迟模式)...", wmj.Log.Tag.Video);

        int init = -99;
        try
        {
            init = NativeVideoBridge.Init(decoderOutW, decoderOutH);
        }
        catch (Exception ex)
        {
            wmj.Log.E("[VideoStreamService] NVDEC init 异常: " + ex.Message, wmj.Log.Tag.Video);
            init = -100;
        }

        if (init != 0)
        {
            wmj.Log.W("[VideoStreamService] Native NVDEC init 失败 (code=" + init + ")，保持原生模式等待重试", wmj.Log.Tag.Video);
            nvdecDelayedInitDone = false; // 允许重试
        }
        else
        {
            wmj.Log.I("[VideoStreamService] Native NVDEC 初始化成功", wmj.Log.Tag.Video);
            nvdecStartTime = Time.realtimeSinceStartup;
            nvdecFallbackTriggered = false;
        }
    }

    private void OnDestroy()
    {
        try
        {
            transport?.Stop();
            transport?.Dispose();
        }
        catch { }
        // 正确释放 ffmpeg 解码器，避免下次运行资源冲突
        (decoder as FfmpegPipeDecoder)?.Dispose();
        decoder = null;
        if (decodeBackend == DecodeBackend.NativeNvdec)
        {
            NativeVideoBridge.Shutdown();
        }
        // 释放双缓冲纹理
        if (CurrentTexture != null)
        {
            UnityEngine.Object.Destroy(CurrentTexture);
            CurrentTexture = null;
        }
        if (backBuffer != null)
        {
            UnityEngine.Object.Destroy(backBuffer);
            backBuffer = null;
        }
    }

    private void OnAnnexBFrame(byte[] annexB)
    {
        if (decodeBackend == DecodeBackend.NativeNvdec && NativeVideoBridge.Available && nvdecDelayedInitDone)
        {
            NativeVideoBridge.Push(annexB, annexB?.Length ?? 0);
            statFramesAssembled++;
            totalFramesAssembled++;
            return;
        }

        // 传输层已非主线程组帧完成，直接推入解码器
        decoder?.Push(annexB);
        statFramesAssembled++;
        if (verbosePerFrameLog)
            wmj.Log.D("[VideoStreamService] 推送至解码: bytes=" + (annexB?.Length ?? 0), wmj.Log.Tag.Video);
    }

    void Update()
    {
        if (decodeBackend == DecodeBackend.NativeNvdec && NativeVideoBridge.Available)
        {
            // 确保延迟初始化已完成
            if (!nvdecDelayedInitDone)
            {
                return; // 等待 Start() 完成初始化
            }

            // 先检查 NVDEC 检测到的实际视频尺寸，如果与当前纹理尺寸不同则需要更新
            if (NativeVideoBridge.TryGetStats(out var sizeStats) && sizeStats.width > 0 && sizeStats.height > 0)
            {
                if (sizeStats.width != nativeTexWidth || sizeStats.height != nativeTexHeight)
                {
                    wmj.Log.I($"[VideoStreamService] 视频尺寸变化: {nativeTexWidth}x{nativeTexHeight} -> {sizeStats.width}x{sizeStats.height}", wmj.Log.Tag.Video);
                    nativeTexWidth = sizeStats.width;
                    nativeTexHeight = sizeStats.height;
                    // 强制重新创建纹理
                    nativeTextureId = 0;
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                        CurrentTexture = null;
                    }
                }
            }

            // 检查是否使用 Vulkan 模式
            bool isVulkan = NativeVideoBridge.IsVulkanEnabled();

            if (isVulkan)
            {
                // Vulkan 模式：使用 VkImage 句柄创建外部纹理
                IntPtr vkImage = NativeVideoBridge.GetVulkanImage();
                if (vkImage != IntPtr.Zero && vkImage.ToInt64() != nativeTextureId)
                {
                    nativeTextureId = (int)vkImage.ToInt64();
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                    }
                    // Vulkan 模式使用 RGBA32 格式
                    CurrentTexture = Texture2D.CreateExternalTexture(nativeTexWidth, nativeTexHeight, TextureFormat.RGBA32, false, false, vkImage);
                    wmj.Log.I("[VideoStreamService] 绑定 Vulkan 纹理: handle=" + vkImage + ", " + nativeTexWidth + "x" + nativeTexHeight, wmj.Log.Tag.Video);
                }
            }
            else
            {
                // OpenGL 模式：使用 GLuint 纹理 ID
                int texId = NativeVideoBridge.GetLatestTextureId();
                if (texId != 0 && texId != nativeTextureId)
                {
                    nativeTextureId = texId;
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                    }
                    CurrentTexture = Texture2D.CreateExternalTexture(nativeTexWidth, nativeTexHeight, TextureFormat.RGB24, false, false, (IntPtr)nativeTextureId);
                    wmj.Log.I("[VideoStreamService] 绑定 OpenGL 纹理: " + nativeTexWidth + "x" + nativeTexHeight + ", id=" + nativeTextureId, wmj.Log.Tag.Video);
                }
            }

            // 定期抓取原生统计，检测无纹理产出场景，尽量避免误回退
            if (NativeVideoBridge.TryGetStats(out var stats) && Time.realtimeSinceStartup - nvdecLastStatLog >= 1f)
            {
                nvdecLastStatLog = Time.realtimeSinceStartup;
                string mode = stats.vulkanEnabled != 0 ? "Vulkan" : "OpenGL";
                wmj.Log.I($"[VideoStreamService][NVDEC] 统计 mode={mode}, tex={stats.tex}, pbo={stats.pbo}, cuPbo={stats.cuPbo}, glReady={stats.glReady}, glFailed={stats.glFailed}, decoded={stats.framesDecoded}, displayed={stats.framesDisplayed}, size={stats.width}x{stats.height}, slices={statSlicesIn}, assembled={statFramesAssembled}", wmj.Log.Tag.Video);
                statSlicesIn = 0;
                statFramesAssembled = 0;
            }

            // 安全回退门限：仅在累计帧数充足且长时间无显示时触发
            if (!nvdecFallbackTriggered && nvdecStartTime > 0f && NativeVideoBridge.TryGetStats(out var st))
            {
                float sinceStart = Time.realtimeSinceStartup - nvdecStartTime;
                // GL 互操作失败快速回退：无需等待 5 秒
                if (sinceStart > 2f && st.glFailed != 0 && st.framesDisplayed <= 0)
                {
                    SwitchToFfmpeg("NVDEC GL 互操作失败 (glFailed=" + st.glFailed + ")，decoded=" + st.framesDecoded);
                    return;
                }
                // 通用回退：解码正常但长时间无纹理产出（使用累计计数器，不受每秒重置影响）
                if (sinceStart > 5f && st.framesDisplayed <= 0 && st.framesDecoded >= 80 && totalFramesAssembled >= 60)
                {
                    SwitchToFfmpeg("NVDEC 长时间未产出纹理，已解码=" + st.framesDecoded + ", 累计组帧=" + totalFramesAssembled);
                    return;
                }
            }
            // 原生路径不需要托管解码队列
            return;
        }

        // 主线程：从解码器队列取帧，正常情况下每帧取一个以保持连贯性
        // 仅在队列积压超过阈值时才跳帧追赶
        Framework.Video.DecodedFrame latest = null;
        int currentQueueSize = 0;
        var ffCheck = decoder as FfmpegPipeDecoder;
        if (ffCheck != null) currentQueueSize = ffCheck.GetQueueCount();

        // 积压判断：队列超过 4 帧认为需要追赶
        const int queueBacklogThreshold = 4;
        bool needCatchUp = currentQueueSize > queueBacklogThreshold;

        if (needCatchUp)
        {
            // 追赶模式：丢弃较旧帧，只保留最新帧
            Framework.Video.DecodedFrame previousLatest = null;
            int drainLimit = Mathf.Max(1, maxDrainPerUpdate);
            int drained = 0;
            while (drained < drainLimit && decoder.TryGetFrame(out var f))
            {
                if (f == null || f.Pixels == null) break;
                statFramesDecoded++;
                decodedSinceLastApply++;
                previousLatest?.ReturnToPool();
                previousLatest = latest;
                latest = f;
                drained++;
            }
            previousLatest?.ReturnToPool();
        }
        else
        {
            // 正常模式：每次只取一帧，保持帧序连贯
            if (decoder.TryGetFrame(out var f))
            {
                if (f != null && f.Pixels != null)
                {
                    statFramesDecoded++;
                    decodedSinceLastApply++;
                    latest = f;
                }
            }
        }

        if (latest != null)
        {
            // 门控：在捕获到首个IDR前，不进行纹理应用，避免黑屏抖动
            var ffGate = decoder as FfmpegPipeDecoder;
            if (ffGate != null)
            {
                var stGate = ffGate.GetStats();
                if (!hasEntered && stGate.IdrsSeen > 0)
                {
                    hasEntered = true;
                    wmj.Log.I("[VideoStreamService] 入场完成，解除IDR门控", wmj.Log.Tag.Video);
                }
                else if (!hasEntered)
                {
                    // 超时回退：若启动超过1秒且已产生解码帧但未检测到IDR，临时放开门控避免长期黑屏
                    float sinceStart = Time.realtimeSinceStartup - startTime;
                    if (gateTimeoutSec > 0f && sinceStart >= gateTimeoutSec && statFramesDecoded > 0)
                    {
                        hasEntered = true;
                        wmj.Log.W("[VideoStreamService] 超时放开门控：未检测到IDR但已有解码帧(sinceStart=" + sinceStart.ToString("F2") + ", gateTimeout=" + gateTimeoutSec.ToString("F2") + ")", wmj.Log.Tag.Video);
                    }
                }
            }

            if (!hasEntered)
            {
                if (!gateNotified)
                {
                    wmj.Log.W("[VideoStreamService] IDR未捕获，暂不应用纹理（门控生效）", wmj.Log.Tag.Video);
                    gateNotified = true;
                }
                // 门控生效时归还帧到池
                latest.ReturnToPool();
                return;
            }

            bool needCreate = CurrentTexture == null || CurrentTexture.width != latest.Width || CurrentTexture.height != latest.Height || CurrentTexture.format != UnityEngine.TextureFormat.RGB24;
            bool needCreateBack = useDoubleBuffer && (backBuffer == null || backBuffer.width != latest.Width || backBuffer.height != latest.Height || backBuffer.format != UnityEngine.TextureFormat.RGB24);
            float now = Time.realtimeSinceStartup;

            // 双缓冲逻辑：写入后台纹理，然后交换
            if (useDoubleBuffer)
            {
                // 确保后台缓冲存在
                if (needCreateBack)
                {
                    if (backBuffer != null)
                    {
                        UnityEngine.Object.Destroy(backBuffer);
                    }
                    backBuffer = new UnityEngine.Texture2D(latest.Width, latest.Height, UnityEngine.TextureFormat.RGB24, false);
                }
                // 确保前台缓冲也存在（首次或尺寸变化）
                if (needCreate)
                {
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                    }
                    CurrentTexture = new UnityEngine.Texture2D(latest.Width, latest.Height, UnityEngine.TextureFormat.RGB24, false);
                    wmj.Log.I($"[VideoStreamService] 新建/调整纹理(双缓冲): {latest.Width}x{latest.Height}", wmj.Log.Tag.Video);
                }
                // 写入后台缓冲
                backBuffer.LoadRawTextureData(latest.Pixels);
                backBuffer.Apply(false, false);
                // 交换前后台缓冲
                var temp = CurrentTexture;
                CurrentTexture = backBuffer;
                backBuffer = temp;
            }
            else
            {
                // 单缓冲逻辑（保留旧代码路径）
                if (needCreate)
                {
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                    }
                    CurrentTexture = new UnityEngine.Texture2D(latest.Width, latest.Height, UnityEngine.TextureFormat.RGB24, false);
                    wmj.Log.I($"[VideoStreamService] 新建/调整纹理: {latest.Width}x{latest.Height}", wmj.Log.Tag.Video);
                }
                CurrentTexture.LoadRawTextureData(latest.Pixels);
                CurrentTexture.Apply(false, false);
            }
            lastApplyTime = now;
            statTexturesApplied++;
            decodedSinceLastApply = 0;
            // 归还帧到ArrayPool
            latest.ReturnToPool();
        }

        // 每秒打印一次统计
        if (Time.realtimeSinceStartup - statLastReport >= 1f)
        {
            // 附加解码器状态快照，帮助定位入场与门控
            var ff = decoder as FfmpegPipeDecoder;
            string st = string.Empty;
            if (ff != null)
            {
                var s = ff.GetStats();
                st = ", IdrsSeen=" + s.IdrsSeen + ", HasParamSets=" + s.HasParameterSets + ", Q=" + ff.GetQueueCount();
            }
            wmj.Log.I($"[VideoStreamService] 每秒统计 slices={statSlicesIn}, assembled={statFramesAssembled}, decoded={statFramesDecoded}, applied={statTexturesApplied}{st}", wmj.Log.Tag.Video);
            // 诊断：若 decoded 远大于 applied，说明可能有队列积压或帧丢失
            if (statFramesDecoded >= 15 && statTexturesApplied <= 1)
            {
                wmj.Log.W("[VideoStreamService] 诊断: decoded=" + statFramesDecoded + " 但 applied=" + statTexturesApplied + "，可能存在队列积压", wmj.Log.Tag.Video);
            }
            statSlicesIn = 0;
            statFramesAssembled = 0;
            statFramesDecoded = 0;
            statTexturesApplied = 0;
            statLastReport = Time.realtimeSinceStartup;
        }

        // 入场诊断：未入场且超过2秒仍未解码，输出状态快照
        float nowDiag = Time.realtimeSinceStartup;
        if (!hasEntered && nowDiag - startTime >= 2f && statSlicesIn > 0 && nowDiag - lastDiagTime >= 1f)
        {
            var ff = decoder as FfmpegPipeDecoder;
            if (ff != null)
            {
                var st = ff.GetStats();
                wmj.Log.W("[VideoStreamService][诊断] 未入场: HasParamSets=" + st.HasParameterSets + ", IdrsSeen=" + st.IdrsSeen + ", PushedFrames=" + st.PushedFrames + ", Codec=" + st.Codec, wmj.Log.Tag.Video);
                // 看门狗：未入场时重发参数集以促进入场
                ff.ResendParameterSets();
                wmj.Log.W("[VideoStreamService][看门狗] 重发参数集以促进入场", wmj.Log.Tag.Video);
            }
            lastDiagTime = nowDiag;
        }

        // 看门狗增强：在未入场窗口内，按固定节流重复重发参数集（最多尝试6次）
        var ffWatch = decoder as FfmpegPipeDecoder;
        if (!hasEntered && ffWatch != null)
        {
            float nowW = Time.realtimeSinceStartup;
            if (watchdogAttempts < 6 && nowW - lastWatchdogSend >= 0.5f)
            {
                ffWatch.ResendParameterSets();
                watchdogAttempts++;
                lastWatchdogSend = nowW;
                wmj.Log.W("[VideoStreamService][看门狗] 周期性重发参数集 尝试=" + watchdogAttempts, wmj.Log.Tag.Video);
            }
            // 入场后重置尝试计数
            if (hasEntered && watchdogAttempts > 0)
            {
                wmj.Log.I("[VideoStreamService] 入场后重置看门狗计数（" + watchdogAttempts + ")", wmj.Log.Tag.Video);
                watchdogAttempts = 0;
            }
        }

        // 增量GC辅助：定期发送GC提示以避免大块内存累积导致突发GC停顿
        framesSinceLastGcHint++;
        if (framesSinceLastGcHint >= GC_HINT_INTERVAL_FRAMES)
        {
            framesSinceLastGcHint = 0;
            // 使用 GarbageCollector 的增量模式提示（Unity 2019.1+）
            // 这会让Unity在下一个合适的时机进行少量GC工作
            if (GarbageCollector.isIncremental)
            {
                // 分配少量时间片给增量GC（不阻塞）
                GarbageCollector.CollectIncremental(1000000); // 1ms
            }
        }

        // 低优先级完整GC：在空闲时段（帧率稳定且无大量解码积压）进行
        float nowGc = Time.realtimeSinceStartup;
        if (nowGc - lastFullGcTime >= FULL_GC_INTERVAL_SEC)
        {
            // 仅在没有解码积压时触发，避免影响实时性
            if (decodedSinceLastApply <= 2)
            {
                lastFullGcTime = nowGc;
                // 触发Generation 0回收（开销最小）
                System.GC.Collect(0, System.GCCollectionMode.Optimized, false);
            }
        }
    }

    private void SwitchToFfmpeg(string reason)
    {
        if (decodeBackend == DecodeBackend.FfmpegPipe)
            return;

        nvdecFallbackTriggered = true;
        wmj.Log.W("[VideoStreamService] 切换到 Ffmpeg 回退：" + reason, wmj.Log.Tag.Video);
        NativeVideoBridge.Shutdown();
        decodeBackend = DecodeBackend.FfmpegPipe;
        // 释放原生纹理引用，防止旧纹理残留
        if (CurrentTexture != null && nativeTextureId != 0)
        {
            UnityEngine.Object.Destroy(CurrentTexture);
            CurrentTexture = null;
            nativeTextureId = 0;
        }

        decoder = new FfmpegPipeDecoder(decoderCodec, useRawVideo: true, outputWidth: decoderOutW, outputHeight: decoderOutH, verboseFrameLogs: false, enableStderrLog: false, useHardwareDecode: true, forceHevc: true);
        if (decoder is FfmpegPipeDecoder ff)
            ff.MaxQueueSize = decoderQueueLimit;

        // 复位入场/门控状态，确保回退链路正常入场
        hasEntered = false;
        gateNotified = false;
        statSlicesIn = 0;
        statFramesAssembled = 0;
        statFramesDecoded = 0;
        statTexturesApplied = 0;
        decodedSinceLastApply = 0;
        startTime = Time.realtimeSinceStartup;
        lastDiagTime = startTime;
    }
}
