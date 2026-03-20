using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UI.Core;
using UI.ViewModels;
using Framework.Network;

namespace UI.HUD
{
    /// <summary>
    /// 吊射模式 HUD — 二值化视觉画面 + 美术边框 + 敌方基地血条
    /// 独立 Canvas, 仅在吊射模式激活时显示
    /// 
    /// 布局：
    ///   顶部：敌方基地血条（含护盾）+ 血量数值
    ///   中央：768×576 二值化视觉画面 + 美术边框
    ///   画面内：轨迹叠加渲染
    /// </summary>
    public class LobShotHUD : MonoBehaviour
    {
        // ─── UI 元素 ───
        private Canvas lobCanvas;
        private CanvasGroup canvasGroup;

        // 美术边框
        private Image borderTop, borderBottom, borderLeft, borderRight;
        private Image cornerTL, cornerTR, cornerBL, cornerBR;

        // 二值化画面
        private RawImage visionDisplay;
        private Texture2D displayTex;
        private const int TEX_W = 192, TEX_H = 144;
        private const int DISPLAY_W = 1344, DISPLAY_H = 1008;

        // 基地血条
        private RectTransform baseBarRoot;
        private Image baseBarBg, baseBarFill, baseBarShield;
        private TextMeshProUGUI baseHpText, baseTitleText;

        // 帧缓冲
        private byte[] frameBuf = new byte[TEX_W * TEX_H / 8]; // 3456B
        private byte[] pixelBuf = new byte[TEX_W * TEX_H * 4]; // RGBA展开

        // 渐进I帧缓冲 (96×72 缩略帧，§5.5)
        private const int THUMB_W = 96, THUMB_H = 72;
        private byte[] thumbBuf = new byte[THUMB_W * THUMB_H / 8]; // 864B
        private ushort lastPart1FrameId;

        // 轨迹元数据(从服务端包解析)
        private struct TrailPoint { public int x, y; }
        private TrailPoint[] trailPoints = new TrailPoint[120];
        private int trailCount;
        private bool ballDetected;
        private int ballCX, ballCY, ballRadius;

        // ─── 轨迹叠加算法 ───
        // 客户端跨 I 帧周期累积整次发射的轨迹，检测到新发射时清除
        private TrailPoint[] accTrail = new TrailPoint[4096];
        private int accTrailCount;
        private int accTrailNewStart;            // 本批次新增点在 accTrail 的起始索引
        private bool prevBallState;              // 上一帧弹丸检测状态
        private float lastBallDetectTime = -10f; // 上次检测到弹丸的时刻
        private int lastServerTrailCount;        // 上一帧服务端轨迹点数(增量检测用)
        private const float SHOT_GAP_SEC = 1.5f; // 超过此间隔无弹丸 → 本轮发射结束
        private int consecutiveNoBall;           // 连续无弹丸帧计数
        private const int MIN_TRAIL_TO_KEEP = 3; // 轨迹点少于此数时不视为有效轨迹

        // 客户端帧差弹丸检测
        private byte[] prevFrameBuf;             // 上一帧的 frameBuf 副本
        private bool clientBallDetected;
        private int clientBallCX, clientBallCY;
        private int prevClientCX = -1, prevClientCY = -1; // 上一帧客户端检测质心
        private int clientConfirmCount;           // 连续有效检测帧计数

        // 诊断统计
        private int diagIFrames, diagDFrames, diagDEmpty, diagTrailFrames;
        private int diagBallDetected, diagAccPoints, diagClientBall;
        private float lastDiagLogTime;
        private const float DIAG_LOG_INTERVAL = 3f;

        // 颜色渲染常量
        // 靶标屏幕坐标(与服务端3D投影参数匹配: cx≈101, cy≈75)
        private const int TARGET_CX = 101, TARGET_CY = 75, TARGET_R = 6;
        private const int COLORFUL_TAIL = 16; // 末尾N个轨迹点保证彩色渐变

        // 客户端帧差弹丸检测阈值 (与§8.1 服务端一致)
        private const int BALL_MIN_PIXELS = 4;
        private const int BALL_MAX_PIXELS = 50;
        private const int BALL_MIN_AREA = 4;
        private const int BALL_MAX_AREA = 400;
        private const int BALL_MAX_DISP = 20; // 质心位移上限(像素)
        private const int BALL_CONFIRM_FRAMES = 2; // 连续N帧有效才确认

        // 帧类型常量 (§4.3 严格按规范)
        private const byte FT_I_PART1 = 0x01;
        private const byte FT_I_PART2 = 0x02;
        private const byte FT_I_SINGLE = 0x03;
        private const byte FT_D_FRAME = 0x10;
        private const byte FT_D_EMPTY = 0x11;
        private const byte FT_TRAIL = 0x20;
        private const byte FT_HEARTBEAT = 0xFE;

        // 块常量
        private const int BLOCKS_X = 24; // 192/8
        private const int BLOCKS_Y = 18; // 144/8

        // ─── 线程安全帧队列 ───
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> pendingFrames
            = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

        // ─── 数据 ───
        private GlobalUnitStatusViewModel unitVM;
        private bool isShowing;
        private int frameCount; // 诊断：已接收帧计数

        // ─── 颜色常量 ───
        private static readonly Color BORDER_COLOR = new Color(0.08f, 0.72f, 0.55f, 0.85f);
        private static readonly Color BORDER_GLOW = new Color(0.05f, 0.95f, 0.70f, 0.30f);
        private static readonly Color BASE_HP_COLOR = new Color(0.9f, 0.15f, 0.1f, 1f);
        private static readonly Color BASE_SHIELD_COLOR = new Color(0.2f, 0.6f, 1f, 0.6f);

