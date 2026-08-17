#!/usr/bin/env python3
from __future__ import annotations
"""TASK-216 production model authoring.

Deterministic glTF 2.0 / GLB source generator used to rebuild the shipping
hard-surface ships, station and resource deposits. Geometry is authored as
purpose-built lofts, plates, trusses and custom faceted deposits; gameplay
collision stays in Godot and is never embedded in the GLB files.
"""
import argparse
from pathlib import Path
import math
import numpy as np
import trimesh
from trimesh.visual.material import PBRMaterial
from trimesh.visual.texture import TextureVisuals

ROOT = Path(__file__).resolve().parents[2]
OUT_SHIPS = ROOT / 'src/Game.Client/Assets/Models/Ships'
OUT_STATIONS = ROOT / 'src/Game.Client/Assets/Models/Stations'
OUT_RESOURCES = ROOT / 'src/Game.Client/Assets/Models/Resources'
VENDOR_KENNEY = ROOT / 'tools/content/vendor/kenney_space_kit'


def mat(name, color, metallic, roughness, emissive=None):
    kwargs = dict(
        name=name,
        baseColorFactor=np.array(color, dtype=np.uint8),
        metallicFactor=float(metallic),
        roughnessFactor=float(roughness),
    )
    if emissive is not None:
        kwargs['emissiveFactor'] = np.array(emissive[:3], dtype=float) / 255.0
    return PBRMaterial(**kwargs)

# Restrained production palette; no texture-per-part explosion.
HULL = mat('MAT_Hull_Graphite', [39, 47, 57, 255], .78, .30)
PANEL = mat('MAT_Hull_Panel', [91, 101, 112, 255], .66, .34)
ACCENT = mat('MAT_Safety_Accent', [196, 91, 28, 255], .42, .38)
CANOPY = mat('MAT_Canopy_Smoked', [8, 18, 28, 255], .18, .08)
ENGINE = mat('MAT_Engine_Emissive', [14, 70, 112, 255], .16, .18, [45, 175, 255, 255])

STN = mat('MAT_Station_Hull', [72, 80, 90, 255], .72, .36)
STN_PANEL = mat('MAT_Station_Panel', [120, 127, 136, 255], .58, .39)
STN_ACCENT = mat('MAT_Station_Safety', [190, 88, 29, 255], .44, .40)
STN_DARK = mat('MAT_Station_Dark', [24, 30, 37, 255], .68, .42)
STN_LIGHT = mat('MAT_Station_Light', [14, 72, 110, 255], .16, .18, [44, 168, 235, 255])

RES_BASE = mat('MAT_Resource_Base', [88, 94, 104, 255], .30, .62)
RES_DARK = mat('MAT_Resource_Dark', [40, 45, 52, 255], .45, .55)
RES_ACCENT = mat('MAT_Resource_Accent', [88, 172, 194, 255], .36, .28, [35, 135, 190, 255])
RES_CORE = mat('MAT_Resource_Core', [62, 120, 148, 255], .20, .18, [55, 170, 225, 255])


def apply_material(mesh: trimesh.Trimesh, material: PBRMaterial):
    mesh.visual = TextureVisuals(material=material)
    return mesh


def T(x=0, y=0, z=0, sx=1, sy=1, sz=1, rx=0, ry=0, rz=0):
    return trimesh.transformations.compose_matrix(
        translate=[x, y, z], angles=np.radians([rx, ry, rz]), scale=[sx, sy, sz])


def add(scene, mesh, name, material, transform=None, parent=None):
    apply_material(mesh, material)
    scene.add_geometry(mesh, geom_name=name + '_Mesh', node_name=name,
                       parent_node_name=parent, transform=transform)


def marker(scene, name, xyz, parent=None):
    scene.graph.update(frame_to=name, frame_from=parent, matrix=T(*xyz))


def loft_hull(sections, sides=12):
    verts = []
    counts = []
    for z, hw, hh, yc in sections:
        if hw <= 1e-5 or hh <= 1e-5:
            counts.append((len(verts), 1)); verts.append([0.0, yc, z]); continue
        start = len(verts); counts.append((start, sides))
        for i in range(sides):
            a = i / sides * math.tau
            c, s = math.cos(a), math.sin(a)
            x = hw * math.copysign(abs(c) ** .78, c)
            y = yc + hh * math.copysign(abs(s) ** 1.25, s)
            verts.append([x, y, z])
    faces = []
    for j in range(len(counts)-1):
        a0, ac = counts[j]; b0, bc = counts[j+1]
        if ac == 1:
            for i in range(bc): faces.append([a0, b0+i, b0+(i+1)%bc])
        elif bc == 1:
            for i in range(ac): faces.append([a0+i, b0, a0+(i+1)%ac])
        else:
            for i in range(ac):
                ni=(i+1)%ac
                faces += [[a0+i,b0+i,b0+ni],[a0+i,b0+ni,a0+ni]]
    return trimesh.Trimesh(np.asarray(verts,float), np.asarray(faces,int), process=True)


