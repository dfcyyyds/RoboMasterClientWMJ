using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Framework.Utils;

namespace Framework.Video
{
    /// 组装器：按帧号与分片号重组完整AnnexB字节流，处理丢包与错帧
    public class AnnexBFrameAssembler
    {
        private class FrameBuffer
        {
            public ushort FrameId;
            public uint ExpectedLength;
            public SortedDictionary<ushort, byte[]> Slices = new SortedDictionary<ushort, byte[]>();
            public int ReceivedBytes;
            public float LastUpdateTime;
            public float TimeoutSec;
        }

        private readonly Dictionary<ushort, FrameBuffer> buffers = new Dictionary<ushort, FrameBuffer>();
        private readonly float baseTimeoutSec;
        private readonly float bytesPerSecForTimeout;
        private readonly int maxBufferedFrames;
        private readonly bool verbose;

        public AnnexBFrameAssembler(float timeoutSec = 0.2f, int maxBufferedFrames = 8, bool verbose = false, float bytesPerSecForTimeout = 300000f)
        {
            this.baseTimeoutSec = Mathf.Max(0.05f, timeoutSec);
            this.bytesPerSecForTimeout = Mathf.Max(50000f, bytesPerSecForTimeout);
            this.maxBufferedFrames = maxBufferedFrames;
            this.verbose = verbose;
        }

        /// 添加一个UDP分片
        public void AddSlice(UdpVideoFrame slice)
        {
            // 清理超时/过旧帧
            CleanupOldFrames();

            if (!buffers.TryGetValue(slice.FrameId, out var fb))
            {
                fb = new FrameBuffer
                {
                    FrameId = slice.FrameId,
                    ExpectedLength = slice.FrameLength,
                    ReceivedBytes = 0,
                    LastUpdateTime = Time.realtimeSinceStartup,
                    TimeoutSec = ComputeTimeoutSec(slice.FrameLength)
                };
                buffers[slice.FrameId] = fb;
                if (verbose)
                    DebugLog.Transport($"[AnnexBFrameAssembler] 新建帧缓冲: frame={slice.FrameId}, expected={slice.FrameLength}");
#if UNITY_EDITOR
                if (verbose)
                {
                    wmj.DebugTools.Info($"[AnnexBFrameAssembler] 新建帧缓冲: frame={slice.FrameId}, expected={slice.FrameLength}", wmj.DebugTools.LogCategory.Video);
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 新建帧缓冲: frame=" + slice.FrameId + ", expected=" + slice.FrameLength, "DEBUG");
                }
#endif
                if (verbose)
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 新建帧缓冲: frame=" + slice.FrameId + ", expected=" + slice.FrameLength, "DEBUG");
            }

            // 若重复分片则忽略
            if (fb.Slices.ContainsKey(slice.SliceId))
            {
                DebugLog.TransportWarning($"[AnnexBFrameAssembler] 重复分片忽略: frame={slice.FrameId}, slice={slice.SliceId}");
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 重复分片忽略: frame={slice.FrameId}, slice={slice.SliceId}", wmj.DebugTools.LogCategory.Video);
                wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 重复分片忽略: frame=" + slice.FrameId + ", slice=" + slice.SliceId, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 重复分片忽略: frame=" + slice.FrameId + ", slice=" + slice.SliceId, "WARN");
                return;
            }
            fb.Slices[slice.SliceId] = slice.Nalu ?? Array.Empty<byte>();
            fb.ReceivedBytes += slice.Nalu?.Length ?? 0;
            fb.LastUpdateTime = Time.realtimeSinceStartup;
            if (verbose)
                DebugLog.Transport($"[AnnexBFrameAssembler] 收片: frame={slice.FrameId}, slice={slice.SliceId}, recv={fb.ReceivedBytes}/{fb.ExpectedLength}, partLen={slice.Nalu?.Length}");
#if UNITY_EDITOR
            if (verbose)
            {
                wmj.DebugTools.Info($"[AnnexBFrameAssembler] 收片: frame={slice.FrameId}, slice={slice.SliceId}, recv={fb.ReceivedBytes}/{fb.ExpectedLength}, partLen={slice.Nalu?.Length}", wmj.DebugTools.LogCategory.Video);
                wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 收片: frame=" + slice.FrameId + ", slice=" + slice.SliceId + ", recv=" + fb.ReceivedBytes + "/" + fb.ExpectedLength + ", partLen=" + (slice.Nalu?.Length ?? 0), "INFO");
            }
#endif
        }

