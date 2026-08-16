# Changelog

## [0.1.0-alpha.162.1] - 2026-08-16

### Fixed — TASK-162.1 Runtime Bootstrap Order

- Fixed a TASK-162 startup regression where `InitializeStageOneVoyageRuntime()` called `SurfaceLogicalToLocalPosition()` before `GalaxyNavigationRuntime` existed, throwing `InvalidOperationException: Galaxy navigation runtime is unavailable`.
- Galaxy navigation now initializes before Stage-1 voyage in all three lifecycle paths: initial `_Ready()`, save-slot load, and new-game/reset.
- This allows initialization to continue into planet environment, star-system simulation, terrain/world composition and the rest of the vertical-slice runtime instead of stopping on the scene's fallback `GroundBody`.
- Added a static bootstrap-order regression gate and wired it into the Windows/Linux section-37 quality runners.

### Runtime evidence that triggered the hotfix

- External Godot 4.7.1 log stopped in `SalvageRepairSlicePlanetSurfaceFrame.cs` while resolving `GalaxyNavigation.CurrentPlanetId` from `InitializeStageOneVoyageRuntime`.
- The accompanying screenshot showed the expected consequence of the aborted bootstrap: fallback square terrain, no initialized sun/atmosphere/world composition, and unavailable player-position HUD.

## [0.1.0-alpha.162] - 2026-08-16

### Added

- Added `PlanetSurfaceFrameRuntime`, a Godot-independent double-precision planet-logical East/North frame with 4096 m floating-origin cells and a 2048 m local rebase threshold.
- Added TASK-162 F5 acceptance covering >150 km logical traversal, bounded local coordinates, chunk identity, cold restore, planet reset and geodesic stability.
- Added three xUnit frame regressions, `docs/PLANET_GLOBAL_SURFACE_FRAME.md` and `validate-task162-planet-global-surface-frame.py`, wired into Windows/Linux section-37 quality gates.

### Changed

- `TerrainChunkManager` now selects/samples chunks in logical surface coordinates while positioning live chunk nodes relative to the current floating-origin offset; bounded 25-chunk residency and asynchronous worker generation are unchanged.
- Procedural resources, POI residency, ecology proximity, planet map, terrain/geodesic HUD, base construction, NPC surface navigation and Stage-1 voyage coordinates now share the same frame conversion contract.
- Live rebase now shifts non-transform absolute caches as well: ground-NPC path targets, NPC-ship route waypoints, flying-fauna aerial entries and aerial obstacle/POI steering environment.
- Player autosave/graceful-exit X/Z now persist planet-logical coordinates and cold load reconstructs a bounded local scene position without a SQLite schema bump; new-slot reset and piloted ship cold-load initialize the frame explicitly.
- TASK-150's static graceful-exit gate now accepts the frame-aware logical player snapshot path in addition to the pre-TASK-162 direct `GlobalPosition` path.

### Known boundary

- TASK-162 closes coordinate/floating-origin scaling, not physical cube-sphere topology: radial gravity, curved collision and cube-face transitions remain outside this iteration.


## [0.1.0-alpha.160.1] - 2026-08-16

### Fixed — TASK-160.1 Traversal-Safe Aerial Acceptance

- Removed TASK-126 F5 dependence on player distance after planet-surface traversal: flying fauna now execute a non-moving acceptance probe through the same shared `AerialSteeringRuntime` even when the authored population is beyond the normal 50 m AI update radius.
- Preserved the original strict `sharedRuntime` and `runtimeSamples` delta assertions instead of weakening them; the probe runs after the acceptance baseline and reports `faunaProbeSamples` for diagnosis.
- Kept gameplay state intact during the probe: no `MoveAndSlide`, no position/velocity replacement, and dead/hidden fauna are not resurrected in the aerial spatial grid.
- Added a distance-regression xUnit check, a TASK-160.1 repository gate and section-37 runner integration.

### Compatibility

- No world-generation, save-schema, resource identity, terrain, TASK-160 composition or TASK-158 streaming contracts changed. `ProjectHorizonGenerator.Version` remains `3`.

## [0.1.0-alpha.160] - 2026-08-16

### Added — TASK-160 Planet Surface World Composition & Persistence

- Replaced the color-only surface background with a deterministic planet/star sky profile: procedural sky, system-star directional light, sky ambient/reflections, aerial fog and lightweight visible cloud clusters.
- Added a weak planet-colored indirect-light floor and richer terrain macro/slope coloring so streamed PBR terrain cannot collapse to absolute black while retaining direct-light relief.
- Replaced the live 58-node catalog resource showcase with chunk-scoped deterministic deposits; legacy nodes remain hidden acceptance fixtures except the three starter salvage nodes required by the repair loop.
- Added stable `planet + chunk + slot` surface-resource identities and dynamic cold-restore bindings; untouched procedural resources create no save delta, while mined deposits stay depleted after chunk unload, save/load and planet return.
- Spread existing stable POI instances through a deterministic 78–420 m live exploration annulus without changing reviewed POI IDs or the TASK-138 golden generation fixture.
- Added TASK-160 F5 acceptance, three xUnit regressions, RU/EN HUD diagnostics, subsystem documentation and a section-37 repository quality gate.

### Compatibility

- `ProjectHorizonGenerator.Version` remains `3`: TASK-160 changes live presentation and introduces a new resource layer without changing the reviewed system/POI golden generator output.
- TASK-156 terrain identity, TASK-158 chunk residency, Stage-1 resource fixture IDs and existing POI/ecology save identities remain compatible.

