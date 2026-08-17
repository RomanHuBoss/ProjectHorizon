# TASK-216 Production 3D Model Art Overhaul

TASK-216 is a production-art pass, not a gameplay rewrite. It raises the close-range visual density of the Explorer, Interceptor and orbital station, and replaces primitive runtime resource presentation with production GLB assets while preserving the collision, mount-marker, persistence and interaction contracts established by TASK-184/186.

## Shipping asset set

The client now ships **24 shipping GLBs across eight visual families**:

- `SHP_Explorer_01` — LOD0/LOD1/LOD2;
- `SHP_Interceptor_01` — LOD0/LOD1/LOD2;
- `STN_Orbital_01` — LOD0/LOD1/LOD2;
- five resource families — Ore, Salvage, Crystal, Fiber and Organic, each with LOD0/LOD1/LOD2.

LOD0 is deliberately the close-range art target. LOD1 and LOD2 retain the established aggressive reduction policy so TASK-200/TASK-202 budgets remain meaningful. Gameplay `CollisionShape3D` nodes remain Godot-authored and are not embedded in GLB files.

## Ship and station art direction

The Explorer keeps its broad exploration silhouette but adds service hatches, recessed vent banks, landing-gear doors, sensor facets, underside radiator elements, fastener/detail rhythm and authored equipment geometry. The Interceptor remains a narrower arrowhead combat silhouette with weapon doors, sensor/avionics hardware and close-range panel breakup. The station adds structural ribs, cargo tanks, conduits, spindle armour and communications/service-truss details to make approach and docking scale readable.

A small reviewed subset of Kenney Space Kit geometry is used as CC0 authoring input for selected close-range detail modules. The imported geometry is normalized, rematerialed into the Project Horizon PBR palette and baked into self-contained shipping GLBs. It does not supply gameplay collision or identity. See `docs/THIRD_PARTY_ASSETS.md`.

## Resource art

Surface, cave and starter resource nodes now resolve to production resource scenes before considering the legacy C# primitive presentation. Resource type maps to one of five silhouettes:

- Ore — irregular faceted mass with secondary nodules/metal veins;
- Salvage — layered manufactured plate/scrap cluster;
- Crystal — multi-prism shard cluster and emissive core;
- Fiber — bundled tapered stems and nodes;
- Organic — irregular lobes, tendrils and core.

Each resource wrapper uses `ProductionModelLodController` with 18 m and 45 m transitions. Existing `SalvageResourceNode` collision remains authoritative. The old `SphereMesh`/`CylinderMesh` path exists only as an emergency fallback when the shipping scene cannot be loaded.

## Acceptance boundary

Static/F5 acceptance verifies files, LOD chains, signatures, live resource replacement and collision separation. **Manual visual acceptance is still required** because triangle counts cannot prove that a silhouette, material hierarchy or close-range composition looks good. The owner should inspect the Explorer in chase/external view, an Interceptor at combat distance, the orbital station on approach/docking, and several resource families at walking distance before marking TASK-216 VERIFIED.
