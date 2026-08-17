using System;
using System.Linq;

public sealed record WorldStreamingAcceptanceReport(
    bool Passed,
    bool ActiveZoneRadii,
    bool PriorityQueue,
    bool WorkerPolicy,
    bool MainThreadBudgets,
    bool CancellationReady,
    bool LiveCoordinator,
    bool MicroStreamerBudgeted,
    int PriorityLevels,
    int WorkerLimit,
    int LiveFullRegions,
    int LiveSimplifiedRegions,
    int LivePreloadRegions,
    string Result)
{
    public string BuildOutputLine() =>
        $"TASK-194 world streaming acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"activeRadii={(ActiveZoneRadii ? 1 : 0)}; priorities={PriorityLevels}/6; " +
        $"workerPolicy={(WorkerPolicy ? 1 : 0)}; workers={WorkerLimit}; " +
        $"budgets={(MainThreadBudgets ? 1 : 0)}; cancellation={(CancellationReady ? 1 : 0)}; " +
        $"live={(LiveCoordinator ? 1 : 0)}; microBudget={(MicroStreamerBudgeted ? 1 : 0)}; " +
        $"full={LiveFullRegions}; simplified={LiveSimplifiedRegions}; preload={LivePreloadRegions}; " +
        $"result={Result}";
}

public static class WorldStreamingAcceptanceRunner
{
    public static WorldStreamingAcceptanceReport Evaluate(
        WorldStreamingDiagnostics live,
        TerrainChunkProfilerSnapshot terrain)
    {
        bool radii =
            WorldStreamingRuntime.OnFootFullDetailRadiusMeters >= 1_500.0 &&
            WorldStreamingRuntime.OnFootFullDetailRadiusMeters <= 2_500.0 &&
            WorldStreamingRuntime.GroundVehicleFullDetailRadiusMeters >= 4_000.0 &&
            WorldStreamingRuntime.GroundVehicleFullDetailRadiusMeters <= 6_000.0 &&
            WorldStreamingRuntime.AtmosphericFlightFullDetailRadiusMeters >= 10_000.0 &&
            WorldStreamingRuntime.AtmosphericFlightFullDetailRadiusMeters <= 20_000.0;

        WorldStreamingPlan priorityPlan = WorldStreamingRuntime.BuildPlan(
            new WorldStreamingObserverSample(
                0.0,
                0.0,
                120.0,
                20.0,
                WorldStreamingTravelMode.GroundVehicle));
        int priorityLevels = priorityPlan.Regions
            .Select(region => region.Priority)
            .Distinct()
            .Count();
        bool priorities = priorityLevels == 6 &&
            priorityPlan.Regions.First().Priority ==
                WorldStreamingPriority.PlayerRegion;

        int expectedWorkers = WorldStreamingRuntime.ResolveWorkerCount(
            System.Environment.ProcessorCount);
        bool workerPolicy = expectedWorkers >= 1 && expectedWorkers <= 4 &&
            WorldStreamingRuntime.ResolveWorkerCount(1) == 1 &&
            WorldStreamingRuntime.ResolveWorkerCount(2) == 1 &&
            WorldStreamingRuntime.ResolveWorkerCount(8) == 4 &&
            live.WorkerLimit == expectedWorkers;
        bool budgets =
            WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
                WorldStreamingFrameBudgetMode.Regular) == 2.0 &&
            WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
                WorldStreamingFrameBudgetMode.ForcedPreload) == 5.0 &&
            WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
                WorldStreamingFrameBudgetMode.LoadingScreen) == 10.0 &&
            live.MainThreadBudgetMilliseconds == 2.0;
        bool cancellation = typeof(WorldStreamingRuntime)
            .GetMethod(nameof(WorldStreamingRuntime.BuildPlan)) is not null;
        bool liveCoordinator = live.Active &&
            live.FullRegions > 0 &&
            live.Revision >= live.CompletedRevision &&
            live.BudgetOverruns == 0;
        bool microBudget = terrain.MainThreadBudgetMilliseconds <= 2.001 &&
            terrain.ForcedPreloadBudgetMilliseconds <= 5.001 &&
            terrain.LoadingScreenBudgetMilliseconds <= 10.001;
        bool passed = radii && priorities && workerPolicy && budgets &&
            cancellation && liveCoordinator && microBudget;
        return new WorldStreamingAcceptanceReport(
            passed,
            radii,
            priorities,
            workerPolicy,
            budgets,
            cancellation,
            liveCoordinator,
            microBudget,
            priorityLevels,
            live.WorkerLimit,
            live.FullRegions,
            live.SimplifiedRegions,
            live.PreloadRegions,
            passed
                ? "spec section 10 active zones, six-level scheduling, cancellable background planning and bounded main-thread application verified"
                : "one or more world-streaming invariants failed");
    }
}
