#!/usr/bin/env python3
"""Static regression gate for TASK-180.3 virtual-flight-stick and log-integrity hotfix."""
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
assist = text("src/Game.Client/Scripts/Ship/ArcadeFlightAssistRuntime.cs")
controller = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
handoff = text("src/Game.Client/Scripts/VerticalSlice/OrbitalHandoffPresentationRuntime.cs")
surface = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceShipSurfaceSafety.cs")
terrain = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
weather = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetWeather.cs")
world = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs")
world_env = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldEnvironmentPresentation.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/FlightControlLogIntegrityAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceFlightControlLogIntegrity.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
vertical_scene = text("src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn")
tests = text("tests/ProjectHorizon.Tests/Unit/FlightControlLogIntegrityTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
doc = text("docs/FLIGHT_CONTROL_LOG_INTEGRITY.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1", "0.1.0-alpha.186", "0.1.0-alpha.188"}, "VERSION must be alpha.180.3 or alpha.182", f)

# Mouse input must be a persistent virtual control position. A raw delta may move
# the stick but may not directly become a one-frame yaw/pitch command or decay away.
need("StatefulVirtualFlightStickEnabled = true" in controller and
     "AccumulateVirtualFlightStick" in controller and
     "BuildVirtualStickAttitudeCommand" in controller and
     "TASK-180.3 ship virtual flight stick INPUT PASS" in controller and
     "MouseButton.Middle" in controller and
     "_mouseVirtualStick = Vector2.Zero" in controller and
     "DecayMouseFlightInput" not in controller,
     "live ship controller is not a stateful/recenterable virtual flight stick", f)
need("AccumulateVirtualFlightStick" in assist and
     "ApplyVirtualStickResponse" in assist and
     "BuildVirtualStickAttitudeCommand" in assist and
     "float roll = Mathf.Clamp(horizontal" in assist and
     "float yaw = Mathf.Clamp(" in assist and
     "DefaultCoordinatedYawFactor = 0.18f" in assist,
     "virtual-stick response is not roll-dominant with modest coordinated yaw", f)
need(0.03 <= number(scene, "MouseVirtualStickDeadZone") <= 0.08 and
     1.2 <= number(scene, "MouseVirtualStickResponseExponent") <= 2.0 and
     number(scene, "MouseCoordinatedYawFactor") <= 0.25 and
     0.75 <= number(scene, "MouseFlightGain") <= 1.75,
     "virtual-stick tuning is outside the controllable envelope", f)
need('float strafe = Input.GetAxis("ship_strafe_left", "ship_strafe_right")' in controller and
     "MouseTranslationCouplingEnabled = false" in controller and
     controller.count("RotateObjectLocal(") >= 3,
     "mouse attitude is coupled to strafe or full local-axis attitude integration is missing", f)

# Log-derived light-culler guard: keep the enormous orbital scene inside a finite,
# well-conditioned camera frustum and do not let the surface weather/shadow owner
# mutate lighting after PlanetRuntime has been released.
near = number(scene, "near")
far = number(scene, "far")
starfield = number(handoff, "StarfieldRadiusMeters")
need(0.20 <= near <= 2.0 and 200000.0 <= far <= 900000.0 and
     far / near <= 4_500_000.0 and starfield <= far * 0.90,
     "camera/starfield frustum remains ill-conditioned for the light culler", f)
need("surfacePresentationOwned" in weather and
     "kind == WorldSceneKind.Surface" in weather and
     "surfaceLightingOwned" in world and
     "sun.ShadowEnabled = surfaceLightingOwned" in world and
     "surfaceDirectional.ShadowEnabled = _surfaceRuntimeActive" in world_env and
     "directional.ShadowEnabled = false" in world_env,
     "surface weather/shadow ownership can still leak into orbital presentation", f)

# Log-derived terrain/surface guards: eliminate both the historical int.MinValue
# overflow and the latest 3.2m hard-floor warning/recovery chatter.
need(number(surface, "PilotedShipClearanceToleranceMeters") >= 0.10 and
     number(surface, "PilotedShipRecoveryPaddingMeters") >= 0.50 and
     "TASK-180.3 surface floor correction" in surface and
     "GD.PushWarning(\"TASK-178.7 surface penetration BLOCKED" not in surface,
     "surface floor lacks hysteresis/padding or still emits warning spam", f)
need("Math.Abs((long)first.X - second.X)" in terrain and
     "Math.Abs((long)first.Y - second.Y)" in terrain and
     "distance >= int.MaxValue ? int.MaxValue" in terrain,
     "TerrainChunkManager Chebyshev distance can regress to int overflow", f)

need('name="FlightStickCursor"' in vertical_scene and
     'text = "ui.game.flight_stick_cursor"' in vertical_scene and
     'UpdateFlightStickCursor();' in slice_cs and
     'MouseVirtualStick' in slice_cs and
     '_flightStickCursor.Visible = active' in slice_cs,
     "virtual-stick HUD feedback marker missing", f)

need("TASK-180.3 flight-control/log-integrity acceptance" in acceptance and
     "StatefulVirtualStick" in acceptance and "RollDominantHorizontal" in acceptance and
     "StableCameraFrustum" in acceptance and "SurfaceGuardHysteresis" in acceptance and
     "OverflowSafeTerrainDistance" in acceptance,
     "TASK-180.3 model acceptance missing", f)
need("RunFlightControlLogIntegrityAcceptance" in live and
     "PrintFlightControlLogIntegrityReady" in live and
     "RunFlightControlLogIntegrityAcceptance();" in slice_cs and
     "_flightControlLogIntegrityAcceptancePassed == true" in slice_cs and
     "TASK-180.3 (F5)" in slice_cs,
     "TASK-180.3 is not wired into F5/final acceptance", f)
need("VirtualFlightStick_CoreRetainsDeflectionBeforeTask182SpringCentering" in tests and
     "VirtualFlightStick_HorizontalIsRollDominant_VerticalIsPitch" in tests and
     "CameraEnvelope_KeepsStarfieldInsideStableFrustum" in tests,
     "TASK-180.3 xUnit coverage missing", f)
need("TASK-180.3" in readme and ("0.1.0-alpha.180.3" in changelog or "0.1.0-alpha.182" in changelog) and
     "TASK-180.3" in status and "stateful virtual flight stick" in doc.lower(),
     "TASK-180.3 documentation/status missing", f)
validator = "validate-task1803-flight-control-log-integrity.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-180.3 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-180.3 FLIGHT CONTROL / LOG INTEGRITY CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-180.3 FLIGHT CONTROL / LOG INTEGRITY CONTRACT PASS: "
    "virtualStick=stateful-core; horizontal=roll-dominant; vertical=pitch; "
    "recenter=1; frustum=bounded; weatherOwner=surface; surfaceHysteresis=1; "
    "terrainOverflowSafe=1; f5=1; xunit=1."
)
