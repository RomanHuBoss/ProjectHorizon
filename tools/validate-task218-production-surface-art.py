#!/usr/bin/env python3
from __future__ import annotations
import json, struct, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
VERSION=ROOT/'VERSION'
GEN=ROOT/'tools/content/generate-production-glb.py'
FACTORY=ROOT/'src/Game.Client/Scripts/VerticalSlice/ProceduralSurfaceVisualFactory.cs'
LIVE=ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionSurfaceArt.cs'
MODEL=ROOT/'src/Game.Client/Scripts/VerticalSlice/ProductionSurfaceArtAcceptance.cs'
SLICE=ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs'
TEST=ROOT/'tests/ProjectHorizon.Tests/Unit/ProductionSurfaceArtTests.cs'
DOC=ROOT/'docs/PRODUCTION_SURFACE_ART.md'
STATUS=ROOT/'REQUIREMENTS_STATUS.md'
README=ROOT/'README.md'
CHANGELOG=ROOT/'CHANGELOG.md'
CONTENT=ROOT/'src/Game.Client/Content/resources.json'
TEXTURE_DIR=ROOT/'src/Game.Client/Assets/Textures/Production'

HERO={
 'SHP_Explorer_01': ROOT/'src/Game.Client/Assets/Models/Ships',
 'SHP_Interceptor_01': ROOT/'src/Game.Client/Assets/Models/Ships',
 'STN_Orbital_01': ROOT/'src/Game.Client/Assets/Models/Stations',
}
RESOURCE_FAMILIES=('Ore','Salvage','Crystal','Fiber','Organic','Ice','Gas','Salt','Glass','Exotic')
ATLAS=('TEX_HardSurface_BaseColor.png','TEX_HardSurface_Normal.png','TEX_HardSurface_MetallicRoughness.png','TEX_HardSurface_Emission.png')

def fail(msg):
 print('TASK-218 PRODUCTION SURFACE ART CONTRACT FAIL:',msg,file=sys.stderr); raise SystemExit(1)
def text(p):
 if not p.is_file(): fail(f'missing {p.relative_to(ROOT)}')
 return p.read_text(encoding='utf-8')
def glb_json(path):
 raw=path.read_bytes()
 if len(raw)<20: fail(f'{path.name} too small')
 magic,ver,total=struct.unpack_from('<4sII',raw,0)
 if magic!=b'glTF' or ver!=2 or total!=len(raw): fail(f'{path.name} invalid glTF2 header')
 off=12; doc=None
 while off<total:
  length,kind=struct.unpack_from('<II',raw,off);off+=8;chunk=raw[off:off+length];off+=length
  if kind==0x4e4f534a: doc=json.loads(chunk.decode('utf-8').rstrip('\0 '))
 if doc is None: fail(f'{path.name} missing JSON chunk')
 return doc