        /// 是否存在完整帧可取
        public bool TryAssemble(out ushort frameId, out byte[] annexB)
        {
            frameId = 0;
            annexB = null;
            // 优先取最小帧号的完整帧
            ushort? readyId = null;
            foreach (var kv in buffers)
            {
                var fb = kv.Value;
                if (fb.ReceivedBytes == fb.ExpectedLength && fb.ExpectedLength > 0)
                {
                    readyId = fb.FrameId;
                    break;
                }
                // 若收到超出长度（错帧），丢弃
                if (fb.ReceivedBytes > fb.ExpectedLength && fb.ExpectedLength > 0)
                {
                    // 错帧，直接移除
                    DebugLog.TransportWarning($"[AnnexBFrameAssembler] 错帧丢弃: frame={fb.FrameId}, recv={fb.ReceivedBytes} > expected={fb.ExpectedLength}");
#if UNITY_EDITOR
                    wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 错帧丢弃: frame={fb.FrameId}, recv={fb.ReceivedBytes} > expected={fb.ExpectedLength}", wmj.DebugTools.LogCategory.Video);
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 错帧丢弃: frame=" + fb.FrameId + ", recv=" + fb.ReceivedBytes + " > expected=" + fb.ExpectedLength, "WARN");
#endif
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 错帧丢弃: frame=" + fb.FrameId + ", recv=" + fb.ReceivedBytes + " > expected=" + fb.ExpectedLength, "WARN");
                    buffers.Remove(fb.FrameId);
                    break;
                }
            }
            if (readyId.HasValue)
            {
                var fb = buffers[readyId.Value];
                // 按分片号排序拼接
                using var ms = new MemoryStream((int)fb.ExpectedLength);
                foreach (var kv in fb.Slices)
                {
                    var data = kv.Value;
                    ms.Write(data, 0, data.Length);
                }
                frameId = fb.FrameId;
                annexB = ms.ToArray();
                buffers.Remove(fb.FrameId);
                if (verbose)
                    DebugLog.Transport($"[AnnexBFrameAssembler] 组帧完成: frame={frameId}, bytes={annexB.Length}");
#if UNITY_EDITOR
                if (verbose)
                {
                    wmj.DebugTools.Info($"[AnnexBFrameAssembler] 组帧完成: frame={frameId}, bytes={annexB.Length}", wmj.DebugTools.LogCategory.Video);
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 组帧完成: frame=" + frameId + ", bytes=" + annexB.Length, "INFO");
                }
#endif
                if (verbose)
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 组帧完成: frame=" + frameId + ", bytes=" + annexB.Length, "INFO");
                return true;
            }
            return false;
        }

        private void CleanupOldFrames()
        {
            var now = Time.realtimeSinceStartup;
            var removeList = new List<ushort>();
            foreach (var kv in buffers)
            {
                var fb = kv.Value;
                if (now - fb.LastUpdateTime > fb.TimeoutSec)
                {
                    removeList.Add(kv.Key);
                }
            }
            foreach (var id in removeList)
            {
                DebugLog.TransportWarning($"[AnnexBFrameAssembler] 缓冲超时移除: frame={id}");
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 缓冲超时移除: frame={id}");
                wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 缓冲超时移除: frame=" + id, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 缓冲超时移除: frame=" + id, "WARN");
                buffers.Remove(id);
            }
            // 限制缓存帧数量
            if (buffers.Count > maxBufferedFrames)
            {
                // 移除最旧的若干帧
                foreach (var id in new List<ushort>(buffers.Keys))
                {
                    DebugLog.TransportWarning($"[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame={id} (count={buffers.Count})");
#if UNITY_EDITOR
                    wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame={id} (count={buffers.Count})");
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame=" + id + " (count=" + buffers.Count + ")", "WARN");
#endif
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame=" + id + " (count=" + buffers.Count + ")", "WARN");
                    buffers.Remove(id);
                    if (buffers.Count <= maxBufferedFrames) break;
                }
            }
        }

        private float ComputeTimeoutSec(uint expectedLength)
        {
            if (expectedLength == 0)
                return baseTimeoutSec;
            // 动态超时：基础 + (帧大小/估计吞吐)，并限制上限避免过高延迟
            float extra = expectedLength / bytesPerSecForTimeout;
            return Mathf.Clamp(baseTimeoutSec + extra, baseTimeoutSec, 0.6f);
        }
    }
}
