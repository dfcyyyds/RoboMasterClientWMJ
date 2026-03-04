using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using UnityEngine;

/// MQTT 客户端服务，封装连接、订阅、发布等功能
public class MqttClientService
{
    private MqttClient client;  // MQTT客户端的实例引用
    public event Action<string, byte[]> OnMessageReceived;  // 消息接受事件，参数为主题和消息内容
    public event Action OnConnected;  // 连接成功事件
    public event Action OnDisconnected;  // 连接断开事件
    private string brokerIp;  // MQTT服务器的IP地址
    private int brokerPort;  // MQTT服务器的端口号
    private string clientId;  // MQTT客户端的ID
    private bool isConnecting = false;  // 是否正在连接中
    private float reconnectInterval => ConfigLoader.config.mqttReconnectInterval;  // 重连间隔
    // 缓存订阅的主题，确保连接/重连后自动订阅
    private Dictionary<string, byte> subscriptions = new Dictionary<string, byte>();  // 缓存订阅的主题

    // 消息发送队列及发送协程
    private struct MqttSendItem { public string Topic; public byte[] Payload; }
    private Queue<MqttSendItem> sendQueue = new Queue<MqttSendItem>();
    private bool isSending = false;

    // 连接到指定服务器
    public void Connect(string brokerIp, int brokerPort, string clientId = null)
    {
        // 保存连接参数
        this.brokerIp = brokerIp;
        this.brokerPort = brokerPort;
        // 使用传入的选手端 ID 作为 clientId；未提供时回退到随机 GUID（仅用于 MockServer 调试）
        if (!string.IsNullOrEmpty(clientId))
        {
            this.clientId = clientId;
            wmj.Log.I($"[MqttClientService] 使用选手端 clientId: {clientId}", wmj.Log.Tag.Network);
        }
        else
        {
            this.clientId = Guid.NewGuid().ToString();
            wmj.Log.W($"[MqttClientService] 未提供选手端 ID，使用随机 GUID（仅限调试）: {this.clientId}", wmj.Log.Tag.Network);
        }
        TryConnect();
        NetworkManager.Instance.StartCoroutine(ReconnectRoutine());
        wmj.Log.I($"[MqttClientService] 当前重连间隔: {reconnectInterval}s (由参数系统ConfigLoader.config.mqttReconnectInterval控制)", wmj.Log.Tag.Network);
    }

    private void TryConnect()
    {
        // 如果已经成功连接，直接返回
        if (client != null && client.IsConnected) return;
        // 否则尝试连接
        try
        {
            wmj.Log.D("[MqttClientService] 尝试连接MQTT...", wmj.Log.Tag.Network);
            // 以明文（非加密）方式连接指定IP和端口的MQTT服务器，不使用证书
            client = new MqttClient(brokerIp, brokerPort, false, null, null, MqttSslProtocols.None);

            // 为MQTT客户端注册消息接收回调，当MQTT客户端收到任意主题的消息时，自动执行大括号内的代码
            client.MqttMsgPublishReceived += (sender, e) =>
            {
                wmj.Log.I($"[MqttClientService] 收到消息: Topic={e.Topic}, Length={e.Message.Length}", wmj.Log.Tag.Network);
                // 自定义消息接收事件
                OnMessageReceived?.Invoke(e.Topic, e.Message);
            };

            // 为MQTT客户端注册断线回调，当MQTT连接断开时，自动执行大括号内的代码
            client.ConnectionClosed += (sender, e) =>
            {
                wmj.Log.F("[MqttClientService] MQTT连接断开", wmj.Log.Tag.Network);
                // 自定义连接断开事件
                OnDisconnected?.Invoke();
            };

            // 绑定本客户端ID
            client.Connect(clientId);
            wmj.Log.I("[MqttClientService] MQTT连接成功", wmj.Log.Tag.Network);
            // 自定义连接成功事件
            OnConnected?.Invoke();

            // 连接成功后，自动订阅所有已登记的主题（支持重连场景）
            if (subscriptions.Count > 0)
            {
                foreach (var kvp in subscriptions)
                {
                    wmj.Log.I($"[MqttClientService] 重连/连接后自动订阅: {kvp.Key}, QoS={kvp.Value}", wmj.Log.Tag.Network);
                    try
                    {
                        client.Subscribe(new string[] { kvp.Key }, new byte[] { kvp.Value });
                    }
                    catch (Exception subEx)
                    {
                        wmj.Log.W($"[MqttClientService] 自动订阅失败: {kvp.Key}, {subEx.Message}", wmj.Log.Tag.Network);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            wmj.Log.F($"[MqttClientService] 连接失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    private System.Collections.IEnumerator ReconnectRoutine()
    {
        while (true)
        {
            if (client == null || !client.IsConnected)
            {
                if (!isConnecting)
                {
                    TryConnect();
                }
            }
            yield return new WaitForSeconds(reconnectInterval);
        }
    }

    // 发布消息到指定主题
    public void Publish(string topic, byte[] payload)
    {
        lock (sendQueue)
        {
            sendQueue.Enqueue(new MqttSendItem { Topic = topic, Payload = payload });
        }
        if (!isSending)
        {
            isSending = true;
            NetworkManager.Instance.StartCoroutine(SendLoop());
        }
    }

    // 发送队列中的所有消息，队列为空时自动等待
    private System.Collections.IEnumerator SendLoop()
    {
        while (true)
        {
            MqttSendItem? item = null;
            lock (sendQueue)
            {
                if (sendQueue.Count > 0)
                    item = sendQueue.Dequeue();
            }
            if (item.HasValue)
            {
                var topic = item.Value.Topic;
                var payload = item.Value.Payload;
                if (client != null && client.IsConnected)
                {
                    wmj.Log.I($"[MqttClientService] 发布消息: Topic={topic}, Length={payload?.Length}", wmj.Log.Tag.Network);
                    client.Publish(topic, payload);
                }
                yield return null; // 逐帧发送，防止阻塞
            }
            else
            {
                isSending = false;
                yield break;
            }
        }
    }

    // 断开与服务器的连接
    public void Disconnect()
    {
        if (client != null && client.IsConnected)
            client.Disconnect();
        wmj.Log.I("[MqttClientService] 断开连接", wmj.Log.Tag.Network);
    }

    // 订阅指定主题(新版协议Qos全是1,省事了)
    public void Subscribe(string topic, byte qos = 1)
    {
        // 先记录订阅（用于连接后自动订阅与重连）
        subscriptions[topic] = qos;
        if (client != null && client.IsConnected)
        {
            wmj.Log.I($"[MqttClientService] 订阅主题: {topic}, QoS={qos}", wmj.Log.Tag.Network);
            client.Subscribe(new string[] { topic }, new byte[] { qos });
        }
    }

    // 退订指定主题
    public void Unsubscribe(string topic)
    {
        // 移除记录，连接后不再自动订阅
        if (subscriptions.ContainsKey(topic)) subscriptions.Remove(topic);
        if (client != null && client.IsConnected)
        {
            wmj.Log.I($"[MqttClientService] 退订主题: {topic}", wmj.Log.Tag.Network);
            client.Unsubscribe(new string[] { topic });
        }
    }
}

