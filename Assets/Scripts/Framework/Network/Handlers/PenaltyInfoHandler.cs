using Google.Protobuf;
using System;

// 处理 PenaltyInfo 协议消息
public class PenaltyInfoHandler : IMessageHandler
{
    public event Action<PenaltyInfo> OnPenaltyInfoReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = PenaltyInfo.Parser.ParseFrom(payload);
            OnPenaltyInfoReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[PenaltyInfoHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
