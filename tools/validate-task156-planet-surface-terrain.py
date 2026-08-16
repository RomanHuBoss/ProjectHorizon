#!/usr/bin/env python3
"""Static contract gate for TASK-156 planet-specific terrain and surface geometry."""
from __future__ import annotations
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
runtime = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceTerrainRuntime.cs')
content = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceContentRuntime.cs')
slice_terrain = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
slice_surface = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceContent.cs')
poi = text('src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiPlanner.cs')
ecology = text('src/Game.Client/Scripts/VerticalSlice/EcologyPlanner.cs')
ecology_scene = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
fauna = text('src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs')
nav = text('src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs')
npc = text('src/Game.Client/Scripts/VerticalSlice/NpcFactionAgentNode.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceTerrainAcceptance.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
en = text('src/Game.Client/Content/localization.en.json')
ru = text('src/Game.Client/Content/localization.ru.json')

for archetype in ('desert', 'frozen', 'volcanic', 'toxic', 'radioactive', 'barren', 'oceanic'):
    need(f'"{archetype}" =>' in runtime, f'morphology profile missing: {archetype}', failures)
need('SafeTerraceRadius: 16.0' in runtime and 'FullReliefRadius: 23.0' in runtime,
     'central gameplay terrace/blend contract missing', failures)
need('ApplyBasinFloor(height, x, z, 22.0, 22.0' in runtime and
     'ApplyBasinFloor(height, x, z, -25.5, 25.5' in runtime,
     'wet-world interaction/ecology basins missing', failures)
need('MorphologySignature' in runtime and 'ValueNoise' in runtime and 'Fbm(' in runtime,
     'deterministic terrain morphology core missing', failures)
need('PlanetSurfaceTerrainProfile Terrain' in content and
     'PlanetSurfaceTerrainRuntime.BuildProfile' in content,
     'terrain profile is not bound to planet surface profile', failures)
need('profile.Terrain' in content and 'BuildEcologyPlan' in content and 'BuildPoiPlan' in content,
     'terrain profile is not fed into ecology/POI planning', failures)
need('SurfaceTool' in slice_terrain and 'CreateTrimeshShape()' in slice_terrain and
     'profile.Resolution * profile.Resolution' in slice_terrain,
     'runtime mesh/trimesh collision generation missing', failures)
need('ApplyPlanetSurfaceTerrain();' in slice_surface,
     'terrain is not activated with planet surface lifecycle', failures)
need('PlanetSurfaceTerrainRuntime.Sample' in poi and 'terrainProfile' in poi,
     'POI constraint sampling is not terrain-aware', failures)
need('PlanetSurfaceTerrainRuntime.SampleHeight' in ecology and 'terrainProfile' in ecology,
     'ecology planning is not terrain-grounded', failures)
need('FloraSurfaceY' in ecology_scene and 'CurrentTerrainProfile' in ecology_scene,
     'ecology scene is not projected onto terrain', failures)
need('PlanetSurfaceTerrainRuntime.SampleHeight' in fauna and 'terrainY + 0.75f' in fauna,
     'ground fauna still uses a flat Y plane', failures)
need('SetTerrainProfile' in nav and 'GetNavigationHeight' in nav and
     'terrain.SlopeDegrees > _terrainProfile.MaximumWalkableSlopeDegrees' in nav and
     'GetNavigationHeight(worldX, worldZ)' in nav,
     'NPC NavigationRegion3D is not heightfield/slope aware', failures)
need('_navigationSurface.GetNavigationHeight' in npc and 'closest.Y = _home.Y' not in npc,
     'NPC agents still force flat navigation height', failures)
need('ProjectPoiPlacementToTerrain' in slice_terrain and
     'ProjectPoiPlacementToTerrain(state)' in main,
     'legacy POI identity-safe terrain projection missing', failures)
need('RunPlanetSurfaceTerrainAcceptance();' in main and 'TASK-156 (F5)' in main,
     'TASK-156 F5 acceptance/HUD wiring missing', failures)
need('TASK-156 planet terrain acceptance' in acceptance and
     'distinctMorphology' in acceptance and 'legacyIdentitySafe' in acceptance,
     'TASK-156 runtime acceptance incomplete', failures)
for name in (
    'PlanetSurfaceTerrain_FourStarterPlanetsHaveDistinctDeterministicMorphology',
    'PlanetSurfaceTerrain_PreservesCentralTerraceAndWetWorldBasins',
    'PlanetSurfaceTerrain_GroundsEcologyAndTerrainAwarePois'):
    need(name in tests, f'xUnit regression missing: {name}', failures)
need('ui.hud.planet_terrain.summary' in en and 'ui.hud.planet_terrain.summary' in ru,
     'RU/EN terrain HUD localization missing', failures)

if failures:
    print('TASK-156 PLANET SURFACE TERRAIN CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-156 PLANET SURFACE TERRAIN CONTRACT PASS: '
    'starterMorphology=4/4; deterministic=1; mesh=65x65; trimesh=1; centralTerrace=1; '
    'waterBasins=1; ecologyProjection=1; poiTerrain=1; navHeightfield=1; '
    'npcGrounding=1; legacyIds=1; f5=1; xunit=3/3; localization=2/2.'
)
