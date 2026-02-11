using UnityEngine;
using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using UI.HUD;

/// 网络管理器，负责管理MQTT和UDP服务及消息分发
public class NetworkManager : MonoBehaviour
{
    // 单例模式，可获得不可设置
    public static NetworkManager Instance { get; private set; }

    // 网络服务
    private MqttClientService mqttService;
    private UdpClientService udpService;
    // 消息分发器
    private MessageDispatcher dispatcher;
    // UDP视频帧事件转发
    public event Action<UdpVideoFrame> OnUdpVideoFrame;

    // 网络状态事件
    // 当MQTT连接成功/断开，UDP启动/停止时触发
    public event Action OnMqttConnected;
    public event Action OnMqttDisconnected;
    public event Action OnUdpStarted;
    public event Action OnUdpStopped;

    // 消息发送队列（存储待发送的二进制消息）
    private Queue<byte[]> messageSendQueue = new Queue<byte[]>();
    // 队列长度初始值/上限
    private int currentQueueLimit = 8;
    private int maxQueueLimit = 32;

    /// <summary>
    /// 将待发送消息入队，支持自动扩容
    /// </summary>
    public void EnqueueMessageForSend(byte[] message)
    {
        if (messageSendQueue.Count >= currentQueueLimit)
        {
            if (currentQueueLimit < maxQueueLimit)
            {
                int oldLimit = currentQueueLimit;
                currentQueueLimit = Math.Min(currentQueueLimit * 2, maxQueueLimit);
                wmj.Log.I($"消息队列拥塞，触发自动扩容: {oldLimit} -> {currentQueueLimit}", wmj.Log.Tag.Network);
            }
            if (messageSendQueue.Count >= currentQueueLimit)
            {
                var removed = messageSendQueue.Dequeue();
                wmj.Log.E($"消息发送队列已达上限 ({currentQueueLimit})，丢弃最早消息（长度: {removed?.Length}）", wmj.Log.Tag.Network);
            }
        }
        // 入队新消息
        messageSendQueue.Enqueue(message);
        wmj.Log.D($"入队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}", wmj.Log.Tag.Network);
    }

    /// <summary>
    /// 尝试出队一个待发送消息
    /// </summary>
    public bool TryDequeueMessageForSend(out byte[] message)
    {
        if (messageSendQueue.Count > 0)
        {
            message = messageSendQueue.Dequeue();
            wmj.Log.D($"出队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}", wmj.Log.Tag.Network);
            return true;
        }
        message = null;
        return false;
    }

