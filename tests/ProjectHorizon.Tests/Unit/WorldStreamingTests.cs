namespace ProjectHorizon.Tests.Unit;

public sealed class WorldStreamingTests
{
    [Theory]
    [InlineData(WorldStreamingTravelMode.OnFoot, 2000.0)]
    [InlineData(WorldStreamingTravelMode.GroundVehicle, 5000.0)]
    [InlineData(WorldStreamingTravelMode.AtmosphericFlight, 15000.0)]
    public void ActiveZoneRadiusMatchesSpecProfile(
        WorldStreamingTravelMode mode,
        double expected)
    {
        Assert.Equal(expected,
            WorldStreamingRuntime.ResolveFullDetailRadiusMeters(mode));
    }

    [Fact]
    public void MovingPlanContainsAllSixPriorityClasses()
    {
        WorldStreamingPlan plan = WorldStreamingRuntime.BuildPlan(
            new WorldStreamingObserverSample(
                0.0,
                0.0,
                100.0,
                15.0,
                WorldStreamingTravelMode.GroundVehicle));

        Assert.Equal(6, plan.Regions.Select(region => region.Priority).Distinct().Count());
        Assert.Equal(WorldStreamingPriority.PlayerRegion, plan.Regions[0].Priority);
        Assert.Contains(plan.Regions, region =>
            region.Detail == WorldStreamingRegionDetail.Preload);
    }

    [Fact]
    public void WorkerAndMainThreadBudgetsMatchSection10()
    {
        Assert.Equal(1, WorldStreamingRuntime.ResolveWorkerCount(1));
        Assert.Equal(1, WorldStreamingRuntime.ResolveWorkerCount(2));
        Assert.Equal(4, WorldStreamingRuntime.ResolveWorkerCount(8));
        Assert.Equal(2.0, WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
            WorldStreamingFrameBudgetMode.Regular));
        Assert.Equal(5.0, WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
            WorldStreamingFrameBudgetMode.ForcedPreload));
        Assert.Equal(10.0, WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(
            WorldStreamingFrameBudgetMode.LoadingScreen));
    }

    [Fact]
    public void BackgroundPlanSupportsCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            WorldStreamingRuntime.BuildPlan(
                new WorldStreamingObserverSample(
                    0.0, 0.0, 1.0, 0.0,
                    WorldStreamingTravelMode.AtmosphericFlight),
                source.Token));
    }
}
