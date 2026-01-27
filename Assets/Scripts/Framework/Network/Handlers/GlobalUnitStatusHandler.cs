using Google.Protobuf;
using System;

// 处理 GlobalUnitStatus 协议消息
public class GlobalUnitStatusHandler : IMessageHandler
{
    public event Action<GlobalUnitStatus> OnGlobalUnitStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var status = GlobalUnitStatus.Parser.ParseFrom(payload);
            OnGlobalUnitStatusReceived?.Invoke(status);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[GlobalUnitStatusHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.Error("[GlobalUnitStatusHandler] 解析失败: " + ex.Message);
#endif
            wmj.DebugTools.Error("[GlobalUnitStatusHandler] 解析失败: " + ex.Message);
        }
    }
}

// 注意：GlobalUnitStatus 类应由 protoc 自动生成，位于 Generated 目录。