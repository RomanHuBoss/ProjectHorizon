using System;

public readonly record struct PlanetSurfaceGeographicAddress(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double RadiusMeters);

public readonly record struct PlanetSurfaceCanonicalLogicalAddress(
    double EastMeters,
    double NorthMeters);

public readonly record struct PlanetSurfaceUnitVector(
    double X,
    double Y,
    double Z)
{
    public double Dot(PlanetSurfaceUnitVector other) =>
        X * other.X + Y * other.Y + Z * other.Z;

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public PlanetSurfaceUnitVector Normalized()
    {
        double length = Length;
        return length <= 0.000000000001
            ? new PlanetSurfaceUnitVector(0.0, 1.0, 0.0)
            : new PlanetSurfaceUnitVector(X / length, Y / length, Z / length);
    }

    public PlanetSurfaceUnitVector Cross(PlanetSurfaceUnitVector other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public static PlanetSurfaceUnitVector operator +(
        PlanetSurfaceUnitVector left,
        PlanetSurfaceUnitVector right) => new(
            left.X + right.X,
            left.Y + right.Y,
            left.Z + right.Z);

    public static PlanetSurfaceUnitVector operator *(
        PlanetSurfaceUnitVector vector,
        double scale) => new(
            vector.X * scale,
            vector.Y * scale,
            vector.Z * scale);
}

public readonly record struct PlanetSurfaceTangentFrame(
    PlanetSurfaceUnitVector East,
    PlanetSurfaceUnitVector Up,
    PlanetSurfaceUnitVector North)
{
    public double MaximumOrthogonalityError => Math.Max(
        Math.Abs(East.Dot(Up)),
        Math.Max(Math.Abs(East.Dot(North)), Math.Abs(Up.Dot(North))));

    public double MaximumUnitLengthError => Math.Max(
        Math.Abs(East.Length - 1.0),
        Math.Max(Math.Abs(Up.Length - 1.0), Math.Abs(North.Length - 1.0)));

    public double Handedness => East.Cross(Up).Dot(North);
}

public readonly record struct PlanetSurfaceCubeFaceAddress(
    CubeSphereFaceId Face,
    double U,
    double V,
    double LatitudeDegrees,
    double LongitudeDegrees)
{
    public string FaceName => PlanetSurfaceTopologyRuntime.FaceName(Face);
}

/// <summary>
/// Global spherical address model for a landable planet. Logical east/north is
/// kept as the backward-compatible persistence cover while TASK-168/170 project
/// it onto a sphere, a cube-sphere face and a continuously changing radial
/// tangent frame. Godot gameplay may therefore remain numerically bounded while
/// map/orbit/physics systems share one planet-global orientation contract.
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

    public PlanetSurfaceGeographicAddress FromGeographic(
        double latitudeDegrees,
        double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) ||
            !double.IsFinite(longitudeDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitudeDegrees),
                "Planet-surface geographic coordinates must be finite.");
        }

        double latitude = latitudeDegrees * Math.PI / 180.0;
        double longitude = longitudeDegrees * Math.PI / 180.0;
        NormalizeLatitudeLongitude(ref latitude, ref longitude);
        return new PlanetSurfaceGeographicAddress(
            latitude * 180.0 / Math.PI,
            longitude * 180.0 / Math.PI,
            RadiusMeters);
    }

    public PlanetSurfaceCanonicalLogicalAddress ToCanonicalLogical(
        PlanetSurfaceGeographicAddress address)
    {
        PlanetSurfaceGeographicAddress normalized = FromGeographic(
            address.LatitudeDegrees,
            address.LongitudeDegrees);
        return new PlanetSurfaceCanonicalLogicalAddress(
            normalized.LongitudeDegrees * Math.PI / 180.0 * RadiusMeters,
            normalized.LatitudeDegrees * Math.PI / 180.0 * RadiusMeters);
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
            cosLatitude * Math.Sin(longitude)).Normalized();
    }

    public PlanetSurfaceGeographicAddress FromUnitVector(
        PlanetSurfaceUnitVector direction)
    {
        PlanetSurfaceUnitVector normalized = direction.Normalized();
        double latitude = Math.Asin(Math.Clamp(normalized.Y, -1.0, 1.0));
        double longitude = Math.Atan2(normalized.Z, normalized.X);
        return FromGeographic(
            latitude * 180.0 / Math.PI,
            longitude * 180.0 / Math.PI);
    }

    public PlanetSurfaceTangentFrame BuildTangentFrame(
        PlanetSurfaceGeographicAddress address)
    {
        PlanetSurfaceGeographicAddress normalized = FromGeographic(
            address.LatitudeDegrees,
            address.LongitudeDegrees);
        double latitude = normalized.LatitudeDegrees * Math.PI / 180.0;
        double longitude = normalized.LongitudeDegrees * Math.PI / 180.0;
        PlanetSurfaceUnitVector up = ToUnitVector(normalized);
        PlanetSurfaceUnitVector east = new(
            -Math.Sin(longitude),
            0.0,
            Math.Cos(longitude));
        PlanetSurfaceUnitVector north = new(
            -Math.Sin(latitude) * Math.Cos(longitude),
            Math.Cos(latitude),
            -Math.Sin(latitude) * Math.Sin(longitude));
        return new PlanetSurfaceTangentFrame(
            east.Normalized(),
            up.Normalized(),
            north.Normalized());
    }

    public PlanetSurfaceCubeFaceAddress ToCubeFaceAddress(
        PlanetSurfaceGeographicAddress address)
    {
        PlanetSurfaceUnitVector direction = ToUnitVector(address);
        double ax = Math.Abs(direction.X);
        double ay = Math.Abs(direction.Y);
        double az = Math.Abs(direction.Z);
        double dominant = Math.Max(ax, Math.Max(ay, az));
        if (dominant <= 0.000000000001)
        {
            throw new InvalidOperationException(
                "A cube-sphere address requires a non-zero radial direction.");
        }

        double cx = direction.X / dominant;
        double cy = direction.Y / dominant;
        double cz = direction.Z / dominant;
        CubeSphereFaceId face;
        double u;
        double v;
        if (ax >= ay && ax >= az)
        {
            if (direction.X >= 0.0)
            {
                face = CubeSphereFaceId.PositiveX;
                u = -cz;
                v = cy;
            }
            else
            {
                face = CubeSphereFaceId.NegativeX;
                u = cz;
                v = cy;
            }
        }
        else if (ay >= az)
        {
            if (direction.Y >= 0.0)
            {
                face = CubeSphereFaceId.PositiveY;
                u = cx;
                v = -cz;
            }
            else
            {
                face = CubeSphereFaceId.NegativeY;
                u = cx;
                v = cz;
            }
        }
        else if (direction.Z >= 0.0)
        {
            face = CubeSphereFaceId.PositiveZ;
            u = cx;
            v = cy;
        }
        else
        {
            face = CubeSphereFaceId.NegativeZ;
            u = -cx;
            v = cy;
        }

        return new PlanetSurfaceCubeFaceAddress(
            face,
            Math.Clamp(u, -1.0, 1.0),
            Math.Clamp(v, -1.0, 1.0),
            address.LatitudeDegrees,
            address.LongitudeDegrees);
    }

    public PlanetSurfaceGeographicAddress GeodesicStep(
        PlanetSurfaceGeographicAddress start,
        double eastMeters,
        double northMeters)
    {
        if (!double.IsFinite(eastMeters) || !double.IsFinite(northMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eastMeters),
                "Geodesic displacement must be finite.");
        }

        PlanetSurfaceTangentFrame frame = BuildTangentFrame(start);
        PlanetSurfaceUnitVector tangent =
            frame.East * eastMeters + frame.North * northMeters;
        double distance = tangent.Length;
        if (distance <= 0.000000001)
        {
            return FromGeographic(
                start.LatitudeDegrees,
                start.LongitudeDegrees);
        }

        PlanetSurfaceUnitVector direction = tangent * (1.0 / distance);
        double angle = distance / RadiusMeters;
        PlanetSurfaceUnitVector destination =
            frame.Up * Math.Cos(angle) + direction * Math.Sin(angle);
        return FromUnitVector(destination);
    }

    public double GreatCircleDistanceMeters(
        double eastA,
        double northA,
        double eastB,
        double northB)
    {
        PlanetSurfaceUnitVector left = ToUnitVector(FromLogical(eastA, northA));
        PlanetSurfaceUnitVector right = ToUnitVector(FromLogical(eastB, northB));
        return GreatCircleDistanceMeters(left, right);
    }

    public double GreatCircleDistanceMeters(
        PlanetSurfaceGeographicAddress left,
        PlanetSurfaceGeographicAddress right) =>
        GreatCircleDistanceMeters(ToUnitVector(left), ToUnitVector(right));

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

    public static string FaceName(CubeSphereFaceId face) => face switch
    {
        CubeSphereFaceId.PositiveX => "+X",
        CubeSphereFaceId.NegativeX => "-X",
        CubeSphereFaceId.PositiveY => "+Y",
        CubeSphereFaceId.NegativeY => "-Y",
        CubeSphereFaceId.PositiveZ => "+Z",
        CubeSphereFaceId.NegativeZ => "-Z",
        _ => "?"
    };

    private double GreatCircleDistanceMeters(
        PlanetSurfaceUnitVector left,
        PlanetSurfaceUnitVector right)
    {
        double dot = Math.Clamp(left.Dot(right), -1.0, 1.0);
        return Math.Acos(dot) * RadiusMeters;
    }

    private static void NormalizeLatitudeLongitude(
        ref double latitudeRadians,
        ref double longitudeRadians)
    {
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
