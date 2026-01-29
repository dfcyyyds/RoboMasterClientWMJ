#!/usr/bin/env bash
set -euo pipefail

# One-click start: MQTT broker (if available), MockServer
# Optional: Qt GUI (disabled by default). Saves PIDs to run/pids.env for stop script

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOCK_ROOT="$SCRIPT_DIR"
RUN_DIR="$MOCK_ROOT/run"
BUILD_DIR="$MOCK_ROOT/build"
SERVER_BIN="$BUILD_DIR/mock_server"

# Defaults (can be overridden by args)
HOST_ARG="127.0.0.1"
PORT_ARG="3333"
UDP_IP_ARG="127.0.0.1"
UDP_PORT_ARG="3334"
# Default codec/gop optimized for quick client entry
CODEC_ARG="hevc"
GOP_ARG="15"
# Default to TestVedio.mp4 for realistic video simulation
VIDEO_ARG="$SCRIPT_DIR/TestVedio.mp4"
VIDEO_PROVIDED=0
INTERVAL_ARG="1000"
FORCE_REBUILD=0
START_GUI=0

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      HOST_ARG="$2"; shift 2;;
    --port)
      PORT_ARG="$2"; shift 2;;
    --udp-ip)
      UDP_IP_ARG="$2"; shift 2;;
    --udp-port)
      UDP_PORT_ARG="$2"; shift 2;;
    --video)
      VIDEO_ARG="$2"; VIDEO_PROVIDED=1; shift 2;;
    --interval)
      INTERVAL_ARG="$2"; shift 2;;
    --codec)
      CODEC_ARG="$2"; shift 2;;
    --gop)
      GOP_ARG="$2"; shift 2;;
    --force-rebuild)
      FORCE_REBUILD=1; shift;;
    --with-gui)
      START_GUI=1; shift;;
    --no-gui)
      START_GUI=0; shift;;
    --help|-h)
      echo "Usage: $0 [--host ip] [--port port] [--udp-ip ip] [--udp-port port] [--video spec] [--codec hevc|h264] [--gop frames] [--interval ms] [--force-rebuild] [--with-gui|--no-gui]"
      echo "  --video can be '/dev/video0' or 'lavfi:testsrc=size=WxH:rate=F'"
      echo "  --codec selects encoder (default hevc); --gop sets IDR interval (default 15)"
      echo "  --with-gui enables Qt GUI (default is headless)"
      exit 0;;
    *)
      echo "Unknown arg: $1"; exit 1;;
  esac
done

mkdir -p "$RUN_DIR"

# Auto-select source if not provided: prefer bundled mp4/avi, else camera, else testsrc
if [[ $VIDEO_PROVIDED -eq 0 ]]; then
  LOCAL_MP4="$MOCK_ROOT/TestVedio.mp4"
  LOCAL_AVI="$MOCK_ROOT/single_20260125_1534.avi"
  if [[ -f "$LOCAL_MP4" ]]; then
    VIDEO_ARG="$LOCAL_MP4"
    echo "[start_mock] Auto-selected bundled MP4: $VIDEO_ARG"
  elif [[ -f "$LOCAL_AVI" ]]; then
    VIDEO_ARG="$LOCAL_AVI"
    echo "[start_mock] Auto-selected bundled AVI: $VIDEO_ARG"
  elif [[ -e "/dev/video0" ]]; then
    VIDEO_ARG="/dev/video0"
    echo "[start_mock] Auto-selected camera: $VIDEO_ARG"
  else
    first_cam=$(ls /dev/video* 2>/dev/null | head -n 1 || true)
    if [[ -n "$first_cam" ]]; then
      VIDEO_ARG="$first_cam"
      echo "[start_mock] Auto-selected camera: $VIDEO_ARG"
    else
      echo "[start_mock] No v4l2 camera found; using test source: $VIDEO_ARG"
    fi
  fi
fi

# Helper: check if binary is outdated vs sources
is_outdated() {
  local bin="$1"; shift
  local src_globs=("$@")
  if [[ ! -e "$bin" ]]; then return 0; fi
  # Find any source newer than binary
  for g in "${src_globs[@]}"; do
    if find "$MOCK_ROOT" -path "$g" -type f -newer "$bin" | grep -q .; then
      return 0
    fi
  done
  return 1
}

