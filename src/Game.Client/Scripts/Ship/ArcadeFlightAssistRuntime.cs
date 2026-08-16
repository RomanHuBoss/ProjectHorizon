using System;
using Godot;

public static class ArcadeFlightAssistRuntime
{
    public const float DefaultVelocityAlignmentRate = 3.2f;
    public const float MinimumAlignmentSpeed = 0.35f;
    public static Vector3 AlignVelocityToShipAxes(
        Vector3 velocity,
        Basis shipBasis,
        ShipControlCommand command,
        bool flightAssistEnabled,
        float deltaSeconds,
        float responseRate = DefaultVelocityAlignmentRate)
    {
        if (!flightAssistEnabled || deltaSeconds <= 0.0f ||
            !float.IsFinite(deltaSeconds) || !float.IsFinite(responseRate) ||
            responseRate <= 0.0f || !velocity.IsFinite())
        {
            return velocity;
        }

        float speed = velocity.Length();
        if (speed < MinimumAlignmentSpeed)
        {
            return velocity;
        }

        Basis basis = shipBasis.Orthonormalized();
        Vector3 localIntent = new(
            command.Strafe,
            command.Lift,
            -command.Forward);
        if (localIntent.LengthSquared() <= 0.000001f)
        {
            // No active translation command: classic arcade flight keeps the
            // existing speed but bends its direction toward the ship's nose.
            localIntent = Vector3.Forward;
        }

        Vector3 desiredDirection = (basis * localIntent.Normalized()).Normalized();
        Vector3 currentDirection = velocity / speed;
        float blend = 1.0f - MathF.Exp(-responseRate * deltaSeconds);
        blend = Mathf.Clamp(blend, 0.0f, 1.0f);
        Vector3 blended = currentDirection.Lerp(desiredDirection, blend);
        if (blended.LengthSquared() <= 0.000001f)
        {
            blended = desiredDirection;
        }

        return blended.Normalized() * speed;
    }

    public static float HeadingErrorDegrees(Vector3 velocity, Basis shipBasis)
    {
        if (velocity.LengthSquared() <= 0.000001f)
        {
            return 0.0f;
        }

        Vector3 forward = -shipBasis.Orthonormalized().Z;
        float dot = Mathf.Clamp(velocity.Normalized().Dot(forward), -1.0f, 1.0f);
        return Mathf.RadToDeg(Mathf.Acos(dot));
    }
}