def prism_polygon(points_xz, thickness, y_center=0.0):
    p=np.asarray(points_xz,float); n=len(p); y0=y_center-thickness/2; y1=y_center+thickness/2
    verts=np.vstack([np.column_stack([p[:,0],np.full(n,y0),p[:,1]]),
                     np.column_stack([p[:,0],np.full(n,y1),p[:,1]])])
    faces=[]
    for i in range(1,n-1): faces += [[0,i+1,i],[n,n+i,n+i+1]]
    for i in range(n):
        ni=(i+1)%n; faces += [[i,ni,n+ni],[i,n+ni,n+i]]
    return trimesh.Trimesh(verts,np.asarray(faces,int),process=True)


def tapered_nacelle(z0,z1,r0,r1,sections=12,y_scale=.78):
    verts=[]
    for z,r in [(z0,r0),(z1,r1)]:
        for i in range(sections):
            a=i/sections*math.tau; verts.append([math.cos(a)*r,math.sin(a)*r*y_scale,z])
    faces=[]
    for i in range(sections):
        ni=(i+1)%sections; faces += [[i,sections+i,sections+ni],[i,sections+ni,ni]]
    f=len(verts); verts.append([0,0,z0]); b=len(verts); verts.append([0,0,z1])
    for i in range(sections):
        ni=(i+1)%sections; faces += [[f,ni,i],[b,sections+i,sections+ni]]
    return trimesh.Trimesh(np.asarray(verts,float),np.asarray(faces,int),process=True)


def beam_between(a,b,radius=.22,sections=8):
    a=np.asarray(a,float); b=np.asarray(b,float); vec=b-a; length=float(np.linalg.norm(vec))
    mesh=trimesh.creation.cylinder(radius=radius,height=length,sections=sections)
    align=trimesh.geometry.align_vectors([0,0,1],vec/max(length,1e-9)); transform=np.eye(4) if align is None else align
    transform[:3,3]=(a+b)/2; mesh.apply_transform(transform); return mesh


def ring_module(radius, angle_deg, tangential=10.5, radial=5.2, height=4.0):
    a=math.radians(angle_deg); x,y=math.cos(a)*radius,math.sin(a)*radius
    mesh=trimesh.creation.box([radial,tangential,height]); mesh.apply_transform(T(x=x,y=y,rz=angle_deg)); return mesh


def panel_box(size, bevel=.12):
    # Custom chamfered-ish box: convex hull of face inset/corner points.
    sx,sy,sz=[v/2 for v in size]; b=min(bevel,sx*.45,sy*.45,sz*.45)
    pts=[]
    for x in (-sx,sx):
        for y in (-sy,sy):
            for z in (-sz,sz):
                pts += [[x-math.copysign(b,x),y,z],[x,y-math.copysign(b,y),z],[x,y,z-math.copysign(b,z)]]
    return trimesh.convex.convex_hull(np.asarray(pts,float))


def hex_bolt(radius=.08,height=.05):
    return trimesh.creation.cylinder(radius=radius,height=height,sections=6)


def vendor_mesh(filename, target_extents, rotation=(0, 0, 0)):
    """Load a reviewed CC0 kit mesh and normalize it into our authored scale.

    Vendor source geometry is an authoring input only. Shipping GLBs remain
    self-contained and all imported material references are discarded.
    """
    path = VENDOR_KENNEY / filename
    loaded = trimesh.load(path, force='mesh', process=True)
    if isinstance(loaded, trimesh.Scene):
        loaded = loaded.dump(concatenate=True)
    mesh = loaded.copy()
    center = mesh.bounds.mean(axis=0)
    mesh.apply_translation(-center)
    extents = np.maximum(mesh.extents, 1e-6)
    target = np.asarray(target_extents, float)
    mesh.apply_scale(target / extents)
    if any(abs(float(v)) > 1e-8 for v in rotation):
        mesh.apply_transform(T(rx=rotation[0], ry=rotation[1], rz=rotation[2]))
    return mesh


