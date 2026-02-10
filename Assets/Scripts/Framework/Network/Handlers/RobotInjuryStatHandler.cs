using Google.Protobuf;
using System;

// 处理 RobotInjuryStat 协议消息
public class RobotInjuryStatHandler : IMessageHandler
{
    public event Action<RobotInjuryStat> OnRobotInjuryStatReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotInjuryStat.Parser.ParseFrom(payload);
            OnRobotInjuryStatReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RobotInjuryStatHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
