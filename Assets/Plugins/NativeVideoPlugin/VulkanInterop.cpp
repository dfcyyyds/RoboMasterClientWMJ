#include "VulkanInterop.h"

#include <unistd.h>

#include <cstdio>
#include <cstring>

#if defined(NVP_HAS_NVDEC) && defined(NVP_HAS_VULKAN)

// 查找满足 CUDA 外部内存需求的 Vulkan 内存类型
static uint32_t findMemoryType(VkPhysicalDevice physDevice, uint32_t typeFilter,
                               VkMemoryPropertyFlags properties) {
  VkPhysicalDeviceMemoryProperties memProps;
  vkGetPhysicalDeviceMemoryProperties(physDevice, &memProps);

  for (uint32_t i = 0; i < memProps.memoryTypeCount; i++) {
    if ((typeFilter & (1 << i)) &&
        (memProps.memoryTypes[i].propertyFlags & properties) == properties) {
      return i;
    }
  }
  return UINT32_MAX;
}

bool vulkan_interop_init(VulkanInteropContext& ctx, VkDevice device,
                         VkPhysicalDevice physDevice, VkQueue queue,
                         uint32_t queueFamilyIndex, int width, int height) {
  std::lock_guard<std::mutex> lock(ctx.mtx);

  ctx.device = device;
  ctx.physicalDevice = physDevice;
  ctx.graphicsQueue = queue;
  ctx.queueFamilyIndex = queueFamilyIndex;
  ctx.width = width;
  ctx.height = height;

  // 创建命令池
  VkCommandPoolCreateInfo poolInfo = {};
  poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
  poolInfo.queueFamilyIndex = queueFamilyIndex;
  poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;

  if (vkCreateCommandPool(device, &poolInfo, nullptr, &ctx.commandPool) !=
      VK_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] Failed to create command pool\n");
    return false;
  }

  ctx.initialized = true;
  printf("[VulkanInterop] Initialized: %dx%d\n", width, height);
  return true;
}

