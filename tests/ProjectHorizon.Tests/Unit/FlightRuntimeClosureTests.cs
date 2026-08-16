using Godot;

namespace ProjectHorizon.Tests.Unit;

public sealed class FlightRuntimeClosureTests
{
    [Fact]
    public void VirtualStick_HoldsBrieflyThenReturnsToNeutral()
    {
        Vector2 initial = new(0.18f, 0.32f);
        Vector2 held = ArcadeFlightAssistRuntime.SpringCenterVirtualFlightStick(
            initial,
            idleSeconds: 0.03f,
            deltaSeconds: 1.0f / 60.0f);
        Assert.Equal(initial, held);

        Vector2 current = initial;
        float idle = ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterDelaySeconds +
            (1.0f / 60.0f);
        for (int frame = 0; frame < 180; frame++)
        {
            current = ArcadeFlightAssistRuntime.SpringCenterVirtualFlightStick(
                current,
                idle,
                1.0f / 60.0f);
            idle += 1.0f / 60.0f;
        }

        Assert.Equal(Vector2.Zero, current);
    }

    [Fact]
    public void AtmospherePresence_UsesHysteresisInsteadOfThresholdChatter()
    {
        Assert.True(ArcadeShipController.ResolveAtmospherePresence(true, 0.010f));
        Assert.False(ArcadeShipController.ResolveAtmospherePresence(false, 0.010f));
        Assert.True(ArcadeShipController.ResolveAtmospherePresence(
            false,
            ArcadeShipController.DefaultAtmospherePresenceEnterBlend));
        Assert.False(ArcadeShipController.ResolveAtmospherePresence(
            true,
            ArcadeShipController.DefaultAtmospherePresenceExitBlend));
    }

    [Fact]
    public void TerrainRefresh_CoalescesAdjacentBusyRevisionButNotLargeJump()
    {
        Assert.True(TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(1, 0),
            refreshInFlight: true,
            coalescingEnabled: true,
            maxLagChunks: 1));
        Assert.False(TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(2, 0),
            refreshInFlight: true,
            coalescingEnabled: true,
            maxLagChunks: 1));
        Assert.False(TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(1, 0),
            refreshInFlight: false,
            coalescingEnabled: true,
            maxLagChunks: 1));
    }

    [Fact]
    public void Acceptance_PassesFlightRuntimeClosureContract()
    {
        FlightRuntimeClosureAcceptanceReport report =
            FlightRuntimeClosureAcceptanceRunner.Evaluate(
                inactiveSurfaceObserverSuppressed: true);
        Assert.True(report.Passed, report.Result);
        Assert.True(report.IdleStickReturnsToNeutral);
        Assert.True(report.AtmosphereHysteresis);
        Assert.True(report.TerrainRefreshCoalescing);
    }
}
