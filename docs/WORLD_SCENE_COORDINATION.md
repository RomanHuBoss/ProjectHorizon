# World Scene Coordination — TASK-148 / TASK-149

TASK-148 closes the vertical-slice scene-orchestration gap left explicitly open by TASK-128. TASK-149 hardens the same subsystem for runtime acceptance. The coordinator does not own galaxy/voyage persistence and does not generate universe content. It converts the already persisted current system, planet and voyage location into one active world context.

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
- Orbit: orbit active; surface may remain active only inside the current 260 m near-planet activation radius.
- StationInterior: surface and orbit suspended.
- HyperspaceTransit: surface and orbit suspended.

Star-system proxy visuals are emitted only in Orbit. The existing lightweight analytic star-system model may remain allocated as application/runtime state, but its scene proxies are not resident in StationInterior or HyperspaceTransit.

## Transactional scene swap

TASK-149 removes the failure window where application state could move to a destination context before the corresponding PackedScene was actually available.

For every legal transition the coordinator now performs the following synchronous transaction:

1. Validate the requested graph edge and stable context IDs.
2. Load and instantiate the destination PackedScene while the old shell is still active.
3. Attach the staged shell and verify that its parent is the coordinator and, when the coordinator is already in the SceneTree, that the staged shell is inside the tree.
4. Only after that preflight succeeds, apply `WorldSceneCoordinatorRuntime.TryTransition`.
5. Promote the staged shell and remove the previous shell.
6. If staging/attachment/state mutation fails, discard the staged shell and restore the exact runtime snapshot; the prior shell/context remain authoritative.

`WorldSceneCoordinatorRuntimeSnapshot` and `WorldSceneCoordinatorNodeSnapshot` are volatile rollback/acceptance structures only. They are not serialized and do not create a second save-state location.

## Hyperspace transaction

Before `GalaxyNavigation.TryJumpToSelected`, the coordinator enters `HyperspaceTransit`. A successful jump completes into the destination `StationInterior`, using the new `CurrentSystem` and `CurrentPlanetId`. A rejected jump completes back into the source station context. This makes context residency transactional with the existing jump operation rather than creating a second navigation implementation.

## Persistence

No `world_scene` SQLite setting or schema migration is introduced. On new game, load and reset, the coordinator derives its context from the existing `StageOneVoyageRuntime` and `GalaxyNavigationRuntime`. Those remain the persisted source of truth.

## Self-restoring F5 acceptance

`F5` now exercises the **live** coordinator rather than validating only a detached application state machine. The temporary path is:

```text
Surface(alpha)
  -> Orbit(alpha)
  -> StationInterior(alpha)
  -> HyperspaceTransit(alpha)
  -> StationInterior(beta)
  -> Orbit(beta)
  -> Surface(beta)
```

After each of the seven states, the probe checks:

- exactly one live shell (`HostChildren == 1`);
- shell kind/system/planet/generation metadata match the current context;
- the active scene path matches the expected PackedScene;
- surface/orbit residency matches the current context;
- the hyperspace step is the only point at which system changes.

It then verifies that direct `Surface -> StationInterior` is rejected without a reload. In `finally`, the runner restores the exact pre-test `WorldSceneCoordinatorNodeSnapshot` and re-applies residency. Successful evidence therefore includes:

```text
livePath=1
transactionalSwap=1
stateRestored=1
steps=7
maxHostChildren=1
testTransitions=6
testReloads=7
testRejected=1
testHyperspace=1
```

The acceptance probe does not write a new save block and must leave the gameplay-slot location unchanged after completion. `tools/validate-task148-world-scene-coordinator.py` and `WorldSceneCoordinatorTests` enforce the same architecture in local/CI/release quality gates.

## Gameplay load safety

The coordinator host is intentionally **not** serialized as a C# `ext_resource` in `SalvageRepairSlice.tscn`. It is orchestration state, not authored world content. Creating the node after the gameplay PackedScene has opened prevents C# UID/resource-cache refresh during overlay upgrades from making the entire gameplay scene return `CantOpen`. `tools/validate-task148-world-scene-coordinator.py` enforces this as `gameplayLoadSafe=1`.
