using Google.Protobuf;
using System;

// 处理 RobotPathPlanInfo 协议消息
public class RobotPathPlanInfoHandler : IMessageHandler
{
    public event Action<RobotPathPlanInfo> OnRobotPathPlanInfoReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotPathPlanInfo.Parser.ParseFrom(payload);
            OnRobotPathPlanInfoReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[RobotPathPlanInfoHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
