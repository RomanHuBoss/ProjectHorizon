using Godot;

namespace ProjectHorizon.Tests.Unit;

public sealed class FlightFeelHotfixTests
{
    [Fact]
    public void SurfaceSun_IsOwnedOnlyBySurfaceWorld()
    {
        Assert.True(PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
            true, WorldSceneKind.Surface));
        Assert.False(PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
            true, WorldSceneKind.Orbit));
        Assert.False(PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
            true, WorldSceneKind.InterplanetaryTransit));
        Assert.False(PlanetSurfaceWorldCompositionRuntime.ShouldRenderSurfaceSun(
            false, WorldSceneKind.Surface));
    }

    [Fact]
    public void PlanetImpactPolicy_AllowsPilotErrorRecoveryButKillsHardDive()
    {
        Assert.False(PlanetaryImpactRuntime.IsLethalSurfaceImpact(5.0f, 20.0f));
        Assert.False(PlanetaryImpactRuntime.IsLethalSurfaceImpact(11.0f, 29.0f));
        Assert.True(PlanetaryImpactRuntime.IsLethalSurfaceImpact(16.0f, 20.0f));
        Assert.True(PlanetaryImpactRuntime.IsLethalSurfaceImpact(13.0f, 35.0f));
        Assert.True(
            PlanetaryApproachRuntime.MaximumManualOrbitalEntrySpeed <
            PlanetaryApproachRuntime.MaximumOrbitalEntrySpeed);
    }

    [Fact]
    public void MouseAttitude_HorizontalTurnsNoseAndBanks_VerticalPitches()
    {
        Vector3 right = ArcadeFlightAssistRuntime.BuildMouseAttitudeCommand(
            new Vector2(0.0f, -0.8f), 0.62f);
        Vector3 up = ArcadeFlightAssistRuntime.BuildMouseAttitudeCommand(
            new Vector2(0.75f, 0.0f), 0.62f);

        Assert.InRange(Math.Abs(right.Y), 0.79f, 0.81f);
        Assert.InRange(Math.Abs(right.Z), 0.49f, 0.51f);
        Assert.InRange(Math.Abs(right.X), 0.0f, 0.001f);
        Assert.InRange(Math.Abs(up.X), 0.74f, 0.76f);
        Assert.InRange(Math.Abs(up.Y), 0.0f, 0.001f);
        Assert.InRange(Math.Abs(up.Z), 0.0f, 0.001f);
        Assert.True(ArcadeShipController.FullAttitudeRotationEnabled);
        Assert.False(ArcadeShipController.MouseTranslationCouplingEnabled);
    }

    [Fact]
    public void Acceptance_RejectsPointLikeStarAndAcceptsSubstantialDisc()
    {
        Assert.False(FlightFeelHotfixAcceptanceRunner.Evaluate(1.0).Passed);
        Assert.True(FlightFeelHotfixAcceptanceRunner.Evaluate(8.0).Passed);
    }
}
