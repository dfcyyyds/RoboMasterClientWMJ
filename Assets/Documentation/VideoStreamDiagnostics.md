# 图传诊断与日志系统

## 概述

本系统为 RoboMaster 自定义客户端的 UDP 视频流传输提供完整的诊断日志链路，帮助快速定位视频不显示、卡顿、数据丢失等问题。

## 架构流程

```
MockServer
  ↓ FFmpeg 编码
  ├─ [VideoSender] 📡 开始推送 HEVC AnnexB
  ├─ [VideoSender] ✅ FFmpeg 进程就绪
  ├─ [VideoSender] 🔍 首次读取: xxx 字节, 头4字节: 00 00 00 01
  └─ [VideoSender] 📊 帧 #N 已发送: M 分片, 总 K 字节
       ↓ UDP 127.0.0.1:3334
    Unity 客户端
      ↓ UdpClientService (监听 0.0.0.0:3334)
      ├─ [UdpClientService] 🔗 已绑定 0.0.0.0:3334
      └─ [UdpClientService] 📊 诊断: 包 X, 字节 Y, 来自 127.0.0.1
           ↓ UdpVideoHandler
           ├─ [UdpVideoHandler] 📈 诊断: N 帧, M 切片抵达
           └─ 上抛 UdpVideoFrame
                ↓ AnnexBFrameAssembler
                └─ [组帧完成并上抛]
                     ↓ VideoStreamService
                     ├─ [推送至解码]
                     └─ FfmpegPipeDecoder
                        ├─ [解码帧: WxH, size=Z]
                        └─ Texture2D.Apply()
                             ↓
                          RawImage 显示
```

## 日志节点与排查策略

### 1. MockServer 端

**启动日志** (`Assets/MockServerCpp/run/mock_server.log`)

```
[VideoSender] 📡 开始推送 HEVC AnnexB 到 127.0.0.1:3334
[VideoSender] ✅ FFmpeg 进程就绪
[VideoSender] 🔍 首次读取: 1048576 字节, 头4字节: 00 00 00 01
[VideoSender] 📊 帧 #10 已发送: 2 分片, 总 2600 字节
...
[VideoSender] ✔️  线程退出. 总计发送: 100 帧, 280 分片, 2500000 字节
```

**诊断要点**
- "首次读取" 是否出现？若无，FFmpeg 未启动或设备不可用。
- "已发送" 日志是否持续增长？若停止，编码或 UDP 发送出错。
- 最终统计是否有 >0 的帧、分片、字节？
  - **否** → 发送方完全无数据，检查 `/dev/video0` 或 FFmpeg 命令。
  - **是** → 发送端工作正常，排查客户端接收。

### 2. 客户端 UDP 接收层

**启动日志** (Unity Console / `Log/RunLog.txt`)

```
[UdpClientService] 开始监听UDP: 127.0.0.1:3334
[UdpClientService] 🔗 已绑定 0.0.0.0:3334，监听来自任意源的 UDP 包
```

**条件编译诊断** (仅在 `UNITY_EDITOR` 或定义 `DIAGNOSE_UDP` 时打印到 Console)

```
[UdpClientService] 📊 诊断: 包 50, 字节 65000, 来自 127.0.0.1:12345
[UdpVideoHandler] 📈 诊断: 3 帧, 15 切片抵达
```

**诊断要点**
- "已绑定 0.0.0.0:3334" 是否出现？
  - **否** → `StartReceive()` 未调用或 NetworkManager 未初始化，检查 Start() 执行顺序。
- "诊断: 包 X" 是否每秒出现且 X > 0？
  - **是** → UDP 正常到达，继续检查解码链路。
  - **否** → 
    - 发送端未发送或目标 IP/端口错误。
    - 防火墙阻断 UDP 3334。
    - 使用 `sudo tcpdump -i lo udp port 3334 -vv -c 5` 验证是否有包到达本机。
- "帧/切片抵达" 是否增长？
  - **是** → 分片正常上抛，检查组帧/解码。
  - **否** → 分片被丢弃或未正确解析，检查 UdpVideoHandler 逻辑。

### 3. 组帧与解码层

**日志链路** (仅编辑器下)

```
[VideoStreamService] 推送至解码
[FfmpegPipeDecoder] 解码帧: 1280x720, size=2764800
[VideoStreamService] 新建/调整纹理: 1280x720
[VideoStreamService] 每秒统计 slices=15, assembled=3, decoded=3, applied=3
```

**诊断要点**
- "推送至解码" 是否出现？
  - **否** → 分片未组成完整帧，检查 AnnexBFrameAssembler 超时/长度匹配逻辑。
- "解码帧" 是否出现？
  - **是** → 解码成功，检查纹理更新。
  - **否** → FFmpeg 无效数据、参数集缺失或解码器参数错误。
