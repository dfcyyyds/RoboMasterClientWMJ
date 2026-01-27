using Google.Protobuf;
using System;

// 处理 GuardCtrlResult 协议消息
public class GuardCtrlResultHandler : IMessageHandler
{
    public event Action<GuardCtrlResult> OnGuardCtrlResultReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = GuardCtrlResult.Parser.ParseFrom(payload);
            OnGuardCtrlResultReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[GuardCtrlResultHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[GuardCtrlResultHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[GuardCtrlResultHandler] 解析失败: " + ex.Message);
        }
    }
}