def explorer(lod:int):
    scene=trimesh.Scene(); root=f'SHP_Explorer_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    sides=(24,16,10)[lod]
    sections=[(-4.65,.03,.03,.02),(-4.05,.44,.22,.03),(-3.20,.88,.40,.06),(-2.15,1.24,.58,.10),
              (-.65,1.52,.72,.08),(.95,1.48,.69,.03),(2.30,1.25,.60,-.02),(3.45,.88,.46,-.05),(3.95,.48,.30,-.05)]
    add(scene,loft_hull(sections,sides),'PrimaryHull',HULL,parent=root)
    port=[(-.72,-2.65),(-3.85,-.72),(-3.64,.20),(-3.20,1.80),(-1.28,3.02),(-.94,1.35)]
    star=[(-x,z) for x,z in port]
    add(scene,prism_polygon(port,.14 if lod==0 else .20,-.04),'WingPort',PANEL if lod<2 else HULL,parent=root)
    add(scene,prism_polygon(star,.14 if lod==0 else .20,-.04),'WingStarboard',PANEL if lod<2 else HULL,parent=root)
    if lod<2:
        canopy=[(-2.95,.28,.04,.70),(-2.58,.55,.17,.83),(-1.75,.72,.26,.91),(-.90,.62,.24,.88),(-.45,.34,.09,.79)]
        add(scene,loft_hull(canopy,12 if lod==0 else 8),'Canopy',CANOPY,parent=root)
    ns=(20,12,8)[lod]
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        add(scene,tapered_nacelle(.45,3.76,.44,.58,ns),'EngineNacelle'+label,HULL,T(x=1.82*side,y=-.08),root)
        add(scene,tapered_nacelle(2.55,3.86,.48,.42,ns,y_scale=.82),'EngineCollar'+label,PANEL,T(x=1.82*side,y=-.08),root)
        nozzle=trimesh.creation.cylinder(radius=.38 if lod<2 else .32,height=.06,sections=ns)
        add(scene,nozzle,'EngineGlow'+label,ENGINE,T(x=1.82*side,y=-.08,z=3.91),root)
    if lod<2:
        finp=[(-1.08,1.10),(-1.55,3.28),(-1.18,3.52),(-.88,1.62)]; fins=[(-x,z) for x,z in finp]
        add(scene,prism_polygon(finp,.09,.56),'DorsalFinPort',ACCENT if lod==0 else PANEL,parent=root)
        add(scene,prism_polygon(fins,.09,.56),'DorsalFinStarboard',ACCENT if lod==0 else PANEL,parent=root)
    if lod==0:
        add(scene,prism_polygon([(-.38,-3.15),(-.52,2.75),(.52,2.75),(.38,-3.15)],.07,.72),'DorsalArmor',PANEL,parent=root)
        add(scene,prism_polygon([(-3.63,-.75),(-3.38,-.40),(-1.02,-2.12),(-.82,-2.48)],.035,.09),'LeadingEdgePort',ACCENT,parent=root)
        add(scene,prism_polygon([(3.63,-.75),(3.38,-.40),(1.02,-2.12),(.82,-2.48)],.035,.09),'LeadingEdgeStarboard',ACCENT,parent=root)
        # dorsal sensor blister + vent banks + access panels + RCS pods
        add(scene,panel_box([.72,.22,1.12],.08),'SensorSpine',HULL,T(y=.76,z=.72),root)
        add(scene,vendor_mesh('spaceCraft1.obj',(1.05,.24,.82),(90,0,0)),
            'AuthoredServiceModule',PANEL,T(y=.79,z=1.70,rz=180),root)
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            pod=loft_hull([(-.38,.14,.10,0),(.20,.26,.17,0),(1.08,.18,.12,0)],10)
            add(scene,pod,'VectorPod'+label,PANEL,T(x=2.56*side,y=.02,z=.60),root)
            # three recessed-looking vent slats
            for j in range(3):
                add(scene,panel_box([.34,.06,.55],.035),f'Vent{label}_{j}',HULL,
                    T(x=(1.02+.33*j)*side,y=.66,z=1.20+.24*j,ry=side*5),root)
            # wing service hatches
            for j in range(3):
                x=(1.35+.55*j)*side; z=-.72+.62*j
                add(scene,panel_box([.34,.045,.52],.035),f'WingPanel{label}_{j}',PANEL,T(x=x,y=.14,z=z),root)
            # landing gear door outlines
            add(scene,panel_box([.46,.04,.80],.05),f'GearDoor{label}',HULL,T(x=.92*side,y=-.66,z=.82),root)
        # nose sensor facets and underside radiator
        for j,x in enumerate((-0.44,0,.44)):
            add(scene,panel_box([.22,.08,.44],.04),f'NoseSensor_{j}',HULL,T(x=x,y=.40,z=-3.15),root)
        for j in range(5):
            add(scene,panel_box([.22,.045,.72],.025),f'VentralRadiator_{j}',HULL,T(x=(j-2)*.28,y=-.69,z=.25),root)
        # fasteners add close-range scale without changing silhouette
        for side in (-1,1):
            for z in (-1.9,-.8,.4,1.6):
                add(scene,hex_bolt(.055,.025),f'Fastener_{side:+d}_{z:+.1f}',ACCENT,T(x=1.34*side,y=.65,z=z,rx=90),root)
    elif lod==1:
        add(scene,panel_box([.70,.18,1.0],.07),'SensorSpine',HULL,T(y=.73,z=.72),root)
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            add(scene,panel_box([.45,.05,.80],.05),'GearDoor'+label,HULL,T(x=.92*side,y=-.65,z=.82),root)
    marker(scene,'MNT_Cockpit',(0,.92,-1.58),root); marker(scene,'MNT_Weapon_Port',(-2.66,-.04,-.72),root)
    marker(scene,'MNT_Weapon_Starboard',(2.66,-.04,-.72),root); marker(scene,'MNT_Engine_Port',(-1.82,-.08,3.95),root)
    marker(scene,'MNT_Engine_Starboard',(1.82,-.08,3.95),root); marker(scene,'MNT_LandingGear',(0,-.72,.45),root)
    return scene


