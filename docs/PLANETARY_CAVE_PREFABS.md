# TASK-192 — Planetary Cave Prefabs, Subsurface POIs & Resource Deposits

## Normative scope

TASK-192 closes PDF-TZ v2.0 §9.9. Version 1.0 does **not** generate a global procedural cave network and does **not** permit player terrain deformation. Caves are authored/procedural prefab objects attached to `poi.cave_entrance`; resources are mined from discrete deposit objects.

## Runtime model

`PlanetaryCaveRuntime` deterministically maps the current planet plus cave-entrance POI identity to one of three prefab archetypes: basalt lava tube, crystal grotto, or hydrothermal hollow. Each plan has an isolated subsurface pocket at least 36 m below the surface, a collision-backed walkable shell, an interactive return portal, and three stable `cave.deposit.*` resource IDs.

The surface terrain mesh is never cut, voxelized, rewritten, or persisted as deltas. Entering a discovered cave teleports the on-foot player from the surface entrance into the isolated prefab pocket; exiting returns to the stored surface logical position. A save requested while inside a cave stores the safe exterior logical position, so a cold load cannot strand the player below terrain before the cave prefab is rebuilt.

## Resource and persistence contract

Cave deposits are ordinary `SalvageResourceNode` objects using catalog `GameResourceDefinition` entries. Collection therefore uses the established `StarterRepairSession.TryCollect` and `ResourceMined` domain event path. Stable deposit IDs make depletion survive autosave/load without introducing a cave-specific save schema.

## Presentation

The cave entrance replaces the generic POI accent with a dark mouth and rock arch. Prefab interiors use a collision-backed floor, ceiling, walls, irregular ribs, archetype-specific rock/crystal/hydrothermal details, low-cost local lights, and an illuminated exit marker. No global cave SDF, marching-cubes terrain, destructive mining, or runtime terrain editing is introduced.

## Acceptance

F5 must report `TASK-192 (F5): PASS`. The acceptance checks three supported archetypes, deterministic/stable deposit identities, live prefab collision, interactive entry/exit, resource persistence compatibility, and explicit `globalProcedural=0` / `terrainDeformation=0` policy.
