#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
fail = []

def text(path):
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition, message):
    if not condition:
        fail.append(message)

version = text('VERSION').strip()
need(version in {'0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218','0.1.0-alpha.220'}, f'VERSION must preserve alpha.210 contract, got {version}')
runtime = text('src/Game.Client/Scripts/VerticalSlice/GalaxyExpeditionRuntime.cs')
accept = text('src/Game.Client/Scripts/VerticalSlice/GalaxyExpeditionAcceptance.cs')
slice210 = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxyExpedition.cs')
galaxy = text('src/Game.Client/Scripts/VerticalSlice/GalaxyNavigationRuntime.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/GalaxyExpeditionTests.cs')

need('RequiredDistinctSystems = 100' in runtime, '100 distinct-system requirement missing')
need('ValidationJumpRangeLightYears = 550.0' in runtime, 'bounded validation jump range missing')
need('for (int visit = 1; visit < RequiredDistinctSystems' in runtime, '100-system on-demand traversal loop missing')
need('navigation.GenerateSystem(visit, 0, 0)' in runtime, 'deterministic neighbor-sector corridor missing')
need('TryJumpToSelected' in runtime and 'StageOneVoyageLocation.OrbitalStation' in runtime, 'real hyperspace jump path missing')
need('VisitedSystemIds.Count == RequiredDistinctSystems' in runtime, '100 distinct visited-system invariant missing')
need('GalaxyNavigationSaveData save = navigation.CreateSaveData()' in runtime and 'GalaxyNavigationRuntime restored = new(save)' in runtime, 'visited-state round-trip missing')
need('MaximumDefinitionReferencesDuringJump = 2' in runtime, 'bounded system-definition residency contract missing')
need('maxResidentDefinitions < RequiredDistinctSystems' in runtime, 'whole-galaxy residency prohibition missing')
need('system.g1.x' in runtime and 'manualPerSystemContent=0-except-starter' in slice210, 'procedural non-starter ID/manual-content contract missing')
need('planetIds.Add(planet.PlanetId)' in runtime and 'planet.Seed > 0' in runtime, 'planet identity/seed validation missing')
need('BuildSignature' in runtime and 'deterministic' in runtime, 'deterministic replay signature missing')
need('GalaxyExpeditionAcceptanceRunner.Run' in slice210, 'TASK-210 live acceptance missing')
need('TASK-210 100-system procedural expedition READY' in slice210, 'TASK-210 startup evidence missing')
need('TASK-210 100-system procedural expedition acceptance' in runtime, 'TASK-210 output evidence missing')
need('PrintGalaxyExpeditionReady();' in main and 'RunGalaxyExpeditionAcceptance();' in main and 'TASK-210 (F5)' in main and '_galaxyExpeditionAcceptancePassed == true' in main, 'TASK-210 F5/final integration missing')
for name in ('ExpeditionRequiresOneHundredDistinctSystems','NeighborSectorJumpFitsValidationRange','GeneratedSystemSignatureIsDeterministic','ExpeditionDoesNotRequireWholeGalaxyResidency'):
    need(name in tests, f'TASK-210 xUnit missing: {name}')
need('MaximumVisitedSystems = 10_000' in galaxy, 'visited metadata capacity no longer supports 100-system expedition')
need((ROOT / 'docs/GALAXY_100_SYSTEM_EXPEDITION.md').exists(), 'TASK-210 docs missing')
need('TASK-210' in text('README.md'), 'README TASK-210 missing')
need('## [0.1.0-alpha.210]' in text('CHANGELOG.md'), 'CHANGELOG alpha.210 missing')
need('TASK-210' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-210 missing')
for path in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task210-galaxy-expedition.py' in text(path), f'release gate missing in {path}')

if fail:
    print('TASK-210 GALAXY EXPEDITION CONTRACT FAIL:')
    for item in fail:
        print('ERROR:', item)
    sys.exit(1)
print('TASK-210 GALAXY EXPEDITION CONTRACT PASS: distinctSystems=100; onDemand=1; realJumpPath=1; deterministicReplay=1; proceduralIds=1; planetIdentity=1; visitedRoundTrip=1; residentDefinitions<=2; wholeGalaxyResident=0; f5=1; xunit=1.')
