using Google.Protobuf;
using System;

// 处理 RaderInfoToClient 协议消息
public class RaderInfoToClientHandler : IMessageHandler
{
    public event Action<RaderInfoToClient> OnRaderInfoToClientReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RaderInfoToClient.Parser.ParseFrom(payload);
            OnRaderInfoToClientReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RaderInfoToClientHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
