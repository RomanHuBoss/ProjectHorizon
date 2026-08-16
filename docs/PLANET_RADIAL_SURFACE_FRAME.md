# TASK-170 — Radial Planetary Physics & Cube-Face Surface Traversal Foundation

## Purpose

TASK-170 bridges the TASK-162 floating-origin surface and TASK-168 spherical globe. The project keeps a numerically small Godot tangent patch for gameplay, but every on-foot position now has a planet-global radial orientation, cube-sphere face and planet-specific gravity magnitude.

## Coordinate contract

- Persistence/streaming address: double-precision logical East/North metres (unchanged).
- Global geographic address: normalized latitude/longitude on the current planet radius.
- Global radial frame: orthonormal `East`, `Up`, `North`; `Up` is the planet-center radial normal.
- Cube-sphere address: one of `+X/-X/+Y/-Y/+Z/-Z` plus bounded `u/v` in `[-1,1]`.
- Local Godot gameplay frame: moving tangent patch where `+Y` is defined as the current radial-up axis.

## Gravity

`PlayerController` receives `surfaceGravityG * 9.80665 m/s²` from the active planet. Because TASK-170 intentionally retains the local tangent collision patch, gravity is applied along local `-Y`; the corresponding global direction is the radial inward vector of the current tangent frame.

## Geodesic traversal

`PlanetSurfaceTopologyRuntime.GeodesicStep` uses the spherical exponential map for local East/North displacement. `surface_warp <lat> <lon>` converts a normalized geographic target back into the canonical logical cover, restores the floating-origin frame there, samples the same deterministic terrain identity and re-centres streaming/resources/POIs. It is a developer acceptance tool, not a player fast-travel feature.

## Cube-face transitions

The radial runtime reports cube-face changes and verifies basis continuity across seams. Face transitions do not change resource/terrain IDs or save schema. The live terrain streamer remains 25 active chunks with 9 collision chunks.

## Explicit non-goals

TASK-170 does not yet replace the local heightfield with a globally curved physical cube-sphere mesh, rotate CharacterBody collision around the planet in one global Godot frame, rebuild NavigationServer across faces, or claim manual physical circumnavigation. Those changes require a later collision/navigation migration and must preserve all TASK-124/158/162 invariants.

## Acceptance

F5 `TASK-170 radial surface frame acceptance` requires planet gravity scaling, orthonormal tangent frames, all six cube faces, bounded face UV, seam continuity, exact 1 km geodesic step, geographic warp round-trip and the unchanged 25/9 streamer budget. Developer smoke should use `surface_warp` at representative face centres and around a 45-degree seam.
