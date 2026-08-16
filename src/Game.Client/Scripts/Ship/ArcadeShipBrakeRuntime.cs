using System;
using Godot;

/// <summary>
/// Pure monotonic braking contract shared by runtime and acceptance tests.
/// A brake can only reduce velocity magnitude toward zero; it never creates
/// velocity in the opposite direction, regardless of hold duration or frame size.
/// </summary>
public static class ArcadeShipBrakeRuntime
{
    public static Vector3 ApplyMonotonicBrake(
        Vector3 velocity,
        float decelerationMetersPerSecondSquared,
        float deltaSeconds)
    {
        if (!velocity.IsFinite() ||
            !float.IsFinite(decelerationMetersPerSecondSquared) ||
            !float.IsFinite(deltaSeconds) ||
            decelerationMetersPerSecondSquared <= 0.0f ||
            deltaSeconds <= 0.0f)
        {
            return velocity.IsFinite() ? velocity : Vector3.Zero;
        }

        float speed = velocity.Length();
        if (speed <= 0.0001f)
        {
            return Vector3.Zero;
        }

        float nextSpeed = Math.Max(
            0.0f,
            speed - (decelerationMetersPerSecondSquared * deltaSeconds));
        if (nextSpeed <= 0.0001f)
        {
            return Vector3.Zero;
        }

        return velocity * (nextSpeed / speed);
    }

    /// <summary>
    /// Applies the brake after environmental forces. The result may not be
    /// faster than the pre-force velocity and may never cross through zero
    /// into the opposite half-space while the brake remains held.
    /// </summary>
    public static Vector3 ApplyMonotonicBrakeEnvelope(
        Vector3 velocityBeforeForces,
        Vector3 velocityAfterForces,
        float decelerationMetersPerSecondSquared,
        float deltaSeconds)
    {
        if (!velocityBeforeForces.IsFinite() || !velocityAfterForces.IsFinite())
        {
            return Vector3.Zero;
        }

        float beforeSpeed = velocityBeforeForces.Length();
        if (beforeSpeed <= 0.0001f)
        {
            return Vector3.Zero;
        }

        Vector3 referenceDirection = velocityBeforeForces / beforeSpeed;
        if (velocityAfterForces.Dot(referenceDirection) <= 0.0f)
        {
            return Vector3.Zero;
        }

        float allowedSpeed = Math.Max(
            0.0f,
            beforeSpeed -
                (Math.Max(0.0f, decelerationMetersPerSecondSquared) *
                 Math.Max(0.0f, deltaSeconds)));
        if (allowedSpeed <= 0.0001f)
        {
            return Vector3.Zero;
        }

        float candidateSpeed = velocityAfterForces.Length();
        if (candidateSpeed <= 0.0001f)
        {
            return Vector3.Zero;
        }

        float resultSpeed = Math.Min(candidateSpeed, allowedSpeed);
        Vector3 candidateDirection = velocityAfterForces / candidateSpeed;
        if (candidateDirection.Dot(referenceDirection) <= 0.0f)
        {
            return Vector3.Zero;
        }

        return candidateDirection * resultSpeed;
    }
}
