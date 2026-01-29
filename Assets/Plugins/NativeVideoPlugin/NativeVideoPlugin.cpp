#include <atomic>
#include <cstdint>
#include <cstdio>
#include <mutex>
#include <vector>

#include "NvdecStub.h"

// Unity Native Plugin Interface
#include "IUnityGraphics.h"
#include "IUnityInterface.h"

#if defined(NVP_HAS_VULKAN)
#include "IUnityGraphicsVulkan.h"
#endif

#if defined(_WIN32)
#define NVP_EXPORT __declspec(dllexport)
#else
#define NVP_EXPORT __attribute__((visibility("default")))
#endif

namespace {
struct PluginState {
  std::atomic<bool> initialized{false};
  std::atomic<bool> graphicsReady{false};  // 标记图形设备是否就绪
  std::atomic<int> width{0};
  std::atomic<int> height{0};
  std::mutex mutex;
  NvdecContext* ctx = nullptr;

  // Unity Graphics
  IUnityInterfaces* unityInterfaces = nullptr;
  IUnityGraphics* unityGraphics = nullptr;
  UnityGfxRenderer renderer = kUnityGfxRendererNull;

#if defined(NVP_HAS_VULKAN)
  IUnityGraphicsVulkan* unityVulkan = nullptr;
  bool vulkanInitialized = false;
#endif
};

PluginState g_state;
}  // namespace

// Unity Graphics Device 事件回调
static void UNITY_INTERFACE_API
OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
  switch (eventType) {
    case kUnityGfxDeviceEventInitialize:
      g_state.renderer = g_state.unityGraphics->GetRenderer();
      g_state.graphicsReady.store(true);  // 标记图形设备就绪
      printf(
          "[NativeVideoPlugin] Graphics device initialized: renderer=%d (%s)\n",
          (int)g_state.renderer,
          g_state.renderer == kUnityGfxRendererOpenGLCore   ? "OpenGL Core"
          : g_state.renderer == kUnityGfxRendererVulkan     ? "Vulkan"
          : g_state.renderer == kUnityGfxRendererOpenGLES30 ? "OpenGL ES 3.0"
                                                            : "Unknown");

#if defined(NVP_HAS_VULKAN)
      if (g_state.renderer == kUnityGfxRendererVulkan) {
        g_state.unityVulkan =
            g_state.unityInterfaces->Get<IUnityGraphicsVulkan>();
        if (g_state.unityVulkan) {
          printf("[NativeVideoPlugin] Vulkan interface acquired\n");
        }
      } else {
        printf(
            "[NativeVideoPlugin] Non-Vulkan renderer, will use CUDA-GL interop "
            "path\n");
      }
#endif
      break;

    case kUnityGfxDeviceEventShutdown:
      g_state.graphicsReady.store(false);  // 标记图形设备关闭
      g_state.renderer = kUnityGfxRendererNull;
#if defined(NVP_HAS_VULKAN)
      g_state.unityVulkan = nullptr;
      g_state.vulkanInitialized = false;
#endif
      break;

    default:
      break;
  }
}

// Unity 插件加载入口点
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces* unityInterfaces) {
  g_state.unityInterfaces = unityInterfaces;
  g_state.unityGraphics = unityInterfaces->Get<IUnityGraphics>();

  if (g_state.unityGraphics) {
    g_state.unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
    // 如果设备已初始化，手动触发
    OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
  }

  printf("[NativeVideoPlugin] Plugin loaded\n");
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
  if (g_state.unityGraphics) {
    g_state.unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
  }
  g_state.unityInterfaces = nullptr;
  g_state.unityGraphics = nullptr;

  printf("[NativeVideoPlugin] Plugin unloaded\n");
}

struct NvpStats {
  int width;
  int height;
  int tex;
  int pbo;
  int cuPbo;
  int frames_decoded;
  int frames_displayed;
  int gl_ready;
  int gl_failed;
  int vulkan_enabled;
};

// 初始化 Vulkan 互操作 (内部函数)
#if defined(NVP_HAS_VULKAN)
static bool InitVulkanInterop() {
  if (!g_state.unityVulkan || !g_state.ctx || g_state.vulkanInitialized)
    return g_state.vulkanInitialized;

  UnityVulkanInstance vkInstance = g_state.unityVulkan->Instance();
  if (!vkInstance.device) {
    fprintf(stderr, "[NativeVideoPlugin] Vulkan instance not available\n");
    return false;
  }

  printf("[NativeVideoPlugin] Initializing Vulkan interop...\n");

  if (nvdec_init_vulkan(*g_state.ctx, vkInstance.device,
                        vkInstance.physicalDevice, vkInstance.graphicsQueue,
                        vkInstance.queueFamilyIndex)) {
    g_state.vulkanInitialized = true;
    printf("[NativeVideoPlugin] Vulkan interop initialized successfully\n");
    return true;
  }

  fprintf(stderr, "[NativeVideoPlugin] Vulkan interop initialization failed\n");
  return false;
}
#endif

