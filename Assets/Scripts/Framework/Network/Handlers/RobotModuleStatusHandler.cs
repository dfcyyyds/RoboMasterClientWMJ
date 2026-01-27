using Google.Protobuf;
using System;

// 处理 RobotModuleStatus 协议消息
public class RobotModuleStatusHandler : IMessageHandler
{
    public event Action<RobotModuleStatus> OnRobotModuleStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotModuleStatus.Parser.ParseFrom(payload);
            OnRobotModuleStatusReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RobotModuleStatusHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[RobotModuleStatusHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[RobotModuleStatusHandler] 解析失败: " + ex.Message);
        }
    }
}
