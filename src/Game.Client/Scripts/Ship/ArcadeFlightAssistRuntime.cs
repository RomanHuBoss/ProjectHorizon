using System;
using Godot;

public static class ArcadeFlightAssistRuntime
{
    public const float DefaultVelocityAlignmentRate = 3.2f;
    public const float MinimumAlignmentSpeed = 0.35f;
    public const float DefaultVirtualStickDeadZone = 0.045f;
    public const float DefaultVirtualStickResponseExponent = 1.55f;
    public const float DefaultCoordinatedYawFactor = 0.18f;

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

    /// <summary>
    /// TASK-180.3: stateful virtual flight stick. Mouse deltas move a virtual
    /// control handle; stopping physical mouse motion does not erase the command.
    /// The pilot must move the mouse back toward centre (or explicitly recenter)
    /// just like returning a joystick to neutral.
    /// Vector X = pitch command axis; Vector Y = horizontal/roll command axis.
    /// </summary>
    public static Vector2 AccumulateVirtualFlightStick(
        Vector2 current,
        Vector2 relative,
        float sensitivity,
        float gain,
        bool invertPitch,
        bool invertHorizontal)
    {
        if (!current.IsFinite() || !relative.IsFinite() ||
            !float.IsFinite(sensitivity) || !float.IsFinite(gain) ||
            sensitivity < 0.0f || gain < 0.0f)
        {
            return Vector2.Zero;
        }

        float pitchSign = invertPitch ? 1.0f : -1.0f;
        float horizontalSign = invertHorizontal ? -1.0f : 1.0f;
        Vector2 accumulated = current + new Vector2(
            relative.Y * sensitivity * gain * pitchSign,
            relative.X * sensitivity * gain * horizontalSign);
        return new Vector2(
            Mathf.Clamp(accumulated.X, -1.0f, 1.0f),
            Mathf.Clamp(accumulated.Y, -1.0f, 1.0f));
    }

    public static float ApplyVirtualStickResponse(
        float value,
        float deadZone = DefaultVirtualStickDeadZone,
        float exponent = DefaultVirtualStickResponseExponent)
    {
        if (!float.IsFinite(value) || !float.IsFinite(deadZone) ||
            !float.IsFinite(exponent))
        {
            return 0.0f;
        }

        float clamped = Mathf.Clamp(value, -1.0f, 1.0f);
        float absolute = Math.Abs(clamped);
        float dz = Mathf.Clamp(deadZone, 0.0f, 0.45f);
        if (absolute <= dz)
        {
            return 0.0f;
        }

        float normalized = (absolute - dz) / Math.Max(0.0001f, 1.0f - dz);
        float shaped = MathF.Pow(
            Mathf.Clamp(normalized, 0.0f, 1.0f),
            Math.Max(1.0f, exponent));
        return MathF.CopySign(shaped, clamped);
    }

    /// <summary>
    /// Roll-dominant aircraft/Elite-like mouse control. Horizontal mouse stick
    /// primarily rolls the hull and adds only a modest coordinated yaw component;
    /// vertical stick controls pitch. This is deliberately not FPS yaw-look.
    /// </summary>
    public static Vector3 BuildVirtualStickAttitudeCommand(
        Vector2 virtualStick,
        float deadZone = DefaultVirtualStickDeadZone,
        float responseExponent = DefaultVirtualStickResponseExponent,
        float coordinatedYawFactor = DefaultCoordinatedYawFactor)
    {
        if (!virtualStick.IsFinite() || !float.IsFinite(coordinatedYawFactor))
        {
            return Vector3.Zero;
        }

        float pitch = ApplyVirtualStickResponse(
            virtualStick.X,
            deadZone,
            responseExponent);
        float horizontal = ApplyVirtualStickResponse(
            virtualStick.Y,
            deadZone,
            responseExponent);
        // Godot local +Y yaw turns the nose left for positive angles while
        // local Forward (+roll command below) banks right for positive angles.
        // Therefore a right-stick command needs +roll and -yaw.
        float yaw = Mathf.Clamp(
            -horizontal * Mathf.Clamp(coordinatedYawFactor, 0.0f, 0.45f),
            -1.0f,
            1.0f);
        float roll = Mathf.Clamp(horizontal, -1.0f, 1.0f);
        return new Vector3(pitch, yaw, roll);
    }

    // Historical TASK-178.6/180.2 helpers are retained for save/test compatibility,
    // but the live controller no longer uses the impulse+decay model.
    public static Vector2 AccumulateMouseSteering(
        Vector2 current,
        Vector2 relative,
        float sensitivity,
        float gain,
        bool invertPitch,
        bool invertYaw) => AccumulateVirtualFlightStick(
            current,
            relative,
            sensitivity,
            gain,
            invertPitch,
            invertYaw);

    public static Vector3 BuildMouseAttitudeCommand(
        Vector2 steering,
        float bankFactor) => BuildVirtualStickAttitudeCommand(
            steering,
            DefaultVirtualStickDeadZone,
            DefaultVirtualStickResponseExponent,
            Math.Min(DefaultCoordinatedYawFactor, Math.Max(0.0f, bankFactor)));

    public static Vector2 DecayMouseSteering(
        Vector2 current,
        float decayRatePerSecond,
        float deltaSeconds)
    {
        if (!current.IsFinite())
        {
            return Vector2.Zero;
        }

        if (!float.IsFinite(decayRatePerSecond) ||
            !float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            return current;
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
