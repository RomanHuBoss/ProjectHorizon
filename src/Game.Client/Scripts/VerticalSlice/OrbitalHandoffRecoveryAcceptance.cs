using System;

public sealed record OrbitalHandoffRecoveryAcceptanceReport(
    bool Passed,
    bool StationDistance,
    bool SurfaceOverlap,
    bool GradualEnvironment,
    bool Starfield,
    bool VacuumVisibility,
    bool StationReveal,
    double StationTravelMeters,
    double TransitionWidthMeters,
    int StarCount,
    double VacuumAmbientEnergy,
    string Result)
{
    public string BuildHudLine() => Passed
        ? $"PASS station={StationTravelMeters:0}m fade={TransitionWidthMeters:0}m stars={StarCount}"
        : $"FAIL station={(StationDistance ? 1 : 0)} fade={(GradualEnvironment ? 1 : 0)} " +
          $"stars={(Starfield ? 1 : 0)} visibility={(VacuumVisibility ? 1 : 0)}";

    public string BuildOutputLine() =>
        $"TASK-178.3 orbital handoff recovery acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"stationDistance={(StationDistance ? 1 : 0)}; surfaceOverlap={(SurfaceOverlap ? 1 : 0)}; " +
        $"gradualEnvironment={(GradualEnvironment ? 1 : 0)}; starfield={(Starfield ? 1 : 0)}; " +
        $"vacuumVisibility={(VacuumVisibility ? 1 : 0)}; stationReveal={(StationReveal ? 1 : 0)}; " +
        $"stationTravel={StationTravelMeters:0.0}m; transitionWidth={TransitionWidthMeters:0.0}m; " +
        $"stars={StarCount}; ambient={VacuumAmbientEnergy:0.00}; result={Result}";
}

public static class OrbitalHandoffRecoveryAcceptanceRunner
{
    public static OrbitalHandoffRecoveryAcceptanceReport Run()
    {
        double stationTravel =
            OrbitalHandoffPresentationRuntime.StationTravelDistanceMeters();
        double transitionWidth =
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters -
            OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters;
        WorldSceneEnvironmentPresentationProfile orbit =
            WorldSceneEnvironmentPresentationRuntime.Resolve(
                WorldSceneKind.Orbit);

        bool stationDistance =
            stationTravel >= OrbitalHandoffPresentationRuntime.MinimumStationTravelMeters &&
            stationTravel <= 3000.0;
        bool surfaceOverlap =
            SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters >= 200.0f &&
            SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters <
                stationTravel * 0.4 &&
            OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters <
                SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters &&
            OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters >
                SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters;
        bool gradualEnvironment =
            transitionWidth >= 400.0 &&
            OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(
                OrbitalHandoffPresentationRuntime.VacuumBlendStartMeters) == 0.0 &&
            OrbitalHandoffPresentationRuntime.ComputeVacuumBlend(
                OrbitalHandoffPresentationRuntime.VacuumBlendEndMeters) == 1.0;
        bool starfield =
            OrbitalHandoffPresentationRuntime.StarCount >= 300 &&
            OrbitalHandoffPresentationRuntime.StarfieldRadiusMeters >= 5000.0f &&
            OrbitalHandoffPresentationRuntime.StarfieldRevealAltitudeMeters >
                OrbitalHandoffPresentationRuntime.SurfaceSkyCeilingMeters;
        bool vacuumVisibility =
            orbit.AmbientEnergy >= 0.25 &&
            orbit.DirectionalEnergy >= 0.85 &&
            WorldSceneEnvironmentPresentationRuntime.IsVacuumProfileValid(orbit);
        bool stationReveal =
            OrbitalHandoffPresentationRuntime.StationRevealAltitudeMeters >=
                OrbitalHandoffPresentationRuntime.StarfieldRevealAltitudeMeters &&
            OrbitalHandoffPresentationRuntime.StationRevealAltitudeMeters <
                SalvageRepairSlice.PlanetRuntimeActivationRadiusMeters;

        bool passed = stationDistance && surfaceOverlap && gradualEnvironment &&
            starfield && vacuumVisibility && stationReveal;

        return new OrbitalHandoffRecoveryAcceptanceReport(
            passed,
            stationDistance,
            surfaceOverlap,
            gradualEnvironment,
            starfield,
            vacuumVisibility,
            stationReveal,
            stationTravel,
            transitionWidth,
            OrbitalHandoffPresentationRuntime.StarCount,
            orbit.AmbientEnergy,
            passed
                ? "scaled station approach, overlapping atmospheric handoff, visible vacuum and starfield verified"
                : "one or more TASK-178.3 orbital handoff recovery invariants failed");
    }
}
