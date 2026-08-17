# TASK-198 Modular Fauna Runtime

## Scope

TASK-198 implements Technical Specification v2.0 section 12 without changing the
TASK-116 ecology save schema. Species identity, biome compatibility, diet,
activity, aggression, health and seed-driven placement remain catalog-owned.
The new runtime supplies morphology compatibility, AI hierarchy, group steering
and distance-tier simulation.

## 12.1 Fixed skeleton families and modular morphology

Six body plans are authoritative:

- Biped
- Quadruped
- Hexapod
- Flying
- Aquatic
- Crawler

Each body plan maps to one immutable versioned skeleton descriptor. Procedural
modules are selected only from that descriptor's compatible lists: head, torso,
limbs, tail, horns and shell. Module IDs are namespaced by body-plan family and
`FaunaBodyPlanRuntime.IsCompatible` rejects any cross-family assembly.

Morphology is deterministic for `FaunaId + InstanceId`: repeating the same seed
produces exactly the same modules and bounded proportions, while different
instances of one species may receive different compatible modules, proportions,
roughness and small colour variation.

No runtime retargeting is attempted. A module from another skeleton family is
invalid by construction.

## 12.2 AI architecture

`FaunaBehaviorRuntime` implements a hierarchical state selection model with
utility scoring inside ordered layers:

1. Survival - Attack / Flee
2. Territory - ReturnToTerritory
3. Needs - Sleep / Drink / Graze
4. Social - FollowGroup
5. Awareness - Threaten / Investigate
6. Ambient - Wander / Idle

The complete specification state set remains supported:
`Idle, Wander, Graze, Drink, Sleep, Investigate, Flee, Threaten, Attack,
ReturnToTerritory, FollowGroup`.

Movement is split by locomotion family:

- ground fauna: steering target + shared `NavigationAgent3D`/TASK-124 navmesh;
- flying fauna: existing TASK-126 aerial steering, obstacle avoidance and
  altitude envelope;
- aquatic fauna: water-relative steering;
- social groups: simplified boids separation + cohesion + alignment.

## 12.3 Update tiers

Decision work is distance-tiered:

- <=25 m: 10 Hz;
- 25-70 m: 5 Hz;
- 70-150 m: 2 Hz;
- >150 m: no per-entity decisions; far population is statistical.

The 80 simplified TASK-116 fauna entries are never expanded to 80 scene nodes.
`FaunaStatisticalSimulationRuntime` aggregates them by species and advances
activity/territory statistics at 0.5 Hz.

`EcologyFaunaNode._Process` performs visual orientation/bob interpolation every
frame independently of the lower-frequency behavior decisions.

## Persistence

No save-schema bump is introduced. TASK-116 remains authoritative for ecology
identity, discoveries and flora removal deltas. Far-fauna statistics are derived
from deterministic content and are intentionally transient.

## Acceptance

F5 runs `FaunaModularAcceptanceRunner` and requires:

- all six fixed skeleton families;
- compatible deterministic modular morphology;
- procedural per-instance variation;
- full HFSM/utility state contract;
- ground navmesh + boids + existing aerial steering integration;
- 10/5/2/statistical distance tiers;
- statistical population equal to the simplified fauna plan;
- per-frame visual interpolation and live morphology binding.
