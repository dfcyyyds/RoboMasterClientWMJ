#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════
# RoboMasterClient Linux 启动器 —— 基于 manifest.json 的增量 HTTPS 更新
#
# 安装目录布局:
#   <INSTALL>/launch.sh            本脚本
#   <INSTALL>/manifest.local.json  上次更新成功后留下的 manifest 副本
#   <INSTALL>/RoboMasterClient     主程序
#   <INSTALL>/RoboMasterClient_Data/...
#   ... （与 Builds/Linux64/ 结构一致）
#
# 首次安装:
#   mkdir -p ~/RoboMasterClient && cd ~/RoboMasterClient
#   curl -sSL https://antientropy.xin/robomaster/Linux64/launch.sh -o launch.sh
#   chmod +x launch.sh && ./launch.sh
#
# 环境变量:
#   ROBOMASTER_UPDATE_URL     manifest 基址, 默认 https://antientropy.xin/robomaster/Linux64/
#   ROBOMASTER_SKIP_UPDATE=1  跳过更新（离线/赛场）
# ═══════════════════════════════════════════════════════════════════════
set -e
cd "$(dirname "$(readlink -f "$0")")"

INSTALL_DIR="$(pwd)"
UPDATE_URL="${ROBOMASTER_UPDATE_URL:-https://antientropy.xin/robomaster/Linux64/}"
UPDATE_URL="${UPDATE_URL%/}/"
MANIFEST_URL="${UPDATE_URL}manifest.json"
FILES_BASE_URL="${UPDATE_URL}files/"
LOCAL_MANIFEST="$INSTALL_DIR/manifest.local.json"
CLIENT_BIN="$INSTALL_DIR/RoboMasterClient"

log() { echo "[launcher] $*"; }

log "═══ RoboMasterClient Launcher (Linux) ═══"
log "安装目录: $INSTALL_DIR"
log "更新源:   $UPDATE_URL"

for cmd in curl python3 sha256sum; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        log "❌ 缺少命令: $cmd （Ubuntu/Debian: sudo apt install $cmd）"
        exit 1
    fi
done

goto_launch=""
if [ "${ROBOMASTER_SKIP_UPDATE:-0}" = "1" ]; then
    log "⚠ ROBOMASTER_SKIP_UPDATE=1，跳过更新"
    goto_launch=1
fi

if [ -z "$goto_launch" ]; then
    TMP_MANIFEST="$(mktemp)"
    trap "rm -f $TMP_MANIFEST" EXIT

    log "▸ 拉取 manifest..."
    if ! curl -sSfL --max-time 15 "$MANIFEST_URL" -o "$TMP_MANIFEST"; then
        log "⚠ 无法获取 manifest（离线或服务异常），沿用本地版本启动"
    else
        python3 - "$INSTALL_DIR" "$TMP_MANIFEST" "$LOCAL_MANIFEST" "$FILES_BASE_URL" <<'PY' || log "⚠ 更新异常 (rc=$?)，尝试启动现有版本"
import hashlib, json, os, shutil, subprocess, sys, urllib.parse

install_dir, remote_manifest_path, local_manifest_path, files_base = sys.argv[1:5]

with open(remote_manifest_path, "r", encoding="utf-8") as f:
    remote = json.load(f)
remote_files = {e["path"]: e for e in remote["files"]}

local_cache = {}
if os.path.isfile(local_manifest_path):
    try:
        with open(local_manifest_path, "r", encoding="utf-8") as f:
            for e in json.load(f)["files"]:
                local_cache[e["path"]] = e
    except Exception:
        pass

def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

need_download, total_bytes = [], 0
for rel, entry in remote_files.items():
    abs_path = os.path.join(install_dir, rel)
    if os.path.isfile(abs_path):
        st = os.stat(abs_path)
        cached = local_cache.get(rel)
        if cached and cached.get("size") == st.st_size and cached.get("sha256") == entry["sha256"]:
            continue
        if st.st_size == entry["size"] and sha256_file(abs_path) == entry["sha256"]:
            continue
    need_download.append(entry)
    total_bytes += entry["size"]

