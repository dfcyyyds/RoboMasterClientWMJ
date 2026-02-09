#!/bin/bash
#===============================================================================
# GPU 真实切换脚本 - 用于低配环境测试
# 
# 功能：
#   - intel    : 切换到 Intel 集显（模拟低配环境，无 CUDA/NVDEC）
#   - nvidia   : 切换到 NVIDIA 独显（高性能模式）
#   - on-demand: 混合模式（默认使用集显，按需使用独显）
#   - status   : 查看当前 GPU 状态
#
# ⚠️  警告：GPU 切换需要重启系统才能生效！
#===============================================================================

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 项目路径
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUP_DIR="$SCRIPT_DIR/.gpu_backup"
BACKUP_FILE="$BACKUP_DIR/gpu_state_before_test.txt"

print_header() {
    echo -e "${CYAN}╔════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║          GPU 真实切换脚本 - 低配测试专用                   ║${NC}"
    echo -e "${CYAN}╚════════════════════════════════════════════════════════════╝${NC}"
    echo ""
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

# 检查是否有 root 权限
check_root() {
    if [[ $EUID -ne 0 ]]; then
        print_error "此脚本需要 root 权限运行"
        echo "请使用: sudo $0 $*"
        exit 1
    fi
}

# 检查 prime-select 是否可用
check_prime_select() {
    if ! command -v prime-select &> /dev/null; then
        print_error "prime-select 未安装"
        echo ""
        echo "请安装 NVIDIA 驱动和 prime-select："
        echo "  sudo apt install nvidia-prime"
        exit 1
    fi
}

# 获取当前 GPU 状态
get_current_status() {
    local current=$(prime-select query 2>/dev/null || echo "unknown")
    echo "$current"
}

# 显示详细状态
show_status() {
    echo -e "${CYAN}═══════════════════ 当前 GPU 状态 ═══════════════════${NC}"
    echo ""
    
    local current=$(get_current_status)
    
    case "$current" in
        nvidia)
            echo -e "  当前模式: ${GREEN}NVIDIA 独显${NC} (高性能)"
            echo -e "  CUDA:     ${GREEN}可用${NC}"
            echo -e "  NVDEC:    ${GREEN}可用${NC}"
            echo -e "  功耗:     ${YELLOW}较高${NC}"
            ;;
        intel)
            echo -e "  当前模式: ${BLUE}Intel 集显${NC} (省电/低配测试)"
            echo -e "  CUDA:     ${RED}不可用${NC}"
            echo -e "  NVDEC:    ${RED}不可用${NC}"
            echo -e "  功耗:     ${GREEN}低${NC}"
            ;;
        on-demand)
            echo -e "  当前模式: ${YELLOW}混合模式${NC} (按需切换)"
            echo -e "  CUDA:     ${YELLOW}按需可用${NC}"
            echo -e "  NVDEC:    ${YELLOW}按需可用${NC}"
            echo -e "  功耗:     ${GREEN}智能${NC}"
            ;;
        *)
            echo -e "  当前模式: ${RED}未知${NC} ($current)"
            ;;
    esac
    
    echo ""
    echo -e "${CYAN}═══════════════════ 硬件信息 ═══════════════════════${NC}"
    echo ""
    lspci | grep -iE "VGA|3D" | while read line; do
        if echo "$line" | grep -qi nvidia; then
            echo -e "  ${GREEN}●${NC} $line"
        elif echo "$line" | grep -qi intel; then
            echo -e "  ${BLUE}●${NC} $line"
        else
            echo -e "  ○ $line"
        fi
    done
    
    echo ""
    
    # 检查 NVIDIA 驱动状态
    if lsmod | grep -q nvidia; then
        echo -e "  NVIDIA 驱动: ${GREEN}已加载${NC}"
        if command -v nvidia-smi &> /dev/null; then
            nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader 2>/dev/null | while read line; do
                echo -e "  GPU 信息:    $line"
            done
        fi
    else
        echo -e "  NVIDIA 驱动: ${RED}未加载${NC}"
    fi
    
    echo ""
    
    # 检查备份状态
    if [[ -f "$BACKUP_FILE" ]]; then
        local backup_mode=$(cat "$BACKUP_FILE")
        echo -e "${CYAN}═══════════════════ 备份信息 ═══════════════════════${NC}"
        echo ""
        echo -e "  已保存的原始模式: ${YELLOW}$backup_mode${NC}"
        echo -e "  恢复命令: ${GREEN}sudo $0 restore${NC}"
        echo ""
    fi
}

