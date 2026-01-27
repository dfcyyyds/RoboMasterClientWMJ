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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[EventHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[EventHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[EventHandler] 解析失败: " + ex.Message);
        }
    }
}
// Event 类应由 protoc 自动生成，位于 Generated 目录。
