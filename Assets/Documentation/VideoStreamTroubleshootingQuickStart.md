# 图传问题诊断快速开始

## 🎯 目标

快速验证从 MockServer → UDP → FFmpeg 解码 → Unity RawImage 的完整视频流管线是否正常工作。

---

## 📋 前置条件

1. **MockServer 已编译**
   ```bash
   cd Assets/MockServerCpp && ./start_mock.sh
   ```
   - 确保 `/dev/video0` 或其他视频设备可用（USB 摄像头或虚拟设备）。

2. **Unity 场景已加载**
   - MainScene.unity 已打开，NetworkManager 正常初始化。

3. **日志输出已启用**
   - Unity Console 可见（Window → General → Console）。
   - `Log/RunLog.txt` 可读取。

---

## 🚀 快速排查流程

### 步骤 1：启动服务器

```bash
cd /home/zby/RoboMasterClientWMJ/Assets/MockServerCpp
./start_mock.sh
```

**预期输出** (`run/mock_server.log`)
```
[MockServerCpp] Connected to tcp://127.0.0.1:3333
[VideoSender] 📡 开始推送 HEVC AnnexB 到 127.0.0.1:3334
[VideoSender] ✅ FFmpeg 进程就绪
[VideoSender] 🔍 首次读取: 1048576 字节, 头4字节: 00 00 00 01
[VideoSender] 📊 帧 #10 已发送: 2 分片, 总 2600 字节
```

**若无此输出**
- ❌ FFmpeg 未启动 → 检查 `/dev/video0` 是否存在
- ❌ 编码失败 → 查看完整 `run/mock_server.log` 中的错误信息

---

### 步骤 2：启动 Unity 客户端

1. 在 VS Code 中打开 Unity Editor。
2. 加载 `Assets/MainScene.unity`。
3. 点击 Play 运行场景。

**预期输出** (Unity Console)
```
[NetworkManager] Awake 完成
[UdpClientService] 🔗 已绑定 0.0.0.0:3334，监听来自任意源的 UDP 包
[UdpClientService] 📊 诊断: 包 50, 字节 65000, 来自 127.0.0.1:12345
[UdpVideoHandler] 📈 诊断: 3 帧, 15 切片抵达
[VideoStreamService] 解码帧: 1280x720
[VideoStreamService] 新建/调整纹理: 1280x720
```

---

## 🔍 分阶段诊断

### 阶段 A：UDP 包是否到达客户端？

**检查方法**

```bash
# 在另一个终端监听 UDP 流量（需 sudo）
sudo tcpdump -i lo udp port 3334 -vv -c 10
```

**预期输出**
```
10:30:45.123456 127.0.0.1.12345 > 127.0.0.1.3334: UDP, length 1408
```

**结果解读**
- ✅ 有包输出 → UDP 正常传输，进入阶段 B。
- ❌ 无包输出 → UDP 未到达：
  - MockServer 是否真的发送？检查 `run/mock_server.log` 中"已发送"日志。
  - 防火墙是否阻断？`sudo ufw allow 3334/udp`。
  - IP/端口是否匹配？检查 `start_mock.sh` 的 `UDP_IP_ARG` 和 `UDP_PORT_ARG`。

---

### 阶段 B：UDP 包是否被正确解析？

**检查 Console 日志**

```
[UdpClientService] 📊 诊断: 包 X, 字节 Y, 来自 Z
```

- ✅ 持续增长 X → UDP 接收正常，进入阶段 C。
- ❌ X 始终为 0 → 检查：
  - `UdpClientService.StartReceive()` 是否调用？
  - 端口号是否正确？

---

### 阶段 C：分片是否组成完整帧？

**检查 Console 日志**

```
[UdpVideoHandler] 📈 诊断: N 帧, M 切片抵达
```

- ✅ N > 0 且持续增长 → 分片正常解析，进入阶段 D。
- ❌ N = 0 → 检查：
  - 分片是否被正确解析？查看 UdpVideoHandler 逻辑。
  - 是否有长度不足 8 字节的错误？

---

### 阶段 D：是否能够组帧？