# Build mock server (server) if needed or forced or outdated
if [[ $FORCE_REBUILD -eq 1 ]] || is_outdated "$SERVER_BIN" "*/Assets/MockServerCpp/src/*.cpp" "*/Assets/MockServerCpp/src/*.h"; then
  echo "[start_mock] Building MockServerCpp (server)..."
  mkdir -p "$BUILD_DIR"
  pushd "$BUILD_DIR" >/dev/null
  cmake ..
  make -j
  popd >/dev/null
else
  echo "[start_mock] Server binary up to date: $SERVER_BIN"
fi

GUI_BUILD_DIR="$MOCK_ROOT/qtgui/build"
GUI_BIN="$GUI_BUILD_DIR/MockServerQtGui"
if [[ $START_GUI -eq 1 ]]; then
  # Build Qt GUI if needed or forced or outdated
  if [[ $FORCE_REBUILD -eq 1 ]] || is_outdated "$GUI_BIN" "*/Assets/MockServerCpp/qtgui/*.cpp" "*/Assets/MockServerCpp/qtgui/*.h"; then
    echo "[start_mock] Building MockServerCpp Qt GUI..."
    mkdir -p "$GUI_BUILD_DIR"
    pushd "$GUI_BUILD_DIR" >/dev/null
    cmake .. || echo "[start_mock] Qt GUI cmake failed (GUI optional)."
    make -j || echo "[start_mock] Qt GUI make failed (GUI optional)."
    popd >/dev/null
  else
    echo "[start_mock] Qt GUI binary up to date: $GUI_BIN"
  fi
else
  echo "[start_mock] Headless mode: Qt GUI disabled (use --with-gui to enable)."
fi

# Prepare and start MQTT broker (mosquitto) on port 3333, if available
MOSQ_PID=""
if command -v mosquitto >/dev/null 2>&1; then
  MOSQ_CONF="$RUN_DIR/mosquitto_mock.conf"
  cat > "$MOSQ_CONF" <<EOF
listener 3333
allow_anonymous true
persistence false
EOF
  echo "[start_mock] Starting mosquitto on :3333..."
  mosquitto -c "$MOSQ_CONF" -v > "$RUN_DIR/mosquitto.log" 2>&1 &
  MOSQ_PID=$!
  echo "[start_mock] mosquitto PID: $MOSQ_PID"
else
  echo "[start_mock] mosquitto not found; skipping local broker (ensure a broker at 192.168.12.1:3333 or adjust --host/--port)."
fi

# If local broker not started, and user kept default host, fall back to 192.168.12.1
if [[ -z "$MOSQ_PID" && "$HOST_ARG" == "127.0.0.1" ]]; then
  HOST_ARG="192.168.12.1"
  PORT_ARG="3333"
fi

echo "[start_mock] Starting mock_server..."
"$SERVER_BIN" --host "$HOST_ARG" --port "$PORT_ARG" \
  --udp-ip "$UDP_IP_ARG" --udp-port "$UDP_PORT_ARG" \
  --video "$VIDEO_ARG" --codec "$CODEC_ARG" --gop "$GOP_ARG" --interval "$INTERVAL_ARG" \
  > "$RUN_DIR/mock_server.log" 2>&1 &
MOCK_PID=$!
echo "[start_mock] mock_server PID: $MOCK_PID"

GUI_PID=""
if [[ $START_GUI -eq 1 ]]; then
  # Start Qt GUI if available
  if [[ -x "$GUI_BIN" ]]; then
    echo "[start_mock] Starting Qt GUI..."
    "$GUI_BIN" > "$RUN_DIR/gui.log" 2>&1 &
    GUI_PID=$!
    echo "[start_mock] Qt GUI PID: $GUI_PID"
  else
    echo "[start_mock] Qt GUI binary not found at $GUI_BIN (skipping)."
  fi
else
  echo "[start_mock] Qt GUI not started (headless)."
fi

# Save PIDs
PIDS_ENV="$RUN_DIR/pids.env"
cat > "$PIDS_ENV" <<EOF
MOSQ_PID=${MOSQ_PID}
MOCK_PID=${MOCK_PID}
GUI_PID=${GUI_PID}
EOF

# Also save JSON for tooling
cat > "$RUN_DIR/pids.json" <<EOF
{
  "mosquitto": "${MOSQ_PID}",
  "mock_server": "${MOCK_PID}",
  "qt_gui": "${GUI_PID}"
}
EOF

echo "[start_mock] PIDs saved to $PIDS_ENV and $RUN_DIR/pids.json"
echo "[start_mock] Done. Logs in $RUN_DIR/*.log"
