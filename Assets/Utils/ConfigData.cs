using UnityEngine;

[System.Serializable]
public class ConfigData
{
    /// <summary>官方服务器ip</summary>
    public string ip;
    /// <summary>官方服务器数据端口</summary>
    public int dataPort;
    /// <summary>官方服务器视频端口</summary>
    public int videoPort;
    /// <summary>场上一共有几个机器人</summary>
    public int RobotNum;
    /// <summary>本机器人ID</summary>
    public int RobotID;

    /// <summary>消息发送队列初始大小</summary>
    public int initialFileQueueSize;
    /// <summary>消息发送队列最大大小</summary>
    public int maxFileQueueSize;
    /// <summary>MQTT重连间隔（秒）</summary>
    public float mqttReconnectInterval;

    // 视频相关参数
    /// <summary>主线程每帧最大解码帧数</summary>
    public int maxDrainPerUpdate = 1;
    /// <summary>解码输出分辨率-宽</summary>
    public int decoderOutputWidth = 960;
    /// <summary>解码输出分辨率-高</summary>
    public int decoderOutputHeight = 540;
    /// <summary>解码队列上限</summary>
    public int decoderQueueSize = 6;

    /// <summary>日志缓冲区大小（积攒多少条日志后批量写入，建议16~256，越大越省IO）</summary>
    public int logBufferSize = 32;
}
