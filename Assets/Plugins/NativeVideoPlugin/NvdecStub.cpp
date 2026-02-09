#include "NvdecStub.h"

#include <dlfcn.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>

#if defined(NVP_HAS_NVDEC)
#define GL_GLEXT_PROTOTYPES
#include <GL/gl.h>
#include <cudaGL.h>

// Define function pointers for dynamic loading
tcuvidCreateVideoParser* cuvidCreateVideoParser = nullptr;
tcuvidParseVideoData* cuvidParseVideoData = nullptr;
tcuvidDestroyVideoParser* cuvidDestroyVideoParser = nullptr;
tcuvidCreateDecoder* cuvidCreateDecoder = nullptr;
tcuvidDecodePicture* cuvidDecodePicture = nullptr;
// 使用 64 位版本的函数指针类型（CUdeviceptr 是 64 位）
tcuvidMapVideoFrame64* cuvidMapVideoFrame = nullptr;
tcuvidUnmapVideoFrame64* cuvidUnmapVideoFrame = nullptr;
tcuvidDestroyDecoder* cuvidDestroyDecoder = nullptr;
tcuvidCtxLockCreate* cuvidCtxLockCreate = nullptr;
tcuvidCtxLockDestroy* cuvidCtxLockDestroy = nullptr;
tcuvidCtxLock* cuvidCtxLock = nullptr;
tcuvidCtxUnlock* cuvidCtxUnlock = nullptr;
tcuvidGetDecoderCaps* cuvidGetDecoderCaps = nullptr;

static bool InitCuvidFunctions();

// CUDA kernels
extern "C" cudaError_t LaunchNV12ToRGB(const uint8_t* nv12_y,
                                       const uint8_t* nv12_uv, uint8_t* rgb,
                                       int width, int height, int y_pitch,
                                       int uv_pitch, cudaStream_t stream);

#if defined(NVP_HAS_VULKAN)
extern "C" cudaError_t LaunchNV12ToSurface(const uint8_t* nv12_y,
                                           const uint8_t* nv12_uv,
                                           cudaSurfaceObject_t surface,
                                           int width, int height, int y_pitch,
                                           int uv_pitch, cudaStream_t stream);
#endif

#endif  // NVP_HAS_NVDEC

// 日志节流
static std::atomic<int> g_log_counter{0};
static std::atomic<time_t> g_log_window_start{0};
constexpr int LOG_PER_SECOND = 10;

static void throttled_log(const char* msg) {
  time_t now = time(nullptr);
  time_t start = g_log_window_start.load();
  if (now != start) {
    g_log_window_start.store(now);
    g_log_counter.store(0);
  }
  if (g_log_counter.fetch_add(1) < LOG_PER_SECOND) {
    fprintf(stderr, "[NvdecPlugin] %s\n", msg);
  }
}

#if defined(NVP_HAS_NVDEC)

