using Google.Protobuf;
using System;

// 处理 SentryStatusSync 协议消息（原 SentinelStatusSync）
public class SentryStatusSyncHandler : IMessageHandler
{
    public event Action<SentryStatusSync> OnSentryStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = SentryStatusSync.Parser.ParseFrom(payload);
            OnSentryStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[SentryStatusSyncHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
