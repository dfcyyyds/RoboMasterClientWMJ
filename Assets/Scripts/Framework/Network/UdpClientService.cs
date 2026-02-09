using System;
using System.Net.Sockets;
using System.Net;
using System.Collections.Concurrent;
using System.Threading;
using System.Buffers;

/// <summary>
/// UDP图传接收
/// </summary>
public class UdpClientService
{
    private struct PooledPacket
    {
        public string Remote;
        public byte[] Buffer;
        public int Length;
    }
    // 创建UDP客户端
    private UdpClient udpClient;
    private const int MaxUdpPacketSize = 65535;
    private bool isReceiving = false;
    private readonly ConcurrentQueue<PooledPacket> recvQueue = new ConcurrentQueue<PooledPacket>();
    // 首次启动后是否已开启队列耗尽协程
    private bool drainCoroutineStarted = false;
    // 阻塞接收线程（双保险）
    private Thread recvThread;
    // 消息接收事件（零拷贝优先，调用方不得持有 segment 生命周期）
    public event Action<string, ArraySegment<byte>> OnMessageReceivedSegment;
    // 兼容旧逻辑：仍支持 byte[] 事件（会复制一份）
    public event Action<string, byte[]> OnMessageReceived;
    // 启动和停止事件
    public event Action OnStarted;
    public event Action OnStopped;

    // 诊断计数器（仅编辑器或定义 DIAGNOSE_UDP 时启用）
#if UNITY_EDITOR || DIAGNOSE_UDP
    private uint diagFramesReceived = 0;
    private uint diagPacketsReceived = 0;
    private uint diagBytesReceived = 0;
    private float diagLastReport = 0;
    private string diagLastRemote = "";
    private bool diagFirstPacketAnnounced = false;
#endif

    public void StartReceive(string ip, int port)
    {
        if (udpClient != null)
            StopReceive();
#if UNITY_EDITOR
            wmj.DebugTools.Info($"[UdpClientService] 开始监听UDP: {ip}:{port}");
            wmj.DebugTools.WriteDebugLog("[UdpClientService] 开始监听UDP: "+ ip + ":" + port,"INFO");
#endif
        wmj.DebugTools.WriteRunLog("[UdpClientService] 开始监听UDP: " + ip + ":" + port, "INFO");
        // 为提高兼容性，统一绑定到 Any 地址，避免容器/命名空间导致的 127.0.0.1 隔离问题
        // 仍保留日志中打印的目标IP，方便排查
        try
        {
            // 优先尝试 IPv6 双栈绑定，兼容 IPv4/IPv6 流量（含 ::1）
            bool boundWithIPv6Dual = false;
            try
            {
                udpClient = new UdpClient(AddressFamily.InterNetworkV6);
                try { udpClient.Client.DualMode = true; } catch { try { udpClient.Client.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27 /*DualMode*/, true); } catch { } }
                udpClient.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
#if UNITY_EDITOR
                wmj.DebugTools.Info($"[UdpClientService] 采用 IPv6 双栈绑定: [::]:{port}");
                wmj.DebugTools.WriteDebugLog("[UdpClientService] 采用 IPv6 双栈绑定: [::]:" + port, "INFO");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] 采用 IPv6 双栈绑定: [::]:" + port, "INFO");
                boundWithIPv6Dual = true;
            }
            catch (System.Exception ipv6Ex)
            {
#if UNITY_EDITOR
                wmj.DebugTools.Warn($"[UdpClientService] IPv6 双栈绑定失败，将回退到 IPv4: {ipv6Ex.Message}");
                wmj.DebugTools.WriteDebugLog("[UdpClientService] IPv6 双栈绑定失败，将回退到 IPv4: " + ipv6Ex.Message, "WARN");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] IPv6 双栈绑定失败，将回退到 IPv4: " + ipv6Ex.Message, "WARN");
            }
            if (!boundWithIPv6Dual)
            {
                // 绑定到 Any 地址（IPv4），接收所有源的 UDP 包
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            }
            // 扩大接收缓冲区，避免高码率下丢包
            try { udpClient.Client.ReceiveBufferSize = 1 << 20; } catch { }
            try { udpClient.Client.SendBufferSize = 1 << 18; } catch { }
#if UNITY_EDITOR || DIAGNOSE_UDP
            wmj.DebugTools.Info($"[UdpClientService] 🔗 已绑定 0.0.0.0:{port}，监听来自任意源的 UDP 包");
            diagLastReport = UnityEngine.Time.realtimeSinceStartup;
#endif
#if UNITY_EDITOR
            wmj.DebugTools.Info($"[UdpClientService] 已绑定 0.0.0.0:{port}，开始接收UDP");
            wmj.DebugTools.WriteDebugLog("[UdpClientService] 已绑定 0.0.0.0:" + port + "，开始接收UDP","INFO");
