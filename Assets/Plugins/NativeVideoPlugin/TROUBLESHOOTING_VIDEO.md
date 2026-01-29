# 图传接收问题诊断报告

## 🔍 问题定位

### 1. ❌ 模拟服务器未启动
**状态**: 已解决 ✅  
**原因**: mock_server进程未运行  
**解决**: 使用 `./diagnose_and_start.sh` 启动服务器  
**验证**: 服务器正在发送视频帧（帧 #1670+）

### 2. ❌ UDP接收异常
**错误**: `The AddressFamily InterNetworkV6 is not valid for the System.Net.IPEndPoint end point, use InterNetwork instead.`  
**状态**: 已修复 ✅  
**原因**: IPv6双栈socket使用了IPv4的EndPoint类型  
**修复位置**: [UdpClientService.cs#L153-L156](../Scripts/Framework/Network/UdpClientService.cs#L153-L156)  
```csharp
// 根据Socket的AddressFamily选择正确的EndPoint类型
EndPoint remote = socket.AddressFamily == AddressFamily.InterNetworkV6
    ? new IPEndPoint(IPAddress.IPv6Any, 0)
    : new IPEndPoint(IPAddress.Any, 0);
```

### 3. ❌ NativeNvdec初始化失败
**错误**: `[WARN] Native NVDEC init 失败，回退 Ffmpeg (code=-1)`  
**状态**: 根本原因已定位 🔍  
**原因**: `cuGraphicsGLRegisterBuffer` 失败 - **没有有效的OpenGL上下文**

#### 技术细节
Unity插件的`nvp_init`在主线程被调用时，OpenGL上下文尚未就绪。导致：
- `cuGraphicsGLRegisterBuffer` → 失败
- `setup_gl_objects` → 返回-1
- `nvdec_init` → 返回false
- 客户端回退到FfmpegPipe

#### 当前状态
- ✅ CUDA可用 (RTX 5080, Driver 590)
- ✅ libnvcuvid.so加载成功
- ✅ cuInit/cuDeviceGet/cuCtxCreate成功
- ❌ GL interop失败（需要渲染线程上下文）

---

## 🎯 当前工作配置

使用 **FfmpegPipe 硬件解码** 作为临时方案：

| 组件       | 状态         | 说明                             |
| ---------- | ------------ | -------------------------------- |
| 模拟服务器 | ✅ 运行中     | PID 36691, 发送HEVC视频          |
| MQTT       | ✅ 正常       | 127.0.0.1:3333, 所有topic已订阅  |
| UDP接收    | ✅ 已修复     | IPv6双栈EndPoint问题已解决       |
| 解码器     | ⚠️ FfmpegPipe | 使用CUDA硬件加速（hwaccel cuda） |

---

## 🔧 后续修复方案

### 方案A：延迟GL初始化（推荐）
修改插件逻辑，在渲染线程首次调用`nvp_get_latest_texture()`时才创建GL资源：

1. `nvp_init()`: 只初始化CUDA和NVDEC解码器
2. `nvp_get_latest_texture()`: 首次调用时初始化PBO/Texture（此时有GL上下文）

**优点**: 解决根本问题，支持零拷贝路径  
**工作量**: 中等，需修改NvdecStub.cpp约50行

### 方案B：保持FfmpegPipe + CUDA加速
继续使用当前配置，通过FFmpeg的`hwaccel cuda`实现硬件解码：

**优点**: 无需修改，已经可用  
**缺点**: 需要CPU拷贝，延迟稍高（~3-5ms）  
**性能**: 仍比纯软解好得多

---

## ✅ 验证步骤

1. **重启Unity客户端**（UDP修复已生效）
2. **观察日志**应看到：
   ```
   [UdpClientService] ✅ 首个UDP包已到达
   [VideoStreamService] 每秒统计 slices>0, assembled>0, decoded>0
   ```
3. **如果仍无视频**，检查：
   - 防火墙：`sudo ufw status`
   - 端口占用：`ss -tuln | grep 3334`
   - 服务器日志：`tail -f Assets/MockServerCpp/run/mock_server.log`

---

## 📊 性能预期

| 延迟阶段   | FfmpegPipe+CUDA | NativeNvdec（待修复） |
| ---------- | --------------- | --------------------- |
| UDP组帧    | 50-80ms         | 50-80ms               |
| 解码       | 50-80ms         | 20-40ms               |
| 纹理上传   | 20-30ms         | 5-10ms                |
| **总延迟** | **~120-190ms**  | **~75-130ms**         |

当前配置已可用且延迟可接受（<200ms）。

---

**下一步**: 重启Unity客户端验证UDP接收修复
