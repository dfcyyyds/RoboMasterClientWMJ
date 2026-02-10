using Google.Protobuf;
using System;

// 处理 RobotDynamicStatus 协议消息
public class RobotDynamicStatusHandler : IMessageHandler
{
    public event Action<RobotDynamicStatus> OnRobotDynamicStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotDynamicStatus.Parser.ParseFrom(payload);
            OnRobotDynamicStatusReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RobotDynamicStatusHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
