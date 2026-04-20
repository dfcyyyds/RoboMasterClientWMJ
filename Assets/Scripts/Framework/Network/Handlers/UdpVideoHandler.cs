using System;
using System.Buffers;
using UnityEngine;

// 处理 UDP 图传数据包
public class UdpVideoHandler : IMessageHandler, IMessageSegmentHandler
{
    public event Action<UdpVideoFrame> OnFrameReceived;
    private static long totalSlices = 0;
    private static bool firstValidSliceAnnounced = false;
    // ArrayPool用于NALU缓冲区复用，显著减少GC压力
    private static readonly ArrayPool<byte> naluPool = ArrayPool<byte>.Shared;

    // ── UDP 包头字节序自动探测 ──
    // 协议文档未明确规定前 8 字节（frame_id/slice_id/frame_len）的字节序，
    // MockServer 使用小端，官方比赛服务器（2026）使用大端。
    // 启动后前 32 个包内根据 frame_len 的合理性投票决定字节序并锁定。
    private static int leVotes = 0;          // 小端合理票数
    private static int beVotes = 0;          // 大端合理票数
    private static bool byteOrderLocked = false;
    private static bool useBigEndian = false; // 默认小端（兼容 MockServer）
    private const uint FRAMELEN_MIN = 512;          // 合理的一帧 AnnexB 字节数下限（0.5KB）
    private const uint FRAMELEN_MAX = 4_000_000;    // 4MB 上限（足够 4K I 帧）
    private const int BYTE_ORDER_LOCK_SAMPLES = 16; // 达到该投票总数后锁定

#if UNITY_EDITOR || DIAGNOSE_UDP
    private static float diagLastReport = 0;
    private static int diagSlicesThisSecond = 0;
    private static int diagFramesThisSecond = 0;
#endif

    public void HandleMessage(string topic, byte[] payload)
    {
        if (payload == null)
        {
            return;
        }
        HandleMessage(topic, new ArraySegment<byte>(payload, 0, payload.Length));
    }

    public void HandleMessage(string topic, ArraySegment<byte> payload)
    {
        // topic 为 remoteEP.ToString()，payload 为原始 UDP 包
        if (payload.Count < 8)
        {
            wmj.Log.W("[UdpVideoHandler] UDP 包长度不足8字节，丢弃", wmj.Log.Tag.Transport);
            return;
        }
        var span = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
        // ── 字节序探测：同时按小端/大端解析 frame_len，合理的一方投票 ──
        if (!byteOrderLocked)
        {
            uint fLenLE = (uint)(span[4] | ((uint)span[5] << 8) | ((uint)span[6] << 16) | ((uint)span[7] << 24));
            uint fLenBE = (uint)(((uint)span[4] << 24) | ((uint)span[5] << 16) | ((uint)span[6] << 8) | span[7]);
            bool leOk = fLenLE >= FRAMELEN_MIN && fLenLE <= FRAMELEN_MAX;
            bool beOk = fLenBE >= FRAMELEN_MIN && fLenBE <= FRAMELEN_MAX;
            if (leOk && !beOk) leVotes++;
            else if (beOk && !leOk) beVotes++;
            // 未锁定期间，先按当前占优的字节序解析（首包也能进入组帧器）
            useBigEndian = beVotes > leVotes;
            if (leVotes + beVotes >= BYTE_ORDER_LOCK_SAMPLES)
            {
                byteOrderLocked = true;
                wmj.Log.I($"[UdpVideoHandler] 字节序已锁定: {(useBigEndian ? "大端 (BigEndian)" : "小端 (LittleEndian)")} | 票数 LE={leVotes} BE={beVotes}", wmj.Log.Tag.Transport);
            }
        }

        // 解析前8字节（按探测到的字节序）
        ushort frameId; ushort sliceId; uint frameLen;
        if (useBigEndian)
        {
            frameId = (ushort)((span[0] << 8) | span[1]);
            sliceId = (ushort)((span[2] << 8) | span[3]);
            frameLen = ((uint)span[4] << 24) | ((uint)span[5] << 16) | ((uint)span[6] << 8) | span[7];
        }
        else
        {
            frameId = (ushort)(span[0] | (span[1] << 8));
            sliceId = (ushort)(span[2] | (span[3] << 8));
            frameLen = (uint)span[4] | ((uint)span[5] << 8) | ((uint)span[6] << 16) | ((uint)span[7] << 24);
        }
        // 剩余为AnnexB格式NALU - 使用ArrayPool减少GC
        int naluLen = span.Length - 8;
        byte[] nalu = naluPool.Rent(naluLen);
        span.Slice(8).CopyTo(nalu);
        totalSlices++;

#if UNITY_EDITOR || DIAGNOSE_UDP
        diagSlicesThisSecond++;
        if (sliceId == 0) diagFramesThisSecond++;
        
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - diagLastReport >= 1.0f)
        {
            wmj.Log.D($"[UdpVideoHandler] 📈 诊断: {diagFramesThisSecond} 帧, {diagSlicesThisSecond} 切片抵达", wmj.Log.Tag.Transport);
            diagSlicesThisSecond = 0;
            diagFramesThisSecond = 0;
            diagLastReport = now;
        }
#endif

        wmj.Log.D($"[UdpVideoHandler] 收到切片: frame={frameId}, slice={sliceId}, frameLen={frameLen}, naluLen={nalu.Length}, totalSlices={totalSlices}", wmj.Log.Tag.Transport);

        if (!firstValidSliceAnnounced)
        {
            wmj.Log.I($"[UdpVideoHandler] 首个有效视频切片已到达: frame={frameId}, slice={sliceId}, naluLen={nalu.Length}", wmj.Log.Tag.Transport);
            firstValidSliceAnnounced = true;
        }

        var frame = new UdpVideoFrame
        {
            FrameId = frameId,
            SliceId = sliceId,
            FrameLength = frameLen,
            Nalu = nalu,
            NaluActualLength = naluLen,
            IsPooled = true
        };
        OnFrameReceived?.Invoke(frame);
    }
}

public class UdpVideoFrame
{
    public ushort FrameId;
    public ushort SliceId;
    public uint FrameLength;
    public byte[] Nalu;
    public int NaluActualLength; // 实际NALU长度（Rent可能返回更大数组）
    public bool IsPooled;        // 是否来自ArrayPool

    /// <summary>归还NALU到ArrayPool（组帧完成后调用）</summary>
    public void ReturnNaluToPool()
    {
        if (IsPooled && Nalu != null)
        {
            ArrayPool<byte>.Shared.Return(Nalu);
            Nalu = null;
            IsPooled = false;
        }
    }
}
