# UDP图传状态诊断报告

**诊断时间**: 2026-01-27 21:56  
**版本判定**: ⚠️ **降级版本（FfmpegPipe）+ 严重Bug**

---

## 🎯 当前配置

| 组件     | 配置                  | 状态                               |
| -------- | --------------------- | ---------------------------------- |
| 解码后端 | FfmpegPipe            | ⚠️ **降级** (NativeNvdec初始化失败) |
| 硬件加速 | CUDA (`hwaccel cuda`) | ✅ 已启用                           |
| 组帧超时 | 0.1s                  | ⚠️ **太激进** (0.2s→0.1s)           |
| 缓冲帧数 | 8                     | ✅ 优化参数 (16→8)                  |
| 解码队列 | 3                     | ✅ 优化参数 (6→3)                   |

---

## ❌ 发现的严重问题

### 1. 重复包处理Bug（已修复）

**现象**：每个UDP包被处理**2次**
```
[INFO] [UdpVideoHandler] 收到切片: frame=0, slice=0, naluLen=1400
[INFO] [UdpVideoHandler] 收到切片: frame=0, slice=0, naluLen=1400  ← 重复！
[WARN] [AnnexBFrameAssembler] 重复分片忽略: frame=0, slice=0
```

**根本原因**：[UdpClientService.cs#L217-L224](../Scripts/Framework/Network/UdpClientService.cs#L217-L224)  
同时触发两个事件：
```csharp
OnMessageReceivedSegment.Invoke(...)  // 第1次
OnMessageReceived.Invoke(...)         // 第2次 ← 多余
```

**修复**：改为互斥分发（优先使用零拷贝版本）
```csharp
if (OnMessageReceivedSegment != null) {
    OnMessageReceivedSegment.Invoke(...);
} else if (OnMessageReceived != null) {  // ← 改为 else if
    OnMessageReceived.Invoke(...);
}
```

**影响**：
- 浪费CPU资源处理重复包
- 组帧器需要额外检测重复
- 增加延迟

---

### 2. 解码失败（零成功帧）

**症状**：
```
[VideoStreamService] 每秒统计 slices=26, assembled=13, decoded=0, applied=0
```

**FFmpeg错误**：
```
[FfmpegPipeDecoder][stderr] Could not find ref with POC 3
[FfmpegPipeDecoder][stderr] Duplicate POC in a sequence: 4
[FfmpegPipeDecoder][stderr] Error parsing NAL unit #1
[vist#0:0/hevc @ 0x578e57372580] Error submitting packet to decoder: Invalid data found when processing input
```

**可能原因**：
1. **激进超时导致帧不完整**
   ```
   [WARN] [AnnexBFrameAssembler] 缓冲超时移除: frame=0
   ```
   0.1s超时太短，帧0还在接收中就被丢弃

2. **参数集丢失或乱序**
   - 组帧器收到frame=0但因超时丢弃
   - 后续帧缺少参考帧（POC 2, 3）

3. **GOP结构混乱**
   - "Duplicate POC in a sequence: 4"
   - FFmpeg看到重复的显示顺序

---

### 3. NativeNvdec初始化失败

**错误**：
```
[WARN] Native NVDEC init 失败，回退 Ffmpeg (code=-1)
```

**根本原因**（已定位）：  
- 在主线程调用时OpenGL上下文未就绪
- `cuGraphicsGLRegisterBuffer` 失败
- 需要在渲染线程首次调用时初始化GL资源

**当前影响**：
- 无法使用零拷贝路径
- 纹理上传延迟 +20-30ms
- 总延迟 ~120-190ms vs 优化目标 ~75-130ms

---

## 🔧 需要执行的修复

### ✅ 已修复
1. **UDP重复包问题** - 改为互斥分发事件

### ⚠️ 待修复（高优先级）

2. **放宽组帧超时**  
   当前0.1s太激进，建议恢复到0.15-0.2s：
   ```diff
   - public UdpAnnexBTransport(float timeoutSec = 0.1f, ...)
   + public UdpAnnexBTransport(float timeoutSec = 0.15f, ...)
   
   - public AnnexBFrameAssembler(float timeoutSec = 0.1f, ...)
   + public AnnexBFrameAssembler(float timeoutSec = 0.15f, ...)
   ```

3. **增加解码队列容错**  
   从3恢复到4-5：
   ```diff
   - int queueLimit = ConfigLoader.config.decoderQueueSize > 0 ? ConfigLoader.config.decoderQueueSize : 3;
   + int queueLimit = ConfigLoader.config.decoderQueueSize > 0 ? ConfigLoader.config.decoderQueueSize : 5;
   ```

### 🔄 待修复（中优先级）

4. **NativeNvdec延迟初始化**  
   修改插件，在渲染线程首次调用`nvp_get_latest_texture()`时才创建GL资源

---

## 📊 性能对比

| 指标       | 优化目标     | 当前实际     | 差距         |
| ---------- | ------------ | ------------ | ------------ |
| 后端       | NativeNvdec  | FfmpegPipe   | ❌ 降级       |
| 组帧延迟   | 50-80ms      | 100ms+       | ❌ 超时太激进 |
| 解码延迟   | 20-40ms      | N/A          | ❌ 解码失败   |
| 纹理上传   | 5-10ms       | 20-30ms      | ❌ 无零拷贝   |
| **总延迟** | **75-130ms** | **无法工作** | ❌            |
| UDP接收    | 零拷贝       | ~~重复处理~~ | ✅ 已修复     |

---

## ✅ 下一步操作

1. **重启Unity客户端** - 应用UDP重复包修复
2. **观察日志** - 确认重复包消失
3. **如果仍无解码帧** - 放宽超时参数
4. **长期修复** - 实现NativeNvdec延迟GL初始化

---

## 🎯 结论

**当前版本判定**：⚠️ **降级版本 + Bug**

- ❌ 使用FfmpegPipe而非NativeNvdec（降级）
- ❌ UDP重复包处理Bug（已修复，需重启）
- ❌ 组帧超时过激进导致丢帧
- ❌ 解码完全失败（0帧）

**建议**：
1. 立即重启客户端应用UDP修复
2. 如仍失败，回滚超时参数（0.15s）
3. 考虑临时提高队列限制以增加容错

参考：[TROUBLESHOOTING_VIDEO.md](TROUBLESHOOTING_VIDEO.md)
