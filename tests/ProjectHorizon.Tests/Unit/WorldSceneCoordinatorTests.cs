namespace ProjectHorizon.Tests.Unit;

public sealed class WorldSceneCoordinatorTests
{
    [Fact]
    public void TransitionGraph_CoversSurfaceOrbitStationAndHyperspace()
    {
        WorldSceneCoordinatorRuntime runtime = new(
            WorldSceneContext.Create(
                WorldSceneKind.Surface,
                "system.alpha",
                "planet.alpha.0"));

        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            runtime.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.Orbit,
                    "system.alpha",
                    "planet.alpha.0"),
                out _));
        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            runtime.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.StationInterior,
                    "system.alpha",
                    "planet.alpha.0"),
                out _));
        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            runtime.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.HyperspaceTransit,
                    "system.alpha",
                    "planet.alpha.0"),
                out _));
        Assert.Equal(
            WorldSceneTransitionResult.Applied,
            runtime.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.StationInterior,
                    "system.beta",
                    "planet.beta.0"),
                out _));
        Assert.Equal("system.beta", runtime.Current.SystemId);
        Assert.Equal(WorldSceneKind.StationInterior, runtime.Current.Kind);
        Assert.Equal(4, runtime.TransitionCount);
        Assert.Equal(1, runtime.HyperspaceTransitions);
    }

    [Fact]
    public void DirectSurfaceToStation_IsRejectedWithoutMutatingContext()
    {
        WorldSceneContext surface = WorldSceneContext.Create(
            WorldSceneKind.Surface,
            "system.alpha",
            "planet.alpha.0");
        WorldSceneCoordinatorRuntime runtime = new(surface);

        WorldSceneTransitionResult result = runtime.TryTransition(
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.alpha",
                "planet.alpha.0"),
            out string message);

        Assert.Equal(WorldSceneTransitionResult.Rejected, result);
        Assert.Equal(surface, runtime.Current);
        Assert.Equal(1, runtime.RejectedTransitions);
        Assert.Contains("not allowed", message);
    }

    [Fact]
    public void ContextIds_AreNormalizedAndBlankIdsAreRejected()
    {
        WorldSceneContext context = WorldSceneContext.Create(
            WorldSceneKind.Orbit,
            " system.alpha ",
            " planet.alpha.0 ");

        Assert.Equal("system.alpha", context.SystemId);
        Assert.Equal("planet.alpha.0", context.PlanetId);
        Assert.Throws<ArgumentException>(() =>
            WorldSceneContext.Create(WorldSceneKind.Surface, " ", "planet.alpha.0"));
        Assert.Throws<ArgumentException>(() =>
            WorldSceneContext.Create(WorldSceneKind.Surface, "system.alpha", ""));
    }
}
