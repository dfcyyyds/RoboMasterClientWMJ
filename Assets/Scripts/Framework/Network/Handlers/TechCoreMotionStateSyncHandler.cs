using Google.Protobuf;
using System;

// 处理 TechCoreMotionStateSync 协议消息
public class TechCoreMotionStateSyncHandler : IMessageHandler
{
    public event Action<TechCoreMotionStateSync> OnTechCoreMotionStateSyncReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = TechCoreMotionStateSync.Parser.ParseFrom(payload);
            OnTechCoreMotionStateSyncReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[TechCoreMotionStateSyncHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
