# UDP 图传架构说明

> 最后更新: 2026-01-29
> 当前状态: **生产就绪** - 支持 2K/4K 120fps，三缓冲 PBO 优化

---

## 1. 系统概览

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Unity (OpenGL/Vulkan 渲染后端)                      │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  VideoStreamService.cs                                                │  │
│  │    - 自动检测渲染模式: NativeVideoBridge.IsVulkanEnabled()               │  │
│  │    - Vulkan: GetVulkanImage() → Texture2D.CreateExternalTexture       │  │
│  │    - OpenGL: GetLatestTextureId() → Texture2D.CreateExternalTexture   │  │
│  │    - 动态尺寸检测: TryGetStats() 自动适配视频分辨率                        │  │
│  │    - 支持分辨率热切换 (无需重启)                                          │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ 纹理句柄 (VkImage / GLuint)
                                     │
┌─────────────────────────────────────────────────────────────────────────────┐
│                   NativeVideoPlugin (libNativeVideoPlugin.so)               │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  NativeVideoPlugin.cpp                                              │    │
│  │    - UnityPluginLoad/Unload: 获取 Unity 图形接口                     │    │
│  │    - InitVulkanInterop: 获取 VkDevice/VkQueue (Vulkan 模式)         │    │
│  │    - nvp_get_stats(): 返回解码统计 (decoded/displayed/dropped)      │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                     │                                       │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  NvdecStub.cpp (NVDEC 硬件解码器)                                    │    │
│  │                                                                     │    │
│  │  HandleVideoSequence: 解析 SPS/PPS，自动检测视频尺寸                  │    │
│  │                       └─ 从 H.265 流中提取 coded_width/height        │    │
│  │                                                                     │    │
│  │  HandlePictureDecode:  NVDEC 硬件解码 H.265 NAL                      │    │
│  │                                                                     │    │
│  │  HandlePictureDisplay: NV12 → RGB 转换 + 写入缓冲区                  │    │
│  │    ├─ Vulkan 路径: LaunchNV12ToSurface → cudaSurfaceObject (零拷贝) │    │
│  │    └─ OpenGL 路径: LaunchNV12ToRGB → 三缓冲 PBO (异步优化)           │    │
│  │                                                                     │    │
│  │  三缓冲 PBO 优化 (NUM_PBO_BUFFERS=3):                                │    │
│  │    ├─ pbo_write_idx:  CUDA 当前写入的 PBO                           │    │
│  │    ├─ pbo_upload_idx: 等待 GL 上传的 PBO                            │    │
│  │    └─ pbo_display_idx: 当前显示的 PBO                               │    │
│  │                                                                     │    │
│  │  异步同步优化:                                                       │    │
│  │    ├─ cudaEvent: 非阻塞检查 CUDA 写入完成                            │    │
│  │    └─ glFenceSync: 非阻塞检查 GL 纹理上传完成                        │    │
│  │                                                                     │    │
│  │  帧丢弃策略: 缓冲区满时丢弃旧帧，保持低延迟                           │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                     │                                       │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  VulkanInterop.cpp (CUDA-Vulkan 互操作) [可选]                       │    │
│  │    - vulkan_create_external_texture: 创建带外部内存的 VkImage        │    │
│  │    - vulkan_import_to_cuda: FD → CUDA External Memory               │    │
│  │    - cuSurfObjectCreate: CUDA Surface 用于 NV12→RGBA 直接写入       │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                     │                                       │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  NV12ToRGB.cu (CUDA Kernel)                                         │    │
│  │    - LaunchNV12ToRGB: NV12 → RGB24 写入 PBO (OpenGL 路径)           │    │
│  │    - LaunchNV12ToSurface: NV12 → RGBA 写入 Vulkan surface (零拷贝)  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ H.265/HEVC AnnexB 数据 (UDP 分片)
                                     │
