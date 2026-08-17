#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
failures=[]
def need(cond,msg):
    if not cond: failures.append(msg)
def text(path): return (ROOT/path).read_text(encoding='utf-8')

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.192','0.1.0-alpha.192.1','0.1.0-alpha.194','0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218'}, f'VERSION must be 0.1.0-alpha.192, got {version}')
runtime=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryCaveRuntime.cs')
prefab=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryCavePrefabNode.cs')
integration=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetaryCaves.cs')
poi=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiNode.cs')
water=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetaryWater.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryCaveAcceptance.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/PlanetaryCaveTests.cs')

# PDF §9.9: prefab-only caves, no global procedural cave field, no terrain deformation.
need('RequiredArchetypeCount = 3' in runtime, 'three deterministic cave prefab archetypes missing')
for cave in ('cave.basalt_lava_tube','cave.crystal_grotto','cave.hydrothermal_hollow'):
    need(cave in runtime, f'{cave} missing')
need('GlobalProceduralCaveNetwork => false' in runtime, 'global procedural cave network must remain disabled')
need('TerrainDeformationEnabled => false' in runtime, 'terrain deformation must remain disabled')
need('DepositsPerCave = 3' in runtime and 'cave.deposit.' in runtime, 'stable cave resource-deposit planning missing')
need('PlanetaryCavePrefabNode' in prefab and 'BuildWalkableShell' in prefab, 'collision-backed cave prefab shell missing')
need('PlanetaryCaveExitNode' in prefab and 'CaveExitPortal' in prefab, 'interactive cave exit missing')
need('SalvageResourceNode' in prefab and 'ConfigureDefinition' in prefab, 'cave deposits are not catalog resource objects')
need('CaveMouth' in poi and 'RockArch' in poi, 'surface cave entrance presentation missing')
need('TryEnterPlanetaryCave' in integration and 'TryExitPlanetaryCave' in integration, 'cave entry/exit integration missing')
need('GetSnapshotLogicalPlayerPosition' in integration and 'GetSnapshotLogicalPlayerPosition();' in slice_cs, 'safe exterior save position for cave sessions missing')
need('Session.CollectedNodeIds.Contains(deposit.ResourceNodeId)' in integration, 'cave depletion persistence restoration missing')
need('!IsPlayerInsidePlanetaryCave' in water, 'surface water must be suppressed inside isolated cave prefab')

# Acceptance/F5/release integration.
need('PlanetaryCaveAcceptanceRunner' in acceptance, 'TASK-192 acceptance runner missing')
need('RunPlanetaryCaveAcceptance();' in slice_cs and 'TASK-192 (F5)' in slice_cs and '_planetaryCaveAcceptancePassed == true' in slice_cs, 'TASK-192 F5/final gate missing')
need('CavePlanIsDeterministicAndPrefabOnly' in tests and 'CaveDepositsUseStableUniquePersistentIds' in tests, 'TASK-192 xUnit coverage missing')
need('TASK-192' in text('README.md'), 'README TASK-192 section missing')
need('## [0.1.0-alpha.192]' in text('CHANGELOG.md'), 'CHANGELOG alpha.192 section missing')
need('TASK-192' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-192 missing')
need((ROOT/'docs/PLANETARY_CAVE_PREFABS.md').exists(), 'TASK-192 runtime doc missing')

# Explicitly reject voxel/destructive-terrain implementations in the TASK-192 sources.
combined=(runtime+'\n'+prefab+'\n'+integration).lower()
for forbidden in ('marchingcubes','marching_cubes','voxeldeform','terrainvoxel','digterrain','modifyterrainmesh'):
    need(forbidden not in combined, f'forbidden terrain-modification path found: {forbidden}')

if failures:
    print('TASK-192 PLANETARY CAVE PREFAB CONTRACT FAIL:')
    for f in failures: print('ERROR:',f)
    sys.exit(1)
print('TASK-192 PLANETARY CAVE PREFAB CONTRACT PASS: archetypes=3; prefabOnly=1; globalProcedural=0; terrainDeformation=0; entryExit=1; deposits=3; persistence=1; waterIsolation=1; f5=1; xunit=1.')
