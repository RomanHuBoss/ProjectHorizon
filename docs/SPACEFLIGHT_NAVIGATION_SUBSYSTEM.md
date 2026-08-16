# TASK-178 — Spaceflight & Navigation Subsystem Closure

## Scope

TASK-178 closes the existing spaceflight/navigation mechanics as one integration boundary. It does not add a new travel mode. Instead it composes the normative contracts already implemented by:

- TASK-110 — ship systems/readiness/fuel;
- TASK-112 — board, launch, orbit/station, undock, return and landing voyage;
- TASK-114 — deterministic galaxy navigation, route planning and hyperspace;
- TASK-128 — live star-system simulation and representation residency;
- TASK-148 — one-live-world scene coordinator and hyperspace scene handoff;
- TASK-152 — same-system interplanetary target, cruise and arrival.

The model acceptance requires all six reports to pass and additionally verifies cross-contract readiness, fuel, transition, persistence, navigation-identity and bounded-residency chains.

## Cross-system target invariant

A selected planet belongs to exactly one current star system. `InterplanetaryTravelRuntime.IsSelectionConsistentWith()` validates that its cached source/target transaction agrees with `GalaxyNavigationRuntime` and that both current and selected planets exist in the current system.

After a successful hyperspace jump, `GalaxyNavigationRuntime` clears its planet target. The live success path now immediately calls `InterplanetaryTravel.SynchronizeSelection(GalaxyNavigation)`, preventing a stale old-system `TargetSelected` transaction from leaking into the destination system.

## Live runtime closure

The F5 TASK-178 acceptance waits for the six component reports and then validates the live Godot state:

1. interplanetary selection state is synchronized with galaxy state;
2. the world scene shell is the single shell matching the current voyage/travel context;
3. the star-system simulation is bound to the same system and current planet;
4. a piloted voyage cannot exist without a commissioned, flight-ready ship;
5. the current planet belongs to the current system;
6. any selected planet belongs to the current system;
7. world residency policy matches Surface/Orbit/Station/Transit context.

PASS output is intentionally detailed so a future cross-subsystem regression identifies the broken boundary rather than only reporting a generic F5 failure.

## Acceptance

Run `tools\\run-section37-quality.cmd`, then start **New Game** and press F5 after the initial world settles. Required new line:

```text
TASK-178 spaceflight navigation subsystem acceptance PASS: contracts=6/6; ship=1; voyage=1; galaxy=1; starSystem=1; interplanetary=1; worldScene=1; readinessChain=1; fuelChain=1; transitionChain=1; persistenceChain=1; navigationIdentity=1; boundedResidency=1; selectionSync=1; worldContext=1; starSystemSync=1; shipVoyageSync=1; currentPlanetScope=1; targetScope=1; liveResidency=1; ...
```

For manual cross-system smoke after F5: repair/commission the ship, reach the orbital station, select a reachable different system and hyperspace. `TASK-114 player hyperspace jump PASS` must include `planetTargetCleared=1; interplanetarySync=1`, and the destination system must open at its orbital station without retaining a planet target from the previous system.


## TASK-178.1 pilot-control ownership

The live closure additionally requires `pilotControl=1`. Unpiloted ships must have pilot control disabled; piloted surface/station ships must be parked with physics off and no external command; manual flight must expose `ManualInputOwnershipActive`; navigation assist may own external control only when explicitly enabled by the player.


## TASK-178.2 orbital navigation and presentation

External manual flight exposed three presentation/integration gaps that the original TASK-178 closure did not measure: navigation assist could stop outside the station capture sphere without executing `TryDock`, the star-system model used a 120x simulation clock with metre-scale planetary/moon orbits, and non-surface world shells inherited the surface atmospheric `WorldEnvironment`.

TASK-178.2 makes `K` a complete assist transaction for the Stage-1 station/planet target. It brakes only while speed must be shed, then applies low forward thrust until the existing range+speed capture contract is true and invokes docking/landing automatically. `Enter` still performs the same transaction manually. Docking switches to `StationInterior`, parks the ship at the canonical dock pose, loads a lit hangar shell and opens the station-services UI.

The system presentation now uses a 1x simulation clock, kilometre-scale compressed planet spacing, moon orbits measured in hundreds of metres and periods measured in tens of minutes, and explicit star/planet/moon visual-size hierarchy. The focused planet is presented as an orbital backdrop; local station/ship statistical proxies are suppressed because physical counterparts already exist.

Finally, `WorldSceneEnvironmentPresentationRuntime` owns non-surface environment profiles. Orbit, interplanetary transit, hyperspace and station interior are dark/fog-free and can no longer inherit the blue surface atmosphere. Returning to Surface reconstructs the atmospheric sky and reapplies the current deterministic weather state.

F5 `TASK-178.2` requires: `orbitClock=1`, `planetSpacing=1`, `moonCadence=1`, `visualHierarchy=1`, `assistDock=1`, `localProxyPolicy=1`, `spaceEnvironment=1`, and `stationInterior=1`.

## TASK-178.5 — arcade kinematics and continuous orbital collision

The default piloted flight mode is now explicitly arcade/heading-coupled. `ArcadeFlightAssistRuntime` rotates the existing velocity direction toward the current ship-local translation axes after angular motion, preserving speed while preventing the accidental permanent world-space drift that made a ship continue away from a planet after turning toward it. `G` disables this coupling and exposes deliberate inertial-drift mode.

Orbital Star/Planet/Moon presentation now also defines the solid-body gameplay envelope. `StarSystemSimulationNode.TryGetBodyDisplaySphere` is the single source of live display centre/radius for both approach and collision. `TryGetFirstSolidBodyHit` uses continuous segment/sphere intersection with a ship-radius safety expansion so a high-speed frame cannot tunnel through a body.

For the current landable planet, the existing TASK-178.4 outer entry shell is also a physical free-flight transition boundary: a manual inbound ship crossing it at safe speed can enter the 220 m curved-surface approach without navigation assist. Unsafe penetration continues to the solid surface envelope and produces a deterministic fatal impact rather than visual pass-through.
