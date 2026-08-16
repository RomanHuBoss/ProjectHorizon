using System;

public sealed record PlanetAtmosphereCloudProfile(
    string PlanetId,
    bool AtmosphereEnabled,
    double AtmosphereDensity,
    int CloudLayers,
    double BaseCloudDensity,
    PlanetEnvironmentColor ZenithColor,
    PlanetEnvironmentColor HorizonColor,
    PlanetEnvironmentColor SunsetColor,
    double ShellRadiusMeters,
    double CloudBaseRadiusMeters,
    double CloudLayerSpacingMeters,
    double HorizonAmplification,
    double CloudShadowStrength);

public sealed record PlanetAtmosphereCloudFrame(
    double SunAzimuthDegrees,
    double SunElevationDegrees,
    double Daylight,
    double CloudDensity,
    double CloudOpacity,
    double CloudShadowFactor,
    double AtmosphereOpacity,
    double SunsetFactor,
    double WindMetersPerSecond,
    double WindDirectionDegrees);

/// <summary>
/// Pure low-cost atmosphere/cloud policy for §9.7-9.8. The renderer uses one
/// transparent atmospheric shell plus at most two texture-driven cloud shells;
/// no volumetric ray marching or fluid/voxel weather simulation is involved.
/// </summary>
public static class PlanetAtmosphereCloudRuntime
{
    public const int MaximumCloudLayers = 2;
    public const double SurfaceAtmosphereShellRadiusMeters = 980.0;
    public const double SurfaceCloudBaseRadiusMeters = 720.0;
    public const double SurfaceCloudLayerSpacingMeters = 52.0;

    public static PlanetAtmosphereCloudProfile BuildProfile(
        PlanetEnvironmentProfile environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        bool atmosphere = environment.AtmosphereDensity > 0.02;
        int layers = atmosphere
            ? Math.Clamp(environment.CloudLayerCount, 0, MaximumCloudLayers)
            : 0;
        double density = Math.Clamp(environment.AtmosphereDensity, 0.0, 1.8);
        double cloudDensity = layers > 0
            ? Math.Clamp(environment.CloudDensity, 0.04, 1.0)
            : 0.0;

        PlanetEnvironmentColor horizon = Blend(
            environment.AtmosphereColor,
            environment.SunsetColor,
            0.10);
        return new PlanetAtmosphereCloudProfile(
            environment.PlanetId,
            atmosphere,
            density,
            layers,
            cloudDensity,
            environment.AtmosphereColor,
            horizon,
            environment.SunsetColor,
            SurfaceAtmosphereShellRadiusMeters,
            SurfaceCloudBaseRadiusMeters,
            SurfaceCloudLayerSpacingMeters,
            Math.Clamp(1.25 + density * 0.85, 1.25, 2.7),
            Math.Clamp(0.12 + cloudDensity * 0.38, 0.0, 0.52));
    }

    public static PlanetAtmosphereCloudFrame BuildFrame(
        PlanetAtmosphereCloudProfile profile,
        PlanetWeatherState weather)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(weather);

        double cloudDensity = profile.CloudLayers <= 0
            ? 0.0
            : Math.Clamp(
                profile.BaseCloudDensity * weather.CloudMultiplier,
                0.02,
                1.0);
        double cloudOpacity = profile.CloudLayers <= 0
            ? 0.0
            : Math.Clamp(0.22 + cloudDensity * 0.72, 0.18, 0.94);
        double cloudShadow = profile.CloudLayers <= 0
            ? 1.0
            : Math.Clamp(
                1.0 - profile.CloudShadowStrength * cloudDensity *
                    Math.Clamp(0.55 + weather.Intensity * 0.55, 0.55, 1.10),
                0.54,
                1.0);
        double sunsetFactor = Math.Clamp(
            1.0 - Math.Abs(weather.SunElevationDegrees) / 16.0,
            0.0,
            1.0) * Math.Clamp(1.0 - Math.Abs(weather.Daylight - 0.5) * 1.35, 0.0, 1.0);
        double atmosphereOpacity = profile.AtmosphereEnabled
            ? Math.Clamp(0.12 + profile.AtmosphereDensity * 0.26, 0.10, 0.58)
            : 0.0;

        return new PlanetAtmosphereCloudFrame(
            weather.SunAzimuthDegrees,
            weather.SunElevationDegrees,
            weather.Daylight,
            cloudDensity,
            cloudOpacity,
            cloudShadow,
            atmosphereOpacity,
            sunsetFactor,
            weather.WindMetersPerSecond,
            weather.WindDirectionDegrees);
    }

    public static double ApplyCloudShadow(double directionalEnergy, double factor) =>
        Math.Max(0.0, directionalEnergy) * Math.Clamp(factor, 0.45, 1.0);

    private static PlanetEnvironmentColor Blend(
        PlanetEnvironmentColor a,
        PlanetEnvironmentColor b,
        double t)
    {
        double clamped = Math.Clamp(t, 0.0, 1.0);
        return new PlanetEnvironmentColor(
            a.R + (b.R - a.R) * clamped,
            a.G + (b.G - a.G) * clamped,
            a.B + (b.B - a.B) * clamped);
    }
}
