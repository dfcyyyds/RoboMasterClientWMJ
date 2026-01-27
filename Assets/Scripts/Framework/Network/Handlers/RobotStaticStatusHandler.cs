using Google.Protobuf;
using System;

// 处理 RobotStaticStatus 协议消息
public class RobotStaticStatusHandler : IMessageHandler
{
    public event Action<RobotStaticStatus> OnRobotStaticStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotStaticStatus.Parser.ParseFrom(payload);
            OnRobotStaticStatusReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RobotStaticStatusHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[RobotStaticStatusHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[RobotStaticStatusHandler] 解析失败: " + ex.Message);
        }
    }
}
