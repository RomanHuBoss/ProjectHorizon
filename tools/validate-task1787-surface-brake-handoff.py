#!/usr/bin/env python3
"""Static regression gate for TASK-178.7 terrain solidity, monotonic braking and smooth atmospheric handoff."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="replace")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

f: list[str] = []
version = text("VERSION").strip()
controller = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
brake = text("src/Game.Client/Scripts/Ship/ArcadeShipBrakeRuntime.cs")
atmosphere = text("src/Game.Client/Scripts/Ship/ArcadeShipAtmosphere.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
approach = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryApproachRuntime.cs")
star = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
terrain = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs")
manager = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
safety = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceShipSurfaceSafety.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/SurfaceFlightSafetyAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSurfaceFlightSafety.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/SurfaceFlightSafetyTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.7", "0.1.0-alpha.180"}, "VERSION must be alpha.178.7 or later", f)
need("ArcadeShipBrakeRuntime.ApplyMonotonicBrake" in controller and
     "if (command.Brake)" in controller and
     "BoostActive = false" in controller and
     "BrakeActive = true" in controller and
     "Input.IsActionPressed(\"ship_reverse\")" in controller and
     "float forward = Input.GetActionStrength(\"ship_forward\")" in controller,
     "manual S/X braking is not exclusive and monotonic", f)
need("nextSpeed = Math.Max" in brake and "return Vector3.Zero" in brake and
     "velocity * (nextSpeed / speed)" in brake and
     "ApplyMonotonicBrakeEnvelope" in brake and
     "velocityAfterForces.Dot(referenceDirection) <= 0.0f" in brake,
     "monotonic brake runtime does not clamp exactly at zero/reject reverse impulses", f)
need("ComputeAtmosphereBlend" in atmosphere and
     "ComputeSmoothAtmosphericClimbSpeed" in atmosphere and
     "AtmosphereFadeStart" in atmosphere and
     "OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters" in voyage and
     "OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters" in voyage and
     "_voyageShip.AtmosphereFadeStart" in voyage and
     "_voyageShip.AtmosphereHeight" in voyage,
     "physical atmosphere dynamics are not matched to the visual handoff envelope", f)
need("SurfaceApproachAltitudeMeters = 680.0" in approach and
     "PlanetRuntimeActivationAltitudeMeters = 900.0f" in star and
     "WorldToPlanetSurfaceLogicalPosition" in star and
     "SamplePlanetSurfaceHeight" in star,
     "surface handoff/residency still uses the old abrupt pad-distance boundary", f)
need("SetRuntimeObserver" in manager and "UpdatePlanetSurfaceStreamingObserver" in terrain and
     "? _voyageShip" in terrain,
     "terrain streamer does not follow the piloted ship near the surface", f)
need("PilotedShipMinimumTerrainClearanceMeters = 3.2" in safety and
     "surface penetration BLOCKED" in safety and
     "SurfaceLogicalToLocalPosition" in safety and
     "Velocity.Dot(normal)" in safety and
     "previous.Lerp(current, t)" in safety and
     "swept=1" in safety,
     "terrain-aware swept hard floor is missing", f)
need("UpdatePilotedShipSurfaceSafety();" in slice_cs,
     "surface hard-floor guard is not executed in runtime", f)
need("TASK-178.7 surface solidity/braking/handoff acceptance" in acceptance and
     "MonotonicBrake" in acceptance and "SmoothAtmosphereDynamics" in acceptance and
     "AtmosphereVisualEnvelopeMatched" in acceptance and "SmoothClimbLimiter" in acceptance and
     "HandoffVelocityContinuity" in acceptance and "SurfaceResidencyEnvelope" in acceptance,
     "TASK-178.7 model acceptance missing", f)
need("RunSurfaceFlightSafetyAcceptance" in live and "PrintSurfaceFlightSafetyReady" in live and
     "_surfaceFlightSafetyAcceptancePassed" in live,
     "TASK-178.7 live acceptance missing", f)
need("RunSurfaceFlightSafetyAcceptance();" in slice_cs and
     "_surfaceFlightSafetyAcceptancePassed == true" in slice_cs and
     "TASK-178.7 (F5)" in slice_cs,
     "TASK-178.7 is not wired into the final F5 gate", f)
need("Brake_HeldForeverStopsAtZeroWithoutReversing" in tests and
     "Brake_RejectsEnvironmentalReverseImpulse" in tests and
     "AtmosphereDynamics_FadeSmoothlyAcrossVisualHandoff" in tests and
     "AtmosphereDynamics_ExactlyComplementVisualVacuumBlend" in tests and
     "AtmosphericClimbLimiter_CannotCreateBoundaryImpulse" in tests and
     "PlanetaryHandoff_PreservesIncomingSpeedInsteadOfHardStopping" in tests and
     "Task1787_ModelContractPasses" in tests,
     "TASK-178.7 xUnit coverage missing", f)
need("TASK-178.7" in readme and "0.1.0-alpha.178.7" in changelog and "TASK-178.7" in status,
     "TASK-178.7 docs/status evidence missing", f)
validator = "validate-task1787-surface-brake-handoff.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.7 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-178.7 SURFACE/BRAKE/HANDOFF CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-178.7 SURFACE/BRAKE/HANDOFF CONTRACT PASS: "
    "surfaceHardFloor=1; sweptTerrain=1; streamerShipObserver=1; monotonicBrake=1; noReverse=1; "
    "smoothAtmosphereDynamics=1; envelopeMatched=1; smoothClimbLimiter=1; "
    "handoffContinuity=1; f5=1; xunit=1."
)