bool vulkan_create_external_texture(VulkanInteropContext& ctx, int width,
                                    int height) {
  std::lock_guard<std::mutex> lock(ctx.mtx);

  if (!ctx.initialized) return false;

  ctx.width = width;
  ctx.height = height;

  // 创建 Vulkan Image，带外部内存支持
  VkExternalMemoryImageCreateInfo extMemInfo = {};
  extMemInfo.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
  extMemInfo.handleTypes =
      VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT;  // Linux

  VkImageCreateInfo imageInfo = {};
  imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
  imageInfo.pNext = &extMemInfo;
  imageInfo.imageType = VK_IMAGE_TYPE_2D;
  imageInfo.format = VK_FORMAT_R8G8B8A8_UNORM;  // RGBA8 格式
  imageInfo.extent.width = width;
  imageInfo.extent.height = height;
  imageInfo.extent.depth = 1;
  imageInfo.mipLevels = 1;
  imageInfo.arrayLayers = 1;
  imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
  imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
  imageInfo.usage =
      VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
  imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
  imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

  if (vkCreateImage(ctx.device, &imageInfo, nullptr, &ctx.image) !=
      VK_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] Failed to create image\n");
    return false;
  }

  // 查询内存需求
  VkMemoryRequirements memReqs;
  vkGetImageMemoryRequirements(ctx.device, ctx.image, &memReqs);

  // 分配带外部句柄的内存
  VkExportMemoryAllocateInfo exportInfo = {};
  exportInfo.sType = VK_STRUCTURE_TYPE_EXPORT_MEMORY_ALLOCATE_INFO;
  exportInfo.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT;

  VkMemoryAllocateInfo allocInfo = {};
  allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
  allocInfo.pNext = &exportInfo;
  allocInfo.allocationSize = memReqs.size;
  allocInfo.memoryTypeIndex =
      findMemoryType(ctx.physicalDevice, memReqs.memoryTypeBits,
                     VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

  if (allocInfo.memoryTypeIndex == UINT32_MAX) {
    fprintf(stderr, "[VulkanInterop] Failed to find suitable memory type\n");
    vkDestroyImage(ctx.device, ctx.image, nullptr);
    ctx.image = VK_NULL_HANDLE;
    return false;
  }

  if (vkAllocateMemory(ctx.device, &allocInfo, nullptr, &ctx.imageMemory) !=
      VK_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] Failed to allocate image memory\n");
    vkDestroyImage(ctx.device, ctx.image, nullptr);
    ctx.image = VK_NULL_HANDLE;
    return false;
  }

  vkBindImageMemory(ctx.device, ctx.image, ctx.imageMemory, 0);

  // 获取外部内存 FD
  VkMemoryGetFdInfoKHR fdInfo = {};
  fdInfo.sType = VK_STRUCTURE_TYPE_MEMORY_GET_FD_INFO_KHR;
  fdInfo.memory = ctx.imageMemory;
  fdInfo.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT;

  // 动态加载 vkGetMemoryFdKHR
  auto vkGetMemoryFdKHR =
      (PFN_vkGetMemoryFdKHR)vkGetDeviceProcAddr(ctx.device, "vkGetMemoryFdKHR");
  if (!vkGetMemoryFdKHR) {
    fprintf(stderr, "[VulkanInterop] vkGetMemoryFdKHR not available\n");
    return false;
  }

  if (vkGetMemoryFdKHR(ctx.device, &fdInfo, &ctx.fdHandle) != VK_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] Failed to get memory FD\n");
    return false;
  }

  // 创建 ImageView
  VkImageViewCreateInfo viewInfo = {};
  viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
  viewInfo.image = ctx.image;
  viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
  viewInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
  viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
  viewInfo.subresourceRange.baseMipLevel = 0;
  viewInfo.subresourceRange.levelCount = 1;
  viewInfo.subresourceRange.baseArrayLayer = 0;
  viewInfo.subresourceRange.layerCount = 1;

  if (vkCreateImageView(ctx.device, &viewInfo, nullptr, &ctx.imageView) !=
      VK_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] Failed to create image view\n");
    return false;
  }

  printf("[VulkanInterop] Created external texture: %dx%d, fd=%d\n", width,
         height, ctx.fdHandle);
  return true;
}

