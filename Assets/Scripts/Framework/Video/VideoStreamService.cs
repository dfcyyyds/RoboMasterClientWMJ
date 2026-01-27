using System.Collections.Concurrent;
using UnityEngine;
using Framework.Video;
using Framework.Utils;

/// 视频流服务：订阅UDP帧，后台组装/解码，在主线程分发纹理
public class VideoStreamService : MonoBehaviour
{
    public static VideoStreamService Instance { get; private set; }

    // 暴露当前纹理，便于挂载显示
    public Texture2D CurrentTexture { get; private set; }
    // 新纹理事件已弃用（改为视图在Update轮询CurrentTexture）

    // 编辑器可见的传输模式开关
    public enum TransportMode { UdpAnnexB = 0, RtspSkeleton = 1 }
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

    private IVideoTransport transport;
    private IVideoDecoder decoder;
    // 统计
    private int statSlicesIn;
    private int statFramesAssembled;
    private int statFramesDecoded;
    private int statTexturesApplied;
    private float statLastReport;
    [Tooltip("限制主线程纹理上传的频率(帧/秒)，降低卡顿")]
    [SerializeField] private int maxApplyFps = 60;
    [Tooltip("是否启用逐帧日志（大量IO，默认关闭）")]
    [SerializeField] private bool verbosePerFrameLog = false;
    private float lastApplyTime;
    private float startTime;
    private float lastDiagTime;
    // 动态应急：若持续出现 applied≈1 且 decoded 正常，短期取消限频以排查外部限制
    private bool applyOverrideUnlimited;
    private float applyOverrideUntil;
    // 自适应追帧：记录自上次 Apply 以来已解码但未应用的帧数
    private int decodedSinceLastApply;
    // 调参与门控：提升可调性
    [Header("Gate & Catch-up Tuning")]
    [Tooltip("未检测到IDR时的超时放开门控秒数（>0）")]
    [SerializeField] private float gateTimeoutSec = 0.8f;
    [Tooltip("追帧最小积压帧数阈值")]
    [SerializeField] private int backlogMinFrames = 4;
    [Tooltip("追帧阈值分母（effectiveFps/分母）")]
    [SerializeField] private int backlogDivisor = 10;
    [Tooltip("临时取消限频的窗口秒数")]
    [SerializeField] private float overrideWindowSec = 1.5f;
    [Tooltip("每次Update最多消耗解码帧数（防止一次性清空队列导致 applied≈1）")]
    [SerializeField] private int maxDrainPerUpdate = 1;
    // 入场与看门狗控制
    private bool hasEntered;
    private bool gateNotified;
    private int watchdogAttempts;
    private float lastWatchdogSend;

