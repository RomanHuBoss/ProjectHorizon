#!/usr/bin/env python3
from __future__ import annotations
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'VERSION'
GEN = ROOT / 'tools/content/generate-production-glb.py'
FACTORY = ROOT / 'src/Game.Client/Scripts/VerticalSlice/ProceduralSurfaceVisualFactory.cs'
RESOURCE_NODE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs'
LIVE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionModelArt.cs'
MODEL = ROOT / 'src/Game.Client/Scripts/VerticalSlice/ProductionModelArtAcceptance.cs'
SLICE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs'
TEST = ROOT / 'tests/ProjectHorizon.Tests/Unit/ProductionModelArtTests.cs'
DOC = ROOT / 'docs/PRODUCTION_MODEL_ART_OVERHAUL.md'
THIRD = ROOT / 'docs/THIRD_PARTY_ASSETS.md'
README = ROOT / 'README.md'
CHANGELOG = ROOT / 'CHANGELOG.md'
STATUS = ROOT / 'REQUIREMENTS_STATUS.md'
VENDOR = ROOT / 'tools/content/vendor/kenney_space_kit'

MODEL_FAMILIES = {
    'SHP_Explorer_01': (ROOT / 'src/Game.Client/Assets/Models/Ships', 2300, 35),
    'SHP_Interceptor_01': (ROOT / 'src/Game.Client/Assets/Models/Ships', 1300, 18),
    'STN_Orbital_01': (ROOT / 'src/Game.Client/Assets/Models/Stations', 5000, 100),
}
RESOURCE_FAMILIES = {
    'RES_Ore_01', 'RES_Salvage_01', 'RES_Crystal_01', 'RES_Fiber_01', 'RES_Organic_01'
}
SIGNATURES = {
    'SHP_Explorer_01': {'SensorSpine','GearDoorPort','VentralRadiator_0','AuthoredServiceModule'},
    'SHP_Interceptor_01': {'DorsalSensor','WeaponDoorPort','AuthoredAvionicsModule'},
    'STN_Orbital_01': {'RingService_00','RadiatorRib_0_0','CargoTank_00','SpindleArmor_-27',
                       'AuthoredCommsDish','AuthoredServiceTrussA','AuthoredServiceTrussB'},
}


def fail(msg: str):
    print(f'TASK-216 PRODUCTION MODEL ART CONTRACT FAIL: {msg}', file=sys.stderr)
    raise SystemExit(1)


def text(path: Path) -> str:
    if not path.is_file(): fail(f'missing {path.relative_to(ROOT)}')
    return path.read_text(encoding='utf-8')


def glb_json(path: Path):
    if not path.is_file(): fail(f'missing {path.relative_to(ROOT)}')
    raw = path.read_bytes()
    if len(raw) < 20: fail(f'{path.name} too small')
    magic, version, total = struct.unpack_from('<4sII', raw, 0)
    if magic != b'glTF' or version != 2 or total != len(raw): fail(f'{path.name} invalid GLB2 header')
    offset = 12; doc = None
    while offset < total:
        length, kind = struct.unpack_from('<II', raw, offset); offset += 8
        chunk = raw[offset:offset+length]; offset += length
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode('utf-8').rstrip('\x00 '))
    if doc is None: fail(f'{path.name} missing JSON chunk')
    return doc


def triangles(doc):
    return sum(doc['accessors'][p['indices']]['count']//3
               for mesh in doc.get('meshes', [])
               for p in mesh.get('primitives', []) if 'indices' in p)


def node_names(doc):
    return {n.get('name','') for n in doc.get('nodes', [])}

version = text(VERSION).strip()
if version != '0.1.0-alpha.216': fail(f'VERSION must be alpha.216, got {version}')

gen = text(GEN)
for token in ['TASK-216 production model authoring','vendor_mesh(', 'VENDOR_KENNEY',
              'spaceCraft1.obj','satelliteDish.obj','metalStructure.obj',
              'resource_ore(', 'resource_crystal(', 'resource_fiber(', 'resource_organic(',
              'TASK-216 PRODUCTION MODEL ART PASS']:
    if token not in gen: fail(f'generator missing {token}')
for forbidden in ['trimesh.creation.icosphere','trimesh.creation.torus']:
    if forbidden in gen: fail(f'legacy toy primitive returned: {forbidden}')

# Reviewed CC0 source input and license are checked in, but shipping GLBs are self-contained.
for filename in ['spaceCraft1.obj','satelliteDish.obj','metalStructure.obj','License.txt']:
    if not (VENDOR/filename).is_file(): fail(f'missing reviewed vendor input {filename}')
license_text = text(VENDOR/'License.txt')
for token in ['Space Kit (1.0)','Creative Commons Zero, CC0','free to use in personal, educational and commercial projects']:
    if token not in license_text: fail(f'vendor license missing {token}')

