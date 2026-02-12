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

        // 轨迹元数据(从服务端包解析)
        private struct TrailPoint { public int x, y; }
        private TrailPoint[] trailPoints = new TrailPoint[120];
        private int trailCount;
        private bool ballDetected;
        private int ballCX, ballCY, ballRadius;

        // 颜色渲染常量
        // 靶标屏幕坐标(与服务端3D投影参数匹配: cx≈101, cy≈75)
        private const int TARGET_CX = 101, TARGET_CY = 75, TARGET_R = 2;
        private const int COLORFUL_TAIL = 8; // 末尾N个轨迹点用彩色渐变

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

        // ─── 线程安全帧缓冲 ───
        private byte[] pendingFrame;
        private readonly object frameLock = new object();

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
            if (block == null || block.Data == null || block.Data.Length < 10) return;

            // 仅缓存原始数据，在主线程Update中解码（MQTT回调在后台线程）
            byte[] raw = block.Data.ToByteArray();
            lock (frameLock) { pendingFrame = raw; }

            // 诊断日志（每10帧打印一次，避免刷屏）
            int fc = System.Threading.Interlocked.Increment(ref frameCount);
            if (fc <= 3 || fc % 10 == 0)
                wmj.Log.D($"[LobShotHUD] 收到帧 #{fc}, 长度={raw.Length}", wmj.Log.Tag.UI);
        }

        private void DecodeFrame(byte[] packet)
        {
            // 基本校验 (包头至少9字节: sync+type+fid*2+frag+plen*2 + xor + end)
            if (packet.Length < 9) return;
            if (packet[0] != 0xA5) return;
            if (packet[packet.Length - 1] != 0x5A) return;

            // XOR校验
            byte xor = 0;
            for (int i = 0; i < packet.Length - 2; i++) xor ^= packet[i];
            if (xor != packet[packet.Length - 2]) return;

            byte frameType = packet[1];
            // payload_len: 2字节 uint16 LE (偏移5-6)
            int payloadLen = packet[5] | (packet[6] << 8);
            int payloadStart = 7;

            switch (frameType)
            {
                case FT_I_SINGLE: // 0x03 — 全帧RLE + 轨迹元数据
                    DecodeIFrameSingle(packet, payloadStart, payloadLen);
                    break;

                case FT_D_FRAME: // 0x10 — XOR差分块 + 弹丸 + 轨迹
                    DecodeDFrame(packet, payloadStart, payloadLen);
                    break;

                case FT_D_EMPTY: // 0x11 — 无变化, 保持当前帧
                    // 不修改frameBuf; D_EMPTY表示无弹丸检测, 清除弹丸状态
                    ballDetected = false;
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

        /// <summary>解码I帧(全帧RLE重建)</summary>
        private void DecodeIFrameSingle(byte[] packet, int payloadStart, int payloadLen)
        {
            if (payloadLen < 5) return; // w/h/bg + rle_len(2B)
            byte bgColor = packet[payloadStart + 2];

            // 读取 rle_len (uint16 LE, payload偏移3-4)
            int rleLen = packet[payloadStart + 3] | (packet[payloadStart + 4] << 8);
            int rleStart = payloadStart + 5;

            // RLE解码到frameBuf(完全重建)
            DecodeRLE(packet, rleStart, rleLen, bgColor);

            // 解析轨迹元数据(RLE数据之后)
            int trailMetaStart = rleStart + rleLen;
            ParseTrailMeta(packet, trailMetaStart);

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

            // 轨迹帧不改变frameBuf, 仅更新轨迹并重新渲染
            UpdateTexture();
        }

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

        private void UpdateTexture()
        {
            // ═══ 第一遍: 1bit→RGBA, 分类着色 ═══
            // 靶标区域白色像素→亮绿, 其余白色像素→暗绿(噪声), 黑色→黑色
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
                        // 判断是否在靶标区域内(使用原始坐标系)
                        int dx = x - TARGET_CX;
                        int dy = srcY - TARGET_CY;
                        if (dx * dx + dy * dy <= (TARGET_R + 1) * (TARGET_R + 1))
                        {
                            // 靶标: 亮绿色
                            pixelBuf[pi] = 0; pixelBuf[pi + 1] = 255;
                            pixelBuf[pi + 2] = 50; pixelBuf[pi + 3] = 255;
                        }
                        else
                        {
                            // 噪声/其他白色像素: 暗绿色
                            pixelBuf[pi] = 20; pixelBuf[pi + 1] = 60;
                            pixelBuf[pi + 2] = 35; pixelBuf[pi + 3] = 255;
                        }
                    }
                }
            }

            // ═══ 第二遍: 叠加轨迹点颜色 ═══
            if (trailCount > 0)
            {
                int oldEnd = Mathf.Max(0, trailCount - COLORFUL_TAIL);

                // 历史轨迹: 白色
                for (int i = 0; i < oldEnd; i++)
                    DrawColoredDot(trailPoints[i].x, trailPoints[i].y, 2, 255, 255, 255);

                // 最新变化: 彩色渐变 蓝→红→黄
                for (int i = oldEnd; i < trailCount; i++)
                {
                    float ratio = (float)(i - oldEnd) / COLORFUL_TAIL;
                    byte cr, cg, cb;
                    if (ratio < 0.5f)
                    {
                        // 蓝→红
                        float t = ratio * 2f;
                        cr = (byte)(t * 255);
                        cg = 0;
                        cb = (byte)((1f - t) * 255);
                    }
                    else
                    {
                        // 红→黄
                        float t = (ratio - 0.5f) * 2f;
                        cr = 255;
                        cg = (byte)(t * 255);
                        cb = 0;
                    }
                    DrawColoredDot(trailPoints[i].x, trailPoints[i].y, 2, cr, cg, cb);
                }
            }

            // ═══ 第三遍: 叠加当前弹丸(亮黄色) ═══
            if (ballDetected)
                DrawColoredDot(ballCX, ballCY, ballRadius, 255, 255, 0);

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

        // ═══════════════════════════════ Update ═══════════════════════════════

        void Update()
        {
            if (!isShowing) return;

            // 在主线程处理挂起的帧数据（线程安全）
            byte[] frame = null;
            lock (frameLock) { frame = pendingFrame; pendingFrame = null; }
            if (frame != null) DecodeFrame(frame);

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
