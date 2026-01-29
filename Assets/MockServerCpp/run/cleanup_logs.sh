#!/bin/bash
# 清理 MockServer 运行时文件和日志
# 使用方法: ./cleanup_logs.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== 清理 MockServer 运行时文件 ==="

# 清空日志文件（保留文件本身）
if [ -f "$SCRIPT_DIR/mock_server.log" ]; then
    truncate -s 0 "$SCRIPT_DIR/mock_server.log"
    echo "✓ 已清空 mock_server.log"
fi

if [ -f "$SCRIPT_DIR/mosquitto.log" ]; then
    truncate -s 0 "$SCRIPT_DIR/mosquitto.log"
    echo "✓ 已清空 mosquitto.log"
fi

if [ -f "$SCRIPT_DIR/gui.log" ]; then
    truncate -s 0 "$SCRIPT_DIR/gui.log"
    echo "✓ 已清空 gui.log"
fi

# 删除临时配置文件（如果存在）
rm -f "$SCRIPT_DIR/pids.env" "$SCRIPT_DIR/pids.json" "$SCRIPT_DIR/mosquitto_mock.conf"

echo "=== 清理完成 ==="
du -sh "$SCRIPT_DIR"