static bool InitCuvidFunctions() {
  static bool initialized = false;
  if (initialized) return true;

  void* handle = dlopen("libnvcuvid.so.1", RTLD_LAZY);
  if (!handle) handle = dlopen("libnvcuvid.so", RTLD_LAZY);
  if (!handle) {
    fprintf(stderr, "[NVDEC] Failed to load libnvcuvid.so: %s\n", dlerror());
    return false;
  }

#define LOAD_FUNC(name)                                    \
  name = (t##name*)dlsym(handle, #name);                   \
  if (!name) {                                             \
    fprintf(stderr, "[NVDEC] Failed to load " #name "\n"); \
    return false;                                          \
  }

// 专门加载 64 位版本的 Map/Unmap 函数（使用 64 位类型）
#define LOAD_FUNC_MAP64(name)                                             \
  name = (t##name##64 *)dlsym(handle, #name "64");                        \
  if (!name) {                                                            \
    fprintf(stderr, "[NVDEC] Failed to load " #name                       \
                    "64, falling back to 32-bit version\n");              \
    name = (t##name##64 *)dlsym(handle, #name);                           \
  }                                                                       \
  if (!name) {                                                            \
    fprintf(stderr, "[NVDEC] Failed to load " #name " or " #name "64\n"); \
    return false;                                                         \
  }

  LOAD_FUNC(cuvidCreateVideoParser)
  LOAD_FUNC(cuvidParseVideoData)
  LOAD_FUNC(cuvidDestroyVideoParser)
  LOAD_FUNC(cuvidCreateDecoder)
  LOAD_FUNC(cuvidDecodePicture)
  LOAD_FUNC_MAP64(cuvidMapVideoFrame)
  LOAD_FUNC_MAP64(cuvidUnmapVideoFrame)
  LOAD_FUNC(cuvidDestroyDecoder)
  LOAD_FUNC(cuvidCtxLockCreate)
  LOAD_FUNC(cuvidCtxLockDestroy)
  LOAD_FUNC(cuvidCtxLock)
  LOAD_FUNC(cuvidCtxUnlock)
  LOAD_FUNC(cuvidGetDecoderCaps)
#undef LOAD_FUNC
#undef LOAD_FUNC_MAP64

  initialized = true;
  printf(
      "[NVDEC] Successfully loaded cuvid functions (v2 with cuvidCtxLock)\n");
  return true;
}

// CUVID 回调
static int CUDAAPI HandleVideoSequence(void* pUserData,
                                       CUVIDEOFORMAT* pFormat) {
  auto* ctx = static_cast<NvdecContext*>(pUserData);
  std::lock_guard<std::mutex> lock(ctx->mtx);

  // 获取 CUVID 上下文锁
  if (!cuvidCtxLock || !ctx->cuLock) {
    fprintf(stderr,
            "[NvdecPlugin] HandleVideoSequence: cuvidCtxLock not available\n");
    return 0;
  }
  CUresult lockRes = cuvidCtxLock(ctx->cuLock, 0);
  if (lockRes != CUDA_SUCCESS) {
    fprintf(stderr,
            "[NvdecPlugin] HandleVideoSequence: cuvidCtxLock failed: %d\n",
            (int)lockRes);
    return 0;
  }

  // 确保解码器销毁和创建在正确的上下文中
  auto cleanup = [&]() { cuvidCtxUnlock(ctx->cuLock, 0); };

  ctx->width = pFormat->coded_width;
  ctx->height = pFormat->coded_height;

  // H.265 需要更多的解码表面来处理 B 帧，使用流中提供的最小值 + 额外缓冲
  unsigned int numDecodeSurfaces = pFormat->min_num_decode_surfaces + 4;
  if (numDecodeSurfaces < 8) numDecodeSurfaces = 8;  // 最少 8 个

  fprintf(stderr,
          "[NVDEC] Video format: %dx%d, min_decode_surfaces=%d, using=%d\n",
          pFormat->coded_width, pFormat->coded_height,
          pFormat->min_num_decode_surfaces, numDecodeSurfaces);

  CUVIDDECODECREATEINFO dci = {};
  dci.CodecType = pFormat->codec;
  dci.ChromaFormat = pFormat->chroma_format;
  dci.OutputFormat = cudaVideoSurfaceFormat_NV12;
  dci.bitDepthMinus8 = pFormat->bit_depth_luma_minus8;
  dci.DeinterlaceMode = cudaVideoDeinterlaceMode_Weave;
  dci.ulNumOutputSurfaces = 4;  // 增加输出表面数量
  dci.ulCreationFlags = cudaVideoCreate_PreferCUVID;
  dci.ulNumDecodeSurfaces = numDecodeSurfaces;
  dci.vidLock = ctx->cuLock;
  dci.ulWidth = pFormat->coded_width;
  dci.ulHeight = pFormat->coded_height;
  dci.ulMaxWidth = pFormat->coded_width;
  dci.ulMaxHeight = pFormat->coded_height;
  dci.ulTargetWidth = pFormat->coded_width;
  dci.ulTargetHeight = pFormat->coded_height;

  if (ctx->decoder) {
    cuvidDestroyDecoder(ctx->decoder);
    ctx->decoder = nullptr;
  }

  CUresult res = cuvidCreateDecoder(&ctx->decoder, &dci);
  if (res != CUDA_SUCCESS) {
    fprintf(stderr, "[NvdecPlugin] cuvidCreateDecoder failed: %d\n", (int)res);
    cleanup();
    return 0;
  }

  fprintf(stderr, "[NVDEC] Decoder created: %dx%d, codec=%d\n", ctx->width,
          ctx->height, pFormat->codec);
  cleanup();
  return 1;
}

static int CUDAAPI HandlePictureDecode(void* pUserData,
                                       CUVIDPICPARAMS* pPicParams) {
  auto* ctx = static_cast<NvdecContext*>(pUserData);
  if (!ctx->decoder) return 0;

  // 获取 CUVID 上下文锁
  if (!cuvidCtxLock || !ctx->cuLock) {
    return 0;
  }
  CUresult lockRes = cuvidCtxLock(ctx->cuLock, 0);
  if (lockRes != CUDA_SUCCESS) {
    return 0;
  }

  CUresult res = cuvidDecodePicture(ctx->decoder, pPicParams);
  cuvidCtxUnlock(ctx->cuLock, 0);

  if (res != CUDA_SUCCESS) {
    char msg[128];
    snprintf(msg, sizeof(msg), "cuvidDecodePicture failed: %d", (int)res);
    throttled_log(msg);
    return 0;
  }
  ctx->frames_decoded++;
  return 1;
}

static int CUDAAPI HandlePictureDisplay(void* pUserData,
                                        CUVIDPARSERDISPINFO* pDispInfo) {
  auto* ctx = static_cast<NvdecContext*>(pUserData);
  std::lock_guard<std::mutex> lock(ctx->mtx);

  if (!ctx->decoder || ctx->width <= 0 || ctx->height <= 0) return 0;

  // 使用 cuvidCtxLock 来确保线程安全的 CUDA 上下文访问
  if (!cuvidCtxLock) {
    throttled_log("cuvidCtxLock function not loaded!");
    return 0;
  }
  CUresult lockRes = cuvidCtxLock(ctx->cuLock, 0);
  if (lockRes != CUDA_SUCCESS) {
    char msg[128];
    snprintf(msg, sizeof(msg), "cuvidCtxLock failed: %d", (int)lockRes);
    throttled_log(msg);
    return 0;
  }

  // 检查当前上下文，如果需要则推送
  CUcontext currentCtx = nullptr;
  cuCtxGetCurrent(&currentCtx);
  bool needPop = false;

  // 诊断：打印上下文状态
  static int diag_count = 0;
  if (diag_count++ < 5) {
    fprintf(stderr,
            "[NvdecPlugin] HandlePictureDisplay: currentCtx=%p, expected=%p\n",
            (void*)currentCtx, (void*)ctx->cuCtx);
  }

  if (currentCtx != ctx->cuCtx) {
    CUresult pushRes = cuCtxPushCurrent(ctx->cuCtx);
    if (pushRes != CUDA_SUCCESS) {
      char msg[128];
      snprintf(msg, sizeof(msg), "cuCtxPushCurrent in callback failed: %d",
               (int)pushRes);
      throttled_log(msg);
      cuvidCtxUnlock(ctx->cuLock, 0);
      return 0;
    }
    needPop = true;
    if (diag_count <= 6) {
      fprintf(stderr, "[NvdecPlugin] Pushed context successfully\n");
    }
  }

  // 使用 lambda 作为退出时的清理
  auto cleanup = [&]() {
    if (needPop) {
      CUcontext poppedCtx;
      cuCtxPopCurrent(&poppedCtx);
    }
    cuvidCtxUnlock(ctx->cuLock, 0);
  };

  // Map decoded surface
  CUVIDPROCPARAMS vpp = {};
  vpp.progressive_frame = 1;

  CUdeviceptr dpSrcFrame = 0;
  unsigned int pitch = 0;

  // 诊断：打印 picture_index 和 decoder 状态
  static int map_diag_count = 0;
  if (map_diag_count++ < 10) {
    fprintf(stderr,
            "[NvdecPlugin] cuvidMapVideoFrame: decoder=%p, picture_index=%d, "
            "cuLock=%p\n",
            (void*)ctx->decoder, pDispInfo->picture_index, (void*)ctx->cuLock);
  }

  // 验证解码器状态
  if (!ctx->decoder) {
    fprintf(stderr, "[NvdecPlugin] ERROR: decoder is NULL!\n");
    cleanup();
    return 0;
  }

  CUresult res = cuvidMapVideoFrame(ctx->decoder, pDispInfo->picture_index,
                                    &dpSrcFrame, &pitch, &vpp);
  if (res != CUDA_SUCCESS) {
    char msg[128];
    snprintf(msg, sizeof(msg), "cuvidMapVideoFrame failed: %d", (int)res);
    throttled_log(msg);
    cleanup();
    return 0;
  }

  const uint8_t* y_plane = reinterpret_cast<const uint8_t*>(dpSrcFrame);
  const uint8_t* uv_plane = y_plane + pitch * ctx->height;

#if defined(NVP_HAS_VULKAN)
  // Vulkan 路径: 零拷贝写入 Vulkan surface
  if (ctx->use_vulkan && ctx->vkSurface) {
    LaunchNV12ToSurface(y_plane, uv_plane, ctx->vkSurface, ctx->width,
                        ctx->height, pitch, pitch, ctx->stream);
    cudaStreamSynchronize(ctx->stream);
    ctx->frame_ready = true;
    ctx->frames_displayed++;
    cuvidUnmapVideoFrame(ctx->decoder, dpSrcFrame);
    cleanup();
    return 1;
  }
#endif

  // OpenGL 路径 - 三缓冲 PBO (支持 4K 120fps)
  int write_idx = ctx->pbo_write_idx;

  // 帧丢弃策略：如果写入缓冲区仍在等待上传，丢弃最旧的帧
  if (ctx->pbo_ready_for_upload[write_idx] ||
      ctx->pbo_cuda_pending[write_idx]) {
    // 当前写入位置被占用，检查是否可以覆盖
    if (ctx->cudaWriteEvent[write_idx]) {
      cudaError_t ev_status = cudaEventQuery(ctx->cudaWriteEvent[write_idx]);
      if (ev_status == cudaErrorNotReady) {
        // CUDA 写入尚未完成，必须丢弃这一帧
        ctx->frames_dropped++;
        static int drop_log = 0;
        if (++drop_log <= 10 || drop_log % 100 == 0) {
          fprintf(
              stderr,
              "[NVDEC] Frame dropped (buffer full): dropped=%d, write_idx=%d\n",
              ctx->frames_dropped.load(), write_idx);
        }
        cuvidUnmapVideoFrame(ctx->decoder, dpSrcFrame);
        cleanup();
        return 1;  // 返回成功，继续解码
      }
    }
    // CUDA 写入已完成但还没上传，可以覆盖（丢弃旧帧）
    ctx->pbo_ready_for_upload[write_idx] = false;
    ctx->pbo_cuda_pending[write_idx] = false;
  }

  if (ctx->cuPbo[write_idx]) {
    size_t pbo_size = 0;
    CUdeviceptr dpPbo = 0;
    bool pbo_mapped = false;

    res = cuGraphicsMapResources(1, &ctx->cuPbo[write_idx], ctx->stream);
    if (res == CUDA_SUCCESS) {
      pbo_mapped = true;
      res = cuGraphicsResourceGetMappedPointer(&dpPbo, &pbo_size,
                                               ctx->cuPbo[write_idx]);
    }

    if (res == CUDA_SUCCESS) {
      // 安全检查：确保 PBO 大小足够
      size_t required_size = (size_t)ctx->width * ctx->height * 3;

      // 诊断：打印 pitch 和大小信息
      static int diag_rgb = 0;
      if (diag_rgb++ < 10) {
        fprintf(stderr,
                "[NVDEC] NV12->RGB (triple-buf): write_idx=%d, src_pitch=%u, "
                "pbo_size=%zu, wxh=%dx%d, required=%zu\n",
                write_idx, pitch, pbo_size, ctx->width, ctx->height,
                required_size);
      }

      // 关键安全检查：如果 PBO 太小，跳过这一帧
      if (pbo_size < required_size) {
        fprintf(stderr,
                "[NVDEC] ERROR: PBO size mismatch! pbo=%zu < required=%zu, "
                "skipping frame\n",
                pbo_size, required_size);
        cuGraphicsUnmapResources(1, &ctx->cuPbo[write_idx], ctx->stream);
        cuvidUnmapVideoFrame(ctx->decoder, dpSrcFrame);
        cleanup();
        return 0;  // 跳过这一帧，等待 GL 对象重新创建
      }

      LaunchNV12ToRGB(y_plane, uv_plane, reinterpret_cast<uint8_t*>(dpPbo),
                      ctx->width, ctx->height, pitch, pitch, ctx->stream);

      // 异步优化：使用 cudaEvent 代替 cudaStreamSynchronize
      if (ctx->cudaWriteEvent[write_idx]) {
        cudaEventRecord(ctx->cudaWriteEvent[write_idx], ctx->stream);
        ctx->pbo_cuda_pending[write_idx] = true;
      }

      // Unmap 必须在同一个 stream 上，CUDA driver 会自动排序
      cuGraphicsUnmapResources(1, &ctx->cuPbo[write_idx], ctx->stream);

      // 三缓冲：标记当前缓冲区已就绪，移动写入索引
      ctx->pbo_ready_for_upload[write_idx] = true;
      ctx->pbo_write_idx = (write_idx + 1) % NvdecContext::NUM_PBO_BUFFERS;
      ctx->frame_ready = true;
      ctx->frames_displayed++;
    } else {
      char msg[128];
      snprintf(msg, sizeof(msg), "cuGraphicsMapResources/GetPointer failed: %d",
               (int)res);
      throttled_log(msg);
      // 重要：如果 Map 成功但 GetPointer 失败，需要 Unmap
      if (pbo_mapped) {
        cuGraphicsUnmapResources(1, &ctx->cuPbo[write_idx], ctx->stream);
      }
    }
  } else {
    // cuPbo 未注册，这说明 nvdec_get_texture 还没被调用
    static int skip_count = 0;
    if (++skip_count <= 5) {
      fprintf(
          stderr,
          "[NvdecPlugin] HandlePictureDisplay: cuPbo[%d] not registered yet "
          "(skip #%d)\n",
          write_idx, skip_count);
    }
  }

  cuvidUnmapVideoFrame(ctx->decoder, dpSrcFrame);
  cleanup();

  return 1;
}

// 销毁 GL 对象（用于尺寸变化时重新创建）
static void destroy_gl_objects(NvdecContext& ctx) {
  fprintf(stderr,
          "[NVDEC] Destroying GL objects: pbo[0]=%u, pbo[1]=%u, pbo[2]=%u, "
          "tex=%u\n",
          ctx.pbo[0], ctx.pbo[1], ctx.pbo[2], ctx.tex);

  // 清理 GL Fence
  if (ctx.glFence) {
    glDeleteSync(static_cast<GLsync>(ctx.glFence));
    ctx.glFence = nullptr;
  }

  // 需要正确的 CUDA 上下文来注销资源
  CUcontext currentCtx = nullptr;
  cuCtxGetCurrent(&currentCtx);
  bool needPush = (currentCtx != ctx.cuCtx);

  if (needPush && ctx.cuCtx) {
    cuCtxPushCurrent(ctx.cuCtx);
  }

  // 注销三缓冲 CUDA 资源
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (ctx.cuPbo[i]) {
      CUresult res = cuGraphicsUnregisterResource(ctx.cuPbo[i]);
      if (res != CUDA_SUCCESS) {
        fprintf(stderr, "[NVDEC] cuGraphicsUnregisterResource[%d] failed: %d\n",
                i, (int)res);
      }
      ctx.cuPbo[i] = nullptr;
    }
    // 重置状态
    ctx.pbo_cuda_pending[i] = false;
    ctx.pbo_ready_for_upload[i] = false;
  }

  if (needPush && ctx.cuCtx) {
    CUcontext popped;
    cuCtxPopCurrent(&popped);
  }

  // 删除三缓冲 PBO
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (ctx.pbo[i]) {
      glDeleteBuffers(1, &ctx.pbo[i]);
      ctx.pbo[i] = 0;
    }
  }
  if (ctx.tex) {
    glDeleteTextures(1, &ctx.tex);
    ctx.tex = 0;
  }
  ctx.gl_ready = false;
  ctx.gl_width = 0;
  ctx.gl_height = 0;
  ctx.pbo_alloc_width = 0;  // 重置分配尺寸跟踪
  ctx.pbo_alloc_height = 0;
  ctx.pbo_write_idx = 0;
  ctx.pbo_upload_idx = 0;
  ctx.pbo_display_idx = 0;
  fprintf(stderr, "[NVDEC] GL objects destroyed\n");
}

static int setup_gl_objects(NvdecContext& ctx, int w, int h) {
  // 检查是否需要扩展 PBO 缓冲区
  bool need_expand = false;
  if (ctx.gl_ready) {
    // 如果当前分辨率超过已分配的 PBO 大小，需要扩展
    if (w > ctx.pbo_alloc_width || h > ctx.pbo_alloc_height) {
      fprintf(stderr,
              "[NVDEC] Resolution %dx%d exceeds allocated PBO %dx%d, "
              "expanding...\n",
              w, h, ctx.pbo_alloc_width, ctx.pbo_alloc_height);
      need_expand = true;
      destroy_gl_objects(ctx);
    } else if (ctx.gl_width != w || ctx.gl_height != h) {
      // 分辨率变化但在已分配范围内，只需更新纹理
      fprintf(stderr,
              "[NVDEC] Size changed from %dx%d to %dx%d (within alloc %dx%d), "
              "updating texture\n",
              ctx.gl_width, ctx.gl_height, w, h, ctx.pbo_alloc_width,
              ctx.pbo_alloc_height);
      ctx.gl_width = w;
      ctx.gl_height = h;
      // 重新创建纹理以匹配新尺寸
      if (ctx.tex != 0) {
        glDeleteTextures(1, &ctx.tex);
        ctx.tex = 0;
      }
      glGenTextures(1, &ctx.tex);
      glBindTexture(GL_TEXTURE_2D, ctx.tex);
      glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
      glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
      glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB8, w, h, 0, GL_RGB, GL_UNSIGNED_BYTE,
                   nullptr);
      glBindTexture(GL_TEXTURE_2D, 0);
      return 0;
    } else {
      return 0;  // 无变化
    }
  }

  // 清除之前可能遗留的 GL 错误
  while (glGetError() != GL_NO_ERROR) {
  }

  // 智能分配策略：
  // - 初始分配 2K (2560x1440)，节省 ~42MB GPU 内存
  // - 如果实际分辨率超过 2K，按需扩展到 4K
  int alloc_w, alloc_h;

  if (need_expand || w > NvdecContext::INITIAL_WIDTH ||
      h > NvdecContext::INITIAL_HEIGHT) {
    // 需要 4K 分配
    alloc_w = NvdecContext::MAX_WIDTH;
    alloc_h = NvdecContext::MAX_HEIGHT;
    fprintf(stderr, "[NVDEC] Using 4K allocation for %dx%d content\n", w, h);
  } else {
    // 使用 2K 初始分配
    alloc_w = NvdecContext::INITIAL_WIDTH;
    alloc_h = NvdecContext::INITIAL_HEIGHT;
    fprintf(stderr, "[NVDEC] Using 2K initial allocation for %dx%d content\n",
            w, h);
  }

  size_t pbo_alloc_size = (size_t)alloc_w * alloc_h * 3;

  fprintf(stderr,
          "[NVDEC] Creating triple-buffer GL objects: display=%dx%d, "
          "alloc=%dx%d (%zu bytes each, total=%zu MB)\n",
          w, h, alloc_w, alloc_h, pbo_alloc_size,
          (pbo_alloc_size * NvdecContext::NUM_PBO_BUFFERS) / (1024 * 1024));

  ctx.gl_width = w;
  ctx.gl_height = h;

  // 创建三缓冲 PBO
  glGenBuffers(NvdecContext::NUM_PBO_BUFFERS, ctx.pbo);
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (ctx.pbo[i] == 0) {
      char msg[64];
      snprintf(msg, sizeof(msg), "glGenBuffers returned 0 for PBO[%d]", i);
      throttled_log(msg);
      ctx.gl_failed = true;
      return -1;
    }
    glBindBuffer(GL_PIXEL_UNPACK_BUFFER, ctx.pbo[i]);
    // 预分配 4K 大小，使用 GL_STREAM_DRAW 优化频繁更新
    glBufferData(GL_PIXEL_UNPACK_BUFFER, pbo_alloc_size, nullptr,
                 GL_STREAM_DRAW);
  }
  glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);

  glGenTextures(1, &ctx.tex);
  if (ctx.tex == 0) {
    throttled_log("glGenTextures returned 0");
    ctx.gl_failed = true;
    return -1;
  }
  glBindTexture(GL_TEXTURE_2D, ctx.tex);
  glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
  glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
  glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB8, w, h, 0, GL_RGB, GL_UNSIGNED_BYTE,
               nullptr);
  glBindTexture(GL_TEXTURE_2D, 0);

  GLenum gl_err = glGetError();
  if (gl_err != GL_NO_ERROR) {
    char msg[128];
    snprintf(msg, sizeof(msg), "GL error after setup: %d", gl_err);
    throttled_log(msg);
    ctx.gl_failed = true;
    return -1;
  }

  ctx.gl_ready = true;
  ctx.pbo_alloc_width = alloc_w;  // 记录实际分配的 PBO 尺寸
  ctx.pbo_alloc_height = alloc_h;
  ctx.pbo_write_idx = 0;
  ctx.pbo_upload_idx = 0;
  ctx.pbo_display_idx = 0;
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    ctx.pbo_ready_for_upload[i] = false;
  }
  printf(
      "[NVDEC] Triple-buffer GL objects created: pbo[0]=%u pbo[1]=%u pbo[2]=%u "
      "tex=%u, display=%dx%d, alloc=%dx%d\n",
      ctx.pbo[0], ctx.pbo[1], ctx.pbo[2], ctx.tex, w, h, alloc_w, alloc_h);
  return 0;
}
#endif  // NVP_HAS_NVDEC

