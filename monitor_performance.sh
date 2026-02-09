#!/bin/bash
#===============================================================================
# 性能监控脚本 - 用于采集 Unity 项目运行时的详细性能数据
#
# 采集数据:
#   - Intel GPU 占用率、频率、功耗
#   - CPU 使用率
#   - 内存使用情况
#   - 系统功耗
#   - 视频解码相关进程
#
# 输出: 测试报告/ 目录下的 CSV 和 TXT 文件
#===============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/测试报告"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
OUTPUT_PREFIX="$OUTPUT_DIR/性能采集_$TIMESTAMP"

# 采样间隔（秒）
INTERVAL=1
# 采样次数（0=无限）
COUNT=${1:-30}

mkdir -p "$OUTPUT_DIR"

echo "==============================================="
echo "  性能监控脚本"
echo "==============================================="
echo ""
echo "采样间隔: ${INTERVAL} 秒"
echo "采样次数: ${COUNT} 次"
echo "输出目录: $OUTPUT_DIR"
echo ""

# 检查工具
check_tools() {
    local missing=""
    command -v intel_gpu_top &>/dev/null || missing="$missing intel-gpu-tools"
    
    if [[ -n "$missing" ]]; then
        echo "[错误] 缺少工具:$missing"
        echo "请运行: sudo apt install$missing"
        exit 1
    fi
}

# 获取 CPU 信息
get_cpu_info() {
    # CPU 使用率
    local cpu_usage=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)
    # CPU 温度
    local cpu_temp=""
    if [[ -f /sys/class/thermal/thermal_zone0/temp ]]; then
        cpu_temp=$(echo "scale=1; $(cat /sys/class/thermal/thermal_zone0/temp) / 1000" | bc)
    fi
    echo "$cpu_usage,$cpu_temp"
}

# 获取内存信息
get_mem_info() {
    free -m | awk '/Mem:/ {printf "%.1f,%.1f,%.1f", $3, $2, $3/$2*100}'
}

# 检查 FFmpeg 进程
get_ffmpeg_info() {
    local ffmpeg_count=$(pgrep -c ffmpeg 2>/dev/null || echo "0")
    local ffmpeg_cpu=""
    if [[ "$ffmpeg_count" -gt 0 ]]; then
        ffmpeg_cpu=$(ps -C ffmpeg -o %cpu= 2>/dev/null | awk '{sum+=$1} END {print sum}' || echo "0")
    else
        ffmpeg_cpu="0"
    fi
    echo "$ffmpeg_count,$ffmpeg_cpu"
}

# 获取 Unity 进程信息
get_unity_info() {
    local unity_pid=$(pgrep -f "Unity" | head -1 2>/dev/null || echo "")
    if [[ -n "$unity_pid" ]]; then
        local unity_cpu=$(ps -p $unity_pid -o %cpu= 2>/dev/null | tr -d ' ' || echo "0")
        local unity_mem=$(ps -p $unity_pid -o %mem= 2>/dev/null | tr -d ' ' || echo "0")
        echo "$unity_pid,$unity_cpu,$unity_mem"
    else
        echo "N/A,0,0"
    fi
}

