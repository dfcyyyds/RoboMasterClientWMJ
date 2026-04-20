using System;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using UnityEngine;

/// <summary>
/// MQTT 客户端服务，封装连接、订阅、发布等功能。
///
/// ══ 设计要点（2026-04-18 重构） ══
/// 1. **事件驱动重连**：只在明确的失败信号下触发重连，杜绝靠 IsConnected 轮询导致
///    的"自我挤占循环"（见 2026-04-17 现场日志）。触发源：
///      a) MqttClient.ConnectionClosed 回调
///      b) 首次 Connect 调用
///      c) 外部 Reconnect(newClientId) 调用
///      d) 首包看门狗超时（CONNACK 成功但 N 秒未收到任何业务消息）
/// 2. **显式 keep-alive=20s**：M2Mqtt 默认 60s，过长；在 NAT/防火墙常见 30s 空闲
///    超时下会"连上但收不到数据"。20s 心跳保证 socket 活跃。
/// 3. **首包看门狗**：官方服务器开局后会持续推送 GameStatus 等至少 1Hz 的消息；若
///    连接后 15s 内没有任何消息，判定为"静默连接"，主动重连。
/// 4. **所有 TCP connect 均在 ThreadPool 线程**，避免阻塞主线程。
/// </summary>
public class MqttClientService
{
    // ───────── 公共事件 ─────────
    public event Action<string, byte[]> OnMessageReceived;
    public event Action OnConnected;
    public event Action OnDisconnected;

    // ───────── 内部状态 ─────────
    private MqttClient client;                      // 当前 MQTT 客户端
    private string brokerIp;
    private int brokerPort;
    private string clientId;                         // 当前正在使用的 clientId（候选列表中的一项）
    private string originalClientId;                 // 调用方传入的原始 clientId（用于生成候选）

    // ── clientId 与协议版本的 fallback 候选 ──
    // 官方协议附录二写 clientId 为 "0x0165" 字面量，但不同 broker 实现可能期望：
    //   0x0165 / 0165 / 165 / 357（十进制）
    // 配合 MQTT 协议版本 3.1.1 与 3.1，形成 N×2 组合，逐一尝试直到 CONNACK 接受。
    private List<string> clientIdCandidates = new List<string>();
    private readonly MqttProtocolVersion[] protocolCandidates = new[]
    {
        MqttProtocolVersion.Version_3_1_1,
        MqttProtocolVersion.Version_3_1
    };
    private int clientIdIdx = 0;
    private int protocolIdx = 0;

    private volatile bool isConnecting = false;     // 是否正在发起连接
    private volatile bool isShuttingDown = false;   // 退出标志
    private volatile bool needsReconnect = true;    // 需要重连（初始 true 触发首次连接）
    private volatile bool routineRunning = false;   // 协程是否已在运行

    private float lastConnectedTime = -999f;        // 上次 CONNACK 成功时间（秒）
    private float lastMessageTime = -999f;          // 上次收到任意 MQTT 消息时间（秒）
    private int connectAttempt = 0;                 // 累计连接尝试计数

    // ───────── 配置常量 ─────────
    // keep-alive 与昨天能连上的版本保持一致（M2Mqtt 默认 60s），避免 broker 因最小 keep-alive 限制返回 0x02
    private const ushort KEEP_ALIVE_PERIOD = 60;           // MQTT keep-alive（秒）
    private const float FIRST_MESSAGE_TIMEOUT = 15f;       // 首包看门狗超时（秒）
    private const float MIN_RECONNECT_INTERVAL = 2f;       // 最小重连间隔（秒）
    private float reconnectInterval => Mathf.Max(MIN_RECONNECT_INTERVAL, ConfigLoader.config.mqttReconnectInterval);

    private Dictionary<string, byte> subscriptions = new Dictionary<string, byte>();

    private struct MqttSendItem { public string Topic; public byte[] Payload; }
    private Queue<MqttSendItem> sendQueue = new Queue<MqttSendItem>();
    private bool isSending = false;

    // 发布丢弃告警节流：避免未连接时每秒刷几十条 WARN 污染日志
    private float lastDropWarnTime = -999f;
    private int dropWarnCount = 0;

    // ══════════════════════════════════════════════════════
    //  公共 API
    // ══════════════════════════════════════════════════════