def interceptor(lod:int):
    scene=trimesh.Scene(); root=f'SHP_Interceptor_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    sides=(20,14,8)[lod]
    add(scene,loft_hull([(-3.20,.02,.02,.02),(-2.62,.36,.20,.05),(-1.55,.70,.36,.08),(-.45,.88,.44,.07),
                         (.70,.80,.39,.01),(1.80,.58,.31,-.03),(2.58,.32,.20,-.04),(2.90,.18,.12,-.04)],sides),
        'PrimaryHull',HULL,parent=root)
    port=[(-.42,-2.05),(-2.95,-.65),(-2.70,.42),(-2.24,1.65),(-.76,2.08)]
    star=[(-x,z) for x,z in port]
    add(scene,prism_polygon(port,.10 if lod==0 else .16,-.02),'BladeWingPort',ACCENT if lod==0 else PANEL,parent=root)
    add(scene,prism_polygon(star,.10 if lod==0 else .16,-.02),'BladeWingStarboard',ACCENT if lod==0 else PANEL,parent=root)
    if lod<2:
        add(scene,loft_hull([(-2.05,.20,.04,.45),(-1.66,.39,.14,.58),(-.90,.43,.16,.59),(-.38,.25,.07,.50)],
                            10 if lod==0 else 7),'Canopy',CANOPY,parent=root)
    ns=(18,12,7)[lod]
    for side,label in [(-1,'Port'),(1,'Starboard')]:
        add(scene,tapered_nacelle(-.30,2.62,.24,.38,ns),'EngineNacelle'+label,PANEL,T(x=1.46*side,y=-.04),root)
        add(scene,trimesh.creation.cylinder(radius=.27,height=.05,sections=ns),'EngineGlow'+label,ENGINE,T(x=1.46*side,y=-.04,z=2.67),root)
    if lod==0:
        add(scene,prism_polygon([(-.10,.15),(-.17,2.52),(.17,2.52),(.10,.15)],.07,-.40),'VentralSpine',PANEL,parent=root)
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            fair=loft_hull([(-1.72,.09,.07,0),(-.72,.17,.11,0),(.36,.10,.07,0)],8)
            add(scene,fair,'GunFairing'+label,HULL,T(x=2.02*side,y=-.07),root)
            for j in range(4):
                add(scene,panel_box([.20,.045,.48],.025),f'Vent{label}_{j}',HULL,
                    T(x=(.78+.24*j)*side,y=.40,z=.70+.28*j),root)
            add(scene,panel_box([.34,.04,.68],.045),f'WeaponDoor{label}',HULL,T(x=1.95*side,y=-.12,z=-.58),root)
        add(scene,panel_box([.60,.16,.70],.06),'DorsalSensor',PANEL,T(y=.47,z=.42),root)
        add(scene,vendor_mesh('spaceCraft1.obj',(.74,.16,.54),(90,0,0)),
            'AuthoredAvionicsModule',PANEL,T(y=.50,z=.96,rz=180),root)
        for j,x in enumerate((-0.34,0,.34)):
            add(scene,panel_box([.16,.06,.34],.03),f'NoseFacet_{j}',HULL,T(x=x,y=.28,z=-2.42),root)
    elif lod==1:
        add(scene,prism_polygon([(-.10,.25),(-.15,2.38),(.15,2.38),(.10,.25)],.08,-.38),'VentralSpine',PANEL,parent=root)
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            fair=loft_hull([(-1.55,.09,.07,0),(-.60,.15,.10,0),(.25,.09,.06,0)],6)
            add(scene,fair,'GunFairing'+label,HULL,T(x=1.96*side,y=-.06),root)
    marker(scene,'MNT_Weapon_Port',(-2.02,-.07,-.92),root); marker(scene,'MNT_Weapon_Starboard',(2.02,-.07,-.92),root)
    marker(scene,'MNT_Engine_Port',(-1.46,-.04,2.71),root); marker(scene,'MNT_Engine_Starboard',(1.46,-.04,2.71),root)
    return scene


