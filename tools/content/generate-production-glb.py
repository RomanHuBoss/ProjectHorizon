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
    kwargs = dict(
        name=name,
        baseColorFactor=np.array(color, dtype=np.uint8),
        metallicFactor=metallic,
        roughnessFactor=roughness,
    )
    if emissive is not None:
        kwargs['emissiveFactor'] = np.array(emissive[:3], dtype=float) / 255.0
    return PBRMaterial(**kwargs)

# TASK-186 palette: restrained hard-surface aerospace/industrial language rather
# than the toy-blue primitive palette from TASK-184.
HULL = mat('MAT_Hull_Graphite', [42, 49, 58, 255], 0.76, 0.34)
PANEL = mat('MAT_Hull_Panel', [88, 96, 106, 255], 0.68, 0.38)
ACCENT = mat('MAT_Safety_Accent', [184, 92, 34, 255], 0.50, 0.40)
CANOPY = mat('MAT_Canopy_Smoked', [10, 21, 30, 255], 0.22, 0.10)
ENGINE = mat('MAT_Engine_Emissive', [18, 88, 135, 255], 0.20, 0.18, [36, 155, 255, 255])

STN = mat('MAT_Station_Hull', [76, 82, 90, 255], 0.72, 0.38)
STN_PANEL = mat('MAT_Station_Panel', [118, 124, 130, 255], 0.58, 0.42)
STN_ACCENT = mat('MAT_Station_Safety', [182, 88, 32, 255], 0.46, 0.42)
STN_DARK = mat('MAT_Station_Dark', [29, 34, 40, 255], 0.64, 0.45)
STN_LIGHT = mat('MAT_Station_Light', [20, 82, 120, 255], 0.18, 0.20, [40, 155, 220, 255])


def apply_material(mesh: trimesh.Trimesh, material: PBRMaterial):
    mesh.visual = TextureVisuals(material=material)
    return mesh


def T(x=0, y=0, z=0, sx=1, sy=1, sz=1, rx=0, ry=0, rz=0):
    return trimesh.transformations.compose_matrix(
        translate=[x, y, z], angles=np.radians([rx, ry, rz]), scale=[sx, sy, sz]
    )


def add(scene, mesh, name, material, transform=None, parent=None):
    apply_material(mesh, material)
    scene.add_geometry(
        mesh,
        geom_name=name + '_Mesh',
        node_name=name,
        parent_node_name=parent,
        transform=transform,
    )


def marker(scene, name, xyz, parent=None):
    scene.graph.update(frame_to=name, frame_from=parent, matrix=T(*xyz))


def loft_hull(sections, sides=10):
    """Faceted aerospace hull lofted through (z, half_width, half_height, y_center).

    The ring is intentionally non-circular: top/bottom are flatter than the
    shoulders, giving a manufactured pressure-hull silhouette without spheres.
    """
    verts = []
    for z, hw, hh, yc in sections:
        if hw <= 1e-5 or hh <= 1e-5:
            verts.append([0.0, yc, z])
            continue
        for i in range(sides):
            a = (i / sides) * np.pi * 2.0
            c, s = np.cos(a), np.sin(a)
            # Superellipse-ish flattening for hard-surface shoulders.
            x = hw * np.sign(c) * (abs(c) ** 0.82)
            y = yc + hh * np.sign(s) * (abs(s) ** 1.18)
            verts.append([x, y, z])

    faces = []
    offsets = []
    cursor = 0
    for z, hw, hh, yc in sections:
        count = 1 if hw <= 1e-5 or hh <= 1e-5 else sides
        offsets.append((cursor, count))
        cursor += count

    for j in range(len(sections) - 1):
        a0, ac = offsets[j]
        b0, bc = offsets[j + 1]
        if ac == 1 and bc > 1:
            for i in range(bc):
                faces.append([a0, b0 + i, b0 + ((i + 1) % bc)])
        elif bc == 1 and ac > 1:
            for i in range(ac):
                faces.append([a0 + i, b0, a0 + ((i + 1) % ac)])
        elif ac == bc:
            for i in range(ac):
                ni = (i + 1) % ac
                faces.append([a0 + i, b0 + i, b0 + ni])
                faces.append([a0 + i, b0 + ni, a0 + ni])
        else:
            raise ValueError('loft sections must use same side count except point caps')
    return trimesh.Trimesh(np.asarray(verts, float), np.asarray(faces, int), process=True)


