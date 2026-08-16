# TASK-180 — Production Procedural Visual Language

## Purpose

TASK-180 is a visual-content pass over the already implemented Stage-2 flight stack. It upgrades flight-facing placeholder primitives into bounded compound forms and semantic material profiles without changing flight physics, collision envelopes, save schema, orbital distances, terrain residency or navigation contracts.

The source snapshot contains `Project_Horizon_Technical_Specification_v2.0.pdf` only as a Git LFS pointer, so this iteration does not reconstruct unavailable PDF text. Scope is grounded in the requirements already mirrored in `REQUIREMENTS_STATUS.md` and in the deferred art-content boundary recorded by TASK-164.

## Player spacecraft and cockpit

`ArcadeShip.tscn` keeps the authoritative `CharacterBody3D` and its single gameplay `BoxShape3D`. The visual hierarchy now adds lateral chines and a dorsal spine to the exterior silhouette. `Visuals/CockpitInterior` adds an instrument panel, primary/side emissive displays, left/right consoles and canopy frame members. These are render/light nodes only and do not create additional colliders.

## Orbital station

`Gameplay/OrbitalStation/VisualDetail` adds a habitation ring, central hub, paired radiators and antenna masts. Dock tunnel collision remains exactly in the pre-existing authoritative nodes; the new detail subtree is mesh-only.

## NPC spacecraft

NPC ships use a nine-part compound silhouette: hull, wings, nose, dorsal spine, canopy, two nacelles and two engine glows. The pre-existing spherical gameplay collision remains authoritative and unchanged.

## Star-system materials and detailed planet

Star-system proxies now use semantic StandardMaterial3D profiles rather than albedo-only materials: stars are emissive; stations and ship contacts are metallic; moons are highly rough; planets use archetype-aware roughness. The focused cube-sphere globe keeps six geometry faces but now assigns six deterministic face material variants. Water, atmosphere and cloud shells have separate roughness profiles.

## Acceptance

F5 runs `TASK-180 production visual language acceptance` and requires:

- player exterior: at least 11 direct mesh parts;
- cockpit interior: at least 9 direct mesh parts;
- station VisualDetail: at least 6 mesh parts;
- NPC ship compound visual: 9 parts;
- detailed planet: exactly 6 face-material instances with seam-safe deterministic vertex-colour breakup;
- at least one live semantic star-system material profile;
- original player/station collision shapes still present;
- cockpit and station detail subtrees remain visual-only.

The acceptance is read-only: it sets the HUD state to `RUNNING`, inspects the live scene tree, then emits `PASS`/`FAIL` without mutating gameplay state, so no restore stage is required. Use `F2` (`ship_camera`) while piloting for the cockpit visual smoke; station approach, NPC traffic and Orbit provide the remaining manual visual checks.

The static validator is `tools/validate-task180-production-visual-language.py`; it is included in local section-37, CI and release gates.

## Deliberate limits

This is a production **procedural** visual pass, not a claim of final hand-authored AAA content. No author GLTF payload, texture set, baked LOD chain, decal atlas or authored PBR texture atlas is present in the supplied snapshot. Those assets can be imported in a later art-content pass without changing the TASK-180 gameplay/collision contract.
