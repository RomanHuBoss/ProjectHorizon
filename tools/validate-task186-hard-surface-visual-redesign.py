#!/usr/bin/env python3
from __future__ import annotations
import json, struct, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'VERSION'
GEN = ROOT / 'tools/content/generate-production-glb.py'
LIVE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceHardSurfaceVisualRedesign.cs'
MODEL = ROOT / 'src/Game.Client/Scripts/VerticalSlice/HardSurfaceVisualRedesignAcceptance.cs'
SLICE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs'
TEST = ROOT / 'tests/ProjectHorizon.Tests/Unit/HardSurfaceVisualRedesignTests.cs'
DOC = ROOT / 'docs/HARD_SURFACE_VISUAL_DIRECTION.md'
README = ROOT / 'README.md'
CHANGELOG = ROOT / 'CHANGELOG.md'

FAMILIES = {
    'SHP_Explorer_01': ROOT / 'src/Game.Client/Assets/Models/Ships',
    'SHP_Interceptor_01': ROOT / 'src/Game.Client/Assets/Models/Ships',
    'STN_Orbital_01': ROOT / 'src/Game.Client/Assets/Models/Stations',
}


def fail(msg: str):
    print(f'TASK-186 HARD-SURFACE VISUAL REDESIGN CONTRACT FAIL: {msg}', file=sys.stderr)
    raise SystemExit(1)


def text(path: Path) -> str:
    if not path.is_file():
        fail(f'missing {path.relative_to(ROOT)}')
    return path.read_text(encoding='utf-8')


def glb_json(path: Path):
    raw = path.read_bytes()
    if len(raw) < 20:
        fail(f'{path.name} too small')
    magic, version, total = struct.unpack_from('<4sII', raw, 0)
    if magic != b'glTF' or version != 2 or total != len(raw):
        fail(f'{path.name} invalid glTF2 header')
    offset = 12
    doc = None
    while offset < total:
        length, kind = struct.unpack_from('<II', raw, offset)
        offset += 8
        chunk = raw[offset:offset+length]
        offset += length
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode('utf-8').rstrip('\x00 '))
    if doc is None:
        fail(f'{path.name} missing JSON chunk')
    return doc


def triangles(doc):
    total = 0
    for mesh in doc.get('meshes', []):
        for prim in mesh.get('primitives', []):
            if 'indices' in prim:
                total += doc['accessors'][prim['indices']]['count'] // 3
    return total


def nodes(doc):
    return {node.get('name', '') for node in doc.get('nodes', [])}


if VERSION.read_text().strip() not in {'0.1.0-alpha.186', '0.1.0-alpha.188','0.1.0-alpha.190'}:
    fail('VERSION must preserve alpha.186 visual redesign or later accepted revision')

gen = text(GEN)
for token in ['loft_hull(', 'prism_polygon(', 'tapered_nacelle(', 'ring_module(', 'beam_between(']:
    if token not in gen:
        fail(f'generator missing hard-surface primitive {token}')
for forbidden in ['trimesh.creation.icosphere', 'trimesh.creation.torus']:
    if forbidden in gen:
        fail(f'legacy toy primitive returned: {forbidden}')
for token in ['MAT_Hull_Graphite', 'MAT_Hull_Panel', 'MAT_Safety_Accent', 'MAT_Canopy_Smoked']:
    if token not in gen:
        fail(f'new restrained material language missing {token}')

