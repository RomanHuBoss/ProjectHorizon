# TASK-166 — Dynamic Planetary Weather & Diurnal Cycle

TASK-166 adds a deterministic planet-scoped time and weather runtime without changing
terrain chunk identity, save schema tables, or the bounded surface-streaming budget.

## Runtime contract

- One full local day advances in 600 real seconds while the surface runtime is active.
- Each planet has a deterministic solar phase derived from its seed.
- Weather is selected in deterministic two-local-hour cells from planet climate data.
- Supported states are `Clear`, `Wind`, `Storm`, and `Toxic`.
- The current state drives sun direction/energy, sky day/night/sunset colors, fog,
  cloud opacity/drift, wind audio, and bounded rain/snow/toxic particle visuals.
- Storm/toxic conditions add bounded suit/life-support hazards through the existing
  player-survival runtime rather than bypassing its equipment/protection rules.
- Flying fauna reduce activity in adverse conditions and receive bounded horizontal
  wind drift while retaining the TASK-126 terrain-relative altitude envelope.

## Persistence

Only elapsed game-hours are persisted as `PlanetWeatherSaveData` under the
`planet_weather` save setting. Weather identity is regenerated deterministically from
planet seed + elapsed time. Developer weather overrides are intentionally transient.
Old saves without `planet_weather` remain valid and start at the deterministic morning
baseline.

## Developer controls

The existing diagnostics commands are now live runtime controls:

- `set_time <0..24>` changes local solar time through `PlanetWeatherRuntime`;
- `set_weather <clear|wind|storm|toxic>` applies a transient weather override.

## Acceptance

Press **F5** and require:

`TASK-166 planetary weather acceptance PASS`

The acceptance covers deterministic sampling, day/night sun range, weather variation,
hazard profiles, exact elapsed-time save/restore, per-planet solar phase, and developer
override behavior. Manual smoke should additionally use `set_time 0`, `set_time 12`,
`set_weather storm`, and `set_weather toxic` from the developer console to verify live
presentation and HUD behavior.
