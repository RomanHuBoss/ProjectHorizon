
## Alpha 0.1.0-alpha.170 — Radial Surface Frame & Cube-Face Traversal Foundation

TASK-170 connects the verified global globe/geodesy layer to live surface gameplay without destabilizing the bounded terrain stack. The active planet now exposes a continuously normalized radial tangent frame (`East/Up/North`), six cube-sphere face addresses, planet-scaled gravity and exact geodesic steps/warp targets. The on-foot controller uses the current planet gravity magnitude while local `+Y` remains the radial-up axis of the moving tangent patch; the existing floating-origin 25/9 terrain/collision/navigation budget and logical-X/Z persistence remain unchanged. Developer `surface_warp <latitudeDeg> <longitudeDeg>` enables deterministic seam/face traversal testing. F5 includes `TASK-170 radial surface frame acceptance`; see `docs/PLANET_RADIAL_SURFACE_FRAME.md`.

This is the safe foundation for later full radial collision/navigation. Alpha.170 does **not** yet claim that CharacterBody/collision terrain is globally curved or that the player physically walks continuously over cube-face seams in one Godot coordinate frame.

## Alpha 0.1.0-alpha.168 — Planetary Globe & Geodesic Surface Topology

TASK-168 promotes the verified cube-sphere prototype into the live Stage-2 world as a bounded detailed current-planet globe in Orbit/Interplanetary contexts. Surface gameplay keeps the stable 25-chunk floating-origin tangent streamer, but now shares a planet-global spherical address model with normalized latitude/longitude, great-circle distance and radius-derived distant-horizon curvature. See `docs/PLANETARY_GLOBE.md`. TASK-170 supplies the radial tangent/gravity/face-address foundation; alpha.174 now bends the bounded collision/navigation patch by the real planet radius while keeping global collision residency streamed rather than permanent.

# Project Horizon

**Project Horizon** — процедурный космический симулятор на Godot Engine с исследованием планет, космическими полётами, добычей ресурсов, крафтом, торговлей, заданиями и строительством баз.

Проект разрабатывается как одиночная игра с возможностью последующего расширения архитектуры для серверных функций и кооперативного режима.

## TASK-182 — Flight Runtime Closure & Streaming Stability

Alpha.182 closes the remaining owner-observed flight-runtime defects after TASK-180.3. The virtual flight stick remains stateful and roll-dominant, but it is now **spring-centered**: after a short 0.08 s idle hold the stored stick command returns smoothly to neutral at 5.5/s, so a small mouse nudge no longer keeps rolling/yawing the ship indefinitely. Middle mouse still recenters immediately; unrestricted pitch/roll and independent A/D lateral thrusters are preserved.

The same iteration adds Schmitt-style atmosphere-presence hysteresis to eliminate the observed `EXIT 590.0 m -> ENTER 589.9 m` boundary chatter. Terrain streaming now coalesces adjacent one-chunk refresh requests while a revision is already in flight, but large observer jumps still replan immediately. PlanetRuntime deactivation no longer swaps the terrain observer and schedules a pointless full 25-create/25-remove window immediately before the streamer is suspended. F5 includes `TASK-182 flight-runtime closure acceptance`; section-37/CI/release enforce `tools/validate-task182-flight-runtime-closure.py`. See `docs/FLIGHT_RUNTIME_CLOSURE.md`.

## TASK-180.3 — Stateful Virtual Flight Stick & Runtime Log Integrity

Alpha.180.3 replaces the previous direct `MouseMotion.Relative -> angular command` path with a stateful virtual flight stick. Physical mouse motion moves a bounded stick position; the command core remains stateful, while TASK-182 now spring-centres live idle input instead of preserving it indefinitely. Horizontal deflection is roll-dominant with only a small coordinated yaw term, vertical deflection is pitch, and middle mouse recenters immediately. The HUD keeps `+` as the ship-nose reticle and shows `○` at the current virtual-stick deflection, so the persistent command is visible. A/D remain independent lateral thrusters. There is no return to the old one-frame FPS mouse-rate path and no Euler attitude clamp; TASK-182 adds only a spring-centred live neutral return, so full loops/rolls remain possible during sustained input.

The same hotfix treats the supplied owner Output as a blocking runtime artifact. The latest run contained repeated Godot light-culler `create_frustum_points` failures after PlanetRuntime release and repeated near-floor recovery chatter. Flight cameras now use a bounded 0.25 m..900 km clip envelope, the 760 km starfield remains inside the far plane, surface weather/shadows are strictly presentation-owner gated, and the 3.2 m terrain floor has a tolerance band plus a larger separation pad. The historical `TerrainChunkManager.ChebyshevDistance` overflow fix remains enforced with saturated Int64 arithmetic. F5 includes `TASK-180.3 flight-control/log-integrity acceptance`; section-37/CI/release enforce `tools/validate-task1803-flight-control-log-integrity.py`. See `docs/FLIGHT_CONTROL_LOG_INTEGRITY.md`.

## TASK-180.2 — Stellar Scale, Planet Crash & Mouse Attitude Hotfix

Alpha.180.2 corrects three owner-reported flight defects without advancing the feature queue. The atmospheric `PlanetSurfaceSunVisual` is now owned strictly by `WorldSceneKind.Surface`; it cannot leak into Orbit/InterplanetaryTransit as a tiny follower sphere. Surface sky keeps a distant follower disc only for atmospheric presentation, while space renders the actual `StarSystemSimulation` star with a strong unshaded emissive profile. F5 checks that the real system star has a substantial angular diameter from the focused planet.

Planet collision is no longer equivalent to invulnerability. Normal piloting mistakes remain inside the surface-safety envelope, but a high-energy descent is deliberately not cancelled by `SurfaceSafetyAcceleration`. `ArcadeShipController` captures the strongest physical `MoveAndSlide` impact from pre-collision velocity; the vertical-slice arbiter kills the ship on lethal terrain-normal impact. Manual free-flight entry has a stricter 55 m/s safe-capture envelope than navigation-assist entry, so a boosted direct run can reach the solid-body collision path instead of being silently converted into a safe atmospheric handoff.

Alpha.180.2 originally introduced direct relative-mouse pitch/yaw plus bank. Owner runtime showed that this still felt like an FPS-rate controller rather than a flight control. Alpha.180.3 supersedes only that mouse-control portion with the stateful roll-dominant virtual-stick controller while retaining alpha.180.2 stellar ownership, crashability and unrestricted 3D attitude fixes. F5 keeps `TASK-180.2 flight feel hotfix acceptance` as a regression and adds TASK-180.3.

## TASK-180.1 — Runtime Integrity Hotfix

Alpha.180.1 is the emergency correction pass driven by the first owner runtime of alpha.180. It fixes three confirmed defect classes rather than adding new content: incomplete orbital planet fill, pass-through orbital-station geometry, and terrain-streaming/runtime instability. Detailed planets now keep an inset opaque core behind the six cube-sphere faces and all planet/moon proxy materials are two-sided. The expanded station presentation now has a compound physical collision envelope (core, arms, spine, dock guides/tunnel, hub, radiators, antennas and 12 habitation-ring segments), plus a continuous high-speed sweep over the same live collision shapes.

`TerrainChunkManager.SetRuntimeObserver()` now resolves the new observer chunk before replanning instead of sorting against the historical `int.MinValue` sentinel; Chebyshev distance is calculated in `Int64` and saturated. Near-surface recovery gains a small separation pad and edge-triggered logging instead of hundreds of duplicate warnings. Outbound surface residency now releases the terrain streamer at the 680 m surface handoff while inbound preload still begins at 900 m, preventing the fast ship from accumulating a large obsolete chunk-removal backlog in vacuum. Orbital/interplanetary directional shadows are disabled while surface shadows remain bounded to 320 m, preventing the large 1,200 km camera frustum from repeatedly entering Godot's shadow light-culler path. F5 includes `TASK-180.1 runtime integrity acceptance`; section-37/CI/release enforce `tools/validate-task1801-runtime-integrity-hotfix.py`.

## TASK-180 — Production Procedural Visual Language

Alpha.180 performs the next isolated art/presentation pass without reopening verified flight/surface mechanics. The player ship now has an 11-part exterior silhouette and a renderable cockpit interior with consoles, emissive instruments and canopy framing; the orbital station has a separate mesh-only habitation/detail layer; NPC ships use a nine-part compound silhouette. Star-system bodies now receive semantic PBR-style material profiles and the focused cube-sphere planet uses six bounded face-material instances with seam-safe procedural vertex-colour breakup plus distinct water/atmosphere/cloud shell roughness.

Gameplay collision is intentionally unchanged: the player ship keeps its single authoritative box collider, station docking/collision uses the existing shapes and the new detail subtrees are visual-only. F5 now includes `TASK-180 production visual language acceptance`; the static contract is enforced by `tools/validate-task180-production-visual-language.py`. Full scope and limits are in `docs/PRODUCTION_VISUAL_LANGUAGE.md`.

This is still a procedural production foundation. The supplied snapshot does not contain author GLTF/texture payloads, baked LODs or authored PBR/decal atlases, so alpha.180 does not claim final hand-authored AAA art.

## TASK-178.7 — Surface Solidity, Monotonic Brake & Smooth Atmosphere Handoff

Alpha.178.7 closes three runtime defects exposed by the first alpha.178.6 owner flight. Near-surface residency is no longer a sphere around the starter landing pad: it is based on actual terrain-relative altitude, the bounded terrain streamer follows the piloted ship, and a 3.2 m terrain-aware **swept** floor checks the whole motion segment before accepting the new ship position. The ship therefore cannot continue below the sampled curved surface even if it crosses the terrain between frames.

Manual arcade braking is now strictly monotonic. `S` and `X` brake the current velocity toward exact zero; no translational thrust or heading-alignment step can turn a held brake into reverse acceleration. The final brake envelope is applied after atmosphere/guidance forces, so those forces cannot push a nearly stopped ship through zero during the same tick. External/autopilot signed thrust remains available internally where an explicit manoeuvre requires it.

The physical atmosphere now uses exactly the same **110..620 m smoothstep envelope** as the visual atmosphere-to-vacuum presentation. Gravity/lift/drag are the complement of the vacuum blend, and the old instantaneous radial climb-speed clamp is replaced by a blend-scaled acceleration limiter. Orbital-to-surface coordinate handoff occurs at 680 m, outside the completed blend, preserves incoming speed and maps it to a coherent radial descent/pitch; near-surface content is preloaded to 900 m. Both ascent and re-entry therefore cross one continuous dynamics/lighting envelope rather than a hidden physics switch.

### Acceptance TASK-178.7

Run `tools\run-section37-quality.cmd` and then verify in Godot: held `S/X` stops at zero without reversing; terrain remains solid after flying hundreds of metres away from the starter pad; ascent/re-entry has no abrupt dynamics/lighting step; F5 prints `TASK-178.7 surface solidity/braking/handoff acceptance PASS`.

## TASK-178.6 — Orbital Scale, Mouse Flight & Multi-Planet Surface Activation

Alpha.178.6 addresses three defects exposed by the first alpha.178.5 flight. The orbital scene is widened by another order of magnitude: planet centres use ~100 km-class compressed spacing, moons keep tens of kilometres of clear space beyond the parent visual surface, and landable planet radii are derived from the catalogued 20–80 km bodies at a much larger compressed display scale. The focused planet is no longer moved farther away when its radius grows; it is kept at a fixed ~9 km surface clearance, so its angular size genuinely reads as a planet. Flight cameras are now bounded to 900 km by TASK-180.3 while retaining the complete starter-system presentation envelope.

TASK-178.6 established `_Input` ownership so HUD controls cannot steal ship mouse motion. Its original impulse+decay steering has since been superseded by TASK-180.3: the same early input path now moves a persistent virtual flight stick and prints `TASK-180.3 ship virtual flight stick INPUT PASS`. `G` still toggles heading-coupled arcade flight vs explicit inertial drift.

Planet approach is also generalized. Every landable planet has a physical entry shell. Entering a different planet at normal ship speed performs the required `Orbit -> InterplanetaryTransit -> Orbit` transaction, changes planet identity only inside that transaction, synchronously builds the destination terrain/ecology/POI/resource state, and then enters the verified 220 m curved-surface approach. Moons and stars remain non-landable solid bodies. Normal orbital entry is allowed up to 110 m/s; this prevents the old situation where a normal 85 m/s ship hit the proxy before surface flora/fauna/resources could ever appear.

Because the enlarged system would otherwise turn the new 100 km-class spacing into an hour-long flight, `K` interplanetary cruise now has a separate scale-aware speed envelope. Far from the destination it may raise the external ship speed limit up to **600 m/s**; target speed is reduced continuously from remaining stopping distance using the ship's 38 m/s² braking capability, and the ordinary <=11 m/s arrival gate is preserved. Manual flight keeps its normal ship limits—high-speed cruise is navigation-assist authority, not a permanent physics cheat.

### Acceptance TASK-178.6

1. Run `tools\run-section37-quality.cmd`: expected clean build `0 errors / 0 warnings`, tests green and `TASK-178.6 ORBITAL SCALE/MOUSE/MULTI-PLANET CONTRACT PASS`.
2. In manual flight, move the mouse immediately after `T`/undock. The first motion must log `TASK-180.3 ship virtual flight stick INPUT PASS`; the stick must respond roll-dominantly/pitch vertically and, under TASK-182, return smoothly toward neutral after physical mouse motion stops.
3. Compare Orbit visually: the focused planet should dominate the view and neighbouring planet centres should no longer read as objects in the same small local cluster. Moons must remain well outside the parent surface. Select another planet and use `K`: long-range cruise should accelerate toward the ~600 m/s envelope and then decelerate automatically instead of crawling for tens of minutes.
4. Approach a **landable planet**, not a moon, at <=110 m/s. For the current planet expect `TASK-178.5 free-flight planetary entry PASS`; for another landable planet expect `TASK-178.6 manual planet transfer PASS ... world=Orbit->InterplanetaryTransit->Orbit ... flora=...; fauna=...; pois=...; resources=...; surfaceHandoff=1`.
5. After the handoff, the destination surface must visibly contain its own terrain, flora/fauna, POIs and mineable resource nodes. F5 must report `TASK-178.6 orbital scale/mouse/multi-planet acceptance PASS` with `playableCruise=1`, `landableContent=1` and `planets=N/N`.
6. A deliberate collision with a moon/star still must be blocked by TASK-178.5 swept collision; those bodies are not expected to spawn landable ecology.

## TASK-178.5 — Arcade Flight Kinematics & Continuous Orbital Collision

Alpha.178.5 closes two gameplay gaps exposed by the first real return flight from the station. The default `ArcadeShipController` now behaves as an arcade spacecraft rather than as an unintentionally Newtonian rigid body: with flight assist enabled, turning the ship continuously bends the velocity vector toward the ship's local forward/translation axes while preserving speed. `G` deliberately disables that coupling and exposes inertial-drift mode; this is an explicit opt-out rather than the default.

Orbital stars, planets and moons are no longer visual-only spheres. `StarSystemSimulationNode` exposes the live display centre/radius of each solid body and performs continuous swept-sphere intersection against the ship trajectory, so even a frame that jumps from one side of a planet to the other is caught. A physical impact clamps the ship to the boundary, zeroes velocity and opens the localized death screen instead of allowing tunnelling.

The current landable planet additionally exposes a physical outer entry shell at the existing TASK-178.4 clearance. TASK-178.6 permits normal manual flight to cross that shell at `<=110 m/s` and transition into the verified 220 m curved-surface approach without requiring `K` or `Enter`; navigation assist and manual capture continue to use the same contract.

### Acceptance TASK-178.5

1. `tools\run-section37-quality.cmd`: clean build `0 errors / 0 warnings`, tests green, `TASK-178.5 ... CONTRACT PASS`.
2. Undock with `T`, leave `G` enabled and accelerate to a visible velocity. Rotate the nose by roughly 90–180 degrees without pressing `K`: the actual trajectory must curve toward the new heading instead of continuing indefinitely along the old world-space vector.
3. Toggle `G` once: Output must show `mode=inertial-drift`; now rotation is allowed to leave the velocity vector uncoupled. Toggle `G` again before the collision/landing tests.
4. Approach the current landable planet manually. At a safe entry speed (TASK-178.6 raises the normal limit to `<=110 m/s`), crossing the outer entry shell must log `TASK-178.5 free-flight planetary entry PASS` and hand off to the surface approach even with navigation assist off.
5. Repeat at unsafe/high speed (or aim at another planet/moon): the ship must not pass through the sphere. Expected Output is `TASK-178.5 orbital body collision PASS ... swept=1; blocked=1; death=1` and the death overlay.
6. F5 must contain `TASK-178.5 spaceflight kinematics/collision acceptance PASS` with `headingCoupling=1; sweptPlanetCollision=1; highSpeedTunnelingBlocked=1; liveAssist=1; currentPlanetSphere=1; liveSweep=1`.