# 主采集函数
collect_data() {
    local gpu_csv="$OUTPUT_PREFIX"_gpu.csv
    local sys_csv="$OUTPUT_PREFIX"_system.csv
    local summary_txt="$OUTPUT_PREFIX"_summary.txt
    
    # 写入 CSV 头
    echo "timestamp,gpu_freq_mhz,gpu_power_w,gpu_render_pct,gpu_video_pct,gpu_rc6_pct" > "$gpu_csv"
    echo "timestamp,cpu_usage_pct,cpu_temp_c,mem_used_mb,mem_total_mb,mem_pct,ffmpeg_count,ffmpeg_cpu,unity_pid,unity_cpu,unity_mem" > "$sys_csv"
    
    echo "开始采集... (按 Ctrl+C 停止)"
    echo ""
    
    local sample=0
    local gpu_power_sum=0
    local gpu_render_sum=0
    local cpu_usage_sum=0
    
    # 使用临时文件获取 intel_gpu_top 输出
    local gpu_tmp=$(mktemp)
    
    while [[ $COUNT -eq 0 || $sample -lt $COUNT ]]; do
        local ts=$(date +%H:%M:%S)
        
        # 采集 Intel GPU 数据（单次采样）
        sudo timeout 1 intel_gpu_top -l -s 500 2>/dev/null | head -3 | tail -1 > "$gpu_tmp" || true
        
        local gpu_line=$(cat "$gpu_tmp" 2>/dev/null || echo "")
        local gpu_freq="N/A"
        local gpu_power="N/A"
        local gpu_render="N/A"
        local gpu_video="N/A"
        local gpu_rc6="N/A"
        
        if [[ -n "$gpu_line" && ! "$gpu_line" =~ ^[[:space:]]*$ && ! "$gpu_line" =~ "req" ]]; then
            gpu_freq=$(echo "$gpu_line" | awk '{print $2}')
            gpu_power=$(echo "$gpu_line" | awk '{print $6}')
            gpu_render=$(echo "$gpu_line" | awk '{print $7}')
            gpu_rc6=$(echo "$gpu_line" | awk '{print $5}')
            # VCS (Video) 在第13列
            gpu_video=$(echo "$gpu_line" | awk '{print $13}')
        fi
        
        # 采集系统数据
        local cpu_info=$(get_cpu_info)
        local mem_info=$(get_mem_info)
        local ffmpeg_info=$(get_ffmpeg_info)
        local unity_info=$(get_unity_info)
        
        # 写入 CSV
        echo "$ts,$gpu_freq,$gpu_power,$gpu_render,$gpu_video,$gpu_rc6" >> "$gpu_csv"
        echo "$ts,$cpu_info,$mem_info,$ffmpeg_info,$unity_info" >> "$sys_csv"
        
        # 累计统计
        if [[ "$gpu_power" != "N/A" ]]; then
            gpu_power_sum=$(echo "$gpu_power_sum + $gpu_power" | bc 2>/dev/null || echo "$gpu_power_sum")
        fi
        if [[ "$gpu_render" != "N/A" ]]; then
            gpu_render_sum=$(echo "$gpu_render_sum + $gpu_render" | bc 2>/dev/null || echo "$gpu_render_sum")
        fi
        local cpu_pct=$(echo "$cpu_info" | cut -d',' -f1)
        if [[ -n "$cpu_pct" ]]; then
            cpu_usage_sum=$(echo "$cpu_usage_sum + $cpu_pct" | bc 2>/dev/null || echo "$cpu_usage_sum")
        fi
        
        # 实时输出
        printf "\r[%s] GPU: %s MHz, %.2fW, Render: %s%% | CPU: %s%% | FFmpeg: %s    " \
            "$ts" "$gpu_freq" "$gpu_power" "$gpu_render" "$(echo "$cpu_info" | cut -d',' -f1)" \
            "$(echo "$ffmpeg_info" | cut -d',' -f1)"
        
        sample=$((sample + 1))
        sleep $INTERVAL
    done
    
    rm -f "$gpu_tmp"
    
    echo ""
    echo ""
    echo "==============================================="
    echo "  采集完成"
    echo "==============================================="
    
    # 生成摘要
    local avg_power=$(echo "scale=2; $gpu_power_sum / $sample" | bc 2>/dev/null || echo "N/A")
    local avg_render=$(echo "scale=2; $gpu_render_sum / $sample" | bc 2>/dev/null || echo "N/A")
    local avg_cpu=$(echo "scale=2; $cpu_usage_sum / $sample" | bc 2>/dev/null || echo "N/A")
    
    cat > "$summary_txt" << EOF
===============================================
  性能采集摘要
===============================================

采集时间: $(date)
采样次数: $sample
采样间隔: ${INTERVAL}s

--- GPU 统计 ---
平均功耗:     ${avg_power} W
平均渲染占用: ${avg_render} %

--- CPU 统计 ---
平均 CPU 占用: ${avg_cpu} %

--- 输出文件 ---
GPU 数据:    $gpu_csv
系统数据:    $sys_csv
摘要:        $summary_txt
EOF

    cat "$summary_txt"
    echo ""
    echo "数据已保存到: $OUTPUT_DIR"
}

# 快速状态检查
quick_status() {
    echo "=== 当前系统状态 ==="
    echo ""
    
    echo "--- GPU 模式 ---"
    prime-select query 2>/dev/null || echo "prime-select 不可用"
    echo ""
    
    echo "--- Intel GPU 实时状态 ---"
    sudo intel_gpu_top -l -s 1000 2>/dev/null | head -5
    echo ""
    
    echo "--- CPU 状态 ---"
    top -bn1 | head -5
    echo ""
    
    echo "--- 内存状态 ---"
    free -h
    echo ""
    
    echo "--- 相关进程 ---"
    ps aux | grep -E "Unity|ffmpeg|vaapi" | grep -v grep || echo "无相关进程运行"
}

# 主入口
main() {
    case "${1:-}" in
        status|s)
            quick_status
            ;;
        help|--help|-h)
            echo "用法: $0 [命令|采样次数]"
            echo ""
            echo "命令:"
            echo "  status    快速查看当前状态"
            echo "  help      显示帮助"
            echo "  [数字]    采样指定次数（默认 30 次）"
            echo "  0         持续采样直到 Ctrl+C"
            echo ""
            echo "示例:"
            echo "  $0 60      采集 60 秒数据"
            echo "  $0 0       持续采集"
            echo "  $0 status  查看当前状态"
            ;;
        *)
            check_tools
            collect_data
            ;;
    esac
}

main "$@"