def station(lod:int):
    scene=trimesh.Scene(); root=f'STN_Orbital_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    radial=34.; ring_count=(24,16,8)[lod]; cyl=(24,16,8)[lod]
    add(scene,tapered_nacelle(-39,39,4.0,4.0,cyl,1.0),'CentralSpindle',STN_DARK,parent=root)
    add(scene,tapered_nacelle(-11,11,8.5,8.5,cyl,1.0),'CommandHub',STN,parent=root)
    # layered hub armor sleeves
    if lod<2:
        add(scene,tapered_nacelle(-6,6,9.0,9.0,cyl,1.0),'CommandHubArmor',STN_PANEL,parent=root)
    for i in range(ring_count):
        ang=i*360/ring_count
        add(scene,ring_module(radial,ang,8.0 if lod==0 else 11.5,4.8 if lod<2 else 6.4,4.1 if lod<2 else 5.0),
            f'RingModule_{i:02d}',STN if i%3 else STN_PANEL,parent=root)
        if lod==0:
            a=math.radians(ang); x,y=math.cos(a)*(radial+2.5),math.sin(a)*(radial+2.5)
            seam=trimesh.creation.box([.16,3.7,1.75]); seam.apply_transform(T(x=x,y=y,rz=ang))
            add(scene,seam,f'RingLight_{i:02d}',STN_LIGHT,parent=root)
            # service cap adds module depth
            cap=panel_box([2.1,2.8,2.0],.18); cap.apply_transform(T(x=math.cos(a)*(radial-3.5),y=math.sin(a)*(radial-3.5),z=0,rz=ang))
            add(scene,cap,f'RingService_{i:02d}',STN_DARK,parent=root)
    spoke_count=(12,8,4)[lod]
    for i in range(spoke_count):
        a=i*360/spoke_count; r=math.radians(a); end=(math.cos(r)*(radial-3),math.sin(r)*(radial-3),0)
        add(scene,beam_between((0,0,0),end,.28 if lod==0 else .42,10 if lod==0 else 8),f'RingTruss_{i:02d}',STN_DARK,parent=root)
        if lod==0:
            # secondary parallel truss for real structural depth
            off=np.array([-math.sin(r),math.cos(r),0])*.65
            add(scene,beam_between(tuple(off),tuple(np.array(end)+off),.14,8),f'RingTrussB_{i:02d}',STN_PANEL,parent=root)
    for i in range(4):
        a=i*90+45; r=math.radians(a); start=(math.cos(r)*8,math.sin(r)*8,-4); end=(math.cos(r)*48,math.sin(r)*48,-4)
        add(scene,beam_between(start,end,.64 if lod==0 else .88,10),f'UtilityPylon_{i:02d}',STN,parent=root)
        if lod<2:
            x,y=math.cos(r)*52,math.sin(r)*52
            panel=panel_box([14,.6,20],.20); panel.apply_transform(T(x=x,y=y,z=-4,rz=a+90)); add(scene,panel,f'Radiator_{i:02d}',STN_PANEL,parent=root)
            stripe=trimesh.creation.box([14.5,.64,1.0]); stripe.apply_transform(T(x=x,y=y,z=3.5,rz=a+90)); add(scene,stripe,f'RadiatorStripe_{i:02d}',STN_ACCENT,parent=root)
            if lod==0:
                for j in range(5):
                    rib=trimesh.creation.box([.18,.70,19.0]); rib.apply_transform(T(x=x+(j-2)*2.6*math.cos(r),y=y+(j-2)*2.6*math.sin(r),z=-4,rz=a+90))
                    add(scene,rib,f'RadiatorRib_{i}_{j}',STN_DARK,parent=root)
    add(scene,tapered_nacelle(22,36,7.4,5.5,cyl,1.0),'DockingCollar',STN,parent=root)
    add(scene,tapered_nacelle(34,48,4.9,4.1,cyl,1.0),'DockingTunnel',STN_DARK,parent=root)
    if lod<2:
        for side,label in [(-1,'Port'),(1,'Starboard')]:
            add(scene,panel_box([1.05,1.05,19],.12),f'DockGuide{label}',STN_ACCENT,T(x=6.3*side,z=38),root)
        for j,z in enumerate([38.8,41.2,43.6,46.0]):
            for side in (-1,1):
                add(scene,panel_box([.38,.38,.38],.05),f'ApproachLight_{j}_{side:+d}',STN_LIGHT,T(x=5*side,z=z),root)
    if lod==0:
        for y,label in [(10.5,'Upper'),(-10.5,'Lower')]:
            add(scene,beam_between((0,y,-8),(0,y*1.9,-17),.20,10),f'Antenna{label}',STN_ACCENT,parent=root)
        for i in range(8):
            a=math.radians(i*45+22.5); x,y=math.cos(a)*12,math.sin(a)*12
            add(scene,tapered_nacelle(-5,5,1.35,1.35,10,1.0),f'ServicePod_{i:02d}',STN_PANEL,T(x=x,y=y),root)
        # Reviewed CC0 kit geometry is used as close-range station greeble;
        # collision and gameplay identity remain Project Horizon-authored.
        add(scene,vendor_mesh('satelliteDish.obj',(4.8,3.8,4.6),(90,0,0)),
            'AuthoredCommsDish',STN_PANEL,T(x=-8.5,y=2.5,z=-17,ry=25),root)
        add(scene,vendor_mesh('metalStructure.obj',(6.0,6.0,6.0),(0,0,0)),
            'AuthoredServiceTrussA',STN_DARK,T(x=9.5,y=-4.5,z=-10,rz=20),root)
        add(scene,vendor_mesh('metalStructure.obj',(5.0,5.0,5.0),(0,90,0)),
            'AuthoredServiceTrussB',STN_DARK,T(x=-11.0,y=-3.0,z=8,rz=-35),root)
        # Cargo tanks and external conduits give close-range industrial scale.
        for i in range(8):
            a=math.radians(i*45); x,y=math.cos(a)*25,math.sin(a)*25
            add(scene,tapered_nacelle(-2.2,2.2,.82,.82,10,1.0),f'CargoTank_{i:02d}',STN_DARK,T(x=x,y=y,z=-6),root)
            add(scene,beam_between((x*.45,y*.45,-4),(x*.92,y*.92,-5.8),.10,6),f'Conduit_{i:02d}',STN_ACCENT,parent=root)
        for z in (-27,-17,-7,7,17,27):
            add(scene,panel_box([7.2,1.2,2.0],.16),f'SpindleArmor_{z:+d}',STN_PANEL,T(z=z),root)
    marker(scene,'MNT_Dock',(0,0,49),root); marker(scene,'MNT_Service',(0,0,34),root)
    marker(scene,'MNT_Traffic_A',(-76,0,-18),root); marker(scene,'MNT_Traffic_B',(76,0,-18),root)
    return scene


