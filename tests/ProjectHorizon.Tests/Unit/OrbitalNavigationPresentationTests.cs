namespace ProjectHorizon.Tests.Unit;

public sealed class OrbitalNavigationPresentationTests
{
    [Fact]
    public void StarterSystem_UsesReadableOrbitalSpacingAndCadence()
    {
        GalaxyNavigationRuntime galaxy = new();
        OrbitalNavigationPresentationAcceptanceReport report =
            OrbitalNavigationPresentationAcceptanceRunner.Run(
                galaxy.CurrentSystem,
                stationInteriorReady: true);

        Assert.True(report.Passed, report.Result);
        Assert.True(report.RealTimeOrbitClock);
        Assert.True(report.PlanetSpacing);
        Assert.True(report.MoonCadence);
        Assert.True(report.VisualHierarchy);
        Assert.True(report.AssistDockCapture);
        Assert.True(report.LocalProxyPolicy);
        Assert.True(report.SpaceEnvironment);
        Assert.InRange(report.MinimumPlanetOrbit, 1800.0, double.MaxValue);
        Assert.InRange(report.MinimumPlanetSpacing, 1000.0, double.MaxValue);
        Assert.InRange(report.MinimumMoonRealPeriod, 1200.0, double.MaxValue);
    }

    [Fact]
    public void DockingCapture_RequiresBothRangeAndSafeSpeed()
    {
        Assert.True(StageOneVoyageRuntime.IsDockingCaptureReady(
            StageOneVoyageRuntime.DockingRangeMeters,
            StageOneVoyageRuntime.MaximumDockingSpeed));
        Assert.False(StageOneVoyageRuntime.IsDockingCaptureReady(
            StageOneVoyageRuntime.DockingRangeMeters + 0.1,
            0.0));
        Assert.False(StageOneVoyageRuntime.IsDockingCaptureReady(
            0.0,
            StageOneVoyageRuntime.MaximumDockingSpeed + 0.1));
    }

    [Fact]
    public void GeneratedSystems_KeepPlanetaryBodiesDistinctFromMoons()
    {
        GalaxyNavigationRuntime galaxy = new();
        for (int x = -2; x <= 2; x++)
        {
            GalaxySystemDefinition system = galaxy.GenerateSystem(x, 0, 1);
            OrbitalNavigationPresentationAcceptanceReport report =
                OrbitalNavigationPresentationAcceptanceRunner.Run(
                    system,
                    stationInteriorReady: true);
            Assert.True(report.Passed, $"{system.SystemId}: {report.Result}");
        }
    }
    [Theory]
    [InlineData(WorldSceneKind.Orbit)]
    [InlineData(WorldSceneKind.InterplanetaryTransit)]
    [InlineData(WorldSceneKind.HyperspaceTransit)]
    [InlineData(WorldSceneKind.StationInterior)]
    public void NonSurfaceWorlds_UseDarkFogFreeEnvironment(WorldSceneKind kind)
    {
        WorldSceneEnvironmentPresentationProfile profile =
            WorldSceneEnvironmentPresentationRuntime.Resolve(kind);

        Assert.False(profile.SurfaceOwned);
        Assert.False(profile.FogEnabled);
        Assert.True(
            WorldSceneEnvironmentPresentationRuntime.IsVacuumProfileValid(profile));
    }

}
