using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Framework.Utils;

namespace Framework.Video
{
    /// 组装器：按帧号与分片号重组完整AnnexB字节流，处理丢包与错帧
    public class AnnexBFrameAssembler
    {
        // 分片元数据：存储实际数据长度和池化标志以便正确归还
        private struct SliceData
        {
            public byte[] Data;
            public int ActualLength;
            public bool IsPooled;
        }
        
        private class FrameBuffer
        {
            public ushort FrameId;
            public uint ExpectedLength;
            public SortedDictionary<ushort, SliceData> Slices = new SortedDictionary<ushort, SliceData>();
            public int ReceivedBytes;
            public float LastUpdateTime;
            public float TimeoutSec;
            
            /// <summary>归还所有分片到ArrayPool</summary>
            public void ReturnSlicesToPool()
            {
                foreach (var kv in Slices)
                {
                    if (kv.Value.IsPooled && kv.Value.Data != null)
                    {
                        ArrayPool<byte>.Shared.Return(kv.Value.Data);
                    }
                }
                Slices.Clear();
            }
        }

        private readonly Dictionary<ushort, FrameBuffer> buffers = new Dictionary<ushort, FrameBuffer>();
        private readonly float baseTimeoutSec;
        private readonly float bytesPerSecForTimeout;
        private readonly int maxBufferedFrames;
        private readonly bool verbose;
        // 预分配List缓存，避免每次TryAssemble/CleanupOldFrames重复分配
        private readonly List<ushort> tempRemoveList = new List<ushort>(32);
        // 可复用MemoryStream，避免每次组帧分配新的（容量自动增长）
        private readonly MemoryStream assembleStream = new MemoryStream(512 * 1024); // 初始512KB
        // 已输出帧号追踪：防止输出比已输出帧更旧的帧（帧回退）
        private ushort lastOutputFrameId = 0;
        private bool hasOutputFrame = false;

        public AnnexBFrameAssembler(float timeoutSec = 0.1f, int maxBufferedFrames = 8, bool verbose = false, float bytesPerSecForTimeout = 800000f)
        {
            this.baseTimeoutSec = Mathf.Max(0.03f, timeoutSec);
            this.bytesPerSecForTimeout = Mathf.Max(50000f, bytesPerSecForTimeout);
            this.maxBufferedFrames = maxBufferedFrames;
            this.verbose = verbose;
        }

        /// 添加一个UDP分片
        public void AddSlice(UdpVideoFrame slice)
        {
            // 清理超时/过旧帧
            CleanupOldFrames();
            
            // 注意：暂时禁用帧回退防护以排查问题
            // 快速拒绝过时帧的分片（帧号比已输出帧更旧）
            // if (hasOutputFrame && !IsFrameNewer(slice.FrameId, lastOutputFrameId))
            // {
            //     slice.ReturnNaluToPool(); // 归还过时分片的NALU
            //     return;
            // }

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

            // 若重复分片则忽略（归还重复的NALU到池）
            if (fb.Slices.ContainsKey(slice.SliceId))
            {
                DebugLog.TransportWarning($"[AnnexBFrameAssembler] 重复分片忽略: frame={slice.FrameId}, slice={slice.SliceId}");
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 重复分片忽略: frame={slice.FrameId}, slice={slice.SliceId}", wmj.DebugTools.LogCategory.Video);
                wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 重复分片忽略: frame=" + slice.FrameId + ", slice=" + slice.SliceId, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 重复分片忽略: frame=" + slice.FrameId + ", slice=" + slice.SliceId, "WARN");
                slice.ReturnNaluToPool(); // 归还重复分片的NALU
                return;
            }
            // 存储分片数据及其元信息
            int actualLen = slice.NaluActualLength > 0 ? slice.NaluActualLength : (slice.Nalu?.Length ?? 0);
            fb.Slices[slice.SliceId] = new SliceData 
            { 
                Data = slice.Nalu ?? Array.Empty<byte>(), 
                ActualLength = actualLen,
                IsPooled = slice.IsPooled 
            };
            fb.ReceivedBytes += actualLen;
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
            
            // 收集所有完整帧，找出帧号最小的那个（但必须比已输出帧更新）
            ushort? readyId = null;
            ushort minFrameId = ushort.MaxValue;
            tempRemoveList.Clear(); // 复用预分配List
            