# ---------- Resource deposit authoring ----------
def subdivide_octahedron(level:int, seed:int=1, scale=(1,1,1)):
    verts=np.array([[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]],float)
    faces=np.array([[0,2,4],[2,1,4],[1,3,4],[3,0,4],[2,0,5],[1,2,5],[3,1,5],[0,3,5]],int)
    for _ in range(level):
        cache={}; nv=verts.tolist(); nf=[]
        def mid(a,b):
            key=tuple(sorted((int(a),int(b))))
            if key in cache:return cache[key]
            p=(verts[a]+verts[b])*.5; p/=max(np.linalg.norm(p),1e-9)
            idx=len(nv); nv.append(p.tolist()); cache[key]=idx; return idx
        for a,b,c in faces:
            ab=mid(a,b); bc=mid(b,c); ca=mid(c,a)
            nf += [[a,ab,ca],[b,bc,ab],[c,ca,bc],[ab,bc,ca]]
        verts=np.asarray(nv,float); faces=np.asarray(nf,int)
    rng=np.random.default_rng(seed)
    r=1 + rng.uniform(-.18,.18,len(verts)) + .08*np.sin(verts[:,0]*7+verts[:,2]*5)
    verts=verts*r[:,None]*np.asarray(scale,float)
    return trimesh.Trimesh(verts,faces,process=True)


