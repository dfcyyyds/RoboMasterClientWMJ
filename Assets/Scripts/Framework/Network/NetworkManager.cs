using UnityEngine;
using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Framework.Utils;

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
#if UNITY_EDITOR
                DebugLog.Network($"消息队列拥塞，触发自动扩容: {oldLimit} -> {currentQueueLimit}");
                wmj.DebugTools.Info($"消息队列拥塞，触发自动扩容: {oldLimit} -> {currentQueueLimit}");
                wmj.DebugTools.WriteDebugLog("消息队列拥塞，触发自动扩容: " + oldLimit + " -> " + currentQueueLimit,"INFO");
#endif
                wmj.DebugTools.WriteRunLog("消息队列拥塞，触发自动扩容: " + oldLimit + " -> " + currentQueueLimit, "INFO");
            }
            if (messageSendQueue.Count >= currentQueueLimit)
            {
                var removed = messageSendQueue.Dequeue();
#if UNITY_EDITOR
                DebugLog.NetworkWarning($"消息发送队列已达上限 ({currentQueueLimit})，丢弃最早消息（长度: {removed?.Length}）");
                wmj.DebugTools.Error($"消息发送队列已达上限 ({currentQueueLimit})，丢弃最早消息（长度: {removed?.Length}）");
                wmj.DebugTools.WriteDebugLog("消息发送队列已达上限 (" + currentQueueLimit + ")，丢弃最早消息（长度: " + removed?.Length + "）","ERROR");
#endif
                wmj.DebugTools.WriteRunLog("消息发送队列已达上限 (" + currentQueueLimit + ")，丢弃最早消息（长度: " + removed?.Length + "）", "ERROR");
            }
        }
        // 入队新消息
        messageSendQueue.Enqueue(message);
#if UNITY_EDITOR
    DebugLog.Network($"入队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}");
    wmj.DebugTools.Info($"入队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}");
    wmj.DebugTools.WriteDebugLog("入队待发送消息: 长度=" + message?.Length + "，队列长度: " + messageSendQueue.Count + "/" + currentQueueLimit,"INFO");
