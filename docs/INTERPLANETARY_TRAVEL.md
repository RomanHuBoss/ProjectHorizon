# TASK-152 — Interplanetary Travel & Planet Activation Handoff

TASK-152 closes the same-system planetary-travel boundary introduced by TASK-150. It does not add a teleport menu: the selected destination is represented by the live star-system proxy, the existing ship controller receives assisted thrust/brake commands, fuel is debited when cruise begins, and the current planet changes only after arrival.

## Player flow

1. Open `M`, switch to the **System** tab and choose a landable planet with Up/Down.
2. Press `Enter`. The row is marked `TARGET`; selecting the current planet clears the target. Gas giants are rejected.
3. Board/launch or undock normally. `K` uses the existing navigation-assist control path. If a planet target exists, it takes priority over station/landing guidance and starts an interplanetary cruise.
4. During cruise the world coordinator uses `InterplanetaryTransit`. Star-system proxies remain resident while the detailed surface is suspended.
5. Arrival requires both destination radius and speed thresholds. The galaxy runtime commits the new `CurrentPlanetId`, increments transfer counters, then the voyage runtime rebases the ship to a local planetary approach and resumes the normal inbound/landing flow.
6. Turning `K` off cancels assisted cruise but retains the selected target. Consumed transfer fuel is not refunded.

## State and persistence

`GalaxyNavigationSaveData` remains the single persistence owner for same-system planetary identity. TASK-152 extends its backward-compatible tail with `SelectedPlanetId`, `InterplanetaryTransferCount` and `TotalInterplanetaryDistanceMeters`. No SQLite schema migration is required. `InterplanetaryTravelRuntime` itself is volatile; after load it reconstructs its selected state from galaxy navigation and never persists Godot coordinates.

## World residency

The world graph is extended with:

`Orbit(source) -> InterplanetaryTransit(source) -> Orbit(destination)`

A direct `Orbit(source) -> Orbit(destination)` planet mutation remains rejected. `InterplanetaryTransit` reuses orbital/system runtime residency so proxy bodies remain available for physical guidance, while surface-only systems remain suspended. At completion only the destination planet can become the detailed `PlanetRuntime`.

## Acceptance

F5 emits `TASK-152 interplanetary travel acceptance PASS` only when target selection, target save/restore, fuel debit, guidance thresholds, transactional world handoff, local approach, transfer counters and completed save/restore all pass. Unit regressions additionally cover target persistence and rejection of direct cross-planet Orbit transitions.
