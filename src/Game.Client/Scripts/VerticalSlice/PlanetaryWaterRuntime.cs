using System;
using System.Collections.Generic;
using System.Linq;

public sealed record PlanetaryWaterLake(
    string LakeId,
    double EastMeters,
    double NorthMeters,
    double RadiusMeters,
    double SurfaceHeightMeters);

public sealed record PlanetaryWaterProfile(
    string PlanetId,
    double PlanetRadiusMeters,
    double WaterCoverage,
    double OceanSurfaceHeightMeters,
    bool OceanEnabled,
    IReadOnlyList<PlanetaryWaterLake> Lakes)
{
    public bool HasWater => OceanEnabled || Lakes.Count > 0;
}

public readonly record struct PlanetaryWaterSample(
    bool WaterPresent,
    string WaterBody,
    double SurfaceHeightMeters,
    double BodyDepthMeters,
    double CameraDepthMeters,
    bool Swimming,
    bool Underwater);

/// <summary>
/// TASK-188 analytic planetary-water model. Water is a fixed semantic radial
/// level and never a simulated liquid volume. The rendered surface is bent by
/// the same spherical sag descriptor as terrain, while swimming/underwater
/// state is resolved from signed depth with Schmitt hysteresis.
/// </summary>
public static class PlanetaryWaterRuntime
{
    public const double MinimumWaterCoverage = 0.12;
    public const double OceanCoverageThreshold = 0.35;
    public const double DefaultOceanSurfaceHeightMeters = 0.55;
    public const double SwimmingEnterDepthMeters = 0.10;
    public const double SwimmingExitDepthMeters = -0.18;
    public const double UnderwaterEnterDepthMeters = 0.06;
    public const double UnderwaterExitDepthMeters = -0.04;

    private static readonly PlanetaryWaterLake[] StarterLakes =
    {
        new("lake.alpha", 22.0, 22.0, 7.2, 0.62),
        new("lake.beta", -25.5, 25.5, 9.2, 0.62)
    };

    public static PlanetaryWaterProfile BuildProfile(
        PlanetEnvironmentProfile environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        double coverage = Math.Clamp(environment.WaterCoverage, 0.0, 1.0);
        bool hasWater = environment.Landable &&
            coverage >= MinimumWaterCoverage;
        bool ocean = hasWater && coverage >= OceanCoverageThreshold;
        IReadOnlyList<PlanetaryWaterLake> lakes = hasWater && !ocean
            ? StarterLakes.ToArray()
            : Array.Empty<PlanetaryWaterLake>();

        return new PlanetaryWaterProfile(
            environment.PlanetId,
            Math.Max(
                PlanetSurfaceTopologyRuntime.MinimumRadiusMeters,
                environment.RadiusKm * 1000.0),
            coverage,
            DefaultOceanSurfaceHeightMeters,
            ocean,
            lakes);
    }

    public static bool TryResolveSurface(
        PlanetaryWaterProfile profile,
        double eastMeters,
        double northMeters,
        out double surfaceHeightMeters,
        out string waterBody)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.OceanEnabled)
        {
            surfaceHeightMeters = profile.OceanSurfaceHeightMeters;
            waterBody = "ocean";
            return true;
        }

        foreach (PlanetaryWaterLake lake in profile.Lakes)
        {
            double dx = eastMeters - lake.EastMeters;
            double dz = northMeters - lake.NorthMeters;
            if (dx * dx + dz * dz <= lake.RadiusMeters * lake.RadiusMeters)
            {
                surfaceHeightMeters = lake.SurfaceHeightMeters;
                waterBody = lake.LakeId;
                return true;
            }
        }

        surfaceHeightMeters = 0.0;
        waterBody = "none";
        return false;
    }

    public static PlanetaryWaterSample Sample(
        PlanetaryWaterProfile profile,
        double eastMeters,
        double northMeters,
        double bodyHeightMeters,
        double cameraHeightMeters,
        bool wasSwimming,
        bool wasUnderwater)
    {
        bool present = TryResolveSurface(
            profile,
            eastMeters,
            northMeters,
            out double surface,
            out string body);
        double bodyDepth = present ? surface - bodyHeightMeters : double.NegativeInfinity;
        double cameraDepth = present ? surface - cameraHeightMeters : double.NegativeInfinity;
        bool swimming = ResolveSwimming(wasSwimming, present, bodyDepth);
        bool underwater = ResolveUnderwater(wasUnderwater, present, cameraDepth);
        return new PlanetaryWaterSample(
            present,
            body,
            surface,
            bodyDepth,
            cameraDepth,
            swimming,
            underwater);
    }

    public static bool ResolveSwimming(
        bool wasSwimming,
        bool waterPresent,
        double bodyDepthMeters)
    {
        if (!waterPresent || !double.IsFinite(bodyDepthMeters))
        {
            return false;
        }
        return wasSwimming
            ? bodyDepthMeters >= SwimmingExitDepthMeters
            : bodyDepthMeters >= SwimmingEnterDepthMeters;
    }

    public static bool ResolveUnderwater(
        bool wasUnderwater,
        bool waterPresent,
        double cameraDepthMeters)
    {
        if (!waterPresent || !double.IsFinite(cameraDepthMeters))
        {
            return false;
        }
        return wasUnderwater
            ? cameraDepthMeters >= UnderwaterExitDepthMeters
            : cameraDepthMeters >= UnderwaterEnterDepthMeters;
    }

    public static double SemanticHeightFromCurvedLocalY(
        PlanetSurfaceCurvedPatchDescriptor patch,
        double logicalEastMeters,
        double logicalNorthMeters,
        double curvedLocalY) =>
        curvedLocalY + patch.TangentSagMeters(
            logicalEastMeters,
            logicalNorthMeters);
}