# 保存当前状态作为备份
save_backup() {
    mkdir -p "$BACKUP_DIR"
    local current=$(get_current_status)
    echo "$current" > "$BACKUP_FILE"
    print_info "已备份当前 GPU 模式: $current"
}

# 确认操作
confirm_action() {
    local action=$1
    local target=$2
    
    echo ""
    echo -e "${YELLOW}╔════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${YELLOW}║                    ⚠️  重要警告 ⚠️                          ║${NC}"
    echo -e "${YELLOW}╠════════════════════════════════════════════════════════════╣${NC}"
    echo -e "${YELLOW}║  GPU 切换需要重启系统才能生效！                            ║${NC}"
    echo -e "${YELLOW}║                                                            ║${NC}"
    echo -e "${YELLOW}║  请确保：                                                  ║${NC}"
    echo -e "${YELLOW}║  1. 已保存所有工作                                         ║${NC}"
    echo -e "${YELLOW}║  2. 了解切换后的影响                                       ║${NC}"
    echo -e "${YELLOW}║  3. 知道如何恢复（sudo $0 restore）               ║${NC}"
    echo -e "${YELLOW}╚════════════════════════════════════════════════════════════╝${NC}"
    echo ""
    
    case "$target" in
        intel)
            echo "切换到 Intel 模式后："
            echo "  - CUDA 将不可用"
            echo "  - NVDEC 硬件解码将不可用"
            echo "  - 项目将使用 FFmpeg CPU 软解"
            echo "  - 这是测试低配环境的理想设置"
            ;;
        nvidia)
            echo "切换到 NVIDIA 模式后："
            echo "  - CUDA 可用"
            echo "  - NVDEC 硬件解码可用"
            echo "  - 最高性能模式"
            ;;
        on-demand)
            echo "切换到混合模式后："
            echo "  - 默认使用 Intel 集显"
            echo "  - 需要时可使用 NVIDIA 独显"
            echo "  - 需要正确配置应用程序"
            ;;
    esac
    
    echo ""
    read -p "确认切换到 $target 模式？(y/N): " -n 1 -r
    echo ""
    
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        print_info "操作已取消"
        exit 0
    fi
}

# 切换 GPU
switch_gpu() {
    local target=$1
    local current=$(get_current_status)
    
    if [[ "$current" == "$target" ]]; then
        print_info "当前已经是 $target 模式，无需切换"
        return 0
    fi
    
    confirm_action "switch" "$target"
    
    # 如果是从 nvidia 切走，先保存备份
    if [[ "$current" == "nvidia" && ! -f "$BACKUP_FILE" ]]; then
        save_backup
    fi
    
    print_info "正在切换到 $target 模式..."
    
    if prime-select "$target"; then
        print_success "GPU 模式已设置为: $target"
        echo ""
        print_warning "请重启系统以使更改生效！"
        echo ""
        
        read -p "是否现在重启？(y/N): " -n 1 -r
        echo ""
        
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            print_info "系统将在 5 秒后重启..."
            sleep 5
            reboot
        else
            print_info "请稍后手动重启: sudo reboot"
        fi
    else
        print_error "GPU 切换失败"
        exit 1
    fi
}

