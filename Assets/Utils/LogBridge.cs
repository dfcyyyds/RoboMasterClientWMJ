// ============================================================================
// LogBridge.cs — 编译期分类宏桥接层
//
// 作用：通过 [Conditional] 属性实现"分类宏 × 级别宏"的双重编译裁剪。
//       当某个分类宏未定义时，该分类的所有调用在编译时被完全移除（零开销）。
//       级别控制由 Log.D/I/W 内部的 [Conditional] 实现。
//
// 宏说明（PlayerSettings > Scripting Define Symbols）：
//   LOG_ALL        — 开启所有分类
//   LOG_GENERAL    — 通用日志
//   LOG_NETWORK    — 网络日志
//   LOG_VIDEO      — 视频日志
//   LOG_DECODER    — 解码器日志
//   LOG_TRANSPORT  — 传输日志
//   LOG_UI         — UI 日志
//
// 使用方式：
//   LogBridge.General.D("消息");      // 需 LOG_GENERAL + LOG_LEVEL_DEBUG
//   LogBridge.Video.I("帧统计");      // 需 LOG_VIDEO   + LOG_LEVEL_INFO
//   LogBridge.Network.W("超时");      // 需 LOG_NETWORK + LOG_LEVEL_WARN
//   LogBridge.Decoder.E("解码失败");  // Error始终输出，但 LOG_DECODER 控制是否编译
// ============================================================================

using System.Diagnostics;

namespace wmj
{
    /// <summary>
    /// 编译期分类日志桥接 — 每个分类一个静态类，通过 [Conditional] 宏控制编译
    /// </summary>
    public static class LogBridge
    {
        // ================================================================
        // General — 通用日志
        // ================================================================
        public static class General
        {
            [Conditional("LOG_GENERAL")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.General, f, l); }

            [Conditional("LOG_GENERAL")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.General, f, l); }

            [Conditional("LOG_GENERAL")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.General, f, l); }

            [Conditional("LOG_GENERAL")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.General, f, l); }

            [Conditional("LOG_GENERAL")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.General, f, l); }
        }

        // ================================================================
        // Network — 网络管理日志
        // ================================================================
        public static class Network
        {
            [Conditional("LOG_NETWORK")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.Network, f, l); }

            [Conditional("LOG_NETWORK")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.Network, f, l); }

            [Conditional("LOG_NETWORK")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.Network, f, l); }

            [Conditional("LOG_NETWORK")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.Network, f, l); }

            [Conditional("LOG_NETWORK")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.Network, f, l); }
        }

        // ================================================================
        // Video — 视频流日志
        // ================================================================
        public static class Video
        {
            [Conditional("LOG_VIDEO")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.Video, f, l); }

            [Conditional("LOG_VIDEO")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.Video, f, l); }

            [Conditional("LOG_VIDEO")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.Video, f, l); }

            [Conditional("LOG_VIDEO")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.Video, f, l); }

            [Conditional("LOG_VIDEO")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.Video, f, l); }
        }

        // ================================================================
        // Decoder — 解码器日志
        // ================================================================
        public static class Decoder
        {
            [Conditional("LOG_DECODER")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.Decoder, f, l); }

            [Conditional("LOG_DECODER")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.Decoder, f, l); }

            [Conditional("LOG_DECODER")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.Decoder, f, l); }

            [Conditional("LOG_DECODER")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.Decoder, f, l); }

            [Conditional("LOG_DECODER")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.Decoder, f, l); }
        }

        // ================================================================
        // Transport — 数据传输日志
        // ================================================================
        public static class Transport
        {
            [Conditional("LOG_TRANSPORT")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.Transport, f, l); }

            [Conditional("LOG_TRANSPORT")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.Transport, f, l); }

            [Conditional("LOG_TRANSPORT")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.Transport, f, l); }

            [Conditional("LOG_TRANSPORT")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.Transport, f, l); }

            [Conditional("LOG_TRANSPORT")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.Transport, f, l); }
        }

        // ================================================================
        // UI — UI 日志
        // ================================================================
        public static class UI
        {
            [Conditional("LOG_UI")]
            [Conditional("LOG_ALL")]
            public static void D(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.D(msg, Log.Tag.UI, f, l); }

            [Conditional("LOG_UI")]
            [Conditional("LOG_ALL")]
            public static void I(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.I(msg, Log.Tag.UI, f, l); }

            [Conditional("LOG_UI")]
            [Conditional("LOG_ALL")]
            public static void W(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.W(msg, Log.Tag.UI, f, l); }

            [Conditional("LOG_UI")]
            [Conditional("LOG_ALL")]
            public static void E(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.E(msg, Log.Tag.UI, f, l); }

            [Conditional("LOG_UI")]
            [Conditional("LOG_ALL")]
            public static void F(string msg,
                [System.Runtime.CompilerServices.CallerFilePath] string f = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
            { Log.F(msg, Log.Tag.UI, f, l); }
        }
    }
}
