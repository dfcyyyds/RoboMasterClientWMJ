using UnityEngine;
using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using UI.HUD;
using UI.RobotSelection;

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

    // 比赛模式被动观察：拦截本客户端主动控制类消息，避免与主客户端冲突
    private readonly HashSet<string> passiveBlockTopics = new HashSet<string>
    {
        "KeyboardMouseControl",
        "CommonCommand",
        "RobotPerformanceSelectionCommand",
        "HeroDeployModeEventCommand",
        "AssemblyCommand",
        "RuneActivateCommand",
        "DartCommand",
        "SentryCtrlCommand",
        "AirSupportCommand"
    };
    private readonly Dictionary<string, float> passiveBlockLogTimes = new Dictionary<string, float>();
    private const float PASSIVE_BLOCK_LOG_INTERVAL = 2f;

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
        Debug.Log("[NetworkManager] ===== Awake() 开始执行 =====");

        // 读取队列长度配置
        try
        {
            currentQueueLimit = ConfigLoader.config.initialFileQueueSize;
            maxQueueLimit = ConfigLoader.config.maxFileQueueSize;
            wmj.Log.I($"初始化文件队列: 初始={currentQueueLimit}, 上限={maxQueueLimit}", wmj.Log.Tag.Network);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NetworkManager] 读取队列配置失败，使用默认值(8/32): {ex.Message}");
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

        // 吊射图传 UDP 接收器：仅在仿真模式下启用（接收 MockServer 推送的 H.264）。
        // 比赛模式下，吊射图传只能走官方 CustomByteBlock(0x0310) → MQTT 通道，
        // 不再启动 UDP 接收器，避免占用端口 / 误导诊断。
        if (!GameParamsConfig.Get.isCompetitionMode && LobShotUdpReceiver.Instance == null)
        {
            var lobUdpGO = new GameObject("[LobShotUdpReceiver]");
            lobUdpGO.AddComponent<LobShotUdpReceiver>();
            wmj.Log.I("[NetworkManager] 仿真模式: 自动创建 LobShotUdpReceiver", wmj.Log.Tag.Network);
        }

        // 订阅消息接收事件
        mqttService.OnMessageReceived += dispatcher.Dispatch;
        // UDP 图传数据分发：优先零拷贝段分发，回退到 byte[]
        // 关键优化：吊射模式激活时，完全跳过主图传 VideoStream 分发，
        // 避免 UdpVideoHandler/AnnexB 组帧/Ffmpeg 解码链路继续消耗 CPU/GPU。
        udpService.OnMessageReceivedSegment += (remoteEp, data) =>
        {
            if (LobShotUdpReceiver.ActiveH264Transport != null)
                return;
            dispatcher.DispatchSegment("VideoStream", data);
        };
        udpService.OnMessageReceived += (remoteEp, data) =>
        {
            if (LobShotUdpReceiver.ActiveH264Transport != null)
                return;
            dispatcher.Dispatch("VideoStream", data);
        };

        // 订阅网络状态事件
        mqttService.OnConnected += () => { OnMqttConnected?.Invoke(); UpdateStatus("✅ 服务器已连接"); };
        mqttService.OnDisconnected += () => { OnMqttDisconnected?.Invoke(); UpdateStatus("⚠ MQTT 连接断开，正在重连..."); };
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
        // 创建自检状态 UI（右下角）
        EnsureConnectionStatusHUD();
        UpdateStatus("等待兵种选择...");

        // 仿真模式允许先用随机 GUID 连接（MockServer 不校验 clientId）
        if (!GameParamsConfig.Get.isCompetitionMode)
        {
            wmj.Log.I("[NetworkManager] 仿真模式: 立即连接 MockServer", wmj.Log.Tag.Network);
            string playerTerminalId = null;
            if (RobotSelectionBootstrap.IsSelectionCompleted && RobotSelectionBootstrap.CurrentSelection != null)
                playerTerminalId = RobotSelectionBootstrap.CurrentSelection.PlayerTerminalId;
            ConnectToServer(playerTerminalId);

            // 兵种选完后用正确 ID 重连
            if (!RobotSelectionBootstrap.IsSelectionCompleted)
                RobotSelectionBootstrap.OnSelectionCompleted += OnRobotSelectionCompleted;
            return;
        }

        // ═══ 比赛模式: 必须等兵种选择完成才连接 ═══
        if (RobotSelectionBootstrap.IsSelectionCompleted && RobotSelectionBootstrap.CurrentSelection != null)
        {
            // 极少数情况：选择已在 Start 前完成
            ConnectToServer(RobotSelectionBootstrap.CurrentSelection.PlayerTerminalId);
        }
        else
        {
            wmj.Log.I("[NetworkManager] 比赛模式: 等待兵种选择完成后连接...", wmj.Log.Tag.Network);
            RobotSelectionBootstrap.OnSelectionCompleted += OnRobotSelectionCompletedFirstConnect;
        }
    }

    /// <summary>比赛模式首次连接（兵种选择完成后触发）</summary>
    private void OnRobotSelectionCompletedFirstConnect(RobotSelectionResult result)
    {
        RobotSelectionBootstrap.OnSelectionCompleted -= OnRobotSelectionCompletedFirstConnect;

        if (result != null && !string.IsNullOrEmpty(result.PlayerTerminalId))
        {
            wmj.Log.I($"[NetworkManager] 兵种选择完成，开始连接: clientId={result.PlayerTerminalId} ({result})", wmj.Log.Tag.Network);
            UpdateStatus($"正在连接服务器... (ID: {result.PlayerTerminalId})");
            ConnectToServer(result.PlayerTerminalId);
        }
        else
        {
            wmj.Log.E("[NetworkManager] 兵种选择完成但未获取到有效的选手端 ID，无法连接", wmj.Log.Tag.Network);
            UpdateStatus("❌ 选手端 ID 无效，无法连接");
        }
    }

    /// <summary>实际连接逻辑</summary>
    private void ConnectToServer(string playerTerminalId)
    {
        try
        {
            wmj.Log.I("[NetworkManager] 启动网络服务...", wmj.Log.Tag.Network);

            string connectIp = ConfigLoader.config.ip;
            int connectDataPort = ConfigLoader.config.dataPort;
            int connectVideoPort = ConfigLoader.config.videoPort;

            if (GameParamsConfig.Get.isCompetitionMode)
            {
                connectIp = GameParamsConfig.Get.competitionServerIp;
                connectDataPort = GameParamsConfig.Get.competitionServerPort;
                connectVideoPort = connectDataPort + 1;
                wmj.Log.I($"[NetworkManager] 比赛模式: 连接官方服务器 {connectIp}:{connectDataPort} (视频: {connectVideoPort})", wmj.Log.Tag.Network);
            }
            else
            {
                connectIp = "127.0.0.1";
                wmj.Log.I($"[NetworkManager] 仿真模式: 连接本机 MockServer {connectIp}:{connectDataPort} (视频: {connectVideoPort})", wmj.Log.Tag.Network);
            }

            UpdateStatus($"正在连接 {connectIp}:{connectDataPort}...");
            wmj.Log.I($"[NetworkManager] 最终连接参数: MQTT={connectIp}:{connectDataPort}, UDP={connectIp}:{connectVideoPort}, clientId={playerTerminalId ?? "(随机)"}", wmj.Log.Tag.Network);
            mqttService.Connect(connectIp, connectDataPort, playerTerminalId);
            udpService.StartReceive(connectIp, connectVideoPort);
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[NetworkManager] 启动网络服务异常: {ex.Message}", wmj.Log.Tag.Network);
            UpdateStatus($"❌ 连接异常: {ex.Message}");
        }
    }

    void OnDestroy()
    {
        try
        {
            RobotSelectionBootstrap.OnSelectionCompleted -= OnRobotSelectionCompleted;
            RobotSelectionBootstrap.OnSelectionCompleted -= OnRobotSelectionCompletedFirstConnect;
            wmj.Log.I("[NetworkManager] 销毁释放资源...", wmj.Log.Tag.Network);
            mqttService?.Disconnect();
            udpService?.StopReceive();
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[NetworkManager] 销毁释放异常: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    /// <summary>
    /// 仿真模式：兵种选择完成后，用正确的选手端 ID 重新连接 MQTT
    /// </summary>
    private void OnRobotSelectionCompleted(RobotSelectionResult result)
    {
        RobotSelectionBootstrap.OnSelectionCompleted -= OnRobotSelectionCompleted;

        if (result != null && !string.IsNullOrEmpty(result.PlayerTerminalId))
        {
            wmj.Log.I($"[NetworkManager] 兵种选择完成，用正确 clientId 重连 MQTT: {result.PlayerTerminalId} ({result})", wmj.Log.Tag.Network);
            UpdateStatus($"重连中... (ID: {result.PlayerTerminalId})");
            mqttService.Reconnect(result.PlayerTerminalId);
        }
        else
        {
            wmj.Log.W("[NetworkManager] 兵种选择完成但未获取到有效的选手端 ID", wmj.Log.Tag.Network);
        }
    }

    // ═══════════════════ 自检状态 UI ═══════════════════

    private ConnectionStatusHUD statusHUD;

    private void EnsureConnectionStatusHUD()
    {
        if (statusHUD != null) return;
        var go = new GameObject("[ConnectionStatusHUD]");
        go.transform.SetParent(transform);
        statusHUD = go.AddComponent<ConnectionStatusHUD>();
    }

    private void UpdateStatus(string message)
    {
        if (statusHUD != null)
            statusHUD.SetStatus(message);
    }

    void OnApplicationQuit()
    {
        try
        {
            wmj.Log.I("[NetworkManager] OnApplicationQuit 释放资源", wmj.Log.Tag.Network);
            mqttService?.Shutdown();   // ← 使用 Shutdown 停止重连循环，避免后台 TCP 阻塞退出
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
        if (GameParamsConfig.Get.isCompetitionMode
            && GameParamsConfig.Get.competitionPassiveObserverMode
            && passiveBlockTopics.Contains(topic))
        {
            float now = Time.realtimeSinceStartup;
            if (!passiveBlockLogTimes.TryGetValue(topic, out var last)
                || now - last >= PASSIVE_BLOCK_LOG_INTERVAL)
            {
                passiveBlockLogTimes[topic] = now;
                wmj.Log.W($"[NetworkManager] 被动观察模式已拦截发送: topic={topic}", wmj.Log.Tag.Network);
            }
            return;
        }

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
        GameParamsConfig.Reload();
        mqttService.Disconnect();
        udpService.StopReceive();

        // 重连时同样使用选手端 ID
        string playerTerminalId = null;
        if (RobotSelectionBootstrap.IsSelectionCompleted && RobotSelectionBootstrap.CurrentSelection != null)
        {
            playerTerminalId = RobotSelectionBootstrap.CurrentSelection.PlayerTerminalId;
        }

        // 比赛模式自动切换到官方服务器
        string connectIp = ConfigLoader.config.ip;
        int connectDataPort = ConfigLoader.config.dataPort;
        int connectVideoPort = ConfigLoader.config.videoPort;

        if (GameParamsConfig.Get.isCompetitionMode)
        {
            connectIp = GameParamsConfig.Get.competitionServerIp;
            connectDataPort = GameParamsConfig.Get.competitionServerPort;
            connectVideoPort = connectDataPort + 1;
            wmj.Log.I($"[NetworkManager] 热更新: 比赛模式连接 {connectIp}:{connectDataPort}", wmj.Log.Tag.Network);
        }
        else
        {
            connectIp = "127.0.0.1";
            wmj.Log.I($"[NetworkManager] 热更新: 仿真模式连接 {connectIp}:{connectDataPort}", wmj.Log.Tag.Network);
        }

        mqttService.Connect(connectIp, connectDataPort, playerTerminalId);
        udpService.StartReceive(connectIp, connectVideoPort);
    }

    // Update is called once per frame
    void Update() { }
}





