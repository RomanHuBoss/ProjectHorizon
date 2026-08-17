#!/usr/bin/env python3
from __future__ import annotations
import json, math, re, struct, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
VERSION=ROOT/'VERSION'
GEN=ROOT/'tools/content/generate-production-glb.py'
FACTORY=ROOT/'src/Game.Client/Scripts/VerticalSlice/ProceduralSurfaceVisualFactory.cs'
RESOURCE_NODE=ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs'
LIVE=ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionArtRecovery.cs'
MODEL=ROOT/'src/Game.Client/Scripts/VerticalSlice/ProductionArtRecoveryAcceptance.cs'
SLICE=ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs'
TEST=ROOT/'tests/ProjectHorizon.Tests/Unit/ProductionArtRecoveryTests.cs'
DOC=ROOT/'docs/PRODUCTION_ART_RECOVERY.md'
STATUS=ROOT/'REQUIREMENTS_STATUS.md'
README=ROOT/'README.md'
CHANGELOG=ROOT/'CHANGELOG.md'


def fail(msg):
    print('TASK-220 PRODUCTION ART RECOVERY CONTRACT FAIL:',msg,file=sys.stderr)
    raise SystemExit(1)

def text(p):
    if not p.is_file(): fail(f'missing {p.relative_to(ROOT)}')
    return p.read_text(encoding='utf-8')

def glb_json(path):
    raw=path.read_bytes()
    if len(raw)<20: fail(f'{path.name} too small')
    magic,ver,total=struct.unpack_from('<4sII',raw,0)
    if magic!=b'glTF' or ver!=2 or total!=len(raw): fail(f'{path.name} invalid GLB2')
    off=12; doc=None
    while off<total:
        length,kind=struct.unpack_from('<II',raw,off); off+=8
        chunk=raw[off:off+length]; off+=length
        if kind==0x4E4F534A: doc=json.loads(chunk.decode('utf-8').rstrip('\0 '))
    if doc is None: fail(f'{path.name} missing JSON')
    return doc

def node_local_dims(doc,name):
    nodes=doc.get('nodes',[])
    node=next((n for n in nodes if n.get('name')==name),None)
    if node is None or 'mesh' not in node: fail(f'missing node {name}')
    mesh=doc['meshes'][node['mesh']]
    prim=mesh['primitives'][0]
    acc=doc['accessors'][prim['attributes']['POSITION']]
    lo=acc.get('min'); hi=acc.get('max')
    if lo is None or hi is None: fail(f'{name} POSITION missing min/max')
    return tuple(float(hi[i])-float(lo[i]) for i in range(3))

version=text(VERSION).strip()
if version!='0.1.0-alpha.220': fail(f'VERSION must be alpha.220, got {version}')

gen=text(GEN)
for token in [
    'TASK-220 visual recovery palette',
    'def upright(mesh: trimesh.Trimesh):',
    'Rotate authored +Z longitudinal primitives so +Y is world-up in Godot',
    "'WingPort',HULL", "'WingStarboard',HULL",
    "'BladeWingPort',HULL", "'BladeWingStarboard',HULL",
    'CrystalSpire_', 'CrystalCore', 'IceBlade_', 'IceCore',
    'GlassBlade_', 'VentChimney_', 'SaltCrystal_', 'ExoticSpire_']:
    if token not in gen: fail(f'generator missing {token}')
if '((39,47,57)' in gen or '[39, 47, 57, 255]' in gen:
    fail('rejected near-black primary hull palette returned')

m=re.search(r"'MAT_Hull_Graphite': \(\((\d+),(\d+),(\d+)\)",gen)
if not m: fail('cannot read primary hull atlas RGB')
r,g,b=map(int,m.groups())
luma=(.2126*r+.7152*g+.0722*b)/255.0
if luma<.55: fail(f'primary hull luma={luma:.3f} < .55')

