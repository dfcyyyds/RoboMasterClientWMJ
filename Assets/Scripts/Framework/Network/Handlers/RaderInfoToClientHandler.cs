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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RaderInfoToClientHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[RaderInfoToClientHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[RaderInfoToClientHandler] 解析失败: " + ex.Message);
        }
    }
}
