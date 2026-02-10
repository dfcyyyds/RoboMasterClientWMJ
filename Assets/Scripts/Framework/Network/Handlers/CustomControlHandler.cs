using Google.Protobuf;
using System;

// 处理 CustomControl 协议消息（原 RemoteControl 拆分）
public class CustomControlHandler : IMessageHandler
{
    public event Action<CustomControl> OnCustomControlReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = CustomControl.Parser.ParseFrom(payload);
            OnCustomControlReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[CustomControlHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
