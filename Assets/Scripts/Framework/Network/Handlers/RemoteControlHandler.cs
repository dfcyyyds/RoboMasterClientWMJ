using Google.Protobuf;
using System;

// 处理 RemoteControl 协议消息
public class RemoteControlHandler : IMessageHandler
{
    public event Action<RemoteControl> OnRemoteControlReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var ctrl = RemoteControl.Parser.ParseFrom(payload);
            OnRemoteControlReceived?.Invoke(ctrl);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[RemoteControlHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[RemoteControlHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[RemoteControlHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}

// 注意：RemoteControl 类应由 protoc 自动生成，位于 Generated 目录。