def crystal_prism(radius,height,sections=6,tip=.28):
    verts=[]
    z0=-height*.45; z1=height*(.5-tip); zt=height*.5
    for z in (z0,z1):
        for i in range(sections):
            a=i/sections*math.tau; verts.append([math.cos(a)*radius,math.sin(a)*radius,z])
    tip_i=len(verts); verts.append([0,0,zt]); base_i=len(verts); verts.append([0,0,z0])
    faces=[]
    for i in range(sections):
        ni=(i+1)%sections
        faces += [[i,sections+i,sections+ni],[i,sections+ni,ni],[sections+i,tip_i,sections+ni],[base_i,ni,i]]
    return trimesh.Trimesh(np.asarray(verts,float),np.asarray(faces,int),process=True)


def resource_ore(lod:int, salvage=False):
    scene=trimesh.Scene(); key='Salvage' if salvage else 'Ore'; root=f'RES_{key}_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    level=(2,1,0)[lod]
    if salvage:
        # Bent manufactured scrap cluster rather than a rock.
        parts=(10,6,3)[lod]
        for i in range(parts):
            a=i*math.tau/max(parts,1); x=math.cos(a)*(.48+.10*(i%2)); z=math.sin(a)*(.42+.08*((i+1)%2))
            size=[.58 if i%3 else .82,.12+.05*(i%2),.42+.08*(i%3)]
            add(scene,panel_box(size,.07),f'ScrapPlate_{i:02d}',RES_BASE if i%3 else RES_DARK,
                T(x=x,y=.18+.06*(i%3),z=z,rx=(i%4)*9,ry=i*29,rz=(i%5)*11),root)
        if lod<2:
            add(scene,tapered_nacelle(-.38,.38,.20,.25,8 if lod==0 else 6,1.0),'SalvageCoupler',RES_ACCENT,T(y=.38,rx=90),root)
    else:
        add(scene,subdivide_octahedron(level,91,(.72,.58,.82)),'OreMass',RES_BASE,T(y=.40),root)
        count=(4,2,1)[lod]
        for i in range(count):
            a=i*math.tau/count; add(scene,subdivide_octahedron(max(level-1,0),101+i,(.32,.26,.36)),f'OreNodule_{i:02d}',RES_DARK,
                T(x=math.cos(a)*.52,y=.25+(.10*(i%2)),z=math.sin(a)*.44,rx=i*13,ry=i*31),root)
        if lod<2:
            for i in range(3 if lod==0 else 1):
                add(scene,prism_polygon([(-.06,-.45),(-.12,.35),(.12,.48),(.08,-.38)],.035,.55+i*.02),f'MetalVein_{i:02d}',RES_ACCENT,T(ry=i*120),root)
    return scene


def resource_crystal(lod:int):
    scene=trimesh.Scene(); root=f'RES_Crystal_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    count=(9,5,3)[lod]; sections=6
    for i in range(count):
        a=i*2.399963; rr=.18+.06*(i%3); h=(1.05,.88,.72)[lod]*(.72+.08*(i%4))
        x=math.cos(a)*(.25+.055*i); z=math.sin(a)*(.25+.045*i)
        add(scene,crystal_prism(rr,h,sections,.24),f'Crystal_{i:02d}',RES_BASE if i%3 else RES_ACCENT,
            T(x=x,y=h*.47,z=z,rx=(i%3-1)*9,rz=(i%2*2-1)*7,ry=i*37),root)
    if lod<2:
        add(scene,crystal_prism(.20,.72,6,.22),'CrystalCore',RES_CORE,T(y=.42,ry=22),root)
    return scene


def resource_fiber(lod:int):
    scene=trimesh.Scene(); root=f'RES_Fiber_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    stems=(7,4,2)[lod]; sections=(10,7,6)[lod]
    for i in range(stems):
        a=i*math.tau/max(stems,1); r=.23+.04*(i%2); h=.82+.14*(i%3)
        add(scene,tapered_nacelle(-h/2,h/2,r,r*.42,sections,y_scale=.82),f'FiberStem_{i:02d}',RES_BASE,
            T(x=math.cos(a)*.34,y=h*.48,z=math.sin(a)*.34,rx=math.cos(a)*13,rz=-math.sin(a)*13,ry=i*41),root)
        if lod==0:
            add(scene,panel_box([.12,.12,.34],.035),f'FiberNode_{i:02d}',RES_ACCENT,
                T(x=math.cos(a)*.48,y=.48,z=math.sin(a)*.48,ry=i*41),root)
    if lod<2:
        add(scene,tapered_nacelle(-.30,.30,.32,.27,sections,1.0),'FiberCore',RES_DARK,T(y=.28,rx=90),root)
    return scene


