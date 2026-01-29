#!/bin/bash
set -e

# Build Native Video Plugin
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"
INSTALL_DIR="$SCRIPT_DIR/../x86_64"

echo "=== Building Native Video Plugin ==="
echo "Build dir: $BUILD_DIR"
echo "Install dir: $INSTALL_DIR"

# Create build directory
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

# Configure with CMake
cmake .. \
    -DCMAKE_BUILD_TYPE=Release \
    -DNVP_ENABLE_NVDEC=ON

# Build
cmake --build . --config Release -j$(nproc)

# Install
mkdir -p "$INSTALL_DIR"
cp libNativeVideoPlugin.so "$INSTALL_DIR/"

# 清理 build 目录下的 so，避免 Unity 误识别重复插件
rm -f "$BUILD_DIR/libNativeVideoPlugin.so"

echo "=== Build Complete ==="
echo "Plugin installed to: $INSTALL_DIR/libNativeVideoPlugin.so"
