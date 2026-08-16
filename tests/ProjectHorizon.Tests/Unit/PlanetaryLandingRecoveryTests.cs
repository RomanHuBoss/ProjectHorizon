namespace ProjectHorizon.Tests.Unit;

public sealed class PlanetaryLandingRecoveryTests
{
    [Fact]
    public void PlanetaryLandingRecovery_ModelContractPasses()
    {
        PlanetaryLandingRecoveryAcceptanceReport report =
            PlanetaryLandingRecoveryAcceptanceRunner.Run();

        Assert.True(report.Passed, report.Result);
        Assert.True(report.RestoreSafe);
        Assert.True(report.PlanetScale);
        Assert.True(report.MoonClearance);
        Assert.True(report.OrbitalEntry);
        Assert.True(report.SurfaceHandoff);
        Assert.True(report.VoyagePath);
        Assert.True(report.LightingContinuity);
        Assert.True(report.MinimumPlanetVisualRadius >= 520.0);
    }

    [Fact]
    public void OrbitalEntryCapture_RequiresBothRangeAndSpeed()
    {
        Assert.True(PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
            PlanetaryApproachRuntime.OrbitalEntryCaptureRadiusMeters,
            PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed));
        Assert.False(PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
            PlanetaryApproachRuntime.OrbitalEntryCaptureRadiusMeters + 1.0,
            0.0));
        Assert.False(PlanetaryApproachRuntime.IsOrbitalEntryCaptureReady(
            0.0,
            PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed + 1.0));
    }

    [Fact]
    public void PersistedStationContext_CanRestoreWithoutIllegalGameplayEdge()
    {
        WorldSceneCoordinatorRuntime runtime = new(
            WorldSceneContext.Create(
                WorldSceneKind.Surface,
                "system.alpha",
                "planet.alpha"));

        runtime.Restore(WorldSceneContext.Create(
            WorldSceneKind.StationInterior,
            "system.alpha",
            "planet.alpha"));

        Assert.Equal(WorldSceneKind.StationInterior, runtime.Current.Kind);
        Assert.Equal(0, runtime.RejectedTransitions);
        Assert.False(WorldSceneCoordinatorRuntime.IsAllowedTransition(
            WorldSceneContext.Create(
                WorldSceneKind.Surface,
                "system.alpha",
                "planet.alpha"),
            WorldSceneContext.Create(
                WorldSceneKind.StationInterior,
                "system.alpha",
                "planet.alpha")));
    }
}
