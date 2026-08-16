# TASK-164 — Planet Surface Visual Language & Procedural Props

## Goal

Replace the most visible single-primitive surface placeholders with a deterministic, bounded procedural visual-language layer without changing gameplay identity, persistence, collision budgets or terrain streaming residency.

## Runtime contracts

- Streamed resources are classified into four deterministic visual families (`ore`, `crystal`, `fiber`, `organic`) from catalog tags and rendered as compound low-poly silhouettes. Resource node IDs, deterministic yields, single gameplay collision and depletion persistence remain unchanged.
- Planetary POIs keep their stable placement/interaction identity and collision, while category-specific child geometry provides readable silhouettes.
- Active fauna keep the existing AI, health and collision runtime while adding body-plan child geometry. Flying altitude is terrain-relative so the visual/runtime upgrade cannot violate TASK-126 altitude invariants on macro relief.
- Flora continues to use MultiMesh batching; pad/fungus primitives use radial silhouettes rather than the prior box/blob placeholders.
- Terrain geometry and height sampling are unchanged by TASK-164. Vertex color breakup is derived from logical coordinates plus height/slope, so streamed and distant terrain gain material variation without bitmap texture residency.

## Regression closures

### TASK-154

Planet-scoped POI planning uses a deterministic `±48 m` candidate lattice. Legacy `PlanetaryPoiPlanner.Plan()` remains on the historical `±34 m` lattice so the reviewed golden fixture does not change. The all-four-starter-planets unit test now builds and validates the complete POI plan.

### TASK-126

Flying fauna no longer use their initial airborne territory Y as the ground reference. Fresh flying spawns are clamped to a terrain-relative band and both steering and acceptance check the terrain height under the current horizontal position; the authored airborne home position remains available for ReturnToTerritory/FollowGroup behavior.

## Budget

TASK-164 does not increase the live terrain gameplay residency: 25 visual chunks / 9 collision chunks remain the TASK-158 contract. Compound resource/POI/fauna geometry is attached only to already-resident gameplay objects. No new save schema or procedural object identity is introduced.

## Acceptance

F5 must return TASK-154 PASS, TASK-126 PASS with `altitude=1`, and `TASK-164 surface visual language acceptance PASS`. Manual smoke additionally checks that resource collection/depletion, POI interaction, fauna interaction and terrain collision behave exactly as before the visual upgrade.

This is a procedural visual foundation, not a claim of final authored art. GLTF/PBR asset ingestion, atlas/decals, authored LODs and art-direction polish remain future content work.