## [0.1.0-alpha.158.1] - 2026-08-16

### Fixed — TASK-158.1 Runtime Acceptance / Golden POI closure

- Bumped `ProjectHorizonGenerator.Version` from 2 to 3 because TASK-156 intentionally changed deterministic POI world-space Y through terrain projection; refreshed the reviewed 20-POI golden fixture to the actual deterministic checksum `6e229717a6faad6043f963d825ba8b13a2af9dbf2335c161e6a24fca450ddfcc`.
- Preserved all POI stable IDs, X/Z coordinates, control heights, slopes, rotations, water distances and danger values; only the deterministic world-space Y expectation changes to `controlHeight + 0.1 + sizeY/2`.
- Removed the two nullable compiler warnings observed in the external Windows build: the NPC navigation callback now requires a bound navigation surface, and the surface-streaming neighbor lookup handles nullable `TryGetValue` output explicitly.
- Added a TASK-158.1 regression gate that independently reconstructs the golden POI checksum and verifies the terrain-projected Y contract, generator-version binding and warning fixes.


## [0.1.0-alpha.158] - 2026-08-16

### Added — TASK-158 Planetary Surface Streaming & Traversal Foundation

- Promoted the verified Prototype-B `TerrainChunkManager` into the live planet-surface lifecycle instead of introducing a parallel terrain streamer.
- Added bounded 5x5 surface residency: 9 central 33x33 LOD0/collision chunks plus a 16-chunk 17x17 LOD1 ring, with the existing stitching, skirts, hysteresis, cancellation and stale-result guards.
- Added pure deterministic planet-profile sampling inside terrain workers, collision-first live traversal transitions and a safe TASK-156 fallback-to-streamer handoff during startup and planet switches.
- Extended terrain-aware TASK-124 navigation beyond the old 80x80 authored patch while keeping only 5x5 NavigationRegion3D tiles resident.
- Added planet-radius geodesic surface addressing, RU/EN streaming HUD diagnostics, F5 acceptance, three xUnit regressions and the TASK-158 repository quality gate.

### Changed

- Planet-surface streamed vertices now sample the TASK-156 height function at their exact world coordinates; legacy Prototype-B noise sampling remains unchanged.
- `PlanetSurfaceStreamer` participates in TASK-148 surface residency and is suspended outside the surface world context.
- Streamed `TerrainChunk` collision bodies are excluded from the NPC static-obstacle catalogue because they represent walkable ground.

## [0.1.0-alpha.156] - 2026-08-15

### Added — TASK-156 Planet-Specific Terrain & Surface Geometry

- Replaced the flat vertical-slice GroundBody at runtime with deterministic 65x65 planet-specific terrain and matching trimesh collision.
- Added distinct temperate/desert/frozen/volcanic morphology plus bounded profiles for all other landable archetypes.
- Preserved a central tutorial terrace and added deterministic wet-world basin floors for existing water interactions/aquatic habitat.
- Grounded ecology, POI presentation, resource nodes and base-construction placement on the shared terrain sampler while preserving legacy stable IDs.
- Upgraded tiled NavigationServer3D regions to heightfield vertices/slope filtering and removed flat-Y forcing from ground NPC movement.
- Added TASK-156 F5 acceptance, three xUnit regressions, RU/EN terrain HUD strings and a repository quality gate.

## [0.1.0-alpha.154.1] - 2026-08-15

### Fixed
- Updated the reviewed TASK-138 golden starter-system fixture from the obsolete one-planet output to the current four-planet Stage 2 starter system and bumped `ProjectHorizonGenerator.Version` to `2`, matching the deterministic world-generation change already introduced by TASK-150.
- Removed the hard-coded generator-version `1` assumption from the section-36 static gate; the golden manifest must now match the central generator version.
- Hardened TASK-124 F5 navigation acceptance against NavigationServer3D synchronization races: path probing retries within bounded per-phase timeouts, samples horizontal/vertical/diagonal routes, revalidates after stream restore, and retains the original cross-tile, obstacle-clearance and recovery invariants.
- Added `validate-task1541-runtime-acceptance-hotfix.py` and strengthened the TASK-149.4 regression gate with explicit navigation-path readiness coverage.

## [0.1.0-alpha.154] - 2026-08-15

### Added
- `TASK-154` planet-scoped surface-content orchestration for the four-planet starter system: deterministic biome/ecology/POI activation follows the actual `CurrentPlanetId`.
- Per-planet ecology and planetary-exploration delta archives inside the existing backward-compatible save settings; no SQLite schema bump.
- Planet-aware ground/sky/water presentation, dry-world aquatic suppression, F5 `TASK-154` acceptance, three xUnit regressions, and a section-37 static contract gate.

### Changed
- Interplanetary arrival and hyperspace lifecycle now capture the previous surface state before changing planet/system and activate the destination surface content when landable.
- Ecology planning now accepts planet seed, active biome set, water coverage and habitability; POI placement samples the real planet climate instead of the fixed `biome.test_plain` environment.
- Legacy `planet.vertical_slice` keeps its historical ecology/POI seeds, region keys and instance IDs so existing saves remain loadable.
- Planet-surface HUD summary is routed through the existing RU/EN localization catalogs, including localized archetype and water-state labels.

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
