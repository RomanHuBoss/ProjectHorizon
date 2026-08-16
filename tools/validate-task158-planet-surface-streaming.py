#!/usr/bin/env python3
"""Static contract gate for TASK-158 planetary surface streaming foundation."""
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
runtime = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceStreamingRuntime.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceStreamingAcceptance.cs')
manager = text('src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs')
builder = text('src/Game.Client/Scripts/Terrain/TerrainChunkDataBuilder.cs')
chunk = text('src/Game.Client/Scripts/Terrain/TerrainChunk.cs')
slice_terrain = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
nav = text('src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs')
star = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
en = text('src/Game.Client/Content/localization.en.json')
ru = text('src/Game.Client/Content/localization.ru.json')

need('ActiveRadius = 2' in runtime and 'ExpectedActiveChunks = 25' in runtime,
     '5x5 bounded residency contract missing', failures)
need('HighDetailRadius = 1' in runtime and 'ExpectedHighDetailChunks = 9' in runtime and
     'ExpectedLowDetailChunks = 16' in runtime,
     '3x3 LOD0 plus LOD1 ring contract missing', failures)
need('CollisionRadius = 1' in runtime and 'ExpectedCollisionChunks = 9' in runtime,
     'bounded 3x3 collision residency missing', failures)
need('HighDetailResolution = 33' in runtime and 'LowDetailResolution = 17' in runtime and
     'ChunkSizeMeters = 32.0' in runtime,
     'streaming resolution/chunk-size contract missing', failures)
need('BuildPlan(' in runtime and 'StitchMask' in runtime and 'SkirtMask' in runtime,
     'LOD stitch/skirt planning missing', failures)
need('ExpectedRetainedChunkCount' in runtime and 'expectedRetained' in acceptance,
     'exact axial/diagonal residency overlap acceptance missing', failures)
need('BuildGeodesicAddress' in runtime and 'CircumferenceMeters' in runtime and
     'NavigationTraversalExtentMeters = 8_192.0' in runtime,
     'planet-radius addressing/traversal envelope missing', failures)
need('PlanetSurfaceProfile' in manager and 'ConfigurePlanetSurface' in manager and
     'IsStreamingSettled' in manager,
     'verified TerrainChunkManager is not promoted to planet-surface mode', failures)
need('Task.Run' in manager and '_planCancellation' in manager and '_discardedStaleJobs' in manager,
     'background generation/cancellation/stale-result guards missing', failures)
need('Live traversal is collision-safety first' in manager and
     manager.index('EnqueueOperations(promotionOperations);') < manager.index('EnqueueOperations(createOperations);', manager.index('Live traversal is collision-safety first')),
     'live traversal does not prioritize the incoming collision band', failures)
need('PlanetSurfaceTerrainRuntime.SampleHeight' in builder and
     'request.PlanetSurfaceProfile is null' in builder and
     '(request.ChunkX * request.ChunkSize) + localX' in builder,
     'worker does not sample TASK-156 terrain at actual streamed world positions', failures)
need('UsePlanetSurfacePresentation' in chunk and 'PlanetSurfaceBaseColor' in chunk,
     'streamed chunk visual presentation binding missing', failures)
need('EnsurePlanetSurfaceStreaming(profile);' in slice_terrain and
     'PlanetSurfaceStreamer' in slice_terrain and
     'fallback=retired' in slice_terrain,
     'live surface streamer/fallback handoff missing', failures)
need('UpdatePlanetSurfaceStreaming();' in main and
     'RunPlanetSurfaceStreamingAcceptance();' in main and
     'TASK-158 (F5)' in main,
     'TASK-158 process/F5/HUD wiring missing', failures)
need('body is TerrainChunk' in nav and 'NavigationTraversalExtentMeters' in nav,
     'TASK-124 navigation is not promoted beyond old 80x80 terrain or treats streamed ground as obstacle', failures)
need('"PlanetSurfaceStreamer"' in star,
     'streamer is not governed by surface residency suspension', failures)
need('TASK-158 planet surface streaming acceptance' in acceptance and
     'boundedResidency' in acceptance and 'planetAddressing' in acceptance and
     'fullRelief' in acceptance,
     'TASK-158 runtime acceptance incomplete', failures)
for name in (
    'PlanetSurfaceStreaming_PlanIsBoundedAndUsesTwoLods',
    'PlanetSurfaceStreaming_ChunkSamplesAreDeterministicAndSeamSafe',
    'PlanetSurfaceStreaming_TraversalAddressUsesPlanetRadius'):
    need(name in tests, f'xUnit regression missing: {name}', failures)
need('ui.hud.planet_streaming.summary' in en and 'ui.hud.planet_streaming.summary' in ru,
     'RU/EN streaming HUD localization missing', failures)

if failures:
    print('TASK-158 PLANET SURFACE STREAMING CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-158 PLANET SURFACE STREAMING CONTRACT PASS: '
    'active=25; lod0=9; lod1=16; collision=9; chunk=32m; async=1; '
    'cancellation=1; staleGuard=1; stitch=1; skirts=1; terrainSampler=1; '
    'fallbackHandoff=1; navTraversal=1; surfaceResidency=1; addressing=1; '
    'f5=1; xunit=3/3; localization=2/2.'
)
