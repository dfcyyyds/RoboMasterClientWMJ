using Google.Protobuf;
using System;

// 处理 RuneStatusSync 协议消息
public class RuneStatusSyncHandler : IMessageHandler
{
    public event Action<RuneStatusSync> OnRuneStatusSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = RuneStatusSync.Parser.ParseFrom(payload);
            OnRuneStatusSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RuneStatusSyncHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[RuneStatusSyncHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[RuneStatusSyncHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}
