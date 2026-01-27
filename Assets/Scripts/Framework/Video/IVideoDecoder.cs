using System;

namespace Framework.Video
{
    /// 解码器接口：将AnnexB HEVC字节流推入并异步产出解码后的帧像素
    public interface IVideoDecoder : IDisposable
    {
        /// 推入一帧AnnexB码流（可包含多个NALU，以帧为单位）
        void Push(byte[] annexBBytes);
        /// 尝试取出一帧解码结果（像素格式RGB24）
        bool TryGetFrame(out DecodedFrame frame);
    }

    public class DecodedFrame
    {
        public int Width;
        public int Height;
        public byte[] Pixels; // RGB24
    }
}
