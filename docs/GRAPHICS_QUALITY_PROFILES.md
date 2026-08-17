# TASK-202 — Graphics Quality Profiles & Runtime Scalability

## Scope

TASK-202 implements Technical Specification §26.4 as a persistent user-facing graphics profile contract. The selected profile is a presentation ceiling. It never changes authoritative physics, collision, combat, AI decision frequency, economy, procedural seeds, quest state, or persistence.

TASK-200 remains the adaptive pressure controller. It may temporarily reduce presentation below the selected ceiling, but it cannot raise quality above the user's selected profile.

## Profiles

| Setting | Low | Medium | High | Compatibility |
|---|---:|---:|---:|---:|
| Vegetation density | 55% | 85% | 100% | 45% |
| Vegetation distance | 58% | 100% | 118% | 50% |
| Surface presentation distance | 58% | 100% | 120% | 50% |
| Shadows | Low / 140 m | Medium / 320 m | High / 480 m | Off |
| Cloud layers | 1 | 2 | 2 | 1 |
| Atmosphere quality scale | 0.72 | 1.00 | 1.12 | 0.55 |
| Water wave scale | 0.55 | 1.00 | 1.15 | 0.35 |
| Water depth shading | 0.55 | 1.00 | 1.12 | 0.35 / simplified |
| Underwater distortion | 0.65 | 1.00 | 1.10 | 0.40 / simplified |
| Glow | Off | On | On | Off |
| Particles | 45% | 80% | 100% | 30% |
| Simplified shaders | No | No | No | Yes |

Low deliberately keeps surface/vegetation presentation distance in the specification's 50–60% band. High extends presentation distance without changing physical terrain residency. Compatibility is the least expensive presentation path and removes heavy effects.

## Renderer rule

When the live renderer reports the Compatibility rendering method, the effective profile is forced to `Compatibility` even if another preset was saved. Selecting the Compatibility preset while running a Forward renderer changes the presentation preset immediately, but it does not hot-switch the engine rendering backend. Renderer selection remains a project/startup renderer decision.

## TASK-194 integration

The existing kilometre-scale world streamer retains its normative 2/5/15 km values as the gameplay-independent baseline. TASK-202 passes a presentation-distance multiplier into its plan overload:

- Low: 0.58
- Medium: 1.00
- High: 1.20
- Compatibility: 0.50

The TASK-158 25-chunk / 9-collision micro terrain window is unchanged, so profile changes cannot reduce collision authority.

## TASK-196 integration

Regional vegetation combines two independent controls:

1. profile density, applied deterministically per region/species batch outside the interaction promotion zone;
2. profile distance multiplied by TASK-200 adaptive vegetation distance.

Close interactive flora is never removed by density filtering. Scan, damage, harvest, proximity and quest promotion remain authoritative.

## Clouds, water, post effects and particles

TASK-190 cloud presentation receives a profile cloud cap, secondary-opacity scale, atmosphere quality scale and simplified-shader flag. TASK-188 water receives wave, depth and underwater-distortion scales. Compatibility uses single-sample cloud shading and bypasses expensive scene-depth/refraction work in the simplified water path.

Directional shadow mode/range, Environment glow and particle `amount_ratio` are presentation-only settings. Particle traversal is only repeated when the effective particle scale actually changes.

## TASK-200 adaptive ceiling

The effective vegetation distance is:

`graphics profile distance × TASK-200 adaptive distance`

Cloud count is the minimum of the selected profile cap and the TASK-200 governor cap. TASK-200 Low budget policy is used for Low and Compatibility; Medium budget policy remains the baseline for Medium and High.

## Acceptance boundary

Static/F5 acceptance verifies profile completeness, persistence, renderer override logic, live presentation hooks and TASK-200 ceiling composition. It cannot prove that High looks better on a particular display or that a specific renderer/backend is active after selecting a preset. Owner runtime acceptance must visually compare profiles and check the renderer diagnostics line.