bool nvdec_init(NvdecContext& ctx, int width, int height) {
#if defined(NVP_HAS_NVDEC)
  if (!InitCuvidFunctions()) {
    throttled_log("InitCuvidFunctions failed");
    return false;
  }

  if (cuInit(0) != CUDA_SUCCESS) {
    throttled_log("cuInit failed");
    return false;
  }

  CUdevice dev = 0;
  if (cuDeviceGet(&dev, 0) != CUDA_SUCCESS) {
    throttled_log("cuDeviceGet failed");
    return false;
  }

  CUctxCreateParams params = {};
  if (cuCtxCreate(&ctx.cuCtx, &params, CU_CTX_SCHED_AUTO, dev) !=
      CUDA_SUCCESS) {
    throttled_log("cuCtxCreate failed");
    return false;
  }

  if (cuvidCtxLockCreate(&ctx.cuLock, ctx.cuCtx) != CUDA_SUCCESS) {
    throttled_log("cuvidCtxLockCreate failed");
    return false;
  }

  if (cudaStreamCreate(&ctx.stream) != cudaSuccess) {
    throttled_log("cudaStreamCreate failed");
    return false;
  }

  // 创建 CUDA Events 用于异步同步（三缓冲）
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    cudaError_t ev_err = cudaEventCreateWithFlags(&ctx.cudaWriteEvent[i],
                                                  cudaEventDisableTiming);
    if (ev_err != cudaSuccess) {
      fprintf(stderr, "[NVDEC] cudaEventCreate[%d] failed: %d\n", i,
              (int)ev_err);
      // 非致命错误，继续但禁用异步优化
      ctx.cudaWriteEvent[i] = nullptr;
    }
    ctx.pbo_cuda_pending[i] = false;
    ctx.pbo_ready_for_upload[i] = false;
  }
  printf("[NVDEC] CUDA Events created for triple-buffer async optimization\n");

  CUVIDPARSERPARAMS vpp = {};
  vpp.CodecType = cudaVideoCodec_HEVC;
  vpp.ulMaxNumDecodeSurfaces = 4;
  vpp.ulMaxDisplayDelay = 0;
  vpp.pUserData = &ctx;
  vpp.pfnSequenceCallback = HandleVideoSequence;
  vpp.pfnDecodePicture = HandlePictureDecode;
  vpp.pfnDisplayPicture = HandlePictureDisplay;

  if (cuvidCreateVideoParser(&ctx.parser, &vpp) != CUDA_SUCCESS) {
    throttled_log("cuvidCreateVideoParser failed");
    return false;
  }

  // 使用传入的尺寸，若为0则默认 2K (2560x1440) 以减少启动时 PBO 重建
  ctx.width = (width > 0) ? width : 2560;
  ctx.height = (height > 0) ? height : 1440;

  printf("[NVDEC] Initialized: %dx%d\n", ctx.width, ctx.height);
  return true;
