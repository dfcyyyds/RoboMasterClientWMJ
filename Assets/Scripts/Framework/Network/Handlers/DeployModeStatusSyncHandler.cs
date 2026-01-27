using Google.Protobuf;
using System;

// 处理 DeployModeStatusSync 协议消息
public class DeployModeStatusSyncHandler : IMessageHandler
{
    public event Action<DeployModeStatusSync> OnDeployModeStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = DeployModeStatusSync.Parser.ParseFrom(payload);
            OnDeployModeStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[DeployModeStatusSyncHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[DeployModeStatusSyncHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[DeployModeStatusSyncHandler] 解析失败: " + ex.Message);
        }
    }
}
