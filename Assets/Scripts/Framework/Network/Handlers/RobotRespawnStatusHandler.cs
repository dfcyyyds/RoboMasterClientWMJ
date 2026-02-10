using Google.Protobuf;
using System;

// 处理 RobotRespawnStatus 协议消息
public class RobotRespawnStatusHandler : IMessageHandler
{
    public event Action<RobotRespawnStatus> OnRobotRespawnStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotRespawnStatus.Parser.ParseFrom(payload);
            OnRobotRespawnStatusReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RobotRespawnStatusHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
