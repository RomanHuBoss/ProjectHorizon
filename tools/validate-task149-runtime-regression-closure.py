#!/usr/bin/env python3
"""Static regression gate for TASK-149.4 runtime failures observed during F5 acceptance."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
frequency = text("src/Game.Domain/Architecture/SystemFrequencyPolicy.cs")
nav = text("src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs")
application = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceApplicationShell.cs")
aerial = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs")
ship = text("src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs")
world = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldScenes.cs")
runner = text("src/Game.Client/Scripts/VerticalSlice/WorldSceneCoordinatorAcceptance.cs")
evaluator = text("src/Game.Client/Scripts/VerticalSlice/AerialNavigationAcceptance.cs")
section38_test = text("tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs")

need("Math.Floor((_accumulator + boundaryTolerance) / _intervalSeconds)" in frequency,
     "frequency gate does not consume elapsed intervals robustly", failures)
need("_accumulator %= _intervalSeconds;\n        return true;" not in frequency,
     "legacy modulo-only frequency gate is still present", failures)
need("Assert.InRange(nearbyTicks, 99, 101)" in section38_test and
     "Assert.InRange(distantTicks, 19, 21)" in section38_test,
     "frequency regression xUnit bounds missing", failures)

need("MapGetIterationId" in nav,
     "NavigationServer iteration guard missing", failures)
need("HasNavigationMapSynchronized()" in nav and
     "_navigationSynchronizationPending" in nav,
     "navigation synchronization state is not tracked", failures)
need("ReadyForQueries =>" in nav and "HasNavigationMapSynchronized();" in nav,
     "navigation queries are not gated by actual map synchronization", failures)

need("PathsReferToSameFile" in application and "Path.GetFullPath" in application,
     "TASK-130 profile path normalization missing", failures)
need("OperatingSystem.IsWindows()" in application,
     "profile path comparison is not platform-aware", failures)

need("StepForAcceptance" in ship and "performMovement: false" in ship,
     "non-moving NPC ship acceptance step missing", failures)
need("IsRuntimeActiveByResidency" in ship and "Node.ProcessModeEnum.Disabled" in ship,
     "NPC ship diagnostics are not residency-aware", failures)
need("ExerciseAerialNavigationDuringWorldAcceptance" in aerial,
     "TASK-126 orbit-resident acceptance observer missing", failures)
need("context.Kind != WorldSceneKind.Orbit" in aerial,
     "NPC ship probe is not restricted to Orbit residency", failures)
need("ExerciseAerialNavigationDuringWorldAcceptance" in world,
     "world-scene runner is not wired to aerial acceptance", failures)
need("Action<WorldSceneContext>? liveStepObserver" in runner and
     "liveStepObserver?.Invoke(context);" in runner,
     "world-scene live step observer hook missing", failures)
need("shipTrafficExpectedActive" in evaluator and "shipResidency" in evaluator,
     "aerial evaluator is not residency-aware", failures)

if failures:
    print("TASK-149.4 RUNTIME REGRESSION CLOSURE FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-149.4 RUNTIME REGRESSION CLOSURE PASS: "
    "frequencyGate=1; navIterationGuard=1; profilePathNormalization=1; "
    "orbitResidentShipProbe=1; residencyAwareAerialAcceptance=1; xunitFrequencyBounds=1."
)
