using System;
using UnityEngine;
using Framework.Utils;

// 处理 UDP 图传数据包
public class UdpVideoHandler : IMessageHandler
{
    public event Action<UdpVideoFrame> OnFrameReceived;
    private static long totalSlices = 0;
    private static bool firstValidSliceAnnounced = false;

#if UNITY_EDITOR || DIAGNOSE_UDP
    private static float diagLastReport = 0;
    private static int diagSlicesThisSecond = 0;
    private static int diagFramesThisSecond = 0;
#endif

    public void HandleMessage(string topic, byte[] payload)
    {
        // topic 为 remoteEP.ToString()，payload 为原始 UDP 包
        if (payload == null || payload.Length < 8)
        {
            DebugLog.TransportWarning("[UdpVideoHandler] UDP 包长度不足8字节，丢弃");
#if UNITY_EDITOR
            wmj.DebugTools.Warn("[UdpVideoHandler] UDP 包长度不足8字节，丢弃");
            wmj.DebugTools.WriteDebugLog("[UdpVideoHandler] UDP 包长度不足8字节，丢弃", "WARN");
#endif
            wmj.DebugTools.WriteRunLog("[UdpVideoHandler] UDP 包长度不足8字节，丢弃", "WARN");
            return;
        }
        // 解析前8字节（严格按小端，不依赖运行时平台字节序）
        ushort frameId = (ushort)(payload[0] | (payload[1] << 8));
        ushort sliceId = (ushort)(payload[2] | (payload[3] << 8));
        uint frameLen = (uint)(
            ((uint)payload[4]) |
            ((uint)payload[5] << 8) |
            ((uint)payload[6] << 16) |
            ((uint)payload[7] << 24)
        );
        // 剩余为AnnexB格式NALU
        byte[] nalu = new byte[payload.Length - 8];
        Buffer.BlockCopy(payload, 8, nalu, 0, nalu.Length);
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
            wmj.DebugTools.WriteRunLog("[UdpVideoHandler] ✅ 首个有效视频切片已到达: frame=" + frameId + ", slice=" + sliceId + ", naluLen=" + nalu.Length, "INFO");
            firstValidSliceAnnounced = true;
        }
        wmj.DebugTools.WriteRunLog("[UdpVideoHandler] 收到切片: frame=" + frameId + ", slice=" + sliceId + ", naluLen=" + nalu.Length, "INFO");
        var frame = new UdpVideoFrame
        {
            FrameId = frameId,
            SliceId = sliceId,
            FrameLength = frameLen,
            Nalu = nalu
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
}