**检查 RunLog 日志**

```
tail -f Log/RunLog.txt | grep "组帧\|推送至解码"
```

**预期输出**
```
[AnnexBFrameAssembler] 组帧完成并上抛
[VideoStreamService] 推送至解码
```

- ✅ 出现此日志 → 组帧成功，进入阶段 E。
- ❌ 无此日志 → 检查：
  - 分片是否超时未等到完整帧？降低 `AnnexBFrameAssembler` 的超时阈值。
  - 帧长度是否不匹配？检查 UDP 包头的 frameLen 字段。

---

### 阶段 E：FFmpeg 是否能解码？

**检查 RunLog 日志**

```
tail -f Log/RunLog.txt | grep "解码帧"
```

**预期输出**
```
[FfmpegPipeDecoder] 解码帧: 1280x720, size=2764800
```

- ✅ 出现此日志 → 解码成功，进入阶段 F。
- ❌ 无此日志 → 检查：
  - FFmpeg stderr 是否有错误？检查 FfmpegPipeDecoder 的 stderr 日志。
  - 参数集是否缺失？检查"参数集解析"或"首个参数集"日志。
  - 像素格式是否正确？当前应为 RGB24。

---

### 阶段 F：纹理是否被应用到 RawImage？

**检查 Console 与运行日志**

```
[VideoStreamService] 新建/调整纹理: 1280x720
[VideoStreamService] 每秒统计 slices=X, assembled=Y, decoded=Z, applied=W
```

- ✅ `applied > 0` → 纹理已更新，进入最终验证。
- ❌ `applied = 0` 但 `decoded > 0` → 可能原因：
  - Apply 限频太低？增加 `VideoStreamService.maxApplyFps`（默认 30）。
  - 纹理创建失败？检查"新建/调整纹理"是否出现。

---

## ✅ 最终验证

若完成以上所有阶段且无错误，则：

1. **RawImage 应显示来自 `/dev/video0` 的实时视频**
2. **帧率应稳定在 10+ FPS**（取决于编码器设置）
3. **色彩应为 BT.709 SDR**（标准视频色彩空间）

---

## 🛠️ 常见问题解决

| 症状                 | 可能原因                  | 解决方案                                 |
| -------------------- | ------------------------- | ---------------------------------------- |
| RawImage 完全空白    | UDP 无数据                | 检查阶段 A–B                             |
| 部分画面显示后卡住   | 帧丢失或解码卡顿          | 检查网络、降低分辨率、增加 `maxApplyFps` |
| 图像有噪点（"雪花"） | PPM 头部解析错误          | 确保 `SkipComments()` 被正确调用         |
| 只显示一帧后不更新   | 解码停止或 Apply 限频过低 | 检查阶段 E–F，增加 `maxApplyFps`         |
| FFmpeg 进程退出      | 编码器配置或设备问题      | 检查 `start_mock.sh` 中的 x265 参数      |

---

## 📊 查看完整日志

**MockServer**
```bash
tail -100 Assets/MockServerCpp/run/mock_server.log
cat Assets/MockServerCpp/log/mockserver.log
```

**Unity 客户端**
```bash
tail -100 Log/RunLog.txt
cat Log/DebugLog.txt  # 仅编辑器
```

---

## 🎓 扩展：自定义诊断

### 启用完整诊断输出

在 Build Settings 中添加编译符号：
```
Player Settings → Scripting Define Symbols → DIAGNOSE_UDP
```

重新编译后，所有 `#if UNITY_EDITOR || DIAGNOSE_UDP` 代码块都会被激活。

### 减少诊断噪音

若 Console 日志过多，可以：
1. 注释掉条件编译块中的 `System.Console.WriteLine()` 调用。
2. 或在 `VideoStreamService` 中增加诊断频率阈值（如 10 秒一次）。

---

## 🚀 下一步

- ✅ 视频正常显示 → 开始功能开发（UI 布局、控制命令等）。
- ⚠️ 仍有问题 → 创建 Issue，附带完整日志与诊断信息。

---

**祝开发顺利！🎉**