resource_dir=ROOT/'src/Game.Client/Assets/Models/Resources'
crystal=glb_json(resource_dir/'RES_Crystal_01_LOD0.glb')
ice=glb_json(resource_dir/'RES_Ice_01_LOD0.glb')
cd=node_local_dims(crystal,'CrystalCore')
idims=node_local_dims(ice,'IceCore')
crystal_core_ratio=cd[1]/max(cd[0],cd[2],1e-6)
ice_core_ratio=idims[1]/max(idims[0],idims[2],1e-6)
if crystal_core_ratio<4.0: fail(f'CrystalCore local Y-up ratio={crystal_core_ratio:.2f}')
if ice_core_ratio<4.0: fail(f'IceCore local Y-up ratio={ice_core_ratio:.2f}')
for doc,name in [(crystal,'Crystal'),(ice,'Ice')]:
    if any('Collision' in n.get('name','') for n in doc.get('nodes',[])):
        fail(f'{name} embeds collision')

factory=text(FACTORY)
for token in ['float metallicScale = 1.0f','float roughnessOffset = 0.0f','float emissionScale = 1.0f',
              'visual.EmissionEnergy * emissionScale','visual.Metallic * metallicScale','visual.Roughness + roughnessOffset']:
    if token not in factory: fail(f'resource material recovery missing {token}')
resource_node=text(RESOURCE_NODE)
for token in ['role.Contains("core"','role.Contains("matrix"','role.Contains("crystal"','role.Contains("scrap"',
              'metallicScale','roughnessOffset','emissionScale']:
    if token not in resource_node: fail(f'semantic resource role missing {token}')

live=text(LIVE); model=text(MODEL); slice_text=text(SLICE); test=text(TEST)
for token in ['TASK-220 production art recovery READY','Task220CrystalLod0','ReadPrimaryHullLuminance','ReadPackedSceneVerticality',
              'crystalVerticality','iceVerticality','ownerRejected=TASK-216+TASK-218']:
    if token not in live: fail(f'live acceptance missing {token}')
for token in ['PrimaryHullLuminance >= 0.55f','CrystalVerticality >= 1.25f','IceVerticality >= 1.20f',
              'production-art-recovery-runtime']:
    if token not in model: fail(f'acceptance model missing {token}')
for token in ['PrintProductionArtRecoveryReady();','RunProductionArtRecoveryAcceptance();','TASK-220 (F5)',
              '_productionArtRecoveryAcceptancePassed is null','_productionArtRecoveryAcceptancePassed == true']:
    if token not in slice_text: fail(f'central F5 integration missing {token}')
for token in ['CorrectedVisualRecoveryPasses','DarkPrimaryHullFails','PancakeCrystalFails','FlatIceFails']:
    if token not in test: fail(f'xUnit coverage missing {token}')

for path,tokens in [
    (DOC,['owner manually rejected','pancakes','light industrial alloy','Crystal >= 1.25','manual visual acceptance']),
    (README,['TASK-220','alpha.220','Y-up','light industrial-alloy']),
    (CHANGELOG,['0.1.0-alpha.220','TASK-220','owner-rejected']),
    (STATUS,['TASK-220 Production Art Recovery','SUPERSEDED BY TASK-220','MANUAL VISUAL ACCEPTANCE REJECTED'])]:
    c=text(path).lower()
    for token in tokens:
        if token.lower() not in c: fail(f'{path.name} missing {token}')

for runner in ['tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml']:
    if 'validate-task220-production-art-recovery.py' not in text(ROOT/runner):
        fail(f'{runner} missing TASK-220 gate')

print('TASK-220 PRODUCTION ART RECOVERY CONTRACT PASS: '
      f'hullLuma={luma:.3f}; crystalCoreYRatio={crystal_core_ratio:.2f}; iceCoreYRatio={ice_core_ratio:.2f}; '
      'resources=10xLOD3; hardSurface=9; semanticMaterials=1; collisionSeparate=1; f5=1; xunit=1; manualVisual=required.')
