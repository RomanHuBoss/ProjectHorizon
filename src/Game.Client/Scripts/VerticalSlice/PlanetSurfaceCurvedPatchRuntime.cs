using System;
using Godot;

/// <summary>
/// TASK-174 exact spherical sag model for the bounded resident surface patch.
/// Logical East/North remains the persistent address; the local physics patch
/// bends downward from its current tangent origin according to the real planet
/// radius. The same mapping is shared by terrain collision and navigation tiles.
/// </summary>
public sealed record PlanetSurfaceCurvedPatchDescriptor(
    double RadiusMeters,
    double OriginEastMeters,
    double OriginNorthMeters)
{
    public double TangentSagMeters(double logicalEastMeters, double logicalNorthMeters)
    {
        double x = logicalEastMeters - OriginEastMeters;
        double z = logicalNorthMeters - OriginNorthMeters;
        double distance = Math.Sqrt(x * x + z * z);
        if (distance <= 0.0)
        {
            return 0.0;
        }
        double clamped = Math.Min(distance, RadiusMeters * 0.999999);
        return RadiusMeters - Math.Sqrt(
            Math.Max(0.0, RadiusMeters * RadiusMeters - clamped * clamped));
    }

    public Vector3 ToLocalPoint(
        double logicalEastMeters,
        double heightMeters,
        double logicalNorthMeters)
    {
        return new Vector3(
            (float)(logicalEastMeters - OriginEastMeters),
            (float)(heightMeters - TangentSagMeters(
                logicalEastMeters,
                logicalNorthMeters)),
            (float)(logicalNorthMeters - OriginNorthMeters));
    }

    public Vector3 ToGameplayLogicalPoint(
        double logicalEastMeters,
        double heightMeters,
        double logicalNorthMeters)
    {
        // Gameplay children use absolute logical X/Z because the Gameplay
        // transform itself subtracts the floating/radial origin. Only Y must be
        // bent in local logical space to match the curved terrain surface.
        return new Vector3(
            (float)logicalEastMeters,
            (float)(heightMeters - TangentSagMeters(
                logicalEastMeters,
                logicalNorthMeters)),
            (float)logicalNorthMeters);
    }

    public Vector3 SurfaceUpLocal(
        double logicalEastMeters,
        double logicalNorthMeters)
    {
        double x = logicalEastMeters - OriginEastMeters;
        double z = logicalNorthMeters - OriginNorthMeters;
        double sag = TangentSagMeters(logicalEastMeters, logicalNorthMeters);
        Vector3 normal = new(
            (float)x,
            (float)(RadiusMeters - sag),
            (float)z);
        return normal.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : normal.Normalized();
    }

    public Vector3 TerrainNormalLocal(
        PlanetSurfaceTerrainProfile profile,
        double logicalEastMeters,
        double logicalNorthMeters,
        double sampleStepMeters = 0.35)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double step = Math.Max(0.05, sampleStepMeters);
        Vector3 west = ToLocalPoint(
            logicalEastMeters - step,
            PlanetSurfaceTerrainRuntime.SampleHeight(
                profile,
                logicalEastMeters - step,
                logicalNorthMeters),
            logicalNorthMeters);
        Vector3 east = ToLocalPoint(
            logicalEastMeters + step,
            PlanetSurfaceTerrainRuntime.SampleHeight(
                profile,
                logicalEastMeters + step,
                logicalNorthMeters),
            logicalNorthMeters);
        Vector3 north = ToLocalPoint(
            logicalEastMeters,
            PlanetSurfaceTerrainRuntime.SampleHeight(
                profile,
                logicalEastMeters,
                logicalNorthMeters + step),
            logicalNorthMeters + step);
        Vector3 south = ToLocalPoint(
            logicalEastMeters,
            PlanetSurfaceTerrainRuntime.SampleHeight(
                profile,
                logicalEastMeters,
                logicalNorthMeters - step),
            logicalNorthMeters - step);
        Vector3 eastTangent = east - west;
        Vector3 northTangent = north - south;
        Vector3 normal = northTangent.Cross(eastTangent);
        return normal.LengthSquared() <= 0.000001f
            ? SurfaceUpLocal(logicalEastMeters, logicalNorthMeters)
            : normal.Normalized();
    }

    public string FaceNameAt(double logicalEastMeters, double logicalNorthMeters)
    {
        PlanetSurfaceTopologyRuntime topology = new(RadiusMeters / 1000.0);
        return topology.ToCubeFaceAddress(
            topology.FromLogical(logicalEastMeters, logicalNorthMeters)).FaceName;
    }
}
