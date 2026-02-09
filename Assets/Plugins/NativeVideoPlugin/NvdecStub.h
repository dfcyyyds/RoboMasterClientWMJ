#pragma once
#include <cstdint>

#if defined(NVP_HAS_NVDEC)
#include <cuda.h>
#include <cuda_runtime.h>

// Use FFmpeg nv-codec-headers (installed in include/)
#include <atomic>
#include <mutex>
#include <vector>

#include "nvcuvid.h"

#if defined(NVP_HAS_VULKAN)
#include "VulkanInterop.h"
#endif

struct NvdecContext {
  CUcontext cuCtx = nullptr;
  CUvideoctxlock cuLock = nullptr;
  CUvideodecoder decoder = nullptr;
  CUvideoparser parser = nullptr;
  cudaStream_t stream = nullptr;
  int width = 0;  // 视频实际尺寸 (来自 NVDEC)
  int height = 0;

  // PBO 缓冲区常量
  static constexpr int NUM_PBO_BUFFERS = 3;  // 三缓冲 PBO

  // 初始分配尺寸 (2K) - 按需扩展到 4K
  static constexpr int INITIAL_WIDTH = 2560;   // 2K 宽度 (初始)
  static constexpr int INITIAL_HEIGHT = 1440;  // 2K 高度 (初始)

  // 最大支持尺寸 (4K)
  static constexpr int MAX_WIDTH = 3840;   // 4K 宽度 (最大)
  static constexpr int MAX_HEIGHT = 2160;  // 4K 高度 (最大)

  static constexpr size_t MAX_PBO_SIZE =
      MAX_WIDTH * MAX_HEIGHT * 3;  // ~25MB (仅用于边界检查)

  // OpenGL 路径 - 三缓冲 PBO 提升流畅度 (支持 4K 120fps)
  unsigned int pbo[NUM_PBO_BUFFERS] = {0, 0, 0};  // 三缓冲 PBO
  unsigned int tex = 0;
  CUgraphicsResource cuPbo[NUM_PBO_BUFFERS] = {nullptr, nullptr, nullptr};
  int pbo_write_idx = 0;    // CUDA 写入的 PBO 索引
  int pbo_upload_idx = 0;   // 等待 GL 上传的 PBO 索引
  int pbo_display_idx = 0;  // 当前显示的 PBO 索引
  int gl_width = 0;         // GL 对象创建时的尺寸
  int gl_height = 0;
  int pbo_alloc_width = 0;  // PBO 实际分配的尺寸 (可能大于 gl_width)
  int pbo_alloc_height = 0;
  bool gl_ready = false;
  bool gl_failed = false;
  bool cuda_gl_failed = false;

  // 异步同步优化：CUDA Event + GL Fence (三缓冲)
  cudaEvent_t cudaWriteEvent[NUM_PBO_BUFFERS] = {nullptr, nullptr, nullptr};
  bool pbo_cuda_pending[NUM_PBO_BUFFERS] = {false, false, false};
  bool pbo_ready_for_upload[NUM_PBO_BUFFERS] = {false, false,
                                                false};  // 帧已写入，等待上传
  void* glFence = nullptr;  // GLsync fence (存储为 void* 避免头文件依赖)

  // 帧丢弃统计
  std::atomic<int> frames_dropped{0};  // 因缓冲区满而丢弃的帧数

  // Vulkan 路径 (主要)
#if defined(NVP_HAS_VULKAN)
  VulkanInteropContext vkCtx;
  bool use_vulkan = false;
  cudaSurfaceObject_t vkSurface = 0;  // Vulkan surface object for CUDA writes
#endif

  bool frame_ready = false;
  std::mutex mtx;
  std::vector<uint8_t> param_sets;
  bool param_sets_sent = false;
  std::atomic<int> frames_decoded{0};
  std::atomic<int> frames_displayed{0};
};
#else
struct NvdecContext {
  int dummy = 0;
};
#endif

bool nvdec_init(NvdecContext& ctx, int width, int height);
bool nvdec_push(NvdecContext& ctx, const uint8_t* data, int len);
int nvdec_get_texture(const NvdecContext& ctx);
void nvdec_shutdown(NvdecContext& ctx);

// Vulkan 支持
#if defined(NVP_HAS_VULKAN)
bool nvdec_init_vulkan(NvdecContext& ctx, void* vkDevice, void* vkPhysDevice,
                       void* vkQueue, uint32_t queueFamilyIndex);
void* nvdec_get_vulkan_image(const NvdecContext& ctx);
#endif
