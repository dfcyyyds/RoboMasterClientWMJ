using Google.Protobuf;
using System;

// 处理 DartSelectTargetStatusSync 协议消息
public class DartSelectTargetStatusSyncHandler : IMessageHandler
{
    public event Action<DartSelectTargetStatusSync> OnDartSelectTargetStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = DartSelectTargetStatusSync.Parser.ParseFrom(payload);
            OnDartSelectTargetStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[DartSelectTargetStatusSyncHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[DartSelectTargetStatusSyncHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[DartSelectTargetStatusSyncHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}
