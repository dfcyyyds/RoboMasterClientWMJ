using Google.Protobuf;
using System;

// 处理 DartSelectTargetStatusSync 协议消息
public class DartSelectTargetStatusSyncHandler : IMessageHandler
{
    public event Action<DartSelectTargetStatusSync> OnDartSelectTargetStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = DartSelectTargetStatusSync.Parser.ParseFrom(payload);
            OnDartSelectTargetStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[DartSelectTargetStatusSyncHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