#else
  (void)ctx;
  (void)width;
  (void)height;
  throttled_log("NVDEC not built");
  return false;
#endif
}

bool nvdec_push(NvdecContext& ctx, const uint8_t* data, int len) {
#if defined(NVP_HAS_NVDEC)
  if (!data || len <= 0 || !ctx.parser) return false;

  CUVIDSOURCEDATAPACKET pkt = {};
  pkt.payload = data;
  pkt.payload_size = len;
  pkt.flags = CUVID_PKT_TIMESTAMP;

  CUresult res = cuvidParseVideoData(ctx.parser, &pkt);
  if (res != CUDA_SUCCESS) {
    throttled_log("cuvidParseVideoData failed");
    return false;
  }
  return true;
#else
  (void)ctx;
  (void)data;
  (void)len;
  return false;
#endif
}

int nvdec_get_texture(const NvdecContext& ctx) {
#if defined(NVP_HAS_NVDEC)
  NvdecContext& mut_ctx = const_cast<NvdecContext&>(ctx);

#if defined(NVP_HAS_VULKAN)
  // Vulkan 路径: 直接返回，纹理更新在 HandlePictureDisplay 中完成
  if (mut_ctx.use_vulkan) {
    // Vulkan 模式不使用 OpenGL 纹理
    // Unity 会直接使用 nvdec_get_vulkan_image 返回的 VkImage
    return 0;
  }
#endif

  // OpenGL 路径
  // 获取当前视频尺寸（可能在 HandleVideoSequence 中更新）
  int w = mut_ctx.width;
  int h = mut_ctx.height;

  // 如果视频尺寸还未知，等待
  if (w <= 0 || h <= 0) {
    static int wait_count = 0;
    if (++wait_count <= 5) {
      fprintf(stderr, "[NVDEC] Waiting for video size (current: %dx%d)\n", w,
              h);
    }
    return 0;
  }

  // 创建或重新创建 GL 对象（如果尺寸变化）
  if (!mut_ctx.gl_ready || mut_ctx.tex == 0 || mut_ctx.pbo[0] == 0 ||
      mut_ctx.pbo[1] == 0 || mut_ctx.pbo[2] == 0 || mut_ctx.gl_width != w ||
      mut_ctx.gl_height != h) {
    if (setup_gl_objects(mut_ctx, w, h) != 0) {
      fprintf(stderr, "[NVDEC] GL setup failed\n");
      return 0;
    }
  }

  // 延迟注册 CUDA-GL 互操作 (三缓冲)
  bool need_register = false;
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (mut_ctx.pbo[i] && !mut_ctx.cuPbo[i] && !mut_ctx.cuda_gl_failed) {
      need_register = true;
      break;
    }
  }

  if (need_register) {
    CUcontext currentCtx = nullptr;
    cuCtxGetCurrent(&currentCtx);
    bool needPush = (currentCtx != mut_ctx.cuCtx);

    if (needPush) {
      if (cuCtxPushCurrent(mut_ctx.cuCtx) != CUDA_SUCCESS) {
        fprintf(stderr, "[NVDEC] cuCtxPushCurrent failed\n");
        return 0;
      }
    }

    // 注册三缓冲 PBO
    for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
      if (mut_ctx.pbo[i] && !mut_ctx.cuPbo[i]) {
        CUresult cu_res = cuGraphicsGLRegisterBuffer(
            &mut_ctx.cuPbo[i], mut_ctx.pbo[i],
            CU_GRAPHICS_REGISTER_FLAGS_WRITE_DISCARD);
        if (cu_res != CUDA_SUCCESS) {
          fprintf(stderr, "[NVDEC] CUDA-GL registration[%d] failed: %d\n", i,
                  (int)cu_res);
          mut_ctx.cuda_gl_failed = true;
        } else {
          fprintf(stderr, "[NVDEC] CUDA-GL registration[%d] SUCCESS\n", i);
        }
      }
    }

    if (needPush) {
      CUcontext popped;
      cuCtxPopCurrent(&popped);
    }

    if (mut_ctx.cuda_gl_failed) {
      fprintf(stderr,
              "[NVDEC] This usually means Unity is using a different GPU for "
              "rendering.\n");
      fprintf(
          stderr,
          "[NVDEC] Please enable Vulkan mode or run Unity with NVIDIA GPU.\n");
      return 0;
    }
  }

  // 上传 PBO 到纹理 - 三缓冲异步优化
  // 查找一个已就绪的缓冲区进行上传
  int upload_idx = -1;
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    int idx = (mut_ctx.pbo_upload_idx + i) % NvdecContext::NUM_PBO_BUFFERS;
    if (mut_ctx.pbo_ready_for_upload[idx] && mut_ctx.cuPbo[idx]) {
      // 检查 CUDA 写入是否完成
      if (mut_ctx.pbo_cuda_pending[idx] && mut_ctx.cudaWriteEvent[idx]) {
        cudaError_t event_status = cudaEventQuery(mut_ctx.cudaWriteEvent[idx]);
        if (event_status == cudaErrorNotReady) {
          // 这个缓冲区的 CUDA 写入尚未完成，尝试下一个
          continue;
        } else if (event_status == cudaSuccess) {
          mut_ctx.pbo_cuda_pending[idx] = false;
        }
      }
      upload_idx = idx;
      break;
    }
  }

  if (upload_idx >= 0) {
    // 删除旧的 GL Fence（如果存在）
    GLsync oldFence = static_cast<GLsync>(mut_ctx.glFence);
    if (oldFence) {
      // 等待上一次纹理上传完成（非阻塞检查）
      GLenum waitResult =
          glClientWaitSync(oldFence, GL_SYNC_FLUSH_COMMANDS_BIT, 0);
      if (waitResult == GL_TIMEOUT_EXPIRED) {
        // 上一次上传还未完成，跳过
        static int fence_skip = 0;
        if (++fence_skip <= 5) {
          fprintf(stderr, "[NVDEC] GL fence not ready, skipping upload\n");
        }
        return static_cast<int>(mut_ctx.tex);
      }
      glDeleteSync(oldFence);
      mut_ctx.glFence = nullptr;
    }

    static int tex_upload_count = 0;
    glBindBuffer(GL_PIXEL_UNPACK_BUFFER, mut_ctx.pbo[upload_idx]);
    glBindTexture(GL_TEXTURE_2D, mut_ctx.tex);
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, mut_ctx.gl_width, mut_ctx.gl_height,
                    GL_RGB, GL_UNSIGNED_BYTE, 0);
    GLenum gl_err = glGetError();
    if (gl_err != GL_NO_ERROR) {
      fprintf(stderr, "[NVDEC] glTexSubImage2D error: 0x%x (size=%dx%d)\n",
              gl_err, mut_ctx.gl_width, mut_ctx.gl_height);
    }

    // 创建 GL Fence 追踪上传完成
    mut_ctx.glFence =
        static_cast<void*>(glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0));

    glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    glBindTexture(GL_TEXTURE_2D, 0);

    // 标记此缓冲区已上传
    mut_ctx.pbo_ready_for_upload[upload_idx] = false;
    mut_ctx.pbo_upload_idx = (upload_idx + 1) % NvdecContext::NUM_PBO_BUFFERS;
    mut_ctx.frame_ready = false;

    tex_upload_count++;
    if (tex_upload_count <= 10 || tex_upload_count % 60 == 0) {
      fprintf(stderr,
              "[NVDEC] Texture uploaded (count=%d, upload_idx=%d, decoded=%d, "
              "displayed=%d, dropped=%d, triple-buf)\n",
              tex_upload_count, upload_idx, mut_ctx.frames_decoded.load(),
              mut_ctx.frames_displayed.load(), mut_ctx.frames_dropped.load());
    }
  } else if (!mut_ctx.cuPbo[0] && !mut_ctx.cuPbo[1] && !mut_ctx.cuPbo[2]) {
    static bool warned = false;
    if (!warned) {
      fprintf(stderr, "[NVDEC] nvdec_get_texture: cuPbo[] not registered\n");
      warned = true;
    }
  }

  return static_cast<int>(mut_ctx.tex);
