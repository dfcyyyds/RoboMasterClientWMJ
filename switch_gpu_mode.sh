#!/bin/bash
#===============================================================================
# GPU 模式切换脚本
# 用于模拟不同硬件配置环境，测试低配优化效果
#
# 使用方法:
#   ./switch_gpu_mode.sh [模式]
#
# 模式:
#   high    - 高配模式 (NVIDIA GPU, 1080p 120fps)
#   mid     - 中配模式 (模拟 NVIDIA 低端 / Intel Iris)
#   low     - 低配模式 (模拟 Intel UHD 集显)
#   real-intel - 真实切换到 Intel 集显 (需要重启应用)
#   real-nvidia - 真实切换回 NVIDIA (需要重启应用)
#   status  - 显示当前状态
#   help    - 显示帮助
#
# 作者: RoboMaster Client WMJ
# 日期: 2026-02-09
#===============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_DIR="$SCRIPT_DIR/Assets/StreamingAssets/Config"
OVERRIDE_FILE="$CONFIG_DIR/hardware_override.json"
UNITY_EXECUTABLE="$SCRIPT_DIR/RoboMasterClientWMJ"

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo -e "${CYAN}"
    echo "╔═══════════════════════════════════════════════════════════╗"
    echo "║          GPU 模式切换工具 - 硬件配置模拟器                ║"
    echo "╚═══════════════════════════════════════════════════════════╝"
    echo -e "${NC}"
}

print_status() {
    echo -e "${GREEN}[✓]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[!]${NC} $1"
}

print_error() {
    echo -e "${RED}[✗]${NC} $1"
}

print_info() {
    echo -e "${BLUE}[i]${NC} $1"
}

# 显示帮助
show_help() {
    print_header
    echo "使用方法: $0 [模式]"
    echo ""
    echo "=== 安全模式 (推荐，无需切换真实 GPU) ==="
    echo ""
    echo "  high      高配模式 - 使用 NVIDIA GPU 全部性能"
    echo "            分辨率: 1920x1080, 帧率: 120fps, 队列: 8"
    echo ""
    echo "  mid       中配模式 - 模拟中端硬件"
    echo "            分辨率: 1920x1080, 帧率: 60fps, 队列: 6"
    echo ""
    echo "  low       低配模式 - 模拟 Intel i3 + UHD 集显"
    echo "            分辨率: 1280x720, 帧率: 30fps, 队列: 4"
    echo ""
    echo "  auto      自动模式 - 清除覆盖，使用真实硬件检测"
    echo ""
    echo "=== 系统模式 (真实切换 GPU，可能影响稳定性) ==="
    echo ""
    echo "  real-intel    切换到 Intel 集成显卡"
    echo "  real-nvidia   切换回 NVIDIA 独立显卡"
    echo ""
    echo "=== 其他命令 ==="
    echo ""
    echo "  status    显示当前配置状态"
    echo "  test      测试 Unity 能否在当前配置下启动"
    echo "  help      显示此帮助信息"
    echo ""
    echo "=== 示例 ==="
    echo ""
    echo "  $0 low              # 模拟低配环境"
    echo "  $0 status           # 查看当前状态"
    echo "  $0 auto             # 恢复自动检测"
    echo ""
}

# 创建硬件覆盖配置文件
create_override() {
    local level="$1"
    local accel="$2"
    local width="$3"
    local height="$4"
    local fps="$5"
    local queue="$6"
    local drain="$7"
    local gpu_name="$8"
    
    mkdir -p "$CONFIG_DIR"
    
    cat > "$OVERRIDE_FILE" << EOF
{
    "_comment": "硬件配置覆盖文件 - 由 switch_gpu_mode.sh 生成",
    "_generated": "$(date '+%Y-%m-%d %H:%M:%S')",
    "enabled": true,
    "forceLevel": "$level",
    "forceAccel": "$accel",
    "simulatedGpuName": "$gpu_name",
    "simulatedGpuVendor": "$(echo $level | grep -qi 'nvidia' && echo 'NVIDIA' || echo 'Intel')",
    "simulatedVramMB": $([ "$level" = "High" ] && echo 8192 || ([ "$level" = "Mid" ] && echo 4096 || echo 1024)),
    "simulatedCpuCores": $([ "$level" = "High" ] && echo 16 || ([ "$level" = "Mid" ] && echo 8 || echo 4)),
    "recommendedWidth": $width,
    "recommendedHeight": $height,
    "recommendedFps": $fps,
    "recommendedQueueSize": $queue,
    "recommendedDrainPerUpdate": $drain
}
EOF
    
    print_status "已创建硬件覆盖配置: $OVERRIDE_FILE"
}

# 设置高配模式
set_high_mode() {
    print_info "切换到高配模式 (NVIDIA 全性能)"
    create_override "High" "NvdecCuda" 1920 1080 120 8 3 "NVIDIA GeForce RTX 3080"
    
    echo ""
    echo "┌─────────────────────────────────────────┐"
    echo "│  高配模式配置                          │"
    echo "├─────────────────────────────────────────┤"
    echo "│  分辨率:     1920 x 1080               │"
    echo "│  目标帧率:   120 fps                   │"
    echo "│  解码队列:   8                         │"
    echo "│  硬件加速:   NVDEC/CUDA                │"
    echo "│  模拟 GPU:   NVIDIA GeForce RTX 3080   │"
    echo "└─────────────────────────────────────────┘"
    echo ""
    print_status "重新启动 Unity 后生效"
}