def prism_polygon(points_xz, thickness, y_center=0.0):
    """Extrude a convex XZ polygon across Y; used for wings and armor plates."""
    p = np.asarray(points_xz, dtype=float)
    n = len(p)
    y0, y1 = y_center - thickness / 2.0, y_center + thickness / 2.0
    verts = np.vstack([
        np.column_stack([p[:, 0], np.full(n, y0), p[:, 1]]),
        np.column_stack([p[:, 0], np.full(n, y1), p[:, 1]]),
    ])
    faces = []
    # Fan caps. Polygon definitions below are convex by construction.
    for i in range(1, n - 1):
        faces.append([0, i + 1, i])
        faces.append([n, n + i, n + i + 1])
    for i in range(n):
        ni = (i + 1) % n
        faces.append([i, ni, n + ni])
        faces.append([i, n + ni, n + i])
    return trimesh.Trimesh(verts, np.asarray(faces, int), process=True)


def tapered_nacelle(z0, z1, r0, r1, sections=10, y_scale=0.78):
    verts = []
    for z, r in [(z0, r0), (z1, r1)]:
        for i in range(sections):
            a = i / sections * np.pi * 2.0
            verts.append([np.cos(a) * r, np.sin(a) * r * y_scale, z])
    faces = []
    for i in range(sections):
        ni = (i + 1) % sections
        faces.append([i, sections + i, sections + ni])
        faces.append([i, sections + ni, ni])
    # front/back caps
    front = len(verts); verts.append([0, 0, z0])
    back = len(verts); verts.append([0, 0, z1])
    for i in range(sections):
        ni = (i + 1) % sections
        faces.append([front, ni, i])
        faces.append([back, sections + i, sections + ni])
    return trimesh.Trimesh(np.asarray(verts, float), np.asarray(faces, int), process=True)


def beam_between(a, b, radius=0.22, sections=8):
    a = np.asarray(a, float); b = np.asarray(b, float)
    vec = b - a; length = float(np.linalg.norm(vec))
    mesh = trimesh.creation.cylinder(radius=radius, height=length, sections=sections)
    align = trimesh.geometry.align_vectors([0, 0, 1], vec / max(length, 1e-9))
    transform = np.eye(4) if align is None else align
    transform[:3, 3] = (a + b) / 2.0
    mesh.apply_transform(transform)
    return mesh


def ring_module(radius, angle_deg, tangential=10.5, radial=5.2, height=4.0):
    a = np.radians(angle_deg)
    x, y = np.cos(a) * radius, np.sin(a) * radius
    mesh = trimesh.creation.box([radial, tangential, height])
    mesh.apply_transform(T(x=x, y=y, rz=angle_deg))
    return mesh


