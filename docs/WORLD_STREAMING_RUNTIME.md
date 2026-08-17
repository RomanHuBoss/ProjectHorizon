# TASK-194 — World Streaming Runtime

TASK-194 implements Technical Specification §10 as a two-tier streaming architecture.

## Active-zone profiles

Macro regions are 1 km cells. Full-detail radii are deliberately chosen inside the specification envelopes:

- on foot: 2.0 km (required 1.5–2.5 km);
- ground vehicle: 5.0 km (required 4–6 km);
- atmospheric flight: 15.0 km (required 10–20 km).

Outside the full-detail ring, the coordinator retains simplified macro-region descriptors. A narrow movement-directed preload corridor extends beyond the simplified ring. This macro residency must not be confused with the collision terrain window: TASK-158 retains 25 local micro chunks (9 collision chunks) around the current observer so the engine never keeps thousands of 32 m collision meshes resident merely to satisfy the kilometre-scale visibility requirement.

## Priority queue

The plan is ordered exactly as §10.2:

1. player region;
2. regions in the direction of movement;
3. collision regions;
4. visible full-detail regions;
5. far/simplified regions;
6. pre-generation regions.

The existing TerrainChunkManager now applies the same first four priorities to its micro-chunk work queue. Far and pre-generation tiers are owned by WorldStreamingCoordinatorNode.

## Threads and Godot ownership

WorldStreamingRuntime.BuildPlan is data-only and may execute on a .NET worker. It returns records and arrays only. The worker never creates Nodes, Mesh resources, CollisionShape3D objects, touches SceneTree or submits GPU resources.

The worker-count policy is:

`max(1, min(4, logical_processor_count - 2))`

Each new macro-plan revision owns a CancellationTokenSource. A new revision cancels obsolete planning before the next plan is accepted.

## Main-thread budgets

The coordinator and terrain micro-streamer enforce time-sliced application:

- regular frame: 2 ms;
- forced preload: 5 ms;
- loading screen: 10 ms.

A single Godot resource upload cannot be pre-empted mid-call, so elapsed time is checked before every additional apply/remove operation. Excess queued work is carried to later ticks rather than drained in one frame.

## Runtime integration

On-foot gameplay uses the player logical surface position. Piloted surface/atmospheric gameplay uses the ship and the 15 km profile. While inside an isolated TASK-192 cave, the macro observer remains anchored at the exterior cave entrance so the surrounding surface region stays resident. Surface streaming suspends when PlanetRuntime is inactive.

## Acceptance

F5 must include:

`TASK-194 (F5): PASS ... pri=6 budget=2ms`

and Output:

`TASK-194 world streaming acceptance PASS: activeRadii=1; priorities=6/6; workerPolicy=1; budgets=1; cancellation=1; live=1; microBudget=1; ...`

Clean-build and runtime acceptance remain owner gates when dotnet/Godot are unavailable in the packaging environment.