    public void Connect(string brokerIp, int brokerPort, string clientId = null)
    {
        this.brokerIp = brokerIp;
        this.brokerPort = brokerPort;
        this.originalClientId = string.IsNullOrEmpty(clientId) ? Guid.NewGuid().ToString() : clientId;
        BuildClientIdCandidates(this.originalClientId);
        clientIdIdx = 0;
        protocolIdx = 0;
        this.clientId = clientIdCandidates[0];

        if (!string.IsNullOrEmpty(clientId))
            wmj.Log.I($"[MqttClientService] 使用选手端 clientId: {this.clientId} | 候选共 {clientIdCandidates.Count} 个: [{string.Join(", ", clientIdCandidates)}]", wmj.Log.Tag.Network);
        else
            wmj.Log.W($"[MqttClientService] 未提供选手端 ID，使用随机 GUID（仅限调试）: {this.clientId}", wmj.Log.Tag.Network);

        needsReconnect = true;
        EnsureRoutineStarted();
        wmj.Log.I($"[MqttClientService] 已启动事件驱动重连协程 | 检查间隔={reconnectInterval}s keep-alive={KEEP_ALIVE_PERIOD}s 首包看门狗={FIRST_MESSAGE_TIMEOUT}s", wmj.Log.Tag.Network);
    }

