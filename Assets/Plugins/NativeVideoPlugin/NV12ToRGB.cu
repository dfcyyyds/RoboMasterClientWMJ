// CUDA kernel: NV12 to RGB conversion
#include <cuda_runtime.h>

#include <cstdint>

__global__ void NV12ToRGBKernel(const uint8_t* __restrict__ nv12_y,
                                const uint8_t* __restrict__ nv12_uv,
                                uint8_t* __restrict__ rgb, int width,
                                int height, int y_pitch, int uv_pitch) {
  int x = blockIdx.x * blockDim.x + threadIdx.x;
  int y = blockIdx.y * blockDim.y + threadIdx.y;

  if (x >= width || y >= height) return;

  // Sample Y
  int y_val = nv12_y[y * y_pitch + x];

  // Sample UV (subsampled 2x2)
  int uv_x = (x / 2) * 2;
  int uv_y = y / 2;
  int uv_idx = uv_y * uv_pitch + uv_x;
  int u_val = nv12_uv[uv_idx] - 128;
  int v_val = nv12_uv[uv_idx + 1] - 128;

  // BT.709 YUV to RGB conversion
  int c = y_val - 16;
  int r = (298 * c + 409 * v_val + 128) >> 8;
  int g = (298 * c - 100 * u_val - 208 * v_val + 128) >> 8;
  int b = (298 * c + 516 * u_val + 128) >> 8;

  r = max(0, min(255, r));
  g = max(0, min(255, g));
  b = max(0, min(255, b));

  int rgb_idx = (y * width + x) * 3;
  rgb[rgb_idx] = static_cast<uint8_t>(r);
  rgb[rgb_idx + 1] = static_cast<uint8_t>(g);
  rgb[rgb_idx + 2] = static_cast<uint8_t>(b);
}

// NV12 转 RGBA 并写入 Vulkan surface (零拷贝)
__global__ void NV12ToSurfaceKernel(const uint8_t* __restrict__ nv12_y,
                                    const uint8_t* __restrict__ nv12_uv,
                                    cudaSurfaceObject_t surface, int width,
                                    int height, int y_pitch, int uv_pitch) {
  int x = blockIdx.x * blockDim.x + threadIdx.x;
  int y = blockIdx.y * blockDim.y + threadIdx.y;

  if (x >= width || y >= height) return;

  // Sample Y
  int y_val = nv12_y[y * y_pitch + x];

  // Sample UV (subsampled 2x2)
  int uv_x = (x / 2) * 2;
  int uv_y = y / 2;
  int uv_idx = uv_y * uv_pitch + uv_x;
  int u_val = nv12_uv[uv_idx] - 128;
  int v_val = nv12_uv[uv_idx + 1] - 128;

  // BT.709 YUV to RGB conversion
  int c = y_val - 16;
  int r = (298 * c + 409 * v_val + 128) >> 8;
  int g = (298 * c - 100 * u_val - 208 * v_val + 128) >> 8;
  int b = (298 * c + 516 * u_val + 128) >> 8;

  r = max(0, min(255, r));
  g = max(0, min(255, g));
  b = max(0, min(255, b));

  // 写入 RGBA 到 surface (Vulkan 纹理)
  uchar4 pixel = make_uchar4(r, g, b, 255);
  // 使用 surf2Dwrite 写入 surface object
  surf2Dwrite(pixel, surface, x * sizeof(uchar4), y, cudaBoundaryModeZero);
}

extern "C" cudaError_t LaunchNV12ToRGB(const uint8_t* nv12_y,
                                       const uint8_t* nv12_uv, uint8_t* rgb,
                                       int width, int height, int y_pitch,
                                       int uv_pitch, cudaStream_t stream) {
  dim3 block(16, 16);
  dim3 grid((width + block.x - 1) / block.x, (height + block.y - 1) / block.y);

  NV12ToRGBKernel<<<grid, block, 0, stream>>>(nv12_y, nv12_uv, rgb, width,
                                              height, y_pitch, uv_pitch);

  return cudaGetLastError();
}

// 新增: NV12 转 Vulkan Surface (零拷贝路径)
extern "C" cudaError_t LaunchNV12ToSurface(const uint8_t* nv12_y,
                                           const uint8_t* nv12_uv,
                                           cudaSurfaceObject_t surface,
                                           int width, int height, int y_pitch,
                                           int uv_pitch, cudaStream_t stream) {
  dim3 block(16, 16);
  dim3 grid((width + block.x - 1) / block.x, (height + block.y - 1) / block.y);

  NV12ToSurfaceKernel<<<grid, block, 0, stream>>>(
      nv12_y, nv12_uv, surface, width, height, y_pitch, uv_pitch);

  return cudaGetLastError();
}
