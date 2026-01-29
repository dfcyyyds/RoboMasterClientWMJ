# Native NVDEC + OpenGL 4.5 Integration Plan (Ubuntu 24.04, NVIDIA GPU)

## Goals
- Decode HEVC/H264 AnnexB via NVDEC (Video Codec SDK) using CUDA fixed-function decode.
- Zero-copy path: UDP → native receive/assemble → NVDEC decode (NV12) → CUDA color convert → PBO/GL texture → Unity samples texture ID via native plugin event.
- Preserve C# ffmpeg pipeline as fallback.

## Architecture
1) **UDP Ingestion (Native)**
   - `recvmsg` on AF_INET/AF_INET6, buffer from fixed pool (ArrayPool equivalent in C++ via preallocated slabs).
   - Slice header: frameId (u16 LE), sliceId (u16 LE), frameLen (u32 LE), payload = AnnexB slice.
   - Frame ring buffer keyed by frameId; per-frame ordered slices map; timeout & overflow eviction; drop-old policy to keep latency low.
   - Parameter sets cache (VPS/SPS/PPS). IDR arrival triggers resend of parameter sets before decode.

2) **Decode (NVDEC/CUVID)**
   - Create CUcontext + CUvideoctxlock + CUvideoparser + CUvideodecoder.
   - Parser callbacks: on sequence → (re)init decoder; on picture → enqueue for decode; on display → map decoded surface.
   - Support HEVC first; allow H264 if detected.

3) **Color Convert & Upload**
   - Map NVDEC output (NV12). Use CUDA kernel or NPP: NV12 → RGB8.
   - CUDA-OpenGL interop: register PBO/texture with CUDA; write RGB into PBO; glTexSubImage to resident texture.
   - Double/triple buffering; store latest texture id for Unity fetch.

4) **Unity Bridge**
   - Exports (C): `nvp_init(width,height)`, `nvp_push_udp(ptr,len)`, `nvp_get_latest_texture()`, `nvp_shutdown()`.
   - C# (`NativeVideoBridge`): P/Invoke the exports; `GL.IssuePluginEvent` or `CustomTextureUpdateV2` to bind latest texture.
   - Fallback: if plugin load fails, remain on ffmpeg pipeline.

5) **Logging & Diagnostics**
   - High-frequency paths only keep counters; per-second aggregated log.
   - Optional verbose flag to dump parser/decoder state snapshots.

## Build (Draft)
- Dependencies: CUDA Toolkit (with Video Codec SDK headers), OpenGL dev headers, CMake ≥ 3.22.
- Target: `NativeVideoPlugin` shared library in `Assets/Plugins/NativeVideoPlugin/`.
- Later CMake additions: find CUDA, link nvcuvid/nvcuda, link GL and dl/pthread.

## Tasks (Next)
- Implement native UDP receive + frame ring buffer.
- Wire CUVID parser/decoder; push AnnexB; parameter set resend before IDR.
- CUDA kernel for NV12→RGB; CUDA-GL interop to PBO/texture; expose texture id.
- Unity side: command buffer / plugin event to blit native texture to material; expose toggle to select native vs ffmpeg path.
- Add build notes to CHANGES.md and finalize CMake with CUDA/GL linkage once dependencies confirmed.
