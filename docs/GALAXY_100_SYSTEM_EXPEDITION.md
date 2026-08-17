# TASK-210 - 100-System Procedural Expedition & Galaxy Residency Validation

## Purpose

TASK-210 turns the version-1.0 acceptance criterion in Technical Specification section 41 into an executable contract: at least 100 distinct procedural star systems must be visitable without authored per-system content. It also enforces the section-42 prohibition against keeping the whole galaxy resident or pregenerating all planets.

## Validation path

The acceptance runner starts in `system.vertical_slice` and performs 99 real `GalaxyNavigationRuntime.TryJumpToSelected` transitions to neighboring deterministic sectors `(1,0,0)` through `(99,0,0)`. The resulting expedition therefore contains exactly 100 distinct visited systems including the starter system.

The corridor uses a 550 ly validation range. Adjacent sectors are separated by the 180 ly sector scale plus bounded procedural jitter, so each target is reachable without constructing a galaxy-wide route graph.

## On-demand residency

Only the current system and the selected destination can be referenced as live `GalaxySystemDefinition` objects during a jump. The historical expedition is retained only as stable visited system IDs in persistence. The validation explicitly requires no more than two distinct live system-definition references and rejects any model that requires 100 system definitions to remain resident.

The test may regenerate a definition to verify determinism. Regeneration is transient and does not become world residency.

## Procedural-content invariants

For every visited system the runner checks:

- deterministic replay produces the same complete system/planet signature;
- every non-starter system uses a coordinate-derived `system.g1.x...` ID;
- planet count remains within 1..8;
- planet orbit and moon bounds remain valid;
- planet IDs are unique across the expedition;
- planet seeds are positive and stable;
- no manual per-system table is required beyond the authored starter vertical slice.

The observed star-type, archetype, planet and landable-system counts are emitted as diagnostics but are not converted into arbitrary distribution quotas that the specification does not require.

## Persistence

After the 100th distinct system is reached, `GalaxyNavigationSaveData` is created and immediately restored. The restored runtime must retain all 100 visited stable IDs, 99 applied jumps and the exact current system.

## Runtime acceptance

F5 runs TASK-210 as part of the existing complete runtime matrix. Expected HUD form:

`TASK-210 (F5): PASS systems=100 resident<=2 maxJump=<N>ly`

Expected output begins:

`TASK-210 100-system procedural expedition acceptance PASS:`

This is an accelerated deterministic integration acceptance. It does not claim that a human manually flew through 100 systems in real time, but it uses the same hyperspace transition method, ship preconditions, route planner, fuel debit path and persistence model as gameplay.
