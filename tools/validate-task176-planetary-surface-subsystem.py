#!/usr/bin/env python3
"""Static contract gate for TASK-176 planetary-surface subsystem closure."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    p = ROOT / path
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
version = text("VERSION").strip()
model = text("src/Game.Client/Scripts/VerticalSlice/PlanetarySurfaceSubsystemAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceSubsystem.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")

match = re.fullmatch(r"0\.1\.0-alpha\.(\d+)(?:\.\d+)?", version)
need(match is not None and int(match.group(1)) >= 176,
     "VERSION must be alpha.176 or later", failures)
need("ExpectedContractCount = 11" in model,
     "TASK-176 must aggregate exactly eleven normative surface contracts", failures)
for runner in (
    "PlanetEnvironmentAcceptanceRunner.Run",
    "InterplanetaryTravelAcceptanceRunner.Run",
    "PlanetSurfaceContentAcceptanceRunner.Run",
    "PlanetSurfaceTerrainAcceptanceRunner.Run",
    "PlanetSurfaceStreamingAcceptanceRunner.Run",
    "PlanetSurfaceWorldCompositionAcceptanceRunner.Run",
    "PlanetWeatherAcceptanceRunner.Run",
    "PlanetSurfaceFrameAcceptanceRunner.Run",
    "PlanetSurfaceRadialFrameAcceptanceRunner.Run",
    "PlanetSurfacePhysicalFrameAcceptanceRunner.Run",
    "PlanetSurfaceCurvedCollisionAcceptanceRunner.Run",
):
    need(runner in model, f"missing normative runner: {runner}", failures)
need("persistenceChain" in model and "traversalChain" in model and
     "boundedResidency" in model and "crossPlanetIdentity" in model,
     "cross-contract closure invariants are incomplete", failures)
need("TASK-176 planetary surface subsystem READY" in live,
     "live subsystem READY diagnostic missing", failures)
need("TASK-176 planetary surface subsystem acceptance" in live and
     "liveStreamer" in live and "liveNavigation" in live and
     "livePlayer" in live and "livePresentation" in live and
     "liveContent" in live and "coldStartSafety" in live and
     "liveWeather" in live and "liveRadialStack" in live,
     "live Godot-layer acceptance checks are incomplete", failures)
need("RunPlanetSurfaceSubsystemAcceptance();" in main and
     "UpdatePlanetSurfaceSubsystemRuntime();" in main,
     "TASK-176 is not wired into F5/runtime readiness", failures)
need('TASK-176 (F5)' in main,
     "TASK-176 HUD acceptance line missing", failures)
need("PlanetarySurfaceSubsystem_AllNormativeContractsCloseTogether" in tests and
     "Assert.True(report.PersistenceChain)" in tests and
     "Assert.True(report.TraversalChain)" in tests and
     "Assert.True(report.BoundedResidency)" in tests and
     "Assert.True(report.CrossPlanetIdentity)" in tests,
     "TASK-176 xUnit subsystem closure regression missing", failures)

if failures:
    print("TASK-176 PLANETARY SURFACE SUBSYSTEM CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-176 PLANETARY SURFACE SUBSYSTEM CONTRACT PASS: "
    "contracts=11; persistenceChain=1; traversalChain=1; bounded=1; "
    "planetIdentity=1; liveRuntime=8; f5=1; xunit=1."
)