def tris(doc):
 return sum(doc['accessors'][p['indices']]['count']//3 for m in doc.get('meshes',[]) for p in m.get('primitives',[]) if 'indices' in p)
def png_size(path):
 raw=path.read_bytes()[:24]
 if len(raw)<24 or raw[:8]!=b'\x89PNG\r\n\x1a\n' or raw[12:16]!=b'IHDR': fail(f'{path.name} invalid PNG')
 return struct.unpack('>II',raw[16:24])

if VERSION.read_text().strip()!='0.1.0-alpha.218': fail('VERSION must be 0.1.0-alpha.218')

# Atlas authoring output and budget.
for name in ATLAS:
 path=TEXTURE_DIR/name
 if not path.is_file(): fail(f'missing atlas map {name}')
 if png_size(path)!=(1024,1024): fail(f'{name} must be 1024x1024')

# Hard-surface GLBs must use a single PBR material with four embedded maps and UV0.
hero_summary={}
for family,folder in HERO.items():
 chain=[]
 for lod in range(3):
  path=folder/f'{family}_LOD{lod}.glb'; doc=glb_json(path); chain.append(tris(doc))
  if len(doc.get('materials',[]))!=1: fail(f'{path.name} must use one atlas material')
  if len(doc.get('images',[]))!=4 or len(doc.get('textures',[]))!=4: fail(f'{path.name} must embed four PBR atlas maps')
  if any(b.get('uri') for b in doc.get('buffers',[])) or any(i.get('uri') for i in doc.get('images',[])): fail(f'{path.name} has external GLB dependency')
  primitives=[p for m in doc.get('meshes',[]) for p in m.get('primitives',[])]
  if not primitives or any('TEXCOORD_0' not in p.get('attributes',{}) for p in primitives): fail(f'{path.name} missing atlas UV0')
  if any('Collision' in n.get('name','') for n in doc.get('nodes',[])): fail(f'{path.name} embeds collision')
 if not(chain[0]>chain[1]>chain[2]): fail(f'{family} LOD chain not descending: {chain}')
 hero_summary[family]=chain

resource_summary={}
for family in RESOURCE_FAMILIES:
 folder=ROOT/'src/Game.Client/Assets/Models/Resources'; wrapper=folder/f'RES_{family}_01.tscn'
 if not wrapper.is_file(): fail(f'missing resource wrapper {wrapper.name}')
 w=text(wrapper)
 if 'ProductionModelLodController.cs' not in w: fail(f'{wrapper.name} missing LOD controller')
 chain=[]
 for lod in range(3):
  path=folder/f'RES_{family}_01_LOD{lod}.glb'; doc=glb_json(path); chain.append(tris(doc))
  if any('Collision' in n.get('name','') for n in doc.get('nodes',[])): fail(f'{path.name} embeds collision')
  if any(b.get('uri') for b in doc.get('buffers',[])) or any(i.get('uri') for i in doc.get('images',[])): fail(f'{path.name} has external dependency')
  if f'RES_{family}_01_LOD{lod}.glb' not in w: fail(f'{wrapper.name} missing LOD{lod}')
 if not(chain[0]>chain[1]>chain[2]): fail(f'{family} LOD chain not descending: {chain}')
 resource_summary[family]=chain

# All 42 normative resource definitions must deterministically route into the ten art families.
def route(defn):
 rid=defn['resourceId']; tags=set(defn.get('tags',[]))
 if rid=='resource.salvage_alloy' or 'salvage' in tags: return 'Salvage'
 if rid=='resource.raw_compotium' or tags & {'compotium','iridium','exotic'}: return 'Exotic'
 if tags & {'glass','volcanic'}: return 'Glass'
 if tags & {'ice','clathrate'}: return 'Ice'
 if tags & {'gas','xenon','argon','inert'}: return 'Gas'
 if tags & {'salt','dust','catalyst'}: return 'Salt'
 if tags & {'crystal','exotic'}: return 'Crystal'
 if tags & {'fiber','filament','gas'}: return 'Fiber'
 if tags & {'bio','gel','resin','sludge','brine','hydrocarbon'}: return 'Organic'
 return 'Ore'
defs=json.loads(CONTENT.read_text(encoding='utf-8'))['definitions']
if len(defs)!=42: fail(f'resource catalog count={len(defs)} expected=42')
coverage={f:0 for f in RESOURCE_FAMILIES}
for d in defs:
 r=route(d)
 if r not in coverage: fail(f'unroutable resource {d["resourceId"]} => {r}')
 coverage[r]+=1
if any(v==0 for v in coverage.values()): fail(f'not all resource art families are catalog-reachable: {coverage}')

source=text(GEN); factory=text(FACTORY); live=text(LIVE); model=text(MODEL); slice_text=text(SLICE); test=text(TEST)
for token in ['ATLAS_SIZE = 1024','TEX_HardSurface_BaseColor.png','metallicRoughnessTexture=mr','normalTexture=normal','resource_ice(','resource_gas(','resource_salt(','resource_glass(','resource_exotic(']:
 if token not in source: fail(f'generator missing {token}')
for token in ['return "Exotic"','return "Glass"','return "Ice"','return "Gas"','return "Salt"','RES_{assetKey}_01.tscn']:
 if token not in factory: fail(f'resource routing missing {token}')
for token in ['TASK-218 production surface art READY','Task218TextureAtlasMaps','Task218ResourceScenes','RunProductionSurfaceArtAcceptance','texture.GetWidth() == 1024']:
 if token not in live: fail(f'live acceptance missing {token}')
for token in ['TextureAtlasMaps >= 4','HardSurfaceLodAssets >= 9','ResourceFamilies >= 10','ResourceGlbAssets >= 30','LiveResourceFallbacks == 0','production-surface-art-runtime']:
 if token not in model: fail(f'acceptance model missing {token}')
for token in ['PrintProductionSurfaceArtReady();','RunProductionSurfaceArtAcceptance();','TASK-218 (F5)','_productionSurfaceArtAcceptancePassed == true']:
 if token not in slice_text: fail(f'F5/final integration missing {token}')
for token in ['CompleteProductionSurfaceArtPasses','MissingAtlasMapFails','ResourceFamilyRegressionFails','ResourceFallbackStillFails']:
 if token not in test: fail(f'xUnit coverage missing {token}')
for path,tokens in [(DOC,['1024','four','PBR','ten resource families','manual visual acceptance']), (README,['TASK-218','alpha.218']), (CHANGELOG,['0.1.0-alpha.218','TASK-218']), (STATUS,['TASK-218','Production PBR Texture Atlas'])]:
 c=text(path).lower()
 for token in tokens:
  if token.lower() not in c: fail(f'{path.name} missing {token}')
for runner in ['tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml']:
 if 'validate-task218-production-surface-art.py' not in text(ROOT/runner): fail(f'{runner} missing TASK-218 gate')

print('TASK-218 PRODUCTION SURFACE ART CONTRACT PASS: '
      f'atlas=4x1024; hardSurfaceGlb=9; sharedMaterial=1; embeddedMaps=4; '
      f'resourceFamilies=10; resourceGlb=30; catalog=42; coverage={coverage}; '
      f'explorer={hero_summary["SHP_Explorer_01"]}; interceptor={hero_summary["SHP_Interceptor_01"]}; '
      f'station={hero_summary["STN_Orbital_01"]}; resourceLod={resource_summary}; collisionSeparate=1; f5=1; xunit=1; manualVisual=required.')
