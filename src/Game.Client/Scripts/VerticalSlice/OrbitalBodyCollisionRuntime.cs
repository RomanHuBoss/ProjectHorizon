using System;
using Godot;

public readonly record struct OrbitalBodyCollisionHit(
    string BodyId,
    StarSystemBodyKind Kind,
    Vector3 Center,
    float DisplayRadius,
    float CollisionRadius,
    float SegmentFraction,
    Vector3 ShipCenterAtImpact,
    Vector3 SurfaceNormal);

public static class OrbitalBodyCollisionRuntime
{
    public const float ShipCollisionRadiusMeters = 4.0f;
    public const float SurfaceSafetyMarginMeters = 1.5f;

    public static bool TrySweepSphere(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float radius,
        out float segmentFraction,
        out Vector3 shipCenterAtImpact,
        out Vector3 surfaceNormal)
    {
        segmentFraction = 0.0f;
        shipCenterAtImpact = start;
        surfaceNormal = Vector3.Up;
        if (!start.IsFinite() || !end.IsFinite() || !center.IsFinite() ||
            !float.IsFinite(radius) || radius <= 0.0f)
        {
            return false;
        }

        Vector3 delta = end - start;
        Vector3 offset = start - center;
        float c = offset.Dot(offset) - (radius * radius);

        // A body already inside the solid envelope is an invalid state. Return
        // an immediate contact so the caller can recover it instead of allowing
        // another frame of penetration.
        if (c <= 0.0f)
        {
            Vector3 normalInside = start - center;
            if (normalInside.LengthSquared() <= 0.000001f)
            {
                normalInside = end - center;
            }
            if (normalInside.LengthSquared() <= 0.000001f)
            {
                normalInside = Vector3.Up;
            }

            surfaceNormal = normalInside.Normalized();
            shipCenterAtImpact = center + surfaceNormal * radius;
            return true;
        }

        float a = delta.Dot(delta);
        if (a <= 0.000001f)
        {
            return false;
        }

        float b = 2.0f * offset.Dot(delta);
        float discriminant = (b * b) - (4.0f * a * c);
        if (discriminant < 0.0f)
        {
            return false;
        }

        float root = MathF.Sqrt(discriminant);
        float inverse = 0.5f / a;
        float first = (-b - root) * inverse;
        float second = (-b + root) * inverse;
        float t = first is >= 0.0f and <= 1.0f
            ? first
            : second is >= 0.0f and <= 1.0f
                ? second
                : float.NaN;
        if (!float.IsFinite(t))
        {
            return false;
        }

        segmentFraction = t;
        shipCenterAtImpact = start + delta * t;
        Vector3 normal = shipCenterAtImpact - center;
        surfaceNormal = normal.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : normal.Normalized();
        return true;
    }

    public static bool CrossedOuterShell(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float shellRadius)
    {
        if (!start.IsFinite() || !end.IsFinite() || !center.IsFinite() ||
            !float.IsFinite(shellRadius) || shellRadius <= 0.0f)
        {
            return false;
        }

        float startDistance = start.DistanceTo(center);
        float endDistance = end.DistanceTo(center);
        return startDistance > shellRadius && endDistance <= shellRadius;
    }
}
