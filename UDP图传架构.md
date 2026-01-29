┌─────────────────────────────────────────────────────────────────┐
│                      Unity (Vulkan 渲染后端)                     │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  VideoStreamService.cs                                   │   │
│  │    - 检测 Vulkan 模式 (NativeVideoBridge.IsVulkanEnabled) │   │
│  │    - 获取 VkImage 句柄 (NativeVideoBridge.GetVulkanImage) │   │
│  │    - Texture2D.CreateExternalTexture (RGBA32)            │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ VkImage 句柄
                              │
┌─────────────────────────────────────────────────────────────────┐
│              NativeVideoPlugin (libNativeVideoPlugin.so)        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  NativeVideoPlugin.cpp                                   │   │
│  │    - UnityPluginLoad/Unload (获取 IUnityGraphicsVulkan)   │   │
│  │    - InitVulkanInterop (获取 VkDevice/VkQueue)            │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  NvdecStub.cpp (NVDEC 解码器)                             │   │
│  │    - HandlePictureDisplay: NV12 → CUDA Surface (零拷贝)   │   │
│  │    - 自动检测渲染后端，选择 Vulkan 或 OpenGL 路径             │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  VulkanInterop.cpp (CUDA-Vulkan 互操作)                   │   │
│  │    - vulkan_create_external_texture: 创建带外部内存的 VkImage   │
│  │    - vulkan_import_to_cuda: FD → CUDA External Memory    │   │
│  │    - cuSurfObjectCreate: CUDA Surface 用于 NV12→RGBA 写入 │   │
│  └──────────────────────────────────────────────────────────┘   │
│                              │                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  NV12ToRGB.cu (CUDA Kernel)                              │   │
│  │    - LaunchNV12ToSurface: 直接写入 Vulkan surface (零拷贝) │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ H.265/HEVC AnnexB 数据
                              │
┌─────────────────────────────────────────────────────────────────┐
│                        UDP 图传数据                              │
└─────────────────────────────────────────────────────────────────┘



┌─────────────────────────────────────────────────────────────────┐
│                    图传尺寸控制链路                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. MockServer (video_sender.cpp)                               │
│     └── ffmpeg scale=2560:1440  ← 视频源分辨率 (已改为2K)          │
│                    ↓                                            │
│  2. H.265 流 (UDP传输)                                           │
│     └── SPS/PPS 包含分辨率信息                                    │
│                    ↓                                            │
│  3. NVDEC (NvdecStub.cpp - HandleVideoSequence)                 │
│     └── ctx->width/height = pFormat->coded_width/height         │
│         (从H.265流自动检测: 2560x1440)                            │
│                    ↓                                            │
│  4. NativeVideoPlugin.cpp - nvp_get_stats()                     │
│     └── 返回 ctx->width/height ← 刚修复的问题！                    │
│                    ↓                                            │
│  5. Unity (VideoStreamService.cs - Update)                      │
│     └── 检测 stats.width != nativeTexWidth                       │
│         → 重新创建 2560x1440 的纹理                               │
│                    ↓                                            │
│  6. RawImage (MainScene.unity)                                  │
│     └── RectTransform Anchor (0,0)-(1,1) 全屏拉伸                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