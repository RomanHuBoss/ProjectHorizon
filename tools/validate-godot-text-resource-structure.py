#!/usr/bin/env python3
"""Lightweight structural gate for authored Godot text scenes/resources.

Godot requires ext_resource/sub_resource declarations to precede node sections
in .tscn files. This check intentionally targets repository-level mistakes that
can make ChangeSceneToFile return CantOpen before runtime code executes.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCENE_ROOT = ROOT / "src" / "Game.Client" / "Scenes"

RESOURCE_TAG = re.compile(r"^\[(ext_resource|sub_resource)\b")
NODE_TAG = re.compile(r"^\[node\b")
EXT_DEF = re.compile(r'^\[ext_resource\b[^\]]*\bid="([^"]+)"')
SUB_DEF = re.compile(r'^\[sub_resource\b[^\]]*\bid="([^"]+)"')
EXT_REF = re.compile(r'ExtResource\("([^"]+)"\)')
SUB_REF = re.compile(r'SubResource\("([^"]+)"\)')


def main() -> int:
    failures: list[str] = []
    scenes = sorted(SCENE_ROOT.rglob("*.tscn"))
    refs_checked = 0

    for scene in scenes:
        rel = scene.relative_to(ROOT)
        text = scene.read_text(encoding="utf-8", errors="replace")
        lines = text.splitlines()
        first_node: int | None = None
        ext_ids: list[str] = []
        sub_ids: list[str] = []

        for line_no, line in enumerate(lines, 1):
            if NODE_TAG.match(line) and first_node is None:
                first_node = line_no
            if first_node is not None and RESOURCE_TAG.match(line):
                failures.append(
                    f"{rel}:{line_no}: resource declaration appears after first node at line {first_node}: {line}"
                )
            match = EXT_DEF.match(line)
            if match:
                ext_ids.append(match.group(1))
            match = SUB_DEF.match(line)
            if match:
                sub_ids.append(match.group(1))

        if first_node is None:
            failures.append(f"{rel}: no [node] declaration")

        duplicate_ext = sorted({value for value in ext_ids if ext_ids.count(value) > 1})
        duplicate_sub = sorted({value for value in sub_ids if sub_ids.count(value) > 1})
        if duplicate_ext:
            failures.append(f"{rel}: duplicate ext_resource ids={duplicate_ext}")
        if duplicate_sub:
            failures.append(f"{rel}: duplicate sub_resource ids={duplicate_sub}")

        ext_set = set(ext_ids)
        sub_set = set(sub_ids)
        missing_ext = sorted(set(EXT_REF.findall(text)) - ext_set)
        missing_sub = sorted(set(SUB_REF.findall(text)) - sub_set)
        refs_checked += len(EXT_REF.findall(text)) + len(SUB_REF.findall(text))
        if missing_ext:
            failures.append(f"{rel}: undefined ExtResource refs={missing_ext}")
        if missing_sub:
            failures.append(f"{rel}: undefined SubResource refs={missing_sub}")

    if failures:
        print(
            "GODOT TEXT RESOURCE STRUCTURE FAIL: "
            f"scenes={len(scenes)}; refs={refs_checked}; failures={len(failures)}."
        )
        for failure in failures:
            print("ERROR: " + failure)
        return 1

    print(
        "GODOT TEXT RESOURCE STRUCTURE PASS: "
        f"scenes={len(scenes)}; refs={refs_checked}; resourceOrder=1; uniqueIds=1; resolvedRefs=1."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
