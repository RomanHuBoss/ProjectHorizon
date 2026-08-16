#!/usr/bin/env python3
"""Static regression gate for TASK-174.2 aerial altitude lifecycle acceptance."""
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
evaluator = text("src/Game.Client/Scripts/VerticalSlice/AerialNavigationAcceptance.cs")
aerial = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs")
tests = text("tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs")

need("public bool IsActiveFlyingNavigationParticipant" in fauna and
     "Visible &&" in fauna and "Health > 0.0" in fauna,
     "live flying-fauna lifecycle predicate missing", failures)
need("!IsActiveFlyingNavigationParticipant" in fauna and
     "return true;" in fauna,
     "dead/hidden flying fauna can still poison the altitude envelope", failures)
need("activeFlying = flying" in evaluator and
     ".Where(node => node.IsActiveFlyingNavigationParticipant)" in evaluator,
     "TASK-126 evaluator does not restrict live altitude checks to active fauna", failures)
need("bool altitudeProbe" in evaluator and
     "altitudeProbe &&" in evaluator,
     "TASK-126 evaluator lacks an independent altitude-controller probe", failures)
need("_aerialAltitudeProbe" in aerial and
     "ApplyAltitudeEnvelope(" in aerial and
     "altitudeProbe={(_aerialAltitudeProbe ? 1 : 0)}" in aerial,
     "TASK-126 orchestration/diagnostics lack altitude probe wiring", failures)
need("activeFlying={_ecologyFaunaNodes.Count(node => node.IsActiveFlyingNavigationParticipant)}" in aerial,
     "TASK-126 output lacks active-flying lifecycle diagnostics", failures)
need("AerialAltitudeAcceptanceIgnoresDeadFaunaButStillExercisesController" in tests and
     "after.AltitudeCorrections > before.AltitudeCorrections" in tests,
     "xUnit altitude lifecycle/controller regression missing", failures)

if failures:
    print("TASK-174.2 AERIAL ALTITUDE LIFECYCLE HOTFIX CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-174.2 AERIAL ALTITUDE LIFECYCLE HOTFIX CONTRACT PASS: "
    "deadFaunaExcluded=1; activeFaunaEnvelope=1; altitudeProbe=1; "
    "controllerCounter=1; diagnostics=1; xunit=1."
)
