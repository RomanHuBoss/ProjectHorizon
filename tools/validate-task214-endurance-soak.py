#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
fail = []

def text(path: str) -> str:
    p = ROOT / path
    if not p.exists():
        fail.append(f'missing file: {path}')
        return ''
    return p.read_text(encoding='utf-8', errors='replace')

def need(condition: bool, message: str) -> None:
    if not condition:
        fail.append(message)

version = text('VERSION').strip()
need(version in {'0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must preserve alpha.214 or later accepted revision, got {version}')
policy = text('src/Game.Domain/EnduranceSoakPolicy.cs')
runtime = text('src/Game.Client/Scripts/VerticalSlice/EnduranceSoakRuntime.cs')
integration = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEnduranceSoak.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/EnduranceSoakAcceptance.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
terrain = text('src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs')
console = text('src/Game.Client/Scripts/Developer/DeveloperDiagnosticsSuite.cs')
bridge = text('src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/EnduranceSoakTests.cs')

for token in (
    'RequiredCertificationHours = 8.0',
    'HeartbeatIntervalSeconds = 60.0',
    'SyntheticWorkloadIntervalSeconds = 30.0',
    'PersistenceCheckpointIntervalSeconds = 5.0 * 60.0',
    'DatabaseIntegrityIntervalSeconds = 15.0 * 60.0',
    'MaximumQueueStallSeconds = 120.0',
    'MaximumManagedMemoryGrowthBytes = 768L * 1024L * 1024L',
    'MaximumConcurrentDatabaseWriters = 1'):
    need(token in policy, f'endurance policy missing: {token}')

for token in (
    'EnduranceSoakRunState',
    'TerrainFailedJobs',
    'DatabaseMaximumConcurrentWriters',
    'DatabaseIntegrityKnown',
    'MaximumManagedMemoryGrowthBytes',
    'MaximumQueueStallSeconds',
    'CompleteIfCoverageSufficient',
    'CertificationPassed',
    'EnduranceSyntheticWorkloadRuntime',
    'GalaxyNavigationRuntime',
    'WorldStreamingRuntime.BuildPlan',
    'PlanetSurfaceTerrainRuntime.Sample'):
    need(token in runtime, f'endurance runtime missing: {token}')

need('FailedJobs' in terrain and 'CompletedRevision' in terrain,
     'terrain profiler does not expose failure/progress counters to TASK-214')
for token in (
    'TASK-214 eight-hour endurance READY',
    '--endurance-soak=',
    'endurance_soak <start [hours]|status|stop>',
    'task214-endurance-latest.json',
    'previous endurance interruption DETECTED',
    'SaveDatabase.CreateAcceptanceSnapshot',
    'ReadDiagnosticsAsync',
    'integrity_check',
    'primarySaveMutation=0',
    'eight-hour endurance CERTIFICATION PASS',
    'ownerCertification'):
    need(token in integration, f'live endurance integration missing: {token}')
need('endurance_soak' in console and '"endurance_soak" => DeveloperEnduranceSoak(parts)' in bridge,
     'developer endurance command missing')
need('commands=17' in bridge and 'commands=17/17' in bridge,
     'TASK-136 command-count coherence not updated')

for token in (
    'MemoryLeakDetected',
    'QueueStallDetected',
    'DatabaseCorruptionDetected',
    'TerrainFailureDetected',
    'CancellationSafe',
    'ownerCertification=8h-real-time-required'):
    need(token in acceptance, f'F5 harness acceptance missing detector: {token}')
need('InitializeEnduranceSoakRuntime();' in main and
     'UpdateEnduranceSoakRuntime(delta);' in main and
     'RunEnduranceSoakAcceptance();' in main and
     'TASK-214 (F5)' in main and
     '_enduranceSoakAcceptancePassed == true' in main,
     'TASK-214 startup/update/F5/final wiring missing')

for name in (
    'CertificationPolicyRequiresEightRealHours',
    'HarnessAcceptanceDetectsCriticalFailureClasses',
    'QueueWithoutProgressFailsAfterTwoMinutes'):
    need(name in tests, f'TASK-214 xUnit missing: {name}')

need((ROOT / 'docs/EIGHT_HOUR_ENDURANCE_SOAK.md').exists(), 'TASK-214 docs missing')
need((ROOT / 'tools/run-task214-endurance.cmd').exists(), 'Windows endurance launcher missing')
need((ROOT / 'tools/run-task214-endurance.sh').exists(), 'Linux endurance launcher missing')
need('--endurance-soak=8' in text('tools/run-task214-endurance.cmd'), 'Windows launcher is not 8h certification')
need('--endurance-soak=8' in text('tools/run-task214-endurance.sh'), 'Linux launcher is not 8h certification')
need('TASK-214' in text('README.md'), 'README TASK-214 missing')
need('## [0.1.0-alpha.214]' in text('CHANGELOG.md'), 'CHANGELOG alpha.214 missing')
need('TASK-214' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-214 missing')
for path in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task214-endurance-soak.py' in text(path), f'TASK-214 release gate missing: {path}')

if fail:
    print('TASK-214 EIGHT-HOUR ENDURANCE CONTRACT FAIL:')
    for item in fail:
        print('ERROR:', item)
    sys.exit(1)

print('TASK-214 EIGHT-HOUR ENDURANCE CONTRACT PASS: duration=8h-real-time; sample=1s; heartbeat=60s; workload=30s-galaxy+streaming+terrain; isolatedSave=5m; dbIntegrity=15m; memoryGrowth<=768MiB; queueStall<=120s; terrainFailure=hard; dbWriter<=1; recoveryMarker=1; primarySaveMutation=0; f5=harness-only; ownerCertification=required; xunit=1.')
