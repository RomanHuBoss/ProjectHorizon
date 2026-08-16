#!/usr/bin/env python3
"""Static regression gate for TASK-178.4 planetary landing/lighting recovery."""
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
world = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldScenes.cs")
world_runtime = text("src/Game.Application/World/WorldSceneCoordinatorRuntime.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
voyage_runtime = text("src/Game.Client/Scripts/VerticalSlice/StageOneVoyageRuntime.cs")
approach = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryApproachRuntime.cs")
interplanetary = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceInterplanetaryTravel.cs")
sim = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationRuntime.cs")
node = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
star_slice = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
globe = text("src/Game.Client/Scripts/VerticalSlice/DetailedPlanetGlobeNode.cs")
env = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldEnvironmentPresentation.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryLandingRecoveryAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetaryLandingRecovery.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
ship_scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
tests = text("tests/ProjectHorizon.Tests/Unit/PlanetaryLandingRecoveryTests.cs")
en = text("src/Game.Client/Content/localization.en.json")
ru = text("src/Game.Client/Content/localization.ru.json")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.4", "0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180"}, "VERSION must be alpha.178.4", f)

# Persistence restore must bypass only the live transition graph, not weaken it.
need("restoreWorldContext: saveData is not null" in voyage and
     "ApplyStageOneVoyageToScene(bool restoreWorldContext = false)" in voyage and
     "restoreFromPersistence: restoreWorldContext" in voyage,
     "voyage restore does not explicitly identify authoritative persistence restore", f)
need("restoreFromPersistence" in world and "_worldSceneCoordinatorNode.Restore(desired)" in world and
     "TASK-178.4 world scene persistence restore PASS" in world,
     "world coordinator lacks restore-safe direct context restoration", f)
need("(WorldSceneKind.Surface, WorldSceneKind.StationInterior)" not in world_runtime and
     "(WorldSceneKind.Orbit, WorldSceneKind.StationInterior)" in world_runtime,
     "live transition graph was weakened instead of adding a persistence restore path", f)

# Orbital scale must make planets dominant, moons separated, and camera able to see it.
need(number(sim, "MinimumPlanetOrbitRadius") >= 6000.0 and
     number(sim, "PlanetOrbitSpacing") >= 5000.0 and
     number(sim, "MinimumMoonOrbitRadius") >= 2500.0 and
     number(sim, "MoonOrbitSpacing") >= 1200.0,
     "planet/moon orbital spacing remains too compressed", f)
need("42000.0" in sim and "26000.0" in sim and "9000.0" in sim and
     "Math.Clamp(resolvedVisualRadius, 9000.0, 28000.0)" in sim,
     "star/planet visual-radius hierarchy is missing", f)
need("profile.RadiusKm * 360.0" in star_slice and
     "Math.Clamp" in star_slice,
     "live planet visual size is not derived from the actual planet environment radius", f)
need("FocusedPlanetSurfaceClearanceMeters" in node and
     "TryGetBodyApproachPoint" in node and
     "displayRadius + (float)clearanceMeters" in node,
     "focused globe placement/near-side approach point contract missing", f)
need("definition.VisualRadius * 1.12" in globe and
     "unshaded: false" in globe and "0.055" in globe and "0.025" in globe,
     "detailed planet still lacks readable shaded atmosphere/cloud presentation", f)
need(number(ship_scene, "far") >= 500000.0 and ship_scene.count("far = ") >= 2,
     "ship cameras cannot retain the expanded planet/system scale", f)

# Two-stage landing: orbital globe envelope -> verified curved surface -> pad.
need("OrbitalEntryClearanceMeters = 220.0" in approach and
     "OrbitalEntryCaptureRadiusMeters = 95.0" in approach and
     number(approach, "MaximumOrbitalEntrySpeed") >= 28.0 and
     "SurfaceApproachAltitudeMeters = 680.0" in approach and
     "IsOrbitalEntryCaptureReady" in approach,
     "planetary entry envelope contract is incomplete", f)
need("TryGetBodyApproachPoint" in interplanetary and
     "OrbitalEntryClearanceMeters" in interplanetary and
     "planetRadius" in interplanetary,
     "interplanetary cruise still flies to planet center instead of a safe approach envelope", f)
need("IsPlanetarySurfaceApproach" in voyage_runtime and
     "PlanetApproachPositionY = PlanetaryApproachRuntime.SurfaceApproachAltitudeMeters" in voyage_runtime,
     "voyage runtime lacks explicit surface-approach phase", f)
need("TryApplyPlanetaryEntryNavigationAssist" in voyage and
     "TryCommitPlanetaryEntryHandoff" in voyage and
     "ArriveAtPlanetaryApproach" in voyage and
     "TASK-178.4 planetary atmosphere entry PASS" in voyage and
     'target={(StageOneVoyage.Location == StageOneVoyageLocation.OutboundFlight ? "station-dock" : StageOneVoyage.IsPlanetarySurfaceApproach ? "planet-pad" : "planet-entry")}' in voyage,
     "K/Enter do not implement a two-stage orbital-entry/surface-landing path", f)
need('"ui.voyage.planet_entry_requires"' in en and '"ui.voyage.planet_entry_requires"' in ru,
     "planet entry feedback is not localized", f)

# Lighting: preserve actual weather frame, shade globe, and smoothly point key light from star.
need("_orbitalHandoffSourceCaptured" in env and
     "environment.BackgroundColor" in env and "environment.AmbientLightColor" in env and
     "environment.FogDensity" in env and "currentDirectional?.LightEnergy" in env,
     "orbital handoff does not capture the actual weather-driven source frame", f)
need("UpdateOrbitalKeyLightDirection" in env and
     "starPosition" in env and "planetPosition" in env and
     "Mathf.Exp" in env and "directional.LookAt" in env,
     "space key-light direction is not star-relative and smoothed", f)
need("UpdateOrbitalKeyLightDirection(delta);" in star_slice,
     "orbital key-light direction is not updated with the star-system simulation", f)

# Aggregate / live / tests / quality gates.
need("TASK-178.4 planetary landing/lighting acceptance" in acceptance and
     all(k in acceptance for k in ("RestoreSafe", "PlanetScale", "MoonClearance", "OrbitalEntry",
                                   "SurfaceHandoff", "VoyagePath", "LightingContinuity")),
     "TASK-178.4 model acceptance is incomplete", f)
need("RunPlanetaryLandingRecoveryAcceptance" in live and
     "entryOutsideGlobe" in live and "landable" in live and
     "MinimumFocusedPlanetAngularRadiusDegrees" in live,
     "TASK-178.4 live acceptance does not verify visible globe/entry/landability", f)
need("RunPlanetaryLandingRecoveryAcceptance();" in slice_cs and
     "_planetaryLandingRecoveryAcceptancePassed == true" in slice_cs and
     "TASK-178.4 (F5)" in slice_cs,
     "TASK-178.4 is not wired into F5/HUD/final-state gating", f)
need("PlanetaryLandingRecovery_ModelContractPasses" in tests and
     "OrbitalEntryCapture_RequiresBothRangeAndSpeed" in tests and
     "PersistedStationContext_CanRestoreWithoutIllegalGameplayEdge" in tests,
     "TASK-178.4 xUnit regression coverage missing", f)
need("TASK-178.4" in readme and "0.1.0-alpha.178.4" in changelog and "TASK-178.4" in status,
     "TASK-178.4 documentation/status evidence missing", f)
validator = "validate-task1784-planetary-landing-lighting.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.4 validator is not enforced in local/CI/release quality gates", f)

if f:
    print("TASK-178.4 PLANETARY LANDING/LIGHTING CONTRACT FAIL:")
    for x in f:
        print(f"- {x}")
    sys.exit(1)

print(
    "TASK-178.4 PLANETARY LANDING/LIGHTING CONTRACT PASS: "
    "restoreSafe=1; liveGraphStrict=1; planetScale=1; moonClearance=1; "
    "orbitalEntry=1; surfaceHandoff=1; twoStageLanding=1; lightingContinuity=1; "
    "starKeyLight=1; f5=1; xunit=1."
)
