#!/usr/bin/env python3
"""Create deterministic-ish Project Horizon release packages and metadata."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import shutil
import tarfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def zip_tree(source: Path, target: Path) -> None:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for path in sorted(p for p in source.rglob("*") if p.is_file()):
            zf.write(path, path.relative_to(source).as_posix())


def tar_tree(source: Path, target: Path) -> None:
    with tarfile.open(target, "w:gz", compresslevel=9) as tf:
        for path in sorted(source.rglob("*")):
            tf.add(path, arcname=path.relative_to(source).as_posix(), recursive=False)


def extract_notes(changelog: str, version: str) -> str:
    marker = f"## [{version}]"
    start = changelog.find(marker)
    if start < 0:
        raise RuntimeError(f"CHANGELOG.md section not found for {version}")
    next_section = changelog.find("\n## [", start + len(marker))
    return changelog[start: next_section if next_section >= 0 else len(changelog)].strip() + "\n"


def collect_symbols(target: Path) -> list[Path]:
    candidates = []
    for path in ROOT.rglob("*.pdb"):
        rel = path.relative_to(ROOT)
        if any(part in {"artifacts", "TestResults"} for part in rel.parts):
            continue
        if "Game.Client" not in path.name and "Game.Client" not in str(rel):
            continue
        candidates.append(path)
    if not candidates:
        raise RuntimeError("no Project Horizon portable PDB symbols found after Release build")
    staging = target / "symbols-staging"
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True)
    copied: list[Path] = []
    for path in sorted(set(candidates)):
        rel = path.relative_to(ROOT)
        out = staging / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, out)
        copied.append(out)
    return copied


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", default=os.environ.get("GITHUB_SHA", "unknown"))
    parser.add_argument("--exports", default=str(ROOT / "artifacts" / "exports"))
    parser.add_argument("--output", default=str(ROOT / "artifacts" / "release"))
    args = parser.parse_args()

    version_file = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    if args.version != version_file:
        raise SystemExit(f"ERROR: requested version {args.version} != VERSION {version_file}")

    exports = Path(args.exports).resolve()
    windows = exports / "windows-release"
    linux = exports / "linux-release"
    if not windows.is_dir() or not any(windows.rglob("*")):
        raise SystemExit(f"ERROR: Windows Release export missing: {windows}")
    if not linux.is_dir() or not any(linux.rglob("*")):
        raise SystemExit(f"ERROR: Linux Release export missing: {linux}")

    output = Path(args.output).resolve()
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)

    win_archive = output / f"ProjectHorizon-{args.version}-windows-x64.zip"
    linux_archive = output / f"ProjectHorizon-{args.version}-linux-x86_64.tar.gz"
    symbols_archive = output / f"ProjectHorizon-{args.version}-symbols.zip"
    zip_tree(windows, win_archive)
    tar_tree(linux, linux_archive)

    symbols = collect_symbols(output)
    staging = output / "symbols-staging"
    zip_tree(staging, symbols_archive)
    shutil.rmtree(staging)

    shutil.copy2(ROOT / "VERSION", output / "VERSION")
    shutil.copy2(ROOT / "CHANGELOG.md", output / "CHANGELOG.md")
    notes = extract_notes((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), args.version)
    (output / "RELEASE_NOTES.md").write_text(notes, encoding="utf-8")

    manifest = {
        "schemaVersion": 1,
        "product": "Project Horizon",
        "version": args.version,
        "commit": args.commit,
        "godotVersion": "4.7.1",
        "dotnetGlobalJson": json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]["version"],
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "platforms": ["windows-x64", "linux-x86_64"],
        "symbolFileCount": len(symbols),
        "artifacts": [],
    }
    for path in (win_archive, linux_archive, symbols_archive):
        manifest["artifacts"].append({"file": path.name, "bytes": path.stat().st_size, "sha256": sha256(path)})
    manifest_path = output / "release-manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    checksum_targets = [win_archive, linux_archive, symbols_archive, manifest_path, output / "VERSION", output / "CHANGELOG.md", output / "RELEASE_NOTES.md"]
    checksums = "".join(f"{sha256(path)}  {path.name}\n" for path in sorted(checksum_targets, key=lambda p: p.name))
    (output / "SHA256SUMS.txt").write_text(checksums, encoding="utf-8")

    print(
        f"TASK-140 RELEASE PACKAGE PASS: version={args.version}; windows=1; linux=1; "
        f"symbols={len(symbols)}; checksums={len(checksum_targets)}; manifest=1; changelog=1."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