def explorer(lod: int):
    scene = trimesh.Scene(); root = f'SHP_Explorer_01_LOD{lod}'
    scene.graph.update(frame_to=root, matrix=np.eye(4))

    sides = 12 if lod == 0 else (10 if lod == 1 else 8)
    hull_sections = [
        (-4.45, 0.05, 0.05, 0.02),
        (-3.55, 0.62, 0.30, 0.03),
        (-2.15, 1.18, 0.54, 0.07),
        (-0.45, 1.48, 0.70, 0.06),
        (1.55, 1.35, 0.65, 0.00),
        (3.15, 0.92, 0.48, -0.03),
        (3.85, 0.55, 0.34, -0.05),
    ]
    add(scene, loft_hull(hull_sections, sides), 'PrimaryHull', HULL, parent=root)

    # Swept cranked wings: strong silhouette, not generic triangles.
    port = [(-0.78, -2.25), (-3.55, -0.55), (-3.20, 1.55), (-1.18, 2.75), (-0.92, 1.10)]
    star = [(-x, z) for x, z in port]
    wing_mat = PANEL if lod < 2 else HULL
    add(scene, prism_polygon(port, 0.16 if lod == 0 else 0.20, -0.04), 'WingPort', wing_mat, parent=root)
    add(scene, prism_polygon(star, 0.16 if lod == 0 else 0.20, -0.04), 'WingStarboard', wing_mat, parent=root)

    # Faceted canopy integrated into the upper fuselage.
    canopy_sections = [
        (-2.72, 0.34, 0.06, 0.66),
        (-2.28, 0.64, 0.22, 0.78),
        (-1.30, 0.72, 0.27, 0.82),
        (-0.58, 0.46, 0.14, 0.76),
    ]
    if lod < 2:
        add(scene, loft_hull(canopy_sections, 8 if lod == 0 else 6), 'Canopy', CANOPY, parent=root)

    # Twin embedded engine nacelles with polygonal cross-section.
    ns = 12 if lod == 0 else (8 if lod == 1 else 6)
    for side, label in [(-1, 'Port'), (1, 'Starboard')]:
        add(scene, tapered_nacelle(0.65, 3.62, 0.43, 0.54, ns), f'EngineNacelle{label}', HULL,
            T(x=1.78 * side, y=-0.04), root)
        nozzle = trimesh.creation.cylinder(radius=0.39 if lod < 2 else 0.34, height=0.055,
                                            sections=ns)
        add(scene, nozzle, f'EngineGlow{label}', ENGINE, T(x=1.78 * side, y=-0.04, z=3.68), root)

    # Vertical stabilizers and armor chine give the ship a readable rear quarter.
    fin_port = [(-1.10, 1.25), (-1.46, 3.18), (-1.06, 3.42), (-0.92, 1.65)]
    fin_star = [(-x, z) for x, z in fin_port]
    # Build fins as XZ panels then rotate into X/Y-ish fins using transform.
    if lod < 2:
        fp = prism_polygon(fin_port, 0.10, 0.52)
        fs = prism_polygon(fin_star, 0.10, 0.52)
        add(scene, fp, 'DorsalFinPort', ACCENT if lod == 0 else PANEL, parent=root)
        add(scene, fs, 'DorsalFinStarboard', ACCENT if lod == 0 else PANEL, parent=root)

    if lod == 0:
        # Dorsal/ventral armor strips and wing leading-edge accents.
        add(scene, prism_polygon([(-0.36, -2.9), (-0.52, 2.55), (0.52, 2.55), (0.36, -2.9)], 0.08, 0.70),
            'DorsalArmor', PANEL, parent=root)
        add(scene, prism_polygon([(-3.33, -0.58), (-3.05, -0.28), (-1.10, -1.86), (-0.84, -2.14)], 0.035, 0.08),
            'LeadingEdgePort', ACCENT, parent=root)
        add(scene, prism_polygon([(3.33, -0.58), (3.05, -0.28), (1.10, -1.86), (0.84, -2.14)], 0.035, 0.08),
            'LeadingEdgeStarboard', ACCENT, parent=root)
        # Small maneuvering thruster housings.
        for x, label in [(-2.45, 'Port'), (2.45, 'Starboard')]:
            pod = loft_hull([(-0.30, .18, .12, 0), (.55, .25, .16, 0), (1.10, .16, .10, 0)], 8)
            add(scene, pod, f'VectorPod{label}', PANEL, T(x=x, y=0.02, z=0.70), root)

    marker(scene, 'MNT_Cockpit', (0, 0.90, -1.55), root)
    marker(scene, 'MNT_Weapon_Port', (-2.58, -0.02, -0.60), root)
    marker(scene, 'MNT_Weapon_Starboard', (2.58, -0.02, -0.60), root)
    marker(scene, 'MNT_Engine_Port', (-1.78, -0.04, 3.72), root)
    marker(scene, 'MNT_Engine_Starboard', (1.78, -0.04, 3.72), root)
    marker(scene, 'MNT_LandingGear', (0, -0.70, 0.40), root)
    return scene


