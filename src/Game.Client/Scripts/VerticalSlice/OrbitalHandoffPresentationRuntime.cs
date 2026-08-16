using System;

public sealed record OrbitalHandoffPresentationState(
    double AltitudeMeters,
    double VacuumBlend,
    bool SurfaceSkyOwned,
    bool StationVisible,
    bool StarfieldVisible,
    string Phase);

/// <summary>
/// TASK-178.3 orbital handoff contract. The local surface, upper atmosphere and
/// vacuum presentation overlap deliberately so takeoff never crosses a single
/// hard visual threshold. Physical orbital infrastructure is kept outside the
/// near-surface gameplay scale.
/// </summary>
public static class OrbitalHandoffPresentationRuntime
{
    public const double SurfaceSkyCeilingMeters = 110.0;
    public const double VacuumBlendStartMeters = 110.0;
    public const double VacuumBlendEndMeters = 620.0;
    public const double StationRevealAltitudeMeters = 220.0;
    public const double StarfieldRevealAltitudeMeters = 145.0;
    public const double MinimumStationTravelMeters = 1200.0;
    public const int StarCount = 420;
    public const float StarfieldRadiusMeters = 7200.0f;

    public static double ComputeVacuumBlend(double altitudeMeters)
    {
        if (double.IsNaN(altitudeMeters) || double.IsNegativeInfinity(altitudeMeters))
        {
            return 0.0;
        }

        if (double.IsPositiveInfinity(altitudeMeters))
        {
            return 1.0;
        }

        double denominator = VacuumBlendEndMeters - VacuumBlendStartMeters;
        double t = denominator <= 0.0
            ? 1.0
            : Math.Clamp(
                (altitudeMeters - VacuumBlendStartMeters) / denominator,
                0.0,
                1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    public static OrbitalHandoffPresentationState Evaluate(
        double altitudeMeters)
    {
        double blend = ComputeVacuumBlend(altitudeMeters);
        bool surfaceOwned = altitudeMeters <= SurfaceSkyCeilingMeters;
        bool stationVisible = altitudeMeters >= StationRevealAltitudeMeters;
        bool starfieldVisible = altitudeMeters >= StarfieldRevealAltitudeMeters;
        string phase = surfaceOwned
            ? "lower-atmosphere"
            : blend >= 0.995
                ? "vacuum-orbit"
                : "upper-atmosphere";
        return new OrbitalHandoffPresentationState(
            altitudeMeters,
            blend,
            surfaceOwned,
            stationVisible,
            starfieldVisible,
            phase);
    }

    public static double StationTravelDistanceMeters()
    {
        double dx = StageOneVoyageRuntime.StationDockPositionX -
            StageOneVoyageRuntime.SurfacePositionX;
        double dy = StageOneVoyageRuntime.StationDockPositionY -
            StageOneVoyageRuntime.LaunchPositionY;
        double dz = StageOneVoyageRuntime.StationDockPositionZ -
            StageOneVoyageRuntime.SurfacePositionZ;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