## TASK-178.4 — Planetary Approach, Landing & Orbital Lighting Recovery

Alpha.178.4 closes the gap between the large-scale orbital representation and the already verified curved planetary surface. Planets are no longer small decorative spheres: orbital planet size is derived from the real `PlanetEnvironment` radius and rendered at a deliberately compressed kilometre-class flight scale, with moon orbits pushed safely beyond the parent body's visible surface. The current detailed globe remains bounded/non-colliding, but is large enough to read as a destination rather than as an object comparable to the ship. Ship cameras retain the expanded system out to 60 km.

Planet landing is now a two-stage physical gameplay route. From Orbit/InboundFlight, `K` guides to a **near-side planetary entry envelope** outside the visible globe (or `Enter` commits it manually when range/speed are safe). Crossing that envelope hands the ship to the verified curved-surface runtime at 220 m altitude; navigation assist can then continue to the landing pad and complete the normal `TryLand` transaction. Interplanetary travel uses the same near-side approach contract instead of aiming at a planet centre.

The alpha also fixes the docked-save crash path: persistence load uses coordinator `Restore(...)`, so an authoritative `OrbitalStation` save can restore directly into `StationInterior` while the normal gameplay graph still rejects illegal `Surface -> StationInterior` transitions. Space lighting now captures the actual weather-driven source frame before blending to vacuum, shades atmosphere/cloud shells, and smoothly aligns the orbital key light with star→planet direction. F5 includes `TASK-178.4 planetary landing/lighting acceptance`.

### Acceptance TASK-178.4

1. `tools\run-section37-quality.cmd`: clean build, all tests and `TASK-178.4 ... CONTRACT PASS`.
2. Load the previously docked save: no `TASK-148 ... Surface->StationInterior ... not allowed`; expected `TASK-178.4 world scene persistence restore PASS ... to=StationInterior`.
3. Undock with `T`, select/return toward a landable planet and use `K`: the ship must approach the visible globe rather than its centre, enter the planetary envelope, log `TASK-178.4 planetary atmosphere entry PASS`, then continue into the 220 m surface-approach layer and land. Manual `Enter` can commit each safe capture stage instead.
4. In Orbit, the focused planet must be visually dominant over the ship and its moons must remain clearly outside the parent surface. Light/shadow must form a stable star-relative terminator without a hard colour/brightness step during upper-atmosphere handoff.
5. F5: `TASK-178.4 planetary landing/lighting acceptance PASS` with `restoreSafe=1; planetScale=1; moonClearance=1; orbitalEntry=1; surfaceHandoff=1; voyagePath=1; lightingContinuity=1; liveGlobe=1; entryOutsideGlobe=1; landable=1`. Existing TASK-178.3/178.2/178/176/126 remain green.

## TASK-178.3 — Orbital Handoff Scale & Visibility Recovery

Alpha.178.3 fixes the remaining takeoff handoff defect exposed by the external alpha.178.2 run. The Stage-1 station is no longer parked roughly one hundred metres from the launch pad: its docking target is about **1.59 km** from launch, so even at the starter ship's ~85 m/s top speed the approach is a real flight rather than a near-surface hop. The station and docking beacon are hidden in the lower atmosphere and become available only after 220 m altitude.

The former visual cutoff was also wrong: ship atmosphere physics ended around 85 m and the shared `WorldEnvironment` immediately jumped from the blue weather sky to an almost-black vacuum color. Visual presentation is now independent of that physics threshold. Surface detail remains resident to 260 m, while the sky/lighting/fog cross-fades from upper atmosphere to vacuum over **110..620 m**. A deterministic emissive 420-star backdrop appears during the climb, and vacuum ambient/directional lighting is strong enough to keep station, ship and planetary geometry readable. F5 now includes `TASK-178.3 orbital handoff recovery acceptance`.

## TASK-178.2 — Orbital Navigation & Presentation Repair

Alpha.178.2 fixes the first real orbital-flight defects exposed after manual control was restored. `K` is now a complete navigation assist: it approaches the Stage-1 station/landing target, sheds excess speed, creeps into the capture envelope and automatically executes docking/landing; `Enter` remains the manual transaction key. Successful station docking switches the world coordinator into a lit hangar shell and opens station services.

The star-system view is rescaled as a deliberate compressed astronomical presentation instead of the former metre-scale toy model. The simulation clock is 1x rather than 120x, moons have much larger orbit radii and multi-minute periods, planets are separated by kilometre-scale gameplay distances, and star/planet/moon visual radii maintain an explicit hierarchy. Both ship cameras now retain a 60 km far plane so the expanded system presentation remains visible. The focused planet is placed behind/below the local flight scene as a large orbital backdrop. Statistical station/traffic proxies are hidden while the physical station and NPC ships are resident, preventing duplicate local objects. Orbit, interplanetary, station-interior and hyperspace contexts also get explicit dark fog-free environment profiles so the planetary blue atmosphere cannot leak into space. F5 now includes `TASK-178.2 orbital navigation/presentation acceptance`.

## TASK-178.1 — Pilot Input Ownership Hotfix

Alpha.178.1 fixes the player-facing control regression discovered after TASK-178. Boarding a repaired ship on the planet previously set a neutral **external** command while leaving ship physics and atmospheric safety active. External control overrides `ReadManualCommand()`, so mouse/WASD were ignored while atmosphere recovery/minimum-speed logic could still move the ship. Parked/docked states now use an explicit physics-off control lock. Press `T` to launch/undock; after the transition the player owns mouse/keyboard control immediately. `K` toggles navigation assist explicitly. TASK-178 F5 acceptance now reports and requires `pilotControl=1`.

## TASK-178 — Spaceflight & Navigation Subsystem Closure

Alpha.178 moves the next mega-iteration outside the now-verified planetary-surface stack. It closes the already implemented ship/readiness, Stage-1 voyage, galaxy/hyperspace, star-system simulation, interplanetary transfer and world-scene coordinator mechanics as **one spaceflight/navigation subsystem**. F5 aggregates six normative reports (TASK-110/112/114/128/148/152), then requires cross-contract readiness, fuel, transition, persistence, navigation-identity and bounded-residency chains plus eight live Godot coherence invariants, including explicit pilot-control ownership.

The iteration also fixes a real boundary defect: a same-system planet selection could remain cached in `InterplanetaryTravelRuntime` after `GalaxyNavigation` successfully mutated to another star system. The successful hyperspace transaction now synchronizes the interplanetary state immediately, and `IsSelectionConsistentWith()` rejects any source/target that does not belong to the current system. `TASK-114 player hyperspace jump PASS` reports `planetTargetCleared` and `interplanetarySync`; TASK-178 exposes the full closure on the existing F5 acceptance. See `docs/SPACEFLIGHT_NAVIGATION_SUBSYSTEM.md`.

The previous alpha.176.1 runtime evidence is now accepted: TASK-126 passed with `activeFlying=3`, `altitude=1`, `altitudeProbe=1`, `altitudeRange=2.33..5.44m`, `altitudeViolations=none`; TASK-176 remained `contracts=11/11`. Therefore the surface stack is not reopened by alpha.178.

## TASK-176.1 — Flying Fauna Terrain-Altitude Runtime Hotfix

External Godot 4.7.1 evidence verified the complete TASK-176 planetary-surface subsystem (`contracts=11/11`, live streamer/navigation/player/presentation/content/weather/radial stack all PASS), but exposed a remaining TASK-126 runtime defect with all four flying fauna alive: `altitudeProbe=1` while the live `altitude` invariant was `0`. The controller itself was correct; after it produced vertical correction, the old whole-vector maximum-speed normalization scaled that vertical authority down together with obstacle/separation/POI steering. On steep streamed terrain this could leave live fauna outside the required terrain-relative altitude corridor.

Alpha.176.1 limits tangent and vertical speeds independently, preserves up to 3 m/s of altitude authority, and applies a hard `+1.6..+7.2 m` terrain-relative safety envelope after `MoveAndSlide`, while distant fauna are on the zero-Hz AI tier, and after radial/curved frame transitions. TASK-126 Output additionally reports `altitudeRange` and exact `altitudeViolations`, so any remaining failure is directly diagnosable rather than a single `altitude=0` bit. No save/content schema or user controls change.

## TASK-176 — Planetary Surface Subsystem Closure

Alpha.176 turns the already implemented TASK-150…174 planet stack into one integration boundary instead of treating environment, interplanetary handoff, surface content, terrain, streaming, presentation, weather, floating origin, radial/physical frames and curved collision as unrelated green checks. F5 now runs a model-level aggregate over **11 existing normative acceptance runners**, then verifies eight live Godot invariants: settled 25/9 curved residency, curved TASK-124 navigation, arbitrary-up player alignment, radial atmosphere/world presentation, active ecology/POIs, cold-start guard, weather runtime and radial/physical alignment. PASS additionally requires end-to-end persistence, traversal, bounded-residency and cross-planet-identity chains. See `docs/PLANETARY_SURFACE_SUBSYSTEM.md`.

## TASK-174.2 — Aerial Altitude Lifecycle Acceptance Hotfix

The TASK-126 altitude acceptance now distinguishes live flying participants from killed/hidden fauna. Dead fauna retain their frozen transform for persistence/interaction history but no longer poison the live altitude envelope. F5 still cannot pass vacuously: an independent `ApplyAltitudeEnvelope` probe must produce a vertical correction and increment the shared altitude-controller counter. TASK-126 output now reports `activeFlying=<n>` and `altitudeProbe=<0/1>`, so the regression can be reproduced by killing flying fauna before F5.

## TASK-174.1 — Curved Surface Cold-Start Safety

Alpha.174.1 closes the first-run regression where the authored player spawn could be below the newly curved terrain before the async streamer settled. The synchronous curved fallback collider is now backface-safe, the player is lifted to at least 1.02 m body-center clearance over semantic terrain during terrain bootstrap, and the same idempotent guard remains active through the short fallback→25/9 streamer handoff. F5 exposes `TASK-174.1 curved surface cold-start safety acceptance`.

## TASK-174 — True Curved Cube-Sphere Collision & Face-Aware Navigation Tiles

Alpha.174 bends the live bounded terrain **visual and trimesh collision** by the real current-planet radius instead of keeping TASK-172's collision patch mathematically flat. TASK-124 navigation tiles consume the same spherical-sag model, player Up follows the curved local normal, and floating-origin/frame handoffs preserve semantic surface height for the player and resident AI/content. The established 25-active/9-collision budget and logical-X/Z save identity remain unchanged.

The same revision fixes the radial-atmosphere artifact exposed by alpha.172.1: the procedural sky hemisphere is aligned to the active radial frame, global-Y height fog is disabled on the surface, horizon gradients are softened, and atmosphere-bearing planets retain a dim blue night dome instead of a near-black half-sky. F5 includes `TASK-174 curved cube-sphere surface acceptance`; see `docs/CURVED_PLANET_SURFACE.md`.

## TASK-172.1 — Radial physics/navigation hotfix

External Godot 4.7.1 testing of alpha.172 exposed three regressions: NavigationServer3D rejected regions rotated >=90° away from the navigation-map UP orientation, the arbitrary-up player visibly rolled while strafing A/D, and two acceptance thresholds produced false negatives (`maxPointErr=7.814 mm`, displayed clearance `0.80 m`). Alpha.172.1 fixes those defects; alpha.174 starts only after external reacceptance: TASK-124 now owns a dedicated navigation map aligned to the current radial Up; navigation regions, avoidance obstacles and NPC navigation agents are detached/rebound around a tangent-frame rotation instead of remaining on Godot's default global-UP map. Ground-NPC avoidance uses Godot's 3D/radius avoidance path (then projects the safe velocity back to the tangent plane), because the 2D avoidance path is global-XZ based. The player body is reconstructed from tangent-forward + radial-Up with zero roll, and the two acceptance tolerances now reflect the actual float/numeric budgets.

## TASK-172 — Physical Radial Surface Frame & Navigation Migration

Alpha.172 turns the TASK-170 spherical/radial mathematics into a live **rotating Godot tangent physics frame**. `Gameplay`, fallback ground and the bounded terrain streamer are oriented to the current planet `East/Up/North` basis; the player uses arbitrary-up CharacterBody gravity/floor/jump/jetpack/swim motion, while logical East/North coordinates and save identity stay unchanged. TASK-124 navigation regions remain bounded children of the rotating surface frame, recovery/path probes are frame-aware, and absolute runtime targets/velocities for ground NPCs, flying fauna and NPC ships are remapped on face/frame handoff.

This is deliberately the safe migration step before a globally curved collision mesh: the live physics surface is still a 25-active/9-collision tangent patch, but it can rotate to the correct radial Up at any cube-face address without breaking terrain identity, navigation residency or persistence. Developer `surface_warp <lat> <lon>` now doubles as a physical seam test; F5 includes `TASK-172 physical radial surface acceptance`. See `docs/PHYSICAL_RADIAL_SURFACE.md`.

## TASK-166 — Dynamic Planetary Weather & Diurnal Cycle

Alpha.166 closes the next cross-system surface subsystem: deterministic local time and climate-aware planetary weather. A full local day advances in 600 active-surface seconds; each planet has a seed-derived solar phase and deterministic two-hour weather cells (`Clear/Wind/Storm/Toxic`). The live state now drives sun direction/energy, day/night/sunset sky colors, atmospheric fog/visibility, cloud opacity/drift, weather audio and bounded rain/snow/toxic precipitation visuals. Storm/toxic states feed the existing suit/life-support hazard model, while flying fauna receive bounded wind drift and adverse-weather activity reduction without changing TASK-126's terrain-relative altitude envelope.

Weather persistence stores only elapsed game-hours as the `planet_weather` setting; weather identity regenerates from planet seed + time and old saves remain valid. Existing developer commands `set_time <0..24>` and `set_weather <clear|wind|storm|toxic>` now control the real weather runtime rather than directly tweaking one light. F5 includes `TASK-166 planetary weather acceptance`; see `docs/PLANETARY_WEATHER.md`.

## TASK-164 — Planet Surface Visual Language & Procedural Props

Alpha.164 starts the next surface mega-iteration after the presentation stack became operational. The goal is not an AAA authored-asset replacement yet; it is a coherent **procedural visual-language layer** that removes the most obvious single-primitive placeholders while preserving the bounded gameplay/runtime contracts. Streamed resources now resolve into deterministic ore/crystal/fiber/organic compound silhouettes, POIs receive category-specific secondary geometry, fauna receive body-plan details (wings/fins/legs/tails), pad/fungus flora silhouettes are improved, and terrain receives low-cost logical-coordinate/height/slope color breakup across both streamed and distant terrain.

The same revision also fixes the two external F5 regressions exposed after alpha.162.2: planet-scoped POI placement gets a broader deterministic candidate search without changing the historical legacy golden fixture, and flying fauna now maintain altitude relative to the actual terrain underneath them. F5 includes `TASK-164 surface visual language acceptance`; external alpha.168 evidence confirms TASK-154, TASK-164 and TASK-126 (`altitude=1`) PASS, so this layer is now recorded as VERIFIED. Gameplay collision, resource IDs/depletion persistence, POI identities and the 25/9 terrain streaming budget remain unchanged.

## TASK-162.2 — Surface Presentation Recovery

External Godot 4.7.1 evidence after TASK-162.1 proved that the surface stack now boots and that TASK-156/158/160/162 F5 contracts pass, but the live screenshot exposed a presentation gap the structural checks did not measure: the 5x5 gameplay streamer ended only ~80 m from the player, temperate relief was just 2.55 m, the system star had no guaranteed visible disc, and low thick cloud lobes read as nearby blobs. Alpha.162.2 keeps the **25-chunk / 9-collision gameplay streamer** unchanged and adds a separate visual-only 840 m distant-terrain proxy with a 116 m center hole, stronger low-frequency planet relief, denser aerial perspective, a camera-frame stellar core+halo aligned with the system-star direction, higher/flatter cloud clusters, and a safe player-to-terrain clearance guard for load/reset.

F5 now includes `TASK-162.2 surface presentation acceptance`, which refuses PASS unless macro relief, the distant proxy, explicit stellar-disc geometry, atmosphere density and player clearance are all present. The startup `TASK-160 ... READY` line also reports `sunVisual` and `distantTerrain`, so `sun=1` can no longer be mistaken for proof of a visible sun. TASK-162.2 is a presentation hotfix; the bounded gameplay/collision/nav budget and TASK-162 logical-coordinate/persistence contracts are intentionally unchanged. The live surface streamer also disables per-job/per-chunk verbose logging by default, leaving plan/completed summaries and errors in Output while keeping the standalone terrain prototypes verbose.

## TASK-162 — Planet-Global Surface Frame & Floating Origin