local_known_paths = set()
for root, _, files in os.walk(install_dir):
    for f in files:
        p = os.path.relpath(os.path.join(root, f), install_dir).replace(os.sep, "/")
        if p in ("launch.sh", "launch.bat", "manifest.local.json"):
            continue
        local_known_paths.add(p)
to_remove = local_known_paths - set(remote_files.keys())

version = remote.get("version", "?")
if not need_download and not to_remove:
    print(f"[launcher] ✓ 已是最新版本 ({version})")
    shutil.copy2(remote_manifest_path, local_manifest_path)
    sys.exit(0)

mb = total_bytes / 1024 / 1024
print(f"[launcher] ▸ 发现更新 {version}: {len(need_download)} 个文件需下载 "
      f"({mb:.1f} MB), {len(to_remove)} 个文件需删除")

def try_download(entry):
    rel = entry["path"]
    url = files_base + urllib.parse.quote(rel)
    target = os.path.join(install_dir, rel)
    os.makedirs(os.path.dirname(target) or ".", exist_ok=True)
    tmp = target + ".tmp.download"
    rc = subprocess.call([
        "curl", "-fSL",
        "--retry", "5", "--retry-delay", "2",
        "--connect-timeout", "30", "--max-time", "1200",
        "-C", "-", "-o", tmp, url,
    ])
    if rc != 0:
        return False, "curl"
    if sha256_file(tmp) != entry["sha256"]:
        try: os.remove(tmp)
        except OSError: pass
        return False, "sha256"
    if entry.get("exec"):
        os.chmod(tmp, 0o755)
    os.replace(tmp, target)
    return True, None

pending = list(need_download)
done_bytes = 0
done_count = 0
total_files = len(need_download)
max_rounds = 5
for round_i in range(1, max_rounds + 1):
    if not pending:
        break
    if round_i > 1:
        print(f"[launcher] ▸ 第 {round_i}/{max_rounds} 轮重试剩余 {len(pending)} 个文件")
    next_pending = []
    for entry in pending:
        rel = entry["path"]
        ok, reason = try_download(entry)
        if ok:
            done_bytes += entry["size"]
            done_count += 1
            pct = done_bytes / total_bytes * 100 if total_bytes else 100
            print(f"[launcher]   [{done_count}/{total_files}] {rel}  ({pct:.1f}%)")
        else:
            print(f"[launcher]   ⚠ {rel}  失败({reason})，稍后重试", file=sys.stderr)
            next_pending.append(entry)
    pending = next_pending

if pending:
    print(f"[launcher] ❌ {len(pending)} 个文件经 {max_rounds} 轮后仍无法下载", file=sys.stderr)
    for e in pending[:10]:
        print(f"    - {e['path']}", file=sys.stderr)
    sys.exit(2)

for rel in to_remove:
    abs_path = os.path.join(install_dir, rel)
    try:
        os.remove(abs_path)
        print(f"[launcher]   × 移除 {rel}")
    except OSError:
        pass

for root, dirs, files in os.walk(install_dir, topdown=False):
    if root == install_dir: continue
    if not os.listdir(root):
        try: os.rmdir(root)
        except OSError: pass

shutil.copy2(remote_manifest_path, local_manifest_path)
print(f"[launcher] ✅ 更新完成 -> {version}")
PY
    fi
fi

# ── 启动 ────────────────────────────────────
if [ ! -x "$CLIENT_BIN" ]; then
    if [ -f "$CLIENT_BIN" ]; then chmod +x "$CLIENT_BIN"
    else log "❌ 找不到可执行文件: $CLIENT_BIN"; exit 1
    fi
fi
FFMPEG_BIN="$INSTALL_DIR/RoboMasterClient_Data/StreamingAssets/ffmpeg/linux64/ffmpeg"
[ -f "$FFMPEG_BIN" ] && chmod +x "$FFMPEG_BIN" 2>/dev/null || true

if [ -f "$LOCAL_MANIFEST" ]; then
    VER=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1])).get('version','?'))" "$LOCAL_MANIFEST" 2>/dev/null || echo "?")
    log "启动 RoboMasterClient (version=$VER)"
fi

exec "$CLIENT_BIN" "$@"
