#!/usr/bin/env bash
set -euo pipefail

# One-click stop: gracefully terminate mosquitto, mock_server, Qt GUI
# Fallback: if PID file missing, try best-effort kill by binary paths

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOCK_ROOT="$SCRIPT_DIR"
RUN_DIR="$MOCK_ROOT/run"
PIDS_ENV="$RUN_DIR/pids.env"
BUILD_DIR="$MOCK_ROOT/build"
GUI_BUILD_DIR="$MOCK_ROOT/qtgui/build"

if [[ ! -f "$PIDS_ENV" ]]; then
  echo "[stop_mock] No PID file found at $PIDS_ENV; trying best-effort stop by binary names."
  # Attempt to kill mock_server and Qt GUI by full path match
  if [[ -x "$BUILD_DIR/mock_server" ]]; then
    pkill -f "$BUILD_DIR/mock_server" || true
  fi
  if [[ -x "$GUI_BUILD_DIR/MockServerQtGui" ]]; then
    pkill -f "$GUI_BUILD_DIR/MockServerQtGui" || true
  fi
  # Try mosquitto (may affect other instances; skip unless log directory exists)
  if [[ -d "$RUN_DIR" ]]; then
    pkill -f mosquitto || true
  fi
  echo "[stop_mock] Done (fallback)."
  exit 0
fi

# Load PIDs
source "$PIDS_ENV"

stop_pid() {
  local name="$1"; shift
  local pid="$1"; shift
  if [[ -n "${pid}" ]] && kill -0 "${pid}" >/dev/null 2>&1; then
    echo "[stop_mock] Stopping ${name} (PID ${pid})..."
    kill -TERM "${pid}" || true
    sleep 1
    if kill -0 "${pid}" >/dev/null 2>&1; then
      echo "[stop_mock] ${name} still alive; sending KILL"
      kill -KILL "${pid}" || true
    fi
  else
    echo "[stop_mock] ${name} not running or PID empty."
  fi
}

stop_pid "mosquitto" "${MOSQ_PID:-}"
stop_pid "mock_server" "${MOCK_PID:-}"
stop_pid "qt_gui" "${GUI_PID:-}"

echo "[stop_mock] Done."
