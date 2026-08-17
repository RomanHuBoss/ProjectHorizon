#!/usr/bin/env python3
from __future__ import annotations
import json, re, struct, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'VERSION'
SHIP = ROOT / 'src/Game.Client/Scenes/Ship/ArcadeShip.tscn'
SLICE = ROOT / 'src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn'
NPC = ROOT / 'src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs'
LOD = ROOT / 'src/Game.Client/Scripts/Presentation/ProductionModelLodController.cs'
LIVE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionAssetPipeline.cs'
MODEL = ROOT / 'src/Game.Client/Scripts/VerticalSlice/ProductionAssetPipelineAcceptance.cs'
TEST = ROOT / 'tests/ProjectHorizon.Tests/Unit/ProductionAssetPipelineTests.cs'
DOC = ROOT / 'docs/PRODUCTION_3D_ASSET_PIPELINE.md'
GEN = ROOT / 'tools/content/generate-production-glb.py'

FAMILIES = {
    'SHP_Explorer_01': ROOT / 'src/Game.Client/Assets/Models/Ships',
    'SHP_Interceptor_01': ROOT / 'src/Game.Client/Assets/Models/Ships',
    'STN_Orbital_01': ROOT / 'src/Game.Client/Assets/Models/Stations',
}
MARKERS = {'SHP_Explorer_01': 6, 'SHP_Interceptor_01': 4, 'STN_Orbital_01': 4}


def fail(msg: str):
    print(f'TASK-184 PRODUCTION ASSET PIPELINE CONTRACT FAIL: {msg}', file=sys.stderr)
    raise SystemExit(1)


def text(path: Path) -> str:
    if not path.is_file(): fail(f'missing {path.relative_to(ROOT)}')
    return path.read_text(encoding='utf-8')


def glb_json(path: Path):
    raw = path.read_bytes()
    if len(raw) < 20: fail(f'{path.name} too small')
    magic, version, total = struct.unpack_from('<4sII', raw, 0)
    if magic != b'glTF' or version != 2 or total != len(raw): fail(f'{path.name} invalid glTF2 header')
    offset=12; doc=None
    while offset < total:
        length, kind = struct.unpack_from('<II', raw, offset); offset += 8
        chunk = raw[offset:offset+length]; offset += length
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode('utf-8').rstrip('\x00 '))
    if doc is None: fail(f'{path.name} missing JSON chunk')
    return doc


def triangles(doc):
    total=0
    for mesh in doc.get('meshes', []):
        for prim in mesh.get('primitives', []):
            if 'indices' in prim:
                total += doc['accessors'][prim['indices']]['count']//3
    return total

if VERSION.read_text().strip() not in {'0.1.0-alpha.184', '0.1.0-alpha.184.1','0.1.0-alpha.186','0.1.0-alpha.188','0.1.0-alpha.192','0.1.0-alpha.192.1','0.1.0-alpha.194','0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218','0.1.0-alpha.220'}: fail('VERSION must be alpha.184/184.1/186')

all_glb=[]
family_triangles={}
marker_total=0
for family, folder in FAMILIES.items():
    tris=[]
    for lod in range(3):
        path=folder/f'{family}_LOD{lod}.glb'
        if not path.is_file(): fail(f'missing {path.relative_to(ROOT)}')
        all_glb.append(path)
        doc=glb_json(path)
        nodes=[n.get('name','') for n in doc.get('nodes',[])]
        if any('Collision' in n for n in nodes): fail(f'{path.name} embeds collision node')
        markers=sum(n.startswith('MNT_') for n in nodes)
        if markers < MARKERS[family]: fail(f'{path.name} markers={markers} expected>={MARKERS[family]}')
        if lod == 0: marker_total += markers
        materials=doc.get('materials',[])
        if len(materials) > 5: fail(f'{path.name} materials={len(materials)} > 5')
        if any(b.get('uri') for b in doc.get('buffers', [])) or any(i.get('uri') for i in doc.get('images', [])): fail(f'{path.name} must be self-contained GLB with embedded buffers/images')
        t=triangles(doc)
        if t <= 0: fail(f'{path.name} has no triangles')
        tris.append(t)
    if not (tris[0] > tris[1] > tris[2]): fail(f'{family} LOD triangle chain not descending: {tris}')
    if tris[1] > tris[0]*0.70 or tris[2] > tris[1]*0.70: fail(f'{family} LOD reduction too weak: {tris}')
    family_triangles[family]=tris
    wrapper=folder/f'{family}.tscn'
    w=text(wrapper)
    for lod in range(3):
        if f'{family}_LOD{lod}.glb' not in w: fail(f'{wrapper.name} missing LOD{lod}')
    if 'ProductionModelLodController.cs' not in w: fail(f'{wrapper.name} missing LOD controller')

