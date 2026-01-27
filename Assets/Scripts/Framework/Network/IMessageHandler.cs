// 用于 NetworkManager 消息分发的标准接口
public interface IMessageHandler
{
    /// <summary>
    /// 处理收到的消息
    /// </summary>
    /// <param name="topic">消息主题</param>
    /// <param name="payload">消息内容（字节数组）</param>
    void HandleMessage(string topic, byte[] payload);
}
