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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RobotPathPlanInfoHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[RobotPathPlanInfoHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[RobotPathPlanInfoHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}
