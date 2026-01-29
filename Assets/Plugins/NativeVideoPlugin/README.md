# Native Video Plugin - NVDEC Integration

## 概述

这是一个为 Unity 开发的原生视频解码插件，使用 NVIDIA NVDEC 硬件加速器和 CUDA-OpenGL 互操作实现零拷贝 UDP 视频流解码和渲染。

## 功能特性

- **NVDEC 硬件解码**: 使用 NVIDIA Video Codec SDK 的 CUVID API 进行 HEVC/H264 硬件解码
- **CUDA 颜色转换**: NV12 → RGB 颜色空间转换在 GPU 上完成（BT.709 标准）
- **零拷贝纹理上传**: 通过 PBO 和 CUDA-OpenGL 互操作实现零拷贝路径
- **原生 UDP 组帧**: 在原生层进行 UDP 切片组装，减少托管代码分配
- **日志节流**: 自动限制高频日志输出（10 条/秒）
- **性能统计**: 跟踪解码帧数和显示帧数

## 构建要求

### 必需组件

- **CMake** >= 3.22
- **CUDA Toolkit** >= 11.0 (推荐 13.0+)
- **NVIDIA Video Codec SDK** >= 12.0 (可选，有兼容 stub)
- **OpenGL** 4.5+
- **C++17** 编译器 (GCC 7+, Clang 5+)

### Ubuntu 24.04 安装依赖

```bash
# CUDA Toolkit
sudo apt install nvidia-cuda-toolkit cuda-cudart-dev cuda-driver-dev

# OpenGL 开发库
sudo apt install libgl1-mesa-dev

# NVIDIA Video Codec SDK 库
sudo apt install libnvcuvid1

# 构建工具
sudo apt install cmake build-essential
```

### 下载 NVIDIA Video Codec SDK (可选但推荐)

当前版本使用兼容 stub 头文件可以编译，但真实解码需要完整的 SDK：

1. 访问 https://developer.nvidia.com/nvidia-video-codec-sdk
2. 下载最新版本（12.0+）
3. 解压到 `/usr/local/video-codec-sdk` 或其他路径
4. 将 `nvcuvid.h` 等头文件复制到系统头文件路径或更新 CMake 包含路径

## 构建步骤

```bash
cd Assets/Plugins/NativeVideoPlugin
chmod +x build.sh
./build.sh
```

构建成功后，插件将安装到 `Assets/Plugins/x86_64/libNativeVideoPlugin.so`

## Unity 集成

### C# 代码示例

```csharp
using UnityEngine;
using Framework.Video;

public class VideoPlayer : MonoBehaviour
{
    private VideoStreamService videoService;
    private Texture2D videoTexture;
    
    void Start()
    {
        videoService = new VideoStreamService();
        videoService.SetDecodeBackend(DecodeBackend.NativeNvdec);
        
        // 初始化 NVDEC 解码器
        NativeVideoBridge.Init(1920, 1080);
        
        // 创建外部纹理
        int texId = NativeVideoBridge.GetLatestTexture();
        if (texId > 0)
        {
            videoTexture = Texture2D.CreateExternalTexture(
                1920, 1080, 
                TextureFormat.RGB24, 
                false, false, 
                new IntPtr(texId)
            );
        }
    }
    
    void OnDestroy()
    {
        NativeVideoBridge.Shutdown();
    }
    
    void OnUdpPacketReceived(byte[] data)
    {
        // 推送 UDP 包到原生解码器
        NativeVideoBridge.PushUdpData(data, data.Length);
        
        // 纹理会自动更新
    }
}
```

## UDP 协议格式

每个 UDP 包包含 8 字节头部 + NALU 负载：

```
0-1:  frameId (uint16_t, Little-Endian)
2-3:  sliceId (uint16_t, Little-Endian)
4-7:  frameLen (uint32_t, Little-Endian)
8+:   NALU payload (H264/HEVC AnnexB format)
```

### 组帧逻辑

- 切片池大小：16 帧同时缓冲
- 最大切片大小：64KB
- 超时清理：1 秒未完成的帧自动丢弃
- 驱逐策略：环形缓冲，最旧帧优先

## 性能优化

### 已实现

1. **ArrayPool 零拷贝**: UDP 接收使用 ArrayPool<byte> 避免分配
2. **Segment 分发**: 消息直接以 ArraySegment 分发，避免额外拷贝
3. **CUDA 流**: 异步 CUDA 操作，不阻塞 CPU
4. **PBO 异步上传**: OpenGL PBO 实现异步 DMA 传输
5. **日志节流**: 避免高频 I/O 影响性能

### 待优化

1. **参数集缓存**: VPS/SPS/PPS 缓存和 IDR 前补发
2. **多流解码**: 支持同时解码多路视频
3. **自适应分辨率**: 动态调整输出分辨率
4. **错误恢复**: 丢帧后快速同步到下一个 IDR

## 故障排除

### 编译错误

**错误**: `nvcuvid.h: No such file or directory`
- **解决**: 下载并安装 NVIDIA Video Codec SDK，或使用内置 stub（会有警告）

**错误**: `cuCtxCreate: too many arguments`
- **解决**: 更新到 CUDA 13.0+，或修改代码兼容旧版 API

**错误**: `glGenBuffers not declared`
- **解决**: 确保定义了 `GL_GLEXT_PROTOTYPES` 并包含了 `<GL/gl.h>`

### 运行时错误

**问题**: 插件加载失败
- 检查 `.so` 文件是否在 `Assets/Plugins/x86_64/` 目录
- 检查 Unity 控制台错误消息
- 使用 `ldd libNativeVideoPlugin.so` 检查依赖

**问题**: 黑屏无视频
- 确认 NVDEC 初始化成功（查看日志）
- 检查 UDP 包是否正确到达
- 验证视频格式是 HEVC 或 H264 AnnexB

**问题**: 性能低下
- 确认使用了硬件解码（非 stub）
- 检查 GPU 利用率 (`nvidia-smi`)
- 查看 `frames_decoded` 和 `frames_displayed` 统计

## 架构文档

详细技术架构请参阅：
- [NATIVE_VIDEO_PLAN.md](../../NATIVE_VIDEO_PLAN.md) - NVDEC 集成计划
- [CHANGES.md](../../CHANGES.md) - 变更日志

## 许可证

本项目使用与 Unity 项目相同的许可证。
NVIDIA Video Codec SDK 有独立的许可协议，请参阅 NVIDIA 官网。