bool vulkan_import_to_cuda(VulkanInteropContext& ctx, CUcontext cuCtx) {
  std::lock_guard<std::mutex> lock(ctx.mtx);

  if (ctx.fdHandle < 0) {
    fprintf(stderr, "[VulkanInterop] No valid FD to import\n");
    return false;
  }

  // 获取内存大小
  VkMemoryRequirements memReqs;
  vkGetImageMemoryRequirements(ctx.device, ctx.image, &memReqs);

  // 导入外部内存到 CUDA
  CUDA_EXTERNAL_MEMORY_HANDLE_DESC extMemDesc = {};
  extMemDesc.type = CU_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD;
  extMemDesc.handle.fd = ctx.fdHandle;
  extMemDesc.size = memReqs.size;
  extMemDesc.flags = 0;

  CUresult res = cuImportExternalMemory(&ctx.cuExtMem, &extMemDesc);
  if (res != CUDA_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] cuImportExternalMemory failed: %d\n",
            (int)res);
    return false;
  }

  // FD 被 CUDA 接管，设置为 -1 避免重复关闭
  ctx.fdHandle = -1;

  // 从外部内存映射为 CUDA mipmapped array
  CUDA_EXTERNAL_MEMORY_MIPMAPPED_ARRAY_DESC mipDesc = {};
  mipDesc.offset = 0;
  mipDesc.arrayDesc.Width = ctx.width;
  mipDesc.arrayDesc.Height = ctx.height;
  mipDesc.arrayDesc.Depth = 0;
  mipDesc.arrayDesc.Format = CU_AD_FORMAT_UNSIGNED_INT8;
  mipDesc.arrayDesc.NumChannels = 4;  // RGBA
  mipDesc.arrayDesc.Flags = CUDA_ARRAY3D_SURFACE_LDST;
  mipDesc.numLevels = 1;

  res = cuExternalMemoryGetMappedMipmappedArray(&ctx.cuMipArray, ctx.cuExtMem,
                                                &mipDesc);
  if (res != CUDA_SUCCESS) {
    fprintf(
        stderr,
        "[VulkanInterop] cuExternalMemoryGetMappedMipmappedArray failed: %d\n",
        (int)res);
    cuDestroyExternalMemory(ctx.cuExtMem);
    ctx.cuExtMem = nullptr;
    return false;
  }

  // 获取 level 0 的 array
  res = cuMipmappedArrayGetLevel(&ctx.cuArray, ctx.cuMipArray, 0);
  if (res != CUDA_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] cuMipmappedArrayGetLevel failed: %d\n",
            (int)res);
    return false;
  }

  // 创建 CUDA surface object 用于写入
  CUDA_RESOURCE_DESC resDesc = {};
  resDesc.resType = CU_RESOURCE_TYPE_ARRAY;
  resDesc.res.array.hArray = ctx.cuArray;

  res = cuSurfObjectCreate(&ctx.cuSurface, &resDesc);
  if (res != CUDA_SUCCESS) {
    fprintf(stderr, "[VulkanInterop] cuSurfObjectCreate failed: %d\n",
            (int)res);
    return false;
  }

  ctx.cuda_imported = true;
  printf("[VulkanInterop] CUDA import successful: surface=%llu\n",
         (unsigned long long)ctx.cuSurface);
  return true;
}

void* vulkan_get_texture_handle(const VulkanInteropContext& ctx) {
  // 返回 VkImage 句柄给 Unity
  return (void*)ctx.image;
}

void vulkan_interop_shutdown(VulkanInteropContext& ctx) {
  std::lock_guard<std::mutex> lock(ctx.mtx);

  // 清理 CUDA 资源
  if (ctx.cuSurface) {
    cuSurfObjectDestroy(ctx.cuSurface);
    ctx.cuSurface = 0;
  }
  if (ctx.cuMipArray) {
    cuMipmappedArrayDestroy(ctx.cuMipArray);
    ctx.cuMipArray = nullptr;
  }
  if (ctx.cuExtMem) {
    cuDestroyExternalMemory(ctx.cuExtMem);
    ctx.cuExtMem = nullptr;
  }

  // 清理 Vulkan 资源
  if (ctx.sampler) {
    vkDestroySampler(ctx.device, ctx.sampler, nullptr);
    ctx.sampler = VK_NULL_HANDLE;
  }
  if (ctx.imageView) {
    vkDestroyImageView(ctx.device, ctx.imageView, nullptr);
    ctx.imageView = VK_NULL_HANDLE;
  }
  if (ctx.image) {
    vkDestroyImage(ctx.device, ctx.image, nullptr);
    ctx.image = VK_NULL_HANDLE;
  }
  if (ctx.imageMemory) {
    vkFreeMemory(ctx.device, ctx.imageMemory, nullptr);
    ctx.imageMemory = VK_NULL_HANDLE;
  }
  if (ctx.commandPool) {
    vkDestroyCommandPool(ctx.device, ctx.commandPool, nullptr);
    ctx.commandPool = VK_NULL_HANDLE;
  }

  if (ctx.fdHandle >= 0) {
    close(ctx.fdHandle);
    ctx.fdHandle = -1;
  }

  ctx.initialized = false;
  ctx.cuda_imported = false;

  printf("[VulkanInterop] Shutdown complete, frames_uploaded=%d\n",
         ctx.frames_uploaded.load());
}

#endif  // NVP_HAS_NVDEC && NVP_HAS_VULKAN