#else
  (void)ctx;
  return 0;
#endif
}

void nvdec_shutdown(NvdecContext& ctx) {
#if defined(NVP_HAS_NVDEC)
#if defined(NVP_HAS_VULKAN)
  if (ctx.use_vulkan) {
    vulkan_interop_shutdown(ctx.vkCtx);
  }
#endif

  // 清理 GL Fence
  if (ctx.glFence) {
    glDeleteSync(static_cast<GLsync>(ctx.glFence));
    ctx.glFence = nullptr;
  }

  // 清理 CUDA Events（三缓冲）
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (ctx.cudaWriteEvent[i]) {
      cudaEventDestroy(ctx.cudaWriteEvent[i]);
      ctx.cudaWriteEvent[i] = nullptr;
    }
    ctx.pbo_cuda_pending[i] = false;
    ctx.pbo_ready_for_upload[i] = false;
  }

  // 清理三缓冲资源
  for (int i = 0; i < NvdecContext::NUM_PBO_BUFFERS; i++) {
    if (ctx.cuPbo[i]) cuGraphicsUnregisterResource(ctx.cuPbo[i]);
    if (ctx.pbo[i]) glDeleteBuffers(1, &ctx.pbo[i]);
  }
  if (ctx.tex) glDeleteTextures(1, &ctx.tex);
  if (ctx.parser) cuvidDestroyVideoParser(ctx.parser);
  if (ctx.decoder) cuvidDestroyDecoder(ctx.decoder);
  if (ctx.stream) cudaStreamDestroy(ctx.stream);
  if (ctx.cuLock) cuvidCtxLockDestroy(ctx.cuLock);
  if (ctx.cuCtx) cuCtxDestroy(ctx.cuCtx);

