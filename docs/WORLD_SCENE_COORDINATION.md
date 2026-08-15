# World Scene Coordination — TASK-148

TASK-148 closes the vertical-slice scene-orchestration gap left explicitly open by TASK-128. The coordinator does not own galaxy/voyage persistence and does not generate universe content. It converts the already persisted current system, planet and voyage location into one active world context.

## State machine

`WorldSceneCoordinatorRuntime` lives in `Game.Application` and has no Godot dependency. The only legal edges are:

```text
Surface <-> Orbit <-> StationInterior -> HyperspaceTransit -> StationInterior
```

Surface/Orbit/Station transitions must keep the same stable system and planet IDs. A system/planet change is accepted only when completing `HyperspaceTransit -> StationInterior`. Direct Surface -> Station and arbitrary same-level teleports are rejected.

## Scene residency

`WorldSceneCoordinatorNode` is created programmatically under `Gameplay` after the authored gameplay scene has loaded. It owns exactly one lightweight PackedScene shell:

- `SurfaceWorldShell.tscn`
- `OrbitWorldShell.tscn`
- `StationInteriorShell.tscn`
- `HyperspaceTransitShell.tscn`

The shell identifies context and environment profile; existing vertical-slice gameplay systems remain authoritative. Heavy local nodes are governed by residency instead of being duplicated:

- Surface: surface active, orbit suspended.
- Orbit: orbit active; surface may remain active only inside the existing 72 m near-planet activation radius.
- StationInterior: surface and orbit suspended.
- HyperspaceTransit: surface and orbit suspended.

Star-system proxy visuals are emitted only in Orbit. The existing lightweight analytic star-system model may remain allocated as application/runtime state, but its scene proxies are not resident in StationInterior or HyperspaceTransit.

## Hyperspace transaction

Before `GalaxyNavigation.TryJumpToSelected`, the coordinator enters `HyperspaceTransit`. A successful jump completes into the destination `StationInterior`, using the new `CurrentSystem` and `CurrentPlanetId`. A rejected jump completes back into the source station context. This makes context residency transactional with the existing jump operation rather than creating a second navigation implementation.

## Persistence

No `world_scene` SQLite setting or schema migration is introduced. On new game, load and reset, the coordinator derives its context from the existing `StageOneVoyageRuntime` and `GalaxyNavigationRuntime`. Those remain the persisted source of truth.

## Acceptance

`F5` runs TASK-148 and checks the legal graph, illegal-transition guard, hyperspace destination-system change, context validation, all four PackedScenes, exactly one live shell, live context metadata and residency state. `tools/validate-task148-world-scene-coordinator.py` and `WorldSceneCoordinatorTests` enforce the same architecture in CI.


## Gameplay load safety

The coordinator host is intentionally **not** serialized as a C# `ext_resource` in `SalvageRepairSlice.tscn`. It is orchestration state, not authored world content. Creating the node after the gameplay PackedScene has opened prevents C# UID/resource-cache refresh during overlay upgrades from making the entire gameplay scene return `CantOpen`. `tools/validate-task148-world-scene-coordinator.py` enforces this as `gameplayLoadSafe=1`.
