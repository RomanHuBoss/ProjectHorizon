# TASK-176 — Planetary Surface Subsystem Closure

## Purpose

TASK-176 is the integration boundary for the Stage-2 planetary-surface stack. It does not replace or reimplement the existing subsystem acceptances. Instead it composes their normative runners and then verifies the live Godot residency/navigation/player/presentation state that exists at the moment F5 is executed.

The source snapshot's PDF specification files are Git LFS pointers rather than PDF payloads, so TASK-176 intentionally introduces no new inferred PDF requirement. It closes the already documented TASK-150…174 surface architecture as one coherent subsystem.

## Model contract

`PlanetarySurfaceSubsystemAcceptanceRunner` requires all eleven existing acceptance runners to pass together:

1. TASK-150 planet environment;
2. TASK-152 interplanetary travel;
3. TASK-154 planet-scoped surface content;
4. TASK-156 planet-specific terrain;
5. TASK-158 bounded surface streaming;
6. TASK-160 surface world composition;
7. TASK-166 planetary weather;
8. TASK-162 planet-global floating-origin frame;
9. TASK-170 radial surface frame/geodesy;
10. TASK-172 physical arbitrary-up surface frame;
11. TASK-174 curved cube-sphere collision/navigation.

It also enforces four integration chains:

- **Persistence:** current/target planet, transfer state, planet deltas, resource depletion, weather time and floating-origin state all round-trip.
- **Traversal:** streamed addressing, floating-origin rebases, six cube faces, seam handoff, physical mapping and curved rebase remain continuous.
- **Bounded residency:** the gameplay window remains 25 active chunks / 9 collision chunks.
- **Planet identity:** all four starter planets retain distinct scoped environment/content/terrain/world identities.

## Live Godot contract

F5 additionally requires eight live invariants:

- settled curved streamer at 25/9;
- TASK-124 curved NavigationServer map fully resident and frame-aligned;
- on-foot player surface frame aligned with the current curved/radial Up;
- initialized radial atmosphere/world presentation and streamed resources;
- live ecology and all 20 planetary POIs;
- TASK-174.1 cold-start/backface/clearance safety evidence;
- finite live planetary-weather time;
- radial + physical frame alignment.

The startup readiness line is:

```text
TASK-176 planetary surface subsystem READY: ... chunks=25/25; collisions=9/9; navRegions=25/25; ...
```

The F5 acceptance line must report `contracts=11/11`, all four integration chains as `1`, `globe=1`, all eight live invariants as `1`, and the live 25/9 + navigation-region counts.

## TASK-174.2 regression bundled with alpha.176

TASK-126 previously evaluated the altitude envelope of flying fauna even after the fauna had died and become hidden. Their frozen transforms could therefore create `altitude=0` although the live steering controller was healthy.

Alpha.176 defines a live participant as `Flying && Visible && Health > 0`. Only these nodes must currently occupy the live terrain-relative altitude envelope. To prevent a vacuous PASS when every flying fauna node is dead, F5 independently applies the shared altitude controller to an out-of-envelope sample and requires both a non-zero correction and an incremented `AltitudeCorrections` counter.

TASK-126 diagnostics therefore include:

```text
activeFlying=<0..4>; altitude=1; altitudeProbe=1; faunaProbeSamples=4
```

Killing one or more flying fauna before F5 is the preferred regression scenario.

## External acceptance

1. Run a clean Windows build and `tools\run-section37-quality.cmd`; require zero warnings/errors and both TASK-174.2/TASK-176 static gates PASS.
2. Start **New Game** and wait for TASK-176 READY at 25/9 and 25/25 navigation regions.
3. Optionally kill one to four flying fauna before F5.
4. Press F5 and wait for the final acceptance lines.
5. Require TASK-126 PASS with `altitude=1; altitudeProbe=1; faunaProbeSamples=4`.
6. Require TASK-176 PASS with `contracts=11/11`, all chain/live flags `1`, `chunks=25/25`, `collisions=9/9`, `navRegions=25/25`.