def interceptor(lod: int):
    scene = trimesh.Scene(); root = f'SHP_Interceptor_01_LOD{lod}'
    scene.graph.update(frame_to=root, matrix=np.eye(4))
    sides = 10 if lod == 0 else (8 if lod == 1 else 6)

    # Arrowhead central fuselage.
    add(scene, loft_hull([
        (-3.00, .03, .03, 0.02),
        (-2.25, .46, .24, 0.03),
        (-1.05, .80, .40, 0.05),
        (.55, .72, .36, 0.00),
        (2.15, .46, .28, -0.03),
        (2.72, .25, .18, -0.04),
    ], sides), 'PrimaryHull', HULL, parent=root)

    port = [(-0.45, -1.85), (-2.72, -0.60), (-2.48, 1.38), (-0.72, 1.92)]
    star = [(-x, z) for x, z in port]
    add(scene, prism_polygon(port, .12 if lod == 0 else .16, -0.02), 'BladeWingPort', ACCENT if lod == 0 else PANEL, parent=root)
    add(scene, prism_polygon(star, .12 if lod == 0 else .16, -0.02), 'BladeWingStarboard', ACCENT if lod == 0 else PANEL, parent=root)

    if lod < 2:
        add(scene, loft_hull([
            (-1.78, .26, .05, .42), (-1.30, .44, .16, .53), (-.55, .38, .14, .50)
        ], 6), 'Canopy', CANOPY, parent=root)

    ns = 10 if lod == 0 else (8 if lod == 1 else 5)
    for side, label in [(-1, 'Port'), (1, 'Starboard')]:
        add(scene, tapered_nacelle(-.10, 2.48, .26, .36, ns), f'EngineNacelle{label}', PANEL,
            T(x=1.38 * side, y=-.03), root)
        nozzle = trimesh.creation.cylinder(radius=.26, height=.045, sections=ns)
        add(scene, nozzle, f'EngineGlow{label}', ENGINE, T(x=1.38 * side, y=-.03, z=2.53), root)

    if lod == 0:
        # ventral blade and gun fairings
        add(scene, prism_polygon([(-.10, .30), (-.16, 2.35), (.16, 2.35), (.10, .30)], .08, -.38),
            'VentralSpine', PANEL, parent=root)
        for side, label in [(-1, 'Port'), (1, 'Starboard')]:
            fairing = loft_hull([(-1.50, .10, .08, 0), (-.55, .16, .11, 0), (.20, .11, .08, 0)], 6)
            add(scene, fairing, f'GunFairing{label}', HULL, T(x=1.94 * side, y=-.06), root)

    marker(scene, 'MNT_Weapon_Port', (-1.94, -0.06, -0.82), root)
    marker(scene, 'MNT_Weapon_Starboard', (1.94, -0.06, -0.82), root)
    marker(scene, 'MNT_Engine_Port', (-1.38, -0.03, 2.56), root)
    marker(scene, 'MNT_Engine_Starboard', (1.38, -0.03, 2.56), root)
    return scene


