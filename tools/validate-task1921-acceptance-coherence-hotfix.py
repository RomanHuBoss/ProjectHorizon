#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition: bool, message: str) -> None:
    if not condition:
        print(f'TASK-192.1 ACCEPTANCE COHERENCE HOTFIX CONTRACT FAIL: {message}', file=sys.stderr)
        raise SystemExit(1)

version = text('VERSION').strip()
need(version in {'0.1.0-alpha.192.1','0.1.0-alpha.194','0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218'}, f'VERSION must be 0.1.0-alpha.192.1, got {version}')

runtime = text('src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationRuntime.cs')
star_accept = text('src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationAcceptance.cs')
orbital_accept = text('src/Game.Client/Scripts/VerticalSlice/OrbitalNavigationPresentationAcceptance.cs')
mouse_live = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceOrbitalScaleMouseSurface.cs')
unit = text('tests/ProjectHorizon.Tests/Unit/AcceptanceCoherenceHotfixTests.cs')

need('ResolveRepresentationForDistance' in runtime, 'shared deterministic representation classifier missing')
need('MarkerDistance + 1.0' in star_accept, 'representation acceptance does not exercise Statistical threshold')
need('coverageSnapshot.StatisticalCount > 0' not in star_accept, 'phase-dependent impossible Statistical snapshot requirement remains')
need('moonParentHierarchy' in orbital_accept, 'moon visual hierarchy is not parent-relative')
need('maxMoonVisual * 2.0' not in orbital_accept, 'global smallest-planet/largest-moon comparison remains')
need('SpringCenteredVirtualFlightStickEnabled' in mouse_live, 'live mouse acceptance is not bound to spring-centered controller')
need('MouseInputDecay >= 5.0f' not in mouse_live, 'legacy impulse-decay mouse gate remains')
need('_voyageShip.MouseFlightGain >= 1.0f' in mouse_live, 'current scene mouse gain envelope is not accepted')
need('liveMouseEvidence' in mouse_live, 'runtime evidence should be diagnostic rather than a pre-F5 hard requirement')
need('StarSystemRepresentationClassifier_CoversAllLevelsWithoutOrbitalPhaseDependence' in unit, 'representation regression xUnit missing')
need('StarterSystem_VisualHierarchyComparesEveryMoonWithItsOwnParent' in unit, 'visual hierarchy regression xUnit missing')
need('MouseAcceptanceContract_UsesSpringCenteredVirtualStickArchitecture' in unit, 'mouse architecture regression xUnit missing')

for path, token in [
    ('CHANGELOG.md', '0.1.0-alpha.192.1'),
    ('README.md', 'TASK-192.1'),
    ('REQUIREMENTS_STATUS.md', 'TASK-192.1')]:
    need(token in text(path), f'{path} missing {token}')

print('TASK-192.1 ACCEPTANCE COHERENCE HOTFIX CONTRACT PASS: representation=threshold-classifier; visualHierarchy=parent-relative; mouse=spring-controller; phaseDependentGate=0; legacyDecayGate=0; xunit=3.')
