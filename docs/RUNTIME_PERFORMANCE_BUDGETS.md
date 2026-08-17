# TASK-200 — Runtime Performance Budgets, Frame Telemetry & Adaptive Quality

## Scope

TASK-200 implements the measurable runtime side of Technical Specification §27. It does not claim that every hardware configuration is already performance-verified. Instead it makes the specification budgets explicit, samples the live engine, reports overruns, and applies bounded presentation-only degradation when sustained pressure is observed.

## Normative profiles

Medium targets 60 FPS with a 16.6 ms CPU/GPU frame budget and the §27.3 scene limits: 1500 draw calls, 2,000,000 rendered primitives, 500 active physics bodies, 20 full AI, 80 simplified AI, 4 GiB video memory, 6 GiB process memory and less than 256 KiB managed allocations per ordinary frame.

Low targets 30 FPS / 33.3 ms. Scene, AI, memory and allocation budgets are reduced by at least 30 percent. Compatibility rendering selects Low automatically; Mobile/RenderingDevice selects Medium.

## Live telemetry

`RuntimePerformanceTelemetryRuntime` samples at `SystemFrequencyPolicy.TelemetryFlushHz` (2 Hz), not every frame. It reads Godot Performance monitors for:

- FPS and process-frame time;
- physics and navigation process time;
- draw calls and rendered primitives;
- video memory;
- active 3D physics objects;
- node/resource counts;
- engine static memory when available.

It supplements these with `.NET` `Environment.WorkingSet` and `GC.GetTotalAllocatedBytes(false)`, amortized over frames since the previous sample.

The AI counters represent per-agent work that is actually scheduled at runtime: `Near` fauna plus physical NPC/ship agents count as full AI, while `MidHigh`/`MidLow` fauna count as simplified AI. TASK-198's far statistical population is deliberately excluded because it is aggregated species-level simulation rather than 80 individually ticking AI agents.

Godot does not expose a portable runtime GPU-frame-time monitor through this contract. The normative GPU frame budget is therefore retained in policy and must be verified with the Godot/GPU profiler during owner profiling. TASK-200 does not fabricate GPU timing from frame delta. Observed FPS is nevertheless evaluated as an independent budget signal, so a render/GPU bottleneck that depresses frame rate can still drive presentation-only adaptation even when the sampled CPU frame time is within its limit.

## Adaptive governor

The governor is hysteretic:

- 4 consecutive over-budget samples → `Constrained`;
- 12 consecutive over-budget samples or pressure >= 1.35× → `Critical`;
- 20 clean samples step down one quality level.

Only presentation may change:

- regional vegetation LOD/cull distances contract earlier;
- cloud layers are capped and the optional second layer opacity is reduced.

The governor never changes collision, player/ship physics, save state, quest/economy results, authoritative AI frequencies, resource placement or procedural seeds.

## F5 acceptance

F5 evaluates the exact Medium/Low policy, the >=30% Low reduction, synthetic overrun detection, managed-allocation budget, hysteresis/recovery behavior, presentation-only hooks and existence of a live telemetry sample.

A live `budgetStatus=OVER...` is diagnostic evidence and does not by itself make the instrumentation acceptance fail: editor overhead, debug monitors and a particular machine can legitimately exceed a target. Actual performance certification remains a measured owner profiling exercise.
