using Google.Protobuf;
using System;

// 处理 SentinelStatusSync 协议消息
public class SentinelStatusSyncHandler : IMessageHandler
{
    public event Action<SentinelStatusSync> OnSentinelStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = SentinelStatusSync.Parser.ParseFrom(payload);
            OnSentinelStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[SentinelStatusSyncHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[SentinelStatusSyncHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[SentinelStatusSyncHandler] 解析失败: " + ex.Message);
        }
    }
}
