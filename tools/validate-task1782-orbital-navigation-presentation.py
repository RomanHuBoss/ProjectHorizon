#!/usr/bin/env python3
"""Static regression gate for TASK-178.2 orbital navigation/presentation repair."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="replace")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

def number(source: str, name: str) -> float:
    m = re.search(rf"{re.escape(name)}\s*=\s*([0-9.]+)", source)
    return float(m.group(1)) if m else float("nan")

f: list[str] = []
version = text("VERSION").strip()
sim = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationRuntime.cs")
node = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
voyage_runtime = text("src/Game.Client/Scripts/VerticalSlice/StageOneVoyageRuntime.cs")
env_model = text("src/Game.Client/Scripts/VerticalSlice/WorldSceneEnvironmentPresentationRuntime.cs")
env_live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldEnvironmentPresentation.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/OrbitalNavigationPresentationAcceptance.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
station_scene = text("src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn")
ship_scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
interior = text("src/Game.Client/Scenes/World/StationInteriorShell.tscn")
tests = text("tests/ProjectHorizon.Tests/Unit/OrbitalNavigationPresentationTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.2", "0.1.0-alpha.178.3", "0.1.0-alpha.178.4"},
     "VERSION must be alpha.178.2 or later", f)
need(number(sim, "OrbitTimeScale") == 1.0 and
     number(sim, "MinimumPlanetOrbitRadius") >= 1800.0 and
     number(sim, "PlanetOrbitSpacing") >= 1200.0 and
     number(sim, "MinimumMoonOrbitRadius") >= 520.0 and
     number(sim, "MinimumMoonOrbitPeriodSeconds") >= 1800.0,
     "system orbital scale/cadence regressed below TASK-178.2 bounds", f)
need("StarSystemBodyKind.Star" in sim and "StarSystemBodyKind.Planet" in sim and
     "StarSystemBodyKind.Moon" in sim and "visualRadius" in sim,
     "star/planet/moon visual hierarchy is not explicitly represented", f)
need("LocalTrafficProxiesSuppressed = true" in node and
     "localPhysicalTraffic" in node and
     "StarSystemBodyKind.Station" in node and
     "StarSystemBodyKind.ShipContact" in node and
     "DisplayAnchor" in node,
     "local station/traffic proxy suppression or focused-planet placement missing", f)
need("IsDockingCaptureReady" in voyage_runtime and
     "TryDockStageOneVoyage(automatic: true)" in voyage and
     "TryLandStageOneVoyage(automatic: true)" in voyage and
     "distance > approachRange + 35.0f" in voyage and
     'mode={(automatic ? "navigation-assist" : "manual")}' in voyage and
     "TASK-178.2 navigation assist PASS" in voyage,
     "K navigation assist does not complete docking/landing transaction", f)
need("WorldSceneEnvironmentPresentationRuntime" in env_model and
     all(k in env_model for k in ("WorldSceneKind.Orbit", "WorldSceneKind.StationInterior",
                                  "WorldSceneKind.InterplanetaryTransit", "WorldSceneKind.HyperspaceTransit")) and
     "luminance <= 0.02" in env_model and "!profile.FogEnabled" in env_model,
     "non-surface vacuum environment model is incomplete", f)
need('background_mode", 1' in env_live and "ApplyPlanetSurfaceSky" in env_live and
     "ApplyPlanetWeatherPresentation" in env_live and "handoff.VacuumBlend" in env_live and
     "TASK-178.3 world environment handoff PASS" in env_live,
     "live world environment does not provide explicit orbit presentation and surface restore", f)
need("UpdateWorldSceneEnvironmentPresentation();" in slice_cs,
     "world environment presentation is not called after weather update", f)
need("TASK-178.2 orbital navigation/presentation acceptance" in acceptance and
     "SpaceEnvironment" in acceptance and "AssistDockCapture" in acceptance and
     "LocalProxyPolicy" in acceptance and "StationInterior" in acceptance,
     "TASK-178.2 aggregate acceptance does not cover all repaired invariants", f)
need("RunOrbitalNavigationPresentationAcceptance();" in slice_cs and
     "UpdateOrbitalNavigationPresentationRuntime();" in slice_cs and
     "_orbitalNavigationPresentationAcceptancePassed == true" in slice_cs and
     "TASK-178.2 (F5)" in slice_cs,
     "TASK-178.2 is not wired into F5/HUD/final gate", f)
need(ship_scene.count("far = 60000.0") >= 2,
     "ship cameras do not retain the expanded orbital presentation range", f)
need(station_scene.count('parent="Gameplay/OrbitalStation"') >= 7 and
     "OrbitalStationGuideMesh" in station_scene and "DockLight" in station_scene,
     "physical orbital station lacks explicit docking presentation", f)
need(interior.count('type="MeshInstance3D"') >= 7 and
     interior.count('type="OmniLight3D"') >= 3 and
     'EnvironmentProfile = "interior"' in interior,
     "station interior shell is still empty/unlit", f)
need("StarterSystem_UsesReadableOrbitalSpacingAndCadence" in tests and
     "DockingCapture_RequiresBothRangeAndSafeSpeed" in tests and
     "NonSurfaceWorlds_UseDarkFogFreeEnvironment" in tests,
     "TASK-178.2 xUnit regressions missing", f)
need("TASK-178.2" in readme and "0.1.0-alpha.178.2" in changelog and
     "TASK-178.2" in status,
     "TASK-178.2 documentation/status evidence missing", f)
validator = "validate-task1782-orbital-navigation-presentation.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.2 validator is not enforced in local/CI/release quality gates", f)

if f:
    print("TASK-178.2 ORBITAL NAVIGATION/PRESENTATION CONTRACT FAIL:")
    for x in f:
        print(f"- {x}")
    sys.exit(1)

print(
    "TASK-178.2 ORBITAL NAVIGATION/PRESENTATION CONTRACT PASS: "
    "autoDock=1; stationInterior=1; orbitClock=1; planetSpacing=1; moonCadence=1; "
    "visualHierarchy=1; cameraRange=1; localProxySuppression=1; vacuumEnvironment=1; f5=1; xunit=1."
)
