#!/bin/bash
# ═══════════════════════════════════════════════════════
# 下载 ffmpeg 静态构建并放入 StreamingAssets
# 用法: ./tools/download_ffmpeg.sh [linux|win|all]
#
# 来源:
#   - Linux x64: https://johnvansickle.com/ffmpeg/ (静态构建, ~80MB → 解压 ~100MB)
#   - Windows x64: https://github.com/BtbN/FFmpeg-Builds (gpl-shared 静态, ~40MB)
# ═══════════════════════════════════════════════════════
set -e

PROJECT_PATH="$(cd "$(dirname "$0")/.." && pwd)"
DEST_ROOT="$PROJECT_PATH/Assets/StreamingAssets/ffmpeg"
TARGET="${1:-all}"

mkdir -p "$DEST_ROOT/linux64" "$DEST_ROOT/win64"

WORK_DIR="$(mktemp -d)"
trap "rm -rf $WORK_DIR" EXIT

download_linux() {
    echo "═══ 下载 Linux x64 ffmpeg (静态构建) ═══"
    local url="https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz"
    local archive="$WORK_DIR/ffmpeg-linux.tar.xz"

    if [ -f "$DEST_ROOT/linux64/ffmpeg" ]; then
        echo "  已存在 $DEST_ROOT/linux64/ffmpeg，跳过下载（删除后重跑可强制更新）"
        return
    fi

    echo "  从 $url 下载..."
    curl -fL --progress-bar -o "$archive" "$url"

    echo "  解压..."
    tar -xJf "$archive" -C "$WORK_DIR"
    local extracted=$(find "$WORK_DIR" -maxdepth 1 -type d -name "ffmpeg-*-amd64-static" | head -1)
    if [ -z "$extracted" ]; then
        echo "  ❌ 解压后未找到 ffmpeg 目录" >&2
        exit 1
    fi

    cp "$extracted/ffmpeg" "$DEST_ROOT/linux64/ffmpeg"
    chmod +x "$DEST_ROOT/linux64/ffmpeg"

    local size=$(du -h "$DEST_ROOT/linux64/ffmpeg" | awk '{print $1}')
    echo "  ✅ 完成: $DEST_ROOT/linux64/ffmpeg ($size)"
}

download_windows() {
    echo "═══ 下载 Windows x64 ffmpeg (gyan.dev release-essentials 精简构建) ═══"
    # essentials 构建仅含常用编解码器（H.264/HEVC/AAC 等），体积约 45MB 压缩 / ~160MB 解压；
    # 相比 BtbN gpl-full（194MB）节省约 30MB 分发包体。hevc/h264 解码足够用。
    local url="https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    local archive="$WORK_DIR/ffmpeg-win.zip"

    if [ -f "$DEST_ROOT/win64/ffmpeg.exe" ]; then
        echo "  已存在 $DEST_ROOT/win64/ffmpeg.exe，跳过下载（删除后重跑可强制更新）"
        return
    fi

    echo "  从 $url 下载..."
    curl -fL --progress-bar -o "$archive" "$url"

    echo "  解压..."
    if ! command -v unzip >/dev/null 2>&1; then
        echo "  ❌ 未安装 unzip，请先: sudo apt install unzip" >&2
        exit 1
    fi
    unzip -q "$archive" -d "$WORK_DIR"

    local exe=$(find "$WORK_DIR" -type f -name "ffmpeg.exe" | head -1)
    if [ -z "$exe" ]; then
        echo "  ❌ 解压后未找到 ffmpeg.exe" >&2
        exit 1
    fi
    cp "$exe" "$DEST_ROOT/win64/ffmpeg.exe"

    local size=$(du -h "$DEST_ROOT/win64/ffmpeg.exe" | awk '{print $1}')
    echo "  ✅ 完成: $DEST_ROOT/win64/ffmpeg.exe ($size)"
}

case "$TARGET" in
    linux)   download_linux ;;
    win|windows) download_windows ;;
    all|*)   download_linux; download_windows ;;
esac

echo ""
echo "═══ 完成 ═══"
echo "目录树:"
ls -lh "$DEST_ROOT"/linux64 "$DEST_ROOT"/win64 2>/dev/null || true
echo ""
echo "下次构建时 Unity 会自动把这些文件复制到 *_Data/StreamingAssets/ffmpeg/"
echo "客户端启动时 FfmpegLocator 会优先使用内置版本，用户无需安装系统 ffmpeg。"
