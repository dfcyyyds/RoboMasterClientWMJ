using System;

namespace Framework.Video
{
    /// 传输抽象：提供 AnnexB 帧输出事件，便于替换底层协议（UDP/RTSP等）
    public interface IVideoTransport : IDisposable
    {
        /// 当组装出完整的 AnnexB 一帧时触发（不含自定义头，仅原始 AnnexB 字节）
        event Action<byte[]> OnAnnexBFrame;

        /// 启动传输（订阅、打开进程或套接字等）
        void Start();

        /// 停止传输（取消订阅、关闭进程或套接字等）
        void Stop();
    }
}
