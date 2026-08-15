#!/usr/bin/env python3
"""Verify VERSION, release tag and changelog consistency."""
from __future__ import annotations
import argparse
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tag", default="")
    args = parser.parse_args()
    version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    changelog = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    failures: list[str] = []
    if not re.fullmatch(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version):
        failures.append(f"VERSION is not SemVer-like: {version}")
    if f"## [{version}]" not in changelog:
        failures.append(f"CHANGELOG.md has no section for {version}")
    if args.tag and args.tag != f"v{version}":
        failures.append(f"tag {args.tag!r} does not match v{version}")
    status = "PASS" if not failures else "FAIL"
    print(f"TASK-140 VERSION {status}: version={version}; tag={args.tag or '<not-required>'}; changelog={int(not any('CHANGELOG' in f for f in failures))}.")
    for failure in failures:
        print("ERROR:", failure)
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
