#!/usr/bin/env python3
"""Static contract gate for TASK-154 planet-scoped Stage 2 surface content."""
from __future__ import annotations
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
surface = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceContentRuntime.cs')
slice_surface = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetSurfaceContent.cs')
slice_terrain = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
ecology_planner = text('src/Game.Client/Scripts/VerticalSlice/EcologyPlanner.cs')
poi_planner = text('src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiPlanner.cs')
ecology_runtime = text('src/Game.Client/Scripts/VerticalSlice/EcologyRuntime.cs')
poi_runtime = text('src/Game.Client/Scripts/VerticalSlice/PlanetaryExplorationRuntime.cs')
models = text('src/Game.Client/Scripts/Persistence/SaveGameModels.cs')
save_db = text('src/Game.Client/Scripts/Persistence/SaveDatabase.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
travel = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceInterplanetaryTravel.cs')
galaxy_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs')
ec_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceContentAcceptance.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
environments = json.loads(text('src/Game.Client/Content/planet_environments.json'))
ecology = json.loads(text('src/Game.Client/Content/ecology.json'))

starter = ['temperate', 'desert', 'frozen', 'volcanic']
archetypes = {item['Archetype']: item for item in environments['Archetypes']}
need(all(name in archetypes for name in starter), 'starter archetype environment data missing', failures)
need(all(archetypes[name]['Landable'] for name in starter), 'starter surface must be landable', failures)
need(len({tuple(archetypes[name]['BiomeIds']) for name in starter}) == 4,
     'starter planets do not expose four distinct biome profiles', failures)
all_biomes = {item['BiomeId'] for item in ecology['Biomes']}
need(all(set(archetypes[name]['BiomeIds']) <= all_biomes for name in starter),
     'starter planet references unknown ecology biome', failures)

need('PlanetSurfaceContentProfile' in surface and 'BuildProfile(' in surface and
     'BuildEcologyPlan(' in surface and 'BuildPoiPlan(' in surface,
     'surface-content orchestration runtime incomplete', failures)
need('profile.Environment.WaterCoverage' in surface and 'profile.Habitability' in surface,
     'ecology planning is not driven by environment/water/habitability', failures)
need('PlanPlanet(' in ecology_planner and 'activeBiomeIds' in ecology_planner and
     'allowAquatic: waterCoverage >= 0.12' in ecology_planner,
     'planet ecology variation/aquatic policy missing', failures)
need('PlanPlanet(' in poi_planner and 'environmentRuntime.SampleBiome' in poi_planner and
     'environmentProfile.WaterCoverage' in poi_planner,
     'POIs are not sampled against real planet climate', failures)
need('long worldSeed' in ecology_runtime and 'string regionKey' in ecology_runtime and
     'long worldSeed' in poi_runtime and 'string regionKey' in poi_runtime,
     'ecology/POI runtimes are not planet-identity aware', failures)

need('PlanetaryExplorationPlanetSaveData' in models and 'EcologyPlanetSaveData' in models and
     'PlanetStates = null' in models,
     'per-planet save archive models/backward-compatible defaults missing', failures)
need('exploration.PlanetStates' in save_db and 'ecology.PlanetStates' in save_db and
     'invalid planet archive state' in save_db,
     'save boundary does not canonicalize/validate planet archives', failures)
need('InitializePlanetSurfaceContentArchives' in slice_surface and
     'CaptureCurrentPlanetSurfaceState' in slice_surface and
     'CreatePlanetaryExplorationArchiveSaveData' in slice_surface and
     'CreateEcologyArchiveSaveData' in slice_surface,
     'surface-content lifecycle/persistence archive integration missing', failures)
need('StarterRepairSnapshotFactory.PlanetId' in slice_surface and
     'EcologyPlanner.Plan(EcologyCatalog)' in slice_surface and
     'PlanetaryPoiPlanner.Plan(PlanetaryPoiCatalog)' in slice_surface,
     'legacy starter-world compatibility path missing', failures)
need('CreatePlanetaryExplorationArchiveSaveData()' in main and
     'CreateEcologyArchiveSaveData()' in main,
     'snapshot creation still writes only current global ecology/POI state', failures)
need('CaptureCurrentPlanetSurfaceState();' in travel and
     'ActivateCurrentPlanetSurfaceContent();' in travel,
     'interplanetary arrival does not switch planet surface state', failures)
need('CaptureCurrentPlanetSurfaceState();' in galaxy_slice and
     'ActivateCurrentPlanetSurfaceContent();' in galaxy_slice,
     'hyperspace lifecycle does not preserve/switch surface state', failures)
need('WaterHabitatEnabled' in ec_slice and 'AquaticHabitat' in ec_slice,
     'dry-world aquatic scene policy missing', failures)
need('Gameplay/WaterPool' in slice_surface and 'WorldEnvironment' in slice_surface and
     'GroundBody/MeshInstance3D' in slice_terrain and 'ApplyPlanetSurfaceTerrain' in slice_terrain,
     'surface visual presentation is not planet-aware', failures)

need('TASK-154 multi-planet surface content acceptance' in acceptance and
     'perPlanetPersistence' in acceptance and 'legacyStarter' in acceptance,
     'TASK-154 runtime acceptance incomplete', failures)
need('RunPlanetSurfaceContentAcceptance();' in main and 'TASK-154 (F5)' in main,
     'TASK-154 is not wired into F5 acceptance/HUD', failures)
for test_name in (
    'PlanetSurfaceContent_VariesAcrossFourStarterPlanets',
    'PlanetSurfaceContent_DryPlanetExcludesAquaticFauna',
    'PlanetSurfaceContent_PoiAndEcologyStateRoundTripByPlanetIdentity'):
    need(test_name in tests, f'xUnit regression missing: {test_name}', failures)

if failures:
    print('TASK-154 MULTI-PLANET SURFACE CONTENT CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-154 MULTI-PLANET SURFACE CONTENT CONTRACT PASS: '
    'starterPlanets=4/4; distinctBiomes=4/4; ecologyClimate=1; aquaticPolicy=1; '
    'poiClimate=1; perPlanetArchives=1; legacyStarter=1; arrivalSwitch=1; '
    'hyperspacePreserve=1; visualSurface=1; f5=1; xunit=3/3.'
)