#endif
        wmj.DebugTools.WriteRunLog("入队待发送消息: 长度=" + message?.Length + "，队列长度: " + messageSendQueue.Count + "/" + currentQueueLimit, "INFO");
    }

    /// <summary>
    /// 尝试出队一个待发送消息
    /// </summary>
    public bool TryDequeueMessageForSend(out byte[] message)
    {
        if (messageSendQueue.Count > 0)
        {
            message = messageSendQueue.Dequeue();
#if UNITY_EDITOR
            DebugLog.Network($"出队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}");
            wmj.DebugTools.Info($"出队待发送消息: 长度={message?.Length}，队列长度: {messageSendQueue.Count}/{currentQueueLimit}");
            wmj.DebugTools.WriteDebugLog("出队待发送消息: 长度=" + message?.Length + "，队列长度: " + messageSendQueue.Count + "/" + currentQueueLimit,"INFO");
#endif
            wmj.DebugTools.WriteRunLog("出队待发送消息: 长度=" + message?.Length + "，队列长度: " + messageSendQueue.Count + "/" + currentQueueLimit, "INFO");
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
#if UNITY_EDITOR
            DebugLog.Network($"初始化文件队列: 初始={currentQueueLimit}, 上限={maxQueueLimit}");
            wmj.DebugTools.Info($"初始化文件队列: 初始={currentQueueLimit}, 上限={maxQueueLimit}", wmj.DebugTools.LogCategory.Network);
            wmj.DebugTools.WriteDebugLog("初始化文件队列: 初始=" + currentQueueLimit + ", 上限=" + maxQueueLimit, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("初始化文件队列: 初始=" + currentQueueLimit + ", 上限=" + maxQueueLimit, "INFO");
        }
        catch (Exception ex)
        {
            wmj.DebugTools.Warn($"[NetworkManager] 读取队列配置失败，使用默认值(8/32): {ex.Message}");
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
        RegisterAndSync<RaderInfoToClientHandler, RaderInfoToClient>("RaderInfoToClient");
        RegisterAndSync<TechCoreMotionStateSyncHandler, TechCoreMotionStateSync>("TechCoreMotionStateSync");
        RegisterAndSync<RobotPerformanceSelectionSyncHandler, RobotPerformanceSelectionSync>("RobotPerformanceSelectionSync");
        RegisterAndSync<DeployModeStatusSyncHandler, DeployModeStatusSync>("DeployModeStatusSync");
        RegisterAndSync<RuneStatusSyncHandler, RuneStatusSync>("RuneStatusSync");
        RegisterAndSync<SentinelStatusSyncHandler, SentinelStatusSync>("SentinelStatusSync");
        RegisterAndSync<DartSelectTargetStatusSyncHandler, DartSelectTargetStatusSync>("DartSelectTargetStatusSync");
        RegisterAndSync<GuardCtrlResultHandler, GuardCtrlResult>("GuardCtrlResult");
        RegisterAndSync<AirSupportStatusSyncHandler, AirSupportStatusSync>("AirSupportStatusSync");
        // 其它协议类型如需补充可继续添加...

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
#if UNITY_EDITOR
            DebugLog.Network("[NetworkManager] 自动创建 VideoStreamService");
            wmj.DebugTools.Info("[NetworkManager] 自动创建 VideoStreamService");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 自动创建 VideoStreamService", "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 自动创建 VideoStreamService", "INFO");
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
#if UNITY_EDITOR
                DebugLog.Network($"[业务] ProtobufManager数据更新: {typeName}");
                wmj.DebugTools.Info($"[业务] ProtobufManager数据更新: {typeName}");
                wmj.DebugTools.WriteDebugLog("[业务] ProtobufManager数据更新: " + typeName, "INFO");
#endif
            wmj.DebugTools.WriteRunLog("[业务] ProtobufManager数据更新: " + typeName, "INFO");
        };

#if UNITY_EDITOR
    DebugLog.Network("[NetworkManager] Awake 完成");
    wmj.DebugTools.Info("[NetworkManager] Awake 完成");
    wmj.DebugTools.WriteDebugLog("[NetworkManager] Awake 完成","INFO");
#endif
        wmj.DebugTools.WriteRunLog("[NetworkManager] Awake 完成", "INFO");
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        try
        {
#if UNITY_EDITOR
            DebugLog.Network("[NetworkManager] 启动网络服务...");
            wmj.DebugTools.Info("[NetworkManager] 启动网络服务...");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 启动网络服务...","DEBUG");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 启动网络服务...", "DEBUG");

            // 连接MQTT服务器，启动UDP接收
            mqttService.Connect(ConfigLoader.config.ip, ConfigLoader.config.dataPort);
            udpService.StartReceive(ConfigLoader.config.ip, ConfigLoader.config.videoPort);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            DebugLog.NetworkWarning($"[NetworkManager] 启动网络服务异常: {ex.Message}");
            wmj.DebugTools.Error($"[NetworkManager] 启动网络服务异常: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 启动网络服务异常: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 启动网络服务异常: " + ex.Message, "ERROR");
        }
    }

    void OnDestroy()
    {
        try
        {
#if UNITY_EDITOR
            DebugLog.Network("[NetworkManager] 销毁释放资源...");
            wmj.DebugTools.Info("[NetworkManager] 销毁释放资源...");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 销毁释放资源...","DEBUG");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 销毁释放资源...", "DEBUG");
            mqttService?.Disconnect();
            udpService?.StopReceive();
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            DebugLog.NetworkWarning($"[NetworkManager] 销毁释放异常: {ex.Message}");
            wmj.DebugTools.Error($"[NetworkManager] 销毁释放异常: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 销毁释放异常: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 销毁释放异常: " + ex.Message, "ERROR");
        }
    }

    void OnApplicationQuit()
    {
        try
        {
#if UNITY_EDITOR
            DebugLog.Network("[NetworkManager] OnApplicationQuit 释放资源");
            wmj.DebugTools.Info("[NetworkManager] OnApplicationQuit 释放资源");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] OnApplicationQuit 释放资源","DEBUG");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] OnApplicationQuit 释放资源", "DEBUG");
            mqttService?.Disconnect();
            udpService?.StopReceive();
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR
            DebugLog.NetworkWarning($"[NetworkManager] 应用退出释放异常: {ex.Message}");
            wmj.DebugTools.Error($"[NetworkManager] 应用退出释放异常: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[NetworkManager] 应用退出释放异常: " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[NetworkManager] 应用退出释放异常: " + ex.Message, "ERROR");
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