            foreach (var kv in buffers)
            {
                var fb = kv.Value;
                
                // 若收到超出长度（错帧），标记移除
                if (fb.ReceivedBytes > fb.ExpectedLength && fb.ExpectedLength > 0)
                {
                    DebugLog.TransportWarning($"[AnnexBFrameAssembler] 错帧丢弃: frame={fb.FrameId}, recv={fb.ReceivedBytes} > expected={fb.ExpectedLength}");
#if UNITY_EDITOR
                    wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 错帧丢弃: frame={fb.FrameId}, recv={fb.ReceivedBytes} > expected={fb.ExpectedLength}", wmj.DebugTools.LogCategory.Video);
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 错帧丢弃: frame=" + fb.FrameId + ", recv=" + fb.ReceivedBytes + " > expected=" + fb.ExpectedLength, "WARN");
#endif
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 错帧丢弃: frame=" + fb.FrameId + ", recv=" + fb.ReceivedBytes + " > expected=" + fb.ExpectedLength, "WARN");
                    tempRemoveList.Add(fb.FrameId);
                    continue;
                }
                
                // 注意：暂时禁用帧回退防护以排查问题
                // 跳过已输出过的过时帧（防止帧回退）
                // if (hasOutputFrame && !IsFrameNewer(fb.FrameId, lastOutputFrameId))
                // {
                //     // 这个帧比已输出帧更旧或相等，标记移除
                //     tempRemoveList.Add(fb.FrameId);
                //     continue;
                // }
                
                // 检查是否完整，并找最小帧号
                if (fb.ReceivedBytes == fb.ExpectedLength && fb.ExpectedLength > 0)
                {
                    // 使用带回绕的帧号比较
                    if (readyId == null || IsFrameNewer(minFrameId, fb.FrameId))
                    {
                        minFrameId = fb.FrameId;
                        readyId = fb.FrameId;
                    }
                }
            }
            
            // 移除错帧和过时帧并归还其NALU到池
            foreach (var id in tempRemoveList)
            {
                if (buffers.TryGetValue(id, out var removeFb))
                {
                    removeFb.ReturnSlicesToPool();
                    buffers.Remove(id);
                }
            }
            
            if (readyId.HasValue)
            {
                var fb = buffers[readyId.Value];
                // 按分片号排序拼接（使用可复用MemoryStream减少分配）
                assembleStream.SetLength(0); // 重置流位置
                assembleStream.Position = 0;
                foreach (var kv in fb.Slices)
                {
                    var sliceData = kv.Value;
                    assembleStream.Write(sliceData.Data, 0, sliceData.ActualLength);
                }
                frameId = fb.FrameId;
                annexB = assembleStream.ToArray(); // 仍需分配输出数组（解码器需要独立副本）
                
                // 更新已输出帧号追踪（防止后续输出更旧的帧）
                lastOutputFrameId = frameId;
                hasOutputFrame = true;
                
                // 归还分片NALU到池后移除帧缓冲
                fb.ReturnSlicesToPool();
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
        
        /// <summary>
        /// 带回绕的帧号比较：判断 frameA 是否比 frameB 更新（更大）
        /// 处理 ushort 从 65535 回绕到 0 的情况
        /// </summary>
        private bool IsFrameNewer(ushort frameA, ushort frameB)
        {
            // 使用带符号差值处理回绕：如果差值在合理范围内（<32768），则正常比较
            int diff = (int)frameA - (int)frameB;
            if (diff > 32768) diff -= 65536;
            if (diff < -32768) diff += 65536;
            return diff > 0;
        }

        private void CleanupOldFrames()
        {
            var now = Time.realtimeSinceStartup;
            tempRemoveList.Clear(); // 复用预分配List
            foreach (var kv in buffers)
            {
                var fb = kv.Value;
                if (now - fb.LastUpdateTime > fb.TimeoutSec)
                {
                    tempRemoveList.Add(kv.Key);
                }
            }
            foreach (var id in tempRemoveList)
            {
                DebugLog.TransportWarning($"[AnnexBFrameAssembler] 缓冲超时移除: frame={id}");
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 缓冲超时移除: frame={id}");
                wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 缓冲超时移除: frame=" + id, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 缓冲超时移除: frame=" + id, "WARN");
                // 归还NALU到池后移除
                if (buffers.TryGetValue(id, out var removeFb))
                {
                    removeFb.ReturnSlicesToPool();
                    buffers.Remove(id);
                }
            }
            // 限制缓存帧数量
            if (buffers.Count > maxBufferedFrames)
            {
                // 移除最旧的若干帧（使用tempRemoveList收集Keys避免新分配）
                tempRemoveList.Clear();
                foreach (var key in buffers.Keys)
                {
                    tempRemoveList.Add(key);
                }
                foreach (var id in tempRemoveList)
                {
                    DebugLog.TransportWarning($"[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame={id} (count={buffers.Count})");
#if UNITY_EDITOR
                    wmj.DebugTools.Warn($"[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame={id} (count={buffers.Count})");
                    wmj.DebugTools.WriteDebugLog("[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame=" + id + " (count=" + buffers.Count + ")", "WARN");
#endif
                    wmj.DebugTools.WriteRunLog("[AnnexBFrameAssembler] 缓冲溢出裁剪: 移除 frame=" + id + " (count=" + buffers.Count + ")", "WARN");
                    if (buffers.TryGetValue(id, out var overflowFb))
                    {
                        overflowFb.ReturnSlicesToPool();
                        buffers.Remove(id);
                    }
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
