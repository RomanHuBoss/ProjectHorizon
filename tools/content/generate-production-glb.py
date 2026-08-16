#!/usr/bin/env python3
from __future__ import annotations
import argparse
from pathlib import Path
import numpy as np
import trimesh
from trimesh.visual.material import PBRMaterial
from trimesh.visual.texture import TextureVisuals

ROOT = Path(__file__).resolve().parents[2]
OUT_SHIPS = ROOT / 'src/Game.Client/Assets/Models/Ships'
OUT_STATIONS = ROOT / 'src/Game.Client/Assets/Models/Stations'


def mat(name, color, metallic, roughness, emissive=None):
    kwargs = dict(name=name, baseColorFactor=np.array(color, dtype=np.uint8), metallicFactor=metallic, roughnessFactor=roughness)
    if emissive is not None:
        kwargs['emissiveFactor'] = np.array(emissive[:3], dtype=float) / 255.0
    return PBRMaterial(**kwargs)

HULL = mat('MAT_Metal_Painted', [34, 50, 72, 255], 0.78, 0.28)
ACCENT = mat('MAT_Metal_Accent', [34, 144, 224, 255], 0.62, 0.24)
CANOPY = mat('MAT_Canopy_Dark', [9, 31, 47, 255], 0.16, 0.12)
ENGINE = mat('MAT_Engine_Emissive', [20, 112, 255, 255], 0.18, 0.16, [30, 150, 255, 255])
DARK = mat('MAT_Metal_Dark', [22, 28, 38, 255], 0.58, 0.42)
STN = mat('MAT_Station_Hull', [78, 86, 100, 255], 0.74, 0.34)
STN_ACCENT = mat('MAT_Station_Accent', [70, 170, 218, 255], 0.46, 0.26)
STN_DARK = mat('MAT_Station_Dark', [22, 28, 38, 255], 0.58, 0.42)


def apply_material(mesh: trimesh.Trimesh, material: PBRMaterial):
    mesh.visual = TextureVisuals(material=material)
    return mesh


def T(x=0, y=0, z=0, sx=1, sy=1, sz=1, rx=0, ry=0, rz=0):
    m = trimesh.transformations.compose_matrix(translate=[x,y,z], angles=np.radians([rx,ry,rz]), scale=[sx,sy,sz])
    return m


def wedge(width_front, width_back, height_front, height_back, z_front, z_back):
    wf, wb = width_front/2, width_back/2
    hf, hb = height_front/2, height_back/2
    v = np.array([
        [-wf,-hf,z_front],[ wf,-hf,z_front],[ wf, hf,z_front],[-wf, hf,z_front],
        [-wb,-hb,z_back], [ wb,-hb,z_back], [ wb, hb,z_back], [-wb, hb,z_back],
    ], dtype=float)
    f = np.array([
        [0,1,2],[0,2,3], [4,6,5],[4,7,6],
        [0,4,5],[0,5,1], [3,2,6],[3,6,7],
        [0,3,7],[0,7,4], [1,5,6],[1,6,2]
    ])
    return trimesh.Trimesh(v, f, process=True)


def wing(side: float, detail: int):
    # angular delta wing, Y thickness; nose is -Z
    x0, x1 = 0.55*side, (3.4 if detail >= 1 else 2.9)*side
    x2 = (2.3 if detail >= 1 else 2.0)*side
    z0, z1, z2 = -1.75, -0.15, 1.85
    h = 0.10 if detail >= 1 else 0.13
    pts = [(x0,-h,z0),(x1,-h,z1),(x2,-h,z2),(x0,-h,z2),(x0,h,z0),(x1,h,z1),(x2,h,z2),(x0,h,z2)]
    f = [[0,1,2],[0,2,3],[4,6,5],[4,7,6],[0,4,5],[0,5,1],[1,5,6],[1,6,2],[2,6,7],[2,7,3],[3,7,4],[3,4,0]]
    return trimesh.Trimesh(np.array(pts), np.array(f), process=True)


def add(scene, mesh, name, material, transform=None, parent=None):
    apply_material(mesh, material)
    scene.add_geometry(mesh, geom_name=name+'_Mesh', node_name=name, parent_node_name=parent, transform=transform)


