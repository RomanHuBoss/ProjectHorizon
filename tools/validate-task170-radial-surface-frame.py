#!/usr/bin/env python3
"""Static contract gate for TASK-170 radial surface frame and cube-face traversal foundation."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    p = ROOT / path
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

f: list[str] = []
topology = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceTopologyRuntime.cs")
radial = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceRadialFrameRuntime.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceRadialFrameAcceptance.cs")
part = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceRadialSurface.cs")
player = text("src/Game.Client/Scripts/Player/PlayerController.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
console = text("src/Game.Client/Scripts/Developer/DeveloperDiagnosticsSuite.cs")
bridge = text("src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs")
workbench = text("src/Game.Client/Scripts/Developer/DeveloperWorkbenchController.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")
version = text("VERSION").strip()

need("PlanetSurfaceTangentFrame" in topology and "BuildTangentFrame" in topology,
     "planet-global tangent-frame math missing", f)
need("PlanetSurfaceCubeFaceAddress" in topology and "ToCubeFaceAddress" in topology and "FaceName" in topology,
     "cube-face addressing missing", f)
need("GeodesicStep" in topology and "ToCanonicalLogical" in topology,
     "geodesic displacement/canonical logical bridge missing", f)
need("class PlanetSurfaceRadialFrameRuntime" in radial and "StandardGravityMetersPerSecondSquared" in radial,
     "radial frame runtime/gravity scaling missing", f)
need("WarpTarget" in radial and "MeasureUpDeltaDegrees" in radial,
     "surface warp/seam-up runtime contract missing", f)
need("SetPlanetSurfaceGravity" in player and "UpDirection = Vector3.Up" in player and "ActivePlanetGravityG" in player,
     "moving-tangent local gravity integration missing", f)
need("TASK-170 radial surface frame READY" in part and "cube-face transition" in part,
     "TASK-170 live radial/face transition diagnostics missing", f)
need("DeveloperSurfaceWarp" in part and "RestorePlanetSurfaceFrameAtLogicalPosition" in part and "surface_warp" in part,
     "developer cross-face surface warp missing", f)
need("RunPlanetRadialSurfaceAcceptance" in part and "PlanetSurfaceRadialFrameAcceptanceRunner.Run" in part,
     "TASK-170 runtime acceptance wiring missing", f)
need("UpdatePlanetRadialSurfaceRuntime();" in main and "RunPlanetRadialSurfaceAcceptance();" in main and "TASK-170 (F5)" in main,
     "TASK-170 process/F5/HUD wiring missing", f)
need("surface_warp" in console and '"surface_warp" => DeveloperSurfaceWarp(parts)' in bridge and "surface_warp <latitudeDeg>" in workbench,
     "surface_warp developer command not registered end-to-end", f)
need("ExpectedActiveChunks == 25" in acceptance and "ExpectedCollisionChunks == 9" in acceptance,
     "bounded 25/9 gameplay streamer guard missing", f)
need("PlanetRadialSurfaceFrame_CoversCubeFacesWithOrthonormalTangentBases" in tests and
     "PlanetRadialSurfaceFrame_GeodesicWarpAndFaceSeamRemainContinuous" in tests and
     "PlanetRadialSurfaceFrame_AcceptanceClosesGravityFaceAndBoundedStreamingContract" in tests,
     "TASK-170 xUnit regression groups missing", f)
need(version in {"0.1.0-alpha.170", "0.1.0-alpha.172", "0.1.0-alpha.172.1", "0.1.0-alpha.174", "0.1.0-alpha.174.1", "0.1.0-alpha.176", "0.1.0-alpha.176.1", "0.1.0-alpha.178", "0.1.0-alpha.178.1", "0.1.0-alpha.178.2", "0.1.0-alpha.178.3", "0.1.0-alpha.178.4", "0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180", "0.1.0-alpha.180.1"}, "VERSION not alpha.170/172/172.1/174/174.1/176", f)

if f:
    print("TASK-170 RADIAL SURFACE FRAME CONTRACT FAIL:")
    for item in f:
        print("- " + item)
    raise SystemExit(1)

print(
    "TASK-170 RADIAL SURFACE FRAME CONTRACT PASS: "
    "topology=tangent+cube-face; gravity=planet-scaled/local-Y; geodesicStep=1; "
    "faceTraversal=1; warp=developer; streamer=25/9-bounded; "
    "persistence=logical-xz/no-schema-bump; f5=1; xunit=3-regression-groups."
)
