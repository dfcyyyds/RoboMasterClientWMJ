using Google.Protobuf;
using System;

// 处理 AirSupportStatusSync 协议消息
public class AirSupportStatusSyncHandler : IMessageHandler
{
    public event Action<AirSupportStatusSync> OnAirSupportStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = AirSupportStatusSync.Parser.ParseFrom(payload);
            OnAirSupportStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[AirSupportStatusSyncHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
