#!/usr/bin/env python3
"""Static contract gate for TASK-172.1 radial physics/navigation emergency hotfix."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    p = ROOT / path
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

f: list[str] = []
player = text("src/Game.Client/Scripts/Player/PlayerController.cs")
nav = text("src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs")
npc = text("src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs")
physical = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePhysicalSurface.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfacePhysicalFrameAcceptance.cs")
runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfacePhysicalFrameRuntime.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")
presentation = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs")
version = text("VERSION").strip()

need("RotateBodyAroundSurfaceUp" in player and "ApplyUprightBasis" in player and
     "AlignBodyToSurfaceUp();" in player and
     "PlanetSurfacePhysicalFrameRuntime.BuildUprightBasis" in player and
     "BuildUprightBasis" in runtime and "RotateY(mouseMotion" not in player,
     "player no-roll arbitrary-up yaw contract missing", f)
need("NavigationServer3D.MapCreate" in nav and "NavigationServer3D.MapSetUp" in nav and
     "PrepareSurfaceFrameChange" in nav and "DestroyNavigationRegions" in nav and
     "region.SetNavigationMap(NavigationMap)" in nav and
     "obstacle.SetNavigationMap(NavigationMap)" in nav and
     "region.GetNavigationMap().Equals(_navigationMap)" in nav and
     "obstacle.GetNavigationMap().Equals(_navigationMap)" in nav,
     "dedicated navigation-map UP migration missing for regions/obstacles", f)
need("SetNavigationMap(navigationSurface.NavigationMap)" in npc and
     "PrepareNavigationMapChange" in npc and "Use3DAvoidance = true" in npc,
     "NPC NavigationAgent3D detach/rebind/radial-safe avoidance contract missing", f)
need("Use3DAvoidance = true" in nav,
     "radial navigation obstacles must use 3D avoidance instead of global XZ 2D avoidance", f)
need("DetachNpcNavigationAgents();" in physical and
     "PrepareSurfaceFrameChange(next.WorldUp)" in physical and
     "AttachNpcNavigationAgents();" in physical and "upright=" in physical,
     "physical handoff ordering/upright acceptance missing", f)
need("PointRoundTripToleranceMeters = 0.02" in acceptance,
     "physical frame float precision budget missing", f)
need("clearanceNumericToleranceMeters = 0.01" in presentation and
     "minimumSafeClearanceMeters = 0.80" in presentation,
     "surface presentation clearance numeric tolerance missing", f)
need("PlanetPhysicalRadialFrame_UprightBasisNeverIntroducesRollAcrossSixRadialAxes" in tests and
     "PointRoundTripToleranceMeters" in tests,
     "TASK-172.1 xUnit no-roll/precision regression coverage missing", f)
need(version in {"0.1.0-alpha.172.1", "0.1.0-alpha.174", "0.1.0-alpha.174.1", "0.1.0-alpha.176", "0.1.0-alpha.176.1", "0.1.0-alpha.178", "0.1.0-alpha.178.1", "0.1.0-alpha.178.2"}, "VERSION not alpha.172.1/174/174.1/176", f)

if f:
    print("TASK-172.1 RADIAL PHYSICS HOTFIX CONTRACT FAIL:")
    for item in f:
        print("- " + item)
    raise SystemExit(1)

print(
    "TASK-172.1 RADIAL PHYSICS HOTFIX CONTRACT PASS: "
    "player=no-roll-upright; navigation=dedicated-map-up(regions+obstacles); avoidance=3d-radial-safe; agents=detach+rebound; "
    "handoff=detach-before-rotate; pointBudget=0.020m; clearanceTol=0.010m; xunit=1+precision-updates."
)
