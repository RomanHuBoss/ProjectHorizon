#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

def need(condition, label, failures):
    if not condition:
        failures.append(label)

failures=[]
runtime=text('src/Game.Application/World/WorldSceneCoordinatorRuntime.cs')
node=text('src/Game.Client/Scripts/VerticalSlice/WorldSceneCoordinatorNode.cs')
slice_world=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldScenes.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
galaxy=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs')
star=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs')
scene=text('src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn')
accept=text('src/Game.Client/Scripts/VerticalSlice/WorldSceneCoordinatorAcceptance.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/WorldSceneCoordinatorTests.cs')
version=text('VERSION').strip()

for token in ('Surface = 0','Orbit = 1','StationInterior = 2','HyperspaceTransit = 3'):
    need(token in runtime, f'context {token}', failures)
for edge in (
    '(WorldSceneKind.Surface, WorldSceneKind.Orbit)',
    '(WorldSceneKind.Orbit, WorldSceneKind.Surface)',
    '(WorldSceneKind.Orbit, WorldSceneKind.StationInterior)',
    '(WorldSceneKind.StationInterior, WorldSceneKind.Orbit)',
    '(WorldSceneKind.StationInterior, WorldSceneKind.HyperspaceTransit)',
    '(WorldSceneKind.HyperspaceTransit, WorldSceneKind.StationInterior)'):
    need(edge in runtime, f'transition edge {edge}', failures)
need('RejectedTransitions++' in runtime, 'illegal transition guard', failures)
need('HyperspaceTransitions++' in runtime, 'hyperspace transition counter', failures)
need('using Godot' not in runtime and 'Godot.' not in runtime, 'application coordinator is Godot-independent', failures)

scene_paths=(
    'SurfaceWorldShell.tscn',
    'OrbitWorldShell.tscn',
    'StationInteriorShell.tscn',
    'HyperspaceTransitShell.tscn')
for name in scene_paths:
    need((ROOT/'src/Game.Client/Scenes/World'/name).exists(), f'packed scene {name}', failures)
    need(name in node, f'coordinator scene path {name}', failures)
need('hostChildren == 1 && validShell' in node, 'single live shell invariant', failures)
need('GetNodeOrNull<Node3D>("Gameplay")' in slice_world, 'gameplay coordinator host binding', failures)
need('new WorldSceneCoordinatorNode' in slice_world and 'gameplay.AddChild(_worldSceneCoordinatorNode);' in slice_world, 'runtime coordinator bootstrap', failures)
need('WorldSceneCoordinatorNode.cs' not in scene and '12_world_scene_coordinator' not in scene, 'gameplay scene has no hard coordinator script dependency', failures)
need('name="WorldSceneCoordinator"' not in scene, 'coordinator is not serialized into gameplay scene', failures)

need('WorldSceneKind.Surface => true' in slice_world, 'surface residency', failures)
need('bool orbitActive = kind == WorldSceneKind.Orbit;' in slice_world, 'orbit residency', failures)
need('WorldSceneKind.StationInterior => false' in slice_world, 'station suspends surface', failures)
need('WorldSceneKind.HyperspaceTransit => false' in slice_world, 'hyperspace suspends surface', failures)
need('SuspendOrbitRuntimeNodes();' in slice_world and 'EnforceOrbitRuntimeSuspended();' in slice_world, 'orbit suspension enforcement', failures)
need('ResolveWorldSceneContext()' in slice_world and 'GalaxyNavigation.CurrentSystem.SystemId' in slice_world, 'persistence-derived context', failures)
need('InitializeWorldSceneCoordinator();' in slice_cs, 'coordinator initialization', failures)
need('SynchronizeWorldSceneCoordinator();' in slice_cs, 'per-frame coordinator synchronization', failures)
need('RunWorldSceneCoordinatorAcceptance();' in slice_cs, 'F5 acceptance hook', failures)
need('TASK-148 (F5)' in slice_cs, 'F5 HUD evidence', failures)

need('BeginWorldHyperspaceTransit();' in galaxy, 'hyperspace begin staging', failures)
need('CompleteWorldHyperspaceTransit(successfulJump: true);' in galaxy, 'hyperspace destination completion', failures)
need('CompleteWorldHyperspaceTransit(successfulJump: false);' in galaxy, 'hyperspace failure rollback', failures)
need('WorldScenes.Current.Kind == WorldSceneKind.Orbit' in star, 'system proxies restricted to Orbit', failures)
need('hyperspaceSystemChange' in accept and 'illegalRejected' in accept, 'runtime acceptance graph coverage', failures)
for token in ('TransitionGraph_CoversSurfaceOrbitStationAndHyperspace','DirectSurfaceToStation_IsRejectedWithoutMutatingContext','ContextIds_AreNormalizedAndBlankIdsAreRejected'):
    need(token in tests, f'xUnit {token}', failures)

# Coordinator derives state from existing voyage/galaxy persistence. No new save key/schema is allowed.
all_save_text='\n'.join(text(p) for p in (
    'src/Game.Client/Scripts/Persistence/SaveGameModels.cs',
    'src/Game.Client/Scripts/Persistence/SaveDatabase.cs',
    'src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs'))
need('world_scene' not in all_save_text.lower(), 'no duplicate world-scene persistence', failures)
need(version == '0.1.0-alpha.148.1', 'VERSION alpha.148.1', failures)

if failures:
    print('TASK-148 WORLD SCENE COORDINATOR CONTRACT FAIL: ' + '; '.join(failures))
    sys.exit(1)
print('TASK-148 WORLD SCENE COORDINATOR CONTRACT PASS: contexts=4/4; packedScenes=4/4; oneResident=1; transitionGraph=1; illegalGuard=1; surfaceResidency=1; orbitResidency=1; stationResidency=1; hyperspaceResidency=1; destinationReload=1; persistenceDerived=1; gameplayLoadSafe=1; runtimeBootstrap=1; f5Acceptance=1; xunit=3/3.')
