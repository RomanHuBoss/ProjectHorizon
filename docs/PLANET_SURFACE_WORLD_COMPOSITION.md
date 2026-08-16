# TASK-160 — Planet Surface World Composition & Persistence

## Scope

TASK-160 promotes the live surface from a technical streaming test field into a coherent planet scene without changing the TASK-158 terrain identity or the legacy Stage-1 acceptance fixtures.

The runtime chain is now:

`planet + star -> sky / sunlight / atmosphere / clouds -> lit streamed terrain -> distributed POIs -> chunk resources -> delta persistence`.

## Surface rendering

The active planet environment is converted into a deterministic `PlanetSurfaceSkyProfile`. The live `WorldEnvironment` uses a procedural sky as its background and ambient/reflection source, a star-type-colored `DirectionalLight3D`, exponential fog with aerial perspective, and an inexpensive deterministic cloud layer. The terrain remains PBR-lit but has a deliberately weak planet-colored emissive floor so an adverse sun angle cannot collapse the terrain to absolute black.

The star direction is deterministic for a planet seed in this stage. Full astronomical day/night rotation remains a later orbital/spherical-frame task.

## Live POI composition

The reviewed TASK-108/TASK-154 POI plans and stable instance IDs are unchanged. Only their live presentation coordinates are spread deterministically through a 78–420 m exploration annulus. Scanner and interaction operate on those live nodes, while discovery persistence remains keyed by the existing stable POI IDs. POI scene nodes are visible/collidable only while their chunk lies inside the current TASK-158 5x5 terrain residency window, so distant sites do not float beyond loaded terrain. This avoids invalidating the reviewed golden world-generation fixture.

## Resource composition

The old 58 physical catalog nodes remain in the scene/controller for legacy TASK-076/TASK-100 structural acceptance, but all except `salvage.alpha`, `salvage.beta` and `salvage.gamma` are runtime-suppressed during normal gameplay.

The live surface uses deterministic chunk-scoped deposits:

- at most two deposits per TASK-158 chunk;
- a 28 m starter reserve around the repair/tutorial site;
- terrain slope filtering;
- archetype/tag-weighted resource selection;
- stable identities derived from `planet + chunk X/Z + slot`;
- only the current 5x5 surface window is instantiated.

## Persistence model

Untouched procedural deposits are not written to the save. They regenerate from the same deterministic world identity whenever their chunk returns.

When a deposit is collected, its stable `surface_resource.*` node ID is stored through the existing `StarterRepairSession` inventory snapshot. Cold restore uses `FromSnapshotWithDynamicResources` to reconstruct the binding even when the depleted chunk is not resident. On a later return, generation produces the same node ID and the scene layer suppresses it because the session already contains that ID.

Therefore persistence cost is proportional to player changes, not to the number of procedural deposits that could exist across a planet.

## Compatibility

TASK-160 intentionally does not change:

- `ProjectHorizonGenerator.Version = 3` or the reviewed TASK-138 system/POI golden fixture;
- TASK-156 terrain morphology identity;
- TASK-158 chunk geometry/residency;
- legacy resource-node IDs used by Stage-1 tests;
- existing POI discovery IDs or per-planet ecology/POI delta archives.

## Acceptance

F5 runs `TASK-160 planet surface world composition acceptance` and requires:

- four starter sky profiles;
- visible star contract and atmospheric profile contract;
- cloud-count policy;
- deterministic resource windows;
- starter reserve clearance;
- planet-scoped unique resource IDs;
- dynamic depletion cold restore;
- zero procedural-resource save deltas for untouched resources.

Manual acceptance should additionally verify that terrain is visibly lit, the star/sky/haze are visible, the old catalog resource wall is gone, POIs are no longer piled around the landing pad, and a mined streamed resource remains absent after leaving the chunk/planet, saving, and returning.
