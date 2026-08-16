using Godot;

public partial class SalvageRepairSlice
{
    private string _spaceflightCollisionRecoveryAcceptanceHud = "READY";
    private bool? _spaceflightCollisionRecoveryAcceptancePassed;

    private void RunSpaceflightCollisionRecoveryAcceptance()
    {
        SpaceflightCollisionRecoveryAcceptanceReport report =
            SpaceflightCollisionRecoveryAcceptanceRunner.Run();

        bool flightAssist = _voyageShip is not null &&
            _voyageShip.VelocityAlignmentRate >= 2.5f;
        StarSystemBodyDefinition definition = null!;
        Vector3 center = Vector3.Zero;
        float radius = 0.0f;
        bool currentPlanetSphere = _starSystemSimulationNode is not null &&
            _galaxyNavigationRuntime is not null &&
            _starSystemSimulationNode.TryGetBodyDisplaySphere(
                GalaxyNavigation.CurrentPlanetId,
                out definition,
                out center,
                out radius) &&
            definition.Kind == StarSystemBodyKind.Planet &&
            center.IsFinite() && radius >= 520.0f;
        bool liveSweep = currentPlanetSphere &&
            OrbitalBodyCollisionRuntime.TrySweepSphere(
                center - Vector3.Right * (radius + 500.0f),
                center + Vector3.Right * (radius + 500.0f),
                center,
                radius + OrbitalBodyCollisionRuntime.ShipCollisionRadiusMeters,
                out _,
                out _,
                out _);

        bool passed = report.Passed && flightAssist && currentPlanetSphere && liveSweep;
        _spaceflightCollisionRecoveryAcceptancePassed = passed;
        _spaceflightCollisionRecoveryAcceptanceHud = passed
            ? $"PASS assist=1 sweep=1 radius={radius:0}m"
            : $"FAIL model={(report.Passed ? 1 : 0)} assist={(flightAssist ? 1 : 0)} " +
              $"sphere={(currentPlanetSphere ? 1 : 0)} sweep={(liveSweep ? 1 : 0)}";

        string output = report.BuildOutputLine().Replace(
            $"acceptance {(report.Passed ? "PASS" : "FAIL")}:",
            $"acceptance {(passed ? "PASS" : "FAIL")}:") +
            $" liveAssist={(flightAssist ? 1 : 0)}; " +
            $"currentPlanetSphere={(currentPlanetSphere ? 1 : 0)}; " +
            $"liveSweep={(liveSweep ? 1 : 0)}; " +
            $"runtimeCollisions={_orbitalSweptCollisionCount}; " +
            $"manualEntries={_freeFlightPlanetEntryCount}; " +
            $"lastBody={(_orbitalCollisionLastBody.Length == 0 ? "none" : _orbitalCollisionLastBody)}.";
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
