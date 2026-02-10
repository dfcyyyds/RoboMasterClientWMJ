using Google.Protobuf;
using System;

// 处理 CustomByteBlock 协议消息
public class CustomByteBlockHandler : IMessageHandler
{
    public event Action<CustomByteBlock> OnCustomByteBlockReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var block = CustomByteBlock.Parser.ParseFrom(payload);
            OnCustomByteBlockReceived?.Invoke(block);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[CustomByteBlockHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}

// 注意：CustomByteBlock 类应由 protoc 自动生成，位于 Generated 目录。