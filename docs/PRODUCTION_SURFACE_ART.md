> **Status after owner review:** visual acceptance was rejected and is superseded by TASK-220. This document remains a historical technical/PBR contract; see `PRODUCTION_ART_RECOVERY.md` for the replacement visual baseline.

# TASK-218 — Production PBR Texture Atlas & Resource Visual Diversity

TASK-218 is the next production-art layer after TASK-216. It does **not** replace the pending manual visual acceptance of TASK-216; it improves the actual surface treatment and expands the resource silhouette vocabulary while preserving gameplay collision and LOD contracts.

## Hard-surface PBR atlas

Explorer, Interceptor and the orbital station are rebuilt with one reusable hard-surface atlas material per GLB. The authoring generator produces four deterministic **1024 × 1024** maps in `Assets/Textures/Production` and embeds the same four maps into each shipping GLB:

- `TEX_HardSurface_BaseColor.png`;
- `TEX_HardSurface_Normal.png`;
- `TEX_HardSurface_MetallicRoughness.png`;
- `TEX_HardSurface_Emission.png`.

The 4×4 atlas reserves semantic cells for graphite hull, panel metal, safety accent, smoked canopy, engine emission and station hull/panel/safety/dark/light surfaces. Geometry receives deterministic UV0 projected from its two dominant local axes and remapped into its semantic atlas cell. Macro panel seams, fastener cues and low-amplitude surface variation are deliberately large-scale; there is no expensive microdetail pass.

Each hero LOD GLB therefore has one material, four embedded PBR maps, no external image URI and no embedded collision. Existing `MNT_*` attachment markers and `ProductionModelLodController` LOD chains are preserved.

## Ten resource families

The production resource library expands from five to **ten resource families**, each with LOD0/LOD1/LOD2:

`Ore`, `Salvage`, `Crystal`, `Fiber`, `Organic`, `Ice`, `Gas`, `Salt`, `Glass`, `Exotic`.

The new families are not color-only aliases. They have distinct geometry: ice shelves/shards, vent fields for gas pockets, stepped salt blocks, volcanic-glass splinters, and an exotic core/spoke formation. All 42 resource definitions route deterministically to one of the ten production scenes. Runtime catalog tint/material override remains authoritative, and the old procedural primitives remain emergency fallback only.

## Collision and runtime ownership

No resource, ship or station collision is embedded in GLB. Gameplay collision remains in the existing Godot scene/runtime owners. The atlas and model generation scripts are authoring/build-time only; no trimesh/Pillow model generation is introduced into gameplay runtime.

## Acceptance

Static TASK-218 acceptance requires four 1024 maps, nine atlas-equipped hero GLBs, ten resource families / thirty resource GLBs, routing coverage for all 42 catalog definitions, self-contained GLBs and preserved separate collision. F5 additionally checks resource/texture availability, exact atlas dimensions, production-resource replacement with zero fallback, and collision separation.

**Manual visual acceptance is still required.** Inspect Explorer/Interceptor at close range for readable panel scale and material response, approach the station for industrial scale/detail, then inspect all ten resource families under surface/cave lighting. A static PASS cannot promote TASK-216 or TASK-218 to VERIFIED without that visual run.
