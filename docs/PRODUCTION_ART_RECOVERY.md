# TASK-220 — Production Art Recovery

## Why this iteration exists

The owner manually rejected the TASK-216/TASK-218 visual result after a successful build/runtime smoke test. The rejection is treated as authoritative visual evidence: crystal deposits read as flat “pancakes” and the player ship carried too much near-black mass. The old technical GLB/LOD/PBR checks therefore remain regression infrastructure only; they are not accepted as evidence of production visual quality.

## Root causes corrected

1. **Crystal/ice authoring axis defect.** `crystal_prism` and similar longitudinal helpers are authored along local +Z, but the resource functions translated them upward on +Y without first rotating the geometry. The cluster footprint therefore dominated its height. TASK-220 introduces `upright(...)`, which bakes Z→Y orientation into longitudinal resource meshes before placement.
2. **Graphite-first ship palette.** The primary hull atlas cell used very dark graphite and large wing surfaces used darker panel/accent slots. Under Forward Mobile this could collapse the craft into a black silhouette. TASK-220 changes the primary hull semantic slot to light industrial alloy, uses the primary slot for the main wing surfaces, and reserves the dark slot for canopy/recess treatment.
3. **Flat resource shading.** Runtime catalog tinting previously replaced every imported resource submesh with nearly the same material except for a simple brightness cycle. TASK-220 adds semantic material roles: host matrix/bed/shelf is rough and dark; crystal/spire/blade parts are cleaner and brighter; veins/cores/throats receive controlled highlight/emission; salvage metal receives higher metallic response.

## Rebuilt resource morphology

All ten production resource families retain three authored LODs and separate gameplay collision.

- **Crystal:** tall 5–7 sided prisms with pointed terminations, a dominant central spire and a low host-rock matrix.
- **Ice:** tall narrow blades plus a central ice core and low shelf.
- **Glass:** upright obsidian-like blades instead of flat plates.
- **Fiber:** upright tapered stems with node details and a denser core.
- **Gas:** upright geological vent chimneys with small bright throats over a rough bed.
- **Salt:** stepped evaporite pedestal with cubic/rectangular salt crystals.
- **Exotic:** asymmetric core, radial field spokes and secondary upright spires.
- **Ore/Salvage/Organic:** rebalanced relief and authored sub-part roles while preserving distinct family silhouettes.

Measured LOD0 cluster aspect ratios after regeneration:

- Crystal: approximately `Y / max(X,Z) = 1.43`.
- Ice: approximately `Y / max(X,Z) = 1.36`.

TASK-220 runtime acceptance requires Crystal >= 1.25 and Ice >= 1.20.

## Ship material recovery

The stable atlas/material IDs remain unchanged for compatibility, but their visual meaning is corrected:

- `MAT_Hull_Graphite`: now a light primary alloy rather than near-black graphite;
- `MAT_Hull_Panel`: medium-value secondary panels;
- `MAT_Canopy_Smoked`: the primary intentionally dark surface;
- `MAT_Safety_Accent`: limited orange safety/accent strips;
- `MAT_Engine_Emissive`: bounded blue engine emission.

The primary player and interceptor wing surfaces now use the light primary hull slot. This specifically prevents the “half-black ship” failure mode while retaining panel/recess contrast.

## Runtime acceptance

F5 includes `TASK-220 production art recovery acceptance`. PASS requires:

- all 9 hard-surface hero LOD GLBs available;
- all 30 resource GLBs available;
- no live procedural resource fallback;
- primary hull atlas luminance >= 0.55;
- Crystal LOD0 verticality >= 1.25;
- Ice LOD0 verticality >= 1.20;
- production collision remains separate from visual GLBs.

Expected output:

```text
TASK-220 production art recovery acceptance PASS: hardSurfaceLod=9; resourceGlb=30; liveResources=<N>; fallbacks=0; hullLuma=<>=0.55; crystalVerticality=<>=1.25; iceVerticality=<>=1.20; collisionSeparate=1; result=production-art-recovery-runtime.
```

## Manual visual acceptance

F5 is intentionally insufficient to declare the art visually accepted. The owner must inspect close-up gameplay rendering and confirm all of the following:

1. Explorer body is predominantly light/medium alloy and no longer reads as a half-black object.
2. Canopy/recesses remain visibly dark without swallowing the hull silhouette.
3. Crystal deposits are unmistakably vertical, pointed crystal clusters rather than discs/plates/pancakes.
4. Ice, glass, salt, gas vents, exotic deposits and ordinary ore remain distinguishable by geometry before relying on color.
5. Resource sub-parts show readable roughness/value hierarchy instead of one flat material.
6. No LOD pop exposes a radically different silhouette at the 18 m / 45 m resource transitions.
7. No visual GLB introduces gameplay collision changes.

TASK-216 and TASK-218 are recorded as `SUPERSEDED` for visual acceptance after owner rejection. Their technical contracts remain regression gates only. TASK-220 remains `IMPLEMENTED` until the new manual visual review passes.
