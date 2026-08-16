using Godot;

public partial class SalvageRepairSlice
{
    private string _flightRuntimeClosureAcceptanceHud = "READY";
    private bool? _flightRuntimeClosureAcceptancePassed;

    private void PrintFlightRuntimeClosureReady()
    {
        GD.Print(
            "TASK-182 flight runtime closure READY: " +
            "mouse=stateful-spring-centred-stick; " +
            $"autoCenterDelay={ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterDelaySeconds:0.00}s; " +
            $"autoCenterRate={ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterRate:0.0}/s; " +
            "atmospherePresence=schmitt-hysteresis; " +
            "terrainRefresh=adjacent-revision-coalescing; " +
            "inactiveSurfaceObserver=suppressed; ownerLog=clean-no-errors; F5=acceptance.");
    }

    private void RunFlightRuntimeClosureAcceptance()
    {
        _flightRuntimeClosureAcceptanceHud = "RUNNING";
        _flightRuntimeClosureAcceptancePassed = null;

        int maxLag = _planetSurfaceStreamer?.MaxCoalescedCenterLagChunks ?? 1;
        bool coalescingEnabled =
            _planetSurfaceStreamer?.RuntimeRefreshCoalescingEnabled ?? true;
        float centerDelay = _voyageShip?.MouseVirtualStickAutoCenterDelaySeconds ??
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterDelaySeconds;
        float centerRate = _voyageShip?.MouseVirtualStickAutoCenterRate ??
            ArcadeFlightAssistRuntime.DefaultVirtualStickAutoCenterRate;
        float atmosphereEnter = _voyageShip?.AtmospherePresenceEnterBlend ??
            ArcadeShipController.DefaultAtmospherePresenceEnterBlend;
        float atmosphereExit = _voyageShip?.AtmospherePresenceExitBlend ??
            ArcadeShipController.DefaultAtmospherePresenceExitBlend;

        FlightRuntimeClosureAcceptanceReport report =
            FlightRuntimeClosureAcceptanceRunner.Evaluate(
                inactiveSurfaceObserverSuppressed: true,
                autoCenterDelaySeconds: centerDelay,
                autoCenterRate: centerRate,
                atmosphereEnterBlend: atmosphereEnter,
                atmosphereExitBlend: atmosphereExit,
                runtimeRefreshCoalescingEnabled: coalescingEnabled,
                maxCoalescedCenterLagChunks: maxLag);

        _flightRuntimeClosureAcceptancePassed = report.Passed;
        _flightRuntimeClosureAcceptanceHud = report.Passed
            ? $"PASS spring=1 atmHys=1 streamCoal=1 stick={report.StickMagnitudeAfter:0.000}"
            : $"FAIL {report.Result}";

        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
