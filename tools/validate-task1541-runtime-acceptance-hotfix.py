#!/usr/bin/env python3
"""TASK-154.1 regression gate for the F5 failures reported after alpha.154."""
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
failures: list[str] = []


def text(path: str) -> str:
    p = ROOT / path
    if not p.exists():
        failures.append(f"missing file: {path}")
        return ""
    return p.read_text(encoding="utf-8", errors="replace")


def need(condition: bool, message: str) -> None:
    if not condition:
        failures.append(message)


generator = text("src/Game.Domain/ProjectHorizonGenerator.cs")
nav_acceptance = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcNavigation.cs")
section36 = text("tools/validate-section36-testing-contract.py")
manifest_path = ROOT / "src/Game.Client/Testing/golden-seeds.v1.json"
need(manifest_path.exists(), "golden manifest missing")
manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}

match = re.search(r"public const int Version = (\d+);", generator)
need(match is not None, "central generator version missing")
version = int(match.group(1)) if match else -1
need(version >= 2, "starter-system deterministic change was not versioned")
need(manifest.get("generatorVersion") == version,
     "golden manifest generatorVersion does not match central generator version")

systems = manifest.get("systemCases", [])
starter = next((case for case in systems
                if case.get("systemId") == "system.vertical_slice" and
                   case.get("sectorX") == 0 and case.get("sectorY") == 0 and
                   case.get("sectorZ") == 0), None)
need(starter is not None, "starter golden system fixture missing")
if starter:
    planets = starter.get("planets", [])
    need(starter.get("planetCount") == 4 and len(planets) == 4,
         "starter golden fixture still expects the obsolete one-planet system")
    need([planet.get("archetype") for planet in planets] ==
         ["temperate", "desert", "frozen", "volcanic"],
         "starter golden archetype sequence does not match Stage 2 starter system")
    parts = [
        f"seed={starter['universeSeed']}",
        f"sector={starter['sectorX']},{starter['sectorY']},{starter['sectorZ']}",
        f"system={starter['systemId']}",
        f"name={starter['displayName']}",
        f"star={starter['starType']}",
        f"economy={starter['economyType']}",
        f"danger={starter['dangerLevel']}",
        f"planets={len(planets)}",
    ]
    payload = ";".join(parts)
    for planet in planets:
        payload += (
            f"|{planet['planetId']},{planet['archetype']},{planet['orbitIndex']},"
            f"{planet['moonCount']},{1 if planet['hasAtmosphere'] else 0},"
            f"{1 if planet['hasWater'] else 0},{planet['seed']}")
    checksum = hashlib.sha256(payload.encode("utf-8")).hexdigest()
    need(checksum == starter.get("checksum"),
         "starter golden checksum is not consistent with the reviewed fixture")
    need(checksum == "de556c0b329522a2fb698e67106542f6befc0e8ecc2238a8fac42f2ea8616d66",
         "starter golden checksum does not match the actual alpha.154 runtime output")

need("central_version" in section36 and "manifest.get(\"generatorVersion\") == central_version" in section36,
     "section-36 static gate still hard-codes generator version 1")

need("TryProbeInitialNavigationSurface" in nav_acceptance,
     "TASK-124 acceptance lacks query-readiness retry probe")
need("RUNNING path-sync attempt=" in nav_acceptance and
     "RUNNING restored-path attempt=" in nav_acceptance,
     "TASK-124 acceptance does not wait for valid paths before/after streaming")
need("BuildNavigationAcceptancePathProbes" in nav_acceptance and
     "bestTiles >= 3" in nav_acceptance and
     "PathAvoidsCapturedObstacles" in nav_acceptance,
     "TASK-124 acceptance does not retain the original cross-tile/clearance invariants")
need("_npcNavigationAcceptanceElapsed = 0.0;" in nav_acceptance and
     "pathProbeAttempts=" in nav_acceptance,
     "TASK-124 phase timing/diagnostics are not bounded and observable")
need("ProbeInitialNavigationSurface();" not in nav_acceptance,
     "obsolete one-shot TASK-124 path probe remains")

if failures:
    print("TASK-154.1 RUNTIME ACCEPTANCE HOTFIX CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-154.1 RUNTIME ACCEPTANCE HOTFIX CONTRACT PASS: "
    f"generatorVersion={version}; goldenStarter=4/4; goldenChecksum=1; "
    "navQueryRetry=1; navRestoredPath=1; navCrossTileInvariant=1; boundedPhases=1."
)
