using Google.Protobuf;
using System;

// 处理 RobotPerformanceSelectionSync 协议消息
public class RobotPerformanceSelectionSyncHandler : IMessageHandler
{
    public event Action<RobotPerformanceSelectionSync> OnRobotPerformanceSelectionSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RobotPerformanceSelectionSync.Parser.ParseFrom(payload);
            OnRobotPerformanceSelectionSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RobotPerformanceSelectionSyncHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[RobotPerformanceSelectionSyncHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[RobotPerformanceSelectionSyncHandler] 解析失败: " + ex.Message);
        }
    }
}
