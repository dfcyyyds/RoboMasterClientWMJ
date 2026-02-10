using Google.Protobuf;
using System;

// 处理 Buff 协议消息
public class BuffHandler : IMessageHandler
{
    public event Action<Buff> OnBuffReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = Buff.Parser.ParseFrom(payload);
            OnBuffReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[BuffHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
