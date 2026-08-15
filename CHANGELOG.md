# Changelog

## [0.1.0-alpha.152] - 2026-08-15

### Added

- TASK-152 same-system interplanetary travel subsystem: System Map planetary target selection, persisted target/counters, fuel-backed assisted physical cruise, live proxy targeting and local destination approach.
- `InterplanetaryTransit` world-shell context and transactional `Orbit(source) -> InterplanetaryTransit -> Orbit(destination)` planet handoff.
- TASK-152 F5 acceptance, three xUnit regression checks, static contract gate and `docs/INTERPLANETARY_TRAVEL.md`.

### Changed

- Existing `K` navigation assist now prioritizes a selected planetary destination while in flight; disabling assist cancels cruise without refunding consumed transfer fuel.
- Star-system proxies remain active during interplanetary transit while detailed surface residency is suspended.
- Galaxy navigation save state now carries backward-compatible selected-planet and transfer diagnostics without a SQLite schema bump.
- TASK-150/TASK-151 are marked accepted after the product owner reported that the delivered `alpha.150.1` works.

All notable changes to Project Horizon are recorded in this file.

The project uses Semantic Versioning for application releases. Content schema,
save schema and procedural-generator versions are versioned independently.

## [Unreleased]

### Changed

- Future changes intended for the next tagged release are recorded here.

## [0.1.0-alpha.150.1] - 2026-08-15

### Fixed

- Fixed the external Windows/Godot compile blocker `CS0104` in `DeveloperWorkbenchController.cs` by explicitly using `Godot.FileAccess` for the Planet Preview environment catalog.
- Made graceful exit/main-menu transition idempotent: a completed flush now commits the scene/quit transition once, stops the current `_Process()` frame, refuses new exit work after `_ExitTree`, and snapshots `Player.GlobalPosition` only while the player is inside the active SceneTree.
- Extended the TASK-150 contract gate with compile-ambiguity and graceful-exit re-entry guards.
- Fixed the TASK-148 validator version parser so patch revisions such as `alpha.150.1` are correctly accepted as newer than `alpha.149`.

## [0.1.0-alpha.150] - 2026-08-15

### Added

- Added `planet_environments.json`, a strict nine-archetype planet-environment catalog covering temperate, desert, frozen, volcanic, toxic, radioactive, barren, oceanic and non-landable gas-giant worlds.
- Added deterministic `PlanetEnvironmentRuntime` climate/biome sampling plus TASK-150 acceptance and four xUnit regressions for the four-planet starter system, deterministic bounded profiles, current-planet persistence and gas-giant landing rules.
- Added stylized spherical water, simplified atmosphere and one/two-layer scrolling cloud shaders to Planet Preview, without physical fluid simulation or volumetric ray marching.
- Added TASK-150 static contract validation to Windows/Linux section-37 quality gates and a dedicated `docs/PLANET_ENVIRONMENT.md` subsystem note.

### Changed

- Expanded the starter system from one procedural planet to four stable landable planets with distinct archetypes (`temperate`, `desert`, `frozen`, `volcanic`) while preserving the original starter planet ID as planet 1.
- Galaxy navigation now carries a backward-compatible `CurrentPlanetId`, validates selected planets against deterministic system generation and persists/restores the active landable planet without a SQLite schema bump.
- System map, gameplay HUD and Developer Planet Preview now expose deterministic radius, gravity, temperature, water, atmosphere, cloud and biome information for the selected/current planet.
- Accepted TASK-149 and its runtime-regression closure by explicit product-owner waiver; exact missing build/manual metrics are not reconstructed.

## [0.1.0-alpha.149] - 2026-08-15

### Changed

- Hardened `WorldSceneCoordinatorNode` into a staged scene transaction: the target PackedScene is loaded, instantiated and attached before application state changes, while the previous shell remains resident until the new shell is proven inside the coordinator tree. Failed swaps retain the prior context/shell and restore exact runtime counters.
- Added exact volatile snapshots for the application/runtime and Godot coordinator state. The snapshots are acceptance/rollback infrastructure only and do not create a second persistence source of truth.
- Upgraded the TASK-148 `F5` probe into a live seven-context traversal (`Surface → Orbit → StationInterior → HyperspaceTransit → StationInterior → Orbit → Surface`) that checks one-shell residency after every step and restores the exact pre-test context/counters in `finally`.
- Expanded World Scene Coordinator diagnostics and contract coverage with transactional-swap, rollback, state-restoration and a fourth xUnit regression test.
- Restored the exact Git LFS payloads for the v2.0 PDF/DOCX technical specification and the v1.0 PDF into their repository paths; no document was regenerated or rewritten.

### Fixed

- Closed the F5 runtime regressions surfaced after TASK-148 acceptance: `SystemFrequencyGate` no longer double-ticks at floating-point interval boundaries; ground NPC navigation waits for a real `NavigationServer3D` map iteration before querying; TASK-130 compares canonical save paths instead of raw strings; and TASK-126 exercises NPC-ship steering only while Orbit residency is actually active.
- Added residency-aware NPC-ship diagnostics plus a non-moving acceptance step so suspended orbital traffic is not misreported as active on Surface and F5 can validate steering without moving `CharacterBody3D` instances outside a physics frame.
- Added `validate-task149-runtime-regression-closure.py` to Windows/Linux local quality gates.
- Fixed the external Windows/Godot build blocker `CS0136` in `WorldSceneCoordinatorAcceptance.cs`: the per-step transition result now uses `transitionResult` instead of shadowing the final acceptance `result` local.
- Removed the failure window where `WorldSceneCoordinatorRuntime` could advance to the destination context before the destination PackedScene had successfully loaded/entered the scene tree.

## [0.1.0-alpha.148.2] - 2026-08-15

### Fixed

- Moved the `PlayerWaterMaterial`/mesh/shape `sub_resource` declarations ahead of the first `[node]` in `SalvageRepairSlice.tscn`; Godot 4.7.1 had rejected the gameplay scene with `Parse Error: Unknown tag 'sub_resource'` and `ChangeSceneToFile(...)=CantOpen`.
- Changed `AudioDirector` root installation to deferred `add_child`, preserving requested environment/music until `_Ready` and guarding playback before the director is inside the scene tree. This removes the startup `Parent node is busy setting up children` and `Playback can only happen when a node is inside the scene tree` failure path.
- Added a repository-wide Godot text-scene structural gate (resource order, duplicate IDs and unresolved `ExtResource`/`SubResource` references) to local quality, CI and release pipelines, and strengthened the audio lifecycle contract.

## [0.1.0-alpha.148.1] - 2026-08-15

### Fixed

- Removed the TASK-148 coordinator C# script as a hard `ext_resource` of `SalvageRepairSlice.tscn`; the orchestration node is now created under `Gameplay` only after the gameplay scene itself has loaded. This prevents a fresh/overlaid Godot C# UID/resource cache from turning the whole gameplay scene into `CantOpen`.
- Extended the TASK-148 contract gate with a gameplay-load-safety invariant so orchestration-only C# nodes cannot accidentally become scene-opening dependencies again.

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
