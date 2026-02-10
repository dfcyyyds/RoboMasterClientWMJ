using Google.Protobuf;
using System;

// 处理 RobotPosition 协议消息
public class RobotPositionHandler : IMessageHandler
{
    public event Action<RobotPosition> OnRobotPositionReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotPosition.Parser.ParseFrom(payload);
            OnRobotPositionReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RobotPositionHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