        public void Initialize()
        {
            unitVM = new GlobalUnitStatusViewModel();
            unitVM.Initialize();

            BuildCanvas();
            BuildBorder();
            BuildVisionDisplay();
            BuildBaseHealthBar();

            // 订阅 CustomByteBlock 数据
            ProtobufManager.Instance.OnDataUpdated += OnProtoDataUpdated;

            Hide();
        }

        public void Shutdown()
        {
            ProtobufManager.Instance.OnDataUpdated -= OnProtoDataUpdated;
            unitVM?.Dispose();
            if (displayTex != null) Destroy(displayTex);
            if (lobCanvas != null) Destroy(lobCanvas.gameObject);
        }

        // ═══════════════════════════════ 显隐 ═══════════════════════════════

        public void Show()
        {
            isShowing = true;
            frameCount = 0;
            // 进入吊射模式时重置累积轨迹
            accTrailCount = 0;
            accTrailNewStart = 0;
            lastServerTrailCount = 0;
            prevBallState = false;
            lastBallDetectTime = -10f;
            consecutiveNoBall = 0;
            clientBallDetected = false;
            prevClientCX = -1; prevClientCY = -1;
            clientConfirmCount = 0;
            if (prevFrameBuf == null) prevFrameBuf = new byte[TEX_W * TEX_H / 8];
            System.Array.Copy(frameBuf, prevFrameBuf, frameBuf.Length);
            diagIFrames = diagDFrames = diagDEmpty = diagTrailFrames = 0;
            diagBallDetected = diagAccPoints = diagClientBall = 0;
            lastDiagLogTime = Time.realtimeSinceStartup;
            if (lobCanvas != null) lobCanvas.gameObject.SetActive(true);
            wmj.Log.I("[LobShotHUD] 显示吊射画面", wmj.Log.Tag.UI);
        }

        public void Hide()
        {
            isShowing = false;
            if (lobCanvas != null) lobCanvas.gameObject.SetActive(false);
            wmj.Log.I($"[LobShotHUD] 隐藏吊射画面 (共接收 {frameCount} 帧)", wmj.Log.Tag.UI);
        }

        // ═══════════════════════════════ 构建 ═══════════════════════════════

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
            float bw = 4f; // 边框宽度

            // 全屏不透明黑色背景(遮盖图传画面)
            var tint = UIFactory.CreateImage(root, "VisionTint",
                new Color(0f, 0f, 0f, 1f));
            var tintRt = tint.rectTransform;
            tintRt.anchorMin = Vector2.zero;
            tintRt.anchorMax = Vector2.one;
            tintRt.offsetMin = Vector2.zero;
            tintRt.offsetMax = Vector2.zero;

            // 中央画面区域参考(屏幕居中)
            float cx = 0.5f, cy = 0.48f;
            float hw = DISPLAY_W / 1920f / 2f;
            float hh = DISPLAY_H / 1080f / 2f;
            float marginPx = 6f;
            float mw = marginPx / 1920f, mh = marginPx / 1080f;

            // 四边边框
            borderTop = CreateBorderStrip(root, "BorderTop",
                cx - hw - mw, cy + hh, cx + hw + mw, cy + hh + bw / 1080f, BORDER_COLOR);
            borderBottom = CreateBorderStrip(root, "BorderBot",
                cx - hw - mw, cy - hh - bw / 1080f, cx + hw + mw, cy - hh, BORDER_COLOR);
            borderLeft = CreateBorderStrip(root, "BorderL",
                cx - hw - mw, cy - hh, cx - hw - mw + bw / 1920f, cy + hh, BORDER_COLOR);
            borderRight = CreateBorderStrip(root, "BorderR",
                cx + hw + mw - bw / 1920f, cy - hh, cx + hw + mw, cy + hh, BORDER_COLOR);

            // 四角装饰(亮色方块)
            float cs = 12f;
            cornerTL = CreateCornerDot(root, "CornerTL", cx - hw - mw, cy + hh, cs, BORDER_GLOW);
            cornerTR = CreateCornerDot(root, "CornerTR", cx + hw + mw, cy + hh, cs, BORDER_GLOW);
            cornerBL = CreateCornerDot(root, "CornerBL", cx - hw - mw, cy - hh, cs, BORDER_GLOW);
            cornerBR = CreateCornerDot(root, "CornerBR", cx + hw + mw, cy - hh, cs, BORDER_GLOW);
        }

        private void BuildVisionDisplay()
        {
            var root = lobCanvas.transform;

            // 创建纹理(RGBA32支持彩色轨迹渲染)
            displayTex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false);
            displayTex.filterMode = FilterMode.Point;
            displayTex.wrapMode = TextureWrapMode.Clamp;

            // 初始化为纯黑(Unity默认灰色，不便于诊断)
            var blackPixels = new byte[TEX_W * TEX_H * 4];
            for (int i = 0; i < blackPixels.Length; i += 4)
            {
                blackPixels[i] = 0; blackPixels[i + 1] = 0;
                blackPixels[i + 2] = 0; blackPixels[i + 3] = 255;
            }
            displayTex.SetPixelData(blackPixels, 0);
            displayTex.Apply(false);

