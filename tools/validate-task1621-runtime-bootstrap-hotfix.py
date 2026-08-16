#!/usr/bin/env python3
"""Static regression gate for TASK-162.1 navigation/voyage bootstrap ordering."""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAIN = ROOT / "src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs"
FRAME = ROOT / "src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceFrame.cs"
GALAXY = ROOT / "src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs"


def method_body(source: str, name: str) -> str:
    match = re.search(rf"\b(?:public|private|protected|internal)\s+(?:async\s+)?[^\n{{;]+\b{re.escape(name)}\s*\([^)]*\)\s*\{{", source)
    if not match:
        raise RuntimeError(f"method not found: {name}")
    start = match.end() - 1
    depth = 0
    in_string = False
    escaped = False
    for index in range(start, len(source)):
        ch = source[index]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == '{':
            depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise RuntimeError(f"unbalanced method: {name}")


def require(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)


main = MAIN.read_text(encoding="utf-8")
frame = FRAME.read_text(encoding="utf-8")
galaxy = GALAXY.read_text(encoding="utf-8")
failures: list[str] = []

for method_name in ("_Ready", "PollLoadTask", "PollResetTask"):
    body = method_body(main, method_name)
    galaxy_index = body.find("InitializeGalaxyNavigationRuntime(")
    voyage_index = body.find("InitializeStageOneVoyageRuntime(")
    require(galaxy_index >= 0, f"{method_name}: galaxy runtime initialization missing", failures)
    require(voyage_index >= 0, f"{method_name}: Stage-1 voyage initialization missing", failures)
    require(
        galaxy_index >= 0 and voyage_index >= 0 and galaxy_index < voyage_index,
        f"{method_name}: GalaxyNavigationRuntime must exist before StageOneVoyage applies frame-aware positions",
        failures,
    )

require(
    "string planetId = GalaxyNavigation.CurrentPlanetId;" in frame and
    "SurfaceLogicalToLocalPosition" in frame,
    "TASK-162 frame no longer exposes the dependency this regression gate is intended to protect",
    failures,
)
require(
    "_galaxyNavigationRuntime = new GalaxyNavigationRuntime(saveData);" in galaxy,
    "GalaxyNavigationRuntime construction contract changed unexpectedly",
    failures,
)

if failures:
    print("TASK-162.1 RUNTIME BOOTSTRAP ORDER HOTFIX FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-162.1 RUNTIME BOOTSTRAP ORDER HOTFIX PASS: "
    "ready=galaxy-before-voyage; load=galaxy-before-voyage; reset=galaxy-before-voyage; "
    "frameCurrentPlanetDependency=guarded-by-order."
)
