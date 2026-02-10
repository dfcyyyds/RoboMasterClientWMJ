using Google.Protobuf;
using System;

// 处理 SentryCtrlResult 协议消息（原 GuardCtrlResult）
public class SentryCtrlResultHandler : IMessageHandler
{
    public event Action<SentryCtrlResult> OnSentryCtrlResultReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = SentryCtrlResult.Parser.ParseFrom(payload);
            OnSentryCtrlResultReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[SentryCtrlResultHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
