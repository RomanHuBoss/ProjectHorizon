# TASK-184 — Production 3D Asset Pipeline & LOD Integration

## Scope

TASK-184 closes the first executable baseline of Technical Specification §33 for the shipping vertical slice. Runtime presentation no longer depends exclusively on Godot primitive meshes for the player explorer, orbital station, and NPC ship traffic.

## Asset rules

- Transfer format: glTF 2.0 binary (`.glb`).
- World/model units: metres; transforms are authored at 1.0 scale.
- Coordinate convention: +Y up, -Z ship nose / forward presentation, +X starboard.
- Each production family ships as separate `LOD0`, `LOD1`, `LOD2` GLB files.
- LOD triangle counts must decrease monotonically and materially between levels.
- PBR material slots are bounded (no texture-per-part explosion).
- Gameplay collision is never imported from GLB. Collision remains authoritative in Godot scenes/runtime nodes.
- Attachment points are empty named markers using the `MNT_*` prefix.
- `.blend`, raw textures, source audio and editor caches are not part of the runtime asset folder or release ZIP.

## Integrated families

### SHP_Explorer_01

Player ship exterior. LOD0 contains the angular hull, delta wings, chines, dorsal/ventral silhouette parts, canopy and paired engines. Markers cover cockpit, paired weapon hardpoints, paired engines and landing gear reference.

### SHP_Interceptor_01

Shared NPC traffic baseline. The imported model is preferred; the old generated primitive model remains a hidden emergency fallback only when the PackedScene cannot be loaded.

### STN_Orbital_01

Orbital-station presentation matching the existing compound gameplay collision envelope: habitation ring, core, spine, arms, docking tunnel/guides, radiators and antennae. Physical collision stays in `SalvageRepairSlice.tscn` and is not embedded in GLB.

## Runtime LOD

`ProductionModelLodController` keeps exactly one of `LOD0/LOD1/LOD2` visible according to the current camera distance. Thresholds are family-specific. If no active camera is available the controller safely selects LOD0.

## Source/regeneration

`tools/content/generate-production-glb.py` deterministically creates the current baseline GLBs. It is an editor/build-time tool; no model generation occurs during gameplay.

## Acceptance

F5 runs TASK-184 after TASK-182. PASS requires:

- 3 production model families;
- all 9 GLB resources loadable;
- 3 complete LOD chains;
- at least 14 `MNT_*` markers across LOD0 family instances;
- player, station and NPC production models loaded;
- no `CollisionShape3D` inside production asset subtrees;
- authoritative gameplay collision still present;
- legacy primitive presentation hidden when imported assets are active;
- `ProductionModelLodController` active for all three families.

Expected Output prefix:

`TASK-184 production asset pipeline acceptance PASS:`

Manual smoke: external/chase view of player ship, station approach from >3 km to docking range, and NPC traffic observation while changing distance. No visible double-model overlap, LOD disappearance, or collision/docking regression is acceptable.