The live planet surface now has a dedicated two-space coordinate model: deterministic double-precision planet-logical East/North coordinates drive terrain chunks, resources, POIs, ecology placements, base placement, maps and persistence, while Godot player/physics/rendering coordinates are automatically rebased in 4096 m cells once local X/Z exceeds 2048 m. `TerrainChunkManager` keeps its 25-chunk bounded residency but selects/samples chunks in logical space and places chunk nodes relative to the current frame origin. Ground-NPC navigation targets, NPC-ship routes, flying-fauna territory/aerial steering caches, Stage-1 voyage targets/state, procedural world composition and cold restore use the same frame contract. F5 includes `TASK-162 planet-global surface frame acceptance`; see `docs/PLANET_GLOBAL_SURFACE_FRAME.md`.

TASK-162 closes the **floating-origin / planet-global coordinate subsystem**, not the physical curved-surface renderer: terrain remains a tangent heightfield and does not yet claim cube-face transitions, radial gravity or a true cube-sphere collision mesh.

## TASK-160 — Planet Surface World Composition & Persistence

The live TASK-158 terrain is now composed as a planet rather than a technical test field. Surface presentation uses the active planet atmosphere plus the current system star to build a procedural sky, visible sun, sky ambient/reflections, aerial haze and lightweight deterministic clouds. The legacy 58-node resource showcase is hidden from normal gameplay (the three starter salvage nodes remain), while the active 5x5 terrain window receives deterministic chunk-scoped deposits with stable planet+chunk+slot IDs and delta-only depletion persistence. Existing POIs keep their reviewed stable identities but are spread through a 78–420 m live exploration annulus instead of being piled around the landing pad. F5 includes `TASK-160 planet surface world composition acceptance`; see `docs/PLANET_SURFACE_WORLD_COMPOSITION.md`.

## TASK-158.1 — Runtime acceptance hotfix

External Godot 4.7.1 evidence confirms TASK-158 live streaming (`25/25` resident chunks, `9/9` collision chunks and F5 PASS), while TASK-138 exposed a stale POI golden fixture left behind by the earlier TASK-156 terrain projection change. Alpha.158.1 versions that deterministic POI change correctly (`ProjectHorizonGenerator.Version = 3`), refreshes the reviewed 20-POI golden checksum and removes the two nullable warnings reported by the Windows build. No TASK-158 streaming behavior or save schema is changed.

## TASK-158 — Planetary Surface Streaming & Traversal Foundation

The live landable-planet surface now uses the verified terrain chunk pipeline as a moving bounded gameplay window instead of ending at the TASK-156 80x80 m mesh. A 5x5 / 25-chunk residency follows the player (9 high-detail collision chunks plus 16 low-detail visual chunks), samples the current planet's deterministic morphology in background workers, retains LOD stitching/cancellation/safe unload, extends TASK-124 terrain-aware navigation with traversal, and retires the old local mesh only after the first streaming plan settles. F5 includes `TASK-158 planet surface streaming acceptance`; see `docs/PLANET_SURFACE_STREAMING.md`.

## Технологический стек

- **Godot Engine 4.7.1 .NET**
- **C#**
- **.NET SDK**
- **JetBrains Rider**
- **Git**
- **Git LFS**
- **SQLite** через `Microsoft.Data.Sqlite` — для локальных сохранений
- **JSON** — для статических игровых данных
- **Godot Shader Language** — для шейдеров
- **ASP.NET Core** — для будущей серверной платформы

## Целевые платформы

- Windows 10 x64
- Windows 11 x64
- Linux x86_64

Основной рендерер — **Godot Mobile Renderer на Vulkan**.
Резервный профиль — **Compatibility Renderer на OpenGL 3.3**.

## Текущее состояние

Stage 1 remains intact and the Stage 2 planet stack now runs through environment → interplanetary travel → planet-scoped content → planet-specific relief → bounded live surface streaming → **surface-world composition and delta-persistent procedural resources**. The product owner reported the TASK-156 revision as working and requested the next mega-iteration; TASK-156/TASK-157 are therefore recorded as accepted by owner waiver without reconstructing missing numeric build metrics. `TASK-158` promotes the already verified Prototype-B async terrain-chunk architecture into `SalvageRepairSlice`: 25-chunk bounded residency, 33/17 LODs, 3x3 collision, exact TASK-156 terrain sampling, safe fallback handoff, moving terrain-aware NPC navigation and planet-radius surface addressing. `TASK-159` remains the Windows/Godot runtime/manual acceptance tail. TASK-162 now adds a planet-global logical coordinate frame plus live floating-origin rebasing on top of the tangent heightfield; external F5 evidence confirms its deterministic acceptance path. TASK-162.2 repairs the surface presentation exposed by the subsequent screenshot without widening the gameplay streamer. TASK-168 global sphere/geodesy and TASK-170 radial-frame mathematics are externally verified. TASK-172 now rotates the live bounded collision/navigation tangent patch and player physics into the planet radial basis while preserving 25/9 residency. Globally curved cube-sphere collision remains a later layer; live >2048 m traversal and distant cold-restore are still the TASK-163 manual acceptance tail.

### TASK-156 Planet-Specific Terrain & Surface Geometry mega-iteration — `VERIFIED` (product-owner acceptance)

- deterministic terrain profile строится из `PlanetId/archetype/seed`; temperate/desert/frozen/volcanic имеют разные morphology signatures;
- bounded `80 x 80 m` active surface заменяет плоский ground на `65 x 65` mesh (`4,225` vertices / `8,192` triangles) и matching trimesh collision;
- центральная tutorial terrace сохраняет starter gameplay, wet-world basins согласованы с водой, dry-world aquatic suppression TASK-154 сохраняется;
- flora/fauna, POI, resources и base construction получают реальную surface Y; POI constraints используют физический slope; legacy IDs/XZ не меняются;
- NPC NavigationServer regions становятся heightfield/slope-aware, ground agents больше не принудительно возвращаются на плоский Y;
- `F5` включает `TASK-156 planet terrain acceptance`; section-37 quality включает новый static gate и 3 xUnit regressions.

### TASK-154 Planet-Scoped Surface Content mega-iteration — `IMPLEMENTED`, runtime acceptance — `VERIFIED`

- четыре стартовые планеты (`temperate/desert/frozen/volcanic`) получают собственные deterministic surface profiles и region identity;
- ecology density/species выбираются из активных биомов текущей планеты; на сухих планетах aquatic fauna/habitat выключаются;
- planetary POI остаются теми же 20 типами, но их размещение и biome/danger/water samples вычисляются из реального planet environment;
- interplanetary arrival выполняет `capture old planet → commit transfer → activate destination`; hyperspace сохраняет предыдущую surface-state и активирует новый landable body;
- ecology/POI deltas архивируются по `PlanetId` внутри существующих JSON save settings без повышения SQLite schema; legacy `planet.vertical_slice` сохраняет исторические seed/region/instance IDs;
- поверхность визуально переключает ground/atmosphere/water profile; water pool и aquatic habitat отсутствуют на dry-world profile;
- `F5` включает `TASK-154 multi-planet surface content acceptance`, а section-37 quality — `validate-task154-multi-planet-surface-content.py`; добавлены три xUnit regression tests.

### TASK-152 Interplanetary Travel mega-iteration — `IMPLEMENTED`, runtime acceptance — `IN_PROGRESS`

Редакция 2.0 технического задания расширяет промышленную подсистему Project Horizon до полноценного data-driven каталога:

```text
schemaVersion=2
items=174
worldResources=42
recipes=128
stations=15
technologies=32
runtimeEnabledRecipes=16
chemistryRecipes=30
compotiumRecipes=13
paraffiniumRecipes=5
dependencyCycles=0
unreachableRecipes=0
```

Нормативные документы находятся в:

```text
Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.docx
Technical_Specification/2.0/Project_Horizon_Technical_Specification_v2.0.pdf
Technical_Specification/2.0/Project_Horizon_Recipe_Catalog_v2.0.csv
Technical_Specification/2.0/Project_Horizon_Industry_Content_Schema_v2.0.json
```

В переданном GitHub snapshot этой редакции PDF/DOCX ТЗ 1.0 и 2.0 представлены Git LFS pointer-файлами, а не бинарными payload. Поэтому локальная preparation/валидация не выдаёт pointer за прочитанный PDF; для полного просмотра ТЗ требуется материализованный LFS payload из репозитория/артефакта. JSON/CSV v2.0 в snapshot присутствуют обычными файлами.

Каталог статических данных:

```text
src/Game.Client/Content/items.json
src/Game.Client/Content/resources.json
src/Game.Client/Content/recipes.json
src/Game.Client/Content/stations.json
src/Game.Client/Content/technologies.json
src/Game.Client/Content/station_services.json
src/Game.Client/Content/base_construction.json
src/Game.Client/Content/planetary_pois.json
src/Game.Client/Content/procedural_quests.json
src/Game.Client/Content/player_survival.json
src/Game.Client/Content/npc_factions.json
src/Game.Client/Content/ships.json
src/Game.Client/Content/planet_environments.json
src/Game.Client/Content/localization.ru.json
src/Game.Client/Content/localization.en.json
src/Game.Client/Content/catalog_manifest.json
```

Редакция содержит шестнадцать runtime-enabled recipes: стартовый ремонт, девять корабельных компонентов PortableFabricator и связную шестирецептурную линию Refining/Chemistry. В сцене работают пять физических типов станций: PortableFabricator, Smelter, Refinery, DistillationColumn и ChemicalProcessor. Каждая станция получает свой список рецептов из JSON, собственную очередь, слоты и энергетический бюджет, но все станции синхронизированы с единым player inventory. Требования `RequiredTechnology` исполняются доменной моделью, исследовательские очки, разблокировки и сеть незавершённых production jobs сохраняются в SQLite. Queue-вкладка показывает progress bar, elapsed/duration, slot status, energy и точные reservations; поддерживает pause/resume и cancellation с полным возвратом inputs, catalysts и energy. Refining/Chemistry recipes являются повторяемыми, их продукты можно использовать как inputs следующих станций. Энергия каждой station автоматически восстанавливается от нуля до capacity за 60 секунд игрового времени. Основной HUD строится непосредственно из `ProductionNetworkRuntime`: агрегирует jobs, состояния и энергию всех пяти станций, показывает постанционную строку `[R/Q/P]` и не считает исправно инициализированную idle network недоступной. Resource layer vertical slice теперь физически покрывает все 42 world-resource definitions: 32 ранее созданных узла сохранены, а для 26 отсутствовавших типов создаётся детерминированное data-driven поле. Всего в сцене доступно 58 узлов; сбор, duplicate protection, расход, зеркала inventory производственной сети, depletion, autosave/cold restore и `F8` reset используют единый generic lifecycle.

Станционные услуги Этапа 1 реализованы отдельным data-driven слоем `station_services.json`. В vertical slice размещён один trader NPC `npc.trader.ilia_voss` с template dialogue и вкладками Dialogue/Buy/Sell/Quests. Каталог задаёт ровно шесть economy types, три factions с relations и три persistent quest graphs. Все 174 items доступны рынку; цена вычисляется из base price, economy, supply/demand, faction, reputation и deterministic daily factor. Credits, reputation, market stock/day и quest state сохраняются в optional SQLite setting `station_services` без повышения schema 2; старые saves используют legacy fallback. Trade синхронизирует основной inventory и все пять production mirrors.

Строительство баз реализовано как отдельная data-driven подсистема `base_construction.json`: 50 модулей покрывают все 16 категорий раздела 20.1 ТЗ — foundations, floors, walls, roofs, corridors, doors, windows, stairs, rooms, generators, batteries, processors, storage, landing pad, terminals и decoration. Дополнительная техническая категория `Structure` содержит несущие балки, арки и колонны, поэтому всего catalog содержит 17 категорий. Модули ставятся на сетку `2,5 м` с cardinal snap, collision rejection, обязательным anchor и проверкой связности при демонтаже. Исполняются ограничения `500/100/200/20`, электрическая сеть представлена графом, учитывает generators, consumers, batteries и enable/disable. Состояние modules, stock, rotation, device state и battery energy сохраняется в optional SQLite setting `base_construction` без повышения schema 2; legacy saves получают пустую базу и полный starter palette. Режим открывается клавишей `G`, а `F6` запускает изолированную `TASK-106` acceptance совместно с legacy coolant regression. Координаты `Player.GlobalPosition` постоянно отображаются в углу HUD во всех режимах `H`.

Корабельные системы vertical slice вынесены в строгий каталог `ships.json`. Он содержит все шесть классов из ТЗ v2.0 §14.2, все одиннадцать class parameters, семь отдельно повреждаемых систем из §14.3 и ровно 18 module definitions, совпадающих с outputs категории `ShipModule` Industry Content v2. Исполняемый starter ship использует универсальный класс; `U` на поверхности открывает loadout manager с вкладками Overview/Modules/Systems. `ShipSystemsRuntime.Commissioned` жёстко синхронизирован с сюжетным `StarterRepairSession.ShipRepaired`: до завершения стартового ремонта семь систем offline, flight/hyperspace readiness равны false, а install/uninstall/damage/repair/refuel запрещены самим domain runtime. Успешный starter repair выполняет единственный commissioning transition, переводит семь систем в исправное состояние и только после этого разрешает эксплуатацию корабля. Установка и снятие модулей расходуют и возвращают предметы через существующий shared inventory API, соблюдают Weapon/Technology slots и изменяют derived stats. Повреждение системы отключает зависящие от неё модули и влияет на flight/hyperspace readiness; ремонт требует catalog-defined ship component, а refuel — `chemical.high_energy_fuel`. Class, commissioned flag, fuel, installations и system health сохраняются в optional SQLite setting `ship_systems` без повышения schema 2; значение fuel одновременно синхронизируется с legacy `ships` row.

`TASK-112` интегрирует эту доменную модель с реальным `ArcadeShipController` и закрывает сквозной критерий Этапа 1: ремонт корабля → посадка в кабину → взлёт → перелёт к физической орбитальной станции → стыковка и открытие уже существующих station services → отстыковка → возврат → посадка → высадка. Ускорение, максимальная скорость и манёвренность контроллера вычисляются из `ShipSystemsRuntime.GetEffectiveStats()`, а взлёт, стыковка, посадка и расход топлива блокируются состоянием commissioning, readiness и соответствующих систем. Voyage location, pilot state, точная поза/скорость, checkpoints, station visit и completed-loop counter сохраняются в optional SQLite setting `stage_one_voyage` без повышения schema 2. `F5` запускает `TASK-076`, `TASK-110` и изолированную `TASK-112` acceptance.

`TASK-114` добавляет следующий целостный subsystem block: procedural galaxy, обязательные system/galaxy maps, route planning и hyperspace. `GalaxyNavigationRuntime` генерирует systems только по запросу из immutable universe seed, `GalaxyId`, integer sector coordinates и double system positions; whole galaxy никогда не помещается в один `Vector3` и не создаётся целиком в памяти. Каждый system имеет deterministic star type, 1–8 planets, archetypes, moons, atmosphere/water flags, economy, danger и planet seeds. `M` открывает Galaxy/System terminal; route planning использует A* по соседним sectors и фактический `HyperdriveRange` установленного ship loadout. Jump разрешён только commissioned/flight-ready кораблю с исправным hyperdrive и активным hyperspace module, только из orbital station; топливо списывается по длине waypoint. Current system, destination, counters и visited systems сохраняются в optional SQLite setting `galaxy_navigation` без повышения schema 2 и согласуются с `visited_planets`. После jump существующие voyage и station-services API переиспользуются в destination system. `F5` запускает отдельную `TASK-114` acceptance, включая 1000 deterministic samples и 100 последовательных hyperjumps.

`TASK-148` добавляет отсутствовавший orchestration layer между уже реализованными voyage/galaxy/star-system подсистемами. `WorldSceneCoordinatorRuntime` живёт в `Game.Application` и не зависит от Godot: разрешены только `Surface ↔ Orbit ↔ StationInterior → HyperspaceTransit → StationInterior`, а system/planet IDs берутся из `GalaxyNavigationRuntime`. `WorldSceneCoordinatorNode`, создаваемый программно под `Gameplay` после загрузки основной сцены, держит ровно один лёгкий PackedScene-shell текущего контекста; heavy surface/orbit объекты не дублируются, а переводятся в suspend/restore policy. На станции и в hyperspace отключены surface и orbit runtime; в Orbit сохраняется прежний bounded 260 m overlap поверхности у планеты, а system proxies выводятся только в Orbit. Hyperspace сначала входит в transit shell, после успешного jump пересобирает destination station context, при отказе возвращается в source station. Отдельного `world_scene` save block нет: cold restore детерминированно выводит контекст из существующих `stage_one_voyage` + `galaxy_navigation`. В `TASK-149` swap сделан транзакционным: destination PackedScene сначала load/instantiate/add-child и проверяется как реально вошедший в tree; только после этого изменяется application-state и освобождается прежний shell. При любой ошибке staged shell удаляется, а exact runtime snapshot восстанавливает context/generation/counters. `F5` теперь реально проходит `Surface → Orbit → StationInterior → HyperspaceTransit → StationInterior → Orbit → Surface`, проверяет single-shell/residency на каждом из 7 состояний и в `finally` возвращает точный pre-test snapshot. `tools/validate-task148-world-scene-coordinator.py` остаётся обязательным quality gate.


