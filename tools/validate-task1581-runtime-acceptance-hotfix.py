#!/usr/bin/env python3
"""TASK-158.1 regression gate for stale POI golden fixture + Windows nullable warnings."""
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
failures: list[str] = []


def need(condition: bool, message: str) -> None:
    if not condition:
        failures.append(message)


def text(path: str) -> str:
    p = ROOT / path
    if not p.exists():
        failures.append(f"missing file: {path}")
        return ""
    return p.read_text(encoding="utf-8", errors="replace")


def fixed(value: float) -> str:
    return f"{value:.3f}"


generator = text("src/Game.Domain/ProjectHorizonGenerator.cs")
manifest_path = ROOT / "src/Game.Client/Testing/golden-seeds.v1.json"
catalog_path = ROOT / "src/Game.Client/Content/planetary_pois.json"
npc = text("src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs")
streaming = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceStreamingRuntime.cs")
golden_tests = text("tests/ProjectHorizon.Tests/Golden/GoldenSeedTests.cs")

version_match = re.search(r"public const int Version = (\d+);", generator)
need(version_match is not None, "central generator version missing")
version = int(version_match.group(1)) if version_match else -1
need(version == 3, "TASK-156 terrain-projected deterministic POI output must be generator version 3")

need(manifest_path.exists(), "golden manifest missing")
need(catalog_path.exists(), "POI catalog missing")
manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
catalog = json.loads(catalog_path.read_text(encoding="utf-8")) if catalog_path.exists() else {}
need(manifest.get("generatorVersion") == version,
     "golden manifest generatorVersion must match central version")

fixture = manifest.get("poiFixture", {})
placements = fixture.get("placements", [])
definitions = {item.get("poiTypeId"): item for item in catalog.get("definitions", [])}
need(fixture.get("expectedCount") == 20 and len(placements) == 20,
     "golden POI fixture must contain exactly 20 placements")
need(len(definitions) == 20, "POI catalog must contain exactly 20 definitions")

for item in placements:
    definition = definitions.get(item.get("poiTypeId"))
    if definition is None:
        failures.append(f"missing POI definition for {item.get('poiTypeId')}")
        continue
    expected_y = (
        float(item.get("controlHeight", 0.0)) +
        0.1 +
        float(definition["size"]["y"]) / 2.0
    )
    need(abs(float(item.get("positionY", 0.0)) - expected_y) <= 0.000001,
         f"terrain-projected golden Y mismatch for {item.get('instanceId')}")

payload = (
    f"worldSeed={fixture.get('worldSeed')};"
    f"region={fixture.get('regionKey')};"
    f"count={len(placements)}"
)
for item in placements:
    payload += "|" + ",".join([
        item["instanceId"],
        item["poiTypeId"],
        fixed(float(item["positionX"])),
        fixed(float(item["positionY"])),
        fixed(float(item["positionZ"])),
        fixed(float(item["rotationDegrees"])),
        fixed(float(item["controlHeight"])),
        fixed(float(item["slopeDegrees"])),
        fixed(float(item["distanceToWater"])),
        str(item["danger"]),
    ])
checksum = hashlib.sha256(payload.encode("utf-8")).hexdigest()
expected_runtime_checksum = "6e229717a6faad6043f963d825ba8b13a2af9dbf2335c161e6a24fca450ddfcc"
need(checksum == fixture.get("checksum"),
     "golden POI checksum is inconsistent with its reviewed placements")
need(checksum == expected_runtime_checksum,
     "golden POI checksum does not match external alpha.158 runtime evidence")

need("_navigationAgent is null ||\n            _navigationSurface is null" in npc,
     "NpcFactionAgentNode nullable navigation-surface guard missing")
need("out PlanetSurfaceStreamingSpec? neighbor" in streaming and
     "neighbor is not null" in streaming,
     "PlanetSurfaceStreamingRuntime nullable TryGetValue guard missing")
need("golden Y must remain terrain-projected" in golden_tests,
     "xUnit terrain-projected golden Y regression missing")

if failures:
    print("TASK-158.1 RUNTIME ACCEPTANCE HOTFIX CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-158.1 RUNTIME ACCEPTANCE HOTFIX CONTRACT PASS: "
    f"generatorVersion={version}; goldenPoi=20/20; terrainProjectedY=20/20; "
    f"checksum={checksum}; nullableWarningsClosed=2/2; xunit=1."
)