# 恢复到备份的状态
restore_gpu() {
    if [[ ! -f "$BACKUP_FILE" ]]; then
        print_error "没有找到备份文件"
        echo "可能原因："
        echo "  1. 之前没有进行过 GPU 切换"
        echo "  2. 备份文件被删除"
        echo ""
        echo "可以手动恢复："
        echo "  sudo $0 nvidia"
        exit 1
    fi
    
    local backup_mode=$(cat "$BACKUP_FILE")
    local current=$(get_current_status)
    
    if [[ "$current" == "$backup_mode" ]]; then
        print_info "当前已经是原始模式: $backup_mode"
        rm -f "$BACKUP_FILE"
        return 0
    fi
    
    print_info "准备恢复到原始模式: $backup_mode"
    
    confirm_action "restore" "$backup_mode"
    
    if prime-select "$backup_mode"; then
        print_success "GPU 模式已恢复为: $backup_mode"
        rm -f "$BACKUP_FILE"
        echo ""
        print_warning "请重启系统以使更改生效！"
        echo ""
        
        read -p "是否现在重启？(y/N): " -n 1 -r
        echo ""
        
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            print_info "系统将在 5 秒后重启..."
            sleep 5
            reboot
        else
            print_info "请稍后手动重启: sudo reboot"
        fi
    else
        print_error "GPU 恢复失败"
        exit 1
    fi
}

# 快速测试模式（不重启，仅设置环境变量）
quick_test() {
    echo -e "${CYAN}═══════════════════ 快速测试模式 ═══════════════════${NC}"
    echo ""
    echo "此模式不需要重启，通过环境变量模拟低配环境："
    echo ""
    echo "方法 1: 禁用 NVIDIA 运行时库（推荐）"
    echo -e "  ${GREEN}__NV_PRIME_RENDER_OFFLOAD=0 __GLX_VENDOR_LIBRARY_NAME=mesa unity${NC}"
    echo ""
    echo "方法 2: 使用 primusrun 反向（如果安装了 bumblebee）"
    echo "  optirun -b none unity"
    echo ""
    echo "方法 3: 使用项目的配置覆盖脚本（推荐，无需重启）"
    echo -e "  ${GREEN}$SCRIPT_DIR/switch_gpu_mode.sh low${NC}"
    echo ""
    print_info "快速测试模式只影响应用层，不会真正禁用 NVIDIA GPU"
}

# 显示帮助
show_help() {
    print_header
    echo "用法: sudo $0 <命令>"
    echo ""
    echo "命令:"
    echo "  ${GREEN}intel${NC}      切换到 Intel 集显（低配测试模式）"
    echo "  ${GREEN}nvidia${NC}     切换到 NVIDIA 独显（高性能模式）"
    echo "  ${GREEN}on-demand${NC}  切换到混合模式"
    echo "  ${GREEN}restore${NC}    恢复到切换前的原始模式"
    echo "  ${GREEN}status${NC}     查看当前 GPU 状态"
    echo "  ${GREEN}quick${NC}      显示快速测试方法（无需重启）"
    echo "  ${GREEN}help${NC}       显示此帮助信息"
    echo ""
    echo "典型工作流程："
    echo "  1. 查看状态:     sudo $0 status"
    echo "  2. 切换低配测试: sudo $0 intel"
    echo "  3. 重启系统后测试"
    echo "  4. 恢复原状:     sudo $0 restore"
    echo ""
    echo "⚠️  注意: intel/nvidia/on-demand 切换需要重启系统才能生效"
    echo ""
}

# 主入口
main() {
    case "${1:-help}" in
        intel|Intel|INTEL)
            check_root
            check_prime_select
            print_header
            switch_gpu "intel"
            ;;
        nvidia|Nvidia|NVIDIA)
            check_root
            check_prime_select
            print_header
            switch_gpu "nvidia"
            ;;
        on-demand|hybrid)
            check_root
            check_prime_select
            print_header
            switch_gpu "on-demand"
            ;;
        restore|Restore|RESTORE)
            check_root
            check_prime_select
            print_header
            restore_gpu
            ;;
        status|Status|STATUS)
            check_prime_select
            print_header
            show_status
            ;;
        quick|Quick|QUICK|test)
            print_header
            quick_test
            ;;
        help|--help|-h)
            show_help
            ;;
        *)
            print_error "未知命令: $1"
            echo ""
            show_help
            exit 1
            ;;
    esac
}

main "$@"
