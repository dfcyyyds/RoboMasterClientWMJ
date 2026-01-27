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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[BuffHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[BuffHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[BuffHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}
