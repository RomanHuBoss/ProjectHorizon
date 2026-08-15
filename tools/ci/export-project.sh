#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/src/Game.Client"
CONFIG="${1:-debug}"
GODOT_BIN="${GODOT_BIN:-}"

if [[ "$CONFIG" != "debug" && "$CONFIG" != "release" ]]; then
  echo "Usage: $0 <debug|release>" >&2
  exit 2
fi
if [[ -z "$GODOT_BIN" || ! -x "$GODOT_BIN" ]]; then
  echo "ERROR: GODOT_BIN must point to the Godot 4.7.1 .NET editor binary." >&2
  exit 1
fi

OUT="$ROOT/artifacts/exports"
WINDOWS_OUT="$OUT/windows-$CONFIG"
LINUX_OUT="$OUT/linux-$CONFIG"
WINDOWS_COMPAT_OUT="$OUT/windows-compatibility-$CONFIG"
LINUX_COMPAT_OUT="$OUT/linux-compatibility-$CONFIG"
mkdir -p "$WINDOWS_OUT" "$LINUX_OUT" "$WINDOWS_COMPAT_OUT" "$LINUX_COMPAT_OUT"

mode="--export-$CONFIG"

echo "[Project Horizon] Import/compile project before exports..."
"$GODOT_BIN" --headless --path "$PROJECT" --editor --quit-after 1

echo "[Project Horizon] Exporting Windows x64 $CONFIG (Mobile/Vulkan primary)..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Windows Desktop" \
  "$WINDOWS_OUT/ProjectHorizon.exe"

echo "[Project Horizon] Exporting Linux x86_64 $CONFIG (Mobile/Vulkan primary)..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Linux" \
  "$LINUX_OUT/ProjectHorizon.x86_64"
chmod +x "$LINUX_OUT/ProjectHorizon.x86_64" || true

echo "[Project Horizon] Exporting Windows x64 $CONFIG (Compatibility/OpenGL 3.3)..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Windows Desktop Compatibility" \
  "$WINDOWS_COMPAT_OUT/ProjectHorizon-Compatibility.exe"

echo "[Project Horizon] Exporting Linux x86_64 $CONFIG (Compatibility/OpenGL 3.3)..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Linux Compatibility" \
  "$LINUX_COMPAT_OUT/ProjectHorizon-Compatibility.x86_64"
chmod +x "$LINUX_COMPAT_OUT/ProjectHorizon-Compatibility.x86_64" || true

for target in "$WINDOWS_OUT" "$LINUX_OUT" "$WINDOWS_COMPAT_OUT" "$LINUX_COMPAT_OUT"; do
  if [[ -z "$(find "$target" -maxdepth 2 -type f -print -quit)" ]]; then
    echo "ERROR: export produced no files in $target" >&2
    exit 1
  fi
done

echo "TASK-144 EXPORT PASS: config=$CONFIG; primaryWindows=1; primaryLinux=1; compatibilityWindows=1; compatibilityLinux=1; headless=1."
