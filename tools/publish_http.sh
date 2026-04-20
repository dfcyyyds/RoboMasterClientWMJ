#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════
# 发布构建产物到 HTTPS 静态站点（manifest + 增量 HTTP 分发）
#
# 用法:
#   ./tools/publish_http.sh [linux|windows|all] [--skip-build] [--message "note"]
#
# 配置（通过环境变量或 tools/.release.env 文件）:
#   RSYNC_TARGET   必填，rsync 目标路径，例:
#                    user@antientropy.xin:/var/www/html/robomaster/
#                    antientropy.xin:/var/www/html/robomaster/
#   RSYNC_SSH_OPTS 可选，例 "-p 22 -i ~/.ssh/id_ed25519"
#
# 生成的远端布局:
#   /var/www/html/robomaster/
#   ├── Linux64/
#   │   ├── manifest.json
#   │   └── files/...  （镜像 Builds/Linux64/ 的目录树）
#   ├── Windows64/
#   │   ├── manifest.json
#   │   └── files/...
#   └── VERSION.txt
#
# manifest.json 格式:
#   { "version": "2026-04-19-abc1234",
#     "build_time": "...", "source_sha": "...", "platform": "Linux64",
#     "files": [ { "path": "relative/path", "size": N, "sha256": "hex", "exec": true }, ... ] }
# ═══════════════════════════════════════════════════════════════════════
set -e

PROJECT_PATH="$(cd "$(dirname "$0")/.." && pwd)"
TARGET="all"
SKIP_BUILD=0
MESSAGE=""

# 加载可选配置文件
if [ -f "$PROJECT_PATH/tools/.release.env" ]; then
    # shellcheck disable=SC1091
    source "$PROJECT_PATH/tools/.release.env"
fi

while [[ $# -gt 0 ]]; do
    case "$1" in
        linux|windows|all) TARGET="$1"; shift ;;
        --skip-build)      SKIP_BUILD=1; shift ;;
        --message)         MESSAGE="$2"; shift 2 ;;
        *) echo "未知参数: $1" >&2; exit 1 ;;
    esac
done

if [ -z "${RSYNC_TARGET:-}" ]; then
    cat >&2 <<EOF
❌ 未配置 RSYNC_TARGET。请在 tools/.release.env 中设置，例:
    RSYNC_TARGET="user@antientropy.xin:/var/www/html/robomaster/"
    # RSYNC_SSH_OPTS="-p 22 -i ~/.ssh/id_ed25519"
EOF
    exit 1
fi

cd "$PROJECT_PATH"
SRC_SHA="$(git rev-parse --short HEAD)"
SRC_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo detached)"
BUILD_TIME="$(date '+%Y-%m-%d %H:%M:%S %z')"
VERSION_TAG="$(date '+%Y%m%d-%H%M%S')-$SRC_SHA"

echo "═══════════════════════════════════════"
echo "  发布 RoboMasterClient (HTTPS)"
echo "  平台: $TARGET   源: $SRC_SHA@$SRC_BRANCH"
echo "  版本: $VERSION_TAG"
echo "  目标: $RSYNC_TARGET"
echo "═══════════════════════════════════════"

# ── 1) 可选构建 ─────────────────────────────
if [ "$SKIP_BUILD" -eq 0 ]; then
    ./build.sh "$TARGET"
else
    echo "▸ 跳过构建（--skip-build）"
fi

# ── 2) 为每个平台生成 manifest.json ─────────
STAGE_DIR="$(mktemp -d)"
trap "rm -rf $STAGE_DIR" EXIT

generate_manifest() {
    local plat="$1"    # Linux64 / Windows64
    local src="$PROJECT_PATH/Builds/$plat"
    if [ ! -d "$src" ]; then
        echo "▸ 跳过 $plat（源目录不存在）"
        return 1
    fi

    echo "▸ 扫描 $plat 生成 manifest..."
    local manifest="$STAGE_DIR/$plat.manifest.json"
    python3 - "$src" "$plat" "$VERSION_TAG" "$BUILD_TIME" "$SRC_SHA" > "$manifest" <<'PY'
import hashlib, json, os, stat, sys

src, platform, version, build_time, source_sha = sys.argv[1:6]
entries = []
for root, dirs, files in os.walk(src):
    # 剔除 Unity 调试符号
    dirs[:] = [d for d in dirs if not d.endswith("_BurstDebugInformation_DoNotShip")]
    for fname in files:
        fp = os.path.join(root, fname)
        rel = os.path.relpath(fp, src).replace(os.sep, "/")
        st = os.stat(fp)
        h = hashlib.sha256()
        with open(fp, "rb") as f:
            for chunk in iter(lambda: f.read(1 << 20), b""):
                h.update(chunk)
        entries.append({
            "path": rel,
            "size": st.st_size,
            "sha256": h.hexdigest(),
            "exec": bool(st.st_mode & stat.S_IXUSR),
        })
entries.sort(key=lambda e: e["path"])
print(json.dumps({
    "version": version,
    "build_time": build_time,
    "source_sha": source_sha,
    "platform": platform,
    "files": entries,
}, indent=2, ensure_ascii=False))
PY

    local count=$(python3 -c "import json; print(len(json.load(open('$manifest'))['files']))")
    local total=$(du -sh "$src" | awk '{print $1}')
    echo "   └─ $count 个文件, 总计 $total"
}