#endif
            wmj.DebugTools.WriteRunLog("[UdpClientService] 已绑定 0.0.0.0:" + port + "，开始接收UDP", "INFO");
            isReceiving = true;
            OnStarted?.Invoke();

            // 启动阻塞接收线程（主路径，确保Linux下稳定抓包）
            try
            {
                recvThread = new Thread(BlockingReceiveLoop) { IsBackground = true, Name = "UdpBlockingReceive" };
                recvThread.Start();
#if UNITY_EDITOR
                wmj.DebugTools.Info("[UdpClientService] 已启动阻塞接收线程");
                wmj.DebugTools.WriteDebugLog("[UdpClientService] 已启动阻塞接收线程", "INFO");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] 已启动阻塞接收线程", "INFO");
            }
            catch (System.Exception ex)
            {
#if UNITY_EDITOR
                wmj.DebugTools.Error($"[UdpClientService] 启动阻塞接收线程失败: {ex.Message}");
                wmj.DebugTools.WriteDebugLog("[UdpClientService] 启动阻塞接收线程失败: " + ex.Message, "ERROR");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] 启动阻塞接收线程失败: " + ex.Message, "ERROR");
            }

            // 在主线程开启队列耗尽协程（仅启动一次）
            if (!drainCoroutineStarted)
            {
                NetworkManager.Instance.StartCoroutine(DrainQueueLoop());
                drainCoroutineStarted = true;
            }
#if DIAGNOSE_UDP_SELFTEST
            // 可选：自测向本地端口发送数个测试包，验证接收链路
            NetworkManager.Instance.StartCoroutine(SelfTestSend(ip, port));
#endif
        }
        catch (System.Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Error($"[UdpClientService] 绑定端口失败 {port}: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[UdpClientService] 绑定端口失败 " + port + ": " + ex.Message, "ERROR");
#endif
            wmj.DebugTools.WriteRunLog("[UdpClientService] 绑定端口失败 " + port + ": " + ex.Message, "ERROR");
        }
    }
    // 阻塞接收循环（运行于后台线程）
    private void BlockingReceiveLoop()
    {
        var socket = udpClient?.Client;
        if (socket == null) return;

        while (isReceiving && udpClient != null)
        {
            byte[] buffer = null;
            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(MaxUdpPacketSize);
                // 根据Socket的AddressFamily选择正确的EndPoint类型，避免IPv6/IPv4不匹配
                EndPoint remote = socket.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0)
                    : new IPEndPoint(IPAddress.Any, 0);
                int received = socket.ReceiveFrom(buffer, 0, MaxUdpPacketSize, SocketFlags.None, ref remote);
                if (received > 0)
                {
                    var pkt = new PooledPacket { Remote = remote.ToString(), Buffer = buffer, Length = received };
#if UNITY_EDITOR || DIAGNOSE_UDP
                    diagPacketsReceived++;
                    diagBytesReceived += (uint)received;
                    diagLastRemote = pkt.Remote;
                    if (!diagFirstPacketAnnounced)
                    {
                        wmj.DebugTools.Info($"[UdpClientService] ✅ 首个UDP包已到达: 来自 {diagLastRemote}, 长度={received}");
                        diagFirstPacketAnnounced = true;
                    }
#endif
                    recvQueue.Enqueue(pkt);
                    buffer = null; // 交由队列消费归还
                }
            }
            catch (ObjectDisposedException)
            {
                buffer = null;
                break;
            }
            catch (SocketException sex)
            {
                wmj.DebugTools.Warn($"[UdpClientService] Receive异常: {sex.Message} (code={sex.ErrorCode})");
                Thread.Sleep(1);
            }
            catch (System.Exception ex)
            {
                wmj.DebugTools.Warn($"[UdpClientService] Receive异常: {ex.Message}");
                Thread.Sleep(1);
            }
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private System.Collections.IEnumerator DrainQueueLoop()
    {
        while (true)
        {
            // 当服务停止后，仍允许把队列剩余消息耗尽再退出
            while (recvQueue.TryDequeue(out var item))
            {
                try
                {
                    var seg = new ArraySegment<byte>(item.Buffer, 0, item.Length);
                    // 高频日志仅在编辑器调试时输出，避免IO阻塞
#if UNITY_EDITOR && UDP_VERBOSE_LOG
                    wmj.DebugTools.Info($"[UdpClientService] 收到UDP包: 来自 {item.Remote}, 长度={item.Length}", wmj.DebugTools.LogCategory.Network);
                    wmj.DebugTools.WriteDebugLog("[UdpClientService] 收到UDP包: 来自 " + item.Remote + ", 长度=" + item.Length, "INFO");
#endif
                    // 移除高频WriteRunLog调用，避免每秒30000+次IO导致卡死

                    // 优先使用零拷贝的Segment版本，避免重复分发
                    if (OnMessageReceivedSegment != null)
                    {
                        OnMessageReceivedSegment.Invoke(item.Remote, seg);
                    }
                    else if (OnMessageReceived != null)
                    {
                        var copy = new byte[item.Length];
                        Buffer.BlockCopy(item.Buffer, 0, copy, 0, item.Length);
                        OnMessageReceived.Invoke(item.Remote, copy);
                    }
                }
                catch (System.Exception ex)
                {
#if UNITY_EDITOR
                    wmj.DebugTools.Error($"[UdpClientService] 分发UDP消息异常: {ex.Message}");
                    wmj.DebugTools.WriteDebugLog("[UdpClientService] 分发UDP消息异常: " + ex.Message, "ERROR");
#endif
                    wmj.DebugTools.WriteRunLog("[UdpClientService] 分发UDP消息异常: " + ex.Message, "ERROR");
                }
                finally
                {
                    if (item.Buffer != null)
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                }
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
#if UNITY_EDITOR || DIAGNOSE_UDP
            if (now - diagLastReport >= 1.0f)
            {
                wmj.DebugTools.Info($"[UdpClientService] 📊 诊断: 包 {diagPacketsReceived}, 字节 {diagBytesReceived}, 来自 {diagLastRemote}");
#if UNITY_EDITOR
                wmj.DebugTools.Info($"[UdpClientService] 诊断: 每秒到达 包={diagPacketsReceived}, 字节={diagBytesReceived}, 最后来源={diagLastRemote}", wmj.DebugTools.LogCategory.Network);
                wmj.DebugTools.WriteDebugLog("[UdpClientService] 诊断: 每秒到达 包=" + diagPacketsReceived + ", 字节=" + diagBytesReceived + ", 最后来源=" + diagLastRemote, "INFO");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] 诊断: 每秒到达 包=" + diagPacketsReceived + ", 字节=" + diagBytesReceived + ", 最后来源=" + diagLastRemote, "INFO");
                diagPacketsReceived = 0;
                diagBytesReceived = 0;
                diagLastReport = now;
            }
#endif

            if (!isReceiving && recvQueue.IsEmpty)
                break;
            yield return null;
        }
        drainCoroutineStarted = false;
    }

