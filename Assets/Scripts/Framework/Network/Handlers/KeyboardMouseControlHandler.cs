using Google.Protobuf;
using System;

// 处理 KeyboardMouseControl 协议消息（原 RemoteControl 拆分）
public class KeyboardMouseControlHandler : IMessageHandler
{
    public event Action<KeyboardMouseControl> OnKeyboardMouseControlReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var msg = KeyboardMouseControl.Parser.ParseFrom(payload);
            OnKeyboardMouseControlReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[KeyboardMouseControlHandler] 解析失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }
}
