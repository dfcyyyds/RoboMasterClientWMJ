using System;

// 用于 NetworkManager 消息分发的标准接口
public interface IMessageHandler
{
    /// <summary>
    /// 处理收到的消息（传统 byte[] 入口）
    /// </summary>
    /// <param name="topic">消息主题</param>
    /// <param name="payload">消息内容（字节数组）</param>
    void HandleMessage(string topic, byte[] payload);
}

/// <summary>
/// 支持零拷贝分发的可选接口：实现后 Dispatcher 会直接下发 ArraySegment，调用方不得在回调结束后持有该引用。
/// </summary>
public interface IMessageSegmentHandler
{
    void HandleMessage(string topic, ArraySegment<byte> payload);
}
