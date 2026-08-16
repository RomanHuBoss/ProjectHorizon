using System;

public readonly record struct PlanetSurfaceGeographicAddress(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double RadiusMeters);

public readonly record struct PlanetSurfaceUnitVector(
    double X,
    double Y,
    double Z)
{
    public double Dot(PlanetSurfaceUnitVector other) =>
        X * other.X + Y * other.Y + Z * other.Z;
}

/// <summary>
/// Global spherical address model for a landable planet. The live gameplay scene
/// remains a bounded tangent patch, while logical east/north coordinates form an
/// unbounded cover over this sphere. Latitude is normalized across the poles and
/// longitude wraps at +/-180 degrees, so navigation/map/orbit systems share a
/// deterministic planet-global address without requiring the whole planet in a
/// Godot Vector3 scene.
/// </summary>
public sealed class PlanetSurfaceTopologyRuntime
{
    public const double MinimumRadiusMeters = 1_000.0;

    public PlanetSurfaceTopologyRuntime(double planetRadiusKm)
    {
        RadiusMeters = Math.Max(MinimumRadiusMeters, planetRadiusKm * 1_000.0);
    }

    public double RadiusMeters { get; }

    public double CircumferenceMeters => Math.PI * 2.0 * RadiusMeters;

    public PlanetSurfaceGeographicAddress FromLogical(
        double eastMeters,
        double northMeters)
    {
        if (!double.IsFinite(eastMeters) || !double.IsFinite(northMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eastMeters),
                "Planet-surface logical coordinates must be finite.");
        }

        double latitude = northMeters / RadiusMeters;
        double longitude = eastMeters / RadiusMeters;
        NormalizeLatitudeLongitude(ref latitude, ref longitude);
        return new PlanetSurfaceGeographicAddress(
            latitude * 180.0 / Math.PI,
            longitude * 180.0 / Math.PI,
            RadiusMeters);
    }

    public PlanetSurfaceUnitVector ToUnitVector(
        PlanetSurfaceGeographicAddress address)
    {
        double latitude = address.LatitudeDegrees * Math.PI / 180.0;
        double longitude = address.LongitudeDegrees * Math.PI / 180.0;
        double cosLatitude = Math.Cos(latitude);
        return new PlanetSurfaceUnitVector(
            cosLatitude * Math.Cos(longitude),
            Math.Sin(latitude),
            cosLatitude * Math.Sin(longitude));
    }

    public double GreatCircleDistanceMeters(
        double eastA,
        double northA,
        double eastB,
        double northB)
    {
        PlanetSurfaceUnitVector left = ToUnitVector(FromLogical(eastA, northA));
        PlanetSurfaceUnitVector right = ToUnitVector(FromLogical(eastB, northB));
        double dot = Math.Clamp(left.Dot(right), -1.0, 1.0);
        return Math.Acos(dot) * RadiusMeters;
    }

    public double GreatCircleDistanceFromOriginMeters(
        double eastMeters,
        double northMeters) =>
        GreatCircleDistanceMeters(0.0, 0.0, eastMeters, northMeters);

    public double TangentSagMeters(double tangentDistanceMeters)
    {
        double distance = Math.Abs(tangentDistanceMeters);
        if (distance <= 0.0)
        {
            return 0.0;
        }

        // A tangent visual proxy must never be asked to span a hemisphere. Clamp
        // defensively so malformed developer settings cannot produce NaN geometry.
        double clamped = Math.Min(distance, RadiusMeters * 0.999999);
        return RadiusMeters - Math.Sqrt(
            Math.Max(0.0, RadiusMeters * RadiusMeters - clamped * clamped));
    }

    public static double WrapLongitudeDegrees(double longitudeDegrees)
    {
        double wrapped = longitudeDegrees % 360.0;
        if (wrapped >= 180.0)
        {
            wrapped -= 360.0;
        }
        else if (wrapped < -180.0)
        {
            wrapped += 360.0;
        }
        return wrapped;
    }

    private static void NormalizeLatitudeLongitude(
        ref double latitudeRadians,
        ref double longitudeRadians)
    {
        // First reduce very large values to a small number of pole crossings.
        double twoPi = Math.PI * 2.0;
        latitudeRadians %= twoPi;
        longitudeRadians %= twoPi;

        while (latitudeRadians > Math.PI * 0.5)
        {
            latitudeRadians = Math.PI - latitudeRadians;
            longitudeRadians += Math.PI;
        }
        while (latitudeRadians < -Math.PI * 0.5)
        {
            latitudeRadians = -Math.PI - latitudeRadians;
            longitudeRadians += Math.PI;
        }

        longitudeRadians = ((longitudeRadians + Math.PI) % twoPi + twoPi) %
            twoPi - Math.PI;
    }
}
