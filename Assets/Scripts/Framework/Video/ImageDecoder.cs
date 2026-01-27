using System;
using System.Collections.Concurrent;

namespace Framework.Video
{
    /// 仅用于调试：将输入视为整帧的PNG/JPEG字节并原样返回（像素未解析）。
    /// 注意：Unity Texture2D 的创建与 LoadImage 必须在主线程，本解码器不做实际解码，仅作为占位。
    public class ImageDecoder : IVideoDecoder
    {
        private readonly ConcurrentQueue<DecodedFrame> queue = new ConcurrentQueue<DecodedFrame>();

        public void Push(byte[] annexBBytes)
        {
            // 非HEVC场景下，允许上层在主线程自行用 LoadImage 解析，这里不转换像素。
            if (annexBBytes == null || annexBBytes.Length == 0) return;
            // 不可知宽高，返回空帧（上层忽略）。
        }

        public bool TryGetFrame(out DecodedFrame frame)
        {
            frame = null;
            return false;
        }

        public void Dispose() { }
    }
}