# 设置中配模式
set_mid_mode() {
    print_info "切换到中配模式 (模拟中端硬件)"
    create_override "Mid" "NvdecCuda" 1920 1080 60 6 2 "NVIDIA GeForce GTX 1650"
    
    echo ""
    echo "┌─────────────────────────────────────────┐"
    echo "│  中配模式配置                          │"
    echo "├─────────────────────────────────────────┤"
    echo "│  分辨率:     1920 x 1080               │"
    echo "│  目标帧率:   60 fps                    │"
    echo "│  解码队列:   6                         │"
    echo "│  硬件加速:   NVDEC/CUDA                │"
    echo "│  模拟 GPU:   NVIDIA GeForce GTX 1650   │"
    echo "└─────────────────────────────────────────┘"
    echo ""
    print_status "重新启动 Unity 后生效"
}

# 设置低配模式
set_low_mode() {
    print_info "切换到低配模式 (模拟 Intel i3 + UHD 集显)"
    create_override "Low" "Vaapi" 1280 720 30 4 1 "Intel UHD Graphics 630"
    
    echo ""
    echo "┌─────────────────────────────────────────┐"
    echo "│  低配模式配置                          │"
    echo "├─────────────────────────────────────────┤"
    echo "│  分辨率:     1280 x 720                │"
    echo "│  目标帧率:   30 fps                    │"
    echo "│  解码队列:   4                         │"
    echo "│  硬件加速:   VAAPI (模拟)              │"
    echo "│  模拟 GPU:   Intel UHD Graphics 630    │"
    echo "│  模拟 CPU:   4 核心                    │"
    echo "└─────────────────────────────────────────┘"
    echo ""
    print_warning "注意: 此模式下仍使用 NVIDIA GPU 进行渲染"
    print_warning "      但应用层会按照低配参数运行"
    print_status "重新启动 Unity 后生效"
}

# 恢复自动检测
set_auto_mode() {
    print_info "恢复自动硬件检测模式"
    
    if [ -f "$OVERRIDE_FILE" ]; then
        rm -f "$OVERRIDE_FILE"
        print_status "已删除硬件覆盖配置"
    else
        print_info "未发现覆盖配置，已处于自动模式"
    fi
    
    echo ""
    print_status "下次启动将使用真实硬件检测结果"
}

# 真实切换到 Intel 集显
switch_to_intel() {
    print_warning "准备切换到 Intel 集成显卡..."
    echo ""
    
    # 检查是否有 Intel GPU
    if ! lspci | grep -i "VGA.*Intel" > /dev/null 2>&1; then
        print_error "未检测到 Intel 集成显卡！"
        echo "  你的系统可能没有集成显卡，或者已被 BIOS 禁用"
        exit 1
    fi
    
    print_info "检测到 Intel GPU: $(lspci | grep -i 'VGA.*Intel' | cut -d: -f3)"
    echo ""
    
    # 创建启动脚本
    cat > "$SCRIPT_DIR/run_intel_mode.sh" << 'EOF'
#!/bin/bash
# Intel 集显模式启动脚本
# 通过环境变量强制使用 Mesa/Intel 驱动

export DRI_PRIME=0
export __GLX_VENDOR_LIBRARY_NAME=mesa
export __NV_PRIME_RENDER_OFFLOAD=0
export __VK_LAYER_NV_optimus=INTEL
export MESA_LOADER_DRIVER_OVERRIDE=iris
export LIBGL_ALWAYS_SOFTWARE=0

echo "[Intel 模式] 当前 GPU 信息:"
glxinfo 2>/dev/null | grep -E "OpenGL vendor|OpenGL renderer|OpenGL version" || echo "无法获取 OpenGL 信息"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "[Intel 模式] 启动 Unity..."
./RoboMasterClientWMJ -force-opengl "$@"
EOF
    
    chmod +x "$SCRIPT_DIR/run_intel_mode.sh"
    
    echo "┌─────────────────────────────────────────────────────────────┐"
    echo "│  Intel 集显模式已配置                                      │"
    echo "├─────────────────────────────────────────────────────────────┤"
    echo "│  启动命令: ./run_intel_mode.sh                             │"
    echo "│                                                             │"
    echo "│  该脚本会:                                                  │"
    echo "│    - 设置 DRI_PRIME=0 (使用第一个 GPU)                     │"
    echo "│    - 设置 __GLX_VENDOR_LIBRARY_NAME=mesa                   │"
    echo "│    - 禁用 NVIDIA Prime Offload                             │"
    echo "│    - 使用 Intel Iris 驱动                                  │"
    echo "└─────────────────────────────────────────────────────────────┘"
    echo ""
    print_warning "注意: 这会真正使用 Intel GPU，性能会大幅下降"
    print_warning "如果 Unity 无法启动，请运行: $0 real-nvidia"
    print_status "使用 ./run_intel_mode.sh 启动 Unity"
}

