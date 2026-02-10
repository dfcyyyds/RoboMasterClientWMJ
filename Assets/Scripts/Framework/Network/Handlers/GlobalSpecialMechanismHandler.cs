using Google.Protobuf;
using System;

// 处理 GlobalSpecialMechanism 协议消息
public class GlobalSpecialMechanismHandler : IMessageHandler
{
    public event Action<GlobalSpecialMechanism> OnGlobalSpecialMechanismReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var status = GlobalSpecialMechanism.Parser.ParseFrom(payload);
            OnGlobalSpecialMechanismReceived?.Invoke(status);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[GlobalSpecialMechanismHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
// GlobalSpecialMechanism 类应由 protoc 自动生成，位于 Generated 目录。
