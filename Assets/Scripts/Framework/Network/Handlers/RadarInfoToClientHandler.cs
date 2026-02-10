using Google.Protobuf;
using System;

// 处理 RadarInfoToClient 协议消息（原 RaderInfoToClient，修正拼写）
public class RadarInfoToClientHandler : IMessageHandler
{
    public event Action<RadarInfoToClient> OnRadarInfoToClientReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RadarInfoToClient.Parser.ParseFrom(payload);
            OnRadarInfoToClientReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RadarInfoToClientHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
