namespace ProjectHorizon.Tests.Unit;

public sealed class OrbitalHandoffRecoveryTests
{
    [Fact]
    public void OrbitalHandoff_UsesOverlappingAtmosphereAndVacuumRanges()
    {
        OrbitalHandoffRecoveryAcceptanceReport report =
            OrbitalHandoffRecoveryAcceptanceRunner.Run();

        Assert.True(report.Passed, report.Result);
        Assert.True(report.StationDistance);
        Assert.True(report.SurfaceOverlap);
        Assert.True(report.GradualEnvironment);
        Assert.True(report.Starfield);
        Assert.True(report.VacuumVisibility);
        Assert.True(report.StationReveal);
        Assert.InRange(report.StationTravelMeters, 1200.0, 3000.0);
        Assert.InRange(report.TransitionWidthMeters, 400.0, 1000.0);
        Assert.InRange(report.StarCount, 300, 1000);
    }

    [Fact]
    public void VacuumBlend_IsSmoothAndMonotonic()
    {
        double previous = -1.0;
        for (double altitude = 0.0; altitude <= 800.0; altitude += 20.0)
        {
            double current =
                OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(altitude);
            Assert.InRange(current, 0.0, 1.0);
            Assert.True(current >= previous);
            previous = current;
        }

        Assert.Equal(
            0.0,
            OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(
                OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters),
            8);
        Assert.Equal(
            1.0,
            OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(
                OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters),
            8);
    }

    [Fact]
    public void OrbitalStation_IsNotPartOfNearSurfaceScale()
    {
        double distance =
            OrbitalHandoffPresentationRuntime.StationTravelDistanceMeters();

        Assert.True(
            distance >= OrbitalHandoffPresentationRuntime.MinimumStationTravelMeters);
        Assert.True(
            OrbitalHandoffPresentationRuntime.StationRevealAltitudeMeters <
            SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters);
        Assert.True(
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters >
            SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters);
    }
}