┌─────────────────────────────────────────────────────────────────────────────┐
│                            UDP 图传数据流                                   │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  协议格式 (每个 UDP 包):                                              │  │
│  │    [FrameId: 2B][FragId: 2B][TotalBytes: 4B][Payload: ≤1400B]        │  │
│  │                                                                       │  │
│  │  UdpAnnexBTransport.cs:                                               │  │
│  │    - 分片重组: 按 FrameId 组帧，FragId 排序                           │  │
│  │    - 超时处理: 0.22s 超时丢弃不完整帧                                  │  │
│  │    - 缓冲管理: 最多 24 帧缓冲                                         │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                     ▲
                                     │ UDP 端口 40922
                                     │
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MockServer (video_sender.cpp)                       │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  视频源支持:                                                          │  │
│  │    1. 4K 测试视频: Test_4k_120fps.mp4 (优先)                          │  │
│  │    2. 标准测试视频: TestVedio.mp4                                     │  │
│  │    3. AVI 录像: single_*.avi                                          │  │
│  │    4. V4L2 摄像头: /dev/video*                                        │  │
│  │    5. FFmpeg 测试源: lavfi:testsrc                                    │  │
│  │                                                                       │  │
│  │  FFmpeg 编码管线:                                                     │  │
│  │    输入 → scale=2560:1440 → fps=30 → libx265 (HEVC)                  │  │
│  │         → preset=ultrafast → tune=zerolatency                        │  │
│  │         → bitrate=20Mbps → GOP=15 → AnnexB 输出                      │  │
│  │                                                                       │  │
│  │  关键编码参数:                                                        │  │
│  │    - repeat-headers=1: 每个 IDR 重复 VPS/SPS/PPS                     │  │
│  │    - aud=1: 每帧前添加 AUD (NAL type 35)                             │  │
│  │    - bframes=0: 禁用 B 帧，降低延迟                                   │  │
│  │    - scenecut=0: 禁用场景切换检测，稳定 GOP                           │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. 图传尺寸控制链路

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           尺寸自动适配流程                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. MockServer (video_sender.cpp)                                           │
│     └── FFmpeg scale=2560:1440 → 输出 2K H.265 流                          │
│                       ↓                                                     │
│  2. H.265 流 (UDP 传输)                                                     │
│     └── VPS/SPS/PPS NAL 单元包含分辨率信息                                  │
│                       ↓                                                     │
│  3. NVDEC (HandleVideoSequence 回调)                                        │
│     └── ctx->width = pFormat->coded_width   (2560)                         │
│     └── ctx->height = pFormat->coded_height (1440)                         │
│                       ↓                                                     │
│  4. PBO 管理 (setup_gl_objects)                                             │
│     └── 预分配 4K 尺寸: MAX_WIDTH=3840, MAX_HEIGHT=2160                    │
│     └── 实际显示尺寸: gl_width/gl_height = ctx->width/height               │
│     └── 三缓冲 PBO: 每个 ~25MB，总计 ~75MB GPU 内存                         │
│                       ↓                                                     │
│  5. NativeVideoPlugin (nvp_get_stats)                                       │
│     └── 返回 { width, height, decoded, displayed, dropped }                │
│                       ↓                                                     │
│  6. Unity (VideoStreamService.Update)                                       │
│     └── 检测 stats.width != nativeTexWidth?                                 │
│         → 是: 销毁旧纹理，创建新尺寸的 Texture2D                            │
│         → 否: 继续使用当前纹理                                              │
│                       ↓                                                     │
│  7. RawImage (MainScene.unity)                                              │
│     └── RectTransform Anchor (0,0)-(1,1) 全屏自适应拉伸                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. 三缓冲 PBO 工作流程 (OpenGL 路径)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        三缓冲 PBO 异步流水线                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  时间轴 →                                                                   │
│  ════════════════════════════════════════════════════════════════════════   │
│                                                                             │
│  帧 N:   [NVDEC解码] → [CUDA写PBO₀] → [cudaEvent记录]                      │
│                              ↓                                              │
│  帧 N+1: [NVDEC解码] → [CUDA写PBO₁] → [cudaEvent记录]                      │
│                              ↓          ↓                                   │
│  帧 N+2: [NVDEC解码] → [CUDA写PBO₂]    [检查PBO₀ Event]                    │
│                              ↓               ↓                              │
│  GL 线程:                               [PBO₀→纹理] → [glFence]            │
│                                                            ↓                │
│  渲染:                                                [显示纹理]            │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  缓冲区状态:                                                                │
│    pbo_write_idx   = 当前 CUDA 写入位置 (循环: 0→1→2→0)                    │
│    pbo_upload_idx  = 当前 GL 上传位置                                       │
│    pbo_cuda_pending[i] = PBO[i] 是否有未完成的 CUDA 写入                   │
│    pbo_ready_for_upload[i] = PBO[i] 是否已准备好上传                       │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  帧丢弃策略 (缓冲区满时):                                                   │
│    if (pbo_ready_for_upload[write_idx] && cudaEvent not ready):            │
│        frames_dropped++; return; // 丢弃当前帧，保持低延迟                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. 数据结构定义 (NvdecStub.h)

