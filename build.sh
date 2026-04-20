#!/bin/bash
# ═══════════════════════════════════════════════════════
# RoboMasterClient 自动构建脚本
# 用法: ./build.sh [linux|windows|all] [--dev] [--clean]
# ═══════════════════════════════════════════════════════
set -e

UNITY="/home/zby/Unity/Hub/Editor/6000.3.5f1/Editor/Unity"
PROJECT_PATH="$(cd "$(dirname "$0")" && pwd)"
LOG_DIR="$PROJECT_PATH/Logs"
DATE=$(date +%Y%m%d_%H%M%S)
LOG_FILE="$LOG_DIR/build_${DATE}.log"

# 解析参数
TARGET="${1:-all}"
EXTRA_ARGS=""
shift || true
for arg in "$@"; do
    case "$arg" in
        --dev)   EXTRA_ARGS="$EXTRA_ARGS -development" ;;
        --clean) EXTRA_ARGS="$EXTRA_ARGS -cleanBuild" ;;
    esac
done

mkdir -p "$LOG_DIR"

echo "═══════════════════════════════════════"
echo "RoboMasterClient 自动构建"
echo "  平台:   $TARGET"
echo "  日志:   $LOG_FILE"
echo "  额外参数: ${EXTRA_ARGS:-无}"
echo "═══════════════════════════════════════"

# 检查 Unity Editor 是否正在运行项目
if pgrep -af "Unity.*-projectpath.*$PROJECT_PATH" | grep -v pgrep > /dev/null 2>&1; then
    echo "⚠️  检测到 Unity Editor 正在运行此项目（PID: $(pgrep -f "Unity.*-projectpath.*$PROJECT_PATH" | head -1)）"
    echo "   batchmode 构建需要先关闭 Unity Editor。"
    read -p "是否自动关闭 Unity Editor 并继续？[y/N] " ans
    if [[ "$ans" =~ ^[Yy] ]]; then
        pkill -f "Unity.*-projectpath.*$PROJECT_PATH" || true
        echo "等待 Unity 关闭..."
        sleep 5
    else
        echo "已取消构建。"
        exit 1
    fi
fi

"$UNITY" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -executeMethod BuildScript.CommandLineBuild \
    -customTarget "$TARGET" \
    $EXTRA_ARGS \
    -logFile "$LOG_FILE" \
    -quit &
UNITY_PID=$!

# ═══════════════════════════════════════════════════════
# 进度条 — 基于 Unity 构建日志阶段关键字推断进度
# ═══════════════════════════════════════════════════════
# 每个阶段对应的目标进度百分比（关键字优先级：上方覆盖下方）
# 关键字来自 Unity Editor batchmode 输出典型序列
render_bar() {
    local pct=$1 label=$2
    local width=40
    local filled=$(( pct * width / 100 ))
    local empty=$(( width - filled ))
    local bar=""
    local i
    for ((i=0; i<filled; i++)); do bar+="█"; done
    for ((i=0; i<empty;  i++)); do bar+="░"; done
    # 重绘一行（\r + 清到行尾）
    printf "\r\033[K  [%s] %3d%% %s" "$bar" "$pct" "$label"
}

# 根据日志内容推断当前阶段，返回 "pct|label"
infer_progress() {
    local log="$1"
    # 由晚到早匹配（优先最后阶段）
    if grep -q "所有构建成功完成\|构建存在失败项"            "$log" 2>/dev/null; then echo "100|完成"; return; fi
    if grep -q "Windows64.*✅.*已打包\|Windows64 完成"       "$log" 2>/dev/null; then echo "95|打包 Windows 产物"; return; fi
    if grep -q "构建 StandaloneWindows64"                     "$log" 2>/dev/null; then echo "75|构建 Windows64 Player"; return; fi
    if grep -q "Linux64.*✅.*已打包\|Linux64 完成"           "$log" 2>/dev/null; then echo "60|打包 Linux 产物"; return; fi
    if grep -q "构建 StandaloneLinux64"                       "$log" 2>/dev/null; then echo "40|构建 Linux64 Player"; return; fi
    if grep -q "Reloading assemblies\|命令行构建:"           "$log" 2>/dev/null; then echo "25|加载脚本程序集"; return; fi
    if grep -q "Refresh completed\|Hotreload"                 "$log" 2>/dev/null; then echo "18|资源刷新完成"; return; fi
    if grep -q "Compilation finished\|CompileScripts:"        "$log" 2>/dev/null; then echo "15|脚本编译完成"; return; fi
    if grep -q "Begin MonoManager ReloadAssembly\|Start importing" "$log" 2>/dev/null; then echo "10|导入资源"; return; fi
    if [ -s "$log" ]; then echo "5|Unity 启动中"; return; fi
    echo "1|等待 Unity 启动"
}

echo ""
LAST_PCT=-1
SPIN_CHARS=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
SPIN_IDX=0
START_TIME=$(date +%s)

while kill -0 "$UNITY_PID" 2>/dev/null; do
    res=$(infer_progress "$LOG_FILE")
    pct="${res%%|*}"
    label="${res#*|}"
    elapsed=$(( $(date +%s) - START_TIME ))
    spinner="${SPIN_CHARS[$SPIN_IDX]}"
    SPIN_IDX=$(( (SPIN_IDX + 1) % 10 ))
    render_bar "$pct" "$spinner $label  (${elapsed}s)"
    LAST_PCT=$pct
    sleep 0.5
done

# 禁用 set -e 以便正确捕获 Unity 进程退出码（非 0 时不要立即终止脚本）
set +e
wait "$UNITY_PID"
EXIT_CODE=$?
set -e

# 构建完成后的终态进度条
if [ $EXIT_CODE -eq 0 ]; then
    render_bar 100 "✅ 构建成功 (共 $(( $(date +%s) - START_TIME ))s)"
else
    render_bar 100 "❌ 构建失败 exit=$EXIT_CODE"
fi
echo ""
echo ""

if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ 构建成功！"
    echo "构建产物："
    ls -lh "$PROJECT_PATH/Builds/"*.tar.gz 2>/dev/null | tail -5
else
    echo "❌ 构建失败 (exit code: $EXIT_CODE)"
    echo "查看日志: tail -50 $LOG_FILE"
    tail -30 "$LOG_FILE" 2>/dev/null
fi

exit $EXIT_CODE