# ── 3) rsync 同步到远端 ────────────────────
sync_to_remote() {
    local plat="$1"
    local src="$PROJECT_PATH/Builds/$plat"
    local manifest="$STAGE_DIR/$plat.manifest.json"
    [ -d "$src" ] || return 0

    # 3a) 上传 files/ （--delete 保证远端与本地一致，仅传改动块）
    echo "▸ 同步 $plat/files/ ..."
    rsync -aH --delete --partial --info=progress2 \
        ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
        --exclude '*_BurstDebugInformation_DoNotShip/' \
        "$src/" \
        "${RSYNC_TARGET%/}/$plat/files/"

    # 3b) 上传 manifest.json（原子替换：先传 .new 再 mv）
    echo "▸ 上传 $plat/manifest.json ..."
    rsync -a --partial \
        ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
        "$manifest" \
        "${RSYNC_TARGET%/}/$plat/manifest.json"
}

CHANGED=()
case "$TARGET" in
    linux)   generate_manifest Linux64  && CHANGED+=(Linux64) ;;
    windows) generate_manifest Windows64 && CHANGED+=(Windows64) ;;
    all)
        generate_manifest Linux64  && CHANGED+=(Linux64)  || true
        generate_manifest Windows64 && CHANGED+=(Windows64) || true
        ;;
esac

if [ ${#CHANGED[@]} -eq 0 ]; then
    echo "❌ 无可用构建产物，退出"
    exit 1
fi

# 写 VERSION.txt
cat > "$STAGE_DIR/VERSION.txt" <<EOF
version:      $VERSION_TAG
build_time:   $BUILD_TIME
source_sha:   $SRC_SHA
source_branch: $SRC_BRANCH
platforms:    ${CHANGED[*]}
note:         ${MESSAGE:-"(none)"}
EOF

for plat in "${CHANGED[@]}"; do
    sync_to_remote "$plat"
done

# 3c) 上传启动器（与平台同目录，便于首次安装时 curl 一键拉取）
echo "▸ 上传启动器 launch.sh / launch.bat ..."
for plat in "${CHANGED[@]}"; do
    case "$plat" in
        Linux64)
            rsync -a --partial ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
                "$PROJECT_PATH/tools/release_launcher/launch.sh" \
                "${RSYNC_TARGET%/}/$plat/launch.sh"
            ;;
        Windows64)
            rsync -a --partial ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
                "$PROJECT_PATH/tools/release_launcher/launch.bat" \
                "${RSYNC_TARGET%/}/$plat/launch.bat"
            ;;
    esac
done

# 上传 VERSION.txt
rsync -a --partial \
    ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
    "$STAGE_DIR/VERSION.txt" \
    "${RSYNC_TARGET%/}/VERSION.txt"

# 上传 Web 安装首页（index.html，与设置面板风格一致的可视化安装页）
if [ -f "$PROJECT_PATH/tools/release_launcher/install.html" ]; then
    echo "▸ 上传 index.html（Web 安装页）..."
    rsync -a --partial \
        ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
        "$PROJECT_PATH/tools/release_launcher/install.html" \
        "${RSYNC_TARGET%/}/index.html"
fi

# 上传 WMJ Logo（用于 Web 安装页与图标）
if [ -f "$PROJECT_PATH/tools/release_launcher/WMJ_LOGO.png" ]; then
    echo "▸ 上传 WMJ_LOGO.png..."
    rsync -a --partial \
        ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
        "$PROJECT_PATH/tools/release_launcher/WMJ_LOGO.png" \
        "${RSYNC_TARGET%/}/WMJ_LOGO.png"
fi

# 上传 download.html（带进度动画 + 相册轮播的可视化下载页）
if [ -f "$PROJECT_PATH/tools/release_launcher/download.html" ]; then
    echo "▸ 上传 download.html（可视化下载页）..."
    rsync -a --partial \
        ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
        "$PROJECT_PATH/tools/release_launcher/download.html" \
        "${RSYNC_TARGET%/}/download.html"
fi

# 上传整包 tar.gz（用于 download.html 浏览器直下，并维护 _latest 软链接）
for plat in Linux64 Windows64; do
    PKG=$(ls -t "$PROJECT_PATH/Builds/RoboMasterClient_${plat}_"*.tar.gz 2>/dev/null | head -1)
    if [ -n "$PKG" ] && [ -f "$PKG" ]; then
        BASENAME=$(basename "$PKG")
        echo "▸ 上传整包: $BASENAME"
        rsync -a --partial --info=progress2 \
            ${RSYNC_SSH_OPTS:+-e "ssh $RSYNC_SSH_OPTS"} \
            "$PKG" \
            "${RSYNC_TARGET%/}/packages/${BASENAME}"
        # 远端创建 _latest 软链接
        if [ -n "$RSYNC_SSH_HOST" ]; then
            ssh ${RSYNC_SSH_OPTS} "$RSYNC_SSH_HOST" \
                "cd $(dirname "${RSYNC_TARGET#*:}")/packages 2>/dev/null && ln -sf '$BASENAME' 'RoboMasterClient_${plat}_latest.tar.gz'" 2>/dev/null || true
        fi
    fi
done

echo ""
echo "✅ 发布完成：$VERSION_TAG"
for plat in "${CHANGED[@]}"; do
    echo "   manifest: https://antientropy.xin/robomaster/$plat/manifest.json"
done
echo "客户端在下次启动 launch.sh/launch.bat 时将自动拉取增量。"
