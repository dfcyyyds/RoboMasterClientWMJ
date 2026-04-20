using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Google.Protobuf;
using Framework.Network;

/// <summary>
/// 吊射模式独立 UDP 接收器 — 仅在仿真模式（isCompetitionMode=false）下启用
/// 监听指定端口接收二值化视觉数据，可配置源 IP 过滤
/// 后台线程收包 → ConcurrentQueue → 主线程 DrainQueue 分发到 ProtobufManager
/// 独立于主图传 UdpClientService, 不经过 MQTT
/// 
/// 配置项（game_params.json）：
///   lobShotUdpIp   — 期望的数据源 IP（"0.0.0.0" 或空字符串表示不过滤）
///   lobShotUdpPort — 监听端口（默认 8888）
/// </summary>
public class LobShotUdpReceiver : MonoBehaviour
{
    public static LobShotUdpReceiver Instance { get; private set; }

    /// <summary>
    /// 当前活跃的 H.264 传输层（吊射模式进入时由 LobShotHUD 设置，退出时置 null）
    /// 不为 null 时，仿真 UDP 数据直接走 H.264 管线而非 CustomByteBlock
    /// </summary>
    public static LobShotH264Transport ActiveH264Transport { get; set; }

    [Tooltip("每帧最多处理的包数(防止主线程卡顿)")]
    [SerializeField] private int maxDrainPerFrame = 32;

    [Tooltip("接收队列最大包数(超限丢最旧，防止内存暴涨)")]
    [SerializeField] private int maxRecvQueuePackets = 1024;

    // 从配置读取
    private int serverPort;
    private string filterIpStr; // null/空 表示不过滤（使用字符串比较，避免IPv4/v6 Equals陷阱）

    // 诊断
    private int diagFilterDropped;

    // 网络
    private UdpClient udpClient;
    private Thread recvThread;
    private volatile bool isReceiving;

    // 线程安全队列
    private readonly ConcurrentQueue<byte[]> recvQueue = new ConcurrentQueue<byte[]>();
    private int recvQueueCount;

    // 诊断
    private uint diagPackets;
    private uint diagBytes;
    private float diagLastReport;
    private bool diagFirstAnnounced;
    private int diagQueueDropped;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 从 GameParamsConfig 读取配置
        var gp = GameParamsConfig.Get;
        serverPort = gp.lobShotUdpPort > 0 ? gp.lobShotUdpPort : 8888;