`TASK-118` закрывает процедурную mission/quest подсистему PDF v2.0 §19 и Stage 2 baseline на 20 заданий. `procedural_quests.json` задаёт баланс всех 15 objective types (`VisitLocation`, `ScanObject`, `ScanSpecies`, `CollectResource`, `CraftItem`, `DeliverItem`, `RepairObject`, `DefeatTarget`, `ProtectTarget`, `BuildModule`, `TradeItem`, `FindSignal`, `ExplorePlanet`, `ExploreSystem`, `ReturnToNpc`). `ProceduralQuestGenerator` строит deterministic 20-quest board из world seed и только из реально доступных capability pools; после TASK-122 combat/protection objectives используют реальные hostile/protected NPC targets, поэтому текущий gameplay-board покрывает все 15 objective types с feasibility по реальным ID. Каждый generated `QuestDefinition` содержит линейный state graph из `QuestNode`/`QuestCondition`/`QuestAction` и `QuestReward`: objective → optional return-to-giver → claim. Feasibility проверяет существование target, NPC, equipment tier, landing/inventory capability и отсутствие циклов. `Q` на поверхности открывает отдельный mission journal; в Station Services `Q` по-прежнему переключает legacy Quests tab, а в полёте остаётся roll input. Progress подключён к существующим resource/craft/trade/repair/build/POI/ecology/voyage/galaxy events. Rewards зачисляются в реальную station-services economy; faction reputation остальных фракций вычисляется из completed mission state. Сохраняются только delta-state миссий в optional SQLite setting `procedural_quests`, schema остаётся `2`. `F5` включает изолированную `TASK-118` acceptance в `save_1.procedural-quests-test.db`.

`TASK-120` закрывает core персонажа PDF v2.0 §13: Health, Shield, Stamina, LifeSupport, HazardProtection, Temperature/Radiation/Toxic protection, Oxygen, JetpackEnergy и MultitoolEnergy. `player_survival.json` связывает три существующих suit-модуля, три существующих Tool outputs и шесть consumables с runtime, не меняя нормативный baseline 174/42/128/15/32. На поверхности работают sprint, crouch, jetpack и water swimming; environmental archetype текущей планеты расходует hazard/life-support/oxygen с учётом protection. `I` открывает Exosuit & Multitool, `Z` переключает функцию multitool. Scanner/mining/weapon/analyzer/repair используют единый энергетический budget, fauna может наносить реальный shield/health damage. Состояние персонажа и equipment сохраняется в optional `save_settings.player_survival`, schema остаётся 2. Одновременно исправлен repeat-save defect: `procedural_quests` теперь удаляется/перезаписывается вместе с прочими optional settings, а TASK-116/TASK-118 acceptance читает фактический `SaveAutosaveCoordinator.AutosaveLogPath`. `F5` включает изолированную TASK-120 acceptance в `save_1.player-survival-test.db`.

`TASK-122` закрывает базовый NPC/faction core PDF v2.0 §16 без создания параллельной экономики. Новая `npc_factions.json` ссылается на уже существующие три faction definitions Station Services и покрывает ровно восемь типов из ТЗ: Trader, Technician, Pilot, Scientist, Guard, GuildRepresentative, Traveler и Opponent. Существующий `npc.trader.ilia_voss` не дублируется; ещё семь физических NPC создаются в `Gameplay/NpcPopulation`, используют behavior targets поверх TASK-124 NavigationAgent3D, `E`-interaction и hitscan damage. Hostile Opponent атакует игрока через существующий `PlayerController.ReceiveExternalDamage`; боевой target воспроизводим после defeat, поэтому процедурные `DefeatTarget` не становятся необратимо невыполнимыми. Scientist и Traveler являются реальными `ProtectTarget`, подтверждаемыми через dialogue action. Каждый template dialogue содержит ID, condition/minimum reputation, RU/EN lines, варианты ответа, consequence, reputation delta и action; GuildRepresentative открывает существующий Mission Journal, Trader — существующие Station Services. `ProceduralQuestCapabilities` теперь получает реальные hostile/protected NPC IDs, поэтому gameplay-board поддерживает все 15 objective types, включая `DefeatTarget` и `ProtectTarget`. Faction reputation и изменённые NPC states хранятся только дельтами в optional `save_settings.npc_factions`; SQLite schema остаётся 2. `F5` включает отдельную `TASK-122` acceptance в `save_1.npc-factions-test.db`.

**TASK-150 Multi-Planet Environment.** `Content/planet_environments.json` задаёт ровно девять архетипов планет: temperate, desert, frozen, volcanic, toxic, radioactive, barren, oceanic и non-landable gas giant. Стартовая система теперь детерминированно содержит четыре landable-планеты разных архетипов, при этом ID исходной стартовой планеты сохранён как planet 1. `PlanetEnvironmentRuntime` вычисляет radius/gravity/temperature/moisture/atmosphere/water/clouds из planet seed и star type; выбор биома учитывает latitude, elevation, distance to water и local deterministic noise и ограничен 1–8 разрешёнными ecology biomes. `GalaxyNavigationSaveData` получил backward-compatible `CurrentPlanetId` без SQLite schema bump. System map и gameplay HUD показывают environment-параметры, а Developer Planet Preview накладывает сферическую воду, упрощённую атмосферу и до двух scrolling cloud shells через три новых shader-файла. Физическая симуляция жидкости и дорогой volumetric ray marching намеренно отсутствуют. F5 запускает `TASK-150 planet environment acceptance` с критериями `4/4 starter planets`, `4/4 starter archetypes`, `9/9 catalog archetypes`, deterministic/radius/biome/water/atmosphere/cloud/persistence invariants и `samples=16`. Полный interplanetary travel между четырьмя планетами остаётся отдельным следующим gameplay-шагом; TASK-150 закрывает environment generation/presentation foundation, а не подменяет полёт к другой планете.

**TASK-152 Interplanetary Travel.** На вкладке System карты `M` Up/Down выбирают планету, а `Enter` ставит landable-планету как `TARGET`; текущая помечается `CURRENT`, gas giant отклоняется. Выбранная цель сохраняется отдельно от `CurrentPlanetId`. После взлёта/отстыковки существующий `K` navigation assist использует реальную proxy-позицию планеты из `StarSystemSimulationNode` и подаёт thrust/boost/brake через тот же `ArcadeShipController`, поэтому перелёт не является teleport. При старте списывается bounded fuel cost; отключение `K` отменяет assist без возврата топлива. World coordinator расширен контекстом `InterplanetaryTransit`: допустим только `Orbit(source) → InterplanetaryTransit(source) → Orbit(destination)`, прямой cross-planet Orbit→Orbit запрещён. На arrival `CurrentPlanetId` меняется транзакционно, затем корабль переносится в локальную planet-approach область и существующий inbound/landing flow продолжает посадку. `SelectedPlanetId`, число перелётов и суммарная дистанция добавлены backward-compatible полями в galaxy-navigation save без SQLite migration. F5 запускает отдельный `TASK-152 interplanetary travel acceptance`; подробности — `docs/INTERPLANETARY_TRAVEL.md`.

**TASK-149.4 runtime regression closure.** После реального F5-прогона TASK-148 world-scene path подтверждён (`livePath=1`, `transactionalSwap=1`, `stateRestored=1`, `sceneLoadFailures=0`, `rollbacks=0`). Одновременно F5 выявил четыре смежных regression defects: TASK-130 raw-path comparison, TASK-142 double-tick frequency gate, TASK-124 NavigationServer query до первой map synchronization и TASK-126 проверку orbital traffic в Surface residency. Исправление делает F5 контекстно-безопасным: ground navigation ждёт фактического изменения map iteration, а NPC ships получают acceptance samples только на двух Orbit legs TASK-148 и после теста снова считаются suspended на Surface.

`TASK-124` реализует ground navigation PDF v2.0 §30.1 отдельным bounded runtime. `Gameplay/NpcNavigation` держит procedural `12 × 12 m` tiles с `1 m` cell и радиусом streaming `2`, поэтому одновременно существует не более `25` `NavigationRegion3D`. Walkable cells выводятся из authored ground bounds и nearby static collision shapes с clearance для NPC; для тех же объектов создаются `NavigationObstacle3D` avoidance proxies. Семь динамических NPC используют `NavigationAgent3D`: target задаётся из patrol/flee/hostile behavior, `GetNextPathPosition()` вызывается в physics update, `velocity_computed` подаёт safe velocity в `MoveAndSlide`. При отсутствии прогресса включается navigation-based recovery waypoint. Base/POI rebuild пересчитывает local obstacles; NPC вне active tile window sleeps вместо движения напрямую. `F5` добавляет `TASK-124`: cross-tile path, obstacle clearance, bounded stream shift/eviction/restore, server sync, реальные path requests, avoidance callbacks и recovery probe.

`TASK-126` закрывает оставшуюся навигационную главу PDF v2.0 §30.2–30.3. Flying fauna и NPC ships используют общий `AerialSteeringRuntime` с локальной 3D spatial-hash grid `10 m`; spherical proxies существующих static collision shapes индексируются в пересекаемые grid cells, а POI остаются data-driven. Все четыре flying species получают separation, spherical obstacle avoidance, POI steering и ограниченный altitude envelope вместо прежнего sine-only vertical motion. В `Gameplay/NpcShipTraffic` создаются четыре физических ship agents на существующих class stats: patrol leader (`arrive`), formation wing (`formation`), trader approach (`arrive`) и hostile raider (`pursuit → CombatApproach → BreakAway → evade → pursuit`). Все ship roles дополнительно применяют local-grid separation, static avoidance и altitude envelope; raider в обычной игре переключается на piloted player ship. Новых save settings нет: runtime transient и воспроизводимо rebuild-ится после load/reset. `F5` добавляет `TASK-126` acceptance с реальными steering samples, grid/obstacle/POI probes, altitude coverage, всеми четырьмя steering primitives, combat-state transitions и ship obstacle-clearance check.

`TASK-128` закрывает vertical-slice runtime звёздной системы PDF v2.0 §15. Уже существующий `GalaxyNavigationRuntime` остаётся единственным источником system/planet seeds и после hyperspace автоматически перестраивает `StarSystemSimulationRuntime`: одна звезда, 1–8 планет, 0–4 спутника на планету, station proxies и локальные ship contacts. Орбиты вычисляются аналитически в наклонённых плоскостях с постоянным радиусом и замедленным simulation time; гравитационные взаимодействия/N-body намеренно отсутствуют по ТЗ. `Gameplay/StarSystemSimulation` создаёт только lightweight visual proxies и переключает representation `Proxy / Marker / Statistical`; текущая планета имеет `DetailedPlanet`, причём одновременно подробной может быть только одна. При удалении корабля более чем на `260 m` от surface checkpoint наземный PlanetRuntime переводится в suspended state: скрываются и перестают process/collide ground, resources, crafting stations, ecology, NPC, ground navigation, base и POI, а orbital station/ship traffic остаются активны; при возвращении восстанавливаются точные прежние visibility/process/collision states. После hyperspace old system model уничтожается и детерминированно строится новая. `F5` добавляет `TASK-128` acceptance на deterministic hierarchy, exact planet/moon bounds, invariant analytic orbits, все три дальних LOD-уровня, single-detailed-planet invariant, system transition, visual projection и PlanetRuntime activation pipeline. Persistence schema не меняется: system runtime восстанавливается из уже сохраняемого `galaxy_navigation`.

`TASK-130` переводит проект с прямого запуска gameplay-сцены на полноценный application shell. `project.godot` теперь запускает `Scenes/UI/MainMenu.tscn`; меню асинхронно инспектирует primary SQLite slot и имеет отдельные экраны Continue/New Game/Load Game/Settings. New Game сбрасывает `save_1` через штатные `SaveDatabase.InitializeAsync → ResetSlotAsync`, не удаляя SQLite-файлы вручную и не затрагивая пользовательские настройки. Settings сохраняются отдельно в `user://settings.cfg` через `ConfigFile`: on-foot/ship sensitivity и inversion, FOV, UI scale, subtitles/camera-shake/motion-blur flags, Music/SFX/Voice volumes и keyboard bindings. On-foot sprint/crouch и вся ручная схема корабля переведены с physical-key polling на `InputMap`, поэтому remapping исполняется реальным gameplay; standard gamepad events остаются параллельными keyboard bindings. В vertical slice `ApplicationShell` работает в `ProcessMode.Always`: Escape/pause останавливает `SceneTree`, Settings остаются доступны во время паузы, `SAVE & MAIN MENU` сначала проходит существующий graceful autosave, а death state показывает отдельный blocking screen. Отдельный `Planet Map` (`N`) проецирует уже существующее planetary-exploration state в локальную карту поверхности с player/unknown/discovered/resolved POI и не дублирует exploration data. Полная §31.3 localization по-прежнему выделена отдельно: TASK-130 не выдаёт существующие hardcoded gameplay strings за локализованные. `F5` добавляет TASK-130 structural/runtime contract acceptance.

`TASK-132` закрывает §31.3 Localization для shipping application/vertical slice. `GameLocalizationService` использует существующие `Content/localization.en.json` и `Content/localization.ru.json` как единственный источник переводов, проверяет exact parity и пустые значения и поддерживает `Automatic / English / Русский` из `user://settings.cfg`. Смена locale работает без restart и перерисовывает Main Menu, Settings, Pause/Death, HUD и открытые gameplay panels. Station Services, NPC/Factions и Ecology мигрированы с дублированных `...En/...Ru` полей на localization keys; player-facing action results и interaction prompts также разрешаются через общий service. После добавления локализованной audio diagnostics в TASK-134 каталоги содержат `1329` ключей на язык с exact RU/EN parity; 50 ранее отсутствовавших `base.module.*` переводов также восстановлены. `tools/validate-localization-contract.py` является статическим gate и проверяет catalog parity, все `486` content-key references, key-only content, shipping-scene keys и отсутствие raw player-facing source sinks. `F5` добавляет TASK-132 runtime acceptance с EN↔RU live switch и required-key coverage. Developer prototype/acceptance diagnostic strings не относятся к shipping UI contract.

`TASK-134` закрывает техническую sound architecture PDF v2.0 §32. Один persistent `AudioDirector` создаёт нормативные buses `Master/Music/Ambient/SFX/UI/Voice/Vehicle/Weather`, использует фиксированные pools `8 × AudioStreamPlayer + 16 × AudioStreamPlayer3D` (не более 24 transient voices; общий hard ceiling 29 с dedicated loops) с priority-aware stealing и маршрутизирует world SFX через positional `AudioStreamPlayer3D` с `UnitSize/MaxDistance`. Четыре environment profiles (`Atmosphere/Vacuum/Interior/Water`) переключают ambient/weather и low-pass effects; в vacuum внешний physical SFX подавляется централизованно, но internal Vehicle/UI/Voice сохраняются. Music state machine `Menu/Surface/Space/Interior/Combat` использует dual-player crossfade; Vehicle loop следует реальной скорости piloted ship. Gameplay hooks покрывают UI, dialogue radio, multitool, resource collect, craft/production completion, damage и life-support alarm. Функциональный `ProceduralAudioBank` создаёт 19 deterministic PCM cues при 44.1 kHz без raw WAV/AIFF source assets; его stable cue IDs можно позднее заменить production OGG без изменения gameplay API. `tools/validate-audio-contract.py` проверяет §32 статически, а `F5` — environment/vacuum/pools/positional/music/runtime contract.
`TASK-136` закрывает внутренние инструменты PDF v2.0 §34 и структурированное логирование §35. В debug build либо при явном `--developer` Main Menu показывает `Developer Tools`, открывающий единый workbench из пяти обязательных инструментов. Seed Explorer принимает произвольный universe seed и sector coordinates, использует существующий `GalaxyNavigationRuntime`, позволяет копировать system ID и экспортировать JSON-отчёт. Planet Preview строит тот же cube-sphere через `CubeSphereMeshBuilder`, меняет LOD/face resolution, показывает generation time и реально применяет комбинируемые grid/biome/height/resource-density overlays в интерактивном prototype; `F6` возвращает в workbench. Chunk Profiler использует существующий terrain runtime и публикует loaded/queued/active work, worker CPU, main-thread apply/GPU-submission proxy, managed memory, vertices, collisions и cancelled/stale jobs. Save Inspector принимает primary или произвольный SQLite save path, снимает WAL-consistent read-only snapshot через `SqliteConnection.BackupDatabase`, а `SaveDatabase` и migrations запускает только на этой изолированной копии; показывает schema/integrity/player/ship/visited systems, экспортирует все пользовательские SQLite-таблицы в CSV read-only и выполняет migration test только на отдельной copy, никогда не на source save. Debug Console (`Ctrl+Shift+D`) исполняет все 15 команд ТЗ (`teleport`, `spawn`, `give`, `damage`, `heal`, `set_time`, `set_weather`, `load_system`, `load_planet`, `show_chunks`, `show_navmesh`, `show_ai`, `profile_worldgen`, `save`, `reload_content`) над тем же vertical slice. `StructuredGameLogger` пишет JSONL с UTC, level, одной из 14 нормативных категорий, session ID, exception/system/scene/world seed/world object и redacted fields; token/password/secret-like и PII-like поля очищаются до записи, а случайные user-home/user-name fragments заменяются безопасными маркерами. `tools/validate-developer-diagnostics-contract.py` является статическим gate, а F5 TASK-136 проверяет все пять tools, 15 commands, 14 log categories и фактическое отсутствие injected secret-строк в JSONL. Полный testing contract §36 оставлен следующей отдельной mega-итерацией и будет опираться на эти инспекторы/метрики.


