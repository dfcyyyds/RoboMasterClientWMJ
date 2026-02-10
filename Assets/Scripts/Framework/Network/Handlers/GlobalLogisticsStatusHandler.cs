using Google.Protobuf;
using System;

// 处理 GlobalLogisticsStatus 协议消息
public class GlobalLogisticsStatusHandler : IMessageHandler
{
    public event Action<GlobalLogisticsStatus> OnGlobalLogisticsStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var status = GlobalLogisticsStatus.Parser.ParseFrom(payload);
            OnGlobalLogisticsStatusReceived?.Invoke(status);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[GlobalLogisticsStatusHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
// GlobalLogisticsStatus 类应由 protoc 自动生成，位于 Generated 目录。
