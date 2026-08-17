#!/usr/bin/env python3
"""Static regression gate for TASK-180.2 stellar/crash/mouse-flight feel hotfix."""
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
world = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs")
world_runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceWorldCompositionRuntime.cs")
star = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
approach = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryApproachRuntime.cs")
orbital = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalCollisionRecovery.cs")
impact = text("src/Game.Client/Scripts/Ship/PlanetaryImpactRuntime.cs")
atmosphere = text("src/Game.Client/Scripts/Ship/ArcadeShipAtmosphere.cs")
surface = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceShipSurfaceSafety.cs")
assist = text("src/Game.Client/Scripts/Ship/ArcadeFlightAssistRuntime.cs")
controller = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/FlightFeelHotfixAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceFlightFeelHotfix.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/FlightFeelHotfixTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1", "0.1.0-alpha.186", "0.1.0-alpha.188", "0.1.0-alpha.192", "0.1.0-alpha.192.1", "0.1.0-alpha.194"}, "VERSION must be alpha.180.2 or later hotfix", f)
need('ShouldRenderSurfaceSun' in world_runtime and
     'worldKind == WorldSceneKind.Surface' in world_runtime and
     'ShouldRenderSurfaceSun(' in world and
     'WorldScenes.Current.Kind' in world,
     "surface-only local sun ownership missing", f)
need('* 900.0f' in world and 'Scale = Vector3.One * 7.5f' in world,
     "surface stellar disc is still a near 180m point proxy", f)
need('case StarSystemBodyKind.Star:' in star and
     'ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded' in star and
     number(star, 'EmissionEnergyMultiplier') >= 7.0,
     "system star is not a strong unshaded emissive body", f)
need(number(approach, 'MaximumManualOrbitalEntrySpeed') <= 60.0 and
     number(approach, 'MaximumManualOrbitalEntrySpeed') < number(approach, 'MaximumOrbitalEntrySpeed') and
     'MaximumManualOrbitalEntrySpeed' in orbital,
     "manual boosted orbital approach cannot bypass safe-entry capture into solid-body crash", f)
need(number(impact, 'SurfaceSafetyMaximumRecoverableInwardSpeed') <= 12.0 and
     number(impact, 'LethalNormalImpactSpeed') <= 18.0 and
     'IsLethalSurfaceImpact' in impact,
     "planetary lethal-impact envelope missing", f)
need('recoverableSurfaceApproach' in atmosphere and
     'PlanetaryImpactRuntime.SurfaceSafetyMaximumRecoverableInwardSpeed' in atmosphere and
     'TryConsumeCollisionImpact' in surface and
     'TASK-180.2 planetary crash IMPACT' in surface and
     'TASK-180.2 planetary crash PENETRATION' in surface and
     'ShowApplicationDeathScreen("ui.death.planet_impact")' in surface,
     "surface safety still makes high-energy planet impacts non-lethal", f)
need('ShipCollisionImpact' in controller and
     'CaptureStrongestCollisionImpact' in controller and
     'velocityBeforeMove' in controller,
     "physical MoveAndSlide impact energy is not captured", f)
need('BuildVirtualStickAttitudeCommand' in assist and
     'float roll = Mathf.Clamp(horizontal' in assist and
     'float yaw = Mathf.Clamp(' in assist and '-horizontal * Mathf.Clamp(coordinatedYawFactor' in assist and
     'mouseAttitude = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand' in controller and
     'StatefulVirtualFlightStickEnabled = true' in controller and
     'MouseAngularResponseMultiplier' in controller,
     "mouse does not drive the superseding stateful roll-dominant attitude controller", f)
need('float strafe = Input.GetAxis("ship_strafe_left", "ship_strafe_right")' in controller and
     'MouseTranslationCouplingEnabled = false' in controller,
     "mouse is still coupled to lateral strafe", f)
need(controller.count('RotateObjectLocal(') >= 3 and
     'FullAttitudeRotationEnabled = true' in controller and
     'Mathf.Clamp(Rotation' not in controller,
     "full pitch/yaw/roll attitude rotation (including loops) is not preserved", f)
need(0.75 <= number(scene, 'MouseFlightGain') <= 1.75 and
     0.03 <= number(scene, 'MouseVirtualStickDeadZone') <= 0.08 and
     1.2 <= number(scene, 'MouseVirtualStickResponseExponent') <= 2.0 and
     number(scene, 'MouseCoordinatedYawFactor') <= 0.25 and
     number(scene, 'MouseAngularResponseMultiplier') >= 3.0,
     "ship scene virtual-stick response envelope is invalid", f)
need('MinimumStarAngularDiameterDegrees' in acceptance and
     'surfaceSunIsolation' in acceptance and
     'manualCrashEnvelope' in acceptance and
     'mouseNoseFirst' in acceptance,
     "TASK-180.2 acceptance model missing", f)
need('RunFlightFeelHotfixAcceptance' in live and
     'TASK-180.2 flight feel hotfix acceptance' in acceptance and
     'RunFlightFeelHotfixAcceptance();' in slice_cs and
     '_flightFeelHotfixAcceptancePassed == true' in slice_cs and
     'TASK-180.2 (F5)' in slice_cs,
     "TASK-180.2 is not wired into final F5 gate", f)
need('MouseAttitude_HorizontalRollDominatesYaw_VerticalPitches' in tests and
     'PlanetImpactPolicy_AllowsPilotErrorRecoveryButKillsHardDive' in tests and
     'SurfaceSun_IsOwnedOnlyBySurfaceWorld' in tests,
     "TASK-180.2 xUnit coverage missing", f)
need('TASK-180.2' in readme and '0.1.0-alpha.180.2' in changelog and 'TASK-180.2' in status,
     "TASK-180.2 documentation/status missing", f)
validator='validate-task1802-flight-feel-hotfix.py'
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-180.2 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-180.2 FLIGHT FEEL HOTFIX CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-180.2 FLIGHT FEEL HOTFIX CONTRACT PASS: "
    "surfaceSun=surface-only; systemStar=emissive; manualCrash=boosted; "
    "surfaceImpact=lethal-envelope; mouse=stateful-roll-dominant/pitch; fullRotation=1; f5=1; xunit=1."
)
