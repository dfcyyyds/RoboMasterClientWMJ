using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using UI.ViewModels;
using Framework.Network;
using Framework.Video;

namespace UI.HUD
{
    /// <summary>
    /// 吊射模式 HUD v2 — H.264 彩色视觉画面 + 美术边框 + 准心 + 敌方基地血条
    /// 独立 Canvas, 仅在吊射模式激活时显示
    /// 
    /// v2.2 升级：
    ///   - 400×400 YUV420 彩色 H.264 解码 → RGB24 → Texture2D
    ///   - 编码端预渲染弹道拖影，解码即可见
    ///   - Bilinear 滤波，自然平滑
    ///   - 自动识别 FRAME_TYPE：0x04 走 v2 管线，0x01~0x20 回退 v1 二值管线
    /// 
    /// 布局：
    ///   顶部：敌方基地血条（含护盾）+ 血量数值
    ///   中央：画面 + 美术边框 + 准心叠加
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

        // v2 H.264 参数 (400×400 RGB24)
        private const int V2_TEX_W = 400, V2_TEX_H = 400;
        private const int V2_BYTES_PER_PIXEL = 3; // RGB24

        // v1 二值化参数 (192×144 1bit) — 向后兼容
        private const int V1_TEX_W = 192, V1_TEX_H = 144;

        // 显示尺寸(屏幕像素)
        private const int DISPLAY_W = 1008, DISPLAY_H = 1008; // 正方形显示

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

        // ─── 线程安全帧队列 ───
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> pendingFrames
            = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

        // ─── 数据 ───
        private GlobalUnitStatusViewModel unitVM;
        private bool isShowing;
        private int frameCount;

        // ─── 诊断 ───
        private int diagDecodeReject;
        private int diagDecodeSuccess;
        private float lastDiagLogTime;
        private const float DIAG_LOG_INTERVAL = 3f;

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