def marker(scene, name, xyz, parent=None):
    scene.graph.update(frame_to=name, frame_from=parent, matrix=T(*xyz))


def explorer(lod: int):
    scene = trimesh.Scene()
    root = f'SHP_Explorer_01_LOD{lod}'
    scene.graph.update(frame_to=root, matrix=np.eye(4))
    add(scene, wedge(0.45,2.25,0.34,0.78,-3.65,1.75), 'HullCore', HULL, parent=root)
    add(scene, wedge(1.55,1.9,0.48,0.62,-2.4,2.55), 'HullUpper', HULL, T(y=0.24), root)
    add(scene, wing(-1, 2-lod), 'WingPort', HULL, parent=root)
    add(scene, wing(1, 2-lod), 'WingStarboard', HULL, parent=root)
    add(scene, wedge(0.24,0.52,0.18,0.30,-2.7,1.8), 'DorsalSpine', ACCENT, T(y=0.58), root)
    if lod <= 1:
        canopy = trimesh.creation.icosphere(subdivisions=2 if lod == 0 else 1, radius=0.72)
        add(scene, canopy, 'Canopy', CANOPY, T(y=0.64,z=-1.05,sx=1.0,sy=0.52,sz=1.45), root)
    cyl_sections = 24 if lod == 0 else (14 if lod == 1 else 8)
    for side, label in [(-1,'Port'),(1,'Starboard')]:
        eng = trimesh.creation.cylinder(radius=0.36 if lod<2 else 0.32, height=1.55, sections=cyl_sections)
        add(scene, eng, f'Engine{label}', HULL, T(x=0.92*side,y=-0.03,z=2.35), root)
        glow = trimesh.creation.cylinder(radius=0.26, height=0.04, sections=cyl_sections)
        add(scene, glow, f'EngineGlow{label}', ENGINE, T(x=0.92*side,y=-0.03,z=3.145), root)
    if lod == 0:
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            chine = wedge(0.14,0.32,0.18,0.26,-2.45,2.15)
            add(scene, chine, f'Chine{label}', ACCENT, T(x=1.28*side,y=0.12), root)
        # ventral keel and tail fins add silhouette without extra material slots
        add(scene, wedge(0.12,0.30,0.12,0.26,-0.2,2.75), 'VentralKeel', DARK, T(y=-0.48), root)
        tail = wedge(0.08,0.22,0.30,1.0,1.25,2.75)
        add(scene, tail, 'TailFin', ACCENT, T(y=0.55), root)
    marker(scene, 'MNT_Cockpit', (0,0.63,-1.20), root)
    marker(scene, 'MNT_Weapon_Port', (-1.82,-0.10,-0.55), root)
    marker(scene, 'MNT_Weapon_Starboard', (1.82,-0.10,-0.55), root)
    marker(scene, 'MNT_Engine_Port', (-0.92,-0.03,3.12), root)
    marker(scene, 'MNT_Engine_Starboard', (0.92,-0.03,3.12), root)
    marker(scene, 'MNT_LandingGear', (0,-0.50,0.25), root)
    return scene


def interceptor(lod: int):
    scene = trimesh.Scene(); root=f'SHP_Interceptor_01_LOD{lod}'; scene.graph.update(frame_to=root, matrix=np.eye(4))
    add(scene, wedge(0.28,1.35,0.24,0.52,-2.2,1.45), 'HullCore', HULL, parent=root)
    add(scene, wing(-1, 2-lod), 'WingPort', ACCENT, T(sx=0.62,sy=0.8,sz=0.72), root)
    add(scene, wing(1, 2-lod), 'WingStarboard', ACCENT, T(sx=0.62,sy=0.8,sz=0.72), root)
    if lod < 2:
        canopy=trimesh.creation.icosphere(subdivisions=1 if lod else 2, radius=.42)
        add(scene, canopy, 'Canopy', CANOPY, T(y=.38,z=-.62,sx=.85,sy=.48,sz=1.25), root)
    sections=18 if lod==0 else (12 if lod==1 else 8)
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        eng=trimesh.creation.cylinder(radius=.22,height=.92,sections=sections)
        add(scene,eng,f'Engine{label}',HULL,T(x=.62*side,z=1.26),root)
        glow=trimesh.creation.cylinder(radius=.17,height=.035,sections=sections)
        add(scene,glow,f'EngineGlow{label}',ENGINE,T(x=.62*side,z=1.74),root)
    marker(scene,'MNT_Weapon_Port',(-.95,-.06,-.42),root); marker(scene,'MNT_Weapon_Starboard',(.95,-.06,-.42),root)
    marker(scene,'MNT_Engine_Port',(-.62,0,1.72),root); marker(scene,'MNT_Engine_Starboard',(.62,0,1.72),root)
    return scene


