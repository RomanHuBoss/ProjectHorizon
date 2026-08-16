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
    public static Vector2 AccumulateMouseSteering(
        Vector2 current,
        Vector2 relative,
        float sensitivity,
        float gain,
        bool invertPitch,
        bool invertYaw)
    {
        if (!current.IsFinite() || !relative.IsFinite() ||
            !float.IsFinite(sensitivity) || !float.IsFinite(gain) ||
            sensitivity < 0.0f || gain < 0.0f)
        {
            return Vector2.Zero;
        }

        float pitchSign = invertPitch ? 1.0f : -1.0f;
        float yawSign = invertYaw ? 1.0f : -1.0f;
        Vector2 accumulated = current + new Vector2(
            relative.Y * sensitivity * gain * pitchSign,
            relative.X * sensitivity * gain * yawSign);
        return accumulated.Clamp(
            new Vector2(-1.0f, -1.0f),
            new Vector2(1.0f, 1.0f));
    }

    public static Vector3 BuildMouseAttitudeCommand(
        Vector2 steering,
        float bankFactor)
    {
        if (!steering.IsFinite() || !float.IsFinite(bankFactor))
        {
            return Vector3.Zero;
        }

        float pitch = Mathf.Clamp(steering.X, -1.0f, 1.0f);
        float yaw = Mathf.Clamp(steering.Y, -1.0f, 1.0f);
        float roll = Mathf.Clamp(yaw * Math.Max(0.0f, bankFactor), -1.0f, 1.0f);
        return new Vector3(pitch, yaw, roll);
    }

    public static Vector2 DecayMouseSteering(
        Vector2 current,
        float decayRatePerSecond,
        float deltaSeconds)
    {
        if (!current.IsFinite() || !float.IsFinite(decayRatePerSecond) ||
            !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            return current.IsFinite() ? current : Vector2.Zero;
        }

        float rate = Math.Max(0.1f, decayRatePerSecond);
        float blend = 1.0f - MathF.Exp(-rate * deltaSeconds);
        Vector2 decayed = current.Lerp(
            Vector2.Zero,
            Mathf.Clamp(blend, 0.0f, 1.0f));
        return decayed.LengthSquared() < 0.000004f
            ? Vector2.Zero
            : decayed;
    }

}