summary = {}
for family,(folder,min_tri,min_parts) in MODEL_FAMILIES.items():
    chain=[]; parts=[]
    for lod in range(3):
        doc=glb_json(folder/f'{family}_LOD{lod}.glb')
        names=node_names(doc)
        if any('Collision' in n for n in names): fail(f'{family} LOD{lod} embeds collision')
        if doc.get('images') or any(b.get('uri') for b in doc.get('buffers', [])):
            fail(f'{family} LOD{lod} is not self-contained')
        if len(doc.get('materials', [])) > 5: fail(f'{family} LOD{lod} material count >5')
        chain.append(triangles(doc)); parts.append(len(doc.get('meshes', [])))
        if lod == 0:
            missing=SIGNATURES[family]-names
            if missing: fail(f'{family} missing TASK-216 detail nodes {sorted(missing)}')
            if chain[0] < min_tri: fail(f'{family} LOD0 triangles={chain[0]} < {min_tri}')
            if parts[0] < min_parts: fail(f'{family} LOD0 mesh parts={parts[0]} < {min_parts}')
    if not (chain[0] > chain[1] > chain[2]): fail(f'{family} LOD chain not descending: {chain}')
    if chain[1] > chain[0]*.70 or chain[2] > chain[1]*.70: fail(f'{family} LOD reduction too weak: {chain}')
    summary[family]=(chain,parts)

resource_summary={}
resource_dir=ROOT/'src/Game.Client/Assets/Models/Resources'
for family in sorted(RESOURCE_FAMILIES):
    chain=[]
    for lod in range(3):
        doc=glb_json(resource_dir/f'{family}_LOD{lod}.glb')
        names=node_names(doc)
        if any('Collision' in n for n in names): fail(f'{family} LOD{lod} embeds collision')
        if len(doc.get('materials', [])) > 4: fail(f'{family} LOD{lod} material count >4')
        chain.append(triangles(doc))
    if not (chain[0] > chain[1] > chain[2]): fail(f'{family} LOD chain not descending: {chain}')
    resource_summary[family]=chain
    wrapper=text(resource_dir/f'{family}.tscn')
    for token in ['metadata/production_resource_visual = true',
                  '[node name="LodController" type="Node3D" parent="."]',
                  'ProductionModelLodController.cs',
                  'Lod1DistanceMeters = 18.0','Lod2DistanceMeters = 45.0',
                  f'{family}_LOD0.glb',f'{family}_LOD1.glb',f'{family}_LOD2.glb']:
        if token not in wrapper: fail(f'{family}.tscn missing {token}')

factory=text(FACTORY)
for token in ['Assets/Models/Resources/RES_', 'ResourceLoader.Load<PackedScene>',
              'Instantiate<MeshInstance3D>', 'production_resource_visual',
              'CreateProceduralResourceFallback', 'ResolveResourceAssetKey']:
    if token not in factory: fail(f'resource factory missing {token}')
resource_node=text(RESOURCE_NODE)
for token in ['UpgradeProductionVisual(definition)', 'ApplyMaterialRecursive',
              'production_resource_visual', 'collision=unchanged', 'BuildResourceMaterial']:
    if token not in resource_node: fail(f'resource-node integration missing {token}')

live=text(LIVE); model=text(MODEL); slice_text=text(SLICE); test=text(TEST)
for token in ['TASK-216 production model art READY', 'RunProductionModelArtAcceptance',
              'Task216ResourceGlbs.Count(path => ResourceLoader.Exists(path))',
              'AuthoredServiceModule','AuthoredAvionicsModule','AuthoredCommsDish']:
    if token not in live: fail(f'live acceptance missing {token}')
for token in ['PlayerMeshParts >= 35','NpcMeshParts >= 18','StationMeshParts >= 100',
              'ResourceFamilies >= 5','ResourceGlbAssets >= 15','LiveResourceFallbacks == 0',
              'production-art-model-overhaul-runtime']:
    if token not in model: fail(f'acceptance model missing {token}')
for token in ['PrintProductionModelArtReady();','RunProductionModelArtAcceptance();',
              'TASK-216 (F5)', '_productionModelArtAcceptancePassed == true']:
    if token not in slice_text: fail(f'F5/final integration missing {token}')
for token in ['CompleteProductionModelArtPasses','ResourceFallbackFails','DetailedSignaturesAreRequired']:
    if token not in test: fail(f'xUnit coverage missing {token}')

for path,tokens in [(DOC,['24 shipping GLBs','five resource families','collision','manual visual acceptance','CC0']),
                    (THIRD,['Kenney','Space Kit','CC0','spaceCraft1.obj','satelliteDish.obj','metalStructure.obj']),
                    (README,['TASK-216','alpha.216']),
                    (CHANGELOG,['0.1.0-alpha.216','TASK-216']),
                    (STATUS,['TASK-216','Production 3D Model Art Overhaul'])]:
    content=text(path)
    for token in tokens:
        if token.lower() not in content.lower(): fail(f'{path.name} missing {token}')

# Raw authoring inputs must not leak into shipping model directories.
raw=[]
for ext in ('*.obj','*.fbx','*.blend','*.dae','*.stl'):
    raw.extend((ROOT/'src/Game.Client/Assets/Models').rglob(ext))
if raw: fail('raw source model leaked into shipping Assets/Models: '+','.join(p.name for p in raw))

print('TASK-216 PRODUCTION MODEL ART CONTRACT PASS: '
      f'glb=24; families=8; resourceFamilies=5; resourceGlb=15; '
      f'explorer={summary["SHP_Explorer_01"]}; interceptor={summary["SHP_Interceptor_01"]}; '
      f'station={summary["STN_Orbital_01"]}; '
      f'resources={resource_summary}; cc0KitInputs=3; collisionSeparate=1; fallbackOnly=1; f5=1; xunit=1; manualVisual=required.')