В состав v2 входят:

- 18 refining recipes;
- 30 chemistry recipes;
- 22 industrial-component recipes;
- 18 ship-module recipes;
- 12 equipment/consumable recipes;
- 10 base recipes;
- 8 drone/vehicle/exotic recipes;
- 10 текущих repair/ship-component recipes.

Химическая линия является канонической частью мира. Она включает Парафиний, добываемый сырой Компотий, растворы и концентраты Компотия, очистку, стабилизацию, катализаторы, электролит, энергетические элементы, реакторный гель и конечные экзотические модули. Название «Компотий» предложено сыном автора проекта и закреплено в ТЗ без изменения.

Recipe schema v2 поддерживает несколько inputs/outputs, catalysts, byproducts, dismantle returns, station/technology tiers, craft time, energy cost, batch size, температуру, давление, вакуум, качество и hazards. `GameContentCatalog` проверяет stable IDs, все ссылки, совместимость station/category/tier, technology graph, циклы и достижимость каждого recipe от мирового сырья.

Текущая стартовая сцена приложения:

```text
src/Game.Client/Scenes/UI/MainMenu.tscn
```

Gameplay vertical slice:

```text
src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn
```

Управление:

```text
WASD / Space   движение и прыжок
Shift          бег (расход Stamina)
Ctrl           присесть; в воде — погружение
Space hold     в воздухе — jetpack; в воде — всплытие
I              Inventory / Exosuit & Multitool (Tab: Overview/Inventory/Suit/Multitool/Consumables)
Z              переключить функцию мультитула
E              собрать ресурс / ремонтировать / открыть station, trader или наземного NPC / подтвердить выбор
Q              на поверхности вне UI открыть/закрыть procedural mission journal
N              открыть / закрыть Planet Map (игрок + unknown/discovered/resolved POI)
Up / Down      выбрать recipe, technology, queue job, market item или quest
Tab            station: Recipes/Research/Queue/Dismantle; services: Dialogue/Buy/Sell/Quests
R              station terminal: переключить Recipes / Research
D              station terminal: открыть Dismantle
B / S / Q      station services: Buy / Sell / Quests
Enter / E      выполнить выбранное station/service действие
Q              station Recipes: поставить recipe в очередь; из других station tabs открыть Queue
C / Delete     отменить выбранный queue job с полным возвратом reservations
Esc            сначала закрыть активный gameplay UI; вне UI — настоящая пауза / Resume / Settings / Save & Main Menu / Save & Quit
H              detailed / compact / hidden HUD; координаты игрока остаются видимыми
U              на поверхности открыть / закрыть управление системами и модулями корабля
Up / Down      в ship manager выбрать модуль или систему
Tab            в ship manager переключить Overview / Modules / Systems
Enter / E      установить модуль / отремонтировать систему / заправить корабль
X              снять выбранный установленный модуль с возвратом в inventory
D              нанести 25 единиц тестового повреждения выбранной системе
R              отремонтировать выбранную систему одним catalog-defined компонентом
E              у отремонтированного корабля: сесть; на pad/station: disembark/services
Enter          в полёте: выполнить docking или landing по текущей фазе
T              в кабине: взлететь с поверхности или отстыковаться от станции
K              navigation assist: выбранная planet TARGET имеет приоритет; иначе текущая voyage-цель
W / S          тяга вперёд / назад в полёте
A / D          lateral strafe влево / вправо
C / Space      vertical thrust вниз / вверх
Стрелки        pitch вверх/вниз и yaw влево/вправо; мышь — pitch/yaw
Q / E          roll влево / вправо
B / X          boost / braking; G — stabilization
F2             переключить корабельную камеру во время пилотирования
M              открыть system/galaxy map; System: Up/Down planet, Enter TARGET; Tab переключает карты
Enter          в galaxy map: построить route и выполнить следующий hyperspace waypoint
G              открыть / закрыть режим строительства базы
Up / Down      в режиме строительства выбрать модуль
R              в режиме строительства повернуть модуль на 90°
Enter          поставить выбранный модуль в target grid cell
X / Delete     демонтировать targeted module с возвратом stock
T              включить / отключить targeted device
F1             TASK-090/092/093/096/098: queue, properties, multi-station industry и aggregate HUD
F2             TASK-083: chemical process runtime
F3             TASK-082 + TASK-102: research и station services mega-acceptance
F4             TASK-080 + TASK-108: Industry Content v2 и planetary exploration acceptance
F5             mega-acceptance, включая TASK-132 localization + TASK-134 audio + TASK-136 diagnostics + TASK-138 verification + TASK-142 architecture
F6             TASK-106: base construction mega-acceptance + legacy coolant regression
F7             TASK-062 + TASK-100: salvage/repair и полный lifecycle всех 42 ресурсов
F8             очистить gameplay-slot, включая ship systems, voyage, galaxy, survival, quests и NPC/faction deltas
F9             регрессия strict JSON catalog
F10            регрессия launch-capacitor persistence
F11            регрессия craft-time state machine
F12            регрессия navigation path
Ctrl+Shift+D   developer console (только debug build / --developer)
F6             в Planet Preview / Chunk Profiler: вернуться в Developer Workbench
```



Ожидаемый `F1` HUD:

```text
TASK-090 production queue (F1): PASS slots=2, queued=1, pause=1, restore=1, cancel=1, refund=1, completed=2, roundTrip=1
TASK-092 queue terminal (F1): PASS progress=1, energy=1, reservations=1, actions=1
TASK-093 item properties (F1): PASS Q=72, P=80, S=80, dismantle=1, roundTrip=1
TASK-096 multi-station industry (F1): PASS stations=4, recipes=6, routing=1, repeatable=1, chain=1, recharge=1, properties=1, roundTrip=1
TASK-098 production network HUD (F1): PASS stations=5, aggregate=1, transitions=1, recharge=1, restore=1, fallback=1, unavailable=0
```

`F1` запускает изолированную проверку smelter queue на два parallel slots. Три jobs резервируют inputs и energy без overcommit; третья job ожидает слот. Проверка выполняет pause/resume, сохраняет незавершённые jobs через `GracefulExit`, восстанавливает точный elapsed progress без offline progress, отменяет активную job с полным возвратом inputs/catalysts/energy, завершает оставшиеся jobs и проверяет финальный `QuestCompleted` SQLite round-trip. Дополнительно строится тот же terminal projection, который используется игровым UI: проверяются progress bar, elapsed time, energy, reservations и допустимые pause/resume/cancel actions. Параллельный изолированный `TASK-093` проверяет детерминированные `Q/P/S`, зависимость dismantle returns от свойств предмета и exact SQLite round-trip. `TASK-096` прогоняет четыре специализированные station types и шесть связанных recipes: refined ferrite, purified water, Paraffinium fraction/lubricant и raw Compotium solution/concentrate. `TASK-098` строит aggregate HUD по всем пяти физическим станциям и проверяет aggregate counts/energy, одновременную работу Smelter и Refinery, pause/resume, cancel/refund, completion, recharge, exact cold restore без offline progress, legacy single-queue fallback и отсутствие ложного `unavailable`. Используются отдельные БД `save_1.production-queue-test.db`, `save_1.item-properties-dismantle-test.db`, `save_1.multi-station-industry-test.db` и `save_1.production-network-hud-test.db`; gameplay-slot не изменяется.

Ожидаемый дополнительный `F7` HUD:

```text
TASK-100 resource lifecycle (F7): PASS catalog=42, physical=42, nodes=58, generated=26, collectTypes=42, collectNodes=58, duplicate=1, mirrors=1, depletion=1, restore=1, reset=1, roundTrip=1
```

`F7` одновременно сохраняет прежнюю регрессию `TASK-062` и запускает отдельную БД `save_1.resource-lifecycle-test.db`. `TASK-100` выбирает по одному физическому узлу каждого из 42 типов, проверяет metadata и MaxStack, собирает весь baseline, отклоняет повторный сбор, синхронно расходует часть ресурсов в session и во всех station inventory mirrors, выполняет exact SQLite round-trip, cold restore, database reset, `maxWriters=1` и `integrity=ok`. Gameplay-slot не изменяется.


### Multi-station Paraffinium and Compotium starter line

После `F8` в мире доступны дополнительные добываемые узлы и четыре отдельные станции. Линия выполняется последовательно:

```text
2 ferric_ore -> Smelter -> refined_ferrite
2 ice_water -> Refinery -> purified_water
2 paraffinium -> DistillationColumn -> paraffinium_fraction
paraffinium_fraction + refined_ferrite -> ChemicalProcessor -> paraffinium_lubricant
raw_compotium + acidic_brine -> ChemicalProcessor -> raw_compotium_solution (repeatable, выполнить дважды)
2 raw_compotium_solution + purified_water + catalytic_dust -> DistillationColumn -> compotium_concentrate
```

Для постановки процесса подойдите к нужной station, откройте терминал `E`, исследуйте требуемую technology, выберите recipe и нажмите `Q`. Вкладки Queue относятся к конкретной станции; jobs разных станций выполняются параллельно и сохраняются одной `production_queue_network`.

### Aggregate production network HUD

В detailed и compact HUD отображается единая сводка, рассчитанная непосредственно из `ProductionNetworkRuntime`:

```text
Production network: stations=5 • jobs=2 • running=2 • queued=0 • paused=0 • energy=948/1060
Stations: PortableFabricator 80/80 [0R/0Q/0P] • Smelter 140/180 [1R/0Q/0P] • Refinery 248/320 [1R/0Q/0P] • DistillationColumn 300/300 [0R/0Q/0P] • ChemicalProcessor 180/180 [0R/0Q/0P]
```

Detailed mode показывает все станции. Compact mode показывает активные stations и `+N idle stations`. Значение `Production network: unavailable (...)` допустимо только при реальном отсутствии или исключении инициализации runtime; пустая сеть с `jobs=0` остаётся доступной.

Для ручной приёмки после `F8` запустите `refined_ferrite` на Smelter и `purified_water` на Refinery, добавьте queue job, выполните pause/resume и cancel, дождитесь completion, затем штатно перезапустите игру с незавершёнными running/queued/paused jobs. Сводка должна немедленно отражать каждое изменение и восстановить elapsed, states и station energy без offline progress.

### Stage 1 station services: economy, trader and quests

Синий `StationTrader` расположен на тестовой площадке примерно в точке `x=14, z=12`. Подойдите и нажмите `E`. Dialogue предлагает открыть market, contracts или завершить разговор. Внутри панели:

```text
Up/Down       выбор
Tab           Dialogue / Buy / Sell / Quests
B / S / Q     быстрый переход Buy / Sell / Quests
Enter / E     buy/sell/accept/claim/dialogue action
Esc           закрыть
```

Market покрывает все `174` item definitions. Для выбранного item показываются buy/sell, stock, player inventory и все шесть факторов цены. Buy уменьшает player credits и stock, Sell увеличивает их; обе операции синхронизируют inventory основной session и пяти production queues. Economy day и deterministic daily modifier обновляются после значимого time delta.

Три стартовых contracts:

```text
CollectResource: 2 x resource.ferric_ore        -> 180 credits, +4 reputation
CraftItem:      1 x material.refined_ferrite    -> 260 credits, +6 reputation
TradeItem:      1 x resource.ice_water          -> 220 credits, +5 reputation
```

Quest нужно принять до соответствующего действия. После достижения objective статус становится `ReadyToClaim`; claim выдаёт credits/reputation и сохраняется. Для smoke-test нажмите `F8`, примите все три quests, соберите ferric ore и ice water, изготовьте refined ferrite на Smelter, продайте ice water и claim contracts. После штатного restart credits, reputation, stock и quest states должны восстановиться; `F8` возвращает `2400` credits, `0` reputation, stock `6` и quests `Offered`.

### Base construction subsystem

Нажмите `G`, чтобы открыть builder. Target cell вычисляется по направлению взгляда игрока и округляется к сетке `2,5 м`. Первый модуль обязан быть `module.base_power_node`; каждый последующий модуль должен находиться в одной из четырёх соседних cells. Зелёный preview означает допустимую постановку, красный — collision, отсутствие snap, stock или нарушение limit.

```text
Up / Down    выбрать один из 50 модулей
R            rotation 0/90/180/270
Enter        place
X / Delete   remove с connectivity check и refund
T            enable/disable generator, battery или consumer
G / Esc      close
```

HUD builder показывает target grid/world coordinates, category, stock, power generation/consumption, battery, powered consumers и компактное окно palette. Module nodes имеют mesh, static collision и фактические dynamic lights согласно catalog metadata. Terrain geometry не изменяется.

Ожидаемый `F6` HUD:

```text
TASK-072 legacy fourth path (F6): PASS resources=2, blocked=1, timed=1, isolated=1, all3=1, output=1, roundTrip=1
TASK-106 base construction (F6): PASS modules=50, placed=50, snap=1, collision=1, preflight=1, power=1, batteryIsolation=1, limits=1, stress500=1, restore=1, roundTrip=1
TASK-146 base construction closure PASS: preflightParity=1; batteryIsolation=1; malformedSaveRejected=1; stress500=1; coldRestore=1; roundTrip=1.
```

`TASK-106` использует отдельную БД `save_1.base-construction-test.db` и проверяет 50 modules / 17 catalog categories (all 16 PDF categories plus Structure), обязательный anchor, grid collision, disconnected placement/removal rejection, connected power graph, battery charge, device toggle, dismantle refund, связный stress graph из 500 modules и отказ на 501-м, отдельный interactive-device limit, exact cold restore, legacy fallback, autosave log, `maxWriters=1` и `integrity=ok`. Gameplay-slot тестом не изменяется.

Ручной smoke-test: нажать `F8`, открыть `G`, поставить anchor, затем несколько соседних structural modules, solar array, battery и consumer; проверить рост generation/consumption и battery; отключить consumer клавишей `T`; попытаться поставить module поверх существующего и отдельно от базы; демонтировать крайний module и убедиться в refund; штатно перезапустить игру и проверить exact restore; `F8` должен вернуть пустую базу и исходный palette.

### Catalog-wide resource lifecycle

При старте `CatalogResourceFieldPlanner` сравнивает `Content/resources.json` с hand-authored узлами сцены. Для каждого отсутствующего типа создаётся один `SalvageResourceNode` со стабильным ID `catalog.<resource>`, детерминированной позицией и материалом из `ResourceVisualDefinition`. Текущая контрольная конфигурация:

```text
catalogResources=42
physicalResourceTypes=42
authoredNodes=32
generatedNodes=26
totalNodes=58
```

Сгенерированное поле расположено на расширенной тестовой площадке в секторе `z=23.0..36.5`. Все узлы используют существующее взаимодействие `E`, deterministic yield, MaxStack validation и одноразовый collection ID. Собранное состояние сохраняется как inventory delta; после cold restart узел остаётся скрытым, а его остаток и production-network mirrors восстанавливаются. `F8` удаляет snapshot и возвращает все 58 узлов. SQLite schema остаётся `2`.

Ручной smoke-test: нажмите `F8`, убедитесь в detailed HUD `types=42/42`, `nodes=58`, `generated=26`; соберите любой узел из generated field; выполните штатный выход и повторный запуск — выбранный узел не должен появиться снова; затем нажмите `F8` и убедитесь, что он снова доступен. Полное покрытие всех 42 типов проверяется автоматически клавишей `F7`.

### Ручная проверка Queue-вкладки

1. Соберите ресурсы для доступного рецепта и при необходимости исследуйте его технологию.
2. Откройте PortableFabricator, выберите рецепт и нажмите `Q`.
3. Терминал автоматически перейдёт во вкладку Queue. Должны отображаться status, progress bar, elapsed/duration, slot, reserved energy и reserved inputs.
4. Нажмите `Enter` или `E`: running job перейдёт в `PAUSED`, прогресс остановится. Повторное нажатие вернёт job в `RUNNING`.
5. Для проверки отмены нажмите `C` или `Delete`: job исчезает, inputs и energy возвращаются полностью.
6. Для проверки persistence поставьте job в очередь, закройте игру штатно и запустите снова. Job должен восстановиться с тем же elapsed progress; offline progress не начисляется. В Output появляется `TASK-092 player queue restore PASS` с числом jobs и сохранённым elapsed.