#if defined(NVP_HAS_VULKAN)
  printf("[NVDEC] Shutdown. Decoded=%d, Displayed=%d, Dropped=%d, Vulkan=%s\n",
         ctx.frames_decoded.load(), ctx.frames_displayed.load(),
         ctx.frames_dropped.load(), ctx.use_vulkan ? "YES" : "NO");
#else
  printf("[NVDEC] Shutdown. Decoded=%d, Displayed=%d, Dropped=%d\n",
         ctx.frames_decoded.load(), ctx.frames_displayed.load(),
         ctx.frames_dropped.load());
#endif
#else
  (void)ctx;
#endif
}

// Vulkan 初始化
#if defined(NVP_HAS_VULKAN)
bool nvdec_init_vulkan(NvdecContext& ctx, void* vkDevice, void* vkPhysDevice,
                       void* vkQueue, uint32_t queueFamilyIndex) {
#if defined(NVP_HAS_NVDEC)
  VkDevice device = static_cast<VkDevice>(vkDevice);
  VkPhysicalDevice physDevice = static_cast<VkPhysicalDevice>(vkPhysDevice);
  VkQueue queue = static_cast<VkQueue>(vkQueue);

  int w = ctx.width > 0 ? ctx.width : 2560;
  int h = ctx.height > 0 ? ctx.height : 1440;

  if (!vulkan_interop_init(ctx.vkCtx, device, physDevice, queue,
                           queueFamilyIndex, w, h)) {
    fprintf(stderr, "[NVDEC] Vulkan interop init failed\n");
    return false;
  }

  if (!vulkan_create_external_texture(ctx.vkCtx, w, h)) {
    fprintf(stderr, "[NVDEC] Failed to create Vulkan external texture\n");
    return false;
  }

  if (!vulkan_import_to_cuda(ctx.vkCtx, ctx.cuCtx)) {
    fprintf(stderr, "[NVDEC] Failed to import Vulkan texture to CUDA\n");
    return false;
  }

  ctx.vkSurface = ctx.vkCtx.cuSurface;
  ctx.use_vulkan = true;

  printf("[NVDEC] Vulkan interop enabled: %dx%d\n", w, h);
  return true;
#else
  (void)ctx;
  (void)vkDevice;
  (void)vkPhysDevice;
  (void)vkQueue;
  (void)queueFamilyIndex;
  return false;
#endif
}

void* nvdec_get_vulkan_image(const NvdecContext& ctx) {
#if defined(NVP_HAS_NVDEC)
  if (ctx.use_vulkan) {
    return vulkan_get_texture_handle(ctx.vkCtx);
  }
#endif
  (void)ctx;
  return nullptr;
}
#endif  // NVP_HAS_VULKAN
