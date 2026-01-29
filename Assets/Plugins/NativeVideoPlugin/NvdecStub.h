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

  // OpenGL 路径 - 双缓冲 PBO 提升流畅度
  unsigned int pbo[2] = {0, 0};  // 双缓冲 PBO
  unsigned int tex = 0;
  CUgraphicsResource cuPbo[2] = {nullptr, nullptr};  // 双缓冲 CUDA 资源
  int pbo_write_idx = 0;  // 当前写入的 PBO 索引 (CUDA 写入)
  int pbo_read_idx = 0;   // 当前读取的 PBO 索引 (GL 上传)
  int gl_width = 0;       // GL 对象创建时的尺寸
  int gl_height = 0;
  bool gl_ready = false;
  bool gl_failed = false;
  bool cuda_gl_failed = false;

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
