# Native Video Plugin 测试指南

## 前置条件

- Ubuntu 24.04
- NVIDIA GPU with NVDEC support
- Unity 6.3 TLS (6000.3.5f1)
- CUDA Toolkit 13.0+
- 已编译的 libNativeVideoPlugin.so

## 快速测试步骤

### 1. 编译插件

```bash
cd Assets/Plugins/NativeVideoPlugin
./build.sh
```

验证输出：
```
=== Build Complete ===
Plugin installed to: .../Assets/Plugins/x86_64/libNativeVideoPlugin.so
```

### 2. 验证插件加载

在 Unity 中创建测试脚本：

```csharp
using UnityEngine;
using Framework.Video;

public class NativeVideoTest : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Testing Native Video Plugin...");
        
        // 测试插件加载
        try
        {
            int result = NativeVideoBridge.Init(1920, 1080);
            if (result == 0)
            {
                Debug.Log("✓ Native Video Plugin initialized successfully");
                
                // 获取纹理 ID
                int texId = NativeVideoBridge.GetLatestTexture();
                Debug.Log($"Texture ID: {texId}");
                
                NativeVideoBridge.Shutdown();
            }
            else
            {
                Debug.LogError($"✗ Plugin init failed with code: {result}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception: {e.Message}");
        }
    }
}
```

预期输出：
- 如果有完整 NVDEC SDK：`✓ Native Video Plugin initialized successfully`
- 如果使用 stub：日志会包含 "NVDEC not available" 警告，但不会崩溃

### 3. UDP 组帧测试

创建模拟 UDP 数据的测试：

```csharp
public class UdpAssemblerTest : MonoBehaviour
{
    void Start()
    {
        // 模拟 UDP 包（8 字节头 + 数据）
        byte[] CreateTestPacket(ushort frameId, ushort sliceId, uint totalLen, byte[] payload)
        {
            var packet = new byte[8 + payload.Length];
            packet[0] = (byte)(frameId & 0xFF);
            packet[1] = (byte)(frameId >> 8);
            packet[2] = (byte)(sliceId & 0xFF);
            packet[3] = (byte)(sliceId >> 8);
            packet[4] = (byte)(totalLen & 0xFF);
            packet[5] = (byte)((totalLen >> 8) & 0xFF);
            packet[6] = (byte)((totalLen >> 16) & 0xFF);
            packet[7] = (byte)((totalLen >> 24) & 0xFF);
            System.Array.Copy(payload, 0, packet, 8, payload.Length);
            return packet;
        }
        
        NativeVideoBridge.Init(1920, 1080);
        
        // 发送 2 个切片组成一帧
        byte[] payload1 = new byte[100];
        byte[] payload2 = new byte[100];
        
        var pkt1 = CreateTestPacket(1, 0, 200, payload1);
        var pkt2 = CreateTestPacket(1, 1, 200, payload2);
        
        NativeVideoBridge.PushUdpData(pkt1, pkt1.Length);
        Debug.Log("Pushed slice 0");
        
        NativeVideoBridge.PushUdpData(pkt2, pkt2.Length);
        Debug.Log("Pushed slice 1 - frame should be complete");
        
        NativeVideoBridge.Shutdown();
    }
}
```

### 4. 性能测试

监控解码性能：

```csharp
public class PerformanceMonitor : MonoBehaviour
{
    private int lastFrameCount = 0;
    private float lastCheckTime = 0f;
    
    void Update()
    {
        if (Time.time - lastCheckTime > 1.0f)
        {
            // 这里可以添加从原生插件获取统计信息的调用
            // 目前需要从日志中查看 frames_decoded/frames_displayed
            
            lastCheckTime = Time.time;
        }
    }
}
```

## 测试检查清单

### 编译阶段

- [ ] CMake 配置成功
- [ ] 找到 CUDA Toolkit
- [ ] 找到 nvcuvid 库
- [ ] 编译所有 .cpp 和 .cu 文件无错误
- [ ] 链接成功生成 .so 文件
- [ ] 文件大小合理（约 40-50KB）

### 加载阶段

- [ ] Unity 启动时无 DLL 加载错误
- [ ] NativeVideoBridge.Init() 返回 0
- [ ] 控制台无 "DllNotFoundException" 错误
- [ ] 获取的 texture ID > 0

### 运行阶段

- [ ] UDP 包成功推送（无异常）
- [ ] 日志输出正常（有节流）
- [ ] 无内存泄漏（长时间运行）
- [ ] GPU 利用率合理（nvidia-smi）

## 常见问题

### Q: 编译警告 "Using nvcuvid stub"
**A**: 这是正常的。表示使用兼容层而非完整 SDK。可以正常编译，但真实解码需要完整 SDK。

### Q: Unity 中加载失败
**A**: 
1. 检查 .so 是否在 `Assets/Plugins/x86_64/`
2. 使用 `ldd libNativeVideoPlugin.so` 检查依赖
3. 确认 Unity 运行在 Linux 平台
4. 检查 Unity Player Settings 中的架构设置

### Q: 黑屏但无错误
**A**:
1. 确认推流端正在发送数据
2. 检查网络连接（UDP 端口）
3. 使用 Wireshark 抓包验证数据到达
4. 查看 Unity Console 的 NVDEC 日志

### Q: 性能不如预期
**A**:
1. 确认使用了真实的 NVDEC SDK（非 stub）
2. 检查 GPU 是否支持 NVDEC（nvidia-smi）
3. 验证没有在 CPU 上做不必要的拷贝
4. 使用 NVIDIA Nsight 进行性能分析

## 调试技巧

### 1. 启用详细日志

在编译前修改 `NvdecStub.cpp` 中的日志节流限制：

```cpp
constexpr int LOG_PER_SECOND = 100; // 增加到 100 条/秒
```

### 2. 使用 nvidia-smi 监控

```bash
watch -n 1 nvidia-smi
```

查看：
- GPU 利用率
- 视频引擎利用率（Video Engine Util）
- 内存使用

### 3. 使用 CUDA-GDB 调试

```bash
cuda-gdb Unity.x86_64
(gdb) break cuvidCreateDecoder
(gdb) run
```

### 4. 检查 OpenGL 错误

在 Unity 中：
```csharp
GL.IssuePluginEvent(...);
var error = GL.GetError();
if (error != 0)
{
    Debug.LogError($"GL Error: {error}");
}
```

## 下一步计划

完成测试后，可以：

1. **集成到 VideoStreamService**: 修改 VideoStreamService.cs 默认使用 NativeNvdec
2. **添加参数集处理**: 实现 VPS/SPS/PPS 缓存
3. **GL 上下文管理**: 使用 Unity RenderThread callback
4. **多流支持**: 扩展支持多路视频同时解码
5. **错误恢复**: 实现丢帧后的快速同步

## 参考资料

- [NVIDIA Video Codec SDK Documentation](https://docs.nvidia.com/video-technologies/video-codec-sdk/)
- [CUDA Programming Guide](https://docs.nvidia.com/cuda/cuda-c-programming-guide/)
- [Unity Native Plugin Interface](https://docs.unity3d.com/Manual/NativePluginInterface.html)
- [OpenGL PBO](https://www.khronos.org/opengl/wiki/Pixel_Buffer_Object)
