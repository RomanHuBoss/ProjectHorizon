#!/usr/bin/env python3
from pathlib import Path
import struct, sys
ROOT=Path(__file__).resolve().parents[1]
fail=[]
def text(p): return (ROOT/p).read_text(encoding='utf-8')
def need(c,m):
    if not c: fail.append(m)
version=text('VERSION').strip()
need(version in {'0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218','0.1.0-alpha.220'}, f'VERSION must preserve alpha.208 contract, got {version}')
policy=text('src/Game.Application/Presentation/TextureLightingBudgetPolicy.cs')
slice208=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceTextureLightingBudgets.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/TextureLightingBudgetAcceptance.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/TextureLightingBudgetPolicyTests.cs')
station=text('src/Game.Client/Scenes/World/StationInteriorShell.tscn')

for token in (
    'PlayerCharacterMaxTextureDimension = 2048',
    'LargeShipMaxTextureDimension = 2048',
    'NpcMaxTextureDimension = 2048',
    'LargeBuildingMaxTextureDimension = 2048',
    'OrdinaryObjectMaxTextureDimension = 1024',
    'PlantMaxTextureDimension = 1024',
    'UiIconMaxTextureDimension = 512',
    'TiledSurfaceMaxTextureDimension = 2048'):
    need(token in policy, f'texture class budget missing: {token}')
for token in ('TextureAtlasesRequired = true','ReusableMaterialsRequired = true','MaximumProductionMaterialsPerAsset = 5'):
    need(token in policy, f'material reuse/atlas policy missing: {token}')
for token in ('SurfaceMaximumLocalLights = 6','SurfaceMaximumShadowedLocalLights = 0',
              'InteriorMaximumLocalLights = 8','InteriorMaximumShadowedLocalLights = 2',
              'CaveMaximumLocalLights = 4','CaveMaximumShadowedLocalLights = 0',
              'DistantLightingSimplificationRequired = true'):
    need(token in policy, f'lighting budget missing: {token}')
need('WorldSceneKind.StationInterior' in policy and 'insideCave' in policy, 'world/cave lighting policy missing')
need('TextureLightingResidencyUpdateSeconds = 0.25' in slice208, '4Hz lighting residency cadence missing')
need('CollectTask208LocalLights' in slice208 and 'OmniLight3D or SpotLight3D' in slice208, 'local-light discovery missing')
need('light.LightEnergy = 0.0f' in slice208, 'distant/over-budget light simplification missing')
need('GraphicsShadowsEnabled && originalShadow' in slice208, 'graphics shadow ceiling integration missing')
need('Task208LightPriorityBias' in slice208 and 'Instrument' in slice208 and 'Hangar' in slice208, 'stable light priority policy missing')
need('TASK-208 texture/material/lighting READY' in slice208 and 'TASK-208 lighting residency PASS' in slice208, 'TASK-208 runtime evidence missing')
need('TextureLightingBudgetAcceptanceRunner' in accept and 'localBudget=' in accept and 'shadowBudget=' in accept, 'TASK-208 acceptance missing')
need('InitializeTextureLightingBudgets();' in main and 'UpdateTextureLightingBudgets(delta);' in main and
     'RunTextureLightingBudgetAcceptance();' in main and 'TASK-208 (F5)' in main and
     '_textureLightingAcceptancePassed == true' in main, 'TASK-208 F5/final integration missing')
for name in ('TextureClassMaximumsMatchSection262','SurfaceUsesOneStarAndBoundedUnshadowedLocalLights',
             'CaveBudgetIsStricterThanSurface','InteriorAllowsOnlyBoundedShadowedDynamicLights',
             'AtlasesAndReusableMaterialsAreNormative'):
    need(name in tests, f'TASK-208 xUnit missing: {name}')
need('emission_enabled = true' in station and station.count('emission_enabled = true') >= 3,
     'station interior static/emissive baseline missing')
need((ROOT/'docs/TEXTURE_MATERIAL_LIGHTING_BUDGETS.md').exists(), 'TASK-208 docs missing')
need('TASK-208' in text('README.md'), 'README TASK-208 missing')
need('## [0.1.0-alpha.208]' in text('CHANGELOG.md'), 'CHANGELOG alpha.208 missing')
need('TASK-208' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-208 missing')
for p in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task208-texture-lighting-budgets.py' in text(p), f'release gate missing in {p}')

# Shipping texture dimensions: all current raster textures are PNG. Unknown future
# raster roles are conservatively limited to the global 2048 ceiling.
def png_size(path):
    data=path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b'\x89PNG\r\n\x1a\n' or data[12:16] != b'IHDR':
        raise ValueError('not a valid PNG header')
    return struct.unpack('>II', data[16:24])

def classify_limit(path):
    s=str(path).replace('\\','/').lower()
    if '/ui/' in s or '/icons/' in s: return 512
    if '/vegetation/' in s or '/flora/' in s or '/plants/' in s: return 1024
    if '/objects/' in s or '/props/' in s: return 1024
    if '/terrain/' in s or '/environment/' in s: return 2048
    if '/ships/' in s or '/stations/' in s or '/characters/' in s or '/npc/' in s: return 2048
    return 2048
raster=list((ROOT/'src/Game.Client/Assets').rglob('*.png'))
need(len(raster) >= 2, 'expected shipping raster textures not found')
for path in raster:
    try:
        w,h=png_size(path)
        limit=classify_limit(path)
        need(w <= limit and h <= limit, f'texture exceeds class ceiling: {path.relative_to(ROOT)} {w}x{h}>{limit}')
    except Exception as exc:
        fail.append(f'texture header invalid: {path.relative_to(ROOT)}: {exc}')

if fail:
    print('TASK-208 TEXTURE/MATERIAL/LIGHTING CONTRACT FAIL:')
    for x in fail: print('ERROR:',x)
    sys.exit(1)
print(f'TASK-208 TEXTURE/MATERIAL/LIGHTING CONTRACT PASS: raster={len(raster)}; textureClasses=8; atlas=1; reusableMaterials=1; productionMaterials<=5; surfaceLights<=6/shadows=0; interiorLights<=8/shadows<=2; caveLights<=4/shadows=0; distantCull=1; runtime4Hz=1; f5=1; xunit=1.')
