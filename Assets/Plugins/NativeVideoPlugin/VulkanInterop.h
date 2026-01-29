#pragma once

#include <atomic>
#include <cstdint>
#include <mutex>

// Vulkan 外部内存 + CUDA 互操作
// 这是完全优化方案：零拷贝 NVDEC -> Vulkan 纹理

#if defined(NVP_HAS_NVDEC) && defined(NVP_HAS_VULKAN)

#include <cuda.h>
#include <cuda_runtime.h>
#include <vulkan/vulkan.h>

struct VulkanInteropContext {
  // Vulkan 资源 (从 Unity 获取)
  VkDevice device = VK_NULL_HANDLE;
  VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
  VkQueue graphicsQueue = VK_NULL_HANDLE;
  VkCommandPool commandPool = VK_NULL_HANDLE;
  uint32_t queueFamilyIndex = 0;

  // Vulkan 纹理 (插件创建，带外部内存)
  VkImage image = VK_NULL_HANDLE;
  VkDeviceMemory imageMemory = VK_NULL_HANDLE;
  VkImageView imageView = VK_NULL_HANDLE;
  VkSampler sampler = VK_NULL_HANDLE;

  // 外部内存句柄 (用于 CUDA 导入)
  int fdHandle = -1;  // Linux: file descriptor

  // CUDA 外部内存
  CUexternalMemory cuExtMem = nullptr;
  CUmipmappedArray cuMipArray = nullptr;
  CUarray cuArray = nullptr;
  cudaSurfaceObject_t cuSurface = 0;

  // 状态
  int width = 0;
  int height = 0;
  bool initialized = false;
  bool cuda_imported = false;

  std::mutex mtx;
  std::atomic<int> frames_uploaded{0};
};

// 初始化 Vulkan 互操作 (在 Unity 图形设备初始化后调用)
bool vulkan_interop_init(VulkanInteropContext& ctx, VkDevice device,
                         VkPhysicalDevice physDevice, VkQueue queue,
                         uint32_t queueFamilyIndex, int width, int height);

// 创建带外部内存的 Vulkan 纹理
bool vulkan_create_external_texture(VulkanInteropContext& ctx, int width,
                                    int height);

// 将 Vulkan 外部内存导入到 CUDA
bool vulkan_import_to_cuda(VulkanInteropContext& ctx, CUcontext cuCtx);

// 将 NV12 数据写入 CUDA surface (NVDEC 输出 -> Vulkan 纹理)
bool vulkan_upload_nv12(VulkanInteropContext& ctx, CUdeviceptr nv12_y,
                        CUdeviceptr nv12_uv, int y_pitch, int uv_pitch,
                        cudaStream_t stream);

// 获取 Vulkan 纹理句柄 (返回给 Unity 用于显示)
void* vulkan_get_texture_handle(const VulkanInteropContext& ctx);

// 清理
void vulkan_interop_shutdown(VulkanInteropContext& ctx);

#else

// 空实现 (非 Vulkan 构建)
struct VulkanInteropContext {
  int dummy = 0;
};
inline bool vulkan_interop_init(VulkanInteropContext&, void*, void*, void*,
                                uint32_t, int, int) {
  return false;
}
inline bool vulkan_create_external_texture(VulkanInteropContext&, int, int) {
  return false;
}
inline bool vulkan_import_to_cuda(VulkanInteropContext&, void*) {
  return false;
}
inline bool vulkan_upload_nv12(VulkanInteropContext&, void*, void*, int, int,
                               void*) {
  return false;
}
inline void* vulkan_get_texture_handle(const VulkanInteropContext&) {
  return nullptr;
}
inline void vulkan_interop_shutdown(VulkanInteropContext&) {}

#endif
