using System.Diagnostics;
using UnityEngine;

namespace Framework.Utils
{
    // 通过 Conditional 宏控制调用是否编译进来；配合 Editor 工具切换脚本宏
    public static class DebugLog
    {
        // 通用调试
        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void General(string message) { UnityEngine.Debug.Log(message); }
        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void GeneralWarning(string message) { UnityEngine.Debug.LogWarning(message); }
        [Conditional("DEBUG_GENERAL_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void GeneralFormat(string format, params object[] args) { UnityEngine.Debug.Log(string.Format(format, args)); }

        // 数据传输（UDP/MQTT等）
        [Conditional("DEBUG_TRANSPORT_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void Transport(string message) { UnityEngine.Debug.Log(message); }
        [Conditional("DEBUG_TRANSPORT_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void TransportWarning(string message) { UnityEngine.Debug.LogWarning(message); }

        // 图传（视频流）
        [Conditional("DEBUG_VIDEO_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void Video(string message) { UnityEngine.Debug.Log(message); }
        [Conditional("DEBUG_VIDEO_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void VideoWarning(string message) { UnityEngine.Debug.LogWarning(message); }

        // 解码器/ffmpeg 相关
        [Conditional("DEBUG_DECODER_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void Decoder(string message) { UnityEngine.Debug.Log(message); }
        [Conditional("DEBUG_DECODER_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void DecoderWarning(string message) { UnityEngine.Debug.LogWarning(message); }

        // 网络管理/注册/分发
        [Conditional("DEBUG_NETWORK_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void Network(string message) { UnityEngine.Debug.Log(message); }
        [Conditional("DEBUG_NETWORK_LOG")]
        [Conditional("DEBUG_ALL_LOG")]
        public static void NetworkWarning(string message) { UnityEngine.Debug.LogWarning(message); }

        // 错误类日志通常应该总是输出；保留统一入口
        public static void Error(string message) { UnityEngine.Debug.LogError(message); }
        public static void ErrorFormat(string format, params object[] args) { UnityEngine.Debug.LogError(string.Format(format, args)); }
    }
}
