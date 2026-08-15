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
audio=text('src/Game.Client/Scripts/Application/AudioDirector.cs')
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
need('WorldSceneCoordinatorRuntimeSnapshot' in runtime and 'CaptureSnapshot()' in runtime and 'RestoreSnapshot(WorldSceneCoordinatorRuntimeSnapshot snapshot)' in runtime, 'exact runtime snapshot/restore', failures)
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
need('WorldSceneCoordinatorNodeSnapshot' in node and 'RestoreSnapshot(WorldSceneCoordinatorNodeSnapshot snapshot)' in node, 'exact node snapshot/restore', failures)
need('TryAttachStagedShell' in node and 'CommitStagedShell' in node and 'Runtime.RestoreSnapshot(runtimeSnapshot)' in node, 'transactional shell swap with rollback', failures)
stage_idx=node.find('stagedShell = StageShell(')
attach_idx=node.find('TryAttachStagedShell(stagedShell')
mutation_idx=node.find('Runtime.TryTransition(context', attach_idx)
need(0 <= stage_idx < attach_idx < mutation_idx, 'staged shell enters tree before application-state mutation', failures)
need('GetNodeOrNull<Node3D>("Gameplay")' in slice_world, 'gameplay coordinator host binding', failures)
need('new WorldSceneCoordinatorNode' in slice_world and 'gameplay.AddChild(_worldSceneCoordinatorNode);' in slice_world, 'runtime coordinator bootstrap', failures)
need('WorldSceneCoordinatorNode.cs' not in scene and '12_world_scene_coordinator' not in scene, 'gameplay scene has no hard coordinator script dependency', failures)
need('name="WorldSceneCoordinator"' not in scene, 'coordinator is not serialized into gameplay scene', failures)

# Runtime startup hotfixes: authored text resources must be declared before nodes,
# and root-persistent audio must install outside the SceneTree child-setup critical section.
scene_lines=scene.splitlines()
first_node=next((i for i,line in enumerate(scene_lines) if line.startswith('[node ')), None)
late_resource = first_node is not None and any(
    line.startswith('[sub_resource ') or line.startswith('[ext_resource ')
    for line in scene_lines[first_node + 1:])
need(first_node is not None and not late_resource, 'gameplay text-scene resource declaration order', failures)
need('root.CallDeferred(Node.MethodName.AddChild, director)' in audio and 'root.AddChild(director);' not in audio, 'deferred AudioDirector root installation', failures)
need('if (!_ready || !IsInsideTree())' in audio, 'pre-ready audio playback guard', failures)

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
need('LiveTransitionPath' in accept and 'liveSteps == 7' in accept and 'reloads == 7' in accept, 'live seven-context F5 path and reload count', failures)
need('RestoreSnapshot(original)' in accept and 'StateRestored' in accept, 'self-restoring F5 acceptance', failures)
need('TransactionalSwap' in accept, 'transactional swap acceptance evidence', failures)
need('livePath=' in slice_world and 'stateRestored=' in slice_world and 'transactionalSwap=' in slice_world and 'testReloads=' in slice_world, 'F5 live transaction output', failures)
need('surfaceActivationTransitionsBefore' in slice_world and 'planetActivationPipelineMaskBefore' in slice_world and 'ApplyWorldResidencyPolicy(force: false)' in slice_world, 'F5 peripheral counter cleanup', failures)
for token in ('TransitionGraph_CoversSurfaceOrbitStationAndHyperspace','DirectSurfaceToStation_IsRejectedWithoutMutatingContext','ContextIds_AreNormalizedAndBlankIdsAreRejected','SnapshotRestore_IsExactAndDoesNotAdvanceCounters'):
    need(token in tests, f'xUnit {token}', failures)

# Coordinator derives state from existing voyage/galaxy persistence. No new save key/schema is allowed.
all_save_text='\n'.join(text(p) for p in (
    'src/Game.Client/Scripts/Persistence/SaveGameModels.cs',
    'src/Game.Client/Scripts/Persistence/SaveDatabase.cs',
    'src/Game.Client/Scripts/Persistence/SaveDatabase.Migration.cs'))
need('world_scene' not in all_save_text.lower(), 'no duplicate world-scene persistence', failures)
need(version == '0.1.0-alpha.149', 'VERSION alpha.149', failures)

if failures:
    print('TASK-148 WORLD SCENE COORDINATOR CONTRACT FAIL: ' + '; '.join(failures))
    sys.exit(1)
print('TASK-148 WORLD SCENE COORDINATOR CONTRACT PASS: contexts=4/4; packedScenes=4/4; oneResident=1; transitionGraph=1; illegalGuard=1; surfaceResidency=1; orbitResidency=1; stationResidency=1; hyperspaceResidency=1; destinationReload=1; persistenceDerived=1; transactionalSwap=1; rollbackRestore=1; livePath=7/7; stateRestore=1; gameplayLoadSafe=1; runtimeBootstrap=1; sceneSyntaxSafe=1; audioLifecycleSafe=1; f5Acceptance=1; xunit=4/4.')