            Hide();
        }

        public void Shutdown()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnProtoDataUpdated;
            unitVM?.Dispose();
            DisposeDecoder();
            if (displayTex != null) Destroy(displayTex);
            if (lobCanvas != null) Destroy(lobCanvas.gameObject);
        }

        /// <summary>启动 H.264 解码器（进入吊射模式时）</summary>
        public void StartH264Decoder()
        {
            if (h264Decoder != null) return;

            h264Decoder = new FfmpegPipeDecoder(
                inputCodec: "h264",
                useRawVideo: true,
                outputWidth: V2_TEX_W,
                outputHeight: V2_TEX_H,
                verboseFrameLogs: false,
                enableStderrLog: true,
                useHardwareDecode: true,
                forceHevc: false
            );
            h264Decoder.MaxQueueSize = 4;

            wmj.Log.I("[LobShotHUD] H.264 解码器已启动 (400×400, 彩色)", wmj.Log.Tag.UI);
        }

        /// <summary>释放解码器资源（退出吊射模式时）</summary>
        public void DisposeDecoder()
        {
            if (h264Decoder != null)
            {
                h264Decoder.Dispose();
                h264Decoder = null;
                wmj.Log.I("[LobShotHUD] H.264 解码器已释放", wmj.Log.Tag.UI);
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
            StartH264Decoder();

            // 仿真模式：将 H264Transport 注册到 UdpReceiver，使 UDP 数据直接走 H.264 管线
            LobShotUdpReceiver.ActiveH264Transport = h264Transport;

            if (lobCanvas != null) lobCanvas.gameObject.SetActive(true);
            wmj.Log.I("[LobShotHUD] 显示吊射画面 (v2 彩色 H.264 就绪)", wmj.Log.Tag.UI);
        }

        public void Hide()
        {
            isShowing = false;
            LobShotUdpReceiver.ActiveH264Transport = null; // 断开仿真 UDP → H.264 管线
            DisposeDecoder();
            if (lobCanvas != null) lobCanvas.gameObject.SetActive(false);
            wmj.Log.I($"[LobShotHUD] 隐藏吊射画面 (共接收 {frameCount} 帧, v2解码 {v2FrameCount} 帧)", wmj.Log.Tag.UI);
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

            // 初始创建 v2 尺寸纹理 (400×400 RGBA32)
            displayTex = new Texture2D(V2_TEX_W, V2_TEX_H, TextureFormat.RGBA32, false);
            displayTex.filterMode = FilterMode.Bilinear;
            displayTex.wrapMode = TextureWrapMode.Clamp;

            // 初始化纯黑
            var initPixels = new byte[V2_TEX_W * V2_TEX_H * 4];
            for (int i = 0; i < initPixels.Length; i += 4)
            {
                initPixels[i + 3] = 255; // alpha
            }
            displayTex.SetPixelData(initPixels, 0);
            displayTex.Apply(false);

            // RGBA 展开缓冲
            v2PixelBuf = new byte[V2_TEX_W * V2_TEX_H * 4];

            // v1 兼容缓冲
            v1FrameBuf = new byte[V1_TEX_W * V1_TEX_H / 8];
            v1PixelBuf = new byte[V1_TEX_W * V1_TEX_H * 4];
            v1ThumbBuf = new byte[V1_THUMB_W * V1_THUMB_H / 8];

            // RawImage 居中显示
            var go = new GameObject("VisionDisplay");
            go.transform.SetParent(root, false);
            visionDisplay = go.AddComponent<RawImage>();
            visionDisplay.texture = displayTex;
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

            byte[] raw = block.Data.ToByteArray();
            pendingFrames.Enqueue(raw);

            int fc = System.Threading.Interlocked.Increment(ref frameCount);
            if (fc <= 5)
            {
                string hexHead = "";
                for (int i = 0; i < Mathf.Min(16, raw.Length); i++)
                    hexHead += raw[i].ToString("X2") + " ";
                wmj.Log.I($"[LobShotHUD] 帧 #{fc} 到达, 长度={raw.Length}, 头={hexHead}", wmj.Log.Tag.UI);
            }
            else if (fc % 50 == 0)
            {
                wmj.Log.D($"[LobShotHUD] 已收到 {fc} 帧", wmj.Log.Tag.UI);
            }
        }

        // ═══════════════════════════════ 帧分发 (v1/v2 自动识别) ═══════════════════════════════

        private void ProcessPendingFrames()
        {
            int decoded = 0;
            while (pendingFrames.TryDequeue(out byte[] packet))
            {
                decoded++;
                if (decoded > 64) break; // 每帧上限

                // 首先尝试 v2 H.264 管线
                if (h264Transport != null && h264Transport.ProcessProtocolPacket(packet))
                {
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
                DecodeV1Frame(packet);
            }
        }

        /// <summary>从 H.264 传输层取出重组完成的帧，推给解码器，取出解码结果更新纹理</summary>
        private void DrainH264Pipeline()
        {
            if (h264Transport == null || h264Decoder == null) return;

            // 取出重组好的 AnnexB 帧 → 推给解码器
            int pushed = 0;
            while (h264Transport.TryGetFrame(out var h264Frame) && pushed < 8)
            {
                h264Decoder.Push(h264Frame.AnnexBData);
                pushed++;
            }

            // 取出解码结果 → 更新纹理
            while (h264Decoder.TryGetFrame(out DecodedFrame decoded))
            {
                // 首次成功解码时自动切换到 v2 彩色模式
                if (!isV2Mode)
                {
                    isV2Mode = true;
                    SwitchToV2Texture();
                    wmj.Log.I("[LobShotHUD] 首帧 H.264 解码成功, 切换到 v2 彩色管线", wmj.Log.Tag.UI);
                }
                ApplyDecodedFrame(decoded);
                decoded.ReturnToPool();
            }
        }

        /// <summary>将解码后的 RGB24 帧数据写入纹理</summary>
        private void ApplyDecodedFrame(DecodedFrame frame)
        {
            if (frame.Pixels == null || frame.Width != V2_TEX_W || frame.Height != V2_TEX_H)
            {
                if (frame.Pixels != null)
                    wmj.Log.W($"[LobShotHUD] 解码帧尺寸不匹配: {frame.Width}×{frame.Height}, 期望 {V2_TEX_W}×{V2_TEX_H}", wmj.Log.Tag.UI);
                return;
            }

            int pixelCount = V2_TEX_W * V2_TEX_H;
            int rgbSize = pixelCount * V2_BYTES_PER_PIXEL;

            // RGB24 → RGBA32 (翻转 Y 轴, Unity 纹理 Y=0 在底部)
            for (int y = 0; y < V2_TEX_H; y++)
            {
                int srcY = V2_TEX_H - 1 - y;
                for (int x = 0; x < V2_TEX_W; x++)
                {
                    int srcIdx = (srcY * V2_TEX_W + x) * 3;
                    int dstIdx = (y * V2_TEX_W + x) * 4;

                    if (srcIdx + 2 < rgbSize)
                    {
                        v2PixelBuf[dstIdx] = frame.Pixels[srcIdx];       // R
                        v2PixelBuf[dstIdx + 1] = frame.Pixels[srcIdx + 1]; // G
                        v2PixelBuf[dstIdx + 2] = frame.Pixels[srcIdx + 2]; // B
                        v2PixelBuf[dstIdx + 3] = 255;                       // A
                    }
                }
            }

            displayTex.SetPixelData(v2PixelBuf, 0);
            displayTex.Apply(false);
            v2FrameCount++;
        }

        // ═══════════════════════════════ 纹理模式切换 ═══════════════════════════════

        private void SwitchToV2Texture()
        {
            if (displayTex.width != V2_TEX_W || displayTex.height != V2_TEX_H)
            {
                Destroy(displayTex);
                displayTex = new Texture2D(V2_TEX_W, V2_TEX_H, TextureFormat.RGBA32, false);
                displayTex.filterMode = FilterMode.Bilinear;
                displayTex.wrapMode = TextureWrapMode.Clamp;
                visionDisplay.texture = displayTex;
            }
        }

        private void SwitchToV1Texture()
        {
            if (displayTex.width != V1_TEX_W || displayTex.height != V1_TEX_H)
            {
                Destroy(displayTex);
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

            if (displayTex.width != V1_TEX_W) SwitchToV1Texture();
            displayTex.SetPixelData(v1PixelBuf, 0);
            displayTex.Apply(false);
        }

        // ═══════════════════════════════ Update ═══════════════════════════════

        void Update()
        {
            if (!isShowing) return;

            // 1. 处理挂起的协议帧（分发到 v1/v2 管线）
            ProcessPendingFrames();

            // 2. v2 管线：驱动 H.264 解码并更新纹理
            //    无条件调用 — 仿真模式下数据通过 UdpReceiver → H264Transport.ProcessSimUdpPacket
            //    直接进入 transport 队列，绕过 pendingFrames，故不能依赖 isV2Mode 门控
            DrainH264Pipeline();

            // 3. 诊断日志
            if (Time.realtimeSinceStartup - lastDiagLogTime > DIAG_LOG_INTERVAL)
            {
                lastDiagLogTime = Time.realtimeSinceStartup;
                string transportDiag = h264Transport?.GetDiagnostics() ?? "N/A";
                string modeStr = isV2Mode ? "v2_H264" : "v1_binary";
                wmj.Log.I($"[LobShotHUD] 诊断: 总帧={frameCount} v2解码={v2FrameCount} 模式={modeStr} | {transportDiag}", wmj.Log.Tag.UI);
            }

            // 4. 更新敌方基地血量
            UpdateBaseHealth();

            // 5. 护盾闪烁动画
            if (baseBarShield != null && baseBarShield.fillAmount > 0)
            {
                float pulse = 0.4f + 0.2f * Mathf.Sin(Time.time * 4f);
                baseBarShield.color = new Color(
                    BASE_SHIELD_COLOR.r, BASE_SHIELD_COLOR.g,
                    BASE_SHIELD_COLOR.b, pulse);
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
    }
}
