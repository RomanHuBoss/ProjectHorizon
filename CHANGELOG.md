# Changelog

All notable changes to Project Horizon are recorded in this file.

The project uses Semantic Versioning for application releases. Content schema,
save schema and procedural-generator versions are versioned independently.

## [Unreleased]

### Changed

- Future changes intended for the next tagged release are recorded here.

## [0.1.0-alpha.148] - 2026-08-15

### Added

- Application-level `WorldSceneCoordinatorRuntime` with a strict Surface ↔ Orbit ↔ StationInterior ↔ HyperspaceTransit transition graph and stable system/planet context IDs.
- Four lightweight world-context PackedScene shells plus a Godot coordinator host that keeps exactly one shell resident and exposes runtime diagnostics.
- TASK-148 F5 acceptance, xUnit transition-graph coverage and an executable world-scene contract validator integrated into local/CI/release quality gates.

### Changed

- Surface and orbital runtime residency are now coordinated explicitly: station interiors and hyperspace suspend both heavy contexts, while Orbit may retain the existing bounded 72 m surface overlap near a planet.
- Star-system proxy rendering is restricted to the Orbit world context; station and hyperspace contexts no longer retain orbital proxy visuals.
- Hyperspace jumps stage an explicit transit context and either complete into the destination station context or roll back to the source station context when the jump is rejected.
- World-scene state is derived from existing voyage/galaxy persistence, avoiding a second SQLite location state or schema change.

## [0.1.0-alpha.146] - 2026-08-15

### Added

- Shared base-construction placement preflight used by both UI preview and the mutating placement path.
- Base-construction closure regression coverage for preflight parity, interactive limits, battery isolation and malformed-save rejection.
- TASK-146 static closure gate integrated into local quality scripts.

### Fixed

- Windows build regression reported from TASK-144: Godot `Toggled` delegate binding, `System.Environment` ambiguity and missing `CultureInfo` import.
- Overlay upgrades can no longer compile stale pre-TASK-144 architecture sources: `Game.Client.csproj` excludes them, build-time `ProjectHorizonSourceHygiene` removes only the known retired artifacts, and unknown source in the retired path fails safely instead of being deleted.
- `clean-build-windows10.cmd` now forces recompilation of `Game.Domain`, `Game.Application` and `Game.Client` instead of leaving referenced-layer `CoreCompile` up-to-date.
- Disabled battery modules no longer contribute available network capacity; corrupted non-finite or over-capacity base energy is rejected on restore.
- Nullable ecology player binding is captured safely before fauna configuration.
- TASK-146 hotfix1 removes two accidental `MalformedSaveRejected` references from planetary-exploration and station-services acceptance output; the field remains scoped only to base-construction acceptance, with a static scope guard added to prevent recurrence.

## [0.1.0-alpha.144] - 2026-08-15

### Added

- Compiled `Game.Domain` and `Game.Application` assemblies with one-way project references,
  leaving `Game.Client` as the Godot composition/presentation host.
- Dedicated Windows/Linux Compatibility export presets that select Godot's
  `gl_compatibility` renderer and OpenGL 3 driver path.
- Runtime renderer-profile evidence plus a TASK-144 platform/architecture acceptance probe.
- Executable TASK-144 platform/architecture contract validator and xUnit assembly-boundary gate.

### Changed

- Domain events, scheduling policy and deterministic generator contracts were moved out of
  `Game.Client`; the event-bus implementation now lives in the application layer.
- CI and release packaging now produce four desktop profiles: primary Windows/Linux and
  Compatibility Windows/Linux, with portable symbols collected across all three assemblies.
- Build/release documentation and architecture evidence were synchronized with the compiled
  layer graph and executable renderer fallback.

## [0.1.0-alpha.142] - 2026-08-15

### Added

- Typed domain event bus with the eleven normative section-38 business events.
- Executable system-frequency policy for 60 Hz physics/player control, 10 Hz nearby AI,
  2 Hz distant AI, bounded background economy updates and batched telemetry.
- Section-38 architecture contract validator and xUnit architecture tests.

### Changed

- Resource, voyage, galaxy, quest, ship-damage, base-placement and save-request flows now
  publish typed domain events instead of relying only on scene-local state strings.
- Ground NPC and NPC-ship decision logic is throttled to the normative nearby-AI cadence
  while movement integration remains physics-rate; distant ecology runs at 2 Hz.
- Structured telemetry is buffered and flushed in batches, including scene-exit flushes.
- Physics tick rate is explicitly pinned to 60 Hz in the Godot project configuration.

## [0.1.0-alpha.140] - 2026-08-15

### Added

- Automated pull-request quality gate for dependency restore, warnings-as-errors
  C# build, xUnit/coverage verification, JSON validation and save-migration tests.
- Headless Godot 4.7.1 .NET debug exports for Windows x64 and Linux x86_64.
- Release pipeline with a non-publishing manual dry-run and matching-tag publication, producing
  Windows/Linux release packages, a separate portable-PDB symbols archive, SHA-256 checksums
  and a machine-readable release manifest.
- Repository-level version, changelog and build/release documentation.

### Changed

- Section 36 verification is promoted to a mandatory CI gate before exports.
- Git/LFS and ignore policy is documented as part of the release contract.