Ожидаемый `F2` HUD:

```text
TASK-083 chemical runtime (F2): PASS batch=2, energy=1, environment=1, vacuum=1, catalyst=1, byproduct=1, roundTrip=1
```

`F2` запускает изолированную chemical-runtime проверку на двух рецептах Компотия. Она подтверждает отказ при нехватке энергии и неверной среде, обязательный вакуум, batch output, deterministic catalyst retained/consumed paths, byproducts, hazards, QuestCompleted autosave и exact SQLite round-trip. Основной gameplay-slot не изменяется.

Ожидаемый `F3` HUD:

```text
TASK-082 selector/research (F3): PASS recipes=9, oneStation=1, initial=4/5, unlocked=9, crafted=1, rp=690, roundTrip=1
TASK-102 station services (F3): PASS economies=6, factions=3, npc=1, quests=3, tradable=174, price=1, daily=1, trade=1, graph=1, restore=1, roundTrip=1
```

Ожидаемый `F4` HUD:

```text
TASK-080 industry catalog (F4): PASS recipes=128, chemistry=30, compotium=13, stations=15, tech=32, cycles=0, unreachable=0
```

`F3` параллельно прогоняет две изолированные проверки. `TASK-082` сохраняет universal station selector и research graph. `TASK-102` проверяет точный baseline station services `6 economies / 3 factions / 1 NPC / 3 dialogue options / 3 quests / 174 tradable items`, шестимножительную price formula, daily/offline economy, atomic buy/sell, credit conservation, quest graph feasibility, rewards/reputation, cold restore, legacy fallback, one-writer и SQLite integrity. Используется отдельная БД `save_1.station-services-test.db`; gameplay-slot не изменяется.

Ожидаемый `F5` HUD:

```text
TASK-076 runtime matrix (F5): PASS station=15, blocked=15, timed=15, isolated=15, crafted=15, output=20, roundTrip=1
TASK-110 ship systems (F5): PASS classes=6, systems=7, modules=18, coverage=1, slots=1, damage=1, repair=1, commissioning=1, readiness=1, fuel=1, restore=1, roundTrip=1
TASK-112 Stage 1 voyage (F5): PASS derived=1, preRepair=1, takeoff=1, fuel=1, dock=1, station=1, undock=1, landing=1, loop=1, readiness=1, restore=1, roundTrip=1
TASK-114 galaxy navigation (F5): PASS deterministic=1, stars=1, route=1, jump=1, stress100=1, restore=1
TASK-116 ecology (F5): PASS biomes=16, flora=60, fauna=20, deterministic=1, populations=1, discovery=1, restore=1
TASK-118 procedural quests (F5): PASS objectiveTypes=15, generated=20, deterministic=1, feasibility=1, lifecycle=1, gameplayBoard=1, restore=1
TASK-120 player survival (F5): PASS suit=3, multitool=3, consumables=6, environments=8, hazards=1, oxygen=1, movement=1, damage=1, restore=1, repeatedSave=1
TASK-122 NPC/factions (F5): PASS factions=3, archetypes=8, agents=8, dialogues=8, relations=1, interaction=1, reputation=1, combat=1, questTargets=1, deltaOnly=1, restore=1, repeatedSave=1
TASK-124 NPC navigation (F5): PASS regions=<1..25>/25, tiles>=3, crossTilePath=1, obstacleClearance=1, boundedStreaming=1, navigationAgents=7, pathRequests>0, avoidanceSamples>0, recoveryProbe=1, sync=1
TASK-126 aerial navigation (F5): PASS flyingFauna=4, npcShips=4, gridCells>0, obstacles>0, poi>=8, faunaCoverage=1, sharedRuntime=1, localGrid=1, sphericalAvoidance=1, altitude=1, poiSteering=1, shipSteering=1, pursuit=1, evade=1, arrive=1, formation=1, combatStates=1, clearance=1, runtimeSamples=1, faunaProbeSamples=4
TASK-128 star-system simulation (F5): PASS deterministic=1, bodyCoverage=1, moonBounds=1, analyticOrbits=1, representationLevels=1, singleDetailedPlanet=1, systemTransition=1, visualProjection=1, runtimeSamples=1, surfaceActivation=1, activationPipeline=1
TASK-148 world scene coordinator (F5): PASS transitionGraph=1, illegalRejected=1, hyperspaceSystemChange=1, contextValidation=1, packedScenes=1, singleLiveScene=1, liveContextMatch=1, residencyPolicy=1, livePath=1, transactionalSwap=1, stateRestored=1, steps=7, maxHostChildren=1, testTransitions=6, testReloads=7, testRejected=1, testHyperspace=1
TASK-130 application shell (F5): PASS mainMenu=1, newGame=1, load=1, settings=1, pauseOverlay=1, deathScreen=1, settingsRoundTrip=1, profileContract=1, keyboardRemap=1, inventory=1, planetMap=1, gamepad=1, accessibility=1
TASK-132 localization (F5): PASS locales=2, keys=1336, parity=1, missingValues=0, missingKeys=0, keyOnlyContent=1, sceneKeys=1, liveSwitch=1, settingsLanguage=1
TASK-134 audio architecture (F5): PASS buses=8/8, cues=19/19, pool2d=8, pool3d=16, activeTransient<=24, maxConcurrent=29, poolSteals>0, positional=1, attenuation=1, atmosphere=1, water=1, interior=1, vacuum=1, externalVacuumSuppressed=1, internalVacuumAllowed=1, musicCrossfade=1, ui=1, voice=1, settingsRouting=1
TASK-136 developer diagnostics (F5): PASS tools=5/5, commands=16/16, devGate=1, seedExplorer=1, planetPreview=1, chunkProfiler=1, saveInspector=1, debugConsole=1, logCategories=14/14, redaction=1, secretLeak=0, jsonl=1
TASK-138 verification suite (F5): PASS generatorVersion=3, goldenSystems=4/4, goldenPoi=1, controlHeights=1, checksums=1, unitGroups=10/10, saveScenarios=8/8, loadScenarios=8/8, landingStress=100/100, visualSmoke=1, visualComponents=1, coverageThresholds=80/70/80
TASK-142 architecture hardening (F5): PASS typedEvents=11/11, liveSubscriptions=11/11, nearbyTicks≈100, distantTicks≈20, physicsHz=60, playerHz=60, nearbyAiHz=10, distantAiHz=2, backgroundEconomyHz=0.5, eventBus=1, frequencyPolicy=1
```

`F5` прогоняет независимые subsystem acceptance-проверки, включая application shell, localization runtime, TASK-134 audio architecture, TASK-136 Developer & Diagnostics Suite, TASK-138 golden/visual smoke и TASK-142 typed-event/frequency architecture smoke. Полная §36 проверка намеренно выполняется отдельной командой `tools\run-section36-tests.cmd`: F5 не подменяет xUnit/coverage gate. `TASK-076` сохраняет полную runtime crafting matrix. `TASK-110` проверяет точные counts `6 classes / 7 systems / 18 modules`, module coverage, class stats, блокировку операций до starter repair, commissioning transition, slot limits, derived stats, damage/repair/readiness/fuel lifecycle, cold restore, legacy fallback и exact SQLite round-trip в `save_1.ship-systems-test.db`. `TASK-112` использует отдельную `save_1.stage-one-voyage-test.db`: подтверждает применение effective ship stats к flight profile, запрет посадки в неотремонтированный корабль, расход топлива, docking/station/return/landing lifecycle, disembark, active-flight restore и exact persistence. `TASK-114` использует `save_1.galaxy-navigation-test.db`: проверяет 1000 deterministic systems, GalaxyId/Sector/Double3 hierarchy, все шесть star types, planet bounds, range-aware A*, strict preconditions, fuel debit, visited discovery, cold restore, legacy fallback, exact round-trip и 100 последовательных hyperjumps. `TASK-116` проверяет deterministic ecology baseline и delta-only persistence. `TASK-118` использует `save_1.procedural-quests-test.db` и проверяет все 15 objective types, deterministic 20-offer board, feasibility rejection, active limit, state-graph lifecycle, rewards, current gameplay board, cold restore, legacy fallback, exact round-trip, autosave log, one-writer discipline и SQLite integrity. `TASK-122` дополнительно проверяет reciprocal faction matrix, все восемь archetypes/dialogue templates, one-shot dialogue reputation consequences, friendly-fire reputation penalty, respawnable hostile combat target, реальные Defeat/Protect capability IDs, delta-only save, repeated optional-setting replacement, cold restore, legacy fallback, autosave log, one-writer discipline и SQLite integrity. `TASK-124` дополнительно проверяет локальный tile budget, межтайловый NavigationServer3D path, obstacle clearance, forced stream eviction/restore, server synchronization, NavigationAgent3D path requests, avoidance callbacks и recovery probe. Path acceptance использует bounded readiness barrier: после NavigationServer iteration gate он повторяет query до появления валидного cross-tile path и повторно проверяет путь после stream restore, не ослабляя исходные инварианты. `TASK-126` проверяет exact flying coverage `4`, общий aerial runtime, local spatial-grid probe, spherical static avoidance, POI selection, altitude envelope, четыре физических NPC ships, runtime samples для `arrive/formation/pursuit/evade`, combat-state transitions и clearance относительно spherical obstacle proxies. Начиная с alpha.160.1 flying-fauna часть acceptance не зависит от положения игрока: после baseline каждый flying node выполняет один non-moving shared-runtime probe, поэтому F5 остаётся валидным и после traversal на сотни метров за пределы штатного 50 m ecology update radius. `TASK-128` проверяет deterministic star-system hierarchy, exact planet/moon coverage, аналитические орбиты с постоянным радиусом, Proxy/Marker/Statistical tiers, invariant ровно одной DetailedPlanet, deterministic system transition, live visual projection и текущий PlanetRuntime activation pipeline. `TASK-150` проверяет four-planet starter system, nine-archetype environment catalog, deterministic radius/climate, 1–8 biome constraints, latitude/elevation/water/noise sampling, water/atmosphere/cloud policy, non-landable gas giant и exact current-planet save round-trip (`samples=16`). `TASK-152` проверяет planetary target selection/persistence, fuel debit, assisted guidance, transactional Orbit→InterplanetaryTransit→Orbit handoff, destination local approach и exact transfer-counter persistence. `TASK-148/TASK-149` сохраняет исходный live acceptance graph Surface/Orbit/Station/Hyperspace, а TASK-152 расширяет общий world coordinator пятым context `InterplanetaryTransit`; загружаются пять PackedScene shells, direct Surface→Station и cross-planet Orbit→Orbit запрещены, system меняется только через hyperspace, planet — только через interplanetary transit, invariant одного активного shell сохраняется. F5 выполняет этот граф на **живом** coordinator, требует `steps=7`, `maxHostChildren=1`, `testTransitions=6`, `testReloads=7`, отсутствие transaction failures и `stateRestored=1`; после теста coordinator context/generation/counters должны совпасть с pre-test snapshot. `TASK-134` переключает atmosphere/water/interior/vacuum profiles, проверяет external-vacuum suppression против internal Vehicle cue, overflow обоих bounded pools, positional requests/attenuation, UI/Voice layers, music state transitions и bus-volume routing, затем восстанавливает текущую audio environment. Gameplay-slot ни одна acceptance не изменяет.

### §36 Verification & automated tests (TASK-138)

Standalone test project:

```text
tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj
```

Обычная автоматическая проверка:

```bat
tools\run-section36-tests.cmd
```

Команда сама выполняет `dotnet test`, собирает Cobertura через coverlet и затем требует
`Domain >= 80%`, `WorldGen >= 70%`, `Persistence >= 80%`. Golden manifest находится в
`src/Game.Client/Testing/golden-seeds.v1.json` и связан с `ProjectHorizonGenerator.Version`;
изменение deterministic output без осознанного bump версии приводит к FAIL.

Полный тяжёлый вариант:

```bat
tools\run-section36-tests.cmd --full-soak
```

Он дополнительно включает реальный SQLite test размером не менее 1 GiB. Обычный gate
выполняет ускоренные virtual-time 2h/8h сценарии, 100 последовательных voyage docking/landing loops с persistence round-trip и 100 реальных hyperspace jumps через существующий navigation acceptance runner; F5 дополнительно повторяет 100 voyage loops,
500-module base, 10,000-entry inventory, 1000 visited systems и repeated recovery, не
создавая гигабайтный файл при каждом запуске.

### §37 Build / CI / Release engineering (TASK-140)

Репозиторий содержит два GitHub Actions pipeline:

```text
.github/workflows/ci.yml
.github/workflows/release.yml
```

Pull request / integration CI выполняет restore, C# build с `ContinuousIntegrationBuild=true`
и warnings-as-errors, xUnit + coverage thresholds, JSON/Industry Schema validation,
изолированные persistence migration/recovery tests, затем headless Godot 4.7.1 .NET exports
четырёх desktop-профилей: primary Windows/Linux и Compatibility/OpenGL Windows/Linux. Локальный quality-equivalent запускается:

```bat
tools\run-section37-quality.cmd
```

или:

```bash
./tools/run-section37-quality.sh
```

Release workflow можно сначала запустить вручную (`workflow_dispatch`) как dry-run: он
повторяет quality gates, создаёт primary + Compatibility Windows/Linux Release exports, отдельный
portable-PDB symbols archive, `release-manifest.json`, `RELEASE_NOTES.md` и `SHA256SUMS.txt`, но ничего
не публикует. Push тега строго `v<VERSION>` запускает тот же pipeline и после успешной
упаковки публикует GitHub Release. Текущая application version хранится в `VERSION`,
а release notes — в `CHANGELOG.md`; tag/version mismatch является hard failure.

Статический §37 gate:

```text
python tools/validate-section37-build-contract.py
TASK-140 SECTION-37 CONTRACT PASS: branches=5/5; prPipeline=8/8; debugExports=4/4; releaseExports=4/4; symbols=1; checksums=1; version=1; changelog=1; jsonSchema=1; migrations=1; warningsAsErrors=1; headlessGodot=1.
```

Подробная branch/release policy: `docs/BUILD_AND_RELEASE.md`. Настройки branch protection
(`quality` и `debug-exports` как required checks) находятся на стороне GitHub. TASK-140/141
приняты владельцем продукта как `VERIFIED`; текущий архив по-прежнему не содержит `.git`,
поэтому repository metadata не приписывается локальной проверке.

### Platform/architecture foundation (TASK-144)

Кодовая база теперь имеет физические границы сборок:

```text
src/Game.Domain/       # domain contracts/policies; без Godot/SQLite
src/Game.Application/  # application orchestration; зависит только от Game.Domain
src/Game.Client/       # Godot host/presentation/adapters; компонует оба слоя
```

Допустимое направление зависимостей: `Game.Domain ← Game.Application ← Game.Client`.
Обратные project references, Godot/SQLite references в Domain/Application и схлопывание
слоёв обратно в один client project блокируются `tools/validate-platform-architecture-contract.py`
и xUnit architecture tests.

Для desktop shipping определены четыре export preset: `Windows Desktop`, `Linux`,
`Windows Desktop Compatibility`, `Linux Compatibility`. Primary профиль использует Mobile/Vulkan;
Compatibility presets добавляют feature `compatibility`, которое переключает project setting на
`gl_compatibility`/`opengl3`. При старте `RendererProfileDiagnostics` печатает фактические renderer
и driver, а TASK-144 F5 probe одновременно подтверждает три разные runtime assemblies. CI/release
создают и primary, и Compatibility artifacts; fallback больше не является только декларацией README.

Статический gate:

```text
TASK-144 PLATFORM/ARCHITECTURE CONTRACT PASS: layers=3/3; domainGodotFree=1; applicationGodotFree=1; projectCycles=0; primaryRenderer=mobile/vulkan; compatibilityRenderer=gl_compatibility/opengl3; desktopPresets=4/4; debugExports=4/4; releaseExports=4/4; runtimeRendererEvidence=1.
```

### §38 Architecture & code-quality hardening (TASK-142)

Section 38 переведён в исполняемый архитектурный контракт. Добавлены Godot-independent
`IDomainEvent` / `IDomainEventBus` и одиннадцать нормативных typed events: `ItemAdded`,
`ItemRemoved`, `ResourceMined`, `PlanetEntered`, `PlanetExited`, `SystemDiscovered`,
`QuestAccepted`, `QuestCompleted`, `ShipDamaged`, `BaseModulePlaced`, `SaveRequested`.
Реальные resource/voyage/galaxy/quest/ship/base/save flows публикуют эти события; Godot
signals не используются как cross-domain business bus.

`SystemFrequencyPolicy` фиксирует 60 Hz physics/player, 10 Hz nearby AI, 2 Hz distant AI,
0.2–1 Hz background economy (shipping default 0.5 Hz) и batched telemetry. Godot project
явно задаёт `physics/common/physics_ticks_per_second=60`; ground NPC и NPC-ship decision
logic работает на 10 Hz, сохраняя physics-rate movement integration, а distant ecology — на
2 Hz. `StructuredGameLogger` накапливает JSONL lines и flush'ит их пакетно, а scene-exit
Main Menu / Developer Workbench / gameplay гарантирует финальный flush.

