#!/usr/bin/env bash
set -euo pipefail

# 简易诊断脚本：聚合客户端日志中的关键入场与错误信号
# 使用：在仓库根目录执行：
#   bash Assets/Diagnostics/investigate.sh

ROOT_DIR=$(cd "$(dirname "$0")/../.." && pwd)
cd "$ROOT_DIR"

TS=$(date -Iseconds)
echo "[Investigate] Start at $TS"

# 目标关键词
PATTERN_DIAG='缓存参数集|IDR前重发参数集|Codec=|解码帧'
PATTERN_ERR='SPS|PPS|VPS|invalid|Invalid|NAL'

# 汇总关键诊断
echo "\n[Investigate] --- Diagnostics (Codec/ParamSets/IDR/Decoded) ---"
if [ -f Log/RunLog.txt ]; then
  grep -E "$PATTERN_DIAG" Log/RunLog.txt | tail -n 50 || true
fi
if [ -f Log/DebugLog.txt ]; then
  grep -E "$PATTERN_DIAG" Log/DebugLog.txt | tail -n 50 || true
fi

# 汇总错误关键词（stderr等）
echo "\n[Investigate] --- ffmpeg stderr / errors ---"
if [ -f Log/RunLog.txt ]; then
  grep -E "$PATTERN_ERR" Log/RunLog.txt | tail -n 50 || true
fi
if [ -f Log/DebugLog.txt ]; then
  grep -E "$PATTERN_ERR" Log/DebugLog.txt | tail -n 50 || true
fi

# 概要统计：最近的每秒统计行
echo "\n[Investigate] --- Recent per-second stats ---"
if [ -f Log/RunLog.txt ]; then
  grep -E "每秒统计" Log/RunLog.txt | tail -n 10 || true
fi
if [ -f Log/DebugLog.txt ]; then
  grep -E "每秒统计" Log/DebugLog.txt | tail -n 10 || true
fi

echo "\n[Investigate] Done."