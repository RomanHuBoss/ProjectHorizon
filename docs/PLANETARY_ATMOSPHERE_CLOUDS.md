# TASK-190 — Planetary Atmosphere & Spherical Cloud Layers

Alpha `0.1.0-alpha.190` implements Technical Specification v2.0 §§9.7–9.8 as a bounded, low-cost surface rendering subsystem.

## Atmosphere

The surface scene owns one transparent spherical atmosphere shell centered on the active surface observer and aligned so local `+Y` equals the current radial planet Up. The shader uses a directional star vector, a zenith-to-horizon scattering gradient, horizon amplification, atmosphere-density-controlled opacity and a sunset tint when the star is near the horizon. It is intentionally a single-pass approximation: there is no volumetric multi-step ray marching.

The existing ProceduralSky/WorldEnvironment remains the fallback/background and keeps fog/ambient ownership. TASK-190 adds the shell as the near-atmosphere scattering layer rather than replacing the world-scene environment handoff contract.

## Clouds

A planet may expose zero, one or two spherical cloud shells. The layer count is bounded by the environment profile and never exceeds two. Both shells sample deterministic PNG noise textures and scroll their UVs using `TIME` and the current weather wind vector. Weather `CloudMultiplier` modifies effective density and opacity.

The old `CloudCluster_XX/Lobe_XX` local sphere blobs are retired and never rebuilt. Their root is kept only as a stable legacy scene anchor.

## Simplified cloud shadow

TASK-190 does not render a costly projected cloud-shadow map. Instead the directional surface light is attenuated by a bounded cloud-shadow factor derived from cloud density and weather intensity. This preserves the Technical Specification requirement for simplified surface darkening while keeping the low-profile renderer bounded.

## Surface-contact regression

The owner alpha.188 runtime log showed repeated `surface floor correction` / `surface contact RECOVERED` pairs while one low-altitude contact episode was still in progress. TASK-190 adds a Schmitt-like contact latch: the correction episode remains active until terrain clearance is at least 4.35 m for 12 consecutive physics frames. Lethal impact thresholds and `PlanetaryImpactRuntime` are unchanged.

## Acceptance

`F5` must show:

`TASK-190 (F5): PASS shell=1 clouds=<0..2> noise=1 shadow=1 noRayMarch=1`

Output must include `TASK-190 planetary atmosphere/cloud acceptance PASS` with shell, directional-star scattering, horizon, sunset, cloud layers, noise scrolling, density response, simplified surface shadow, no-ray-march, retired legacy blobs, live-node and contact-latch flags passing.

Manual smoke:

1. Observe horizon/zenith colour separation at morning/noon/sunset.
2. Use developer time/weather controls to move the star near the horizon and verify warm sunset tint.
3. On planets with clouds, verify broad spherical noise fields drift with wind instead of discrete ellipsoid blobs.
4. Clear/Wind/Storm should visibly alter cloud density and surface light intensity.
5. Take off and cross the surface/orbit handoff: surface shells must disappear once vacuum presentation owns the frame.
6. Skim terrain without a lethal impact: one contact episode may print one correction and later one recovered line; it must not alternate every frame. A lethal dive must still produce the existing TASK-180.2 death path.
