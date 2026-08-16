# TASK-162 — Planet-Global Surface Frame & Floating Origin

## Purpose

TASK-162 closes the coordinate-frame scaling layer between the bounded TASK-158 terrain streamer and long-distance traversal on a planet surface. The gameplay scene now separates two coordinate spaces:

- **planet-logical East/North** — deterministic double-precision coordinates used for terrain sampling, chunk IDs, resource/POI identity, base placement, maps and persistence;
- **local Godot scene space** — a bounded frame kept close to the origin for player/physics/rendering precision.

This is a floating-origin subsystem. It does **not** claim curved cube-sphere collision, continuously rotating gravity, or cube-face transitions; the current terrain topology remains a tangent heightfield while its coordinate frame is no longer limited to a small authored patch.

## Frame policy

`PlanetSurfaceFrameRuntime` uses 4096 m cells and triggers a rebase after a local X or Z component exceeds 2048 m. The shift is an exact cell multiple, so after a rebase the player returns to the `[-2048, +2048] m` local interval while logical coordinates remain continuous.

The logical navigation envelope is 300 km per axis. This covers the relevant geodesic scale of the configured 20–80 km planet radii while keeping bounded residency: terrain remains 25 active chunks, with 9 high-detail/collision chunks around the current logical chunk.

## Runtime integration

A live rebase updates the frame origin and shifts the local player by the opposite cell delta. The following systems use the same logical frame:

- `TerrainChunkManager`: logical chunk selection and sampling, but local chunk node placement relative to frame origin;
- TASK-160 procedural resources and POI residency;
- planet terrain/geodesic HUD and local planet map;
- base-construction targeting and preview;
- NPC surface-navigation streaming/obstacle coordinates and ground-NPC global navigation caches;
- NPC-ship absolute route waypoints plus aerial obstacle/POI steering caches;
- flying-fauna logical territory centers and aerial entity refresh;
- Stage-1 voyage surface/station targets and saved ship flight coordinates;
- autosave/graceful-exit player X/Z persistence.

`Gameplay` is translated by the frame origin. The short-lived fallback `GroundBody` stays at local `(0,0,0)`, while its mesh/collision are generated around the current **logical** frame origin; this keeps cold loads far from the starter area grounded before async chunks settle. Procedural resource/cloud roots are parented under `Gameplay`, so surface content participates in the same translation.

## Persistence compatibility

There is no SQLite schema bump. Existing `Player.PositionX/PositionZ` fields are interpreted as planet-logical coordinates. Legacy saves near the starter area naturally restore with origin `(0,0)`. A distant save uses the saved logical position as the initial floating origin during load and reconstructs a zero-near/bounded local position before gameplay resumes.

Stage-1 ship position state is also normalized through the frame when entering/leaving the Godot scene, so a surface rebase cannot silently corrupt voyage coordinates. New-slot/reset always resets the frame explicitly, and a piloted cold load seeds the frame from the saved ship logical X/Z rather than inheriting the previous slot origin.

## Acceptance

`F5` runs `TASK-162 planet-global surface frame acceptance`. TASK-162 itself is a small deterministic CPU probe (normally well below one second; the complete pre-existing F5 matrix can take longer). For the current constants the expected TASK-162 line is `rebases=48; traversalSamples=49; maxLocal=2030.709m`, followed by all boolean invariants equal to `1`. The deterministic probe traverses a >150 km logical route and verifies:

- local coordinates remain within the rebase threshold;
- logical position is continuous through all rebases;
- logical terrain chunk identity is unchanged;
- cold restore reconstructs the same logical coordinates in a bounded local frame;
- changing planet resets frame origin/state;
- planet-radius geodesic addressing remains finite and bounded.

The repository also contains three xUnit regressions and `tools/validate-task162-planet-global-surface-frame.py`, integrated into both section-37 quality runners.

## Manual runtime smoke

For live Godot verification, travel more than 2048 m along surface X or Z. Output must contain `TASK-162 planet surface REBASE` with `continuityError=0.000000m` (within formatting tolerance). The HUD `surface frame` line must show a bounded local X/Z and a growing logical X/Z/origin. Terrain must remain settled at 25 active chunks, and returning/saving/restarting must preserve the logical location and depleted procedural-resource identities.
