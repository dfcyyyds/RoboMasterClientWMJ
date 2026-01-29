// Compatibility wrapper for FFmpeg nv-codec-headers
// Maps dynlink_nvcuvid.h to standard nvcuvid.h interface

#pragma once

// Include the FFmpeg dynamic linking headers
#include "dynlink_cuda.h"
#include "dynlink_cuviddec.h"
#include "dynlink_loader.h"
#include "dynlink_nvcuvid.h"

// FFmpeg headers define function pointer types with 't' prefix (e.g.,
// tcuvidCreateVideoParser) We need to declare the actual function pointers and
// provide wrapper macros

#ifdef __cplusplus
extern "C" {
#endif

// Declare external function pointers (will be loaded by dynlink_loader)
extern tcuvidCreateVideoParser* cuvidCreateVideoParser;
extern tcuvidParseVideoData* cuvidParseVideoData;
extern tcuvidDestroyVideoParser* cuvidDestroyVideoParser;
extern tcuvidCreateDecoder* cuvidCreateDecoder;
extern tcuvidDecodePicture* cuvidDecodePicture;
extern tcuvidMapVideoFrame* cuvidMapVideoFrame;
extern tcuvidUnmapVideoFrame* cuvidUnmapVideoFrame;
extern tcuvidDestroyDecoder* cuvidDestroyDecoder;
extern tcuvidCtxLockCreate* cuvidCtxLockCreate;
extern tcuvidCtxLockDestroy* cuvidCtxLockDestroy;
extern tcuvidGetDecoderCaps* cuvidGetDecoderCaps;

#ifdef __cplusplus
}
#endif
