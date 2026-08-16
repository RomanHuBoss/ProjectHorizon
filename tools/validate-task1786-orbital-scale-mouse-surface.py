#!/usr/bin/env python3
"""Static regression gate for TASK-178.6 scale, mouse steering and multi-planet surface activation."""
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
star_slice = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
controller = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
assist = text("src/Game.Client/Scripts/Ship/ArcadeFlightAssistRuntime.cs")
scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
handoff = text("src/Game.Client/Scripts/VerticalSlice/OrbitalHandoffPresentationRuntime.cs")
collision = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalCollisionRecovery.cs")
transfer = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceManualPlanetTransfer.cs")
approach = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryApproachRuntime.cs")
travel = text("src/Game.Client/Scripts/VerticalSlice/InterplanetaryTravelRuntime.cs")
travel_slice = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceInterplanetaryTravel.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/OrbitalScaleMouseSurfaceAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalScaleMouseSurface.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/OrbitalScaleMouseSurfaceTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180", "0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184"}, "VERSION must be alpha.178.6 or later", f)

# Scale: large bodies, large orbital gaps, and focused planet angular dominance.
need(number(sim, "MinimumPlanetOrbitRadius") >= 110000.0 and
     number(sim, "PlanetOrbitSpacing") >= 90000.0 and
     number(sim, "MinimumMoonOrbitRadius") >= 50000.0 and
     number(sim, "MoonOrbitSpacing") >= 15000.0 and
     number(sim, "MinimumMoonSurfaceClearance") >= 25000.0,
     "orbital body spacing is still below TASK-178.6 scale", f)
need("Math.Clamp(resolvedVisualRadius, 9000.0, 28000.0)" in sim and
     "profile.RadiusKm * 360.0" in star_slice and
     "FocusedPlanetSurfaceClearanceMeters" in node and
     "focusState.Definition.VisualRadius * 1.12" in node,
     "planet visual radius / focused angular-size contract missing", f)
need(200000.0 <= number(scene, "far") <= 900000.0 and scene.count("far = ") >= 2 and
     0.20 <= number(scene, "near") <= 2.0 and
     500000.0 <= number(handoff, "StarfieldRadiusMeters") <= number(scene, "far") * 0.90,
     "camera/starfield range is not a bounded large-world frustum", f)

# Mouse steering: input owns a persistent virtual flight stick, not FPS-style mouse deltas.
need("public override void _Input(InputEvent inputEvent)" in controller and
     "ManualInputOwnershipActive" in controller and
     "InputEventMouseMotion" in controller and
     "AccumulateVirtualFlightStick" in controller and
     "GetViewport().SetInputAsHandled();" in controller and
     "SetProcessInput(enabled);" in controller and
     "MouseSteeringSampleCount" in controller and
     "TASK-180.3 ship virtual flight stick INPUT PASS" in controller,
     "ship virtual flight stick is not captured as owned flight input before HUD", f)
need("AccumulateVirtualFlightStick" in assist and
     "BuildVirtualStickAttitudeCommand" in assist and
     "DefaultVirtualStickDeadZone" in assist and
     "DefaultCoordinatedYawFactor" in assist and
     "DecayMouseFlightInput" not in controller and
     0.75 <= number(scene, "MouseFlightGain") <= 1.75,
     "mouse input is not a stateful bounded virtual-stick controller", f)

# Every landable planet entry must preserve the strict world graph and activate content before surface handoff.
need("TryGetFirstPlanetEntryShellHit" in node and
     "TryCaptureFreeFlightPlanetEntry" in collision and
     "ResolveLandablePlanet" in collision and
     "TryCommitManualCrossPlanetEntry" in collision,
     "physical entry shells do not cover arbitrary landable planets", f)
need("WorldSceneKind.InterplanetaryTransit" in transfer and
     "TrySelectPlanetDestination" in transfer and
     "TryCompletePlanetTransfer" in transfer and
     "ActivateCurrentPlanetSurfaceContent();" in transfer and
     "ApplyStageOneVoyageToScene();" in transfer and
     "world=Orbit->InterplanetaryTransit->Orbit" in transfer and
     "flora=" in transfer and "fauna=" in transfer and "pois=" in transfer and "resources=" in transfer,
     "cross-planet physical entry is not transactional/content-complete", f)
need(number(approach, "MaximumOrbitalEntrySpeed") >= 85.0,
     "normal ship cruise speed can still hit a landable planet before surface activation", f)

# The enlarged orbital scale must remain playable: K cruise raises the external
# speed cap far from a planet and reduces it by stopping-distance guidance.
need(number(travel, "CruiseSpeedMetersPerSecond") >= 500.0 and
     number(travel, "AssumedBrakeDecelerationMetersPerSecondSquared") >= 30.0 and
     "CalculateSafeCruiseSpeed" in travel and
     "SpeedLimit" in travel and
     "SetExternalSpeedLimit" in controller and
     "_externalMaxSpeedOverride" in controller and
     "SetExternalSpeedLimit(guidance.SpeedLimit)" in travel_slice,
     "expanded interplanetary spacing lacks scale-aware high-speed K cruise", f)

# Aggregate verifies flora/fauna/POI/resources for every landable starter planet.
need("TASK-178.6 orbital scale/mouse/multi-planet acceptance" in acceptance and
     all(token in acceptance for token in (
         "LandableSurfaceCoverage", "BuildEcologyPlan", "BuildPoiPlan",
         "BuildResourceWindow", "contentReady == landable", "MinimumFlora",
         "MinimumFauna", "MinimumPois", "MinimumResources",
         "PlayableCruise", "CalculateSafeCruiseSpeed")),
     "TASK-178.6 model acceptance does not prove content for every landable planet", f)
need("RunOrbitalScaleMouseSurfaceAcceptance" in live and
     "liveMouse" in live and "liveScale" in live and "mouseSamples" in live,
     "TASK-178.6 live acceptance diagnostics missing", f)
need("RunOrbitalScaleMouseSurfaceAcceptance();" in slice_cs and
     "_orbitalScaleMouseSurfaceAcceptancePassed == true" in slice_cs and
     "TASK-178.6 (F5)" in slice_cs,
     "TASK-178.6 is not wired into F5/final gate", f)

need("MouseSteering_StatefulVirtualStickRetainsDeflectionWithoutMotion" in tests and
     "ExpandedSystem_UsesPlayableScaleAwareCruiseSpeed" in tests and
     "StarterSystem_UsesLargeBodiesAndWideOrbitalSeparation" in tests and
     "EveryLandableStarterPlanet_HasEcologyPoisAndResources" in tests and
     "CrossPlanetSurfaceActivation_KeepsStrictTransitGraph" in tests,
     "TASK-178.6 xUnit coverage missing", f)
need("TASK-178.6" in readme and "0.1.0-alpha.178.6" in changelog and "TASK-178.6" in status,
     "TASK-178.6 docs/status evidence missing", f)
validator = "validate-task1786-orbital-scale-mouse-surface.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.6 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-178.6 ORBITAL SCALE/MOUSE/MULTI-PLANET CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-178.6 ORBITAL SCALE/MOUSE/MULTI-PLANET CONTRACT PASS: "
    "scale=1; mouse=1; cruise=1; shells=1; transferGraph=1; landableContent=1; "
    "safeEntry=1; f5=1; xunit=1."
)
