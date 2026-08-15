# TASK-154 — Planet-Scoped Surface Content

## Scope

TASK-154 closes the Stage 2 gap between interplanetary travel and actual planetary content. `GalaxyNavigationRuntime.CurrentPlanetId` is now the authoritative key for the landable surface-content bundle: environment, active biomes, ecology plan, planetary POI plan and surface presentation.

The starter system keeps four landable archetypes: `temperate`, `desert`, `frozen`, and `volcanic`. Each produces a deterministic `PlanetSurfaceContentProfile` from the planet seed and the existing `PlanetEnvironmentRuntime`.

## Ecology

For non-legacy planets `EcologyPlanner.PlanPlanet` receives the current planet seed, 1–8 active biome IDs, water coverage and a bounded habitability coefficient. Flora count and active/simplified fauna budgets remain below the existing global limits but vary by habitability. Species are chosen only from compatible active biomes. When water coverage is below the surface habitat threshold, aquatic fauna and the local aquatic habitat are omitted.

The original `planet.vertical_slice` deliberately keeps the historical `EcologyPlanner.Plan` path, world seed, region key and instance IDs. This prevents existing saves from becoming invalid merely because planet-scoped content was introduced.

## Planetary POI

The catalog still defines exactly 20 POI types. For a planet-scoped plan, candidate environment samples are no longer hard-coded to `biome.test_plain`: biome is obtained through `PlanetEnvironmentRuntime.SampleBiome`, water distance follows planet water coverage, and danger incorporates the planet hazard profile plus deterministic local variation.

`biome.test_plain` in the legacy POI catalog is treated as a compatibility wildcard until individual POI definitions are migrated to explicit biome matrices. The legacy starter planet keeps the exact historical planner identity and placement IDs.

## Persistence

No SQLite schema migration is required. The existing `planetary_exploration` and `ecology` JSON settings gained optional `PlanetId` and `PlanetStates` fields. Each visited surface stores an independent delta bundle keyed by stable `planet.*` ID.

Old saves have no `PlanetId`/`PlanetStates`; such root deltas are assigned to `planet.vertical_slice`. This also handles a TASK-152 save made after travel but before TASK-154: legacy ecology/POI deltas remain attached to the starter planet, while the current destination planet starts with a fresh deterministic plan.

Before an interplanetary or hyperspace identity change the current surface deltas are captured. After a successful same-system arrival the destination landable surface is activated immediately. A non-landable body does not instantiate a surface plan; the last landable surface archive remains preserved until another landable planet is activated.

## Presentation

The vertical-slice surface now derives its ground tint, world-environment ambient/background color and water presentation from the active planet profile. The local water pool is disabled on dry profiles. Ecology rebuild uses the same water-habitat policy, so a volcanic dry world cannot retain the starter aquatic patch.

This is still a bounded vertical-slice presentation, not full seamless planetary terrain streaming. TASK-154 makes content identity and gameplay state planet-correct; the existing cube-sphere/quadtree environment foundation remains the geometry source for later Stage 2 expansion.

## Acceptance

`F5` now runs `TASK-154 multi-planet surface content acceptance`. The domain-level acceptance verifies:

- 4/4 starter planets and 4/4 distinct biome/region profiles;
- ecology instances constrained to the planet biome set;
- dry-world aquatic exclusion;
- deterministic planet-aware POI planning using real climate samples;
- per-planet ecology and POI state round-trip;
- legacy starter planner compatibility.

Static CI/local quality adds `tools/validate-task154-multi-planet-surface-content.py`, and `WorldGenTests` contains three corresponding regression tests.
