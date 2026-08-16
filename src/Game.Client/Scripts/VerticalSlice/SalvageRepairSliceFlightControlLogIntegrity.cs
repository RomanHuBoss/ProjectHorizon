using Godot;

public partial class SalvageRepairSlice
{
    private string _flightControlLogIntegrityAcceptanceHud = "READY";
    private bool? _flightControlLogIntegrityAcceptancePassed;

    private void PrintFlightControlLogIntegrityReady()
    {
        GD.Print(
            "TASK-180.3 flight control/log integrity READY: " +
            "mouse=stateful-virtual-stick; horizontal=roll-dominant+coordinated-yaw; " +
            "vertical=pitch; commandCore=stateful; liveCentering=TASK-182; recenter=middle-mouse; " +
            $"camera={ArcadeShipController.DefaultFlightCameraNearMeters:0.00}.." +
            $"{ArcadeShipController.DefaultFlightCameraFarMeters / 1000.0f:0}km; " +
            "surfaceGuard=hysteretic; weatherOwnership=surface-only; " +
            "terrainDistance=int64-saturated; F5=acceptance.");
    }

    private void RunFlightControlLogIntegrityAcceptance()
    {
        _flightControlLogIntegrityAcceptanceHud = "RUNNING";
        _flightControlLogIntegrityAcceptancePassed = null;

        float nearPlane = _voyageShip?.FlightCameraNearMeters ??
            ArcadeShipController.DefaultFlightCameraNearMeters;
        float farPlane = _voyageShip?.FlightCameraFarMeters ??
            ArcadeShipController.DefaultFlightCameraFarMeters;
        FlightControlLogIntegrityAcceptanceReport report =
            FlightControlLogIntegrityAcceptanceRunner.Evaluate(
                nearPlane,
                farPlane,
                surfaceGuardHysteresis: true,
                surfaceWeatherOwnership: true,
                overflowSafeTerrainDistance: true);

        _flightControlLogIntegrityAcceptancePassed = report.Passed;
        _flightControlLogIntegrityAcceptanceHud = report.Passed
            ? "PASS stick=stateful roll>yaw frustum=bounded logguards=1"
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