    void Awake()
    {
        if (Instance != null)
        {
            wmj.DebugTools.WriteRunLog("[VideoStreamService] 检测到重复实例，销毁自身", "WARN");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // 参数化主线程解码帧数
        maxDrainPerUpdate = ConfigLoader.config.maxDrainPerUpdate > 0 ? ConfigLoader.config.maxDrainPerUpdate : 1;

        // 若 Inspector 中序列化的 maxApplyFps 过低（被误设为1），强制抬升到合理下限
        if (maxApplyFps < 30)
        {
            int oldFps = maxApplyFps;
            maxApplyFps = 60;
            wmj.DebugTools.WriteRunLog("[VideoStreamService] 检测到过低的纹理上传帧率上限(" + oldFps + "), 已提升至 " + maxApplyFps, "WARN");
        }

        // 选择传输模式
        switch (transportMode)
        {
            case TransportMode.UdpAnnexB:
                // 下调组帧超时以降低卡顿，同时保留适度缓冲
                // 低延迟模式：缩短组帧等待并减少缓冲帧数（牺牲完整性换流畅）
                transport = new UdpAnnexBTransport(timeoutSec: 0.18f, maxBufferedFrames: 24, verbose: false);
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
        string decoderCodec = "hevc";
        if (transportMode == TransportMode.RtspSkeleton)
        {
            if (rtspCodec == RtspCodec.H264) decoderCodec = "h264";
            else if (rtspCodec == RtspCodec.Hevc || rtspCodec == RtspCodec.Auto) decoderCodec = "hevc";
        }
        // 临时将输出改为 rawvideo 以排除 PPM 解析因素；指定固定分辨率 1280x720
        // 低延迟优先：进一步下采样以提升解码吞吐与纹理上传速度
        // 强制HEVC链路：禁用软解回退，不允许切换到H264
        // 参数化解码输出分辨率和队列上限
        int outW = ConfigLoader.config.decoderOutputWidth > 0 ? ConfigLoader.config.decoderOutputWidth : 960;
        int outH = ConfigLoader.config.decoderOutputHeight > 0 ? ConfigLoader.config.decoderOutputHeight : 540;
        int queueLimit = ConfigLoader.config.decoderQueueSize > 0 ? ConfigLoader.config.decoderQueueSize : 6;
        decoder = new FfmpegPipeDecoder(decoderCodec, useRawVideo: true, outputWidth: outW, outputHeight: outH, verboseFrameLogs: false, enableStderrLog: false, useHardwareDecode: true, forceHevc: true);
        if (decoder is FfmpegPipeDecoder ff)
            ff.MaxQueueSize = queueLimit;
        transport.OnAnnexBFrame += OnAnnexBFrame;
        transport.Start();
        DebugLog.Video("[VideoStreamService] 初始化完成，已订阅UDP帧并启动解码循环 (输出=rawvideo 1280x720)");
#if UNITY_EDITOR
    wmj.DebugTools.Info("[VideoStreamService] 初始化完成，已订阅UDP帧并启动解码循环");
    wmj.DebugTools.WriteDebugLog("[VideoStreamService] 初始化完成 (输出=rawvideo 1280x720)", "INFO");
#endif
        wmj.DebugTools.WriteRunLog("[VideoStreamService] 初始化完成 (输出=rawvideo 1280x720)", "INFO");
        startTime = Time.realtimeSinceStartup;
        lastDiagTime = startTime;
        hasEntered = false;
        gateNotified = false;
        watchdogAttempts = 0;
        lastWatchdogSend = startTime;
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
    }

    private void OnAnnexBFrame(byte[] annexB)
    {
        // 传输层已非主线程组帧完成，直接推入解码器
        decoder.Push(annexB);
        statFramesAssembled++;
        DebugLog.Transport("[VideoStreamService] 推送至解码: bytes=" + (annexB?.Length ?? 0));
#if UNITY_EDITOR
    wmj.DebugTools.WriteDebugLog("[VideoStreamService] 推送至解码: bytes=" + (annexB?.Length ?? 0), "DEBUG");
#endif
        if (verbosePerFrameLog)
            wmj.DebugTools.WriteRunLog("[VideoStreamService] 推送至解码", "DEBUG");
    }

    void Update()
    {
        // 主线程：耗尽解码器队列，只应用最新帧，并限制 Apply 频率
        Framework.Video.DecodedFrame latest = null;
        int drainLimit = Mathf.Max(1, maxDrainPerUpdate);
        int drained = 0;
        while (drained < drainLimit && decoder.TryGetFrame(out var f))
        {
            if (f == null || f.Pixels == null) break;
            statFramesDecoded++;
            decodedSinceLastApply++;
            latest = f; // 仅保留最新帧
            drained++;
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
                    wmj.DebugTools.WriteRunLog("[VideoStreamService] 入场完成，解除IDR门控", "INFO");
                }
                else if (!hasEntered)
                {
                    // 超时回退：若启动超过1秒且已产生解码帧但未检测到IDR，临时放开门控避免长期黑屏
                    float sinceStart = Time.realtimeSinceStartup - startTime;
                    if (gateTimeoutSec > 0f && sinceStart >= gateTimeoutSec && statFramesDecoded > 0)
                    {
                        hasEntered = true;
                        wmj.DebugTools.WriteRunLog("[VideoStreamService] 超时放开门控：未检测到IDR但已有解码帧(sinceStart=" + sinceStart.ToString("F2") + ", gateTimeout=" + gateTimeoutSec.ToString("F2") + ")", "WARN");
                    }
                }
            }

            if (!hasEntered)
            {
                if (!gateNotified)
                {
                    wmj.DebugTools.WriteRunLog("[VideoStreamService] IDR未捕获，暂不应用纹理（门控生效）", "WARN");
                    gateNotified = true;
                }
                // 丢弃应用阶段，仅继续解码队列耗尽，以等待首个IDR
                return;
            }

            bool needCreate = CurrentTexture == null || CurrentTexture.width != latest.Width || CurrentTexture.height != latest.Height || CurrentTexture.format != UnityEngine.TextureFormat.RGB24;
            float now = Time.realtimeSinceStartup;
            // 运行时强制下限，避免 Inspector 误设为 1fps 导致 applied=1
            int effectiveFps = Mathf.Max(maxApplyFps, 60);
            // 动态应急：在限定窗口内直接取消限频
            bool overrideNow = applyOverrideUnlimited && now < applyOverrideUntil;
            float minInterval = overrideNow ? 0f : (effectiveFps > 0 ? (1f / Mathf.Clamp(effectiveFps, 1, 120)) : 0f);
            // 自适应追帧：若自上次 Apply 以来积压帧数过多，则本次允许越过限频立即应用
            // 阈值按帧率动态设定，最低为5帧，约等于 ~1/12 秒的预算。
            int denom = Mathf.Max(backlogDivisor, 1);
            int backlogThreshold = Mathf.Max(backlogMinFrames, effectiveFps / denom);
            bool backlogPressure = decodedSinceLastApply >= backlogThreshold;
            if (backlogPressure && !overrideNow)
            {
                // 短期开放更高应用节奏以快速追帧，避免出现 applied≈1 的极端情况
                applyOverrideUnlimited = true;
                applyOverrideUntil = now + Mathf.Max(overrideWindowSec, 0.2f);
                wmj.DebugTools.WriteRunLog("[VideoStreamService] 触发自适应追帧：积压=" + decodedSinceLastApply + ", 阈值=" + backlogThreshold + ", 临时取消限频 " + overrideWindowSec.ToString("F2") + "s", "WARN");
                overrideNow = true;
                minInterval = 0f;
            }
            bool allowApply = needCreate || (minInterval <= 0f) || backlogPressure || (now - lastApplyTime >= minInterval);
            if (allowApply)
            {
                if (needCreate)
                {
                    if (CurrentTexture != null)
                    {
                        UnityEngine.Object.Destroy(CurrentTexture);
                    }
                    CurrentTexture = new UnityEngine.Texture2D(latest.Width, latest.Height, UnityEngine.TextureFormat.RGB24, false);
                    DebugLog.Video($"[VideoStreamService] 新建/调整纹理: {latest.Width}x{latest.Height}");
#if UNITY_EDITOR
                    wmj.DebugTools.Info($"[VideoStreamService] 新建/调整纹理: {latest.Width}x{latest.Height}");
                    wmj.DebugTools.WriteDebugLog("[VideoStreamService] 新建/调整纹理: " + latest.Width + "x" + latest.Height, "INFO");
#endif
                    wmj.DebugTools.WriteRunLog("[VideoStreamService] 新建/调整纹理: " + latest.Width + "x" + latest.Height, "INFO");
                }
                CurrentTexture.LoadRawTextureData(latest.Pixels);
                CurrentTexture.Apply(false, false);
                lastApplyTime = now;
                statTexturesApplied++;
                decodedSinceLastApply = 0; // 已应用，清空积压计数
            }
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
            DebugLog.Video($"[VideoStreamService] 每秒统计 slices={statSlicesIn}, assembled={statFramesAssembled}, decoded={statFramesDecoded}, applied={statTexturesApplied}{st}");
#if UNITY_EDITOR
            wmj.DebugTools.Info($"[VideoStreamService] 每秒统计 slices={statSlicesIn}, assembled={statFramesAssembled}, decoded={statFramesDecoded}, applied={statTexturesApplied}{st}", wmj.DebugTools.LogCategory.Video);
            wmj.DebugTools.WriteDebugLog("[VideoStreamService] 每秒统计 slices=" + statSlicesIn + ", assembled=" + statFramesAssembled + ", decoded=" + statFramesDecoded + ", applied=" + statTexturesApplied + st, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[VideoStreamService] 每秒统计 slices=" + statSlicesIn + ", assembled=" + statFramesAssembled + ", decoded=" + statFramesDecoded + ", applied=" + statTexturesApplied + st, "INFO");
            // 若 decoded 正常而 applied≈1，开启5秒的取消限频以定位外部限制
            if (statFramesDecoded >= 15 && statTexturesApplied <= 1)
            {
                applyOverrideUnlimited = true;
                applyOverrideUntil = Time.realtimeSinceStartup + 5f;
                wmj.DebugTools.WriteRunLog("[VideoStreamService] 检测到 applied≈1（decoded=" + statFramesDecoded + "),短期取消限频以排查外部限制", "WARN");
            }
            statSlicesIn = 0;
            statFramesAssembled = 0;
            statFramesDecoded = 0;
            statTexturesApplied = 0;
            statLastReport = Time.realtimeSinceStartup;
        }

        // 入场诊断：超过2秒仍未解码，输出状态快照
        float nowDiag = Time.realtimeSinceStartup;
        if (nowDiag - startTime >= 2f && statSlicesIn > 0 && statFramesDecoded == 0 && nowDiag - lastDiagTime >= 1f)
        {
            var ff = decoder as FfmpegPipeDecoder;
            if (ff != null)
            {
                var st = ff.GetStats();
                wmj.DebugTools.WriteRunLog("[VideoStreamService][诊断] 未入场: HasParamSets=" + st.HasParameterSets + ", IdrsSeen=" + st.IdrsSeen + ", PushedFrames=" + st.PushedFrames + ", Codec=" + st.Codec, "WARN");
                // 看门狗：未入场时重发参数集以促进入场
                ff.ResendParameterSets();
                wmj.DebugTools.WriteRunLog("[VideoStreamService][看门狗] 重发参数集以促进入场", "WARN");
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
                wmj.DebugTools.WriteRunLog("[VideoStreamService][看门狗] 周期性重发参数集 尝试=" + watchdogAttempts, "WARN");
            }
            // 入场后重置尝试计数
            if (hasEntered && watchdogAttempts > 0)
            {
                wmj.DebugTools.WriteRunLog("[VideoStreamService] 入场后重置看门狗计数（" + watchdogAttempts + ")", "INFO");
                watchdogAttempts = 0;
            }
        }
    }
}
