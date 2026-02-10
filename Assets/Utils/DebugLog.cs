using System;
using System.Diagnostics;

namespace Framework.Utils
{
    // ====================================================================
    // 兼容旧 API 的转发层 —— 所有调用转发到新 wmj.Log 统一框架
    //
    // 旧宏：DEBUG_GENERAL_LOG / DEBUG_ALL_LOG 等
    // 新宏：LOG_GENERAL / LOG_ALL 等（由 LogBridge 桥接）
    //
    // 迁移完成后此文件可删除，请直接使用：
    //   LogBridge.Video.D("msg");   或   Log.I("msg", Log.Tag.Video);
    // ====================================================================
    [Obsolete("请迁移到 wmj.LogBridge 或 wmj.Log。此类仅作过渡兼容。")]
    public static class DebugLog
    {
        // ---- 通用 ----
        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_GENERAL")]
        [Conditional("LOG_ALL")]
        public static void General(string message)
        { wmj.Log.I(message, wmj.Log.Tag.General); }

        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_GENERAL")]
        [Conditional("LOG_ALL")]
        public static void GeneralWarning(string message)
        { wmj.Log.W(message, wmj.Log.Tag.General); }

        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_GENERAL")]
        [Conditional("LOG_ALL")]
        public static void GeneralFormat(string format, params object[] args)
        { wmj.Log.I(string.Format(format, args), wmj.Log.Tag.General); }

        // ---- 数据传输 ----
        [Conditional("DEBUG_TRANSPORT_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_TRANSPORT")]
        [Conditional("LOG_ALL")]
        public static void Transport(string message)
        { wmj.Log.I(message, wmj.Log.Tag.Transport); }

        [Conditional("DEBUG_TRANSPORT_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_TRANSPORT")]
        [Conditional("LOG_ALL")]
        public static void TransportWarning(string message)
        { wmj.Log.W(message, wmj.Log.Tag.Transport); }

        // ---- 图传/视频流 ----
        [Conditional("DEBUG_VIDEO_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_VIDEO")]
        [Conditional("LOG_ALL")]
        public static void Video(string message)
        { wmj.Log.I(message, wmj.Log.Tag.Video); }

        [Conditional("DEBUG_VIDEO_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_VIDEO")]
        [Conditional("LOG_ALL")]
        public static void VideoWarning(string message)
        { wmj.Log.W(message, wmj.Log.Tag.Video); }

        // ---- 解码器/ffmpeg ----
        [Conditional("DEBUG_DECODER_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_DECODER")]
        [Conditional("LOG_ALL")]
        public static void Decoder(string message)
        { wmj.Log.I(message, wmj.Log.Tag.Decoder); }

        [Conditional("DEBUG_DECODER_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_DECODER")]
        [Conditional("LOG_ALL")]
        public static void DecoderWarning(string message)
        { wmj.Log.W(message, wmj.Log.Tag.Decoder); }

        // ---- 网络管理 ----
        [Conditional("DEBUG_NETWORK_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_NETWORK")]
        [Conditional("LOG_ALL")]
        public static void Network(string message)
        { wmj.Log.I(message, wmj.Log.Tag.Network); }

        [Conditional("DEBUG_NETWORK_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        [Conditional("LOG_NETWORK")]
        [Conditional("LOG_ALL")]
        public static void NetworkWarning(string message)
        { wmj.Log.W(message, wmj.Log.Tag.Network); }

        // ---- 错误（始终输出）----
        public static void Error(string message)
        { wmj.Log.E(message, wmj.Log.Tag.General); }

        public static void ErrorFormat(string format, params object[] args)
        { wmj.Log.E(string.Format(format, args), wmj.Log.Tag.General); }
    }
}
