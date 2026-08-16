# TASK-158 — Planetary Surface Streaming & Traversal Foundation

## Purpose

TASK-158 promotes the previously verified Prototype-B terrain chunk pipeline into the live `SalvageRepairSlice` surface runtime. The active planet no longer depends on the bounded 80 x 80 m TASK-156 mesh as its permanent playable ground. Instead, a moving, bounded chunk window follows the player and samples the current planet's deterministic TASK-156 terrain profile.

This is deliberately an integration/promotion step rather than a second terrain engine. Existing background generation, cancellation, stale-result protection, LOD stitching, skirts and safe unload semantics remain owned by `TerrainChunkManager`.

## Runtime contract

- Chunk size: 32 m.
- Active window: 5 x 5 chunks (25 maximum resident visual chunks).
- LOD0: central 3 x 3 chunks, 33 x 33 vertices.
- LOD1: outer 16 chunks, 17 x 17 vertices.
- Collision: central 3 x 3 chunks only, 33 x 33 collision samples.
- Chunk-center switching uses the existing 3 m hysteresis.
- Visual outer edges retain skirts; LOD0/LOD1 borders retain stitching.
- Terrain workers sample `PlanetSurfaceTerrainRuntime` using the exact world position of each streamed vertex.
- Mesh/collision resource mutation remains on the Godot main thread.
- Profile changes cancel the old generation plan; revision/stale-result guards prevent another planet's chunks from being applied later.
- During live traversal the newly entered collision/LOD0 band is promoted before visual-only outer creates/demotions, preserving a collision cushion in front of the player even when workers are slower than normal.

## Startup and planet switching

The TASK-156 65 x 65 local mesh remains a compatibility fallback while the first streaming plan is being generated. Once the streamer reports a settled 25-chunk plan, the fallback mesh and trimesh collision are retired. On a planet-profile switch the fallback is restored while the destination plan is rebuilt.

This handoff keeps the player from losing ground collision simply because background workers have not completed yet.

## Traversal and navigation

`NpcNavigationSurfaceNode` keeps its existing 5 x 5 bounded NavigationRegion3D residency around the player, but terrain-aware navigation is no longer clipped to the original 80 x 80 m authored patch. TASK-158 defines an 8,192 m tangent-plane traversal envelope. Streamed `TerrainChunk` collision bodies are explicitly excluded from the obstacle catalogue, because they are the ground itself.

The surface HUD also exposes a deterministic geodesic address derived from the active planet radius and the player's east/north displacement. This is an addressing/diagnostic bridge for later cube-sphere promotion; it does not claim that the live character controller already walks on a physically curved sphere.

## World residency

`PlanetSurfaceStreamer` is part of the same surface-runtime residency set as ground, water, ecology, POIs and NPC navigation. Orbit, station interiors and hyperspace can therefore suspend it through the existing TASK-148 world-scene coordinator rather than leaving terrain workers active in non-surface contexts.

## Compatibility

- No save-schema migration.
- No procedural-generator version bump: TASK-158 changes runtime residency/geometry delivery, not deterministic planet/system identities.
- TASK-156 terrain sampler, starter terrace, water basins, POI/ecology IDs and per-planet persistence remain unchanged.
- Prototype-B default behavior remains available because planet-surface sampling is opt-in through `ConfigurePlanetSurface`.

## Acceptance

1. `tools\clean-build-windows10.cmd` must complete with 0 warnings and 0 errors.
2. `tools\run-section37-quality.cmd` must include `TASK-158 PLANET SURFACE STREAMING CONTRACT PASS` and the xUnit suite must remain green.
3. F5 must print `TASK-158 planet surface streaming acceptance PASS` with 25 active-plan specs, 9 LOD0, 16 LOD1 and 9 collision chunks.
4. Runtime must print `TASK-158 planet surface streaming READY` after the initial worker plan settles and the TASK-156 fallback is retired.
5. Walk/jetpack beyond the old ±40 m boundary and cross at least five 32 m chunk centers. Terrain must continue without a hard edge or fall-through; the HUD sector must change while resident chunks remain bounded at 25.
6. TASK-124 navigation acceptance must remain PASS; ground NPCs near the active traversal area must not treat streamed terrain as an obstacle.
7. Switch to another landable planet and confirm the streamer rebuilds for the destination archetype without briefly applying old-planet relief after the handoff.

## Known boundary / next scaling step

TASK-158 is a **planetary surface streaming foundation**, not the final whole-sphere traversal implementation. The live gameplay frame is still a tangent plane and does not yet promote the verified Prototype-C cube-sphere/floating-origin work into the player/ship surface lifecycle. Ecology/POI/resource authoring is also still centered on the existing starter-region plans rather than chunk-streamed planet-wide content. The next planet-scale step should integrate surface frames, cube-face/quadtree addressing and floating-origin rebasing while retaining this bounded chunk residency contract; planet-wide content streaming can then key off the same surface address.
