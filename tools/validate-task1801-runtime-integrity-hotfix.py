#!/usr/bin/env python3
"""Static regression gate for TASK-180.1 runtime integrity hotfix."""
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
terrain = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
globe = text("src/Game.Client/Scripts/VerticalSlice/DetailedPlanetGlobeNode.cs")
star = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
surface_residency = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
station_scene = text("src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn")
station_sweep = text("src/Game.Client/Scripts/VerticalSlice/OrbitalStationCollisionRuntime.cs")
orbital = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalCollisionRecovery.cs")
safety = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceShipSurfaceSafety.cs")
environment = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldEnvironmentPresentation.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/RuntimeIntegrityAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceRuntimeIntegrity.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/RuntimeIntegrityTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182"}, "VERSION must be alpha.180.1", f)
need('PlanetRuntimeOutboundDeactivationAltitudeMeters = 680.0f' in surface_residency and
     'residencyAltitude' in surface_residency and
     'StageOneVoyageLocation.OutboundFlight' in surface_residency,
     "outbound surface-streamer deactivation hysteresis missing", f)
need('ToLogicalPosition(observer.GlobalPosition)' in terrain and
     'Math.Abs((long)first.X - second.X)' in terrain and
     'Math.Abs((long)first.Y - second.Y)' in terrain,
     "terrain observer switch/saturated Chebyshev fix missing", f)
need('Name = "OpaqueCoreShell"' in globe and
     'radius * 0.985f' in globe and
     globe.count('CullMode = BaseMaterial3D.CullModeEnum.Disabled') >= 3 and
     'OpaqueCoreShell' in globe,
     "closed detailed-planet fallback/two-sided terrain missing", f)
need('StarSystemBodyKind.Planet or' in star and
     'BaseMaterial3D.CullModeEnum.Disabled' in star,
     "two-sided distant planet/moon proxy material missing", f)
for node in (
    'LeftArmCollision', 'RightArmCollision', 'SpineCollision',
    'CentralHubCollision', 'PortRadiatorCollision', 'StarboardRadiatorCollision',
    'UpperAntennaCollision', 'LowerAntennaCollision',
    'HabitationRingCollision00', 'HabitationRingCollision11'):
    need(f'name="{node}"' in station_scene, f"station collider missing: {node}", f)
need(station_scene.count('type="CollisionShape3D" parent="Gameplay/OrbitalStation"') >= 20,
     "orbital station must expose at least 20 compound collision shapes", f)
need('TrySweepExpandedAabb' in station_sweep and
     'TryBlockOrbitalStationSweep(previous, current)' in orbital and
     'TASK-180.1 orbital station collision BLOCKED' in orbital,
     "continuous station anti-tunneling sweep missing", f)
need(('PilotedShipRecoveryPaddingMeters = 0.18' in safety or
      'PilotedShipRecoveryPaddingMeters = 0.65' in safety) and
     'PilotedShipClearanceToleranceMeters' in safety and
     'TASK-180.3 surface floor correction' in safety and
     'GD.PushWarning("TASK-178.7 surface penetration BLOCKED' not in safety,
     "surface-contact hysteresis/de-chatter missing", f)
need('directional_shadow_max_distance = 320.0' in station_scene and
     'directional.ShadowEnabled = false' in environment and
     'surfaceDirectional.ShadowEnabled = _surfaceRuntimeActive' in environment,
     "bounded surface/orbit shadow-frustum policy missing", f)
need('MinimumStationCollisionShapes' in acceptance and
     'planetClosed' in acceptance and 'terrainObserverResolved' in acceptance,
     "TASK-180.1 acceptance model missing", f)
need('RunRuntimeIntegrityAcceptance' in live and 'RUNNING' in live and
     'TASK-180.1 runtime integrity acceptance' in acceptance,
     "TASK-180.1 live acceptance missing", f)
need('RunRuntimeIntegrityAcceptance();' in slice_cs and
     '_runtimeIntegrityAcceptancePassed == true' in slice_cs and
     'TASK-180.1 (F5)' in slice_cs,
     "TASK-180.1 is not wired into final F5 gate", f)
need('Task1801_StationSweepBlocksHighSpeedCenterlineTraversal' in tests and
     'Task1801_StationSweepLeavesDockApproachOutsideCoreFree' in tests and
     'Task1801_RuntimeIntegrityContractRequiresAllPhysicalGuards' in tests,
     "TASK-180.1 xUnit coverage missing", f)
need('TASK-180.1' in readme and '0.1.0-alpha.180.1' in changelog and
     'TASK-180.1' in status,
     "TASK-180.1 documentation/status missing", f)
validator = 'validate-task1801-runtime-integrity-hotfix.py'
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-180.1 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-180.1 RUNTIME INTEGRITY HOTFIX CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-180.1 RUNTIME INTEGRITY HOTFIX CONTRACT PASS: "
    "planetClosed=1; stationCompoundCollision>=20; stationSweep=1; "
    "terrainOverflowGuard=1; surfaceDebounce=1; orbitShadowFrustum=bounded; f5=1; xunit=1."
)
