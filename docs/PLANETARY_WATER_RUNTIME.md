# TASK-188 — Planetary Water, Swimming & Underwater Rendering

TASK-188 implements Technical Specification §9.6 as a bounded gameplay/rendering subsystem without physical liquid simulation.

## Water model

Water has one semantic radial height above the current planet reference surface. On planets with high water coverage the runtime exposes an ocean; lower non-zero coverage uses local simplified lake bodies. The visible ocean is a camera/player-centred bounded curved patch, not a resident planet-scale mesh. Its vertices use the same `PlanetSurfaceCurvedPatchDescriptor` spherical sag as terrain, so floating-origin and radial-surface rebasing remain compatible.

The starter temperate planet uses the existing `0.55 m` semantic water level. Terrain above the level remains land; terrain below it is visually covered by the ocean. Local lakes use deterministic centres/radii and the same curved mapping.

## Rendering

`PlanetaryWaterSurfaceNode` owns the surface shader. It provides two animated shader-wave components, Fresnel/specular response to the world environment and depth-buffer-based shallow/deep colour darkening. Transparent water uses a depth prepass. No runtime mesh deformation or fluid solver is used outside the shader.

When the player camera crosses below the water surface, `UnderwaterPostEffect` enables a full-screen screen-texture post-process with water tint, mild refraction/wobble and edge darkening. HUD remains above the effect.

## Gameplay

Water interaction is resolved analytically from the player's logical East/North position and signed body/camera depth. Separate Schmitt thresholds prevent repeated swimming/underwater toggles at the surface.

While swimming, WASD remains tangential movement. Space ascends, Ctrl descends, and with no vertical command a bounded buoyancy-like controller tends toward a shallow immersion depth. This is a movement controller only; there is no physical fluid simulation.

`PlayerSurvivalRuntime` now distinguishes `Swimming` from `Underwater`. Oxygen drain is forced only when the camera/head is submerged. Merely wading or swimming with the head above water does not trigger the underwater oxygen penalty.

The legacy `Gameplay/WaterPool` `Area3D` is hidden and has monitoring disabled; it is no longer authoritative for swimming.

## Acceptance

F5 must include:

`TASK-188 (F5): PASS fixed=1 ocean=1 lakes=1 swim=1 post=1`

and Output must include `TASK-188 planetary water acceptance PASS` with fixed level, ocean, lakes, shader waves, reflection, depth darkening, underwater post, swimming, oxygen, no-fluid-simulation, live-node and retired-legacy-volume flags equal to 1.

Manual smoke: enter water on foot, cross the surface repeatedly without state chatter, swim with WASD/Space/Ctrl, submerge the camera and confirm the underwater effect/oxygen drain, then surface and confirm both clear. Flight, terrain streaming, station docking and save/load must remain unchanged.
