#!/usr/bin/env python3
"""Static regression gate for TASK-178.5 arcade kinematics + orbital collision."""
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
assist = text("src/Game.Client/Scripts/Ship/ArcadeFlightAssistRuntime.cs")
collision = text("src/Game.Client/Scripts/VerticalSlice/OrbitalBodyCollisionRuntime.cs")
node = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
live_collision = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalCollisionRecovery.cs")
model_acceptance = text("src/Game.Client/Scripts/VerticalSlice/SpaceflightCollisionRecoveryAcceptance.cs")
live_acceptance = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSpaceflightCollisionRecovery.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
tests = text("tests/ProjectHorizon.Tests/Unit/SpaceflightCollisionRecoveryTests.cs")
en = text("src/Game.Client/Content/localization.en.json")
ru = text("src/Game.Client/Content/localization.ru.json")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180", "0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3"}, "VERSION must be alpha.178.5 or later", f)

# Default arcade flight must couple translation direction to the ship heading.
need("DefaultVelocityAlignmentRate = 3.2f" in assist and
     "AlignVelocityToShipAxes" in assist and
     "MathF.Exp" in assist and
     "HeadingErrorDegrees" in assist,
     "arcade flight-assist heading-coupling model is missing", f)
need("VelocityAlignmentRate" in controller and
     "ApplyArcadeFlightAssist(command, deltaSeconds);" in controller and
     "AutoStabilizationEnabled" in controller and
     "FlightAssistHeadingErrorDegrees" in controller,
     "ArcadeShipController does not apply heading-coupled velocity each physics tick", f)
need("VelocityAlignmentRate = 3.2" in scene,
     "ArcadeShip scene does not pin the heading-coupling response", f)
need("flightAssist=heading-coupled" in voyage,
     "takeoff/undock diagnostics do not expose heading-coupled manual flight", f)

# Orbital bodies must be continuous swept collision volumes, not visual-only proxies.
need("TrySweepSphere" in collision and
     "CrossedOuterShell" in collision and
     "ShipCollisionRadiusMeters = 4.0f" in collision and
     "SurfaceSafetyMarginMeters = 1.5f" in collision,
     "continuous swept-sphere collision math is incomplete", f)
need("TryGetBodyDisplaySphere" in node and
     "TryGetFirstSolidBodyHit" in node and
     "StarSystemBodyKind.Planet" in node and
     "StarSystemBodyKind.Moon" in node and
     "StarSystemBodyKind.Star" in node,
     "StarSystemSimulationNode does not expose solid planet/moon/star envelopes", f)
need("UpdateOrbitalCollisionRecovery" in live_collision and
     "TryGetFirstSolidBodyHit" in live_collision and
     "TASK-178.5 orbital body collision PASS" in live_collision and
     "ShowApplicationDeathScreen" in live_collision,
     "live orbital collision recovery is missing", f)
need("TryCaptureFreeFlightPlanetEntry" in live_collision and
     "TryGetFirstPlanetEntryShellHit" in live_collision and
     "TryCommitPlanetaryEntryHandoff(automatic: false)" in live_collision and
     "TASK-178.5 free-flight planetary entry PASS" in live_collision,
     "manual free-flight planet approach is not physically connected to surface handoff", f)
need("UpdateStarSystemSimulation(delta);\n        UpdateOrbitalCollisionRecovery();" in slice_cs,
     "orbital collision sweep is not executed after star-system positions update", f)

# Fatal impact strings must remain localized.
need('"ui.death.planet_impact"' in en and '"ui.death.star_impact"' in en and
     '"ui.death.planet_impact"' in ru and '"ui.death.star_impact"' in ru,
     "planet/star impact death reasons are not localized", f)

# Acceptance / regression gates.
need("TASK-178.5 spaceflight kinematics/collision acceptance" in model_acceptance and
     all(token in model_acceptance for token in (
         "HeadingCoupling", "DriftOptOut", "SpeedConservation",
         "SweptPlanetCollision", "HighSpeedTunnelingBlocked", "EntryShellCrossing")),
     "TASK-178.5 model acceptance is incomplete", f)
need("RunSpaceflightCollisionRecoveryAcceptance" in live_acceptance and
     "currentPlanetSphere" in live_acceptance and "liveSweep" in live_acceptance,
     "TASK-178.5 live acceptance is incomplete", f)
need("RunSpaceflightCollisionRecoveryAcceptance();" in slice_cs and
     "_spaceflightCollisionRecoveryAcceptancePassed == true" in slice_cs and
     "TASK-178.5 (F5)" in slice_cs,
     "TASK-178.5 is not wired into F5/final-state gating", f)
need("SpaceflightCollisionRecovery_ModelContractPasses" in tests and
     "SweptSphere_CatchesSegmentThatCrossesEntirePlanetInOneTick" in tests and
     "HeadingAssist_CurvesVelocityTowardTurnedNoseWithoutChangingSpeed" in tests,
     "TASK-178.5 xUnit regression coverage missing", f)
need("TASK-178.5" in readme and "0.1.0-alpha.178.5" in changelog and "TASK-178.5" in status,
     "TASK-178.5 documentation/status evidence missing", f)
validator = "validate-task1785-spaceflight-kinematics-collision.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.5 validator is not enforced in local/CI/release quality gates", f)

if f:
    print("TASK-178.5 SPACEFLIGHT KINEMATICS/COLLISION CONTRACT FAIL:")
    for x in f:
        print(f"- {x}")
    sys.exit(1)

print(
    "TASK-178.5 SPACEFLIGHT KINEMATICS/COLLISION CONTRACT PASS: "
    "headingCoupling=1; inertialOptOut=1; sweptCollision=1; tunnelingBlocked=1; "
    "manualPlanetEntry=1; deathBoundary=1; f5=1; xunit=1."
)
