using System;
using Godot;

public readonly record struct OrbitalStationCollisionHit(
    float SegmentFraction,
    Vector3 LocalShipCenterAtImpact,
    Vector3 LocalSurfaceNormal);

/// <summary>
/// Continuous segment-vs-expanded-box sweep used as a high-speed supplement to
/// Godot's CharacterBody3D/StaticBody3D contact solver. Each station collision
/// shape is transformed into its own local frame before calling this routine,
/// so rotated compound shapes remain supported without a second station model.
/// </summary>
public static class OrbitalStationCollisionRuntime
{
    public const int MinimumPhysicalShapeCount = 20;
    public const float SeparationPaddingMeters = 0.12f;

    public static bool TrySweepExpandedAabb(
        Vector3 localStart,
        Vector3 localEnd,
        Vector3 halfExtents,
        float shipRadius,
        out OrbitalStationCollisionHit hit)
    {
        hit = default;
        if (!localStart.IsFinite() || !localEnd.IsFinite() ||
            !halfExtents.IsFinite() || !float.IsFinite(shipRadius) ||
            shipRadius < 0.0f || halfExtents.X <= 0.0f ||
            halfExtents.Y <= 0.0f || halfExtents.Z <= 0.0f)
        {
            return false;
        }

        Vector3 expanded = halfExtents + Vector3.One * shipRadius;
        Vector3 delta = localEnd - localStart;
        if (IsInside(localStart, expanded))
        {
            ResolveInsideContact(localStart, expanded, out Vector3 point, out Vector3 normal);
            hit = new OrbitalStationCollisionHit(0.0f, point, normal);
            return true;
        }

        float enter = 0.0f;
        float exit = 1.0f;
        Vector3 enterNormal = Vector3.Zero;
        if (!SweepAxis(localStart.X, delta.X, expanded.X, Vector3.Right,
                ref enter, ref exit, ref enterNormal) ||
            !SweepAxis(localStart.Y, delta.Y, expanded.Y, Vector3.Up,
                ref enter, ref exit, ref enterNormal) ||
            !SweepAxis(localStart.Z, delta.Z, expanded.Z, Vector3.Back,
                ref enter, ref exit, ref enterNormal) ||
            enter < 0.0f || enter > 1.0f || enter > exit)
        {
            return false;
        }

        if (enterNormal.LengthSquared() <= 0.000001f)
        {
            Vector3 fallback = -delta;
            enterNormal = fallback.LengthSquared() <= 0.000001f
                ? Vector3.Up
                : fallback.Normalized();
        }

        hit = new OrbitalStationCollisionHit(
            enter,
            localStart + delta * enter,
            enterNormal);
        return true;
    }

    private static bool SweepAxis(
        float start,
        float delta,
        float halfExtent,
        Vector3 positiveAxis,
        ref float enter,
        ref float exit,
        ref Vector3 enterNormal)
    {
        const float epsilon = 0.000001f;
        if (MathF.Abs(delta) <= epsilon)
        {
            return start >= -halfExtent && start <= halfExtent;
        }

        float nearPlane = delta > 0.0f ? -halfExtent : halfExtent;
        float farPlane = delta > 0.0f ? halfExtent : -halfExtent;
        float near = (nearPlane - start) / delta;
        float far = (farPlane - start) / delta;
        Vector3 normal = delta > 0.0f ? -positiveAxis : positiveAxis;

        if (near > enter)
        {
            enter = near;
            enterNormal = normal;
        }
        if (far < exit)
        {
            exit = far;
        }
        return enter <= exit;
    }

    private static bool IsInside(Vector3 point, Vector3 halfExtents) =>
        MathF.Abs(point.X) <= halfExtents.X &&
        MathF.Abs(point.Y) <= halfExtents.Y &&
        MathF.Abs(point.Z) <= halfExtents.Z;

    private static void ResolveInsideContact(
        Vector3 point,
        Vector3 halfExtents,
        out Vector3 surfacePoint,
        out Vector3 normal)
    {
        float xDistance = halfExtents.X - MathF.Abs(point.X);
        float yDistance = halfExtents.Y - MathF.Abs(point.Y);
        float zDistance = halfExtents.Z - MathF.Abs(point.Z);
        surfacePoint = point;

        if (xDistance <= yDistance && xDistance <= zDistance)
        {
            float sign = point.X < 0.0f ? -1.0f : 1.0f;
            normal = Vector3.Right * sign;
            surfacePoint.X = halfExtents.X * sign;
        }
        else if (yDistance <= zDistance)
        {
            float sign = point.Y < 0.0f ? -1.0f : 1.0f;
            normal = Vector3.Up * sign;
            surfacePoint.Y = halfExtents.Y * sign;
        }
        else
        {
            float sign = point.Z < 0.0f ? -1.0f : 1.0f;
            normal = Vector3.Back * sign;
            surfacePoint.Z = halfExtents.Z * sign;
        }
    }
}
