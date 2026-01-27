using Google.Protobuf;
using System;

// 处理 GameStatus 协议消息
public class GameStatusHandler : IMessageHandler
{
    public event Action<GameStatus> OnGameStatusReceived;

    public void HandleMessage(string topic, byte[] payload)
    {
        try
        {
            var status = GameStatus.Parser.ParseFrom(payload);
            OnGameStatusReceived?.Invoke(status);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[GameStatusHandler] 解析失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[GameStatusHandler] 解析失败: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[GameStatusHandler] 解析失败: " + ex.Message, "ERROR");
        }
    }
}

// 注意：GameStatus 类应由 protoc 自动生成，位于 Generated 目录。