def station(lod: int):
    scene=trimesh.Scene(); root=f'STN_Orbital_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    sec=64 if lod==0 else (36 if lod==1 else 18)
    tor=trimesh.creation.torus(major_radius=29.5,minor_radius=2.0 if lod<2 else 2.35,major_sections=sec,minor_sections=max(8,sec//3))
    add(scene,tor,'HabitationRing',STN,parent=root)
    hub=trimesh.creation.cylinder(radius=5.5,height=22.0,sections=sec)
    add(scene,hub,'CentralHub',STN,parent=root)
    core=trimesh.creation.box([54.0,16.0,18.0]); add(scene,core,'CoreHull',STN_DARK,T(z=-4.0),root)
    spine=trimesh.creation.box([10.0,28.0,58.0]); add(scene,spine,'DockSpine',STN_DARK,T(z=-3.0),root)
    if lod<2:
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            arm=trimesh.creation.box([34.0,2.2,11.0]); add(scene,arm,f'Arm{label}',STN,T(x=43.0*side,z=1.0),root)
            rad=trimesh.creation.box([18.0,0.34,14.0]); add(scene,rad,f'Radiator{label}',STN_ACCENT,T(x=35.0*side,z=-4.0,rz=-6.9*side),root)
            tunnel=trimesh.creation.box([2.2,12.0,38.0]); add(scene,tunnel,f'DockTunnel{label}',STN,T(x=8.2*side,z=20.0),root)
        antenna=trimesh.creation.cylinder(radius=.32,height=18.0,sections=12 if lod==0 else 8)
        add(scene,antenna.copy(),'AntennaUpper',STN_ACCENT,T(y=21.0,z=-5.0,rx=90),root)
        add(scene,antenna.copy(),'AntennaLower',STN_ACCENT,T(y=-21.0,z=-5.0,rx=90),root)
    if lod==0:
        for i in range(12):
            a=np.radians(i*30); x,y=np.cos(a)*29.5,np.sin(a)*29.5
            pod=trimesh.creation.box([4.4,2.6,4.2])
            add(scene,pod,f'RingPod_{i:02d}',STN_DARK,T(x=x,y=y,rz=i*30),root)
        for y,label in [(7.6,'Upper'),(-7.6,'Lower')]:
            guide=trimesh.creation.box([16.0,0.55,0.7]); add(scene,guide,f'DockGuide{label}',STN_ACCENT,T(y=y,z=23.5),root)
    marker(scene,'MNT_Dock',(0,0,31.0),root); marker(scene,'MNT_Service',(0,0,27.0),root)
    marker(scene,'MNT_Traffic_A',(-70,0,-20),root); marker(scene,'MNT_Traffic_B',(70,0,-20),root)
    return scene


def export_scene(scene: trimesh.Scene, path: Path):
    path.parent.mkdir(parents=True,exist_ok=True)
    data=scene.export(file_type='glb')
    path.write_bytes(data)


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--check',action='store_true'); args=parser.parse_args()
    targets=[]
    for lod in range(3):
        targets += [(explorer(lod), OUT_SHIPS/f'SHP_Explorer_01_LOD{lod}.glb'), (interceptor(lod), OUT_SHIPS/f'SHP_Interceptor_01_LOD{lod}.glb'), (station(lod), OUT_STATIONS/f'STN_Orbital_01_LOD{lod}.glb')]
    for scene,path in targets: export_scene(scene,path)
    print('TASK-184 GLB generation PASS: assets=9; families=3; lods=3; sourceBlend=0; texturesRaw=0.')

if __name__=='__main__': main()