#if DIAGNOSE_UDP_SELFTEST
    // 本地自测：向自己绑定的端口发 UDP 包，以验证接收链路
    private System.Collections.IEnumerator SelfTestSend(string ip, int port)
    {
        yield return null; // 等一帧，确保绑定完成
        try
        {
            using (var sender = new UdpClient())
            {
                var target = new IPEndPoint(IPAddress.Loopback, port);
                // 构造一个合法的 8 字节小端头 + 4 字节伪 NALU
                byte[] pkt = new byte[12];
                // frameId=1
                pkt[0] = 0x01; pkt[1] = 0x00;
                // sliceId=0
                pkt[2] = 0x00; pkt[3] = 0x00;
                // frameLen=4
                pkt[4] = 0x04; pkt[5] = 0x00; pkt[6] = 0x00; pkt[7] = 0x00;
                // 假装 AnnexB: 00 00 00 01（起始码），足以走通解析与日志
                pkt[8] = 0x00; pkt[9] = 0x00; pkt[10] = 0x00; pkt[11] = 0x01;
                sender.Send(pkt, pkt.Length, target);
#if UNITY_EDITOR
                wmj.DebugTools.Info($"[UdpClientService] 自测包已发送到 {target}");
                wmj.DebugTools.WriteDebugLog("[UdpClientService] 自测包已发送到 " + target, "INFO");
#endif
                wmj.DebugTools.WriteRunLog("[UdpClientService] 自测包已发送到 " + target, "INFO");
            }
        }
        catch (System.Exception ex)
        {
#if UNITY_EDITOR
            wmj.DebugTools.Warn($"[UdpClientService] 自测发送失败: {ex.Message}");
            wmj.DebugTools.WriteDebugLog("[UdpClientService] 自测发送失败: " + ex.Message, "WARN");
#endif
            wmj.DebugTools.WriteRunLog("[UdpClientService] 自测发送失败: " + ex.Message, "WARN");
        }
    }
#endif

    public void StopReceive()
    {
        isReceiving = false;
        if (udpClient != null)
        {
            wmj.DebugTools.Info("[UdpClientService] 停止UDP监听");
            wmj.DebugTools.WriteDebugLog("[UdpClientService] 停止UDP监听", "INFO");
            wmj.DebugTools.WriteRunLog("[UdpClientService] 停止UDP监听", "INFO");
#if UNITY_EDITOR || DIAGNOSE_UDP
            wmj.DebugTools.Info($"[UdpClientService] ⏹️ UDP 接收已停止 (总计接收: {diagFramesReceived} 帧)");
#endif
            udpClient.Close();
            udpClient = null;
        }
        // 等待接收线程退出
        if (recvThread != null)
        {
            try { recvThread.Join(100); } catch { }
            recvThread = null;
        }
        OnStopped?.Invoke();
    }
}
