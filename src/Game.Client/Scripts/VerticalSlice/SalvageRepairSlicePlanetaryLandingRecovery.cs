using System;
using System.Globalization;
using Godot;

public partial class SalvageRepairSlice
{
    private string _planetaryLandingRecoveryAcceptanceHud = "READY";
    private bool? _planetaryLandingRecoveryAcceptancePassed;

    private void RunPlanetaryLandingRecoveryAcceptance()
    {
        PlanetaryLandingRecoveryAcceptanceReport report =
            PlanetaryLandingRecoveryAcceptanceRunner.Run();

        Vector3 entry = Vector3.Zero;
        Vector3 center = Vector3.Zero;
        float radius = 0.0f;
        bool liveGlobe = _starSystemSimulationNode is not null &&
            _voyageShip is not null &&
            _starSystemSimulationNode.TryGetBodyApproachPoint(
                GalaxyNavigation.CurrentPlanetId,
                _voyageShip.GlobalPosition,
                PlanetaryApproachRuntime.OrbitalEntryClearanceMeters,
                out entry,
                out center,
                out radius);
        // Acceptance must be independent of where the player happens to press
        // F5. Measure orbital readability from the star-system presentation
        // origin, not from the current free-flight ship position.
        double presentationDistance = liveGlobe && _starSystemSimulationNode is not null
            ? _starSystemSimulationNode.GlobalPosition.DistanceTo(center)
            : double.PositiveInfinity;
        double angularRadius = liveGlobe
            ? PlanetaryApproachRuntime.AngularRadiusDegrees(
                radius,
                Math.Max(radius + 0.1, presentationDistance))
            : 0.0;
        bool readableGlobe = liveGlobe &&
            radius >= 520.0f &&
            angularRadius >=
                PlanetaryApproachRuntime.MinimumFocusedPlanetAngularRadiusDegrees;
        bool entryOutsideGlobe = liveGlobe &&
            entry.DistanceTo(center) >=
                radius + PlanetaryApproachRuntime.OrbitalEntryClearanceMeters - 0.5;
        PlanetEnvironmentProfile profile = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        bool landable = profile.Landable;

        bool passed = report.Passed && readableGlobe &&
            entryOutsideGlobe && landable;
        _planetaryLandingRecoveryAcceptancePassed = passed;
        _planetaryLandingRecoveryAcceptanceHud = passed
            ? $"PASS radius={radius:0}m angular={angularRadius:0.0}deg entry=2-stage"
            : $"FAIL model={(report.Passed ? 1 : 0)} globe={(readableGlobe ? 1 : 0)} " +
              $"entry={(entryOutsideGlobe ? 1 : 0)} landable={(landable ? 1 : 0)}";

        string output = report.BuildOutputLine().Replace(
            $"acceptance {(report.Passed ? "PASS" : "FAIL")}:",
            $"acceptance {(passed ? "PASS" : "FAIL")}:") +
            $" liveGlobe={(readableGlobe ? 1 : 0)}; " +
            $"displayRadius={radius.ToString("0", CultureInfo.InvariantCulture)}m; " +
            $"angularRadius={angularRadius.ToString("0.0", CultureInfo.InvariantCulture)}deg; " +
            $"entryOutsideGlobe={(entryOutsideGlobe ? 1 : 0)}; landable={(landable ? 1 : 0)}.";
        if (passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }
}
