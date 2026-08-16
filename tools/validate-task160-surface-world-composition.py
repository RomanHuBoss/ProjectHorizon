#!/usr/bin/env python3
"""Static contract gate for TASK-160 planet surface world composition and persistence."""
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
runtime = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceWorldCompositionRuntime.cs')
scene = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs')
domain = text('src/Game.Client/Scripts/VerticalSlice/StarterRepairDomain.cs')
resource = text('src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs')
terrain = text('src/Game.Client/Scripts/Terrain/TerrainChunk.cs')
terrain_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
content = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceContent.cs')
poi = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
poi_node = text('src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiNode.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceWorldCompositionAcceptance.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
en = text('src/Game.Client/Content/localization.en.json')
ru = text('src/Game.Client/Content/localization.ru.json')

need('BuildSkyProfile(' in runtime and 'ResolveStarColor' in runtime and
     'SunElevationDegrees' in runtime and 'FogSunScatter' in runtime,
     'planet/star sky profile derivation missing', failures)
need('ProceduralSkyMaterial' in scene and 'background_mode", 2' in scene and
     'ambient_light_source", 3' in scene and 'reflection_source", 2' in scene,
     'procedural sky/sky ambient/reflection wiring missing', failures)
need('DirectionalLight3D' in scene and 'SunEnergy' in scene and 'sun.LookAt' in scene,
     'visible system-star directional lighting missing', failures)
need('fog_enabled' in scene and 'fog_aerial_perspective' in scene and
     'fog_sun_scatter' in scene,
     'surface atmospheric perspective missing', failures)
need('PlanetSurfaceClouds' in scene and 'CloudClusterCount' in scene and
     'SphereMesh' in scene,
     'visible deterministic cloud layer missing', failures)
need('EmissionEnergyMultiplier = 0.16f' in terrain and
     'EmissionEnergyMultiplier = 0.14f' in terrain_slice,
     'terrain minimum indirect-light floor missing', failures)
need('MaximumResourcesPerChunk = 2' in runtime and 'BuildResourceWindow' in runtime and
     'BuildChunkResources' in runtime and 'StarterReserveRadiusMeters = 28.0' in runtime,
     'chunk-scoped resource generation contract missing', failures)
need('surface_resource.' in runtime and 'BuildResourceNodeId' in runtime and
     'PlanetId' in runtime and 'ChunkX' in runtime and 'ChunkZ' in runtime,
     'planet+chunk+slot stable resource identity missing', failures)
need('FromSnapshotWithDynamicResources' in domain and 'dynamicResourceResolver' in domain,
     'dynamic resource cold-restore support missing', failures)
need('ResolveDynamicResourceBinding' in scene and 'Session.CollectedNodeIds.Contains' in scene and
     'persistence=seed+deltas' in scene,
     'depleted dynamic resource suppression/restore wiring missing', failures)
need('SetRuntimeSuppressed' in resource and 'legacyFixtures=hidden' in scene and
     'salvage.alpha' in scene and 'salvage.gamma' in scene,
     'legacy catalog resource fixtures are not hidden from live gameplay safely', failures)
need('BuildPoiPresentationPosition' in runtime and 'PositionX = worldX' in poi and
     'PositionZ = worldZ' in poi,
     'legacy POI live presentation is still concentrated in the starter pad', failures)
need('SetRuntimeResident' in poi_node and 'UpdatePlanetaryPoiResidency' in scene and
     'ActiveRadius' in scene,
     'distributed POIs are not bounded to the active terrain window', failures)
need('InitializePlanetSurfaceWorldComposition();' in main and
     'UpdatePlanetSurfaceWorldComposition(delta);' in main and
     'RunPlanetSurfaceWorldCompositionAcceptance();' in main and
     'TASK-160 (F5)' in main,
     'TASK-160 lifecycle/F5/HUD wiring missing', failures)
need('_planetSurfaceWorldCompositionInitialized' in content and
     'ApplyPlanetSurfaceWorldComposition();' in content,
     'planet switch does not refresh sky/resources/composition', failures)
need('TASK-160 planet surface world composition acceptance' in acceptance and
     'coldRestoreDepletion' in acceptance and 'untouchedDeltaEmpty' in acceptance,
     'TASK-160 runtime acceptance missing persistence invariants', failures)
for name in (
    'PlanetSurfaceWorldComposition_SkyProfilesExposeStarAtmosphereAndCloudPolicy',
    'PlanetSurfaceWorldComposition_ResourcesAreDeterministicDistributedAndPlanetScoped',
    'PlanetSurfaceWorldComposition_DynamicDepletionSurvivesColdRestoreWithoutUntouchedDeltas'):
    need(name in tests, f'xUnit regression missing: {name}', failures)
need('ui.hud.world_composition.summary' in en and 'ui.hud.world_composition.summary' in ru,
     'RU/EN world-composition HUD localization missing', failures)

if failures:
    print('TASK-160 SURFACE WORLD COMPOSITION CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-160 SURFACE WORLD COMPOSITION CONTRACT PASS: '
    'sky=procedural; star=system-bound; atmosphere=fog+aerial; clouds=deterministic; '
    'terrainLightFloor=1; resources=chunk-scoped; starterReserve=28m; '
    'resourceIdentity=planet+chunk+slot; depletionPersistence=seed+deltas; '
    'legacyFixtures=hidden; poiSpread=1; poiResidency=bounded; f5=1; xunit=3/3; localization=2/2.'
)
