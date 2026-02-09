using System;
using System.Buffers;
using UnityEngine;
using Framework.Utils;

// 处理 UDP 图传数据包
public class UdpVideoHandler : IMessageHandler, IMessageSegmentHandler
{
    public event Action<UdpVideoFrame> OnFrameReceived;
    private static long totalSlices = 0;
    private static bool firstValidSliceAnnounced = false;
    // ArrayPool用于NALU缓冲区复用，显著减少GC压力
    private static readonly ArrayPool<byte> naluPool = ArrayPool<byte>.Shared;

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
            DebugLog.TransportWarning("[UdpVideoHandler] UDP 包长度不足8字节，丢弃");
#if UNITY_EDITOR
            wmj.DebugTools.Warn("[UdpVideoHandler] UDP 包长度不足8字节，丢弃");
            wmj.DebugTools.WriteDebugLog("[UdpVideoHandler] UDP 包长度不足8字节，丢弃", "WARN");
#endif
            wmj.DebugTools.WriteRunLog("[UdpVideoHandler] UDP 包长度不足8字节，丢弃", "WARN");
            return;
        }
        var span = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
        // 解析前8字节（严格按小端，不依赖运行时平台字节序）
        ushort frameId = (ushort)(span[0] | (span[1] << 8));
        ushort sliceId = (ushort)(span[2] | (span[3] << 8));
        uint frameLen = (uint)((uint)span[4] | ((uint)span[5] << 8) | ((uint)span[6] << 16) | ((uint)span[7] << 24));
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
            wmj.DebugTools.Info($"[UdpVideoHandler] 📈 诊断: {diagFramesThisSecond} 帧, {diagSlicesThisSecond} 切片抵达");
            diagSlicesThisSecond = 0;
            diagFramesThisSecond = 0;
            diagLastReport = now;
        }
#endif

        DebugLog.Transport($"[UdpVideoHandler] 收到切片: frame={frameId}, slice={sliceId}, frameLen={frameLen}, naluLen={nalu.Length}, totalSlices={totalSlices}");
#if UNITY_EDITOR
    wmj.DebugTools.Info($"[UdpVideoHandler] 收到切片: frame={frameId}, slice={sliceId}, frameLen={frameLen}, naluLen={nalu.Length}, totalSlices={totalSlices}", wmj.DebugTools.LogCategory.Network);
    wmj.DebugTools.WriteDebugLog("[UdpVideoHandler] 收到切片: frame=" + frameId + ", slice=" + sliceId + ", frameLen=" + frameLen + ", naluLen=" + nalu.Length + ", totalSlices=" + totalSlices, "INFO");
#endif
        if (!firstValidSliceAnnounced)
        {
            DebugLog.Transport($"[UdpVideoHandler] 首个有效视频切片已到达: frame={frameId}, slice={sliceId}, naluLen={nalu.Length}");
#if UNITY_EDITOR
            wmj.DebugTools.Info($"[UdpVideoHandler] 首个有效视频切片已到达: frame={frameId}, slice={sliceId}, naluLen={nalu.Length}", wmj.DebugTools.LogCategory.Network);
            wmj.DebugTools.WriteDebugLog("[UdpVideoHandler] 首个有效视频切片已到达: frame=" + frameId + ", slice=" + sliceId + ", naluLen=" + nalu.Length, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[UdpVideoHandler] 首个有效视频切片已到达: frame=" + frameId + ", slice=" + sliceId + ", naluLen=" + nalu.Length, "INFO");
            firstValidSliceAnnounced = true;
        }
        // 移除高频日志调用，每秒约30000次调用会导致IO阻塞和卡死
        // 如需调试可启用: wmj.DebugTools.WriteRunLog("[UdpVideoHandler] 收到切片: frame=" + frameId + ", slice=" + sliceId + ", naluLen=" + nalu.Length, "INFO");
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