        // 源 IP 过滤（空/"0.0.0.0" 表示不过滤，使用字符串末尾比较避免 IPv4/IPv6 mapped 问题）
        filterIpStr = null;
        if (!string.IsNullOrEmpty(gp.lobShotUdpIp) && gp.lobShotUdpIp != "0.0.0.0")
        {
            filterIpStr = gp.lobShotUdpIp;
        }
    }

    void Start()
    {
        StartReceive();
    }

    void OnDestroy()
    {
        StopReceive();
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit()
    {
        StopReceive();
    }

    // ═══════════════════════════════ 收发控制 ═══════════════════════════════

    private void StartReceive()
    {
        if (isReceiving) return;

        try
        {
            udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, serverPort));
            try { udpClient.Client.ReceiveBufferSize = 1 << 19; } catch { } // 512KB
            isReceiving = true;

            recvThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "LobShotUdpRecv"
            };
            recvThread.Start();

            wmj.Log.I($"[LobShotUdpReceiver] 已绑定 0.0.0.0:{serverPort}，源IP过滤={filterIpStr ?? "无"}，开始接收吊射图传", wmj.Log.Tag.Network);
            diagLastReport = Time.realtimeSinceStartup;
        }
        catch (Exception ex)
        {
            wmj.Log.E($"[LobShotUdpReceiver] 启动失败: {ex.Message}", wmj.Log.Tag.Network);
        }
    }

    private void StopReceive()
    {
        isReceiving = false;
        try { udpClient?.Close(); } catch { }
        udpClient = null;
        try { recvThread?.Join(500); } catch { }
        recvThread = null;
        wmj.Log.I("[LobShotUdpReceiver] 已停止接收", wmj.Log.Tag.Network);
    }

    // ═══════════════════════════════ 后台接收线程 ═══════════════════════════════

    private void ReceiveLoop()
    {
        while (isReceiving)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] data = udpClient.Receive(ref remote);
                if (data != null && data.Length > 0)
                {
                    // 源 IP 过滤：使用字符串 EndsWith 比较（兼容 IPv6-mapped 如 "::ffff:192.168.50.22"）
                    if (filterIpStr != null && remote != null)
                    {
                        string remoteStr = remote.Address.ToString();
                        if (!remoteStr.EndsWith(filterIpStr))
                        {
                            if (diagFilterDropped++ < 5)
                                wmj.Log.W($"[LobShotUdpReceiver] IP过滤丢弃: 来源={remoteStr}, 期望含={filterIpStr}", wmj.Log.Tag.Network);
                            continue;
                        }
                    }

                    recvQueue.Enqueue(data);
                    Interlocked.Increment(ref recvQueueCount);

                    while (Volatile.Read(ref recvQueueCount) > maxRecvQueuePackets && recvQueue.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref recvQueueCount);
                        diagQueueDropped++;
                    }
                    diagPackets++;
                    diagBytes += (uint)data.Length;

                    if (!diagFirstAnnounced)
                    {
                        diagFirstAnnounced = true;
                        wmj.Log.I($"[LobShotUdpReceiver] 首包到达: {remote}, 长度={data.Length}", wmj.Log.Tag.Network);
                    }
                }
            }
            catch (SocketException) when (!isReceiving) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (isReceiving)
                    wmj.Log.W($"[LobShotUdpReceiver] 接收异常: {ex.Message}", wmj.Log.Tag.Network);
                Thread.Sleep(10);
            }
        }
    }

    // ═══════════════════════════════ 主线程分发 ═══════════════════════════════

    void Update()
    {
        DrainQueue();
        DiagnosticLog();
    }

    /// <summary>从队列取出数据包，v2走H264Transport，v1走CustomByteBlock</summary>
    private void DrainQueue()
    {
        int count = 0;
        var transport = ActiveH264Transport;

        // 非吊射模式（transport=null）：仅保留最近 ≤2 个包，避免堆积
        if (transport == null)
        {
            while (Volatile.Read(ref recvQueueCount) > 2 && recvQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref recvQueueCount);
                diagQueueDropped++;
            }
            return;
        }

        int drainBudget = maxDrainPerFrame;
        int backlog = Volatile.Read(ref recvQueueCount);
        if (backlog > 768) drainBudget = Math.Max(drainBudget, 256);
        else if (backlog > 384) drainBudget = Math.Max(drainBudget, 128);

        while (count < drainBudget && recvQueue.TryDequeue(out byte[] data))
        {
            count++;
            Interlocked.Decrement(ref recvQueueCount);
            try
            {
                if (transport != null)
                {
                    // v2 H.264 管线：直接传给 H264Transport 处理仿真 UDP 包
                    transport.ProcessSimUdpPacket(data);
                }
                else
                {
                    // v1 兼容路径：包装为 CustomByteBlock → ProtobufManager
                    var block = new CustomByteBlock { Data = ByteString.CopyFrom(data) };
                    Framework.Network.ProtobufManager.Instance.UpdateData(block);
                }
            }
            catch (Exception ex)
            {
                wmj.Log.W($"[LobShotUdpReceiver] 分发异常: {ex.Message}", wmj.Log.Tag.Network);
            }
        }
    }

    /// <summary>定期输出诊断统计</summary>
    private void DiagnosticLog()
    {
        float now = Time.realtimeSinceStartup;
        if (now - diagLastReport < 5f) return;
        diagLastReport = now;

        if (diagPackets > 0)
        {
            string mode = ActiveH264Transport != null ? "v2_H264" : "v1_CustomByteBlock";
            int q = Volatile.Read(ref recvQueueCount);
            wmj.Log.I($"[LobShotUdpReceiver] 统计: packets={diagPackets} bytes={diagBytes} queue={q} dropped={diagQueueDropped} mode={mode}",
                wmj.Log.Tag.Network);
        }
    }
}
