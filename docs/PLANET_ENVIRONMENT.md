# Planet Environment subsystem — TASK-150

## Scope

TASK-150 is the first Stage 2 planetary-environment foundation. It extends the
accepted Stage 1 starter system from one planet to four deterministic landable
planets and centralizes planet type, climate, biome, water, atmosphere and cloud
rules in a data-driven runtime.

This iteration intentionally does **not** add free in-system travel between the
four planets. The current planet is selectable/persistable by the application
runtime and inspectable in the system map/developer tools; physical travel and
world-shell handoff to another planet remain the next gameplay step.

## Static catalog

`Content/planet_environments.json` defines exactly nine archetypes:

- `temperate`
- `desert`
- `frozen`
- `volcanic`
- `toxic`
- `radioactive`
- `barren`
- `oceanic`
- `gas_giant`

Each definition provides bounded radius/gravity/temperature/moisture,
atmosphere, water, clouds, radiation/toxicity, presentation colors and the
allowed ecology biome IDs. Landable archetypes expose from one to eight biomes.
`gas_giant` is explicitly non-landable and has no surface biome set.

The catalog is strict: malformed ranges, unknown biome IDs, invalid colors,
more than two cloud layers, duplicate archetypes or a landable planet without a
biome set are rejected at load time.

## Deterministic runtime

`PlanetEnvironmentRuntime` derives a `PlanetEnvironmentProfile` from:

- deterministic `GalaxyPlanetDefinition.Seed`;
- planet archetype;
- star type;
- generated atmosphere/water flags.

No global sequential random generator is used. Re-evaluating the same planet
with the same generator inputs produces the same radius, gravity, climate,
water coverage, atmosphere density and cloud parameters.

Biome sampling combines the current profile with latitude, normalized
elevation, distance to water and local deterministic noise. The result is
restricted to the planet's catalog-approved biome set and is scored against the
existing ecology temperature/moisture ranges.

## Starter system

The deterministic starter system contains exactly four landable planets:

1. the existing starter planet ID — `temperate`;
2. `desert`;
3. `frozen`;
4. `volcanic`.

The original starter planet ID remains unchanged, so Stage 1 saves and gameplay
references continue to resolve to the same first planet. Other generated
systems retain the existing 1–8 planet rule and can use all nine archetypes.

`GalaxyNavigationSaveData.CurrentPlanetId` is optional and therefore backward
compatible with earlier serialized saves. A missing value selects the first
landable deterministic planet. A saved planet that does not belong to the
regenerated system, or points to a gas giant, is rejected instead of silently
teleporting the player.

No SQLite schema migration is required: galaxy navigation remains an optional
serialized save-settings payload.

## Presentation

The normal system map shows per-planet environment details. The gameplay HUD
shows the current planet's compact environment summary.

Developer Planet Preview adds live stylized presentation:

- spherical fixed-level water surface using `planet_water_shell.gdshader`;
- simplified transparent atmospheric shell using
  `planet_atmosphere_shell.gdshader`;
- zero, one or two scrolling cloud shells using
  `planet_cloud_shell.gdshader`.

These effects are deliberately bounded: there is no physical fluid simulation
and no expensive volumetric/multi-scattering ray marching. They are visual
shells layered over the existing cube-sphere prototype.

## Acceptance

The existing gameplay `F5` acceptance matrix now includes TASK-150. A successful
run prints a HUD/output result beginning with:

```text
TASK-150 planet environment acceptance PASS:
```

Required invariants are:

```text
starterPlanets=4/4
starterArchetypes=4/4
catalogArchetypes=9/9
deterministic=1
radiusBounds=1
biomeCoverage=1
biomeFactorSampling=1
waterPolicy=1
atmospherePolicy=1
cloudPolicy=1
gasGiantNonLandable=1
currentPlanetRoundTrip=1
samples=16
```

The repository quality gate also runs:

```text
python tools/validate-task150-planet-environment.py
```

and expects:

```text
TASK-150 PLANET ENVIRONMENT CONTRACT PASS: starterPlanets=4/4; archetypes=9/9; radius=20-80km; biomes=max8; water=1; atmosphere=1; clouds=0-2; climateFactors=1; persistence=1; systemMap=1; planetPreview=1; currentPlanetConsumers=1; persistenceBoundary=1; starDirection=1; shaders=3/3; f5=1; xunit=4/4.
```
