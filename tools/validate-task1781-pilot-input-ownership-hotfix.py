#!/usr/bin/env python3
"""Static regression gate for TASK-178.1 pilot input ownership hotfix."""
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
ship = text("src/Game.Client/Scripts/Ship/ArcadeShipController.cs")
voyage = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs")
closure = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSpaceflightNavigationSubsystem.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.178.1", "0.1.0-alpha.178.2", "0.1.0-alpha.178.3", "0.1.0-alpha.178.4", "0.1.0-alpha.178.5", "0.1.0-alpha.178.6", "0.1.0-alpha.178.7", "0.1.0-alpha.180", "0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1", "0.1.0-alpha.186", "0.1.0-alpha.188", "0.1.0-alpha.190"}, "VERSION must be alpha.178.1 or later", f)
need("SetParkedControlLock" in ship and
     "ParkedControlLocked" in ship and
     "ManualInputOwnershipActive" in ship and
     "SetPhysicsProcess(false)" in ship and
     "ClearExternalCommand();" in ship,
     "ship controller has no explicit parked/manual input-ownership state", f)
need("_voyageShip.SetParkedControlLock(parked);" in voyage and
     "SetExternalCommand(ShipControlCommand.Neutral)" not in voyage,
     "parked voyage still relies on neutral external control instead of physics lock", f)
need(voyage.count("_voyageNavigationAssist = false;") >= 4 and
     "navigationAssist=0" in voyage and
     "manualControl=" in voyage and
     "externalControl=" in voyage,
     "takeoff/undock do not default to manual ownership or lack diagnostics", f)
need("pilotControl=" in closure and
     "live.PilotControl" in closure and
     "ManualInputOwnershipActive" in closure and
     "ParkedControlLocked" in closure and
     "live=8/8" in closure,
     "TASK-178 live closure does not enforce pilot-control ownership", f)
need("TASK-178.1" in readme and "0.1.0-alpha.178.1" in changelog and "TASK-178.1" in status,
     "TASK-178.1 documentation/status/version evidence missing", f)
validator = "validate-task1781-pilot-input-ownership-hotfix.py"
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-178.1 validator is not enforced in local/CI/release quality gates", f)

if f:
    print("TASK-178.1 PILOT INPUT OWNERSHIP HOTFIX CONTRACT FAIL:")
    for x in f:
        print(f"- {x}")
    sys.exit(1)

print(
    "TASK-178.1 PILOT INPUT OWNERSHIP HOTFIX CONTRACT PASS: "
    "parkedLock=1; noNeutralTakeover=1; manualTakeoff=1; manualUndock=1; "
    "pilotControlInvariant=1; diagnostics=1; qualityGate=1."
)
