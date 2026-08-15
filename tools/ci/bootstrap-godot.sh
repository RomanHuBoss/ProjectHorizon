#!/usr/bin/env bash
set -euo pipefail

GODOT_VERSION="${GODOT_VERSION:-4.7.1}"
GODOT_CHANNEL="${GODOT_CHANNEL:-stable}"
CACHE_ROOT="${GODOT_CACHE_DIR:-$HOME/.cache/project-horizon/godot-${GODOT_VERSION}-${GODOT_CHANNEL}}"
EDITOR_ARCHIVE="Godot_v${GODOT_VERSION}-${GODOT_CHANNEL}_mono_linux_x86_64.zip"
TEMPLATE_ARCHIVE="Godot_v${GODOT_VERSION}-${GODOT_CHANNEL}_mono_export_templates.tpz"
BASE_URL="https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-${GODOT_CHANNEL}"
EDITOR_ROOT="$CACHE_ROOT/editor"
DOWNLOAD_ROOT="$CACHE_ROOT/downloads"
TEMPLATE_ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/${GODOT_VERSION}.${GODOT_CHANNEL}"

mkdir -p "$EDITOR_ROOT" "$DOWNLOAD_ROOT"

download() {
  local url="$1"
  local target="$2"
  if [[ -s "$target" ]]; then
    return
  fi
  echo "[Project Horizon] Downloading $(basename "$target")..."
  curl --fail --location --retry 4 --retry-delay 3 --output "$target.tmp" "$url"
  mv "$target.tmp" "$target"
}

download "$BASE_URL/$EDITOR_ARCHIVE" "$DOWNLOAD_ROOT/$EDITOR_ARCHIVE"
download "$BASE_URL/$TEMPLATE_ARCHIVE" "$DOWNLOAD_ROOT/$TEMPLATE_ARCHIVE"

if ! find "$EDITOR_ROOT" -maxdepth 3 -type f -name 'Godot_v*_mono_linux*.x86_64' -print -quit | grep -q .; then
  rm -rf "$EDITOR_ROOT"
  mkdir -p "$EDITOR_ROOT"
  unzip -q "$DOWNLOAD_ROOT/$EDITOR_ARCHIVE" -d "$EDITOR_ROOT"
fi

GODOT_BIN="$(find "$EDITOR_ROOT" -maxdepth 3 -type f -name 'Godot_v*_mono_linux*.x86_64' -print -quit)"
if [[ -z "$GODOT_BIN" ]]; then
  echo "ERROR: Godot .NET editor binary was not found after extraction." >&2
  exit 1
fi
chmod +x "$GODOT_BIN"

if [[ ! -f "$TEMPLATE_ROOT/windows_debug_x86_64.exe" || ! -f "$TEMPLATE_ROOT/linux_debug.x86_64" ]]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  unzip -q "$DOWNLOAD_ROOT/$TEMPLATE_ARCHIVE" -d "$tmp"
  src="$tmp/templates"
  if [[ ! -d "$src" ]]; then
    echo "ERROR: mono export template archive does not contain templates/." >&2
    exit 1
  fi
  rm -rf "$TEMPLATE_ROOT"
  mkdir -p "$(dirname "$TEMPLATE_ROOT")"
  mv "$src" "$TEMPLATE_ROOT"
fi

export GODOT_BIN
export PATH="$(dirname "$GODOT_BIN"):$PATH"

echo "[Project Horizon] Godot editor: $GODOT_BIN"
echo "[Project Horizon] Export templates: $TEMPLATE_ROOT"
"$GODOT_BIN" --version

if [[ -n "${GITHUB_ENV:-}" ]]; then
  printf 'GODOT_BIN=%s\n' "$GODOT_BIN" >> "$GITHUB_ENV"
  printf '%s\n' "$(dirname "$GODOT_BIN")" >> "$GITHUB_PATH"
fi