# 真实切换回 NVIDIA
switch_to_nvidia() {
    print_info "切换回 NVIDIA 独立显卡..."
    
    # 创建 NVIDIA 启动脚本
    cat > "$SCRIPT_DIR/run_nvidia_mode.sh" << 'EOF'
#!/bin/bash
# NVIDIA 独显模式启动脚本

export __NV_PRIME_RENDER_OFFLOAD=1
export __GLX_VENDOR_LIBRARY_NAME=nvidia
export __VK_LAYER_NV_optimus=NVIDIA_only

echo "[NVIDIA 模式] 当前 GPU 信息:"
glxinfo 2>/dev/null | grep -E "OpenGL vendor|OpenGL renderer|OpenGL version" || echo "无法获取 OpenGL 信息"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "[NVIDIA 模式] 启动 Unity..."
./RoboMasterClientWMJ -force-opengl "$@"
EOF
    
    chmod +x "$SCRIPT_DIR/run_nvidia_mode.sh"
    
    # 删除 Intel 模式的低配覆盖
    rm -f "$OVERRIDE_FILE"
    
    echo "┌─────────────────────────────────────────────────────────────┐"
    echo "│  NVIDIA 独显模式已配置                                     │"
    echo "├─────────────────────────────────────────────────────────────┤"
    echo "│  启动命令: ./run_nvidia_mode.sh 或 ./RoboMasterClientWMJ   │"
    echo "└─────────────────────────────────────────────────────────────┘"
    echo ""
    print_status "已恢复 NVIDIA 模式"
}

# 显示当前状态
show_status() {
    print_header
    
    echo "=== 系统 GPU 信息 ==="
    echo ""
    
    # 检测所有 GPU
    echo "检测到的 GPU:"
    lspci | grep -E "VGA|3D" | while read line; do
        echo "  • $line"
    done
    echo ""
    
    # 当前 OpenGL 渲染器
    echo "当前 OpenGL 渲染器:"
    if command -v glxinfo > /dev/null 2>&1; then
        glxinfo 2>/dev/null | grep -E "OpenGL vendor|OpenGL renderer|OpenGL version" | sed 's/^/  /'
    else
        echo "  (需要安装 mesa-utils: sudo apt install mesa-utils)"
    fi
    echo ""
    
    # NVIDIA 驱动状态
    echo "NVIDIA 驱动状态:"
    if command -v nvidia-smi > /dev/null 2>&1; then
        nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader 2>/dev/null | sed 's/^/  /'
    else
        echo "  NVIDIA 驱动未加载或未安装"
    fi
    echo ""
    
    echo "=== 应用配置状态 ==="
    echo ""
    
    # 检查硬件覆盖配置
    if [ -f "$OVERRIDE_FILE" ]; then
        print_warning "检测到硬件覆盖配置 (非自动模式)"
        echo ""
        echo "覆盖配置内容:"
        cat "$OVERRIDE_FILE" | grep -v "^{" | grep -v "^}" | grep -v "_comment" | grep -v "_generated" | sed 's/^/  /'
    else
        print_status "当前为自动硬件检测模式"
    fi
    echo ""
    
    # 检查配置文件
    echo "可用配置文件:"
    for cfg in "$CONFIG_DIR"/params*.json; do
        if [ -f "$cfg" ]; then
            basename "$cfg" | sed 's/^/  • /'
        fi
    done
    echo ""
}

# 测试 Unity 启动
test_unity() {
    print_info "测试 Unity 启动..."
    echo ""
    
    if [ ! -f "$UNITY_EXECUTABLE" ]; then
        print_warning "未找到 Unity 可执行文件: $UNITY_EXECUTABLE"
        print_info "请先构建项目"
        return 1
    fi
    
    # 尝试启动并等待几秒
    print_info "启动 Unity (5秒后自动关闭)..."
    timeout 5 "$UNITY_EXECUTABLE" -force-opengl -logFile /dev/stdout 2>&1 | head -50 &
    pid=$!
    
    sleep 3
    
    if kill -0 $pid 2>/dev/null; then
        print_status "Unity 启动成功!"
        kill $pid 2>/dev/null || true
    else
        print_error "Unity 启动失败或已退出"
        return 1
    fi
}

# 主函数
main() {
    case "${1:-help}" in
        high)
            print_header
            set_high_mode
            ;;
        mid)
            print_header
            set_mid_mode
            ;;
        low)
            print_header
            set_low_mode
            ;;
        auto)
            print_header
            set_auto_mode
            ;;
        real-intel|intel)
            print_header
            switch_to_intel
            ;;
        real-nvidia|nvidia)
            print_header
            switch_to_nvidia
            ;;
        status)
            show_status
            ;;
        test)
            print_header
            test_unity
            ;;
        help|--help|-h)
            show_help
            ;;
        *)
            print_error "未知模式: $1"
            echo ""
            show_help
            exit 1
            ;;
    esac
}

main "$@"
