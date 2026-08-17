namespace ProjectHorizon.Tests.Unit;

public sealed class AcceptanceCoherenceHotfixTests
{
    [Fact]
    public void StarSystemRepresentationClassifier_CoversAllLevelsWithoutOrbitalPhaseDependence()
    {
        Assert.Equal(
            StarSystemRepresentation.Proxy,
            StarSystemSimulationRuntime.ResolveRepresentationForDistance(
                StarSystemBodyKind.Planet,
                StarSystemSimulationRuntime.ProxyDistance * 0.5));
        Assert.Equal(
            StarSystemRepresentation.Marker,
            StarSystemSimulationRuntime.ResolveRepresentationForDistance(
                StarSystemBodyKind.Planet,
                (StarSystemSimulationRuntime.ProxyDistance +
                 StarSystemSimulationRuntime.MarkerDistance) * 0.5));
        Assert.Equal(
            StarSystemRepresentation.Statistical,
            StarSystemSimulationRuntime.ResolveRepresentationForDistance(
                StarSystemBodyKind.Planet,
                StarSystemSimulationRuntime.MarkerDistance + 1.0));
        Assert.Equal(
            StarSystemRepresentation.DetailedPlanet,
            StarSystemSimulationRuntime.ResolveRepresentationForDistance(
                StarSystemBodyKind.Planet,
                0.0,
                detailedPlanet: true));
        Assert.Equal(
            StarSystemRepresentation.Marker,
            StarSystemSimulationRuntime.ResolveRepresentationForDistance(
                StarSystemBodyKind.ShipContact,
                StarSystemSimulationRuntime.ProxyDistance * 0.5));
    }

    [Fact]
    public void StarterSystem_VisualHierarchyComparesEveryMoonWithItsOwnParent()
    {
        GalaxyNavigationRuntime galaxy = new();
        OrbitalNavigationPresentationAcceptanceReport report =
            OrbitalNavigationPresentationAcceptanceRunner.Run(
                galaxy.CurrentSystem,
                stationInteriorReady: true);

        Assert.True(report.VisualHierarchy, report.Result);
        Assert.True(report.Passed, report.Result);
    }

    [Fact]
    public void MouseAcceptanceContract_UsesSpringCenteredVirtualStickArchitecture()
    {
        Assert.True(ArcadeShipController.StatefulVirtualFlightStickEnabled);
        Assert.True(ArcadeShipController.SpringCenteredVirtualFlightStickEnabled);
        Assert.InRange(
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterDelaySeconds,
            0.01f,
            0.25f);
        Assert.True(
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterRate >= 4.0f);
    }
}
