#!/usr/bin/env python3
"""Static regression gate for TASK-160.1 traversal-safe TASK-126 acceptance."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
fauna = text("src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs")
aerial = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs")
ecology = text("src/Game.Client/Scripts/VerticalSlice/EcologyRuntime.cs")
tests = text("tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs")

need("FaunaBehaviorRuntime.GetDecisionFrequencyHz(distanceMeters)" in ecology and
     "MidLowDistanceMeters = 150.0" in text("src/Game.Client/Scripts/VerticalSlice/FaunaBehaviorRuntime.cs") and
     "return FaunaSimulationTier.Statistical" in text("src/Game.Client/Scripts/VerticalSlice/FaunaBehaviorRuntime.cs"),
     "TASK-198 fauna distance-tier/statistical traversal premise changed; review aerial regression", failures)
need("public bool StepAerialForAcceptance()" in fauna,
     "distance-independent flying-fauna acceptance probe missing", failures)
need("_ = ApplyFlyingSteering(direction * speed, speed);" in fauna,
     "fauna acceptance does not exercise the shared live steering path", failures)
need("if (!Visible || Health <= 0.0)" in fauna and
     "_aerialSteering.RemoveEntity(InstanceId);" in fauna,
     "fauna acceptance may resurrect depleted/dead runtime entities", failures)
need("Vector3 originalVelocity = Velocity;" in fauna and
     '"flying_fauna"' in fauna,
     "fauna acceptance does not preserve gameplay velocity/runtime identity", failures)
need("ExerciseFlyingFaunaForAerialAcceptance" in aerial and
     "_aerialAcceptanceFaunaProbeSamples" in aerial,
     "TASK-126 orchestration is not wired to the fauna probe", failures)
need("_aerialNavigationAcceptanceBaseline = AerialSteering.CreateSnapshot();" in aerial and
     aerial.index("_aerialNavigationAcceptanceBaseline = AerialSteering.CreateSnapshot();") <
     aerial.index("ExerciseFlyingFaunaForAerialAcceptance();"),
     "fauna acceptance samples are not recorded after the TASK-126 baseline", failures)
need("faunaProbeSamples={_aerialAcceptanceFaunaProbeSamples}" in aerial,
     "TASK-126 output lacks forced-fauna probe diagnostics", failures)
need("AerialAcceptanceHasDistanceIndependentFlyingFaunaProbe" in tests and
     "GetUpdateFrequencyHz(160.0)" in tests,
     "xUnit traversal-distance regression missing", failures)

if failures:
    print("TASK-160.1 AERIAL ACCEPTANCE HOTFIX CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-160.1 AERIAL ACCEPTANCE HOTFIX CONTRACT PASS: "
    "distanceTier=10/5/2Hz-to-150m; farTraversal=160m-statistical; faunaProbe=shared-runtime; "
    "statePreserved=1; deadFaunaNotResurrected=1; baselineOrdered=1; "
    "diagnostics=1; xunit=1."
)
