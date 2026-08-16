# TASK-174 — Curved Cube-Sphere Surface Collision

TASK-174 promotes the bounded radial tangent patch from TASK-172 into a physically curved local surface without abandoning the stable streaming budget. Persistent addresses remain planet-logical East/North coordinates. For the active floating-origin tangent anchor `(E0,N0)`, every surface vertex uses exact sphere sag

`y_physical = terrain_height - (R - sqrt(R^2 - d^2))`, where `d^2=(E-E0)^2+(N-N0)^2`.

The same `PlanetSurfaceCurvedPatchDescriptor` is consumed by streamed visual terrain, trimesh collision, navigation tiles, distant presentation, player radial Up and curvature-aware frame/rebase handoff. Terrain residency remains 25 visual chunks and 9 collision chunks; curvature changes trigger asynchronous updates rather than global planet collision residency.

The atmosphere is also aligned to the active radial frame. Global-Y height fog is disabled on the surface because it becomes a vertical slab when local radial Up is not world Y; isotropic/aerial fog remains active. Atmospheric planets retain a dim blue night dome rather than the near-black vacuum palette used by airless bodies.

F5 runs `TASK-174 curved cube-sphere surface acceptance`. It verifies all six cube faces, spherical sag/normals, curvature-aware rebase round-trip, live curved terrain collision, curved TASK-124 navigation, radial player Up, radial sky alignment and the unchanged 25/9 residency budget.

## Curvature-anchor resident remap

Floating-origin changes preserve semantic terrain height for Node3D residents and MultiMesh flora separately. MultiMesh instance transforms are adjusted explicitly because they are not individual scene-tree nodes. Cloud-root altitude is also referenced to the curved tangent baseline so long traversals do not lift cloud decks away from the planet.
