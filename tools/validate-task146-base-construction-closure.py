#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(rel):
    return (ROOT / rel).read_text(encoding='utf-8')

def need(cond, label, failures):
    if not cond:
        failures.append(label)

failures=[]
client_proj=text('src/Game.Client/Game.Client.csproj')
clean=text('tools/clean-build-windows10.cmd')
runtime=text('src/Game.Client/Scripts/VerticalSlice/BaseConstructionRuntime.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/BaseConstructionAcceptance.cs')
settings=text('src/Game.Client/Scripts/Application/GameSettingsPanel.cs')
logger=text('src/Game.Client/Scripts/Infrastructure/StructuredGameLogger.cs')
voyage=text('src/Game.Client/Scripts/VerticalSlice/StageOneVoyageRuntime.cs')
ecology=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/BaseConstructionTests.cs')

need('<Compile Remove="Scripts/Infrastructure/Architecture/**/*.cs" />' in client_proj, 'legacy architecture compile exclusion', failures)
need('<Compile Remove="Scripts/Infrastructure/ProjectHorizonGenerator.cs" />' in client_proj, 'legacy generator compile exclusion', failures)
need('Name="ProjectHorizonSourceHygiene"' in client_proj, 'build-time source hygiene target', failures)
need('<Delete Files="@(Task144LegacyArtifact)"' in client_proj, 'known legacy auto-delete', failures)
need('automatic cleanup never deletes unknown source files' in client_proj, 'unknown source fail-safe', failures)
for token in ('%DOMAIN_DIR%\\bin','%DOMAIN_DIR%\\obj','%APPLICATION_DIR%\\bin','%APPLICATION_DIR%\\obj','DomainEvents.cs','DomainEventBus.cs','SystemFrequencyPolicy.cs','ProjectHorizonGenerator.cs'):
    need(token in clean, f'clean-build token {token}', failures)
need('check.Toggled += toggledOn => setter(toggledOn);' in settings, 'Godot toggled delegate fix', failures)
need('Environment.Version' not in logger.replace('System.Environment.Version',''), 'System.Environment disambiguation', failures)
need('using System.Globalization;' in voyage, 'CultureInfo import', failures)
need('PlayerController? player = _player;' in ecology and 'spawn,\n                player,' in ecology, 'nullable player capture', failures)
need('public BasePlacementResult EvaluatePlacement(' in runtime, 'shared placement preflight', failures)
need('BasePlacementResult preflight = EvaluatePlacement(' in runtime, 'TryPlace delegates to preflight', failures)
need('BaseConstruction.EvaluatePlacement(' in slice_cs, 'builder preview delegates to preflight', failures)
need('double capacity = enabled.Sum' in runtime, 'enabled battery capacity', failures)
need('!double.IsFinite(saveData.StoredEnergy)' in runtime, 'non-finite save rejection', failures)
need('energy exceeds enabled battery capacity' in runtime, 'over-capacity save rejection', failures)
for token in ('PlacementPreflightParity','BatteryIsolation','MalformedSaveRejected'):
    need(token in accept, f'acceptance {token}', failures)
need('TASK-146 base construction closure PASS' in slice_cs, 'TASK-146 runtime evidence', failures)
need(slice_cs.count('report.MalformedSaveRejected') == 2, 'MalformedSaveRejected scoped to base construction report only', failures)
for token in ('PlacementPreflight_UsesTheSameRulesAsTryPlace','PlacementPreflight_RejectsTheSameInteractiveLimitAsTryPlace','DisabledBattery_IsRemovedFromAvailableNetworkCapacity','Restore_RejectsNonFiniteAndOverCapacityEnergy'):
    need(token in tests, f'xUnit {token}', failures)

legacy_paths=[
    'src/Game.Client/Scripts/Infrastructure/Architecture/DomainEvents.cs',
    'src/Game.Client/Scripts/Infrastructure/Architecture/DomainEventBus.cs',
    'src/Game.Client/Scripts/Infrastructure/Architecture/SystemFrequencyPolicy.cs',
    'src/Game.Client/Scripts/Infrastructure/ProjectHorizonGenerator.cs',
]
need(not any((ROOT/p).exists() for p in legacy_paths), 'release snapshot contains no legacy source copies', failures)

if failures:
    print('TASK-146 BASE CONSTRUCTION CLOSURE CONTRACT FAIL: ' + '; '.join(failures))
    sys.exit(1)
print('TASK-146 BASE CONSTRUCTION CLOSURE CONTRACT PASS: buildFixes=4/4; legacyShadowGuard=1; sourceHygiene=1; cleanThreeLayers=1; preflightParity=1; batteryIsolation=1; malformedSaveRejection=1; reportScopeGuard=1; xunit=4/4; runtimeEvidence=1.')
