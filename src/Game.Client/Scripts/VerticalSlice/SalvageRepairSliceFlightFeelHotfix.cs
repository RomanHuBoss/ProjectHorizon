using System;
using Godot;

public partial class SalvageRepairSlice
{
    private string _flightFeelHotfixAcceptanceHud = "READY";
    private bool? _flightFeelHotfixAcceptancePassed;

    private void PrintFlightFeelHotfixReady()
    {
        GD.Print(
            "TASK-180.2 flight feel READY: stellarVisual=system-only-in-orbit; " +
            $"manualEntrySafe<={PlanetaryApproachRuntime.MaximumManualOrbitalEntrySpeed:0}m/s; " +
            $"surfaceLethalNormal>={PlanetaryImpactRuntime.LethalNormalImpactSpeed:0}m/s; " +
            "mouse=virtual-stick-roll-dominant/Y:pitch; fullAttitude=1; strafe=keyboard-only; superseded-by=TASK-180.3; F5=acceptance.");
    }

    private void RunFlightFeelHotfixAcceptance()
    {
        _flightFeelHotfixAcceptanceHud = "RUNNING";
        _flightFeelHotfixAcceptancePassed = null;

        double angularDiameter = 0.0;
        if (_starSystemSimulationNode is not null &&
            _galaxyNavigationRuntime is not null)
        {
            string starId = $"{GalaxyNavigation.CurrentSystem.SystemId}.star";
            if (_starSystemSimulationNode.TryGetBodyDisplaySphere(
                    starId, out _, out Vector3 starCenter, out float starRadius) &&
                _starSystemSimulationNode.TryGetBodyDisplaySphere(
                    GalaxyNavigation.CurrentPlanetId, out _,
                    out Vector3 planetCenter, out _))
            {
                angularDiameter = 2.0 * PlanetaryApproachRuntime.AngularRadiusDegrees(
                    starRadius,
                    Math.Max(starRadius + 0.1, starCenter.DistanceTo(planetCenter)));
            }
        }

        FlightFeelHotfixAcceptanceReport report =
            FlightFeelHotfixAcceptanceRunner.Evaluate(angularDiameter);
        _flightFeelHotfixAcceptancePassed = report.Passed;
        _flightFeelHotfixAcceptanceHud = report.Passed
            ? $"PASS sun={report.StarAngularDiameterDegrees:0.0}deg crash=1 mouse=roll-dominant"
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
