#!/usr/bin/env bash
set -euo pipefail

# 快速诊断和启动脚本
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== RoboMaster 仿真环境诊断 ==="
echo ""

# 1. 检查模拟服务器状态
echo "📡 检查模拟服务器..."
if pgrep -f "mock_server" > /dev/null; then
    echo "✅ 模拟服务器已运行 (PID: $(pgrep -f mock_server))"
else
    echo "❌ 模拟服务器未运行"
    MOCK_NOT_RUNNING=1
fi

# 2. 检查MQTT broker
echo ""
echo "📬 检查MQTT broker..."
if pgrep -f "mosquitto.*mosquitto_mock.conf" > /dev/null; then
    echo "✅ MQTT broker已运行 (PID: $(pgrep -f 'mosquitto.*mosquitto_mock.conf'))"
else
    echo "⚠️  MQTT broker未运行"
    MQTT_NOT_RUNNING=1
fi

# 3. 检查端口占用
echo ""
echo "🔌 检查端口占用..."
if ss -tuln | grep -q ":3333 "; then
    echo "✅ TCP 3333 (MQTT) 已监听"
else
    echo "⚠️  TCP 3333 未监听"
fi

if ss -tuln | grep -q ":3334 "; then
    echo "⚠️  UDP 3334 已被占用（可能是Unity客户端）"
else
    echo "ℹ️  UDP 3334 未占用"
fi

# 4. 检查可执行文件
echo ""
echo "🔨 检查构建产物..."
if [[ -x "$SCRIPT_DIR/build/mock_server" ]]; then
    BUILD_DATE=$(stat -c %y "$SCRIPT_DIR/build/mock_server" 2>/dev/null | cut -d'.' -f1)
    echo "✅ mock_server 可执行文件存在 (构建时间: $BUILD_DATE)"
else
    echo "❌ mock_server 可执行文件不存在或无执行权限"
    NEEDS_BUILD=1
fi

# 5. 检查视频文件
echo ""
echo "🎥 检查测试视频..."
if [[ -f "$SCRIPT_DIR/single_20260125_1534.avi" ]]; then
    VIDEO_SIZE=$(du -h "$SCRIPT_DIR/single_20260125_1534.avi" | cut -f1)
    echo "✅ 测试视频存在 (大小: $VIDEO_SIZE)"
    USE_VIDEO="$SCRIPT_DIR/single_20260125_1534.avi"
else
    echo "ℹ️  测试视频不存在，将使用lavfi测试源"
    USE_VIDEO="lavfi:testsrc=size=640x360:rate=30"
fi

# 6. 检查NativeVideoPlugin
echo ""
echo "🎮 检查Unity插件..."
PLUGIN_PATH="/home/zby/RoboMasterClientWMJ/Assets/Plugins/x86_64/libNativeVideoPlugin.so"
if [[ -f "$PLUGIN_PATH" ]]; then
    PLUGIN_SIZE=$(stat -c%s "$PLUGIN_PATH")
    echo "✅ NativeVideoPlugin 存在 (大小: $PLUGIN_SIZE bytes)"
    if ldd "$PLUGIN_PATH" 2>&1 | grep -q "not found"; then
        echo "⚠️  插件依赖缺失:"
        ldd "$PLUGIN_PATH" | grep "not found" || true
    fi
else
    echo "❌ NativeVideoPlugin 不存在"
fi

echo ""
echo "==================================="
echo ""

# 决定是否需要启动
if [[ -n "${MOCK_NOT_RUNNING:-}" ]]; then
    echo "🚀 正在启动模拟服务器..."
    echo ""
    
    # 检查是否需要重新构建
    if [[ -n "${NEEDS_BUILD:-}" ]]; then
        echo "🔨 构建mock_server..."
        cd "$SCRIPT_DIR"
        mkdir -p build
        cd build
        cmake .. -DCMAKE_BUILD_TYPE=Release
        make -j$(nproc)
        cd ..
    fi
    
    # 启动参数
    START_ARGS=(
        --host 127.0.0.1
        --port 3333
        --udp-ip 127.0.0.1
        --udp-port 3334
        --codec hevc
        --gop 15
        --interval 1000
    )
    
    # 选择视频源
    if [[ "$USE_VIDEO" == lavfi* ]]; then
        START_ARGS+=(--video "$USE_VIDEO")
    else
        START_ARGS+=(--video "$USE_VIDEO")
    fi
    
    echo "启动命令: ./start_mock.sh ${START_ARGS[@]}"
    echo ""
    
    # 使用start_mock.sh启动（它会处理MQTT和后台运行）
    ./start_mock.sh "${START_ARGS[@]}"
    
    echo ""
    echo "✅ 启动完成！等待2秒让服务稳定..."
    sleep 2
    
    # 验证启动
    if pgrep -f "mock_server" > /dev/null; then
        echo "✅ 模拟服务器已成功启动"
        echo ""
        echo "📊 实时日志查看:"
        echo "   tail -f run/server.log"
        echo ""
        echo "🛑 停止服务器:"
        echo "   ./stop_mock.sh"
    else
        echo "❌ 启动失败，请检查 run/server.log"
        exit 1
    fi
else
    echo "✅ 所有服务已运行，无需启动"
    echo ""
    echo "📊 查看服务器日志:"
    echo "   tail -f run/server.log"
fi

echo ""
echo "==================================="
echo "🎯 Unity客户端应配置为:"
echo "   - MQTT: 127.0.0.1:3333"
echo "   - UDP:  127.0.0.1:3334"
echo "==================================="
