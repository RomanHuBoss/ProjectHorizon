#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
fail=[]
def text(path): return (ROOT/path).read_text(encoding='utf-8')
def need(cond,msg):
    if not cond: fail.append(msg)

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must be alpha.196, got {version}')
runtime=text('src/Game.Client/Scripts/VerticalSlice/VegetationRegionRuntime.cs')
integration=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVegetationRegional.cs')
ecologyslice=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
specimen=text('src/Game.Client/Scripts/VerticalSlice/EcologyFloraSpecimenNode.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/VegetationRegionalAcceptance.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/VegetationRegionalTests.cs')

# §11.1 deterministic regional placement remains seed-driven and gets region partitioning.
need('RegionSizeMeters = 32.0' in runtime and 'WorldToRegion' in runtime, 'regional vegetation grid missing')
need('BuildRegionalBatches' in runtime and 'GroupBy' in runtime and 'FloraId' in runtime, 'region+species batching missing')
need('EcologyPlanner.Plan' in text('src/Game.Client/Scripts/VerticalSlice/EcologyAcceptance.cs'), 'seed-driven ecology planner regression missing')
# §11.2 MultiMesh per region/type + LOD + distance cull.
need('CreateLodMesh' in specimen and 'simplified ? 6 : 10' in specimen, 'separate flora LOD geometry missing')
need('Lod0Node' in ecologyslice and 'Lod1Node' in ecologyslice, 'regional LOD nodes not bound')
need('VegetationLodTier.Near' in runtime and 'VegetationLodTier.Mid' in runtime and 'VegetationLodTier.Culled' in runtime, 'near/mid/cull policy missing')
need('SmallObjectCullDistanceMeters = 52.0' in runtime, 'small-object distance culling missing')
need('_worldStreamingCoordinator?.GetDetailAt' in integration and 'WorldStreamingRegionDetail.Simplified' in runtime and 'WorldStreamingRegionDetail.Preload' in runtime, 'TASK-194 residency integration missing')
need('MultiMeshInstance3D' in integration and 'region+species' in integration, 'per-region/type MultiMesh integration missing')
# §11.3 five promotion triggers and demotion.
for token in ('Proximity = 0','Scan = 1','Damage = 2','Harvest = 3','Quest = 4'):
    need(token in runtime, f'promotion trigger missing: {token}')
need('EnsureFloraPromoted(flora, VegetationPromotionReason.Scan)' in ecologyslice, 'scanner promotion path missing')
need('Damaged += OnEcologyFloraDamaged' in integration or 'Damaged += OnEcologyFloraDamaged' in ecologyslice, 'damage promotion path missing')
need('VegetationPromotionReason.Harvest' in ecologyslice, 'harvest promotion path missing')
need('IsFloraQuestRelevant' in integration and 'MaximumQuestPromotions' in runtime, 'quest promotion path missing')
need('ShouldDemote' in runtime and 'questRelevant' in runtime, 'distance demotion/quest pin missing')
# F5/release.
need('RunVegetationRegionalAcceptance();' in slice_cs and 'TASK-196 (F5)' in slice_cs and '_vegetationRegionalAcceptancePassed == true' in slice_cs, 'TASK-196 F5/final gate missing')
need('VegetationRegionalAcceptanceRunner' in acceptance, 'TASK-196 acceptance runner missing')
need('AllSpecPromotionTriggersAreSupported' in tests and 'LodPolicyRespectsDistanceAndResidency' in tests, 'TASK-196 xUnit coverage missing')
need('TASK-196' in text('README.md'), 'README TASK-196 section missing')
need('## [0.1.0-alpha.196]' in text('CHANGELOG.md'), 'CHANGELOG alpha.196 section missing')
need('TASK-196' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-196 missing')
need((ROOT/'docs/REGIONAL_VEGETATION_RUNTIME.md').exists(), 'TASK-196 runtime doc missing')

if fail:
    print('TASK-196 REGIONAL VEGETATION CONTRACT FAIL:')
    for e in fail: print('ERROR:', e)
    sys.exit(1)
print('TASK-196 REGIONAL VEGETATION CONTRACT PASS: partition=region+species; region=32m; multimesh=regional; lod=LOD0/LOD1/cull; smallCull=52m; residency=TASK-194; promotion=proximity+scan+damage+harvest+quest; demotion=distance+quest-pin; f5=1; xunit=1.')