signatures = {
    'SHP_Explorer_01': {
        'PrimaryHull', 'WingPort', 'WingStarboard', 'Canopy',
        'EngineNacellePort', 'EngineNacelleStarboard',
        'DorsalFinPort', 'DorsalFinStarboard', 'DorsalArmor',
        'LeadingEdgePort', 'LeadingEdgeStarboard',
        'VectorPodPort', 'VectorPodStarboard',
    },
    'SHP_Interceptor_01': {
        'PrimaryHull', 'BladeWingPort', 'BladeWingStarboard', 'Canopy',
        'EngineNacellePort', 'EngineNacelleStarboard',
        'VentralSpine', 'GunFairingPort', 'GunFairingStarboard',
    },
    'STN_Orbital_01': {
        'CentralSpindle', 'CommandHub', 'RingModule_00', 'RingTruss_00',
        'UtilityPylon_00', 'Radiator_00', 'DockingCollar', 'DockingTunnel',
        'DockGuidePort', 'DockGuideStarboard', 'ApproachLight_0_-1',
    },
}
minimum_lod0_meshes = {'SHP_Explorer_01': 13, 'SHP_Interceptor_01': 9, 'STN_Orbital_01': 60}
minimum_lod0_triangles = {'SHP_Explorer_01': 500, 'SHP_Interceptor_01': 340, 'STN_Orbital_01': 1400}
summary = {}
for family, folder in FAMILIES.items():
    chain = []
    mesh_counts = []
    for lod in range(3):
        path = folder / f'{family}_LOD{lod}.glb'
        if not path.is_file():
            fail(f'missing {path.relative_to(ROOT)}')
        doc = glb_json(path)
        ns = nodes(doc)
        t = triangles(doc)
        chain.append(t)
        mesh_counts.append(len(doc.get('meshes', [])))
        if lod == 0:
            missing = sorted(signatures[family] - ns)
            if missing:
                fail(f'{family} LOD0 missing signature nodes {missing}')
            if mesh_counts[0] < minimum_lod0_meshes[family]:
                fail(f'{family} LOD0 mesh parts={mesh_counts[0]} expected>={minimum_lod0_meshes[family]}')
            if t < minimum_lod0_triangles[family]:
                fail(f'{family} LOD0 triangles={t} expected>={minimum_lod0_triangles[family]}')
        if any('Collision' in n for n in ns):
            fail(f'{path.name} embeds collision node')
    if not (chain[0] > chain[1] > chain[2]):
        fail(f'{family} LOD triangle chain not descending: {chain}')
    summary[family] = (chain, mesh_counts)

live = text(LIVE); model = text(MODEL); slice_text = text(SLICE); test = text(TEST); doc = text(DOC)
for token in ['TASK-186 hard-surface visual redesign READY', 'RunHardSurfaceVisualRedesignAcceptance',
              'PlayerSignaturePresent', 'StationSignaturePresent', 'CountMeshInstances', 'HasDescendantNamed']:
    if token not in live and token not in model:
        fail(f'live/model acceptance missing {token}')
for token in ['PlayerMeshParts >= 10', 'NpcMeshParts >= 8', 'StationMeshParts >= 28', 'hard-surface-visual-redesign-runtime']:
    if token not in model:
        fail(f'acceptance model missing {token}')
if 'RunHardSurfaceVisualRedesignAcceptance();' not in slice_text or 'TASK-186 (F5)' not in slice_text:
    fail('TASK-186 not wired into F5 runtime matrix')
if '_hardSurfaceVisualRedesignAcceptancePassed == true' not in slice_text:
    fail('TASK-186 missing final runtime acceptance gate')
if 'CompleteHardSurfaceRedesignPasses' not in test or 'PrimitiveOrIncompletePresentationFails' not in test:
    fail('TASK-186 xUnit coverage missing')
for token in ['lofted fuselage', 'segmented ring', 'manual visual acceptance', 'no gameplay collision']:
    if token not in doc.lower():
        fail(f'art-direction documentation missing {token}')
if 'TASK-186' not in text(README) or '0.1.0-alpha.186' not in text(CHANGELOG):
    fail('README/changelog not updated for TASK-186')

print('TASK-186 HARD-SURFACE VISUAL REDESIGN CONTRACT PASS: '
      f'explorer={summary["SHP_Explorer_01"]}; '
      f'interceptor={summary["SHP_Interceptor_01"]}; '
      f'station={summary["STN_Orbital_01"]}; '
      'lofted=1; segmentedStation=1; toySphere=0; toyTorus=0; collisionSeparate=1; f5=1; manualVisual=required.')
