#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT_BIN="${1:-godot}"
echo "TASK-214: launching uninterrupted 8-hour endurance certification."
"$GODOT_BIN" --path "$ROOT/src/Game.Client" -- --developer --endurance-soak=8