    void Awake()
    {
        // 读取队列长度配置
        try
        {
            currentQueueLimit = ConfigLoader.config.initialFileQueueSize;
            maxQueueLimit = ConfigLoader.config.maxFileQueueSize;
            wmj.Log.I($"初始化文件队列: 初始={currentQueueLimit}, 上限={maxQueueLimit}", wmj.Log.Tag.Network);
        }
        catch (Exception ex)
        {
            wmj.Log.W($"[NetworkManager] 读取队列配置失败，使用默认值(8/32): {ex.Message}", wmj.Log.Tag.Network);
            currentQueueLimit = 8;
            maxQueueLimit = 32;
        }

        // 确保单例模式
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 让对象在场景切换时不被销毁，确保网络管理器全局唯一且常驻
        DontDestroyOnLoad(gameObject);

        // 初始化网络服务和消息分发器
        mqttService = new MqttClientService();
        udpService = new UdpClientService();
        dispatcher = new MessageDispatcher();



        // 注册所有协议Handler并自动同步ProtobufManager
        void RegisterAndSync<THandler, TMsg>(string topic, Action<TMsg> onReceived = null)
            where THandler : IMessageHandler, new()
            where TMsg : class
        {
            var handler = new THandler();
            dispatcher.RegisterHandler(topic, handler);
            var eventInfo = typeof(THandler).GetEvent($"On{typeof(TMsg).Name}Received");
            if (eventInfo != null)
            {
                // 绑定事件：收到消息自动同步ProtobufManager
                eventInfo.AddEventHandler(handler, (Action<TMsg>)((msg) =>
                {
                    Framework.Network.ProtobufManager.Instance.UpdateData(msg);
                    onReceived?.Invoke(msg);
                }));
            }
            mqttService.Subscribe(topic);
        }


        // 服务器->自定义客户端所有协议类型注册
        RegisterAndSync<GameStatusHandler, GameStatus>("GameStatus");
        RegisterAndSync<GlobalUnitStatusHandler, GlobalUnitStatus>("GlobalUnitStatus");
        RegisterAndSync<GlobalLogisticsStatusHandler, GlobalLogisticsStatus>("GlobalLogisticsStatus");
        RegisterAndSync<GlobalSpecialMechanismHandler, GlobalSpecialMechanism>("GlobalSpecialMechanism");
        RegisterAndSync<EventHandler, Event>("Event");
        RegisterAndSync<RobotInjuryStatHandler, RobotInjuryStat>("RobotInjuryStat");
        RegisterAndSync<RobotRespawnStatusHandler, RobotRespawnStatus>("RobotRespawnStatus");
        RegisterAndSync<RobotStaticStatusHandler, RobotStaticStatus>("RobotStaticStatus");
        RegisterAndSync<RobotDynamicStatusHandler, RobotDynamicStatus>("RobotDynamicStatus");
        RegisterAndSync<RobotModuleStatusHandler, RobotModuleStatus>("RobotModuleStatus");
        RegisterAndSync<RobotPositionHandler, RobotPosition>("RobotPosition");
        RegisterAndSync<BuffHandler, Buff>("Buff");
        RegisterAndSync<PenaltyInfoHandler, PenaltyInfo>("PenaltyInfo");
        RegisterAndSync<RobotPathPlanInfoHandler, RobotPathPlanInfo>("RobotPathPlanInfo");
        RegisterAndSync<RadarInfoToClientHandler, RadarInfoToClient>("RadarInfoToClient");
        RegisterAndSync<TechCoreMotionStateSyncHandler, TechCoreMotionStateSync>("TechCoreMotionStateSync");
        RegisterAndSync<RobotPerformanceSelectionSyncHandler, RobotPerformanceSelectionSync>("RobotPerformanceSelectionSync");
        RegisterAndSync<DeployModeStatusSyncHandler, DeployModeStatusSync>("DeployModeStatusSync");
        RegisterAndSync<RuneStatusSyncHandler, RuneStatusSync>("RuneStatusSync");
        RegisterAndSync<SentryStatusSyncHandler, SentryStatusSync>("SentryStatusSync");
        RegisterAndSync<DartSelectTargetStatusSyncHandler, DartSelectTargetStatusSync>("DartSelectTargetStatusSync");
        RegisterAndSync<SentryCtrlResultHandler, SentryCtrlResult>("SentryCtrlResult");
        RegisterAndSync<AirSupportStatusSyncHandler, AirSupportStatusSync>("AirSupportStatusSync");
        RegisterAndSync<CustomByteBlockHandler, CustomByteBlock>("CustomByteBlock");

        // UDP 图传处理器（统一用 "VideoStream" 主题）
        var udpVideoHandler = new UdpVideoHandler();
        dispatcher.RegisterHandler("VideoStream", udpVideoHandler);
        // 转发UDP图传帧事件，便于视频服务订阅
        udpVideoHandler.OnFrameReceived += (frame) => OnUdpVideoFrame?.Invoke(frame);

        // 确保视频流服务常驻（若场景未挂载则自动创建）
        if (VideoStreamService.Instance == null)
        {
            var go = new GameObject("VideoStreamService");
            go.AddComponent<VideoStreamService>();
            wmj.Log.I("[NetworkManager] 自动创建 VideoStreamService", wmj.Log.Tag.Network);
        }

        // 订阅消息接收事件
        mqttService.OnMessageReceived += dispatcher.Dispatch;
        // UDP 图传数据分发：优先零拷贝段分发，回退到 byte[]
        udpService.OnMessageReceivedSegment += (remoteEp, data) => dispatcher.DispatchSegment("VideoStream", data);
        udpService.OnMessageReceived += (remoteEp, data) => dispatcher.Dispatch("VideoStream", data);

        // 订阅网络状态事件
        mqttService.OnConnected += () => OnMqttConnected?.Invoke();
        mqttService.OnDisconnected += () => OnMqttDisconnected?.Invoke();
        udpService.OnStarted += () => OnUdpStarted?.Invoke();
        udpService.OnStopped += () => OnUdpStopped?.Invoke();

        // 业务层订阅ProtobufManager数据变更（如用于MVVM、Loxodon等）
        Framework.Network.ProtobufManager.Instance.OnDataUpdated += (typeName, data) =>
        {
            wmj.Log.D($"[业务] ProtobufManager数据更新: {typeName}", wmj.Log.Tag.Network);
        };

        // 初始化事件通知服务（MonoBehaviour），将 Event / PenaltyInfo 转为 HUD 通知
        if (EventNotificationService.Instance == null)
        {
            var evtGO = new GameObject("EventNotificationService");
            evtGO.AddComponent<EventNotificationService>();
            wmj.Log.I("[NetworkManager] 自动创建 EventNotificationService", wmj.Log.Tag.Network);
        }

        wmj.Log.I("[NetworkManager] Awake 完成", wmj.Log.Tag.Network);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        try
        {
            wmj.Log.I("[NetworkManager] 启动网络服务...", wmj.Log.Tag.Network);

            // 连接MQTT服务器，启动UDP接收
            mqttService.Connect(ConfigLoader.config.ip, ConfigLoader.config.dataPort);
            udpService.StartReceive(ConfigLoader.config.ip, ConfigLoader.config.videoPort);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[NetworkManager] 启动网络服务异常: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    void OnDestroy()
    {
        try
        {
            wmj.Log.I("[NetworkManager] 销毁释放资源...", wmj.Log.Tag.Network);
            mqttService?.Disconnect();
            udpService?.StopReceive();
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[NetworkManager] 销毁释放异常: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    void OnApplicationQuit()
    {
        try
        {
            wmj.Log.I("[NetworkManager] OnApplicationQuit 释放资源", wmj.Log.Tag.Network);
            mqttService?.Disconnect();
            udpService?.StopReceive();
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[NetworkManager] 应用退出释放异常: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    // 注册/注销协议消息处理器
    public void RegisterHandler(string topic, IMessageHandler handler)
    {
        dispatcher.RegisterHandler(topic, handler);
    }

    public void UnregisterHandler(string topic)
    {
        dispatcher.UnregisterHandler(topic);
    }

    public void SendMqttMessage(string topic, byte[] payload)
    {
        mqttService.Publish(topic, payload);
    }

    // 订阅/退订MQTT
    public void SubscribeMqtt(string topic, byte qos = 1)
    {
        mqttService.Subscribe(topic, qos);
    }
    public void UnsubscribeMqtt(string topic)
    {
        mqttService.Unsubscribe(topic);
    }

    // 配置热更新
    public void ReloadConfigAndReconnect()
    {
        ConfigLoader.LoadConfig();
        mqttService.Disconnect();
        udpService.StopReceive();
        mqttService.Connect(ConfigLoader.config.ip, ConfigLoader.config.dataPort);
        udpService.StartReceive(ConfigLoader.config.ip, ConfigLoader.config.videoPort);
    }

    // Update is called once per frame
    void Update() { }
}





