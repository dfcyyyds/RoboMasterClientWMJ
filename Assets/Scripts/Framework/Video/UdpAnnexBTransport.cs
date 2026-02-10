using System;
using UnityEngine;

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

        public UdpAnnexBTransport(float timeoutSec = 0.1f, int maxBufferedFrames = 8, bool verbose = false)
        {
            this.verbose = verbose;
            // 低延迟取舍：提高估计吞吐，缩短动态超时
            assembler = new AnnexBFrameAssembler(timeoutSec, maxBufferedFrames, verbose, bytesPerSecForTimeout: 800000f);
        }

        public void Start()
        {
            if (started) return;
            started = true;
            NetworkManager.Instance.OnUdpVideoFrame += OnUdpSlice;
            wmj.Log.I("[UdpAnnexBTransport] 已订阅 UDP 视频分片事件", wmj.Log.Tag.Transport);
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            NetworkManager.Instance.OnUdpVideoFrame -= OnUdpSlice;
            wmj.Log.I("[UdpAnnexBTransport] 已取消订阅 UDP 视频分片事件", wmj.Log.Tag.Transport);
        }

        private void OnUdpSlice(UdpVideoFrame slice)
        {
            // 接入现有组帧器
            assembler.AddSlice(slice);
            // 尝试取出尽可能多的完整帧
            while (assembler.TryAssemble(out var frameId, out var annexB))
            {
                if (verbose)
                    wmj.Log.I($"[UdpAnnexBTransport] 组帧完成并上抛: frame={frameId}, bytes={annexB?.Length}", wmj.Log.Tag.Transport);
                OnAnnexBFrame?.Invoke(annexB);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