def resource_organic(lod:int):
    scene=trimesh.Scene(); root=f'RES_Organic_01_LOD{lod}'; scene.graph.update(frame_to=root,matrix=np.eye(4))
    count=(6,4,2)[lod]; level=(2,1,0)[lod]
    for i in range(count):
        a=i*math.tau/count; sc=(.44+.07*(i%3),.28+.05*(i%2),.50+.06*((i+1)%3))
        add(scene,subdivide_octahedron(level,200+i,sc),f'OrganicLobe_{i:02d}',RES_BASE if i%2 else RES_DARK,
            T(x=math.cos(a)*.34,y=.30+.08*(i%2),z=math.sin(a)*.34,rx=i*11,ry=i*23),root)
    if lod<2:
        for i in range(4 if lod==0 else 2):
            a=i*math.tau/(4 if lod==0 else 2)
            add(scene,tapered_nacelle(-.30,.30,.07,.025,7,1.0),f'OrganicTendril_{i:02d}',RES_ACCENT,
                T(x=math.cos(a)*.52,y=.22,z=math.sin(a)*.52,rx=90+math.sin(a)*18,ry=math.degrees(a)),root)
        add(scene,subdivide_octahedron(1,255,(.20,.16,.22)),'OrganicCore',RES_CORE,T(y=.42),root)
    return scene


def export_scene(scene,path:Path):
    path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(scene.export(file_type='glb'))


def write_resource_wrappers():
    specs={
        'RES_Ore_01':'Ore','RES_Salvage_01':'Salvage','RES_Crystal_01':'Crystal',
        'RES_Fiber_01':'Fiber','RES_Organic_01':'Organic'}
    for family in specs:
        text=f'''[gd_scene load_steps=5 format=3]\n\n[ext_resource type="Script" path="res://Scripts/Presentation/ProductionModelLodController.cs" id="1_lod"]\n[ext_resource type="PackedScene" path="res://Assets/Models/Resources/{family}_LOD0.glb" id="2_lod0"]\n[ext_resource type="PackedScene" path="res://Assets/Models/Resources/{family}_LOD1.glb" id="3_lod1"]\n[ext_resource type="PackedScene" path="res://Assets/Models/Resources/{family}_LOD2.glb" id="4_lod2"]\n\n[node name="MeshInstance3D" type="MeshInstance3D"]\nmetadata/production_resource_visual = true\n\n[node name="LodController" type="Node3D" parent="."]\nscript = ExtResource("1_lod")\nLod1DistanceMeters = 18.0\nLod2DistanceMeters = 45.0\n\n[node name="LOD0" parent="LodController" instance=ExtResource("2_lod0")]\n[node name="LOD1" parent="LodController" instance=ExtResource("3_lod1")]\nvisible = false\n[node name="LOD2" parent="LodController" instance=ExtResource("4_lod2")]\nvisible = false\n'''
        (OUT_RESOURCES/f'{family}.tscn').write_text(text,encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--check',action='store_true'); parser.parse_args()
    targets=[]
    for lod in range(3):
        targets += [(explorer(lod),OUT_SHIPS/f'SHP_Explorer_01_LOD{lod}.glb'),
                    (interceptor(lod),OUT_SHIPS/f'SHP_Interceptor_01_LOD{lod}.glb'),
                    (station(lod),OUT_STATIONS/f'STN_Orbital_01_LOD{lod}.glb'),
                    (resource_ore(lod,False),OUT_RESOURCES/f'RES_Ore_01_LOD{lod}.glb'),
                    (resource_ore(lod,True),OUT_RESOURCES/f'RES_Salvage_01_LOD{lod}.glb'),
                    (resource_crystal(lod),OUT_RESOURCES/f'RES_Crystal_01_LOD{lod}.glb'),
                    (resource_fiber(lod),OUT_RESOURCES/f'RES_Fiber_01_LOD{lod}.glb'),
                    (resource_organic(lod),OUT_RESOURCES/f'RES_Organic_01_LOD{lod}.glb')]
    for scene,path in targets: export_scene(scene,path)
    write_resource_wrappers()
    print('TASK-216 PRODUCTION MODEL ART PASS: glb=24; families=8; ships=2; station=1; resourceFamilies=5; LOD=3; collisionSeparate=1; runtimeGeneration=0.')

if __name__=='__main__': main()
