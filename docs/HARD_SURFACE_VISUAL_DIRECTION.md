# TASK-186 — Hard-Surface Visual Direction

TASK-186 replaces the technically functional but visually weak TASK-184 primitive-composition baseline. The runtime contract remains GLB + separate LOD0/LOD1/LOD2 + `MNT_*` attachment points + external Godot collision, while the shape language is rebuilt around intentional hard-surface silhouettes.

## Player explorer

The explorer now uses a **lofted fuselage** with flattened manufactured shoulders, a long faceted nose, cranked swept wings, twin polygonal engine nacelles, a faceted smoked canopy, dorsal stabilizers, armor strips, leading-edge accents and maneuvering pods. The primary silhouette is defined by the hull and wings rather than by spheres or cylinders.

## NPC interceptor

The interceptor uses a compact arrowhead pressure hull, blade wings, embedded twin nacelles, a faceted canopy, ventral spine and gun fairings. It is deliberately more aggressive and narrower than the explorer so the two ships remain readable at combat distance.

## Orbital station

The station is no longer a torus with boxes attached. It uses a **segmented ring** made from individual habitation/industry modules, structural truss spokes, a central spindle and command hub, four utility pylons with radiator arrays, a dedicated docking collar/tunnel and approach guidance lights. The segmentation and service pods are intended to provide human-scale cues during approach.

## Materials

The palette is restrained graphite/steel with limited safety-orange accents, smoked canopy material and cyan engine/approach emission. The goal is an industrial aerospace look rather than saturated toy-blue presentation. Material slots remain bounded for the existing production pipeline.

## Runtime contract

- GLB contains **no gameplay collision**; existing `CollisionShape3D` remains authoritative.
- LOD controller, mount markers, station docking envelope and flight physics are unchanged.
- Legacy procedural presentation remains hidden fallback only.
- LODs preserve the same principal silhouette and progressively remove small details.

## Acceptance boundary

Structural/runtime acceptance can verify that the redesigned assets import, contain the required hard-surface signature nodes, preserve LOD/collision contracts and replace the old presentation. **Manual visual acceptance** is still required because no static metric can prove that an art direction is attractive. Owner screenshots of player exterior, nearby NPC and station approach are the acceptance evidence for the aesthetic portion.
