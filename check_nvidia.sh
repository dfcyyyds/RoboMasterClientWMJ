#!/bin/bash
# ============================================================================
# NVIDIA 环境自检脚本
# 检查本计算机是否完全应用 NVIDIA GPU 进行图形/计算任务
# ============================================================================

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}       NVIDIA 环境自检脚本${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# 记录问题
ISSUES=()

# ----------------------------------------------------------------------------
# 1. 检测 NVIDIA GPU 是否存在
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[1/7] 检测 NVIDIA GPU 硬件...${NC}"

if lspci | grep -i nvidia > /dev/null 2>&1; then
    GPU_INFO=$(lspci | grep -i nvidia | head -1)
    echo -e "  ${GREEN}✓ 检测到 NVIDIA GPU: ${GPU_INFO}${NC}"
else
    echo -e "  ${RED}✗ 未检测到 NVIDIA GPU 硬件${NC}"
    ISSUES+=("未检测到 NVIDIA GPU 硬件（可能是纯 Intel/AMD 集显）")
fi
echo ""

# ----------------------------------------------------------------------------
# 2. 检测 NVIDIA 驱动是否加载
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[2/7] 检测 NVIDIA 驱动...${NC}"

if lsmod | grep -q "^nvidia "; then
    DRIVER_VER=$(cat /sys/module/nvidia/version 2>/dev/null || echo "未知")
    echo -e "  ${GREEN}✓ NVIDIA 驱动已加载，版本: ${DRIVER_VER}${NC}"
else
    echo -e "  ${RED}✗ NVIDIA 驱动未加载${NC}"
    ISSUES+=("NVIDIA 内核驱动未加载（nvidia.ko）")
fi
echo ""

# ----------------------------------------------------------------------------
# 3. 检测 nvidia-smi 工具
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[3/7] 检测 nvidia-smi 工具...${NC}"

if command -v nvidia-smi > /dev/null 2>&1; then
    if nvidia-smi > /dev/null 2>&1; then
        echo -e "  ${GREEN}✓ nvidia-smi 可用${NC}"
        echo ""
        echo -e "  ${BLUE}GPU 状态:${NC}"
        nvidia-smi --query-gpu=name,driver_version,memory.total,memory.used,utilization.gpu --format=csv,noheader 2>/dev/null | while read line; do
            echo -e "    $line"
        done
    else
        echo -e "  ${RED}✗ nvidia-smi 命令失败${NC}"
        ISSUES+=("nvidia-smi 命令执行失败（驱动可能损坏）")
    fi
else
    echo -e "  ${RED}✗ nvidia-smi 未安装${NC}"
    ISSUES+=("nvidia-smi 工具未安装")
fi
echo ""

# ----------------------------------------------------------------------------
# 4. 检测 CUDA 环境
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[4/7] 检测 CUDA 环境...${NC}"

CUDA_FOUND=false

# 检查 nvcc
if command -v nvcc > /dev/null 2>&1; then
    NVCC_VER=$(nvcc --version | grep "release" | awk '{print $5}' | tr -d ',')
    echo -e "  ${GREEN}✓ CUDA Toolkit (nvcc): ${NVCC_VER}${NC}"
    CUDA_FOUND=true
else
    echo -e "  ${YELLOW}⚠ nvcc 未在 PATH 中（CUDA Toolkit 可能未安装或未配置）${NC}"
fi

# 检查 CUDA 运行时库
if ldconfig -p 2>/dev/null | grep -q "libcudart"; then
    CUDART_PATH=$(ldconfig -p | grep libcudart | head -1 | awk '{print $NF}')
    echo -e "  ${GREEN}✓ CUDA 运行时库: ${CUDART_PATH}${NC}"
    CUDA_FOUND=true
elif [ -f "/usr/local/cuda/lib64/libcudart.so" ]; then
    echo -e "  ${GREEN}✓ CUDA 运行时库: /usr/local/cuda/lib64/libcudart.so${NC}"
    CUDA_FOUND=true
else
    echo -e "  ${YELLOW}⚠ CUDA 运行时库未找到${NC}"
fi

if [ "$CUDA_FOUND" = false ]; then
    ISSUES+=("CUDA 环境未配置（nvcc 和 libcudart 都未找到）")
fi
echo ""

# ----------------------------------------------------------------------------
# 5. 检测当前 X11/Wayland 使用的 GPU
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[5/7] 检测显示服务器使用的 GPU...${NC}"

# 检查 X11
if [ -n "$DISPLAY" ]; then
    if command -v glxinfo > /dev/null 2>&1; then
        GL_RENDERER=$(glxinfo 2>/dev/null | grep "OpenGL renderer" | cut -d':' -f2 | xargs)
        GL_VENDOR=$(glxinfo 2>/dev/null | grep "OpenGL vendor" | cut -d':' -f2 | xargs)
        
        echo -e "  OpenGL 渲染器: ${GL_RENDERER}"
        echo -e "  OpenGL 厂商:   ${GL_VENDOR}"
        
        if echo "$GL_VENDOR" | grep -iq "nvidia"; then
            echo -e "  ${GREEN}✓ 显示服务器正在使用 NVIDIA GPU${NC}"
        else
            echo -e "  ${RED}✗ 显示服务器未使用 NVIDIA GPU${NC}"
            ISSUES+=("显示服务器使用的是: ${GL_VENDOR} ${GL_RENDERER}（非 NVIDIA）")
        fi
    else
        echo -e "  ${YELLOW}⚠ glxinfo 未安装，无法检测 OpenGL 渲染器${NC}"
        ISSUES+=("glxinfo 未安装，无法确认显示 GPU")
    fi
else
    echo -e "  ${YELLOW}⚠ 未检测到 DISPLAY 环境变量（可能是无头服务器）${NC}"
fi
echo ""

# ----------------------------------------------------------------------------
# 6. 检测 Vulkan 支持
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[6/7] 检测 Vulkan 环境...${NC}"

if command -v vulkaninfo > /dev/null 2>&1; then
    VK_DEVICES=$(vulkaninfo 2>/dev/null | grep "deviceName" | head -5)
    if [ -n "$VK_DEVICES" ]; then
        echo -e "  ${BLUE}Vulkan 设备:${NC}"
        echo "$VK_DEVICES" | while read line; do
            echo -e "    $line"
        done
        
        if echo "$VK_DEVICES" | grep -iq "nvidia"; then
            echo -e "  ${GREEN}✓ Vulkan 支持 NVIDIA GPU${NC}"
        else
            echo -e "  ${YELLOW}⚠ Vulkan 设备中未发现 NVIDIA${NC}"
            ISSUES+=("Vulkan 未检测到 NVIDIA 设备")
        fi
    else
        echo -e "  ${RED}✗ 未检测到 Vulkan 设备${NC}"
        ISSUES+=("Vulkan 环境异常，未检测到任何设备")
    fi
else
    echo -e "  ${YELLOW}⚠ vulkaninfo 未安装${NC}"
fi
echo ""

# ----------------------------------------------------------------------------
# 7. 检测 ffmpeg 硬件加速支持
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[7/7] 检测 ffmpeg 硬件加速...${NC}"

if command -v ffmpeg > /dev/null 2>&1; then
    echo -e "  ${BLUE}ffmpeg 支持的硬件加速:${NC}"
    
    # NVDEC/CUVID
    if ffmpeg -hwaccels 2>/dev/null | grep -q "cuda"; then
        echo -e "    ${GREEN}✓ cuda (NVDEC)${NC}"
    else
        echo -e "    ${RED}✗ cuda (NVDEC) - 未支持${NC}"
        ISSUES+=("ffmpeg 未编译 CUDA/NVDEC 支持")
    fi
    
    # NVENC (编码)
    if ffmpeg -encoders 2>/dev/null | grep -q "nvenc"; then
        echo -e "    ${GREEN}✓ nvenc (编码)${NC}"
    else
        echo -e "    ${YELLOW}⚠ nvenc (编码) - 未支持${NC}"
    fi
    
    # VAAPI (Intel/AMD)
    if ffmpeg -hwaccels 2>/dev/null | grep -q "vaapi"; then
        echo -e "    ${GREEN}✓ vaapi (Intel/AMD 备用)${NC}"
    else
        echo -e "    ${YELLOW}⚠ vaapi - 未支持${NC}"
    fi
    
    # Vulkan
    if ffmpeg -hwaccels 2>/dev/null | grep -q "vulkan"; then
        echo -e "    ${GREEN}✓ vulkan${NC}"
    else
        echo -e "    ${YELLOW}⚠ vulkan - 未支持${NC}"
    fi
else
    echo -e "  ${RED}✗ ffmpeg 未安装${NC}"
    ISSUES+=("ffmpeg 未安装")
fi
echo ""

# ----------------------------------------------------------------------------
# 8. 检测 NVIDIA 相关进程
# ----------------------------------------------------------------------------
echo -e "${YELLOW}[附加] 当前使用 NVIDIA GPU 的进程:${NC}"
if command -v nvidia-smi > /dev/null 2>&1; then
    nvidia-smi --query-compute-apps=pid,process_name,used_memory --format=csv,noheader 2>/dev/null | head -10 | while read line; do
        if [ -n "$line" ]; then
            echo -e "  $line"
        fi
    done
    PROC_COUNT=$(nvidia-smi --query-compute-apps=pid --format=csv,noheader 2>/dev/null | wc -l)
    if [ "$PROC_COUNT" -eq 0 ]; then
        echo -e "  ${YELLOW}(当前无进程使用 GPU 计算)${NC}"
    fi
fi
echo ""

# ----------------------------------------------------------------------------
# 汇总报告
# ----------------------------------------------------------------------------
echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}               检测结果汇总${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

if [ ${#ISSUES[@]} -eq 0 ]; then
    echo -e "${GREEN}✓ 所有检测通过！本机已完全应用 NVIDIA GPU。${NC}"
else
    echo -e "${RED}✗ 发现 ${#ISSUES[@]} 个问题，以下组件未使用 NVIDIA:${NC}"
    echo ""
    for i in "${!ISSUES[@]}"; do
        echo -e "  ${RED}$((i+1)). ${ISSUES[$i]}${NC}"
    done
    echo ""
    echo -e "${YELLOW}建议操作:${NC}"
    echo -e "  - 确认已安装 NVIDIA 专有驱动"
    echo -e "  - 检查 prime-select 或 optimus 设置"
    echo -e "  - 确认 CUDA Toolkit 已正确安装"
    echo -e "  - 运行: sudo ubuntu-drivers autoinstall"
fi
echo ""