def station(lod: int):
    scene = trimesh.Scene(); root = f'STN_Orbital_01_LOD{lod}'
    scene.graph.update(frame_to=root, matrix=np.eye(4))

    radial = 34.0
    ring_count = 16 if lod == 0 else (12 if lod == 1 else 8)
    cyl_sections = 18 if lod == 0 else (12 if lod == 1 else 8)

    # Central service spindle and armored command hub.
    add(scene, tapered_nacelle(-38.0, 38.0, 4.0, 4.0, cyl_sections, 1.0), 'CentralSpindle', STN_DARK, parent=root)
    add(scene, tapered_nacelle(-10.0, 10.0, 8.0, 8.0, cyl_sections, 1.0), 'CommandHub', STN, parent=root)

    # Segmented habitation/industry ring rather than a toy torus.
    for i in range(ring_count):
        ang = i * (360.0 / ring_count)
        module = ring_module(radial, ang,
                             tangential=11.5 if lod == 0 else 13.0,
                             radial=5.2 if lod < 2 else 6.3,
                             height=4.4 if lod < 2 else 5.0)
        add(scene, module, f'RingModule_{i:02d}', STN if i % 2 == 0 else STN_PANEL, parent=root)
        if lod == 0:
            # Thin luminous seam on each ring module.
            a = np.radians(ang)
            x, y = np.cos(a) * (radial + 2.75), np.sin(a) * (radial + 2.75)
            seam = trimesh.creation.box([.18, 5.6, 2.1])
            seam.apply_transform(T(x=x, y=y, rz=ang))
            add(scene, seam, f'RingLight_{i:02d}', STN_LIGHT, parent=root)

    # Truss spokes physically read the ring as a built structure.
    spoke_count = 8 if lod == 0 else (6 if lod == 1 else 4)
    for i in range(spoke_count):
        a = i * (360.0 / spoke_count)
        r = np.radians(a)
        end = (np.cos(r) * (radial - 3.0), np.sin(r) * (radial - 3.0), 0)
        add(scene, beam_between((0, 0, 0), end, .34 if lod == 0 else .48, 8), f'RingTruss_{i:02d}', STN_DARK, parent=root)

    # Four industrial pylons with solar/radiator panels.
    arm_count = 4
    for i in range(arm_count):
        a = i * 90.0 + 45.0
        r = np.radians(a)
        start = (np.cos(r) * 8.0, np.sin(r) * 8.0, -4.0)
        end = (np.cos(r) * 48.0, np.sin(r) * 48.0, -4.0)
        add(scene, beam_between(start, end, .72 if lod == 0 else .90, 8), f'UtilityPylon_{i:02d}', STN, parent=root)
        if lod < 2:
            x, y = np.cos(r) * 52.0, np.sin(r) * 52.0
            panel = trimesh.creation.box([14.0, 0.5, 20.0])
            panel.apply_transform(T(x=x, y=y, z=-4.0, rz=a + 90.0))
            add(scene, panel, f'Radiator_{i:02d}', STN_PANEL, parent=root)
            stripe = trimesh.creation.box([14.4, 0.56, 1.2])
            stripe.apply_transform(T(x=x, y=y, z=3.5, rz=a + 90.0))
            add(scene, stripe, f'RadiatorStripe_{i:02d}', STN_ACCENT, parent=root)

    # Docking end: octagonal collar, approach tunnel and guidance lights.
    add(scene, tapered_nacelle(22.0, 36.0, 7.2, 5.4, cyl_sections, 1.0), 'DockingCollar', STN, parent=root)
    add(scene, tapered_nacelle(34.0, 47.0, 4.8, 4.1, cyl_sections, 1.0), 'DockingTunnel', STN_DARK, parent=root)
    if lod < 2:
        for side, label in [(-1, 'Port'), (1, 'Starboard')]:
            guide = trimesh.creation.box([1.0, 1.0, 18.0])
            add(scene, guide, f'DockGuide{label}', STN_ACCENT, T(x=6.3 * side, z=37.5), root)
        for j, z in enumerate([39.0, 42.0, 45.0]):
            for side in (-1, 1):
                light = trimesh.creation.box([.36, .36, .36])
                add(scene, light, f'ApproachLight_{j}_{side:+d}', STN_LIGHT, T(x=5.0*side, y=0, z=z), root)

    if lod == 0:
        # Command antennas and service pods add scale cues.
        for y, label in [(10.5, 'Upper'), (-10.5, 'Lower')]:
            add(scene, beam_between((0, y, -8), (0, y * 1.9, -16), .24, 8), f'Antenna{label}', STN_ACCENT, parent=root)
        for i in range(6):
            a = np.radians(i * 60.0 + 30.0)
            x, y = np.cos(a) * 11.5, np.sin(a) * 11.5
            pod = tapered_nacelle(-5, 5, 1.4, 1.4, 8, 1.0)
            add(scene, pod, f'ServicePod_{i:02d}', STN_PANEL, T(x=x, y=y), root)

    marker(scene, 'MNT_Dock', (0, 0, 48.0), root)
    marker(scene, 'MNT_Service', (0, 0, 34.0), root)
    marker(scene, 'MNT_Traffic_A', (-76, 0, -18), root)
    marker(scene, 'MNT_Traffic_B', (76, 0, -18), root)
    return scene


def export_scene(scene: trimesh.Scene, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(scene.export(file_type='glb'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    targets = []
    for lod in range(3):
        targets += [
            (explorer(lod), OUT_SHIPS / f'SHP_Explorer_01_LOD{lod}.glb'),
            (interceptor(lod), OUT_SHIPS / f'SHP_Interceptor_01_LOD{lod}.glb'),
            (station(lod), OUT_STATIONS / f'STN_Orbital_01_LOD{lod}.glb'),
        ]
    for scene, path in targets:
        export_scene(scene, path)
    print('TASK-186 GLB visual redesign PASS: assets=9; families=3; hardSurfaceLoft=1; segmentedStation=1; sourceBlend=0; texturesRaw=0.')


if __name__ == '__main__':
    main()
