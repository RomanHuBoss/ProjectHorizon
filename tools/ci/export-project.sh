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
mkdir -p "$WINDOWS_OUT" "$LINUX_OUT"

mode="--export-$CONFIG"

echo "[Project Horizon] Import/compile project before exports..."
"$GODOT_BIN" --headless --path "$PROJECT" --editor --quit-after 1

echo "[Project Horizon] Exporting Windows x64 $CONFIG..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Windows Desktop" \
  "$WINDOWS_OUT/ProjectHorizon.exe"

echo "[Project Horizon] Exporting Linux x86_64 $CONFIG..."
"$GODOT_BIN" --headless --path "$PROJECT" "$mode" "Linux" \
  "$LINUX_OUT/ProjectHorizon.x86_64"
chmod +x "$LINUX_OUT/ProjectHorizon.x86_64" || true

for target in "$WINDOWS_OUT" "$LINUX_OUT"; do
  if [[ -z "$(find "$target" -maxdepth 2 -type f -print -quit)" ]]; then
    echo "ERROR: export produced no files in $target" >&2
    exit 1
  fi
done

echo "TASK-140 EXPORT PASS: config=$CONFIG; windows=1; linux=1; headless=1."
