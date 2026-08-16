using System;

/// <summary>
/// TASK-178.4 two-stage orbital-to-surface landing contract. The visible globe
/// is a compressed orbital representation, while the verified curved-surface
/// streamer owns the final atmospheric approach. TASK-178.7 keeps the coordinate handoff above the completed visual/flight-dynamics atmosphere blend so the transfer itself does not create a force or lighting step. Flight crosses an explicit entry
/// envelope before the bounded surface runtime is activated.
/// </summary>
public static class PlanetaryApproachRuntime
{
    public const double OrbitalEntryClearanceMeters = 220.0;
    public const double OrbitalEntryCaptureRadiusMeters = 95.0;
    public const double MaximumOrbitalEntrySpeed = 110.0;
    public const double SurfaceApproachAltitudeMeters = 680.0;
    public const double MinimumFocusedPlanetAngularRadiusDegrees = 12.0;

    public static bool IsOrbitalEntryCaptureReady(
        double distanceMeters,
        double speedMetersPerSecond) =>
        double.IsFinite(distanceMeters) &&
        distanceMeters >= 0.0 &&
        distanceMeters <= OrbitalEntryCaptureRadiusMeters &&
        double.IsFinite(speedMetersPerSecond) &&
        speedMetersPerSecond >= 0.0 &&
        speedMetersPerSecond <= MaximumOrbitalEntrySpeed;

    public static double AngularRadiusDegrees(
        double displayRadiusMeters,
        double distanceToCenterMeters)
    {
        if (!double.IsFinite(displayRadiusMeters) ||
            !double.IsFinite(distanceToCenterMeters) ||
            displayRadiusMeters <= 0.0 ||
            distanceToCenterMeters <= displayRadiusMeters)
        {
            return 90.0;
        }

        return Math.Asin(Math.Clamp(
            displayRadiusMeters / distanceToCenterMeters,
            0.0,
            1.0)) * 180.0 / Math.PI;
    }
}
