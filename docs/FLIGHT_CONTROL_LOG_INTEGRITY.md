# TASK-180.3 — Flight Control & Runtime Log Integrity

Alpha.180.3 replaces the live ship's one-frame relative-mouse steering with a **stateful virtual flight stick**. Mouse motion changes a bounded virtual control position. If physical mouse motion stops, the virtual stick remains deflected and the ship continues the commanded manoeuvre until the pilot moves the stick back toward centre or presses the middle mouse button to recenter it.

The horizontal virtual-stick axis is deliberately **roll-dominant**. It commands full roll and only a small coordinated yaw component; the vertical axis commands pitch. A/D remain independent lateral thrusters. The live controller does not decay mouse state between physics ticks and does not map mouse motion to translation. Local-axis attitude integration remains unrestricted, so pitch/roll can pass through 180/360 degrees.

## Log-derived runtime repairs

The owner log was treated as one multi-run artifact rather than as isolated warnings. It exposed three distinct defect families:

1. historical `TerrainChunkManager.ChebyshevDistance` overflow caused by int-minimum/sentinel arithmetic; the current implementation resolves observer chunks directly and uses saturated Int64 distance arithmetic;
2. repeated near-floor correction chatter around the 3.2 m surface guard; the guard now has a tolerance band and a larger recovery separation pad, and ordinary correction is diagnostic output rather than warning spam;
3. repeated Godot light-culler `create_frustum_points` failures after the surface runtime released while the orbital camera used an extreme far/near ratio. Flight cameras now use a bounded 0.25 m .. 900 km clip envelope, the starfield is inside 90% of the far plane, and surface weather/directional-shadow ownership is gated so surface presentation cannot mutate the orbital frame after handoff.

## Runtime acceptance

F5 must report `TASK-180.3 (F5): PASS stick=stateful roll>yaw frustum=bounded logguards=1` and Output must contain `TASK-180.3 flight-control/log-integrity acceptance PASS`.

Manual flight smoke:

- move the mouse right and stop moving it: the ship must continue a roll-dominant manoeuvre because the virtual stick remains right of centre;
- move the mouse back left toward centre: angular command must reduce naturally; middle click recenters immediately;
- move vertically: pitch must respond continuously and permit full loops;
- A/D must strafe without changing the mouse virtual-stick state;
- complete surface → orbit → surface transitions and inspect Output: no `create_frustum_points`, no `OverflowException`, and no frame-by-frame surface-penetration warning/recovery chatter is acceptable.
