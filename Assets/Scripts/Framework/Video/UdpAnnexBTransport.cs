using System;
using UnityEngine;
using Framework.Utils;

namespace Framework.Video
{
    /// 基于现有自定义 UDP 协议 + AnnexB 的传输实现。
    /// - 订阅 NetworkManager.Instance.OnUdpVideoFrame
    /// - 使用 AnnexBFrameAssembler 组帧
    /// - 组帧成功后触发 OnAnnexBFrame 事件
    public class UdpAnnexBTransport : IVideoTransport
    {
        private AnnexBFrameAssembler assembler;
        private readonly bool verbose;
        private bool started;

        public event Action<byte[]> OnAnnexBFrame;

        public UdpAnnexBTransport(float timeoutSec = 0.2f, int maxBufferedFrames = 16, bool verbose = false)
        {
            this.verbose = verbose;
            // 低延迟取舍：提高估计吞吐，缩短动态超时
            assembler = new AnnexBFrameAssembler(timeoutSec, maxBufferedFrames, verbose, bytesPerSecForTimeout: 600000f);
        }

        public void Start()
        {
            if (started) return;
            started = true;
            NetworkManager.Instance.OnUdpVideoFrame += OnUdpSlice;
            DebugLog.Transport("[UdpAnnexBTransport] 已订阅 UDP 视频分片事件");
#if UNITY_EDITOR
            wmj.DebugTools.Info("[UdpAnnexBTransport] 已订阅 UDP 视频分片事件");
            wmj.DebugTools.WriteDebugLog("[UdpAnnexBTransport] 已订阅 UDP 视频分片事件", "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[UdpAnnexBTransport] 已订阅 UDP 视频分片事件", "INFO");
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            NetworkManager.Instance.OnUdpVideoFrame -= OnUdpSlice;
            DebugLog.Transport("[UdpAnnexBTransport] 已取消订阅 UDP 视频分片事件");
#if UNITY_EDITOR
            wmj.DebugTools.Info("[UdpAnnexBTransport] 已取消订阅 UDP 视频分片事件");
            wmj.DebugTools.WriteDebugLog("[UdpAnnexBTransport] 已取消订阅 UDP 视频分片事件", "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[UdpAnnexBTransport] 已取消订阅 UDP 视频分片事件", "INFO");
        }

        private void OnUdpSlice(UdpVideoFrame slice)
        {
            // 接入现有组帧器
            assembler.AddSlice(slice);
            // 尝试取出尽可能多的完整帧
            while (assembler.TryAssemble(out var frameId, out var annexB))
            {
                if (verbose)
                    DebugLog.Transport($"[UdpAnnexBTransport] 组帧完成并上抛: frame={frameId}, bytes={annexB?.Length}");
#if UNITY_EDITOR
                if (verbose)
                {
                    wmj.DebugTools.Info($"[UdpAnnexBTransport] 组帧完成并上抛: frame={frameId}, bytes={annexB?.Length}", wmj.DebugTools.LogCategory.Transport);
                    wmj.DebugTools.WriteDebugLog("[UdpAnnexBTransport] 组帧完成并上抛: frame=" + frameId + ", bytes=" + (annexB?.Length ?? 0), "INFO");
                }
#endif
                if (verbose)
                    wmj.DebugTools.WriteRunLog("[UdpAnnexBTransport] 组帧完成并上抛: frame=" + frameId, "INFO");
                OnAnnexBFrame?.Invoke(annexB);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
