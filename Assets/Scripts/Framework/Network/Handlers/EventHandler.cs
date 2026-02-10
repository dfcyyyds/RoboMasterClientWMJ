using Google.Protobuf;
using System;

// 处理 Event 协议消息
public class EventHandler : IMessageHandler
{
    public event Action<Event> OnEventReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var status = Event.Parser.ParseFrom(payload);
            OnEventReceived?.Invoke(status);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[EventHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
// Event 类应由 protoc 自动生成，位于 Generated 目录。
