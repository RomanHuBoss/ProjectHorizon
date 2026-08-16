# TASK-172 — Physical Radial Surface Frame & Navigation Migration

## Purpose

TASK-172 is the physical bridge between the verified planet-global geodesy/radial mathematics (TASK-168/TASK-170) and a future globally curved cube-sphere collision surface. It deliberately preserves the proven bounded terrain/navigation budget while rotating that local patch into the correct planet tangent basis.

## Coordinate contract

Persistent planet coordinates remain **logical East / height / North**. They do not rotate and they remain the save/chunk/resource/POI identity space.

For a selected logical origin, `PlanetSurfacePhysicalFrameRuntime` builds:

- `X = East`;
- `Y = radial Up`;
- `Z = North`;
- a `GameplayTransform` that both recentres the floating origin and rotates the physical patch.

Logical points are mapped by `GameplayTransform`; world points are restored with its affine inverse. Frame changes therefore preserve logical identity while the visible/physical representation rotates.

## Player physics

`PlayerController` now supports arbitrary surface Up:

- gravity acts along `-Up` with current planet gravity magnitude;
- `CharacterBody3D.UpDirection` follows radial Up;
- movement is projected onto the tangent plane;
- jump, jetpack and swim vertical motion use radial Up;
- body orientation preserves heading while aligning the capsule/body Y axis to radial Up;
- velocity and body basis are remapped during physical-frame transitions.

## Terrain and collision

The live gameplay budget remains unchanged:

- 25 active terrain chunks;
- 9 high-detail collision chunks;
- bounded local NavigationServer residency.

`Gameplay`, fallback `GroundBody` and `TerrainChunkManager` rotate into the same tangent basis. `TerrainChunkManager` converts world probes through its own local transform before adding the logical origin, so chunk addressing remains deterministic after rotation.

This is **not** a globally curved collision mesh yet. Each resident patch is still a tangent heightfield.

## Navigation and AI

TASK-124 navigation regions are children of the rotating surface root and therefore inherit the physical tangent basis. The surface runtime additionally:

- forces NavigationServer re-synchronization after frame handoff;
- performs recovery-waypoint lateral offsets in surface-local coordinates;
- builds acceptance path probes in the rotating local frame;
- remaps cached world-space targets for faction NPCs;
- remaps NPC and fauna velocities across frame changes;
- keeps flying-fauna altitude, weather drift and surface movement relative to local Up;
- makes surface NPC-ship altitude/formations use the surface basis.

## Seam testing

Developer command:

```text
surface_warp <latitudeDeg> <longitudeDeg>
```

Recommended seam smoke:

```text
surface_warp 0 44.9
surface_warp 0 45.1
```

Expected diagnostics include both the TASK-170 logical cube-face transition and:

```text
TASK-172 physical cube-face handoff PASS: +X->+Z; ...
```

After handoff the terrain streamer must settle again at `25/25` active and `9/9` collision chunks, the player must remain aligned with the terrain normal, and navigation must resume after its synchronization gate.

## Persistence

No SQLite migration is introduced. Save identity remains logical East/North plus existing subsystem deltas. Physical transforms are reconstructed from the current planet environment and logical origin.

## Deferred work

A later subsystem may replace the rotating tangent patch with truly curved collision/nav tiles. TASK-172 does not claim:

- a global cube-sphere collision mesh;
- continuously curved collision inside one patch;
- NavigationServer routing across nonresident faces;
- full physical circumnavigation without bounded-patch handoff.
