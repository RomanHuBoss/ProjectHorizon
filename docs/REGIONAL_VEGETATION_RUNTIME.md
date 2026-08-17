# TASK-196 Regional Vegetation Runtime

TASK-196 implements Technical Specification section 11 on top of the TASK-116 ecology catalog and TASK-194 world-residency coordinator.

## Regional batching

Flora remains deterministic from the ecology/planet seed. Rendering batches are now keyed by `(32 m vegetation region, flora species)` instead of one planet-local MultiMesh per species. Each batch owns LOD0 and LOD1 MultiMesh geometry; this keeps instancing regional and makes individual regions independently simplifiable/cullable.

## LOD and residency

Near batches use LOD0. Mid-distance batches use reduced-segment LOD1. Small Tuft/Pad/Fungus-style objects are removed beyond 52 m and all vegetation is culled past the local mid-distance envelope. TASK-194 residency participates in the decision: Full regions may use LOD0/LOD1, Simplified regions use LOD1 only, and Preload/non-resident regions do not instantiate visible vegetation.

The existing TASK-158 terrain streamer remains responsible for physical terrain. TASK-196 does not expand collision terrain residency.

## Interactive promotion

The MultiMesh is the visual representation. A flora specimen is promoted to a full `StaticBody3D` interaction/hitscan proxy when required by one of the section 11.3 triggers: proximity, scanner targeting, damage, harvest, or an active procedural quest. Promoted objects demote after leaving the interaction envelope unless an active quest still pins the flora target. Harvested objects continue to persist through TASK-116 removed-instance deltas.

## Acceptance

F5 evaluates regional partitioning, both LOD tiers, small-object culling, TASK-194 residency integration, all five promotion reasons and the live region/type binding. Runtime visual review should verify that nearby plants remain detailed, farther plants simplify/disappear, and interaction/scanning remains functional while traversing region boundaries.
