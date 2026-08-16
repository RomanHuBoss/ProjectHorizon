# TASK-182 — Flight Runtime Closure & Streaming Stability

## Purpose

TASK-182 closes the remaining runtime defects observed after the TASK-180.3
virtual-flight-stick owner run. It does not replace the roll-dominant virtual
stick model. It adds spring centering to the live stick state, a hysteretic
atmosphere-presence state machine, and bounded terrain-refresh coalescing.

## 1. Spring-centered virtual flight stick

TASK-180.3 intentionally made the virtual stick stateful. Owner runtime showed
the undesirable edge case: a small horizontal deflection remained indefinitely
when the physical mouse stopped, so the ship kept rolling/yawing without further
pilot motion.

TASK-182 keeps the stateful control position but adds a spring return:

- physical mouse motion still moves the virtual stick;
- the stick holds its last value for `0.08 s` to preserve deliberate micro-input;
- after that idle delay it exponentially returns to neutral at `5.5 / s`;
- values below the inner neutral threshold snap to zero;
- middle mouse still performs immediate recenter;
- horizontal attitude remains roll-dominant with coordinated yaw;
- vertical attitude remains unrestricted pitch;
- A/D remain independent lateral thrusters.

This is a spring-centered virtual control, not the old one-frame FPS mouse-rate
controller.

## 2. Atmosphere transition hysteresis

The owner log contained a boundary oscillation `EXIT 590.0 m -> ENTER 589.9 m`.
A single atmosphere-presence threshold therefore created state chatter when the
ship hovered around the blend boundary.

TASK-182 uses separate thresholds:

- enter atmosphere when blend is `>= 0.018`;
- remain inside until blend falls to `<= 0.004`;
- while the blend is between these values, preserve the current presence state.

Atmospheric forces still use the continuous 110..620 m blend. The new state
hysteresis only prevents presentation/event ownership from toggling on adjacent
frames.

## 3. Terrain streaming refresh closure

During fast low-altitude flight the log showed multiple adjacent refresh plans
being superseded before completion and stale-result counts in the low double
digits. It also showed a `queued=50` refresh immediately before PlanetRuntime
was suspended at the station.

TASK-182 changes runtime streaming policy:

- while a refresh is in flight, an adjacent one-chunk observer movement is
  coalesced instead of cancelling/replanning the active revision;
- movement beyond the bounded lag immediately replans, preserving safety;
- once the active revision completes, the next physics tick targets the latest
  observer position;
- when PlanetRuntime is inactive, surface observer handoff is suppressed, so
  deactivation cannot create a pointless full-window refresh immediately before
  the streamer is disabled.

The existing 25 active / 9 collision chunk budget, cancellation model, Int64
coordinate safety and worker failure reporting remain unchanged.

## Acceptance

F5 must report:

`TASK-182 flight-runtime closure acceptance PASS`

The live smoke test must also satisfy:

1. small mouse deflection followed by no physical motion returns the `○` marker
   smoothly toward `+` and stops the induced angular motion;
2. continuous mouse motion still permits sustained roll/pitch and full loops;
3. repeated traversal around the atmosphere boundary does not produce adjacent
   ENTER/EXIT chatter;
4. fast surface flight remains collision-safe and streamer `failed=0`;
5. surface-to-orbit/station deactivation does not schedule a new 25-create /
   25-remove observer refresh;
6. no regression of TASK-180.1/180.2/180.3 error guards.
