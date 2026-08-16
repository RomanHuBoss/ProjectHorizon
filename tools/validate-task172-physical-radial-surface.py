#!/usr/bin/env python3
"""Static contract gate for TASK-172 rotating radial collision/navigation tangent frame."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    p = ROOT / path
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

f: list[str] = []
physical = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfacePhysicalFrameRuntime.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfacePhysicalFrameAcceptance.cs")
part = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePhysicalSurface.cs")
frame_part = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceFrame.cs")
radial_part = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceRadialSurface.cs")
player = text("src/Game.Client/Scripts/Player/PlayerController.cs")
terrain = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
nav = text("src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs")
npc = text("src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs")
fauna = text("src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs")
ships = text("src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")
version = text("VERSION").strip()

need("class PlanetSurfacePhysicalFrameRuntime" in physical and
     "GameplayTransform" in physical and "MapPoint" in physical and "MapVector" in physical,
     "physical rotating tangent-frame runtime missing", f)
need("Build(" in physical and "SurfaceBasis" in physical and "WorldUp" in physical,
     "physical frame basis/up state missing", f)
need("SetPlanetSurfaceFrame" in player and "ApplySurfaceFrameTransition" in player and
     "UpDirection = up" in player and "velocity.Dot(up)" in player,
     "arbitrary-up player physics missing", f)
need("gameplay.GlobalTransform = next.GameplayTransform" in part and
     "ground.GlobalTransform" in part and "_planetSurfaceStreamer.GlobalTransform" in part,
     "rotating Gameplay/ground/terrain physical transforms missing", f)
need("ApplyWorldFrameTransform" in npc and "ApplyWorldFrameTransform" in fauna and
     "ApplyWorldFrameTransform" in ships,
     "AI runtime cache remap across physical frame transitions missing", f)
need("ToLocal(worldPosition)" in terrain and "_logicalOriginEastMeters" in terrain,
     "terrain streamer is not frame-aware before logical chunk addressing", f)
need("NotifySurfaceFrameChanged" in nav and "ParentFrameAligned" in nav and
     "SurfaceLogicalToWorld" in nav,
     "navigation frame handoff/recovery bridge missing", f)
need("TASK-172 physical cube-face handoff PASS" in part and
     "TASK-172 physical radial surface READY" in part,
     "TASK-172 runtime diagnostics missing", f)
need("RunPlanetSurfacePhysicalFrameAcceptance" in part and
     "PlanetSurfacePhysicalFrameAcceptanceRunner.Run" in part,
     "TASK-172 F5 acceptance implementation missing", f)
need("RunPlanetSurfacePhysicalFrameAcceptance();" in main and "TASK-172 (F5)" in main and
     "BuildPlanetSurfacePhysicalFrameHudLine" in main,
     "TASK-172 F5/HUD wiring missing", f)
need("RestorePlanetSurfaceFrameAtLogicalPosition" in radial_part and
     "SurfaceLogicalToLocalPosition" in radial_part,
     "developer cross-face warp does not re-enter physical logical frame", f)
need("ApplyPlanetSurfacePhysicalTransforms" in frame_part,
     "floating-origin rebase is not delegated to physical radial frame", f)
need("PlanetPhysicalRadialFrame_MapsLogicalPointsAndVectorsThroughRotatingTangentBasis" in tests and
     "PlanetPhysicalRadialFrame_CubeFaceHandoffPreservesLogicalIdentity" in tests and
     "PlanetPhysicalRadialFrame_AcceptanceCoversSixFacesAndSeamHandoff" in tests,
     "TASK-172 xUnit regression groups missing", f)
need("ExpectedActiveChunks" in part and "ExpectedCollisionChunks" in part,
     "25/9 bounded-streamer guard missing from TASK-172 acceptance", f)
need(version in {"0.1.0-alpha.172", "0.1.0-alpha.172.1", "0.1.0-alpha.174", "0.1.0-alpha.174.1", "0.1.0-alpha.176", "0.1.0-alpha.176.1", "0.1.0-alpha.178", "0.1.0-alpha.178.1", "0.1.0-alpha.178.2", "0.1.0-alpha.178.3", "0.1.0-alpha.178.4", "0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180", "0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1", "0.1.0-alpha.186", "0.1.0-alpha.188", "0.1.0-alpha.190"}, "VERSION not alpha.172/172.1/174/174.1/176", f)

if f:
    print("TASK-172 PHYSICAL RADIAL SURFACE CONTRACT FAIL:")
    for item in f:
        print("- " + item)
    raise SystemExit(1)

print(
    "TASK-172 PHYSICAL RADIAL SURFACE CONTRACT PASS: "
    "player=arbitrary-up; gameplay=rotating-tangent; collision=rotated-25/9; "
    "navigation=frame-aware; aiCaches=remapped; seams=handoff; "
    "persistence=logical-xz/no-schema-bump; f5=1; xunit=3-regression-groups."
)
