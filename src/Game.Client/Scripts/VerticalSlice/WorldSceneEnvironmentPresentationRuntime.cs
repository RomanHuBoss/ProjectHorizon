using System;

public sealed record WorldSceneEnvironmentPresentationProfile(
    WorldSceneKind Kind,
    bool SurfaceOwned,
    double BackgroundRed,
    double BackgroundGreen,
    double BackgroundBlue,
    double AmbientRed,
    double AmbientGreen,
    double AmbientBlue,
    double AmbientEnergy,
    double DirectionalEnergy,
    bool FogEnabled,
    string ProfileName);

/// <summary>
/// Presentation contract for the shared root WorldEnvironment. Surface weather
/// owns the atmospheric sky; every non-surface world context must explicitly
/// override it so an orbit/station scene can never inherit a blue planetary sky.
/// </summary>
public static class WorldSceneEnvironmentPresentationRuntime
{
    public static WorldSceneEnvironmentPresentationProfile Resolve(
        WorldSceneKind kind) => kind switch
        {
            WorldSceneKind.Surface => new(
                kind, true,
                0.0, 0.0, 0.0,
                0.0, 0.0, 0.0,
                0.0, 0.0, true,
                "planet-atmosphere"),
            WorldSceneKind.Orbit => new(
                kind, false,
                0.0015, 0.0030, 0.0080,
                0.10, 0.14, 0.22,
                0.16, 0.85, false,
                "vacuum-orbit"),
            WorldSceneKind.InterplanetaryTransit => new(
                kind, false,
                0.0008, 0.0015, 0.0045,
                0.08, 0.11, 0.18,
                0.13, 0.72, false,
                "vacuum-cruise"),
            WorldSceneKind.HyperspaceTransit => new(
                kind, false,
                0.0060, 0.0015, 0.0140,
                0.18, 0.08, 0.28,
                0.20, 0.35, false,
                "hyperspace"),
            WorldSceneKind.StationInterior => new(
                kind, false,
                0.0030, 0.0050, 0.0090,
                0.16, 0.20, 0.27,
                0.24, 0.08, false,
                "station-interior"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static bool IsVacuumProfileValid(
        WorldSceneEnvironmentPresentationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SurfaceOwned)
        {
            return profile.Kind == WorldSceneKind.Surface;
        }

        double luminance =
            (profile.BackgroundRed * 0.2126) +
            (profile.BackgroundGreen * 0.7152) +
            (profile.BackgroundBlue * 0.0722);
        return !profile.FogEnabled &&
            luminance <= 0.02 &&
            profile.AmbientEnergy is >= 0.05 and <= 0.35 &&
            profile.DirectionalEnergy is >= 0.0 and <= 1.0;
    }
}
