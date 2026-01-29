cmake_minimum_required(VERSION 3.22)
project(NativeVideoPlugin LANGUAGES CXX CUDA)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CUDA_STANDARD 17)
set(CMAKE_CUDA_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

option(NVP_ENABLE_NVDEC "Enable NVDEC/CUDA path" ON)
option(NVP_ENABLE_VULKAN "Enable Vulkan interop path" ON)

find_package(OpenGL REQUIRED)
find_package(CUDAToolkit QUIET)
find_package(Vulkan QUIET)

# Try to locate nvcuvid
if(NVP_ENABLE_NVDEC AND CUDAToolkit_FOUND)
	find_library(NVCUVID_LIB nvcuvid
		PATHS
			/usr/lib/x86_64-linux-gnu
			/usr/local/lib
			/usr/local/cuda/lib64
			${CUDAToolkit_LIBRARY_ROOT}/lib64
	)
endif()

add_library(NativeVideoPlugin SHARED
	NativeVideoPlugin.cpp
	NvdecStub.cpp
	UdpFrameAssembler.cpp
	UdpFrameAssembler.h
	NV12ToRGB.cu
	VulkanInterop.cpp
	VulkanInterop.h
)

target_include_directories(NativeVideoPlugin
	PRIVATE
		${CMAKE_CURRENT_SOURCE_DIR}
		${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_definitions(NativeVideoPlugin
	PRIVATE
		NVP_STUB_BUILD
)

if(NVP_ENABLE_NVDEC AND CUDAToolkit_FOUND AND NVCUVID_LIB)
	target_compile_definitions(NativeVideoPlugin PRIVATE NVP_HAS_NVDEC=1)
	target_include_directories(NativeVideoPlugin PRIVATE ${CUDAToolkit_INCLUDE_DIRS})
	target_link_libraries(NativeVideoPlugin PRIVATE
		${NVCUVID_LIB}
		CUDA::cuda_driver
		CUDA::cudart
		OpenGL::GL
	)
	set_target_properties(NativeVideoPlugin PROPERTIES
		CUDA_SEPARABLE_COMPILATION ON
		CUDA_RESOLVE_DEVICE_SYMBOLS ON
	)
	message(STATUS "NVDEC enabled with nvcuvid: ${NVCUVID_LIB}")
	
	# Vulkan 互操作支持
	if(NVP_ENABLE_VULKAN AND Vulkan_FOUND)
		target_compile_definitions(NativeVideoPlugin PRIVATE NVP_HAS_VULKAN=1)
		target_include_directories(NativeVideoPlugin PRIVATE ${Vulkan_INCLUDE_DIRS})
		target_link_libraries(NativeVideoPlugin PRIVATE Vulkan::Vulkan)
		message(STATUS "Vulkan interop enabled: ${Vulkan_LIBRARY}")
	else()
		message(WARNING "Vulkan interop disabled - Vulkan not found")
	endif()
else()
	target_link_libraries(NativeVideoPlugin PRIVATE OpenGL::GL)
	message(WARNING "NVDEC disabled - missing CUDA or nvcuvid")
endif()

if(UNIX AND NOT APPLE)
	target_link_libraries(NativeVideoPlugin PRIVATE dl pthread)
endif()

