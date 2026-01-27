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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RobotPositionHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[RobotPositionHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[RobotPositionHandler] 解析失败: " + ex.Message);
        }
    }
}