    /// <summary>
    /// 基于原始 clientId 生成候选列表。
    /// 原始 = "0x0165" 时生成: ["0x0165", "0165", "165", "357", "0X0165", "101"]。
    /// 原始 = 其他字符串时仅生成自身。
    /// </summary>
    private void BuildClientIdCandidates(string primary)
    {
        clientIdCandidates.Clear();
        if (string.IsNullOrEmpty(primary)) { clientIdCandidates.Add(Guid.NewGuid().ToString("N").Substring(0, 8)); return; }

        // 0) 若用户在 game_params.json 提供了 mqttClientIdOverride，则只用它，跳过所有 fallback
        var overrideId = GameParamsConfig.Get?.mqttClientIdOverride;
        if (!string.IsNullOrEmpty(overrideId))
        {
            clientIdCandidates.Add(overrideId);
            wmj.Log.I($"[MqttClientService] 使用 game_params.json 中的 mqttClientIdOverride = \"{overrideId}\"，跳过自动 fallback", wmj.Log.Tag.Network);
            return;
        }

        // 0.5) 比赛模式被动观察：优先尝试“观察者 clientId”，减少与主客户端同 clientId 抢占会话
        bool passiveObserver = GameParamsConfig.Get.isCompetitionMode
            && GameParamsConfig.Get.competitionPassiveObserverMode;
        if (passiveObserver)
        {
            string baseId = primary;
            if (baseId.Length > 19) baseId = baseId.Substring(0, 19);
            string observerId = baseId + "_obs";
            if (!clientIdCandidates.Contains(observerId)) clientIdCandidates.Add(observerId);

            string randomObserverId = "obs_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!clientIdCandidates.Contains(randomObserverId)) clientIdCandidates.Add(randomObserverId);

            wmj.Log.I($"[MqttClientService] 被动观察模式: 优先尝试观察者 clientId 候选 [{observerId}, {randomObserverId}]", wmj.Log.Tag.Network);
        }

        clientIdCandidates.Add(primary);

        // 形如 "0x0165" → 派生多种表示
        if ((primary.StartsWith("0x") || primary.StartsWith("0X")) && primary.Length > 2)
        {
            string hex = primary.Substring(2);
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture, out int val))
            {
                // ★ 第二优先级：对应的裁判系统"机器人 ID"（官方 2026 比赛服务器确认使用此格式）
                //   蓝方 0x0165（英雄） → 101；红方 0x0101 → 1
                int robotId = val > 0x0164 ? (val - 0x0164 + 100) : (val - 0x0100);
                if (robotId > 0 && robotId < 200)
                {
                    string rid = robotId.ToString();                                                    // "101"
                    if (!clientIdCandidates.Contains(rid)) clientIdCandidates.Add(rid);
                }
                if (!clientIdCandidates.Contains(hex)) clientIdCandidates.Add(hex);                    // "0165"
                string hexNoLead = val.ToString("X");                                                   // "165"
                if (!clientIdCandidates.Contains(hexNoLead)) clientIdCandidates.Add(hexNoLead);
                string dec = val.ToString();                                                            // "357"
                if (!clientIdCandidates.Contains(dec)) clientIdCandidates.Add(dec);
                string upper = "0X" + hex;                                                              // "0X0165"
                if (!clientIdCandidates.Contains(upper)) clientIdCandidates.Add(upper);
                string lower = primary.ToLowerInvariant();                                              // "0x0165"
                if (!clientIdCandidates.Contains(lower)) clientIdCandidates.Add(lower);
            }
        }
    }

    /// <summary>
    /// CONNACK 被拒时推进 fallback 组合：先遍历 clientId，全失败后换协议版本再重来。
    /// 返回 false 表示所有组合都已尝试完毕，将继续在最后一个组合上循环重试。
    /// </summary>
    private bool AdvanceCandidate()
    {
        if (clientIdIdx + 1 < clientIdCandidates.Count)
        {
            clientIdIdx++;
            clientId = clientIdCandidates[clientIdIdx];
            wmj.Log.W($"[MqttClientService] 切换 clientId 候选 → {clientId} (候选 {clientIdIdx + 1}/{clientIdCandidates.Count}, 协议={protocolCandidates[protocolIdx]})", wmj.Log.Tag.Network);
            return true;
        }
        if (protocolIdx + 1 < protocolCandidates.Length)
        {
            protocolIdx++;
            clientIdIdx = 0;
            clientId = clientIdCandidates[0];
            wmj.Log.W($"[MqttClientService] 所有 clientId 候选均被拒，切换协议版本 → {protocolCandidates[protocolIdx]}，重新从 clientId={clientId} 开始", wmj.Log.Tag.Network);
            return true;
        }
        wmj.Log.E($"[MqttClientService] 所有 clientId × 协议版本组合均被服务器拒绝，请检查裁判系统 clientId 格式配置", wmj.Log.Tag.Network);
        return false;
    }

    public void Reconnect(string newClientId)
    {
        wmj.Log.I($"[MqttClientService] 使用新 clientId 重连: {newClientId}", wmj.Log.Tag.Network);
        this.clientId = newClientId;
        SafeDisconnectCurrent("Reconnect-switch-clientId");
        needsReconnect = true;
        EnsureRoutineStarted();
    }

    public void Disconnect()
    {
        SafeDisconnectCurrent("External-Disconnect");
        wmj.Log.I("[MqttClientService] 断开连接", wmj.Log.Tag.Network);
    }

    public void Shutdown()
    {
        isShuttingDown = true;
        SafeDisconnectCurrent("Shutdown");
        wmj.Log.I("[MqttClientService] 服务已关闭", wmj.Log.Tag.Network);
    }

    public void Subscribe(string topic, byte qos = 1)
    {
        subscriptions[topic] = qos;
        var c = client;
        if (c != null && c.IsConnected)
        {
            wmj.Log.I($"[MqttClientService] 订阅主题: {topic}, QoS={qos}", wmj.Log.Tag.Network);
            try { c.Subscribe(new string[] { topic }, new byte[] { qos }); }
            catch (Exception ex) { wmj.Log.W($"[MqttClientService] 订阅异常: {topic}, {ex.Message}", wmj.Log.Tag.Network); }
        }
    }

    public void Unsubscribe(string topic)
    {
        if (subscriptions.ContainsKey(topic)) subscriptions.Remove(topic);
        var c = client;
        if (c != null && c.IsConnected)
        {
            wmj.Log.I($"[MqttClientService] 退订主题: {topic}", wmj.Log.Tag.Network);
            try { c.Unsubscribe(new string[] { topic }); } catch { /* ignore */ }
        }
    }

    public void Publish(string topic, byte[] payload)
    {
        lock (sendQueue) { sendQueue.Enqueue(new MqttSendItem { Topic = topic, Payload = payload }); }
        if (!isSending)
        {
            isSending = true;
            NetworkManager.Instance.StartCoroutine(SendLoop());
        }
    }

    // ══════════════════════════════════════════════════════
    //  内部实现
    // ══════════════════════════════════════════════════════

    private void EnsureRoutineStarted()
    {
        if (routineRunning) return;
        routineRunning = true;
        NetworkManager.Instance.StartCoroutine(ReconnectRoutine());
    }

    /// <summary>
    /// 事件驱动重连协程：仅在 needsReconnect=true 时发起连接，
    /// 并通过首包看门狗主动发现"静默连接"。
    /// </summary>
    private System.Collections.IEnumerator ReconnectRoutine()
    {
        while (!isShuttingDown)
        {
            float now = Time.realtimeSinceStartup;

            // ── 首包看门狗：连接已建立但长时间未收到消息 → 判定静默连接 ──
            var c = client;
            if (c != null && c.IsConnected && !needsReconnect && !isConnecting)
            {
                float sinceConnect = now - lastConnectedTime;
                float sinceMessage = now - lastMessageTime;
                if (sinceConnect >= FIRST_MESSAGE_TIMEOUT && sinceMessage >= FIRST_MESSAGE_TIMEOUT)
                {
                    wmj.Log.W($"[MqttClientService] 首包看门狗触发：连接已 {sinceConnect:F1}s 但 {sinceMessage:F1}s 未收到任何消息，判定为静默连接，强制重连", wmj.Log.Tag.Network);
                    SafeDisconnectCurrent("FirstMessageWatchdog");
                    needsReconnect = true;
                }
            }

            // ── 发起重连 ──
            if (needsReconnect && !isConnecting && !isShuttingDown)
            {
                isConnecting = true;
                needsReconnect = false;
                int attempt = ++connectAttempt;
                wmj.Log.I($"[MqttClientService] 发起连接 #{attempt} → {brokerIp}:{brokerPort} (clientId={clientId})", wmj.Log.Tag.Network);

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (!isShuttingDown) TryConnect(attempt);
                    }
                    catch (Exception ex)
                    {
                        wmj.Log.F($"[MqttClientService] 后台连接异常 #{attempt}: {ex.Message}", wmj.Log.Tag.Network);
                        needsReconnect = true;
                    }
                    finally
                    {
                        isConnecting = false;
                    }
                });
            }

            yield return new WaitForSeconds(reconnectInterval);
        }
        routineRunning = false;
        wmj.Log.I("[MqttClientService] 重连协程已按 shutdown 标志退出", wmj.Log.Tag.Network);
    }

    /// <summary>实际建立 MQTT 连接（仅在 ThreadPool 线程调用）</summary>
    private void TryConnect(int attempt)
    {
        MqttClient newClient = null;
        var protocolVersion = protocolCandidates[protocolIdx];
        var currentId = clientId;
        try
        {
            newClient = new MqttClient(brokerIp, brokerPort, false, null, null, MqttSslProtocols.None);
            // 显式指定 MQTT 协议版本；服务器若只支持 3.1，默认 3.1.1 会被拒
            newClient.ProtocolVersion = protocolVersion;

            newClient.MqttMsgPublishReceived += (sender, e) =>
            {
                lastMessageTime = Time.realtimeSinceStartup;
                wmj.Log.I($"[MqttClientService] 收到消息: Topic={e.Topic}, Length={e.Message.Length}", wmj.Log.Tag.Network);
                try { OnMessageReceived?.Invoke(e.Topic, e.Message); }
                catch (Exception exInvoke) { wmj.Log.F($"[MqttClientService] 消息处理回调异常: {exInvoke.Message}", wmj.Log.Tag.Network); }
            };

            newClient.ConnectionClosed += (sender, e) =>
            {
                wmj.Log.W($"[MqttClientService] ConnectionClosed 事件触发 (attempt #{attempt})", wmj.Log.Tag.Network);
                if (client == newClient) client = null;
                needsReconnect = true;
                try { OnDisconnected?.Invoke(); } catch { /* ignore */ }
            };

            wmj.Log.I($"[MqttClientService] 发送 CONNECT: clientId={currentId}, 协议={protocolVersion}, keepAlive={KEEP_ALIVE_PERIOD}s, cleanSession=true", wmj.Log.Tag.Network);
            // 读取 game_params 中可选的鉴权信息（官方服务器若要求时使用）
            string user = GameParamsConfig.Get?.mqttUsername;
            string pwd  = GameParamsConfig.Get?.mqttPassword;
            if (string.IsNullOrEmpty(user)) user = null;
            if (string.IsNullOrEmpty(pwd))  pwd  = null;
            if (user != null)
                wmj.Log.I($"[MqttClientService] 使用 MQTT 鉴权 | username={user}", wmj.Log.Tag.Network);
            // Connect 重载：(clientId, username, password, cleanSession, keepAlivePeriod)
            byte connAck = newClient.Connect(currentId, user, pwd, true, KEEP_ALIVE_PERIOD);

            if (connAck != MqttMsgConnack.CONN_ACCEPTED)
            {
                string reason = connAck switch
                {
                    0x01 => "Unacceptable protocol version",
                    0x02 => "Identifier rejected",
                    0x03 => "Server unavailable",
                    0x04 => "Bad user name or password",
                    0x05 => "Not authorized",
                    _    => "Unknown"
                };
                wmj.Log.F($"[MqttClientService] CONNACK 被拒绝: code=0x{connAck:X2} ({reason}) clientId={currentId} 协议={protocolVersion} (attempt #{attempt})", wmj.Log.Tag.Network);
                try { newClient.Disconnect(); } catch { }
                // 0x01/0x02/0x04/0x05 属于参数/身份问题，推进 fallback 组合；0x03 属服务端问题，原地重试
                if (connAck != 0x03) AdvanceCandidate();
                needsReconnect = true;
                return;
            }

            client = newClient;
            lastConnectedTime = Time.realtimeSinceStartup;
            lastMessageTime = lastConnectedTime; // 重置看门狗基准
            wmj.Log.I($"[MqttClientService] ✅ MQTT 连接成功 #{attempt} | clientId={currentId} 协议={protocolVersion} keep-alive={KEEP_ALIVE_PERIOD}s", wmj.Log.Tag.Network);
            try { OnConnected?.Invoke(); } catch { /* ignore */ }

            foreach (var kvp in subscriptions)
            {
                try
                {
                    wmj.Log.I($"[MqttClientService] 重连/连接后自动订阅: {kvp.Key}, QoS={kvp.Value}", wmj.Log.Tag.Network);
                    client.Subscribe(new string[] { kvp.Key }, new byte[] { kvp.Value });
                }
                catch (Exception subEx)
                {
                    wmj.Log.W($"[MqttClientService] 自动订阅失败: {kvp.Key}, {subEx.Message}", wmj.Log.Tag.Network);
                }
            }
        }
        catch (Exception ex)
        {
            wmj.Log.F($"[MqttClientService] 连接失败 #{attempt}: {ex.GetType().Name}: {ex.Message}", wmj.Log.Tag.Network);
            if (newClient != null) { try { newClient.Disconnect(); } catch { } }
            needsReconnect = true;
        }
    }

    private void SafeDisconnectCurrent(string reason)
    {
        var c = client;
        client = null;
        if (c == null) return;
        try
        {
            if (c.IsConnected) c.Disconnect();
            wmj.Log.I($"[MqttClientService] 已断开旧连接 (reason={reason})", wmj.Log.Tag.Network);
        }
        catch (Exception ex)
        {
            wmj.Log.W($"[MqttClientService] 断开旧连接异常 (reason={reason}): {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    private System.Collections.IEnumerator SendLoop()
    {
        while (true)
        {
            MqttSendItem? item = null;
            lock (sendQueue)
            {
                if (sendQueue.Count > 0) item = sendQueue.Dequeue();
            }
            if (item.HasValue)
            {
                var topic = item.Value.Topic;
                var payload = item.Value.Payload;
                var c = client;
                if (c != null && c.IsConnected)
                {
                    try
                    {
                        wmj.Log.I($"[MqttClientService] 发布消息: Topic={topic}, Length={payload?.Length}", wmj.Log.Tag.Network);
                        c.Publish(topic, payload);
                    }
                    catch (Exception ex)
                    {
                        wmj.Log.W($"[MqttClientService] 发布异常: {topic}, {ex.Message}", wmj.Log.Tag.Network);
                        needsReconnect = true;
                    }
                }
                else
                {
                    // 发布丢弃告警节流：同类型每 2 秒最多一条，统计总数
                    dropWarnCount++;
                    float nowSec = Time.realtimeSinceStartup;
                    if (nowSec - lastDropWarnTime >= 2f)
                    {
                        wmj.Log.W($"[MqttClientService] 发布时未连接，已丢弃 {dropWarnCount} 条 (最近: {topic})", wmj.Log.Tag.Network);
                        lastDropWarnTime = nowSec;
                        dropWarnCount = 0;
                    }
                }
                yield return null;
            }
            else
            {
                isSending = false;
                yield break;
            }
        }
    }
}

