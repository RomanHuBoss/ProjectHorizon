using System;
using Godot;

public sealed record FlightRuntimeClosureAcceptanceReport(
    bool Passed,
    bool SpringCenteredStick,
    bool IdleStickReturnsToNeutral,
    bool MicroInputHoldWindow,
    bool AtmosphereHysteresis,
    bool TerrainRefreshCoalescing,
    bool LargeObserverJumpNotCoalesced,
    bool InactiveSurfaceObserverSuppressed,
    float StickMagnitudeBefore,
    float StickMagnitudeAfter,
    string Result)
{
    public string BuildOutputLine() =>
        "TASK-182 flight-runtime closure acceptance " +
        $"{(Passed ? "PASS" : "FAIL")}: " +
        $"springCenter={(SpringCenteredStick ? 1 : 0)}; " +
        $"neutralReturn={(IdleStickReturnsToNeutral ? 1 : 0)}; " +
        $"holdWindow={(MicroInputHoldWindow ? 1 : 0)}; " +
        $"atmosphereHysteresis={(AtmosphereHysteresis ? 1 : 0)}; " +
        $"streamCoalescing={(TerrainRefreshCoalescing ? 1 : 0)}; " +
        $"largeJumpImmediate={(LargeObserverJumpNotCoalesced ? 1 : 0)}; " +
        $"inactiveObserverSuppressed={(InactiveSurfaceObserverSuppressed ? 1 : 0)}; " +
        $"stick={StickMagnitudeBefore:0.000}->{StickMagnitudeAfter:0.000}; " +
        $"result={Result}";
}

public static class FlightRuntimeClosureAcceptanceRunner
{
    public static FlightRuntimeClosureAcceptanceReport Evaluate(
        bool inactiveSurfaceObserverSuppressed,
        float autoCenterDelaySeconds =
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterDelaySeconds,
        float autoCenterRate =
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterRate,
        float atmosphereEnterBlend =
            ArcadeShipController.DefaultAtmospherePresenceEnterBlend,
        float atmosphereExitBlend =
            ArcadeShipController.DefaultAtmospherePresenceExitBlend,
        bool runtimeRefreshCoalescingEnabled = true,
        int maxCoalescedCenterLagChunks = 1)
    {
        Vector2 initial = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
            Vector2.Zero,
            new Vector2(36.0f, -18.0f),
            0.0035f,
            1.45f,
            invertPitch: false,
            invertHorizontal: false);

        Vector2 held = ArcadeFlightAssistRuntime.SpringCenterVirtualFlightStick(
            initial,
            idleSeconds: Math.Max(0.0f, autoCenterDelaySeconds * 0.50f),
            deltaSeconds: 1.0f / 60.0f,
            delaySeconds: autoCenterDelaySeconds,
            returnRate: autoCenterRate);
        bool microInputHoldWindow = held.DistanceTo(initial) <= 0.000001f;

        Vector2 centered = initial;
        float idle = Math.Max(0.0f, autoCenterDelaySeconds) + (1.0f / 60.0f);
        for (int frame = 0; frame < 180; frame++)
        {
            centered = ArcadeFlightAssistRuntime.SpringCenterVirtualFlightStick(
                centered,
                idle,
                1.0f / 60.0f,
                autoCenterDelaySeconds,
                autoCenterRate);
            idle += 1.0f / 60.0f;
        }

        bool springCenteredStick =
            ArcadeShipController.StatefulVirtualFlightStickEnabled &&
            ArcadeShipController.SpringCenteredVirtualFlightStickEnabled &&
            autoCenterDelaySeconds >= 0.0f &&
            autoCenterDelaySeconds <= 0.40f &&
            autoCenterRate >= 0.5f;
        bool idleStickReturnsToNeutral =
            initial.Length() > ArcadeFlightAssistRuntime.DefaultVirtualStickDeadZone &&
            centered.Length() <= 0.000001f;

        // TASK-182 log regression: the owner run crossed the 590 m boundary and
        // immediately toggled EXIT 590.0 -> ENTER 589.9. A Schmitt-trigger style
        // atmosphere presence state must hold the current state in the middle band.
        float middleBlend = (atmosphereEnterBlend + atmosphereExitBlend) * 0.5f;
        bool thresholdsOrdered = atmosphereEnterBlend > atmosphereExitBlend &&
            atmosphereExitBlend >= 0.0001f;
        bool insideMiddleBand = ArcadeShipController.ResolveAtmospherePresence(
            currentlyInAtmosphere: true,
            atmosphereBlend: middleBlend,
            enterBlend: atmosphereEnterBlend,
            exitBlend: atmosphereExitBlend);
        bool outsideMiddleBand = ArcadeShipController.ResolveAtmospherePresence(
            currentlyInAtmosphere: false,
            atmosphereBlend: middleBlend,
            enterBlend: atmosphereEnterBlend,
            exitBlend: atmosphereExitBlend);
        bool entersAtUpperThreshold = ArcadeShipController.ResolveAtmospherePresence(
            currentlyInAtmosphere: false,
            atmosphereBlend: atmosphereEnterBlend,
            enterBlend: atmosphereEnterBlend,
            exitBlend: atmosphereExitBlend);
        bool exitsAtLowerThreshold = !ArcadeShipController.ResolveAtmospherePresence(
            currentlyInAtmosphere: true,
            atmosphereBlend: atmosphereExitBlend,
            enterBlend: atmosphereEnterBlend,
            exitBlend: atmosphereExitBlend);
        bool atmosphereHysteresis = thresholdsOrdered && insideMiddleBand &&
            !outsideMiddleBand && entersAtUpperThreshold && exitsAtLowerThreshold;

        bool adjacentBusyRefreshCoalesced = TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(1, 0),
            refreshInFlight: true,
            coalescingEnabled: runtimeRefreshCoalescingEnabled,
            maxLagChunks: maxCoalescedCenterLagChunks);
        bool idleRefreshNotCoalesced = !TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(1, 0),
            refreshInFlight: false,
            coalescingEnabled: runtimeRefreshCoalescingEnabled,
            maxLagChunks: maxCoalescedCenterLagChunks);
        bool terrainRefreshCoalescing = adjacentBusyRefreshCoalesced &&
            idleRefreshNotCoalesced;
        bool largeObserverJumpNotCoalesced = !TerrainChunkManager.ShouldCoalesceRuntimeRefresh(
            Vector2I.Zero,
            new Vector2I(Math.Max(2, maxCoalescedCenterLagChunks + 1), 0),
            refreshInFlight: true,
            coalescingEnabled: runtimeRefreshCoalescingEnabled,
            maxLagChunks: maxCoalescedCenterLagChunks);

        bool passed = springCenteredStick && idleStickReturnsToNeutral &&
            microInputHoldWindow && atmosphereHysteresis &&
            terrainRefreshCoalescing && largeObserverJumpNotCoalesced &&
            inactiveSurfaceObserverSuppressed;

        return new FlightRuntimeClosureAcceptanceReport(
            passed,
            springCenteredStick,
            idleStickReturnsToNeutral,
            microInputHoldWindow,
            atmosphereHysteresis,
            terrainRefreshCoalescing,
            largeObserverJumpNotCoalesced,
            inactiveSurfaceObserverSuppressed,
            initial.Length(),
            centered.Length(),
            passed
                ? "spring-centred mouse flight, atmospheric hysteresis and bounded streaming refresh verified"
                : "spring-centering, atmosphere hysteresis or terrain refresh closure failed");
    }
}