```cpp
struct NvdecContext {
  // 4K 120fps 优化常量
  static constexpr int NUM_PBO_BUFFERS = 3;   // 三缓冲 PBO
  static constexpr int MAX_WIDTH = 3840;      // 4K 宽度预分配
  static constexpr int MAX_HEIGHT = 2160;     // 4K 高度预分配
  static constexpr size_t MAX_PBO_SIZE = MAX_WIDTH * MAX_HEIGHT * 3; // ~25MB

  // CUDA 上下文
  CUcontext cuCtx;
  CUvideoctxlock cuLock;
  CUvideodecoder decoder;
  CUvideoparser parser;
  cudaStream_t stream;

  // 视频尺寸 (从流中自动检测)
  int width, height;

  // OpenGL 三缓冲 PBO
  unsigned int pbo[3];           // GL Buffer IDs
  unsigned int tex;              // GL Texture ID
  CUgraphicsResource cuPbo[3];   // CUDA-GL 互操作资源
  int pbo_write_idx;             // CUDA 写入索引
  int pbo_upload_idx;            // GL 上传索引
  int gl_width, gl_height;       // 当前 GL 对象尺寸

  // 异步同步
  cudaEvent_t cudaWriteEvent[3];       // CUDA 写入完成事件
  bool pbo_cuda_pending[3];            // CUDA 写入进行中
  bool pbo_ready_for_upload[3];        // 准备好 GL 上传
  void* glFence;                       // GLsync fence

  // 统计
  std::atomic<int> frames_decoded;
  std::atomic<int> frames_displayed;
  std::atomic<int> frames_dropped;     // 因缓冲区满丢弃的帧

  // Vulkan 路径 (可选)
  VulkanInteropContext vkCtx;
  bool use_vulkan;
  cudaSurfaceObject_t vkSurface;
};
```

---

## 5. 性能指标

| 指标              | 2K (2560×1440) | 4K (3840×2160) |
| ----------------- | -------------- | -------------- |
| 目标帧率          | 30-60 fps      | 30-120 fps     |
| 单帧 RGB 大小     | ~11 MB         | ~25 MB         |
| 三缓冲 PBO 总内存 | ~33 MB         | ~75 MB         |
| NVDEC 解码表面    | 8-12 个        | 8-12 个        |
| 总 GPU 内存占用   | ~100 MB        | ~275 MB        |
| 延迟 (端到端)     | <50ms          | <80ms          |

---

## 6. 关键文件清单

| 文件                  | 路径                              | 功能                       |
| --------------------- | --------------------------------- | -------------------------- |
| VideoStreamService.cs | Assets/Scripts/Framework/Video/   | Unity 端视频服务，纹理管理 |
| NativeVideoBridge.cs  | Assets/Scripts/Framework/Video/   | C# ↔ Native 桥接           |
| NvdecStub.h/cpp       | Assets/Plugins/NativeVideoPlugin/ | NVDEC 解码器 + PBO 管理    |
| NV12ToRGB.cu          | Assets/Plugins/NativeVideoPlugin/ | CUDA 色彩空间转换内核      |
| VulkanInterop.cpp     | Assets/Plugins/NativeVideoPlugin/ | Vulkan 互操作 (可选)       |
| video_sender.cpp      | Assets/MockServerCpp/src/         | MockServer 视频发送        |
| start_mock.sh         | Assets/MockServerCpp/             | MockServer 启动脚本        |

---

## 7. 启动流程

```bash
# 1. 启动 MockServer (视频源)
cd Assets/MockServerCpp
./start_mock.sh                    # 自动选择 Test_4k_120fps.mp4

# 2. 启动 Unity (OpenGL 模式)
./RoboMasterClientWMJ -force-opengl

# 3. 监控日志
tail -f Log/DebugLog.txt | grep -E "NVDEC|VideoStream"
```

---

## 8. 故障排查

| 问题         | 可能原因          | 解决方案                   |
| ------------ | ----------------- | -------------------------- |
| 黑屏无图像   | MockServer 未启动 | `./start_mock.sh`          |
| 画面撕裂     | 双缓冲不足        | 已升级为三缓冲             |
| 帧率低       | GPU 内存不足      | 检查 `nvidia-smi`          |
| 尺寸错误     | PBO 未重建        | 会自动检测并重建           |
| CUDA-GL 失败 | GPU 不匹配        | 确保 Unity 使用 NVIDIA GPU |

---

## 9. 版本历史

- **v1.0**: 初始双缓冲 PBO 实现
- **v1.1**: 添加 Vulkan 路径支持
- **v1.2**: 修复视频尺寸检测
- **v2.0**: 三缓冲 PBO + 异步 CUDA Event + GL Fence 优化
- **v2.1**: 4K 预分配 + 帧丢弃策略 (当前版本)