if len(all_glb) != 9 or marker_total < 14: fail(f'asset totals glb={len(all_glb)} markers={marker_total}')

lod=text(LOD)
for token in ['Lod1DistanceMeters','Lod2DistanceMeters','GetCamera3D','ApplyLod(0)','ApplyLod(target)','ActiveLod']:
    if token not in lod: fail(f'LOD controller missing {token}')

ship=text(SHIP)
if 'SHP_Explorer_01.tscn' not in ship or 'name="ProductionExterior"' not in ship: fail('player ship not bound to production asset')
for name in ['Hull','Nose','LeftWing','RightWing','TailFin','Cockpit','LeftEngine','RightEngine','LeftChine','RightChine','DorsalSpine']:
    pat=rf'\[node name="{re.escape(name)}" type="MeshInstance3D" parent="Visuals"\]\nvisible = false'
    if not re.search(pat,ship): fail(f'legacy player mesh {name} not hidden')
if ship.count('type="CollisionShape3D"') != 1: fail('player gameplay collision changed')

slice_text=text(SLICE)
if 'STN_Orbital_01.tscn' not in slice_text or 'name="ProductionModel" parent="Gameplay/OrbitalStation"' not in slice_text: fail('station production model missing')
if slice_text.count('type="CollisionShape3D" parent="Gameplay/OrbitalStation"') < 20: fail('station compound collision not preserved')
if not re.search(r'\[node name="MeshInstance3D" type="MeshInstance3D" parent="Gameplay/OrbitalStation"\]\nvisible = false',slice_text): fail('legacy station core not hidden')

npc=text(NPC)
for token in ['ProductionAssetScenePath','SHP_Interceptor_01.tscn','TryAttachProductionModel','LegacyProceduralFallback','legacyVisual.Visible = !ProductionAssetLoaded','production_asset_loaded']:
    if token not in npc: fail(f'NPC asset integration missing {token}')
if npc.count('new CollisionShape3D') != 1: fail('NPC gameplay collision changed')

model=text(MODEL); live=text(LIVE); test=text(TEST); doc=text(DOC); gen=text(GEN)
for token in ['AssetFamilies >= 3','GlbAssets >= 9','LodChains >= 3','MountMarkers >= 14','CollisionSeparated','LodControllerPresent']:
    if token not in model: fail(f'acceptance model missing {token}')
for token in ['TASK-184 production asset pipeline READY','RunProductionAssetPipelineAcceptance','ProductionAssetPipelineAcceptanceRunner.Evaluate','ResourceLoader.Exists','CountMountMarkers','ContainsCollisionShape','ProductionModelLodController']:
    if token not in live: fail(f'live acceptance missing {token}')
if 'TASK-184 (F5)' not in text(ROOT/'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs'): fail('F5 HUD gate missing')
if 'CompleteProductionAssetPipelinePasses' not in test or 'MissingLodChainFails' not in test: fail('xUnit coverage missing')
for token in ['glTF 2.0 binary','LOD0','CollisionShape3D','MNT_*','generate-production-glb.py']:
    if token not in doc: fail(f'documentation missing {token}')
if 'trimesh' not in gen or 'MNT_Dock' not in gen: fail('deterministic GLB generator incomplete')

bad=[]
for ext in ('*.blend','*.fbx','*.obj','*.tga','*.exr','*.wav'):
    bad.extend((ROOT/'src/Game.Client/Assets/Models').rglob(ext))
if bad: fail('source/raw model payload leaked: '+','.join(p.name for p in bad))

print('TASK-184 PRODUCTION ASSET PIPELINE CONTRACT PASS: '
      f'families=3; glb=9; lodChains=3; markers={marker_total}; '
      f'explorer={family_triangles["SHP_Explorer_01"]}; '
      f'interceptor={family_triangles["SHP_Interceptor_01"]}; '
      f'station={family_triangles["STN_Orbital_01"]}; collisionSeparate=1; fallback=1; f5=1; xunit=1.')