extern "C" {
NVP_EXPORT int nvp_init(int width, int height) {
  // 检查图形设备是否就绪
  if (!g_state.graphicsReady.load()) {
    fprintf(
        stderr,
        "[NativeVideoPlugin] nvp_init called but graphics device not ready\n");
    return -10;
  }

  std::lock_guard<std::mutex> lock(g_state.mutex);
  if (g_state.initialized.load()) return 0;

  printf("[NativeVideoPlugin] nvp_init: %dx%d, renderer=%d\n", width, height,
         (int)g_state.renderer);

  g_state.ctx = new NvdecContext();
  if (!nvdec_init(*g_state.ctx, width, height)) {
    fprintf(stderr, "[NativeVideoPlugin] nvdec_init failed\n");
    delete g_state.ctx;
    g_state.ctx = nullptr;
    return -1;
  }
  g_state.width.store(width);
  g_state.height.store(height);
  g_state.initialized.store(true);

#if defined(NVP_HAS_VULKAN)
  // 如果是 Vulkan 渲染器，初始化 Vulkan 互操作
  if (g_state.renderer == kUnityGfxRendererVulkan && g_state.unityVulkan) {
    InitVulkanInterop();
  }
#endif

  printf("[NativeVideoPlugin] nvp_init success\n");
  return 0;
}

NVP_EXPORT int nvp_push_udp(const uint8_t* data, int length) {
  if (!g_state.initialized.load() || data == nullptr || length <= 0) return -1;

  std::lock_guard<std::mutex> lock(g_state.mutex);

  // C# 侧已完成组帧，这里直接推送完整 AnnexB 帧到 NVDEC
  return nvdec_push(*g_state.ctx, data, length) ? 0 : -2;
}

NVP_EXPORT int nvp_get_latest_texture() {
  if (!g_state.initialized.load()) return 0;
  return nvdec_get_texture(*g_state.ctx);
}

NVP_EXPORT int nvp_get_stats(NvpStats* out_stats) {
  if (!out_stats) return -1;
  std::lock_guard<std::mutex> lock(g_state.mutex);
  if (!g_state.initialized.load() || !g_state.ctx) return -2;
  // 使用 NVDEC 检测到的实际视频尺寸，而不是初始化时的配置尺寸
  out_stats->width =
      g_state.ctx->width > 0 ? g_state.ctx->width : g_state.width.load();
  out_stats->height =
      g_state.ctx->height > 0 ? g_state.ctx->height : g_state.height.load();
  out_stats->tex = nvdec_get_texture(*g_state.ctx);
  // 双缓冲 PBO：报告第一个 PBO 的 ID
  out_stats->pbo = g_state.ctx ? (int)g_state.ctx->pbo[0] : 0;
  out_stats->cuPbo = g_state.ctx ? (int)(uintptr_t)g_state.ctx->cuPbo[0] : 0;
#if defined(NVP_HAS_NVDEC)
  out_stats->frames_decoded = g_state.ctx->frames_decoded.load();
  out_stats->frames_displayed = g_state.ctx->frames_displayed.load();
  out_stats->gl_ready = g_state.ctx->gl_ready ? 1 : 0;
  out_stats->gl_failed = g_state.ctx->gl_failed ? 1 : 0;
#if defined(NVP_HAS_VULKAN)
  out_stats->vulkan_enabled = g_state.ctx->use_vulkan ? 1 : 0;
#else
  out_stats->vulkan_enabled = 0;
#endif
#else
  out_stats->frames_decoded = 0;
  out_stats->frames_displayed = 0;
  out_stats->gl_ready = 0;
  out_stats->gl_failed = 0;
  out_stats->vulkan_enabled = 0;
#endif
  return 0;
}

// 获取 Vulkan 纹理句柄
NVP_EXPORT void* nvp_get_vulkan_image() {
#if defined(NVP_HAS_VULKAN)
  if (!g_state.initialized.load() || !g_state.ctx) return nullptr;
  return nvdec_get_vulkan_image(*g_state.ctx);
#else
  return nullptr;
#endif
}

// 检查是否使用 Vulkan
NVP_EXPORT int nvp_is_vulkan_enabled() {
#if defined(NVP_HAS_VULKAN)
  if (g_state.ctx && g_state.ctx->use_vulkan) return 1;
#endif
  return 0;
}

NVP_EXPORT void nvp_shutdown() {
  std::lock_guard<std::mutex> lock(g_state.mutex);
  if (g_state.ctx) {
    nvdec_shutdown(*g_state.ctx);
    delete g_state.ctx;
    g_state.ctx = nullptr;
  }
  g_state.initialized.store(false);
}
}
