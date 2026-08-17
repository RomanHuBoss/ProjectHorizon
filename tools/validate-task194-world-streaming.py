#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
failures=[]
def text(path): return (ROOT/path).read_text(encoding='utf-8')
def need(cond,msg):
    if not cond: failures.append(msg)

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.194','0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218'}, f'VERSION must preserve alpha.194 or later accepted revision, got {version}')
runtime=text('src/Game.Client/Scripts/VerticalSlice/WorldStreamingRuntime.cs')
node=text('src/Game.Client/Scripts/VerticalSlice/WorldStreamingCoordinatorNode.cs')
integration=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldStreaming.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/WorldStreamingAcceptance.cs')
terrain=text('src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs')
terrain_binding=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/WorldStreamingTests.cs')

# PDF §10.1 active zones.
need('OnFootFullDetailRadiusMeters = 2_000.0' in runtime, 'on-foot 2 km active zone missing')
need('GroundVehicleFullDetailRadiusMeters = 5_000.0' in runtime, 'ground 5 km active zone missing')
need('AtmosphericFlightFullDetailRadiusMeters = 15_000.0' in runtime, 'atmospheric 15 km active zone missing')
need('WorldStreamingRegionDetail.Simplified' in runtime and 'WorldStreamingRegionDetail.Preload' in runtime, 'simplified/preload macro regions missing')

# PDF §10.2 six priorities.
for token in ('PlayerRegion = 1','DirectionOfMovement = 2','CollisionRegion = 3','VisibleRegion = 4','FarRegion = 5','PreGeneration = 6'):
    need(token in runtime, f'priority missing: {token}')
need('ResolveDesiredPriority' in terrain and 'return 1;' in terrain and 'return 2;' in terrain and 'return 3;' in terrain and 'return 4;' in terrain, 'micro terrain priority ordering missing')

# PDF §10.3 worker/data-only policy + cancellation.
need('Math.Max(1, Math.Min(4, logicalProcessorCount - 2))' in runtime, 'worker-count formula missing')
need('CancellationToken cancellationToken' in runtime and 'ThrowIfCancellationRequested' in runtime, 'cancellable background plan missing')
need('Task.Run' in node and 'WorldStreamingRuntime.BuildPlan' in node, 'background macro planning missing')
need('Node3D' not in runtime and 'MeshInstance3D' not in runtime and 'SceneTree' not in runtime, 'data-only worker runtime must not depend on Godot scene API')

# PDF §10.4 budgets.
need('RegularMainThreadBudgetMilliseconds = 2.0' in runtime, '2 ms regular budget missing')
need('ForcedPreloadMainThreadBudgetMilliseconds = 5.0' in runtime, '5 ms forced budget missing')
need('LoadingScreenMainThreadBudgetMilliseconds = 10.0' in runtime, '10 ms loading budget missing')
need('Stopwatch sliceStopwatch' in terrain and 'timeBudgetMilliseconds' in terrain, 'micro streamer is not time-sliced')
need('MainThreadBudgetMilliseconds = 2.0f' in terrain_binding and 'ForcedPreloadBudgetMilliseconds = 5.0f' in terrain_binding and 'LoadingScreenBudgetMilliseconds = 10.0f' in terrain_binding, 'production terrain budget binding missing')

# Runtime/F5/release integration.
need('InitializeWorldStreamingRuntime();' in slice_cs and 'UpdateWorldStreamingRuntime(delta);' in slice_cs, 'live world-streaming integration missing')
need('RunWorldStreamingAcceptance();' in slice_cs and 'TASK-194 (F5)' in slice_cs and '_worldStreamingAcceptancePassed == true' in slice_cs, 'TASK-194 F5/final gate missing')
need('WorldStreamingAcceptanceRunner' in acceptance, 'TASK-194 acceptance runner missing')
need('MovingPlanContainsAllSixPriorityClasses' in tests and 'BackgroundPlanSupportsCancellation' in tests, 'TASK-194 xUnit coverage missing')
need('TASK-194' in text('README.md'), 'README TASK-194 section missing')
need('## [0.1.0-alpha.194]' in text('CHANGELOG.md'), 'CHANGELOG alpha.194 section missing')
need('TASK-194' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-194 missing')
need((ROOT/'docs/WORLD_STREAMING_RUNTIME.md').exists(), 'TASK-194 runtime doc missing')

if failures:
    print('TASK-194 WORLD STREAMING CONTRACT FAIL:')
    for f in failures: print('ERROR:',f)
    sys.exit(1)
print('TASK-194 WORLD STREAMING CONTRACT PASS: activeZones=2/5/15km; macroRegion=1km; priorities=6/6; simplified=1; preload=1; workerPolicy=cpu-2-clamped-1..4; background=data-only+cancellable; budgets=2/5/10ms; microTerrain=time-sliced; f5=1; xunit=1.')
