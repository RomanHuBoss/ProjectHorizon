#!/usr/bin/env python3
"""Static regression gate for TASK-176.1 terrain-following flying-fauna altitude runtime."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="replace")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
steering = text("src/Game.Client/Scripts/VerticalSlice/AerialSteeringRuntime.cs")
fauna = text("src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs")
aerial = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs")
tests = text("tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs")
version = text("VERSION").strip()

need(version == "0.1.0-alpha.176.1", "VERSION must be alpha.176.1", failures)
need("ClampHorizontalAndVerticalSpeed" in steering and
     "horizontal = horizontal.Normalized() * maximumHorizontalSpeed" in steering and
     "Math.Clamp(localVelocity.Y, -maximumVerticalSpeed, maximumVerticalSpeed)" in steering,
     "independent tangent/vertical speed limiter missing", failures)
need("FlyingMinimumClearanceMeters = 1.6f" in fauna and
     "FlyingMaximumClearanceMeters = 7.2f" in fauna and
     "FlyingMaximumVerticalSpeed = 3.0f" in fauna,
     "single-source flying altitude constants missing", failures)
need("AerialSteeringRuntime.ClampHorizontalAndVerticalSpeed" in fauna and
     "desired = desired.Normalized() * maximumSpeed" not in fauna,
     "flying steering still normalizes away vertical altitude authority", failures)
need("private void EnforceFlyingAltitudeSafety()" in fauna and
     "MoveAndSlide();" in fauna and
     "EnforceFlyingAltitudeSafety();" in fauna,
     "post-physics hard altitude safety envelope missing", failures)
need("zero-Hz AI tier" in fauna and
     fauna.count("EnforceFlyingAltitudeSafety();") >= 3,
     "dormant/frame-transition altitude safety hooks missing", failures)
need("altitudeRange=" in aerial and "altitudeViolations=" in aerial and
     "FlyingAltitudeClearanceMeters" in aerial,
     "TASK-126 altitude failure diagnostics are insufficient", failures)
need("AerialSpeedLimiterPreservesVerticalAuthorityUnderHeavyHorizontalSteering" in tests and
     "FlyingMaximumVerticalSpeed" in tests,
     "xUnit regression for vertical-authority preservation missing", failures)

if failures:
    print("TASK-176.1 AERIAL ALTITUDE RUNTIME HOTFIX CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-176.1 AERIAL ALTITUDE RUNTIME HOTFIX CONTRACT PASS: "
    "splitSpeedLimit=1; verticalAuthority=1; hardEnvelope=1; zeroHzSafety=1; "
    "frameSafety=1; diagnostics=1; xunit=1."
)
