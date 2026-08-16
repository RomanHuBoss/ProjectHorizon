using System;
using Godot;

public sealed record PlanetSurfacePhysicalFrameState(
    string PlanetId,
    PlanetSurfaceRadialFrameState Radial,
    double OriginEastMeters,
    double OriginNorthMeters,
    Basis SurfaceBasis,
    Transform3D GameplayTransform)
{
    public Vector3 WorldEast => SurfaceBasis.X.Normalized();
    public Vector3 WorldUp => SurfaceBasis.Y.Normalized();
    public Vector3 WorldNorth => SurfaceBasis.Z.Normalized();

    public Vector3 LogicalToWorld(Vector3 logicalPosition) =>
        GameplayTransform * logicalPosition;

    public Vector3 WorldToLogical(Vector3 worldPosition) =>
        GameplayTransform.AffineInverse() * worldPosition;
}

/// <summary>
/// TASK-172 physical tangent-frame bridge. The planet remains globally spherical,
/// while a bounded Godot physics patch is rotated so its local X/Y/Z axes are the
/// current planet East/Up/North axes. Logical coordinates remain absolute and
/// persistent; only the bounded physical representation rotates/recentres.
/// </summary>
public sealed class PlanetSurfacePhysicalFrameRuntime
{
    public PlanetSurfacePhysicalFrameRuntime(PlanetSurfaceRadialFrameRuntime radial)
    {
        Radial = radial ?? throw new ArgumentNullException(nameof(radial));
    }

    public PlanetSurfaceRadialFrameRuntime Radial { get; }

    public PlanetSurfacePhysicalFrameState Build(
        double originEastMeters,
        double originNorthMeters)
    {
        PlanetSurfaceRadialFrameState radial = Radial.Build(
            originEastMeters,
            originNorthMeters);
        Basis basis = ToGodotBasis(radial.GlobalFrame);
        Vector3 logicalOrigin = new(
            (float)-originEastMeters,
            0.0f,
            (float)-originNorthMeters);
        Transform3D gameplayTransform = new(
            basis,
            basis * logicalOrigin);
        return new PlanetSurfacePhysicalFrameState(
            radial.PlanetId,
            radial,
            originEastMeters,
            originNorthMeters,
            basis,
            gameplayTransform);
    }

    public static Basis ToGodotBasis(PlanetSurfaceTangentFrame frame)
    {
        Vector3 east = ToVector3(frame.East).Normalized();
        Vector3 up = ToVector3(frame.Up).Normalized();
        Vector3 north = ToVector3(frame.North).Normalized();
        return new Basis(east, up, north).Orthonormalized();
    }

    public static Vector3 MapPoint(
        Transform3D previousFrame,
        Transform3D nextFrame,
        Vector3 worldPoint)
    {
        Vector3 logical = previousFrame.AffineInverse() * worldPoint;
        return nextFrame * logical;
    }

    public static Vector3 MapCurvedPoint(
        Transform3D previousFrame,
        Transform3D nextFrame,
        Vector3 worldPoint,
        PlanetSurfaceCurvedPatchDescriptor previousPatch,
        PlanetSurfaceCurvedPatchDescriptor nextPatch)
    {
        Vector3 logical = previousFrame.AffineInverse() * worldPoint;
        double semanticHeight = logical.Y + previousPatch.TangentSagMeters(
            logical.X, logical.Z);
        logical.Y = (float)(semanticHeight - nextPatch.TangentSagMeters(
            logical.X, logical.Z));
        return nextFrame * logical;
    }

    public static Vector3 MapVector(
        Basis previousBasis,
        Basis nextBasis,
        Vector3 worldVector)
    {
        Vector3 local = previousBasis.Inverse() * worldVector;
        return nextBasis * local;
    }

    public static Basis MapBasis(
        Basis previousBasis,
        Basis nextBasis,
        Basis worldBasis)
    {
        Basis local = previousBasis.Inverse() * worldBasis;
        return (nextBasis * local).Orthonormalized();
    }

    public static Basis BuildUprightBasis(Vector3 forward, Vector3 worldUp)
    {
        if (worldUp.LengthSquared() <= 0.000001f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldUp),
                "Surface up vector must be non-zero.");
        }

        Vector3 up = worldUp.Normalized();
        Vector3 tangentForward = forward.Slide(up);
        if (tangentForward.LengthSquared() <= 0.000001f)
        {
            Vector3 reference = Math.Abs(up.Dot(Vector3.Forward)) > 0.95f
                ? Vector3.Right
                : Vector3.Forward;
            tangentForward = reference.Slide(up);
        }
        tangentForward = tangentForward.Normalized();
        Vector3 right = tangentForward.Cross(up).Normalized();
        Vector3 back = right.Cross(up).Normalized();
        return new Basis(right, up, back).Orthonormalized();
    }

    public static double MaximumAxisErrorDegrees(Basis left, Basis right)
    {
        return Math.Max(
            AxisError(left.X, right.X),
            Math.Max(
                AxisError(left.Y, right.Y),
                AxisError(left.Z, right.Z)));
    }

    private static double AxisError(Vector3 left, Vector3 right)
    {
        if (left.LengthSquared() <= 0.0000001f ||
            right.LengthSquared() <= 0.0000001f)
        {
            return 180.0;
        }
        double dot = Math.Clamp(
            left.Normalized().Dot(right.Normalized()),
            -1.0f,
            1.0f);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private static Vector3 ToVector3(PlanetSurfaceUnitVector value) => new(
        (float)value.X,
        (float)value.Y,
        (float)value.Z);
}