Все production `Task`/`ValueTask` операции имеют явный `CancellationToken`; SQL ограничен
persistence/developer-inspection boundary, public interfaces XML-документированы, empty
`catch` запрещены, domain runtime/catalog/model не наследуются от `Godot.Node`, worldgen
не запускается напрямую из `_Process`, а application UI не мутирует inventory/crafting
напрямую. Подробный контракт: `docs/ARCHITECTURE_SECTION38.md`.

Статический §38 gate:

```text
python tools/validate-section38-architecture-contract.py
TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=5; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.
```

Section-38 xUnit tests находятся в `tests/ProjectHorizon.Tests/Architecture/Section38ArchitectureTests.cs`;
`tools/run-section37-quality.*`, PR CI и release workflow выполняют новый gate автоматически.

Статические contract gates:

```text
python tools/validate-localization-contract.py
TASK-132 LOCALIZATION CONTRACT PASS: locales=2; keys=1329; parity=1; blanks=0; contentKeys=486; dynamicKeys=60; sourceUiKeys=574; sceneKeys=14; keyOnlyContent=1; sourceSinks=0; legacyLiterals=0.

python tools/validate-audio-contract.py
TASK-134 AUDIO CONTRACT PASS: buses=8/8; cues=19; pool2d=8; pool3d=16; maxTransient=24; maxConcurrent=29; environments=4; musicStates=6; positional=1; attenuation=1; pooling=1; vacuumRule=1; gameplayHooks=6; settingsRouting=1; localization=1; sourceAudioAssets=0.

python tools/validate-developer-diagnostics-contract.py
TASK-136 DEVELOPER DIAGNOSTICS CONTRACT PASS: tools=5/5; commands=16/16; logCategories=14/14; logFields=10/10; devGate=1; seedExplorer=1; planetPreview=1; chunkProfiler=1; saveInspector=1; debugConsole=1; redaction=1.

python tools/validate-section36-testing-contract.py
TASK-138 SECTION-36 CONTRACT PASS: unitGroups=10/10; saveScenarios=8/8; loadScenarios=8/8+abnormal; goldenVersion=2; goldenSystems=4; goldenPoi=20; coverage=80/70/80; visualSmoke=1; standaloneDotnet=1; f5Smoke=1.

python tools/validate-section37-build-contract.py
TASK-140 SECTION-37 CONTRACT PASS: branches=5/5; prPipeline=8/8; debugExports=4/4; releaseExports=4/4; symbols=1; checksums=1; version=1; changelog=1; jsonSchema=1; migrations=1; warningsAsErrors=1; headlessGodot=1.

python tools/validate-section38-architecture-contract.py
TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=5; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.
```

После замены файлов поверх существующей рабочей копии необходимо запускать `tools\clean-build-windows10.cmd`. TASK-146 удаляет stale pre-TASK-144 architecture copies и очищает build outputs всех трёх production layers; в полном логе `CoreCompile` должен реально выполняться для `Game.Domain`, `Game.Application` и `Game.Client`.
Обычный `dotnet build` также запускает `ProjectHorizonSourceHygiene`: он удаляет только известные retired TASK-144 файлы. Неизвестные исходники автоматически не стираются; если `.cs` остаётся в retired `Scripts/Infrastructure/Architecture`, сборка завершается понятным FAIL, чтобы не потерять пользовательский код.


### Прототип A. Персонаж — `VERIFIED`

Реализованы плоская тестовая сцена, `CharacterBody3D`, камера от первого лица, WASD, гравитация, прыжок, столкновения, взаимодействие по `E` и простая hitscan-стрельба по ЛКМ. Сцена сохранена в:

```text
src/Game.Client/Scenes/DebugWorld.tscn
```

Предыдущие функциональные итерации персонажа, столкновений, взаимодействия и простой стрельбы приняты пользователем как `VERIFIED`. Для окончательной репозиторной фиксации остаётся записать SHA контрольного коммита или тега.

### Прототип B. Чанк рельефа — `VERIFIED`

Прототип B завершён и принят по фактическим runtime-проверкам. Реализованы детерминированный noise-рельеф, сетка `3 × 3`, LOD0/LOD1, согласование кромок, глобальные нормали, отдельная collision-сетка, гистерезис, отменяемая фоновая генерация, дозированное main-thread применение и выгрузка ресурсов.

Короткий stress-test `TASK-025` завершён с результатом:

```text
PASS: rev=13, cancel=0, stale=48, 9/9, queue=0, workers=0, errors=0
```

Длительный soak-test `TASK-026` завершён с результатом:

```text
PASS: 121 s, moves=82, managedDelta=0.0 MB, mesh=9, collision=9
```

После soak-test стриминг вернулся в стабильное состояние: `9/9`, `queue=0`, `workers=0/4`, ошибок фоновой генерации нет. Сцена сохранена для регрессии:

```text
src/Game.Client/Scenes/Terrain/TerrainChunkPrototype.tscn
```

### Прототип C. Сферическая планета — `VERIFIED`

Все обязательные критерии PDF-ТЗ подтверждены локальными runtime-проверками:

- cube sphere и совпадение швов граней;
- гравитация к центру и касательное управление;
- ходьба через независимые collision-грани;
- floating origin;
- quadtree LOD-швы;
- отменяемый async visual streaming `L1/L2/L3`;
- динамический topology-complete collision LOD.

Финальная collision-приёмка:

```text
build: 0 errors, 0 warnings
TASK-038 collision (K): PASS
plans=60, commits=60, created=257, unloaded=233, fallback=60
L3=28, gap=0.00 s, rMin=92.46 m, recoveries=0, errors=0
```

После теста сохранены `ground=да`, `floor=да`, `probe=да`, радиальная система
`PASS`, а циклические провалы и подбрасывания отсутствуют.

Регрессионная сцена планеты сохранена в:

```text
src/Game.Client/Scenes/Planet/CubeSpherePrototype.tscn
```

### Диагностический HUD Прототипа C — `VERIFIED`

Панель больше не должна перекрывать весь 3D-холст. По умолчанию используется
компактный HUD размером около `700 × 220 px`.

Клавиша `H` циклически переключает:

1. `COMPACT` — только ключевые visual/collision/player/topology/test показатели;
2. `DETAILED` — вся телеметрия в ограниченной прокручиваемой панели;
3. `HIDDEN` — основная панель скрыта, остаётся небольшой hint `HUD скрыт • H`.

Detailed mode прокручивается колёсиком мыши. Размер обоих видимых режимов
ограничивается текущим viewport, поэтому панель не выходит за границы окна.
Каждое переключение дублируется в Output строкой `Prototype HUD mode: ...`.


### Прототип D. Базовый корабль — `VERIFIED`

Свободный полёт, атмосферный переход, поиск площадки, touchdown/takeoff и
нагрузочный тест 100 последовательных физических посадок приняты runtime.

Финальная soak-приёмка:

```text
TASK-051 soak (V): PASS 100/100
gear=3
vTouch=2,67 м/с
managedDelta=0,02 MiB
nodeDelta=0
build: 0 warnings, 0 errors
```

Регрессионная сцена корабля сохранена в:

```text
src/Game.Client/Scenes/Ship/ShipFlightPrototype.tscn
```

### Прототип E. SQLite save, backup, recovery и migration — `VERIFIED`

Регрессионная сцена persistence-прототипа:

```text
src/Game.Client/Scenes/Persistence/SavePrototype.tscn
```

Все обязательные элементы Прототипа E подтверждены локальной runtime-приёмкой:

- SQLite через `Microsoft.Data.Sqlite 8.0.29`, без Entity Framework;
- один slot — одна БД: `user://profiles/profile_prototype/save_1.db`;
- обязательные PRAGMA, последовательная очередь записи и транзакционный snapshot;
- exact round-trip игрока, корабля, inventory и посещённой планеты;
- валидированная предыдущая копия, атомарное recovery, quarantine и журналы;
- copy migration schema `1→2` с byte-identical сохранением исходной БД;
- безопасные alias/placeholder для неизвестного контента;
- регрессионные `C: PASS`, `X: PASS` и `Z: PASS` при сборке `0/0`.

После приёмки всех пяти прототипов начата производственная ступень persistence.
В `TASK-060` реализован autosave/graceful-exit foundation по разделу 22.8 и
критерию 14 PDF-ТЗ:

- периодический autosave каждые `60` секунд после появления игрового snapshot;
- типизированные причины `Landing`, `Takeoff`, `Hyperspace`,
  `QuestCompleted`, `ShipPurchased`, `BaseChanged` и `GracefulExit`;
- входом worker является только неизменяемый `SaveGameSnapshot`; Godot API не
  вызывается из фоновой операции;
- burst событий объединяется в один batch с сохранением самого нового snapshot;
- запись проходит через существующую единственную очередь `SaveDatabase`;
- событие и revision фиксируются в `logs/save_1.autosave.log`;
- запрос закрытия окна перехватывается, последний snapshot записывается и очередь
  полностью flush-ится до вызова `SceneTree.Quit()`;
- `F6` запускает изолированный тест всех восьми trigger types, coalescing,
  graceful-exit flush, exact round-trip и `integrity_check`.

Управление:

```text
S     сохранить snapshot; предыдущая копия защищается автоматически
L     загрузить snapshot
R     очистить slot, сохранив предыдущую копию
B     создать или обновить валидированный backup
Y     восстановить предыдущую копию с quarantine текущей БД
Z     TASK-054 SQLite foundation acceptance
X     TASK-056 backup/recovery acceptance в изолированной БД
C     TASK-058 schema migration / unknown-content acceptance в изолированной БД
F6    TASK-060 autosave / graceful-exit acceptance в изолированной БД
H     compact / detailed / hidden HUD
```

После каждой команды необходимо дождаться завершения текущей операции. Для
проверки реального штатного выхода сначала создайте snapshot клавишей `S`, затем
закройте игровое окно кнопкой закрытия в заголовке или сочетанием `Alt+F4`:
приложение должно завершиться только после строки `graceful-exit autosave PASS`.
Если slot намеренно пуст, выход ждёт активные persistence-операции, но не создаёт
новый snapshot.

## Состояние реализации ТЗ

Актуальный статус требований, доказательства реализации и очередь следующих задач
ведутся в документе:

[`REQUIREMENTS_STATUS.md`](REQUIREMENTS_STATUS.md)

Требование считается завершённым только после получения статуса `VERIFIED`.

## Структура репозитория

```text
ProjectHorizon/
├── src/
│   ├── Game.Client/
│   │   ├── Scenes/
│   │   ├── Scripts/
│   │   ├── Shaders/
│   │   ├── UI/
│   │   ├── Audio/
│   │   ├── project.godot
│   │   └── Game.Client.csproj
│   ├── Game.Domain/
│   ├── Game.Application/
│   ├── Game.WorldGen/
│   ├── Game.Persistence/
│   ├── Game.Networking/
│   ├── Game.Content/
│   └── Game.Tools/
├── server/
│   ├── Universe.Api/
│   └── Universe.Worker/
├── tests/
│   ├── Game.Domain.Tests/
│   ├── Game.WorldGen.Tests/
│   ├── Game.Persistence.Tests/
│   └── Game.IntegrationTests/
├── content/
│   ├── Items/
│   ├── Biomes/
│   ├── Planets/
│   ├── Ships/
│   ├── Species/
│   ├── Quests/
│   └── Localization/
├── art/
│   ├── Source/
│   ├── Models/
│   ├── Textures/
│   ├── Animations/
│   └── Audio/
├── build/
├── docs/
├── .gitattributes
├── .gitignore
└── README.md
```

Часть каталогов будет добавляться по мере перехода к соответствующим этапам разработки.

## Архитектурные принципы

Проект использует многослойную архитектуру:

1. **Presentation Layer** — сцены, камеры, UI и визуальные эффекты.
2. **Application Layer** — игровые сценарии и координация систем.
3. **Domain Layer** — правила мира и чистая игровая логика.
4. **Infrastructure Layer** — базы данных, файлы, сеть и логирование.
5. **Tools Layer** — внутренние редакторы, генераторы и диагностика.

Процедурная генерация, экономика, предметы, задания и состояние мира не должны напрямую зависеть от `Godot.Node`.

`Game.Domain` не должен содержать ссылок на Godot.

## Запуск проекта

### Требования

Перед запуском должны быть установлены:

- Godot Engine 4.7.1 .NET;
- .NET SDK x64;
- JetBrains Rider или другая IDE с поддержкой C#;
- Git;
- Git LFS.

Проверка .NET SDK:

```powershell
dotnet --info
```

### Запуск через Godot

1. Открыть Godot Project Manager и импортировать:

```text
src/Game.Client/project.godot
```

2. Дождаться импорта ресурсов и выполнить сборку C#.
3. Нажать `F5`: стартует `MainMenu`; выбрать New Game/Continue для входа в gameplay. В запущенном gameplay клавиша `F5` выполняет встроенные acceptance probes текущего vertical slice.
4. Проверить ручной free-flight:
   - W/S — тяга;
   - A/D и Space/C — боковые/вертикальные импульсные двигатели;
   - мышь или стрелки — тангаж/рыскание;
   - Q/E — крен;
   - B — форсаж;
   - X — торможение;
   - G — автоматическая стабилизация;
   - F2 — chase/cockpit camera;
   - R — reset.
5. Нажать `J` и убедиться, что ранее принятый free-flight test остаётся `PASS`.
6. Нажать `P`: корабль перемещается к верхней границе атмосферы. Временный
   radial guidance поддерживает снижение до `blend >= 0,20`, затем отключается;
   повторное `P` возвращает космический spawn.
7. Нажать `L` и убедиться, что принятый atmospheric test остаётся
   `TASK-045 atmosphere (L): PASS`.
8. Нажать `M`: коричневая наклонная площадка и серое препятствие должны быть
   отклонены, зелёная площадка зарезервирована и помечена cyan marker, корабль
   должен перейти в `Aligned` примерно в `12 м` над surface normal. Повторное
   `M` восстанавливает baseline.
9. Нажать `N` и дождаться `TASK-047 landing (N): PASS`.
10. Нажать `O` и убедиться, что `TASK-049 touchdown (O): PASS` виден в HUD.
11. Нажать `V` для soak-теста 100 последовательных посадок; на подтверждённой
    машине ожидаемая продолжительность — около 4–5 минут. Hard timeout рассчитывается
    автоматически; при стандартных параметрах он равен 480 секундам.
12. Клавиша `H` переключает compact, detailed и hidden HUD корабля.
13. Для регрессии Прототипа C открыть
   `Scenes/Planet/CubeSpherePrototype.tscn` через `F6`; compact mode теперь явно
   отключает scrollbar, detailed mode сохраняет прокрутку.
14. Для регрессии Прототипа B открыть
   `Scenes/Terrain/TerrainChunkPrototype.tscn` через `F6`; `F10` запускает
   stress-test, `P` — soak-test.
15. Для повторной проверки Прототипа A открыть `Scenes/DebugWorld.tscn` через `F6`.

### Сборка через командную строку

Из корня репозитория:

```powershell
dotnet build .\src\Game.Client\Game.Client.csproj -c Debug
```

## Первый этап разработки

Разработка начинается с независимых технических прототипов.

### Прототип A. Персонаж

- плоская тестовая сцена;
- управление;
- камера;
- прыжок;
- взаимодействие;
- простая стрельба.

### Прототип B. Чанк рельефа

- noise;
- mesh;
- collision;
- LOD;
- фоновая генерация;
- выгрузка.

### Прототип C. Сферическая планета

- cube sphere;
- гравитация к центру;
- ходьба;
- floating origin;
- устранение швов LOD.

### Прототип D. Корабль — `VERIFIED`

- свободный аркадный полёт — `VERIFIED`;
- тяга, импульсные двигатели, тангаж/рыскание/крен — `VERIFIED`;
- форсаж, торможение, стабилизация и камеры — `VERIFIED`;
- переход `SPACE ↔ ATMOSPHERE` — `VERIFIED`;
- simplified lift, minimum speed, drag и climb limit — `VERIFIED`;
- surface-safety — `VERIFIED`;
- поиск точки, slope/obstacle checks и alignment — `VERIFIED`;
- touchdown, трёхточечные опоры и landed-state — `VERIFIED`;
- контролируемый взлёт и складывание опор — `VERIFIED`;
- soak-test 100 последовательных посадок — `VERIFIED`.

### Прототип E. Сохранение — `VERIFIED`

- SQLite foundation, snapshot и exact round-trip — `VERIFIED`;
- последовательная очередь записи — `VERIFIED`;
- валидированная backup, атомарное recovery и quarantine — `VERIFIED`;
- copy migration schema `1→2` — `VERIFIED`;
- alias/placeholder compatibility для неизвестного контента — `VERIFIED`;
- runtime-приёмка migration/unknown-content и регрессии `C/X/Z` — `VERIFIED`.

