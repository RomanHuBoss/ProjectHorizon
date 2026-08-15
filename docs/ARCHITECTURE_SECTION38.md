# Project Horizon — Section 38 architecture contract

This document records the executable coding/architecture rules used by the current shipping vertical slice. The normative source remains Technical Specification v2.0 §38.

## Domain events

Cross-system business changes use `IDomainEventBus`. Godot scene signals are reserved for scene-local presentation/interaction concerns and are not the business integration mechanism.

The normative typed event set is:

- `ItemAdded`
- `ItemRemoved`
- `ResourceMined`
- `PlanetEntered`
- `PlanetExited`
- `SystemDiscovered`
- `QuestAccepted`
- `QuestCompleted`
- `ShipDamaged`
- `BaseModulePlaced`
- `SaveRequested`

`DomainEventBus` has no Godot dependency. The shipping `SalvageRepairSlice` owns one bus, injects it into the autosave coordinator, and subscribes domain/application reactions once during initialization. Subscriptions are disposed on scene shutdown.

## System frequencies

`SystemFrequencyPolicy` is the single policy source for scheduled gameplay work:

| Work | Policy |
|---|---:|
| Physics | 60 Hz |
| Player controller | 60 Hz |
| Nearby AI decisions | 10 Hz |
| Distant AI | 2 Hz |
| Economy UI | event-driven |
| Background economy | 0.2–1 Hz, current 0.5 Hz |
| Save queue | event-driven |
| Telemetry flush | batched, current 2 Hz |

The Godot project explicitly pins `physics/common/physics_ticks_per_second=60`. AI decision throttling does not reduce physics integration frequency: navigation/movement continues on `_PhysicsProcess`; expensive target/state decisions are cached between policy ticks.

## Persistence and async boundaries

- SQLite commands stay in persistence or developer-inspection code; scene files contain no SQL.
- Persistence values are passed as SQL parameters. Dynamic table identifiers in the developer read-only exporter come only from SQLite metadata and are safely quoted.
- Every production `Task`/`ValueTask` method accepts an explicit `CancellationToken`, including private workers and graceful-exit operations.
- Save requests are queued through `SaveAutosaveCoordinator` and publish `SaveRequested`; no scene-local direct SQL is permitted.
- Save/content/generator formats remain explicitly versioned.

## Compiled layer boundaries (TASK-144)

The architecture rule is now represented by three separate .NET assemblies rather than folders
inside one Godot project:

```text
Game.Domain <- Game.Application <- Game.Client
```

- `Game.Domain` contains domain-event contracts, scheduling policy and deterministic generator
  contracts and has no ProjectReference, Godot or SQLite dependency.
- `Game.Application` contains application orchestration such as `DomainEventBus` and references
  only `Game.Domain`.
- `Game.Client` is the Godot composition/presentation host and references both lower layers.

`Section38ArchitectureTests.LayeredAssembliesHaveOneWayDependencies` verifies the loaded assembly
graph, while `validate-platform-architecture-contract.py` verifies the project graph and prevents
Godot/SQLite dependencies from leaking into Domain/Application.

## Domain / Godot separation

- Domain/runtime/catalog/model classes must not use `Godot.Node` as their data model.
- The typed domain event contracts and event bus compile without a Godot dependency.
- World-generation builders are not invoked directly from `_Process`.
- Application/UI code must not directly perform inventory/crafting mutations.
- Game IDs are explicit content/runtime identifiers; CLR class names are diagnostic metadata only and are not persistent IDs.

## Diagnostics and exceptions

Structured telemetry is buffered and flushed in batches instead of appending every message immediately. Gameplay flushes at the telemetry policy cadence; Main Menu, Developer Workbench and gameplay shutdown also flush pending lines before the scene disappears.

Exceptions are not swallowed by empty `catch` blocks. Expected cancellation is filtered explicitly with `OperationCanceledException` and a cancellation-token condition; unexpected failures are reported or propagated according to the owning subsystem contract.

## Automated gates

Run:

```bash
python tools/validate-section38-architecture-contract.py
```

The validator checks nullable/warnings policy, XML documentation of public interfaces, cancellation tokens on all production async operations, exact typed-event coverage, Godot-independent event contracts, system frequencies, batched telemetry, SQL boundaries, exception handling, layer direction, serialization versioning and UI/domain separation.

Section-38 xUnit tests live in:

```text
tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs
```

`tools/run-section37-quality.*` and both GitHub Actions workflows execute the section-38 contract plus `tools/validate-platform-architecture-contract.py` in addition to the previous quality gates.

## Runtime acceptance

One gameplay `F5` run includes `TASK-142` and `TASK-144`. TASK-142 validates all eleven typed events on an isolated bus, the live subscription set, 10 Hz / 2 Hz fixed-rate gates over a 60 Hz physics sample, and ecology frequency mapping. TASK-144 verifies that domain/application/client types are loaded from `Game.Domain`, `Game.Application` and `Game.Client` respectively and that the observed renderer profile is internally consistent. A separate Compatibility export/run must report `feature=compatibility`, `method=gl_compatibility` and an `opengl3...` driver before the fallback requirement can be marked VERIFIED. The probes do not modify the gameplay save slot merely to prove these contracts.
