using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Framework.Network
{
    /// <summary>
    /// 吊射 H.264 码流传输层 — 解析自定义协议 0x04 帧类型，重组 AnnexB 分片
    /// 
    /// 比赛模式：
    ///   CustomByteBlock(0x0310) → 自定义协议(SYNC=0xA5, TYPE=0x04) → 本组件 → AnnexB 帧
    /// 仿真模式：
    ///   裸 UDP 8888 → [seq(4B) + ts(4B) + AnnexB 数据] → 本组件 → AnnexB 帧
    /// 
    /// 输出：完整的 H.264 AnnexB 帧（可直接喂给 FfmpegPipeDecoder.Push）
    /// </summary>
    public class LobShotH264Transport
    {
        // ─── 协议常量 ───
        private const byte SYNC = 0xA5;
        private const byte END = 0x5A;
        private const byte FT_H264_STREAM = 0x04;

        // flags 位定义
        private const byte FLAG_KEYFRAME = 1 << 0;
        private const byte FLAG_FIRST = 1 << 1;
        private const byte FLAG_LAST = 1 << 2;

        // ─── 分片重组状态 ───
        private ushort currentFrameId;
        private readonly List<byte> assemblyBuffer = new List<byte>(4096);
        private bool assembling;
        private bool currentIsKeyframe;

        // ─── 输出队列 ───
        private readonly ConcurrentQueue<H264Frame> outputFrames = new ConcurrentQueue<H264Frame>();
        private const int MAX_OUTPUT_FRAMES = 12;
        private int outputFrameCount;

        // ─── 仿真 UDP 数据块（带实际长度，因为 ArrayPool.Rent 可能返回较大的数组）───
        private struct SimUdpChunk
        {
            public byte[] Data;
            public int Length;
        }

        // ─── 仿真 UDP 原始数据队列（不走 outputFrames，避免每个分包当成完整帧）───
        private readonly ConcurrentQueue<SimUdpChunk> simUdpDataChunks = new ConcurrentQueue<SimUdpChunk>();
        private const int MAX_SIM_UDP_CHUNKS = 1024;
        private const int MAX_SIM_UDP_BYTES = 2 * 1024 * 1024; // 2MB
        private int simUdpQueuedBytes;
        private int simUdpChunkCount;

        // ─── 诊断 ───
        private int diagProtocolPackets;
        private int diagAssembledFrames;
        private int diagDroppedPackets;
        private int diagSimUdpChunks;

        public struct H264Frame
        {
            public byte[] AnnexBData;
            public int DataLength;  // 实际数据长度（ArrayPool.Rent 可能返回更大的数组）
            public bool IsKeyframe;
            public ushort FrameId;
        }

        /// <summary>取出一帧已重组的 H.264 AnnexB 数据</summary>
        public bool TryGetFrame(out H264Frame frame)
        {
            if (outputFrames.TryDequeue(out frame))
            {
                Interlocked.Decrement(ref outputFrameCount);
                return true;
            }
            return false;
        }

        /// <summary>当前输出队列帧数</summary>
        public int PendingFrameCount => Volatile.Read(ref outputFrameCount);

        /// <summary>获取诊断统计</summary>
        public string GetDiagnostics()
        {
            return $"proto={diagProtocolPackets} assembled={diagAssembledFrames} " +
                   $"dropped={diagDroppedPackets} simUdp={diagSimUdpChunks}";
        }

        // ═══════════════════════════════ 比赛模式：协议帧解析 ═══════════════════════════════

        /// <summary>
        /// 处理一个完整的自定义协议数据包（来自 CustomByteBlock.Data）
        /// 自动识别 FRAME_TYPE：0x04 走 H.264 管线，其他返回 false 供 v1 管线处理
        /// </summary>
        /// <returns>true=已处理(H.264), false=非H.264帧(应交给v1管线)</returns>
        public bool ProcessProtocolPacket(byte[] packet)
        {
            return ProcessProtocolPacket(packet, packet?.Length ?? 0);
        }

        /// <summary>
        /// 处理自定义协议数据包（支持 ArrayPool 租借的大数组，通过 packetLength 指定实际长度）
        /// </summary>
        public bool ProcessProtocolPacket(byte[] packet, int packetLength)
        {
            if (packet == null || packetLength < 9) return false;
            if (packet[0] != SYNC || packet[packetLength - 1] != END) return false;

            byte frameType = packet[1];
            if (frameType != FT_H264_STREAM) return false;

            // XOR 校验
            byte xor = 0;
            for (int i = 0; i < packetLength - 2; i++) xor ^= packet[i];
            if (xor != packet[packetLength - 2])
            {
                diagDroppedPackets++;
                return true; // 是 H.264 类型但校验失败，已处理（丢弃）
            }

            ushort frameId = (ushort)(packet[2] | (packet[3] << 8));
            byte fragIdx = packet[4];
            int payloadLen = packet[5] | (packet[6] << 8);
            int payloadStart = 7;

            // H264_STREAM payload: stream_id(1B) + flags(1B) + h264_data(变长)
            if (payloadLen < 2) return true;
            // byte streamId = packet[payloadStart]; // 0=吊射，预留
            byte flags = packet[payloadStart + 1];
            bool isKeyframe = (flags & FLAG_KEYFRAME) != 0;
            bool isFirst = (flags & FLAG_FIRST) != 0;
            bool isLast = (flags & FLAG_LAST) != 0;

            int h264Start = payloadStart + 2;
            int h264Len = payloadLen - 2;
            if (h264Start + h264Len > packetLength - 2) return true; // 数据越界

            diagProtocolPackets++;

            // ─── 分片重组逻辑 ───

            if (isFirst)
            {
                // 新帧开始：重置组装缓冲
                currentFrameId = frameId;
                assemblyBuffer.Clear();
                assembling = true;
                currentIsKeyframe = isKeyframe;
            }
            else if (assembling && frameId != currentFrameId)
            {
                // FrameId 切换但没收到 is_first → 上一帧不完整，丢弃
                assemblyBuffer.Clear();
                currentFrameId = frameId;
                assembling = true;
                currentIsKeyframe = isKeyframe;
                diagDroppedPackets++;
            }

            if (!assembling) return true;

            // 追加 H.264 数据
            for (int i = 0; i < h264Len; i++)
                assemblyBuffer.Add(packet[h264Start + i]);

            if (isKeyframe) currentIsKeyframe = true;

            if (isLast)
            {
                // 帧组装完成 → 使用 ArrayPool 输出，避免 GC 分配
                int assembledLen = assemblyBuffer.Count;
                byte[] annexBData = ArrayPool<byte>.Shared.Rent(assembledLen);
                assemblyBuffer.CopyTo(annexBData);
                var frame = new H264Frame
                {
                    AnnexBData = annexBData,
                    DataLength = assembledLen,
                    IsKeyframe = currentIsKeyframe,
                    FrameId = currentFrameId
                };
                outputFrames.Enqueue(frame);
                Interlocked.Increment(ref outputFrameCount);

                while (Volatile.Read(ref outputFrameCount) > MAX_OUTPUT_FRAMES && outputFrames.TryDequeue(out var dropped))
                {
                    Interlocked.Decrement(ref outputFrameCount);
                    ArrayPool<byte>.Shared.Return(dropped.AnnexBData);
                    diagDroppedPackets++;
                }
                assemblyBuffer.Clear();
                assembling = false;
                diagAssembledFrames++;
            }

            return true;
        }

        // ═══════════════════════════════ 仿真模式：裸 UDP 处理 ═══════════════════════════════

        /// <summary>
        /// 处理仿真模式裸 UDP 数据包
        /// 格式：seq_id(4B uint32 LE) + timestamp_ns(4B) + H.264 AnnexB 数据
        /// 
        /// 仿真模式下不做分片重组，原始 AnnexB 数据累积到 simUdpDataChunks 队列，
        /// 由 DrainH264Pipeline 批量拼接后一次性推给解码器，减少 syscall 开销。
        /// </summary>
        public void ProcessSimUdpPacket(byte[] data)
        {
            if (data == null || data.Length <= 8) return;

            // 跳过 8 字节头 (seq + timestamp)，提取纯 H.264 AnnexB 数据
            int h264Len = data.Length - 8;
            // 使用 ArrayPool 避免每包 new byte[] 分配，减少 GC 压力
            byte[] h264Data = ArrayPool<byte>.Shared.Rent(h264Len);
            Buffer.BlockCopy(data, 8, h264Data, 0, h264Len);

            // 封装为带长度元数据的 chunk，因为 ArrayPool.Rent 可能返回较大的数组
            simUdpDataChunks.Enqueue(new SimUdpChunk { Data = h264Data, Length = h264Len });
            Interlocked.Increment(ref simUdpChunkCount);
            simUdpQueuedBytes += h264Len;
            while ((Volatile.Read(ref simUdpChunkCount) > MAX_SIM_UDP_CHUNKS || simUdpQueuedBytes > MAX_SIM_UDP_BYTES) &&
                   simUdpDataChunks.TryDequeue(out var dropped))
            {
                Interlocked.Decrement(ref simUdpChunkCount);
                simUdpQueuedBytes -= dropped.Length;
                if (simUdpQueuedBytes < 0) simUdpQueuedBytes = 0;
                ArrayPool<byte>.Shared.Return(dropped.Data);
                diagDroppedPackets++;
            }
            diagSimUdpChunks++;
        }

        /// <summary>
        /// 批量排空仿真 UDP 数据队列，将累积的 AnnexB 数据拼接为一个字节数组。
        /// 使用 ArrayPool 避免每帧分配，调用方处理完毕后必须调用 ArrayPool&lt;byte&gt;.Shared.Return(batch)。
        /// 每次最多拼接 maxBytes 字节（默认 32KB），避免一次性生成超大数据块。
        /// 返回 false 表示队列为空，无数据可处理。
        /// </summary>
        public bool TryDrainSimUdpData(out byte[] batch, out int batchLength, int maxBytes = 32768)
        {
            batch = null;
            batchLength = 0;
            if (simUdpDataChunks.IsEmpty) return false;

            // 收集可用分包，不超过 maxBytes
            var chunks = new List<SimUdpChunk>();
            int totalLen = 0;
            while (simUdpDataChunks.TryDequeue(out var chunk))
            {
                Interlocked.Decrement(ref simUdpChunkCount);
                chunks.Add(chunk);
                totalLen += chunk.Length;
                simUdpQueuedBytes -= chunk.Length;
                if (simUdpQueuedBytes < 0) simUdpQueuedBytes = 0;
                if (totalLen >= maxBytes) break;
            }

            if (totalLen == 0) return false;

            // 使用 ArrayPool 拼接，避免 new byte[] 分配
            batch = ArrayPool<byte>.Shared.Rent(totalLen);
            batchLength = totalLen;
            int offset = 0;
            foreach (var c in chunks)
            {
                Buffer.BlockCopy(c.Data, 0, batch, offset, c.Length);
                offset += c.Length;
                ArrayPool<byte>.Shared.Return(c.Data);
            }
            return true;
        }

        /// <summary>简单检测 AnnexB 数据中是否包含 H.264 IDR NAL</summary>
        private static bool ContainsH264Idr(byte[] data)
        {
            for (int i = 0; i < data.Length - 4; i++)
            {
                // 查找起始码 00 00 01 或 00 00 00 01
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    int nalOffset = -1;
                    if (data[i + 2] == 1)
                        nalOffset = i + 3;
                    else if (data[i + 2] == 0 && i + 3 < data.Length && data[i + 3] == 1)
                        nalOffset = i + 4;

                    if (nalOffset >= 0 && nalOffset < data.Length)
                    {
                        int nalType = data[nalOffset] & 0x1F;
                        if (nalType == 5) return true; // IDR slice
                    }
                }
            }
            return false;
        }

        /// <summary>重置传输层状态（模式切换时调用）</summary>
        public void Reset()
        {
            assemblyBuffer.Clear();
            assembling = false;
            while (outputFrames.TryDequeue(out var frame))
            {
                Interlocked.Decrement(ref outputFrameCount);
                ArrayPool<byte>.Shared.Return(frame.AnnexBData);
            }
            while (simUdpDataChunks.TryDequeue(out var chunk))
            {
                Interlocked.Decrement(ref simUdpChunkCount);
                ArrayPool<byte>.Shared.Return(chunk.Data);
            }
            simUdpQueuedBytes = 0;
            diagProtocolPackets = 0;
            diagAssembledFrames = 0;
            diagDroppedPackets = 0;
            diagSimUdpChunks = 0;
        }
    }
}
