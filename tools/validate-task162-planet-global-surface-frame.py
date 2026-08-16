#!/usr/bin/env python3
"""Static contract gate for TASK-162 planet-global surface frame / floating origin."""
from __future__ import annotations
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
runtime = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceFrameRuntime.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceFrameAcceptance.cs')
slice_frame = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceFrame.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
terrain_manager = text('src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs')
terrain_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
world = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs')
ecology = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
nav = text('src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs')
planet_map = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetMap.cs')
voyage = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs')
star_system = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs')
aerial = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAerialNavigation.cs')
npc_agent = text('src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs')
npc_ship = text('src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs')
fauna = text('src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')

need('RebaseCellSizeMeters = 4096.0' in runtime and
     'RebaseThresholdMeters = 2048.0' in runtime and
     'RestoreAtLogicalPosition' in runtime and 'PlanRebase' in runtime,
     'bounded double-precision logical frame/rebase runtime missing', failures)
need('UpdatePlanetSurfaceFrame();' in main and
     'RunPlanetSurfaceFrameAcceptance();' in main and
     'TASK-162 (F5)' in main,
     'TASK-162 lifecycle/F5/HUD wiring missing', failures)
need('GetPlanetSurfaceLogicalPlayerPosition' in slice_frame and
     'ApplyPlanetSurfaceFrameTransforms' in slice_frame and
     'TASK-162 planet surface REBASE' in slice_frame,
     'live scene rebase/diagnostics missing', failures)
need('SetLogicalSurfaceOrigin' in terrain_manager and
     'BuildLocalChunkPosition' in terrain_manager and
     'ToLogicalPosition(_player.GlobalPosition)' in terrain_manager,
     'terrain streamer is not separated into logical chunk and bounded local frame', failures)
need('SetLogicalSurfaceOrigin' in terrain_slice and
     'logicalPlayer.EastMeters' in terrain_slice and
     'logicalPlayer.NorthMeters' in terrain_slice and
     'BuildPlanetTerrainMesh(' in terrain_slice and
     'logicalCenterEastMeters' in terrain_slice and
     'logicalCenterNorthMeters' in terrain_slice,
     'terrain/geodesic/fallback integration does not use logical coordinates', failures)
need('ground.Position = Vector3.Zero' in slice_frame,
     'fallback GroundBody is not kept in bounded local frame', failures)
need('GetPlanetSurfaceLogicalPlayerPosition' in world and
     'WorldToPlanetSurfaceLogicalPosition' in world and
     '(GetNodeOrNull<Node3D>("Gameplay") ?? this).AddChild' in world,
     'world composition/resources/POI residency are not frame-aware', failures)
need('ToSurfaceLogical' in nav and
     'PlanetLogicalHalfExtentMeters' in nav and
     'NavigationTraversalExtentMeters' in nav,
     'NPC navigation is not frame-aware across long traversal', failures)
need(ecology.count('logicalObserver = WorldToPlanetSurfaceLogicalPosition(observer)') >= 2,
     'ecology flora promotion/scanner still mixes physical and logical coordinates', failures)
need('ApplyPlanetSurfaceOriginShiftToRuntimeCaches' in slice_frame and
     'ApplyWorldOriginShift(worldShift)' in slice_frame and
     'RefreshAerialNavigationEnvironment();' in slice_frame,
     'live rebase does not update absolute runtime caches', failures)
need('SurfaceGlobalToLocal' in npc_agent and
     'SurfaceLocalToGlobal' in npc_agent and
     'ApplyWorldOriginShift(Vector3 worldShift)' in npc_agent,
     'ground NPC home/navigation cache is not frame-aware', failures)
need('ApplyWorldOriginShift(Vector3 worldShift)' in npc_ship and
     '_route[index] -= worldShift' in npc_ship and
     'gameplay?.ToGlobal' in aerial,
     'aerial NPC routes/authored POIs are not frame-aware', failures)
need('TerritoryCenterGlobal()' in fauna and
     'ApplyWorldOriginShift()' in fauna,
     'fauna territory/aerial cache is not frame-aware', failures)
need('GetPlanetSurfaceLogicalPlayerPosition' in planet_map,
     'planet map does not read logical surface coordinates', failures)
need('WorldToPlanetSurfaceLogicalPosition(' in voyage and
     voyage.count('SurfaceLogicalToLocalPosition(') >= 5 and
     'SurfaceLogicalToLocalPosition(' in star_system,
     'Stage-1 voyage/star-system physical targets are not normalized through the logical frame', failures)
need('playerPosition.EastMeters' in main and
     'RestorePlanetSurfaceFrameAtLogicalPosition' in main and
     'ResetPlanetSurfaceFrameForCurrentPlanet' in main and
     'voyagePosition.PositionX' in main and
     'SurfaceLogicalToLocalPosition' in main,
     'save/load does not persist logical X/Z through rebase', failures)
need('TASK-162 planet-global surface frame acceptance' in acceptance and
     'chunkIdentity' in acceptance and 'coldRestore' in acceptance and
     'planetReset' in acceptance,
     'TASK-162 deterministic runtime acceptance invariants missing', failures)
for name in (
    'PlanetSurfaceFrame_RebaseKeepsLocalCoordinatesBoundedAndLogicalPositionContinuous',
    'PlanetSurfaceFrame_ColdRestorePreservesChunkIdentity',
    'PlanetSurfaceFrame_AcceptanceCoversLongTraversalRestoreAndPlanetReset'):
    need(name in tests, f'xUnit regression missing: {name}', failures)

if failures:
    print('TASK-162 PLANET GLOBAL SURFACE FRAME CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-162 PLANET GLOBAL SURFACE FRAME CONTRACT PASS: '
    'logical=double; cell=4096m; threshold=2048m; scene=bounded-local; '
    'terrainChunks=logical; resources=logical; poi=logical; ecology=frame-aware; nav=frame-aware; '
    'map=logical; voyage=frame-aware; fallback=logical-centered; '
    'saveRestore=logical-xz; f5=1; xunit=3/3.'
)
