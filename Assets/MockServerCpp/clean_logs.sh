#!/usr/bin/env bash
set -euo pipefail

# 可根据需要追加更多日志路径
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOG_DIR="$ROOT_DIR/Log"
TARGETS=(
  "$LOG_DIR/RunLog.txt"
  "$LOG_DIR/DebugLog.txt"
)

for f in "${TARGETS[@]}"; do
  if [ -f "$f" ]; then
    : > "$f"
    echo "cleared: $f"
  else
    echo "skip (not found): $f"
  fi
done

# 可选：清空 Logs 目录下的 .log 文件（默认注释，需时可放开）
# find "$ROOT_DIR/Logs" -maxdepth 1 -type f -name '*.log' -print -exec truncate -s 0 {} +