Все пять технических прототипов приняты; переход к вертикальному срезу разрешён.
Autosave/graceful-exit foundation следующей производственной ступени имеет статус
`IMPLEMENTED` до локальной приёмки `TASK-061`.

## Правила разработки

- основной язык производственного кода — C#;
- `Nullable` должен быть включён;
- предупреждения компилятора должны устраняться;
- зависимости передаются явно;
- асинхронные операции принимают `CancellationToken`;
- SQL-запросы должны быть параметризованы;
- исключения не подавляются;
- запрещены циклические зависимости проектов;
- Godot Node не используется как доменная модель;
- игровая логика не размещается непосредственно в UI;
- SQL не размещается внутри сцен;
- генерация мира не выполняется непосредственно в `_Process`;
- Godot Signals используются для локального взаимодействия сцены;
- доменные события используются для бизнес-логики.

## Ветки Git

Используемая модель ветвления:

```text
main
develop
feature/*
fix/*
release/*
```

- `main` — стабильное собираемое состояние;
- `develop` — интеграционная ветка разработки;
- `feature/*` — разработка отдельных функций;
- `fix/*` — исправления;
- `release/*` — подготовка выпусков.

Пример создания рабочей ветки:

```powershell
git switch develop
git switch -c feature/player-prototype
```


## Регламент итеративной разработки

Порядок выбора следующей задачи, внесения изменений, проверки, обновления журнала,
подготовки доказательств работоспособности и упаковки архива определён в:

```text
DEVELOPMENT_ITERATION_PROTOCOL.md
```

Краткий запрос для следующей итерации:

```text
Выполни следующую итерацию разработки Project Horizon по регламенту
`DEVELOPMENT_ITERATION_PROTOCOL.md`, PDF-ТЗ и `REQUIREMENTS_STATUS.md`.

Последняя редакция проекта, скачанная с GitHub, приложена к сообщению.
```

Фактические статусы требований и результаты приёмки ведутся только в
`REQUIREMENTS_STATUS.md`.

## Git LFS

Git LFS применяется для крупных бинарных файлов, включая:

- исходные 3D-модели;
- крупные текстуры;
- звуковые файлы;
- видео;
- другие тяжёлые бинарные ресурсы.

Правила LFS хранятся в `.gitattributes`.

## Файлы, не включаемые в Git

В репозиторий не должны попадать:

- `.godot/`;
- `bin/`;
- `obj/`;
- `.idea/`;
- локальные настройки IDE;
- временные сборки;
- локальные базы данных;
- журналы;
- секреты и экспортные учётные данные.

## Лицензия

Лицензия проекта пока не определена.

До выбора лицензии исходный код и материалы проекта считаются закрытыми и не предназначенными для свободного распространения.

## Quality, purity, stability and dismantling

`TASK-093` adds persistent industrial properties to crafted inventory:

- `Quality` is constrained by the recipe quality range;
- `Purity` reflects process conditions, technology tier and hazards;
- `Stability` derives from quality, purity, environment fit and hazards;
- the same recipe and process sequence produce the same values;
- old saves without property metadata load as `100/100/100`.

The PortableFabricator terminal has a fourth `Dismantle` tab. Press `D` from
any terminal tab, or reach it by cycling with `Tab`. The tab lists crafted
items that define `DismantleReturns`, shows `Q/P/S`, recovery efficiency and a
preview of recovered materials. `Enter/E` consumes one item, returns the
quality-scaled materials and requests a `BaseChanged` autosave.

The nine runtime ship-component recipes now define dismantle returns. F1 runs
an additional isolated `TASK-093` acceptance using
`save_1.item-properties-dismantle-test.db`; it checks deterministic property
generation, quality-sensitive partial recovery and exact SQLite round-trip.

## Multi-station refining and Compotium starter line

`TASK-096` expands the playable catalog to sixteen runtime-enabled recipes and
five physical station types. The PortableFabricator keeps the nine one-time
ship-component recipes, while Smelter, Refinery, DistillationColumn and
ChemicalProcessor execute six repeatable refining/chemistry recipes.

All station queues have independent slots and energy, but mirror one shared
player inventory. Intermediate products can move through the chain from
refined ferrite and purified water to Paraffinium lubricant and Compotium
concentrate. The complete queue network is stored in
`save_settings.production_queue_network`; legacy single-queue saves remain
loadable. Gameplay energy recharges to station capacity over sixty active
seconds and does not advance while the game is closed.

`TASK-098` replaces the legacy single-queue HUD diagnostic with a read-only
projection of the complete `ProductionNetworkRuntime`. The HUD aggregates five
physical stations, job states and energy, shows per-station `[R/Q/P]` counters,
and treats an initialized network with zero jobs as available. F1 validates the
projection, transitions, recharge, cold restore, legacy fallback and SQLite
integrity in `save_1.production-network-hud-test.db`.


## Catalog-wide resource lifecycle closure

`TASK-100` closes the vertical-slice resource subsystem against the fixed v2 baseline. Every one of the 42 world-resource definitions is represented by a physical generic node. Missing scene types are generated deterministically, while existing authored nodes and their stable IDs remain unchanged for save compatibility. Collection, duplicate rejection, available inventory, station mirrors, depletion, cold restore and reset are covered by the isolated F7 acceptance database `save_1.resource-lifecycle-test.db`. No SQLite schema migration is introduced. After `TASK-101` runtime acceptance, further functional iterations may consume the established resource API but should not add separate resource-lifecycle mechanics unless a confirmed defect requires it.

## Stage 1 station-services closure

`TASK-102` adds the complete Stage 1 station-services vertical-slice block: six economy types, three data-driven factions, one physical trader, template dialogue, catalog-wide market pricing, credits and reputation, and three persistent quest graphs. Every one of the 174 catalog items is quotable through the six-factor price formula. Buy/sell operations synchronize the player session and all five production inventory mirrors. The optional `station_services` SQLite setting stores credits, reputation, economy day, stock and quest-node state without increasing schema version 2; legacy saves remain loadable. F3 runs the isolated `save_1.station-services-test.db` acceptance alongside the existing research test. Full galaxy NPC populations, procedural quest generation and inter-system economies remain later-stage features, not unfinished work in this Stage 1 subsystem.



### Base construction closure iteration

`TASK-106` adds a 50-module, 17-category data-driven base-construction runtime matching PDF section 20: cardinal snapping, overlap and disconnection rejection, per-base limits, a graph-based electric network with generators/batteries/consumers and switchable devices, static collisions, dynamic lights, dismantle refunds, autosave/cold restore, legacy fallback and F8 reset. F6 runs the isolated SQLite acceptance in parallel with the existing fourth-path regression. The coordinate overlay is preserved across detailed, compact and hidden HUD modes.

### Planetary exploration and discovery closure

`TASK-108` closes the Stage 1 planetary exploration loop with a strict
`planetary_pois.json` catalog containing exactly 20 POI types, including all
15 types required by PDF section 21. The deterministic planner evaluates
biome, slope, height, water distance, danger, rarity, quest tags, pairwise
spacing and vertical-slice infrastructure clearance. One physical
`StaticBody3D` is generated for every POI type without changing planetary
terrain geometry.

Press `P` to pulse the scanner. A POI must normally be scanned before `E`
can resolve its interaction; scan-only POIs complete during the pulse. Press
`J` to open the persistent discovery catalog, use `Up/Down` to browse and `N`
to assign a deterministic waypoint name to a discovered, nameable object.
Discovery points, discovered/resolved state and custom names are saved in the
optional `save_settings.planetary_exploration` value. SQLite schema remains
version 2 and old saves load with an empty discovery state.

`F4` preserves the complete Industry Content v2 structural acceptance and
also runs `TASK-108` against the isolated database
`save_1.planetary-exploration-test.db`. The command uses an event-silence
gate: every F4 key-down, key-up and repeat packet refreshes the gate. A new
run is permitted only after an actual release packet was the last F4 event,
the previous acceptance has completed and no further F4 event has been
observed for at least 750 ms. A subsequent press or repeat cancels the pending
release. This does not depend on platform-specific physical-key polling and
blocks synthetic release / non-echo repeat sequences while the key is held. One held press therefore
produces exactly one TASK-080/TASK-108 acceptance pair. The test verifies 20 deterministic
placements, environment constraints, symmetric spacing, infrastructure
clearance, quest bias, complete scan/resolve/naming flow, cold restore, legacy
fallback, exact round-trip, one-writer discipline and SQLite integrity.


### Ship systems, loadout and damage closure

`TASK-110` закрывает core-подсистему корабельных классов, модулей и повреждений, требуемую ТЗ v2.0 §14.2–14.3. `ships.json` содержит шесть class profiles с Hull, Shield, CargoCapacity, FuelCapacity, Acceleration, MaxSpeed, Maneuverability, WeaponSlots, TechnologySlots, HyperdriveRange и AtmosphericEfficiency; семь system definitions; восемнадцать module definitions, полностью совпадающих с category `ShipModule` outputs.

После ремонта starter ship нажмите `U` на поверхности. В Modules установка по `Enter/E` атомарно потребляет один предмет из shared inventory; `X` снимает модуль и возвращает его. В Systems клавиша `D` наносит контролируемое тестовое повреждение, `R` расходует заданный системой repair component. Overview позволяет заправить корабль высокоэнергетическим топливом. Повреждённые engine/impulse/landing/hull блокируют flight readiness, повреждённый hyperdrive или его module — hyperspace readiness, а affected modules перестают давать stat bonuses до ремонта.

Snapshot хранит class ID, commissioned flag, fuel, slot installations и exact system health в `save_settings.ship_systems`; `ships.fuel` синхронизируется для совместимости. Старые saves без блока получают состояние, согласованное с сюжетным starter repair. Покупка и смена класса корабля остаются отдельной будущей функцией.

### Stage 1 repair-to-station voyage closure

`TASK-112` соединяет ранее изолированные vertical-slice системы в обязательный сквозной цикл Этапа 1. После `StarterRepairQuestCompleted` повторное `E` у корабля передаёт управление встроенному экземпляру `ArcadeShip.tscn`. Контроллер получает acceleration, max speed и angular response из текущих effective ship stats; модульные бонусы и повреждения поэтому влияют не только на интерфейс, но и на фактический полёт.

Основной маршрут:

```text
repair ship → E board → T takeoff → fly/navigation assist to orbital dock
→ Enter dock → E open station services → T undock
→ return to planet approach → Enter land → E disembark
```

Физическая орбитальная станция, docking marker и planet approach marker находятся в той же vertical-slice сцене. `K` включает deterministic navigation assist к текущей цели, но не телепортирует корабль и использует тот же controller input path. Docking требует допустимой дистанции и скорости; landing дополнительно требует исправной Landing system. Каждая фазовая операция расходует fuel и отклоняется при недостаточном запасе. На станции повторно используется уже принятая панель `STATION SERVICES` с торговлей и заданиями.

`save_settings.stage_one_voyage` хранит location, piloted flag, station visit, counters, exact ship pose/velocity и checkpoint. `ships` row получает ту же позицию для cross-table validation. Cold restore возвращает игрока в кабину и в точную фазу полёта без offline progress; legacy saves получают surface/not-piloted state. `F8` очищает voyage вместе с остальными gameplay-данными. SQLite schema остаётся `2`.

После runtime-приёмки `TASK-112` изменять этот контур следует только при интеграции полноценной планетарно-космической смены сцен, межсистемных перелётов или новых типов станций; повторно реализовывать boarding, readiness/fuel gates, docking/landing lifecycle и persistence не требуется.

### Procedural galaxy, maps and hyperspace (`TASK-114`)

После полного Stage 1 loop приобрести и установить `module.ship.hyperspace_core` либо `module.ship.compotium_drive_core`, состыковаться с orbital station и нажать `M`. Galaxy tab показывает nearby systems, sector coordinates, star type, прямую distance, количество waypoint jumps и `VISITED/NEW`; System tab показывает все planets текущей системы. `Up/Down` меняет selection, `Enter` строит route и выполняет следующий waypoint. Jump отклоняется на поверхности, в полёте, без commissioning, при `flightReady=0`, с повреждённым hyperdrive, без активного hyperspace module, при недостатке fuel или отсутствии range-aware route. После успешного jump ship остаётся piloted и docked у station checkpoint новой системы; station services доступны без нового economy runtime. Штатное завершение и cold restore обязаны сохранять exact system/sector/destination/jump/distance/visited state. `F8` возвращает `galaxy.g1/system.vertical_slice`, `visited=1`, `jumps=0`.

## Procedural planetary ecology closure

`TASK-116` adds the Stage 2-ready planetary ecology core required by PDF v2.0
sections 11–12 and the Stage 2 content baseline. `Content/ecology.json` defines
16 biomes, 60 flora modules and 20 fauna archetypes split into 12 terrestrial,
4 flying and 4 aquatic species. All six required fauna body plans are covered.
The runtime regenerates populations deterministically from `WorldSeed` and
`RegionKey` instead of serializing every plant or animal.

Repeated vegetation is rendered by `MultiMeshInstance3D` groups. Only nearby
flora specimens are promoted to interactive `StaticBody3D` nodes for scan,
harvest and damage interaction. Fauna is capped at 20 fully active local
`CharacterBody3D` agents plus 80 statistical/simplified population entries.
Nearby AI evaluates at 10 Hz, medium-range AI at 4 Hz and distant fauna remains
statistical. The utility/steering runtime covers Idle, Wander, Graze, Drink,
Sleep, Investigate, Flee, Threaten, Attack, ReturnToTerritory and FollowGroup.

On foot:

```text
V          scan the nearest flora/fauna signal within 16 m
O          open/close the ecology catalogue
Tab        switch Flora/Fauna inside the catalogue
Up/Down    browse discovered species
E          harvest an interactable promoted flora specimen
```

Harvesting yields `resource.flora_pulp`. Discovery species IDs and removed flora
instance IDs are persisted in the optional `save_settings.ecology` value. No
procedural fauna instance pose/state is stored. SQLite schema remains version 2;
legacy saves regenerate ecology from the catalog seed with empty discovery and
harvest deltas.

`F5` now runs `TASK-116` in the isolated
`save_1.ecology-test.db` alongside the existing runtime/ship/voyage/galaxy
acceptance. The ecology test checks the 16/60/20 baseline, 12/4/4 movement
coverage, six body plans, all eleven behavior states, deterministic placement,
MultiMesh-oriented flora population, 20/80 population limits, update tiers,
utility behavior, discovery/harvest lifecycle, delta-only persistence, all 16
biomes, cold restore, legacy fallback, exact SQLite round-trip, one-writer
discipline and integrity.

The integrated `VoyageShip` now has a concrete `Gameplay/AtmospherePlanet` target for its default `../AtmospherePlanet` reference. The product owner explicitly waived the remaining ecology/runtime acceptance, so disappearance of the prior `Arcade ship has no atmosphere reference` warning is not claimed as independently verified in this prepared snapshot.


## Procedural mission system closure

`TASK-118` implements the repeatable mission system required by PDF v2.0 §19 without replacing hand-authored story content. The generated board contains exactly 20 deterministic offers and supports all 15 objective types in the domain model. Gameplay generation uses capability-gated feasibility: objectives are built only from resources, runtime-enabled craft outputs, attainable resource/craft items, base modules, real POIs/species, reachable first planets of nearby systems and existing NPCs. Combat/protection objectives now use the physical hostile/protected NPC targets introduced by TASK-122, so the current gameplay board covers all 15 objective types with feasibility against real IDs.

On foot outside other UI surfaces:

```text
Q          open/close procedural mission journal
Up/Down    select mission
Enter      accept / deliver / return / claim
Esc        close
```

The mission graph is `Objective -> Return (when required) -> Claim`. Accepting, progressing, returning and claiming are persistent. Rewards grant credits through the existing Station Services economy; completed-state faction reputation remains deterministic and restorable. Mission progress is hooked to resource collection, runtime crafting/production, trade, starter/system repair, base construction, POI scan/resolve, ecology scan/harvest, planetary landing and hyperspace system exploration. `DeliverItem` consumes the exact shared inventory quantity at the giver. `ReturnToNpc` and return-required nodes advance only at the real trader/orbital service checkpoint.

`save_settings.procedural_quests` stores only non-default board deltas (status/progress) plus seed/revision; the 20 definitions are regenerated from content and seed on load. SQLite schema remains 2 and legacy saves receive a fresh zero-progress board. `F8` resets the board. `F5` uses `save_1.procedural-quests-test.db` and validates exact 15-type support, deterministic generation, feasibility rejection, active limit, full state-graph lifecycle, reward integrity, a playable current board with real DefeatTarget/ProtectTarget NPC IDs, cold restore, legacy fallback, round-trip, autosave log, one-writer discipline and SQLite integrity.
