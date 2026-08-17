# TASK-208 Texture, Material and Lighting Budgets

TASK-208 turns Technical Specification sections 26.2 and 26.3 into executable content/runtime policy.

## Texture classes

Maximum raster dimensions are bounded by content role: player/large ship/large building/tiled surface 2048, NPC up to 2048, ordinary object/plant up to 1024 and UI icon up to 512. The release validator inspects shipping PNG/JPEG/WebP dimensions and rejects files that exceed their classified ceiling. Unclassified shipping raster textures are conservatively capped at 2048.

Production content must prefer atlases and reusable materials. The existing TASK-184 production model ceiling of five material slots per asset remains authoritative.

## Surface lighting

The detailed surface owns one directional star plus WorldEnvironment ambient lighting. Local Omni/Spot lights are presentation accents only. TASK-208 keeps at most six nearby local lights resident and gives them no local dynamic shadows. Distant local lights are reduced to zero energy instead of remaining active outside the residency envelope.

## Interiors and caves

Station/interior shells may retain at most eight nearby dynamic local lights and at most two pre-authored shadow-casting lights. Static/baked illumination remains the content-pipeline baseline required by section 26.3; TASK-208 does not fabricate a LightmapGI bake in a container without Godot. Cave prefabs use a stricter four-light, zero-local-shadow budget.

## Runtime ownership

The culler runs at 4 Hz. Priority is stable: cockpit/dock lights, hangar/cave lights, discovery/guide lights, then ordinary local lights; within a class the nearest light wins. Culling changes presentation only. Collision, AI, persistence, quests, weather and the authoritative directional star are not modified.

F5 validates policy constants plus the live directional star, ambient environment and currently active local/shadow budgets. A real LightmapGI bake remains an art/content authoring acceptance item rather than a claim made by TASK-208.
