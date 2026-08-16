# TASK-156 — Planet-Specific Terrain & Surface Geometry

## Purpose

TASK-156 closes the geometry gap left after TASK-150/TASK-154. Planet climate, biome, ecology and POI state were already planet-scoped, but the playable vertical-slice surface still used one flat `80 x 80 m` box. The new terrain layer keeps the existing bounded Stage 2 surface residency model while making the active landable planet own deterministic local relief, collision and navigation height.

## Terrain contract

`PlanetSurfaceTerrainRuntime` derives a `PlanetSurfaceTerrainProfile` from the current `PlanetEnvironmentProfile` and stable planet seed. The local surface remains `80 x 80 m`, matching the existing vertical-slice and bounded navigation footprint. Runtime geometry uses a `65 x 65` grid (4,225 vertices / 8,192 triangles) with deterministic fBm/value-noise morphology and archetype shaping.

Starter-world morphology is intentionally distinct:

- temperate — rolling mixed hills;
- desert — dunes plus softened mesa structure;
- frozen — smoother ridge/drift relief;
- volcanic — the largest relief budget, ridges and a deterministic crater feature.

The other landable archetypes also have dedicated bounded amplitude/frequency profiles. The same sampler is used by mesh generation, collision projection, ecology, POI terrain constraints and navigation, so there is no separate visual-only height source.

## Gameplay-safe terrace and water

The central tutorial/infrastructure area is a deterministic terrace: relief is suppressed inside a 16 m radius and blended to full relief by 23 m. This preserves the starter repair ship, core production stations, early building interactions and authored tutorial spacing.

Wet planets generate two protected local depressions matching the existing gameplay water volumes: the interaction pool around `(22, 22)` and the aquatic ecology habitat around `(-25.5, 25.5)`. Dry planets do not create those basin floors and keep the TASK-154 aquatic exclusion policy.

## Geometry and collision

`SalvageRepairSlicePlanetTerrain` replaces the flat ground mesh at runtime with an `ArrayMesh` built through `SurfaceTool`. Per-vertex normals and archetype/height/slope color modulation are generated from the same terrain sampler. The ground `CollisionShape3D` is replaced with the mesh `ConcavePolygonShape3D`, so physics follows the visible relief.

Legacy authored scene data is not rewritten. Planet change simply rebuilds the active bounded surface from `(planetId, seed, archetype)`.

## Ecology and exploration integration

For non-legacy planets, `EcologyPlanner.PlanPlanet` receives the terrain profile. Flora records receive sampled surface Y, ground fauna are spawned and continuously re-grounded against the terrain, while flying fauna retain their altitude envelope above their territory. The starter world's historical flora/fauna instance IDs and X/Z positions stay unchanged; scene projection supplies the new Y coordinate without invalidating persistence deltas.

`PlanetaryPoiPlanner.PlanPlanet` now uses physical terrain slope in candidate constraints. The existing POI `Height` field remains a bounded local-relief constraint band for backward compatibility, while runtime scene placement is projected onto the exact terrain Y. The legacy starter POI IDs and X/Z positions therefore remain save-compatible.

## Navigation and construction

`NpcNavigationSurfaceNode` no longer requires the ground collider to remain a `BoxShape3D` when a terrain profile is active. Each `NavigationRegion3D` vertex receives terrain height, cells above the archetype's maximum walkable slope are excluded, and avoidance obstacles use local terrain Y. NPC agents no longer force themselves back to `_home.Y`; they use navigation-surface height after movement.

Base-construction preview and rebuilt modules are projected onto sampled surface height. The persistent construction grid remains X/Z-only, preserving save compatibility and power/connectivity semantics.

## Acceptance

F5 now includes `TASK-156 planet terrain acceptance`. It checks all four starter planets for:

- four distinct deterministic morphology signatures;
- stable central tutorial terrace;
- bounded relief and walkable coverage;
- wet/dry basin policy;
- terrain-grounded ecology;
- terrain-aware POI constraints;
- unchanged legacy instance identity;
- the fixed 4,225-vertex / 8,192-triangle geometry contract.

The repository quality gate `tools/validate-task156-planet-surface-terrain.py` and three xUnit regressions cover the static/runtime-facing contract. Final verification still requires a Windows/Godot clean build and F5/manual visual traversal.
