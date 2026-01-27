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
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[GlobalLogisticsStatusHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[GlobalLogisticsStatusHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[GlobalLogisticsStatusHandler] 解析失败: " + ex.Message);
        }
    }
}
// GlobalLogisticsStatus 类应由 protoc 自动生成，位于 Generated 目录。