            // RawImage 居中显示
            var go = new GameObject("VisionDisplay");
            go.transform.SetParent(root, false);
            visionDisplay = go.AddComponent<RawImage>();
            visionDisplay.texture = displayTex;
            visionDisplay.color = Color.white; // 纹理自带颜色，不需要tint

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.48f);
            rt.anchorMax = new Vector2(0.5f, 0.48f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(DISPLAY_W, DISPLAY_H);
        }

        private void BuildBaseHealthBar()
        {
            var root = lobCanvas.transform;

            // 根容器(画面正上方)
            var barGO = new GameObject("BaseBar");
            barGO.transform.SetParent(root, false);
            baseBarRoot = barGO.AddComponent<RectTransform>();
            baseBarRoot.anchorMin = new Vector2(0.5f, 0.48f + DISPLAY_H / 1080f / 2f + 0.025f);
            baseBarRoot.anchorMax = baseBarRoot.anchorMin;
            baseBarRoot.pivot = new Vector2(0.5f, 0.5f);
            baseBarRoot.anchoredPosition = Vector2.zero;
            baseBarRoot.sizeDelta = new Vector2(DISPLAY_W, 40);

            // 标题
            baseTitleText = UIFactory.CreateText(baseBarRoot, "Title",
                "[ ENEMY BASE ]", 16, TextAlignmentOptions.Center,
                BORDER_COLOR, FontStyles.Bold);
            baseTitleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            baseTitleText.rectTransform.anchorMax = new Vector2(1f, 1.8f);
            baseTitleText.rectTransform.offsetMin = Vector2.zero;
            baseTitleText.rectTransform.offsetMax = Vector2.zero;

            // 血条背景
            baseBarBg = UIFactory.CreateImage(baseBarRoot, "BarBg",
                new Color(0.08f, 0.08f, 0.12f, 0.85f));
            UIFactory.ApplyRoundedCorners(baseBarBg, 64, 8);
            baseBarBg.rectTransform.anchorMin = Vector2.zero;
            baseBarBg.rectTransform.anchorMax = Vector2.one;
            baseBarBg.rectTransform.offsetMin = new Vector2(40, 2);
            baseBarBg.rectTransform.offsetMax = new Vector2(-40, -2);

            // 血条填充
            baseBarFill = UIFactory.CreateImage(baseBarBg.transform, "Fill", BASE_HP_COLOR);
            UIFactory.ApplyRoundedCorners(baseBarFill, 64, 8);
            baseBarFill.rectTransform.anchorMin = Vector2.zero;
            baseBarFill.rectTransform.anchorMax = Vector2.one;
            baseBarFill.rectTransform.offsetMin = new Vector2(2, 2);
            baseBarFill.rectTransform.offsetMax = new Vector2(-2, -2);
            baseBarFill.type = Image.Type.Filled;
            baseBarFill.fillMethod = Image.FillMethod.Horizontal;
            baseBarFill.fillOrigin = 0;

            // 护盾层(叠加在血条上方)
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
            // 护盾闪烁动画在Update中处理

            // 血量数值(居中叠加)
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
            if (block == null || block.Data == null)
            {
                wmj.Log.W("[LobShotHUD] 收到 CustomByteBlock 但 Data 为空", wmj.Log.Tag.UI);
                return;
            }
            if (block.Data.Length < 9)
            {
                wmj.Log.W($"[LobShotHUD] 收到 CustomByteBlock 但数据太短: {block.Data.Length} 字节", wmj.Log.Tag.UI);
                return;
            }

            // 入队而非覆盖，确保D帧XOR增量不丢失
            byte[] raw = block.Data.ToByteArray();
            pendingFrames.Enqueue(raw);

            // 诊断日志 — 前5帧用Info级别(Release可见)，之后每50帧输出一次
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

        // 诊断：DecodeFrame 拒绝计数
        private int diagDecodeReject;
        private int diagDecodeSuccess;

        private void DecodeFrame(byte[] packet)
        {
            // 基本校验 (包头至少9字节: sync+type+fid*2+frag+plen*2 + xor + end)
            if (packet.Length < 9)
            {
                if (diagDecodeReject++ < 5)
                    wmj.Log.W($"[LobShotHUD] DecodeFrame: 包太短 ({packet.Length} 字节)", wmj.Log.Tag.UI);
                return;
            }
            if (packet[0] != 0xA5)
            {
                if (diagDecodeReject++ < 5)
                {
                    string hex = "";
                    for (int i = 0; i < Mathf.Min(8, packet.Length); i++)
                        hex += packet[i].ToString("X2") + " ";
                    wmj.Log.W($"[LobShotHUD] DecodeFrame: 首字节非 0xA5, 前8字节=[{hex}], 总长={packet.Length}", wmj.Log.Tag.UI);
                }
                return;
            }
            if (packet[packet.Length - 1] != 0x5A)
            {
                if (diagDecodeReject++ < 5)
                    wmj.Log.W($"[LobShotHUD] DecodeFrame: 尾字节非 0x5A (实际=0x{packet[packet.Length - 1]:X2})", wmj.Log.Tag.UI);
                return;
            }

            // XOR校验
            byte xor = 0;
            for (int i = 0; i < packet.Length - 2; i++) xor ^= packet[i];
            if (xor != packet[packet.Length - 2])
            {
                if (diagDecodeReject++ < 5)
                    wmj.Log.W($"[LobShotHUD] DecodeFrame: XOR校验失败 (计算=0x{xor:X2}, 期望=0x{packet[packet.Length - 2]:X2})", wmj.Log.Tag.UI);
                return;
            }

            if (diagDecodeSuccess++ < 3)
                wmj.Log.I($"[LobShotHUD] DecodeFrame: 帧解码成功 type=0x{packet[1]:X2} len={packet.Length}", wmj.Log.Tag.UI);

            byte frameType = packet[1];
            // payload_len: 2字节 uint16 LE (偏移5-6)
            int payloadLen = packet[5] | (packet[6] << 8);
            int payloadStart = 7;

            switch (frameType)
            {
                case FT_I_PART1: // 0x01 — 渐进I帧第1包 (96×72 缩略帧 RLE)
                    DecodeIFramePart1(packet, payloadStart, payloadLen);
                    break;

                case FT_I_PART2: // 0x02 — 渐进I帧第2包 (上采样差分补丁)
                    DecodeIFramePart2(packet, payloadStart, payloadLen);
                    break;

                case FT_I_SINGLE: // 0x03 — 单包I帧 (bg_color + 块跳过, §4.5.3)
                    DecodeIFrameSingle(packet, payloadStart, payloadLen);
                    break;

                case FT_D_FRAME: // 0x10 — XOR差分块 + 弹丸 + 轨迹
                    DecodeDFrame(packet, payloadStart, payloadLen);
                    break;

                case FT_D_EMPTY: // 0x11 — 无变化, 保持当前帧
                    // 不修改frameBuf; D_EMPTY表示无弹丸检测, 清除弹丸状态
                    ballDetected = false;
                    diagDEmpty++;
                    HandleShotDetection(false);
                    UpdateTexture();
                    break;

                case FT_TRAIL: // 0x20 — 独立轨迹帧
                    DecodeTrailFrame(packet, payloadStart, payloadLen);
                    break;

                case FT_HEARTBEAT: // 0xFE — 心跳, 忽略
                    break;

                default:
                    // 未知帧类型, 忽略
                    break;
            }
        }

        /// <summary>
        /// 解码单包I帧 (§4.5.3 块跳过格式)
        /// 载荷: width_div4(1B) + height_div4(1B) + bg_color(1B) + mixed_count(1B) + block_entries
        /// 混合块条目: bx(1B) + by(1B) + rle_len(1B) + data(变长)
        ///   rle_len==0: 全翻转(纯色但非背景色)
        ///   rle_len&0x80: 原始8字节  rle_len&0x7F=数据长度
        ///   其他: 块内RLE编码  rle_len=RLE字节数
        /// </summary>
        private void DecodeIFrameSingle(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 4) return; // width_div4 + height_div4 + bg_color + mixed_count
            byte bgColor = packet[payloadStart + 2];
            int mixedCount = packet[payloadStart + 3];

            // 用背景色填充整个帧缓冲
            byte bgFill = (bgColor == 0xFF) ? (byte)0xFF : (byte)0x00;
            for (int i = 0; i < frameBuf.Length; i++) frameBuf[i] = bgFill;

            // 解码混合块列表（覆盖写入, 非XOR）
            int pos = payloadStart + 4;
            int packetEnd = packet.Length - 2;
            DecodeBlockEntries(packet, pos, mixedCount, packetEnd, false, bgFill);

            // I帧开启新周期：服务端重置轨迹计数，但客户端累积轨迹跨I帧保持
            lastServerTrailCount = 0;
            diagIFrames++;

            if (prevFrameBuf != null)
                System.Array.Copy(frameBuf, prevFrameBuf, frameBuf.Length);

            UpdateTexture();
        }

        /// <summary>
        /// 解码渐进I帧第1包 (§4.5.1 缩略帧RLE)
        /// 载荷: width_div4(1B) + height_div4(1B) + bg_color(1B) + rle_data(变长)
        /// 解码96×72缩略帧，最近邻上采样到192×144
        /// </summary>
        private void DecodeIFramePart1(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 3) return;
            byte bgColor = packet[payloadStart + 2];
            int rleDataLen = payloadLen - 3;

            // RLE解码到缩略帧缓冲 (96×72)
            DecodeRLEToBuf(packet, payloadStart + 3, rleDataLen, bgColor,
                           thumbBuf, THUMB_W, THUMB_H);

            // 最近邻上采样 96×72 → 192×144
            UpsampleNN(thumbBuf, THUMB_W, THUMB_H, frameBuf, TEX_W, TEX_H);

            // 记录frame_id，供Part2关联
            lastPart1FrameId = (ushort)(packet[2] | (packet[3] << 8));

            // I帧开启新周期：服务端重置轨迹计数，但客户端累积轨迹跨I帧保持
            lastServerTrailCount = 0;
            diagIFrames++;

            if (prevFrameBuf != null)
                System.Array.Copy(frameBuf, prevFrameBuf, frameBuf.Length);

            UpdateTexture();
        }

        /// <summary>
        /// 解码渐进I帧第2包 (§4.5.2 上采样差分补丁)
        /// 载荷: ref_frame_id(2B) + mixed_count(1B) + block_entries
        /// 块数据为上采样缩略帧与原帧的XOR差分，需XOR应用到frameBuf
        /// </summary>
        private void DecodeIFramePart2(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 3) return;
            int pos = payloadStart;
            int packetEnd = packet.Length - 2;

            // ref_frame_id (2字节, 用于关联Part1, 暂不做严格校验)
            pos += 2;
            int mixedCount = packet[pos++];

            // Part2的块数据是XOR补丁，需要异或到frameBuf上
            DecodeBlockEntries(packet, pos, mixedCount, packetEnd, true, 0x00);

            UpdateTexture();
        }

        /// <summary>解码D帧(XOR差分块应用到frameBuf)</summary>
        private void DecodeDFrame(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 1) return;
            int pos = payloadStart;
            int packetEnd = packet.Length - 2; // 排除 XOR + 0x5A

            // 差异块段
            int blockCount = packet[pos++];
            for (int i = 0; i < blockCount && pos + 10 <= packetEnd; i++)
            {
                int bx = packet[pos++];
                int by = packet[pos++];
                // 读取8字节XOR数据并应用到frameBuf
                for (int row = 0; row < 8; row++)
                {
                    int y = by * 8 + row;
                    int byteOffset = y * (TEX_W / 8) + bx;
                    if (byteOffset < frameBuf.Length)
                        frameBuf[byteOffset] ^= packet[pos + row];
                }
                pos += 8;
            }

            // 弹丸坐标
            if (pos + 4 <= packetEnd)
            {
                ballDetected = packet[pos] != 0; pos++;
                ballCX = packet[pos++];
                ballCY = packet[pos++];
                ballRadius = packet[pos++];
            }
            else
            {
                ballDetected = false;
            }

            // 轨迹点
            if (pos + 1 <= packetEnd)
            {
                int rawCount = packet[pos++];
                int maxPts = Mathf.Min(rawCount, 120);
                for (int i = 0; i < maxPts; i++)
                {
                    if (pos + 1 >= packetEnd) { maxPts = i; break; }
                    trailPoints[i].x = packet[pos++];
                    trailPoints[i].y = packet[pos++];
                }
                trailCount = maxPts;
            }

            // 客户端帧差弹丸检测 (不依赖服务端 ball_detected)
            ClientDiffDetectBall();

            // 融合：服务端或客户端任一检测到弹丸即累积
            bool anyBall = ballDetected || clientBallDetected;
            int useCX = ballDetected ? ballCX : clientBallCX;
            int useCY = ballDetected ? ballCY : clientBallCY;

            // 检测新一轮发射 + 累积轨迹
            HandleShotDetection(anyBall);
            if (anyBall) AccumulateBallXY(useCX, useCY);
            // 也接收服务端轨迹作为补充
            AccumulateServerTrail();
            diagDFrames++;
            if (ballDetected) diagBallDetected++;
            if (clientBallDetected) diagClientBall++;

            // 保存当前帧作为下一次差分的参考帧
            System.Array.Copy(frameBuf, prevFrameBuf, frameBuf.Length);

            UpdateTexture();
        }

        /// <summary>解码独立轨迹帧(§4.5.6)</summary>
        private void DecodeTrailFrame(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 2) return;
            int pos = payloadStart;
            int packetEnd = packet.Length - 2;

            int count = packet[pos++];
            int oldestAge = packet[pos++]; // 保留但暂不使用
            int maxPts = Mathf.Min(count, 120);

            for (int i = 0; i < maxPts; i++)
            {
                if (pos + 1 >= packetEnd) { maxPts = i; break; }
                trailPoints[i].x = packet[pos++];
                trailPoints[i].y = packet[pos++];
            }
            trailCount = maxPts;

            // 增量累积轨迹
            AccumulateServerTrail();
            diagTrailFrames++;

            // 轨迹帧不改变frameBuf, 仅更新轨迹并重新渲染
            UpdateTexture();
        }

        /// <summary>
        /// 解码混合块列表（I_FRAME_SINGLE / I_FRAME_PART2 共用）
        /// </summary>
        /// <param name="applyXor">true=XOR叠加(Part2补丁), false=直接覆盖(Single)</param>
        /// <param name="bgFill">背景填充字节, 用于 rle_len=0 的全翻转块</param>
        private void DecodeBlockEntries(byte[] packet, int startPos, int count,
                                        int packetEnd, bool applyXor, byte bgFill)
        {
            int pos = startPos;
            byte[] blockBuf = new byte[8];

            for (int i = 0; i < count && pos < packetEnd; i++)
            {
                if (pos + 3 > packetEnd) break;

                int bx = packet[pos++];
                int by = packet[pos++];
                byte rleLenByte = packet[pos++];

                if (rleLenByte == 0)
                {
                    // 全翻转: 块内所有像素为背景色的反色 (§5.3 rle_len=0)
                    byte fillVal = (byte)(bgFill ^ 0xFF);
                    for (int r = 0; r < 8; r++) blockBuf[r] = fillVal;
                }
                else if ((rleLenByte & 0x80) != 0)
                {
                    // 原始8字节: 最高位=1 表示未压缩 (§5.3.2)
                    int rawLen = rleLenByte & 0x7F;
                    if (pos + rawLen > packetEnd) break;
                    for (int r = 0; r < 8; r++)
                        blockBuf[r] = (r < rawLen) ? packet[pos + r] : (byte)0;
                    pos += rawLen;
                }
                else
                {
                    // 块内RLE: rleLenByte = RLE数据字节数 (§5.3.2)
                    int rleLen = rleLenByte & 0x7F;
                    if (pos + rleLen > packetEnd) break;
                    DecodeBlockRLE(packet, pos, rleLen, blockBuf);
                    pos += rleLen;
                }

                // 写入frameBuf对应位置
                for (int row = 0; row < 8; row++)
                {
                    int y = by * 8 + row;
                    int byteOffset = y * (TEX_W / 8) + bx;
                    if (byteOffset >= 0 && byteOffset < frameBuf.Length)
                    {
                        if (applyXor)
                            frameBuf[byteOffset] ^= blockBuf[row];
                        else
                            frameBuf[byteOffset] = blockBuf[row];
                    }
                }
            }
        }

        /// <summary>
        /// 解码8×8块的内部RLE（64像素 → 8字节）
        /// 格式同全帧RLE: 起始颜色(1B) + run计数序列
        /// </summary>
        private void DecodeBlockRLE(byte[] data, int offset, int rleLen, byte[] blockBuf)
        {
            for (int i = 0; i < 8; i++) blockBuf[i] = 0;
            if (rleLen <= 0 || offset >= data.Length) return;

            bool curColor = (data[offset] == 0x01);
            int dataIdx = offset + 1;
            int endOffset = offset + rleLen;
            int pixIdx = 0;
            const int totalPixels = 64; // 8×8

            while (dataIdx < endOffset && dataIdx < data.Length && pixIdx < totalPixels)
            {
                int run = data[dataIdx++];
                if (run == 0) { curColor = !curColor; continue; }

                for (int j = 0; j < run && pixIdx < totalPixels; j++, pixIdx++)
                {
                    if (curColor)
                    {
                        int byteIdx = pixIdx / 8;
                        int bitIdx = 7 - (pixIdx % 8);
                        blockBuf[byteIdx] |= (byte)(1 << bitIdx);
                    }
                }
                curColor = !curColor;
            }
        }

        /// <summary>
        /// RLE解码到指定缓冲区（用于Part1缩略帧 96×72）
        /// </summary>
        private void DecodeRLEToBuf(byte[] data, int offset, int rleLen, byte bgColor,
                                    byte[] buf, int width, int height)
        {
            int totalPixels = width * height;
            int bytesCount = totalPixels / 8;

            byte fillByte = (bgColor == 0xFF) ? (byte)0xFF : (byte)0x00;
            for (int i = 0; i < bytesCount && i < buf.Length; i++) buf[i] = fillByte;

            if (rleLen <= 0 || offset >= data.Length) return;

            bool curColor = (data[offset] == 0x01);
            int dataIdx = offset + 1;
            int endOffset = offset + rleLen;
            int pixIdx = 0;

            while (dataIdx < endOffset && dataIdx < data.Length && pixIdx < totalPixels)
            {
                int run = data[dataIdx++];
                if (run == 0) { curColor = !curColor; continue; }

                for (int j = 0; j < run && pixIdx < totalPixels; j++, pixIdx++)
                {
                    int byteIdx = pixIdx / 8;
                    int bitIdx = 7 - (pixIdx % 8);
                    if (byteIdx < buf.Length)
                    {
                        if (curColor)
                            buf[byteIdx] |= (byte)(1 << bitIdx);
                        else
                            buf[byteIdx] &= (byte)~(1 << bitIdx);
                    }
                }
                curColor = !curColor;
            }
        }

        /// <summary>
        /// 最近邻2×上采样: 96×72 → 192×144
        /// 每个源像素复制为2×2目标像素 (§5.5.3)
        /// </summary>
        private void UpsampleNN(byte[] src, int srcW, int srcH,
                                byte[] dst, int dstW, int dstH)
        {
            for (int i = 0; i < dst.Length; i++) dst[i] = 0;

            for (int ty = 0; ty < srcH; ty++)
            {
                for (int tx = 0; tx < srcW; tx++)
                {
                    int sByte = ty * (srcW / 8) + tx / 8;
                    int sBit = 7 - (tx % 8);
                    if (sByte >= src.Length) continue;
                    bool pixel = (src[sByte] & (1 << sBit)) != 0;

                    if (pixel)
                    {
                        for (int dy = 0; dy < 2; dy++)
                        {
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int fx = tx * 2 + dx;
                                int fy = ty * 2 + dy;
                                int fByte = fy * (dstW / 8) + fx / 8;
                                int fBit = 7 - (fx % 8);
                                if (fByte < dst.Length)
                                    dst[fByte] |= (byte)(1 << fBit);
                            }
                        }
                    }
                }
            }
        }

        // ─── 以下保留旧RLE方法供兼容，新逻辑不再调用 ───

        private void DecodeRLE(byte[] data, int offset, int rleLen, byte bgColor)
        {
            if (rleLen <= 0) return;
            int totalPixels = TEX_W * TEX_H;

            // 清除帧缓冲
            byte fillByte = (bgColor == 0xFF) ? (byte)0xFF : (byte)0x00;
            for (int i = 0; i < frameBuf.Length; i++) frameBuf[i] = fillByte;

            if (offset >= data.Length) return;
            bool curColor = (data[offset] == 0x01);
            int dataIdx = offset + 1;
            int pixIdx = 0;
            int endOffset = offset + rleLen;

            while (dataIdx < endOffset && dataIdx < data.Length && pixIdx < totalPixels)
            {
                int run = data[dataIdx++];
                if (run == 0)
                {
                    // 续接标记：上次255 run末尾已翻转颜色，翻转回来保持同色
                    curColor = !curColor;
                    continue;
                }

                for (int i = 0; i < run && pixIdx < totalPixels; i++, pixIdx++)
                {
                    int byteIdx = pixIdx / 8;
                    int bitIdx = 7 - (pixIdx % 8);
                    if (curColor)
                        frameBuf[byteIdx] |= (byte)(1 << bitIdx);
                    else
                        frameBuf[byteIdx] &= (byte)~(1 << bitIdx);
                }
                curColor = !curColor;
            }
        }

        private void ParseTrailMeta(byte[] data, int offset)
        {
            ballDetected = false;
            trailCount = 0;

            // 需要至少5字节: ball_detected + ball_cx + ball_cy + ball_radius + trail_count
            if (offset + 5 > data.Length - 2) return; // 减2跳过 XOR + 0x5A

            ballDetected = data[offset] != 0;
            ballCX = data[offset + 1];
            ballCY = data[offset + 2];
            ballRadius = data[offset + 3];
            int rawCount = data[offset + 4];
            int maxPts = Mathf.Min(rawCount, 120);

            int ptOffset = offset + 5;
            for (int i = 0; i < maxPts; i++)
            {
                if (ptOffset + 1 >= data.Length - 2) { maxPts = i; break; }
                trailPoints[i].x = data[ptOffset];
                trailPoints[i].y = data[ptOffset + 1];
                ptOffset += 2;
            }
            trailCount = maxPts;
        }

        // ═══════════════════ 轨迹叠加算法 ═══════════════════

        /// <summary>
        /// 新弹丸发射检测：弹丸消失超过阈值后重新出现 → 新一轮发射
        /// 清除上次累积轨迹，开始叠加本次轨迹
        /// </summary>
        private void HandleShotDetection(bool anyBallNow)
        {
            if (anyBallNow)
            {
                consecutiveNoBall = 0;
                if (!prevBallState &&
                    Time.realtimeSinceStartup - lastBallDetectTime > SHOT_GAP_SEC &&
                    accTrailCount >= MIN_TRAIL_TO_KEEP)
                {
                    // 新一轮发射 → 清除上次轨迹
                    accTrailCount = 0;
                    accTrailNewStart = 0;
                    lastServerTrailCount = 0;
                    wmj.Log.I("[LobShotHUD] 检测到新一轮发射，清除历史轨迹",
                        wmj.Log.Tag.UI);
                }
                lastBallDetectTime = Time.realtimeSinceStartup;
                prevBallState = true;
            }
            else
            {
                consecutiveNoBall++;
                // 只有连续多帧无弹丸才判定弹丸消失
                if (consecutiveNoBall > 15)
                    prevBallState = false;
            }
        }

        /// <summary>
        /// 客户端帧差弹丸检测：严格按 §8.1 协议参数过滤。
        /// 像素数 4~50, 面积 4~400, 质心位移 &lt;20px, 连续2帧确认
        /// </summary>
        private void ClientDiffDetectBall()
        {
            clientBallDetected = false;
            if (prevFrameBuf == null) return;

            // Step 1: 帧差分统计
            int sumX = 0, sumY = 0, count = 0;
            int minX = TEX_W, maxX = 0, minY = TEX_H, maxY = 0;

            for (int byteIdx = 0; byteIdx < frameBuf.Length; byteIdx++)
            {
                int diff = frameBuf[byteIdx] ^ prevFrameBuf[byteIdx];
                if (diff == 0) continue;

                int baseX = (byteIdx % (TEX_W / 8)) * 8;
                int y = byteIdx / (TEX_W / 8);

                for (int bit = 7; bit >= 0; bit--)
                {
                    if (((diff >> bit) & 1) == 0) continue;
                    int x = baseX + (7 - bit);
                    sumX += x; sumY += y; count++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            // Step 2: 像素数门限 (§8.1: 4~50)
            if (count < BALL_MIN_PIXELS || count > BALL_MAX_PIXELS)
            {
                clientConfirmCount = 0;
                prevClientCX = -1; prevClientCY = -1;
                return;
            }

            // Step 3: 包围盒面积 (§8.1: 4~400)
            int area = (maxX - minX + 1) * (maxY - minY + 1);
            if (area < BALL_MIN_AREA || area > BALL_MAX_AREA)
            {
                clientConfirmCount = 0;
                prevClientCX = -1; prevClientCY = -1;
                return;
            }

            int cx = sumX / count;
            int cy = sumY / count;

            // Step 4: 运动连续性 — 与上一帧客户端质心距离 < 20px
            if (prevClientCX >= 0)
            {
                int dx = cx - prevClientCX, dy = cy - prevClientCY;
                if (dx * dx + dy * dy > BALL_MAX_DISP * BALL_MAX_DISP)
                {
                    // 不连续 → 可能是新弹丸出现，重置连续计数但记住位置
                    clientConfirmCount = 1;
                    prevClientCX = cx; prevClientCY = cy;
                    return;
                }
            }

            // 更新候选质心
            prevClientCX = cx; prevClientCY = cy;
            clientConfirmCount++;

            // Step 5: 连续确认 — 需连续 N 帧通过上述检测才输出
            if (clientConfirmCount >= BALL_CONFIRM_FRAMES)
            {
                clientBallDetected = true;
                clientBallCX = cx;
                clientBallCY = cy;
            }
        }

        /// <summary>
        /// 将弹丸坐标追加到累积轨迹（去重）
        /// </summary>
        private void AccumulateBallXY(int cx, int cy)
        {
            // 去重：与上一个累积点相同则跳过
            if (accTrailCount > 0)
            {
                var last = accTrail[accTrailCount - 1];
                if (last.x == cx && last.y == cy) return;
                // 跳跃过大也跳过 — 与§8.1 BALL_MAX_DISP 一致
                int dx = cx - last.x, dy = cy - last.y;
                if (dx * dx + dy * dy > BALL_MAX_DISP * BALL_MAX_DISP) return;
            }

            if (accTrailCount < accTrail.Length)
            {
                accTrailNewStart = accTrailCount;
                accTrail[accTrailCount].x = cx;
                accTrail[accTrailCount].y = cy;
                accTrailCount++;
                diagAccPoints++;
            }
        }

        /// <summary>
        /// 增量累积服务端轨迹到客户端缓冲
        /// 服务端每个 D 帧发送完整累积轨迹；客户端检测增量部分追加到 accTrail
        /// </summary>
        private void AccumulateServerTrail()
        {
            int newStart, newCount;

            if (trailCount > lastServerTrailCount)
            {
                // 正常增长：仅追加新增的点
                newStart = lastServerTrailCount;
                newCount = trailCount - lastServerTrailCount;
            }
            else if (trailCount > 0 && trailCount < lastServerTrailCount)
            {
                // 服务端重置（I帧后重新开始）：追加全部
                newStart = 0;
                newCount = trailCount;
            }
            else
            {
                // 数量不变 — 没有新增
                lastServerTrailCount = trailCount;
                return;
            }

            // 标记新批次起始位置（用于彩色渲染）
            accTrailNewStart = accTrailCount;

            for (int i = 0; i < newCount && accTrailCount < accTrail.Length; i++)
                accTrail[accTrailCount++] = trailPoints[newStart + i];

            lastServerTrailCount = trailCount;
        }

        /// <summary>HSV 彩虹映射: t∈[0,1] → 红→黄→绿→青→蓝</summary>
        private static void RainbowColor(float t, out byte r, out byte g, out byte b)
        {
            Color c = Color.HSVToRGB(t * 0.75f, 1f, 1f);
            r = (byte)(c.r * 255);
            g = (byte)(c.g * 255);
            b = (byte)(c.b * 255);
        }

        private void UpdateTexture()
        {
            // ═══ 第一遍: 1bit→RGBA, 分类着色 ═══
            // 所有白色像素清晰显示(亮白绿)
            for (int y = 0; y < TEX_H; y++)
            {
                int srcY = TEX_H - 1 - y; // 翻转Y轴(Unity纹理Y=0在底部)
                for (int x = 0; x < TEX_W; x++)
                {
                    int srcIdx = srcY * (TEX_W / 8) + x / 8;
                    int bit = 7 - (x % 8);
                    bool white = (frameBuf[srcIdx] & (1 << bit)) != 0;

                    int pi = (y * TEX_W + x) * 4; // RGBA偏移
                    if (!white)
                    {
                        // 黑色背景
                        pixelBuf[pi] = 0; pixelBuf[pi + 1] = 0;
                        pixelBuf[pi + 2] = 0; pixelBuf[pi + 3] = 255;
                    }
                    else
                    {
                        // 场景白色像素: 明亮白绿色(清晰可见)
                        pixelBuf[pi] = 180; pixelBuf[pi + 1] = 220;
                        pixelBuf[pi + 2] = 190; pixelBuf[pi + 3] = 255;
                    }
                }
            }

            // ═══ 轨迹叠加暂时禁用，只显示原始图像 ═══
            // TODO: 轨迹叠加待调试完成后重新启用
            // if (accTrailCount > 1) { ... }
            // if (ballDetected) DrawColoredDot(...);

            displayTex.SetPixelData(pixelBuf, 0);
            displayTex.Apply(false);
        }

        /// <summary>在pixelBuf上绘制彩色实心圆(服务端坐标系, 自动翻转Y)</summary>
        private void DrawColoredDot(int cx, int cy, int radius, byte r, byte g, byte b)
        {
            int flippedCY = TEX_H - 1 - cy; // 翻转Y轴
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int px = cx + dx, py = flippedCY + dy;
                    if (px < 0 || px >= TEX_W || py < 0 || py >= TEX_H) continue;
                    int pi = (py * TEX_W + px) * 4;
                    pixelBuf[pi] = r; pixelBuf[pi + 1] = g;
                    pixelBuf[pi + 2] = b; pixelBuf[pi + 3] = 255;
                }
            }
        }

        /// <summary>在pixelBuf上绘制十字线标记(服务端坐标系, 自动翻转Y)</summary>
        private void DrawCrosshair(int cx, int cy, int armLen, byte r, byte g, byte b)
        {
            int fcy = TEX_H - 1 - cy;
            // 水平臂
            for (int dx = -armLen; dx <= armLen; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= TEX_W || fcy < 0 || fcy >= TEX_H) continue;
                int pi = (fcy * TEX_W + px) * 4;
                pixelBuf[pi] = r; pixelBuf[pi + 1] = g;
                pixelBuf[pi + 2] = b; pixelBuf[pi + 3] = 255;
            }
            // 垂直臂
            for (int dy = -armLen; dy <= armLen; dy++)
            {
                int py = fcy + dy;
                if (cx < 0 || cx >= TEX_W || py < 0 || py >= TEX_H) continue;
                int pi = (py * TEX_W + cx) * 4;
                pixelBuf[pi] = r; pixelBuf[pi + 1] = g;
                pixelBuf[pi + 2] = b; pixelBuf[pi + 3] = 255;
            }
        }

        /// <summary>Bresenham 线段绘制（服务端坐标系，自动翻转Y）</summary>
        private void DrawLine(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            int fy0 = TEX_H - 1 - y0;
            int fy1 = TEX_H - 1 - y1;
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(fy1 - fy0), sy = fy0 < fy1 ? 1 : -1;
            int err = dx + dy;
            int px = x0, py = fy0;

            for (int step = 0; step < 500; step++) // 安全上限
            {
                if (px >= 0 && px < TEX_W && py >= 0 && py < TEX_H)
                {
                    int pi = (py * TEX_W + px) * 4;
                    pixelBuf[pi] = r; pixelBuf[pi + 1] = g;
                    pixelBuf[pi + 2] = b; pixelBuf[pi + 3] = 255;
                }
                if (px == x1 && py == fy1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; px += sx; }
                if (e2 <= dx) { err += dx; py += sy; }
            }
        }

        // ═══════════════════════════════ Update ═══════════════════════════════

        void Update()
        {
            if (!isShowing) return;

            // 在主线程顺序解码所有挂起的帧数据（D帧是XOR增量，不能跳过）
            int decoded = 0;
            while (pendingFrames.TryDequeue(out byte[] frame))
            {
                DecodeFrame(frame);
                decoded++;
                // 每帧最多解码64个包防止卡顿
                if (decoded >= 64) break;
            }

            // 定期诊断日志 (Info级别, Release可见)
            if (isShowing && Time.realtimeSinceStartup - lastDiagLogTime > DIAG_LOG_INTERVAL)
            {
                lastDiagLogTime = Time.realtimeSinceStartup;
                wmj.Log.I($"[LobShotHUD] 诊断: I={diagIFrames} D={diagDFrames} " +
                    $"D_E={diagDEmpty} Trail={diagTrailFrames} | " +
                    $"srvBall={diagBallDetected} cltBall={diagClientBall} " +
                    $"accPts={diagAccPoints} accTotal={accTrailCount}", wmj.Log.Tag.UI);
            }

            // 更新敌方基地血量
            UpdateBaseHealth();

            // 护盾闪烁动画
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
            uint maxHp = 5000; // BASE_MAX_HEALTH
            float pct = maxHp > 0 ? (float)hp / maxHp : 0f;

            if (baseBarFill != null) baseBarFill.fillAmount = pct;
            if (baseHpText != null) baseHpText.text = $"{hp} / {maxHp}";

            // 护盾效果：当基地有前哨站存活时显示
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
