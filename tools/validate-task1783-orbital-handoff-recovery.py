#!/usr/bin/env python3
"""Static regression gate for TASK-178.3 orbital handoff recovery."""
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
handoff = text("src/Game.Client/Scripts/VerticalSlice/OrbitalHandoffPresentationRuntime.cs")
env_model = text("src/Game.Client/Scripts/VerticalSlice/WorldSceneEnvironmentPresentationRuntime.cs")
env_live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldEnvironmentPresentation.cs")
backdrop = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalBackdrop.cs")
star_system = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
voyage_runtime = text("src/Game.Client/Scripts/VerticalSlice/StageOneVoyageRuntime.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
scene = text("src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/OrbitalHandoffRecoveryAcceptance.cs")
live_acceptance = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalHandoffRecovery.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/OrbitalHandoffRecoveryTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.3", "0.1.0-alpha.178.4", "0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7"}, "VERSION must be alpha.178.3 or later", f)
need("StationDockPositionZ = -1600.0" in voyage_runtime and
     "StationUndockPositionZ = -1582.0" in voyage_runtime,
     "physical station approach regressed to the old near-surface scale", f)
need("PlanetRuntimeActivationRadiusMeters = 260.0f" in star_system,
     "surface runtime overlap was not extended beyond the old 72 m cutoff", f)
need("VacuumBlendStartMeters = 110.0" in handoff and
     "VacuumBlendEndMeters = 620.0" in handoff and
     "StationRevealAltitudeMeters = 220.0" in handoff and
     "StarfieldRevealAltitudeMeters = 145.0" in handoff and
     "ComputeVacuumBlend" in handoff,
     "gradual orbital handoff model is incomplete", f)
need("StarCount = 420" in handoff and number(handoff, "StarfieldRadiusMeters") >= 7200.0,
     "procedural starfield contract missing", f)
need("ambient_light_energy" in env_live and "_orbitalHandoffSourceCaptured" in env_live and
     "environment.BackgroundColor" in env_live and "environment.AmbientLightEnergy" in env_live and
     "handoff.VacuumBlend" in env_live and "fogDensity" in env_live and
     "TASK-178.3 world environment handoff PASS" in env_live,
     "live environment no longer preserves the gradual atmosphere-to-vacuum handoff", f)
need("0.30, 1.00, false" in env_model,
     "vacuum orbit profile remains too dim for readable geometry", f)
need("EnsureOrbitalBackdropRuntime" in backdrop and
     "MultiMeshInstance3D" in backdrop and "ShadingModeEnum.Unshaded" in backdrop and
     "ApplyOrbitalStationVisibility" in backdrop and
     "TASK-178.3 orbital backdrop READY" in backdrop,
     "starfield/station reveal runtime is not implemented", f)
need("UpdateOrbitalBackdropRuntime();" in slice_cs,
     "orbital backdrop runtime is not updated every frame", f)
need("position = Vector3(0, 35, -1600)" in scene and
     "0, 35, -1631)" in scene,
     "scene station/marker positions do not match StageOneVoyageRuntime", f)
need("OrbitalHandoffPresentationRuntime.Evaluate" in voyage and
     "handoff.StationVisible" in voyage,
     "dock marker visibility is not gated by orbital handoff", f)
need("TASK-178.3 orbital handoff recovery acceptance" in acceptance and
     "StationDistance" in acceptance and "GradualEnvironment" in acceptance and
     "VacuumVisibility" in acceptance and "Starfield" in acceptance,
     "TASK-178.3 aggregate acceptance is incomplete", f)
need("RunOrbitalHandoffRecoveryAcceptance();" in slice_cs and
     "_orbitalHandoffRecoveryAcceptancePassed == true" in slice_cs and
     "TASK-178.3 (F5)" in slice_cs and
     "liveStarfield" in live_acceptance and "stationScene" in live_acceptance,
     "TASK-178.3 is not wired into F5/HUD/final-state gating", f)
need("OrbitalHandoff_UsesOverlappingAtmosphereAndVacuumRanges" in tests and
     "VacuumBlend_IsSmoothAndMonotonic" in tests and
     "OrbitalStation_IsNotPartOfNearSurfaceScale" in tests,
     "TASK-178.3 xUnit regression coverage missing", f)
need("TASK-178.3" in readme and "0.1.0-alpha.178.3" in changelog and
     "TASK-178.3" in status,
     "TASK-178.3 documentation/status evidence missing", f)

validator = "validate-task1783-orbital-handoff-recovery.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.3 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-178.3 ORBITAL HANDOFF RECOVERY CONTRACT FAIL:")
    for x in f:
        print(f"- {x}")
    sys.exit(1)

print(
    "TASK-178.3 ORBITAL HANDOFF RECOVERY CONTRACT PASS: "
    "stationScale=1; surfaceOverlap=1; gradualEnvironment=1; starfield=1; "
    "vacuumVisibility=1; stationReveal=1; f5=1; xunit=1."
)