- 每秒统计的 `slices/assembled/decoded/applied` 是否都 > 0 且增长？
  - **否** → 链路某处断裂：
    - `slices=0` → UDP 未接收。
    - `assembled=0` → 组帧失败。
    - `decoded=0` → 解码失败。
    - `applied=0` → 纹理未更新（可能限频所致）。

## 快速排查流程

### 场景 A：RawImage 始终空白

1. **检查发送端**
   ```bash
   tail -f Assets/MockServerCpp/run/mock_server.log | grep "已发送"
   ```
   - 若无输出 → MockServer 未推送，检查 FFmpeg 和设备。

2. **检查接收端**
   ```bash
   # Unity Console 应显示：
   [UdpClientService] 📊 诊断: 包 X, 字节 Y, 来自 ...
   ```
   - 若无输出 → UDP 未到达，检查 IP/端口、防火墙。

3. **检查组帧**
   ```bash
   tail -f Log/RunLog.txt | grep "组帧\|推送至解码"
   ```
   - 若 "推送至解码" 出现但 "解码帧" 不出现 → FFmpeg 解码失败。

4. **检查 Apply 限频**
   - 若 `applied=0` 但 `decoded>0` → 调高 `VideoStreamService.maxApplyFps`。

### 场景 B：图像卡顿或闪烁

1. **检查统计**
   ```bash
   # 观察每秒统计：
   [VideoStreamService] 每秒统计 slices=XX, assembled=X, decoded=X, applied=Y
   ```
   - 若 `applied` 远小于 `decoded` → Apply 限频太低，增加 `maxApplyFps`。
   - 若 `decoded` 不稳定 → 网络丢包或 FFmpeg 编码卡住。

2. **检查 PPM 解析**
   - 若 "雪花" 噪点出现 → PPM 头部解析出错，检查 `SkipComments()` 调用。

### 场景 C：完全无数据

1. 确认 MockServer 正在运行：
   ```bash
   ps aux | grep mock_server
   ```

2. 确认 Unity 正在运行并已初始化 NetworkManager：
   - 查看 `Log/RunLog.txt` 中 "[NetworkManager] Awake 完成"。

3. 验证 UDP 到达（Linux）：
   ```bash
   sudo tcpdump -i lo udp port 3334 -vv -c 10
   ```
   - 若无包 → 网络配置或防火墙问题。

## 条件编译与诊断开关

### 启用详细诊断

在 Unity 中定义编译符号：

1. **编辑器模式下自动启用**（推荐）
   ```csharp
   #if UNITY_EDITOR || DIAGNOSE_UDP
       System.Console.WriteLine("[...] 诊断信息");
   #endif
   ```
   - 编辑器自动启用 `UNITY_EDITOR`，诊断日志会输出到 Console。

2. **发布版本手动启用**
   - 在 Build Settings → Player Settings → Scripting Define Symbols 中添加 `DIAGNOSE_UDP`。

### 日志输出位置

| 来源          | 输出位置                                   |
| ------------- | ------------------------------------------ |
| MockServer    | `Assets/MockServerCpp/run/mock_server.log` |
| Unity Console | 编辑器 Console 窗口                        |
| 运行日志      | `Log/RunLog.txt` (条件编译之外的部分)      |
| 调试日志      | `Log/DebugLog.txt` (仅编辑器)              |

## 示例：完整诊断会话

```
[MockServer] ✅ FFmpeg 进程就绪
[UdpClientService] 🔗 已绑定 0.0.0.0:3334
[UdpClientService] 📊 诊断: 包 50, 字节 65000, 来自 127.0.0.1
[UdpVideoHandler] 📈 诊断: 3 帧, 15 切片抵达
[组帧完成并上抛]
[推送至解码]
[解码帧: 1280x720, size=2764800]
[新建/调整纹理: 1280x720]
[每秒统计 slices=15, assembled=3, decoded=3, applied=3]
→ RawImage 显示视频 ✅
```

## 关键文件

| 文件                                                                                                                     | 用途                   |
| ------------------------------------------------------------------------------------------------------------------------ | ---------------------- |
| [Assets/MockServerCpp/src/video_sender.cpp](../MockServerCpp/src/video_sender.cpp)                                       | 服务器视频编码与发送   |
| [Assets/Scripts/Framework/Network/UdpClientService.cs](../Scripts/Framework/Network/UdpClientService.cs)                 | 客户端 UDP 接收与诊断  |
| [Assets/Scripts/Framework/Network/Handlers/UdpVideoHandler.cs](../Scripts/Framework/Network/Handlers/UdpVideoHandler.cs) | UDP 视频分片解析与诊断 |
| [Assets/Scripts/Framework/Video/VideoStreamService.cs](../Scripts/Framework/Video/VideoStreamService.cs)                 | 视频流统计与纹理更新   |
| [Assets/Scripts/Framework/Video/FfmpegPipeDecoder.cs](../Scripts/Framework/Video/FfmpegPipeDecoder.cs)                   | FFmpeg 解码与帧读取    |

---

**祝你快速定位问题！🚀**

