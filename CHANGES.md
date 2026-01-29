# Change Log

## 2026-01-28 (下午) - NVIDIA Video Codec SDK 集成完成

- **✅ 安装完整的 NVDEC SDK 头文件**:
  - 使用 FFmpeg 维护的 nv-codec-headers（开源、免费、无需注册）
  - 包含所有必需头文件：dynlink_nvcuvid.h, dynlink_cuviddec.h 等
  - 安装到项目本地 include/ 目录
  
- **✅ 实现动态加载机制**:
  - 使用 dlopen/dlsym 运行时加载 libnvcuvid.so
  - 添加 InitCuvidFunctions() 初始化所有函数指针
  - 支持不同版本的 NVIDIA 驱动和运行时库
  
- **✅ 删除 stub 兼容层**:
  - 移除 nvcuvid_stub.h（不再需要）
  - 编译无警告无错误
  - 测试确认所有 cuvid 函数可正常加载
  
- **✅ 创建文档**:
  - SDK_INSTALL_GUIDE.md - 详细安装指南（含手动方案）
  - SDK_INSTALLATION_COMPLETE.md - 完整安装报告
  - 包含验证步骤和故障排除

**测试结果**:
```
✓ libnvcuvid.so 加载成功
✓ cuvidCreateVideoParser 函数可用
✓ cuvidCtxLockCreate 函数可用
✓ 插件大小：54KB
✓ 依赖正确：libcuda, libcudart, libOpenGL
```

**现在可以进行真实视频解码测试！**

## 2026-01-28 (上午) - NVDEC 原生解码实现
- **完成 NVDEC 原生解码集成**:
  - 实现了完整的 NVDEC 解码管道：CUVID parser callbacks (HandleVideoSequence, HandlePictureDecode, HandlePictureDisplay)
  - 添加了 CUDA NV12→RGB 颜色空间转换内核 (NV12ToRGB.cu)，使用 BT.709 标准
  - 实现了 PBO 零拷贝纹理上传：CUDA 写入 PBO → glTexSubImage2D
  - 添加了 GL 上下文错误检查和资源管理
  - 实现了日志节流机制（10 条/秒），避免高频日志影响性能
  - 添加了帧统计（frames_decoded, frames_displayed）
  
- **实现原生 UDP 组帧逻辑**:
  - 创建了 UdpFrameAssembler 类，实现切片池和环形帧缓冲
  - 支持乱序切片接收和自动组装
  - 实现了超时清理机制（1 秒超时）
  - 集成到 NativeVideoPlugin，所有 UDP 包现在在原生层组装
  
- **构建系统完善**:
  - 更新 CMakeLists.txt 支持 CUDA 编译（separable compilation）
  - 添加了 nvcuvid stub 头文件用于 SDK 缺失时的兼容性
  - 修复了 CUDA 13+ cuCtxCreate API 变化
  - 添加了构建脚本 build.sh 用于自动化编译
  - 成功编译出 libNativeVideoPlugin.so

- **待完善项**:
  - NVDEC stub 警告：需要下载完整的 NVIDIA Video Codec SDK（当前使用兼容层）
  - GL 上下文线程安全：需要确认 Unity RenderThread 上下文可用性
  - 参数集缓存：需要实现 VPS/SPS/PPS 提取和 IDR 前补发
  - 更多错误处理和诊断日志

## 2026-01-27
- Added zero-copy capable message dispatching via `IMessageSegmentHandler` and segment-based UDP pipeline.
- Updated `UdpClientService` to use pooled buffers and segment dispatch to reduce allocations.
- Updated `UdpVideoHandler` and `NetworkManager` to consume segment-based payloads.
- Introduced stub native plugin scaffolding (CMake + exported APIs) for future NVDEC/CUDA + OpenGL 4.5 integration.
- Added C# bridge `NativeVideoBridge` (P/Invoke stub) to load and call the native plugin, with graceful fallback when plugin is absent.
- Added `NATIVE_VIDEO_PLAN.md` documenting NVDEC + OpenGL 4.5 integration plan and next tasks.
- VideoStreamService now supports selecting a Native NVDEC decode backend (through NativeVideoBridge) with fallback to Ffmpeg when unavailable.
