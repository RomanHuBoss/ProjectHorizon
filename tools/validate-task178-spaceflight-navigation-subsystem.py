#!/usr/bin/env python3
"""Static regression gate for TASK-178 spaceflight/navigation subsystem closure."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]


def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="replace")


def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)


failures: list[str] = []
version = text("VERSION").strip()
model = text("src/Game.Client/Scripts/VerticalSlice/SpaceflightNavigationSubsystemAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSpaceflightNavigationSubsystem.cs")
travel = text("src/Game.Client/Scripts/VerticalSlice/InterplanetaryTravelRuntime.cs")
galaxy = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs")
developer = text("src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178", "0.1.0-alpha.178.1", "0.1.0-alpha.178.2", "0.1.0-alpha.178.3", "0.1.0-alpha.178.4", "0.1.0-alpha.178.5"}, "VERSION must be alpha.178/178.1", failures)
need("ExpectedContractCount = 6" in model and
     all(token in model for token in (
         "ShipSystemsContract", "VoyageContract", "GalaxyContract",
         "StarSystemContract", "InterplanetaryContract", "WorldSceneContract")),
     "six normative spaceflight/navigation contracts are not aggregated", failures)
need(all(token in model for token in (
         "ReadinessChain", "FuelChain", "TransitionChain", "PersistenceChain",
         "NavigationIdentity", "BoundedResidency")),
     "cross-contract closure chains are incomplete", failures)
need("IsSelectionConsistentWith" in travel and
     "targetInScope" in travel and
     "InterplanetaryTravelPhase.Cruising" in travel,
     "same-system selection invariant missing from interplanetary runtime", failures)
need("InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);" in galaxy and
     "planetTargetCleared=" in galaxy and "interplanetarySync=" in galaxy,
     "hyperspace transaction does not clear/synchronize planetary navigation", failures)
need("GalaxyNavigation.LoadSystemForDeveloper" in developer and
     "InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation);" in developer,
     "developer system mutation leaves stale planetary navigation state", failures)
need("TASK-178 spaceflight navigation subsystem READY" in live and
     "TASK-178 spaceflight navigation subsystem acceptance" in live and
     "selectionSync=" in live and "worldContext=" in live and
     "starSystemSync=" in live and "pilotControl=" in live and "liveResidency=" in live and
     "FailSpaceflightNavigationSubsystemAcceptance" in live and
     "did not complete" in live,
     "live TASK-178 integration diagnostics/fail-safe missing", failures)
need("RequestSpaceflightNavigationSubsystemAcceptance();" in slice_cs and
     "UpdateSpaceflightNavigationSubsystemAcceptance();" in slice_cs and
     'TASK-178 (F5)' in slice_cs and
     "_spaceflightNavigationSubsystemAcceptancePassed == true" in slice_cs,
     "TASK-178 is not wired into F5/HUD/final acceptance state", failures)
need("InterplanetarySelectionConsistencyRejectsStaleCrossSystemTarget" in tests and
     "SpaceflightNavigationClosureExposesSixNormativeContracts" in tests and
     "SpaceflightNavigationClosureRequiresEveryCrossContractChain" in tests and
     "brokenFuel.FuelChain" in tests,
     "xUnit architecture regressions for TASK-178 missing", failures)
need("TASK-178" in readme and "0.1.0-alpha.178" in changelog and
     "TASK-178" in status,
     "TASK-178 documentation/status/version evidence missing", failures)
validator = "validate-task178-spaceflight-navigation-subsystem.py"
need(validator in quality_sh and validator in quality_cmd and
     validator in ci and validator in release,
     "TASK-178 validator is not enforced in local/CI/release quality gates", failures)

if failures:
    print("TASK-178 SPACEFLIGHT NAVIGATION SUBSYSTEM CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-178 SPACEFLIGHT NAVIGATION SUBSYSTEM CONTRACT PASS: "
    "contracts=6; readinessChain=1; fuelChain=1; transitionChain=1; "
    "persistenceChain=1; navigationIdentity=1; boundedResidency=1; "
    "selectionSync=1; liveRuntime=1; f5=1; xunit=1."
)
