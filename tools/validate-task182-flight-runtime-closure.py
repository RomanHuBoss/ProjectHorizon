#!/usr/bin/env python3
"""Static regression gate for TASK-182 flight runtime closure and streaming stability."""
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
    match = re.search(rf"{re.escape(name)}\s*=\s*([0-9.]+)", source)
    return float(match.group(1)) if match else float("nan")


f: list[str] = []
version = text("VERSION").strip()
assist = text("src/Game.Client/Scripts/Ship/ArcadeFlightAssistRuntime.cs")
controller = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
atmosphere = text("src/Game.Client/Scripts/Ship/ArcadeShipAtmosphere.cs")
ship_scene = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
terrain = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
planet_terrain = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/FlightRuntimeClosureAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceFlightRuntimeClosure.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/FlightRuntimeClosureTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
doc = text("docs/FLIGHT_RUNTIME_CLOSURE.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1"}, "VERSION must be alpha.182 or later accepted revision", f)

# The TASK-180.3 virtual stick remains the input representation, but live pilot
# input must return to neutral after a short idle hold rather than preserving an
# infinite command. This must happen before ReadManualCommand each physics tick.
need("SpringCenteredVirtualFlightStickEnabled = true" in controller and
     "UpdateVirtualFlightStickSpringCentering(deltaSeconds);" in controller and
     "SpringCenterVirtualFlightStick(" in controller and
     "_mouseVirtualStickIdleSeconds" in controller and
     "_mouseMotionSinceLastPhysics" in controller,
     "live controller does not spring-centre the stateful virtual stick", f)
need(controller.index("UpdateVirtualFlightStickSpringCentering(deltaSeconds);") <
     controller.index("ReadManualCommand();"),
     "spring centering must be applied before reading the manual attitude command", f)
need("DefaultVirtualStickAutoCenterDelaySeconds = 0.08f" in assist and
     "DefaultVirtualStickAutoCenterRate = 5.5f" in assist and
     "SpringCenterVirtualFlightStick" in assist and
     "MathF.Exp" in assist and
     "DefaultVirtualStickDeadZone * 0.70f" in assist,
     "spring-center helper/tuning is missing or does not converge to neutral", f)
need(abs(number(ship_scene, "MouseVirtualStickAutoCenterDelaySeconds") - 0.08) < 0.001 and
     abs(number(ship_scene, "MouseVirtualStickAutoCenterRate") - 5.5) < 0.01,
     "ship scene does not carry TASK-182 spring-centering tuning", f)
need("BuildVirtualStickAttitudeCommand" in controller and
     "MouseTranslationCouplingEnabled = false" in controller and
     'Input.GetAxis("ship_strafe_left", "ship_strafe_right")' in controller,
     "TASK-182 regressed roll/pitch attitude or separated keyboard strafe", f)
need("else if (AutoStabilizationEnabled || command.Brake)" in controller and
     "AngularVelocityLocal = AngularVelocityLocal.MoveToward(" in controller and
     "StabilizationAcceleration * deltaSeconds" in controller,
     "neutral spring return does not hand off to angular stabilization", f)

# Owner log showed EXIT 590.0 -> ENTER 589.9 on adjacent frames. Atmosphere
# presence must use separate enter/exit thresholds rather than one threshold.
need("DefaultAtmospherePresenceEnterBlend = 0.018f" in atmosphere and
     "DefaultAtmospherePresenceExitBlend = 0.004f" in atmosphere and
     "ResolveAtmospherePresence" in atmosphere and
     "currentlyInAtmosphere ? blend > exit : blend >= enter" in atmosphere and
     "InAtmosphere = ResolveAtmospherePresence(" in atmosphere,
     "atmosphere state does not use a Schmitt/hysteresis transition", f)
need(abs(number(ship_scene, "AtmospherePresenceEnterBlend") - 0.018) < 0.001 and
     abs(number(ship_scene, "AtmospherePresenceExitBlend") - 0.004) < 0.001,
     "ship scene atmosphere hysteresis thresholds are missing", f)

# Owner log also showed refresh churn/stale work while traversing quickly and a
# pointless queued=50 revision immediately before PlanetRuntime suspension.
need("RuntimeRefreshCoalescingEnabled" in terrain and
     "MaxCoalescedCenterLagChunks" in terrain and
     "ShouldCoalesceRuntimeRefresh" in terrain and
     "_coalescedRuntimeRefreshSkips++" in terrain and
     "Math.Abs((long)requestedCenter.X - currentCenter.X)" in terrain,
     "terrain streamer lacks bounded adjacent-revision coalescing", f)
need("RuntimeRefreshCoalescingEnabled = true" in planet_terrain and
     "MaxCoalescedCenterLagChunks = 1" in planet_terrain and
     "if (!_surfaceRuntimeActive)" in planet_terrain and
     "UpdatePlanetSurfaceStreamingObserver" in planet_terrain and
     planet_terrain.index("if (!_surfaceRuntimeActive)") <
     planet_terrain.index("CharacterBody3D? observer = StageOneVoyage.Piloted"),
     "inactive PlanetRuntime can still schedule an observer-driven terrain refresh", f)

need("TASK-182 flight-runtime closure acceptance" in acceptance and
     "IdleStickReturnsToNeutral" in acceptance and
     "AtmosphereHysteresis" in acceptance and
     "TerrainRefreshCoalescing" in acceptance and
     "LargeObserverJumpNotCoalesced" in acceptance,
     "TASK-182 model acceptance is incomplete", f)
need("PrintFlightRuntimeClosureReady" in live and
     "RunFlightRuntimeClosureAcceptance" in live and
     "RunFlightRuntimeClosureAcceptance();" in slice_cs and
     "_flightRuntimeClosureAcceptancePassed == true" in slice_cs and
     "TASK-182 (F5)" in slice_cs,
     "TASK-182 is not wired into F5/final acceptance", f)
need("VirtualStick_HoldsBrieflyThenReturnsToNeutral" in tests and
     "AtmospherePresence_UsesHysteresisInsteadOfThresholdChatter" in tests and
     "TerrainRefresh_CoalescesAdjacentBusyRevisionButNotLargeJump" in tests and
     "Acceptance_PassesFlightRuntimeClosureContract" in tests,
     "TASK-182 xUnit coverage is incomplete", f)
need("TASK-182" in readme and "0.1.0-alpha.182" in changelog and
     "TASK-182" in status and "spring-centered" in doc.lower(),
     "TASK-182 documentation/status is missing", f)
validator = "validate-task182-flight-runtime-closure.py"
need(validator in quality_sh and validator in quality_cmd and
     validator in ci and validator in release,
     "TASK-182 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-182 FLIGHT RUNTIME CLOSURE CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-182 FLIGHT RUNTIME CLOSURE CONTRACT PASS: "
    "mouse=stateful+spring-centered; hold=0.08s; return=5.5/s; "
    "atmosphere=hysteretic; stream=coalesced; inactiveObserver=suppressed; "
    "f5=1; xunit=1."
)
