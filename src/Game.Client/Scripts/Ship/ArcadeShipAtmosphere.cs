using System;
using Godot;

public partial class ArcadeShipController
{
    [Export]
    public NodePath AtmosphereBodyPath { get; set; } = new("../AtmospherePlanet");

    [Export(PropertyHint.Range, "10.0,10000.0,1.0")]
    public float AtmosphereSurfaceRadius { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "5.0,1000.0,1.0")]
    public float AtmosphereHeight { get; set; } = 90.0f;

    [Export(PropertyHint.Range, "0.0,30.0,0.1")]
    public float AtmosphereGravityAcceleration { get; set; } = 7.5f;

    [Export(PropertyHint.Range, "0.0,3.0,0.01")]
    public float AtmosphereLiftMultiplier { get; set; } = 1.05f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float AtmosphereMinimumForwardSpeed { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.0,100.0,0.5")]
    public float AtmosphereMinimumSpeedAssist { get; set; } = 18.0f;

    [Export(PropertyHint.Range, "0.0001,0.2,0.0001")]
    public float AtmosphereDragCoefficient { get; set; } = 0.012f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float AtmosphereMaximumDragAcceleration { get; set; } = 34.0f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float AtmosphereMaximumClimbSpeed { get; set; } = 16.0f;

    [Export(PropertyHint.Range, "2.0,100.0,0.5")]
    public float SurfaceSafetyActivationAltitude { get; set; } = 32.0f;

    [Export(PropertyHint.Range, "1.0,30.0,0.5")]
    public float SurfaceSafetyClearance { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "5.0,200.0,1.0")]
    public float SurfaceSafetyAcceleration { get; set; } = 58.0f;

    [Export(PropertyHint.Range, "0.5,20.0,0.5")]
    public float SurfaceHardFloor { get; set; } = 5.0f;

    private Node3D? _atmosphereBody;
    private bool _wasInAtmosphere;

    public bool HasAtmosphereReference => _atmosphereBody is not null;
    public bool InAtmosphere { get; private set; }
    public float AtmosphereBlend { get; private set; }
    public float AltitudeAboveSurface { get; private set; } = float.PositiveInfinity;
    public float RadialSpeed { get; private set; }
    public float ForwardAirSpeed { get; private set; }
    public Vector3 AtmosphereRadialUp { get; private set; } = Vector3.Up;
    public bool StallProtectionActive { get; private set; }
    public bool SurfaceSafetyActive { get; private set; }
    public int AtmosphereEntryCount { get; private set; }
    public int AtmosphereExitCount { get; private set; }
    public int AtmosphereDragApplications { get; private set; }
    public int MinimumSpeedAssistApplications { get; private set; }
    public int ClimbLimitApplications { get; private set; }
    public int SurfaceSafetyApplications { get; private set; }
    public int SurfaceRecoveryCount { get; private set; }

    public Vector3 AtmosphereCenter => _atmosphereBody?.GlobalPosition ?? Vector3.Zero;

    public void SetAtmosphereBody(Node3D? atmosphereBody)
    {
        _atmosphereBody = atmosphereBody;
        _wasInAtmosphere = false;
        UpdateAtmosphereContext();
    }

    public void SetKinematicState(
        Transform3D globalTransform,
        Vector3 velocity,
        Vector3 angularVelocityLocal)
    {
        GlobalTransform = new Transform3D(
            globalTransform.Basis.Orthonormalized(),
            globalTransform.Origin);
        Velocity = velocity;
        AngularVelocityLocal = angularVelocityLocal;
        UpdateAtmosphereContext();
        UpdateDiagnostics();
    }

    public Transform3D CreateAtmosphericTransform(
        float altitude,
        Vector3 radialDirection,
        Vector3 preferredForward)
    {
        Vector3 radialUp = radialDirection.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : radialDirection.Normalized();
        Vector3 forward = preferredForward.Slide(radialUp);
        if (forward.LengthSquared() <= 0.000001f)
        {
            Vector3 fallback = Math.Abs(radialUp.Dot(Vector3.Forward)) > 0.95f
                ? Vector3.Right
                : Vector3.Forward;
            forward = fallback.Slide(radialUp);
        }

        forward = forward.Normalized();
        Vector3 right = forward.Cross(radialUp).Normalized();
        Vector3 back = right.Cross(radialUp).Normalized();
        Basis basis = new Basis(right, radialUp, back).Orthonormalized();
        Vector3 position = AtmosphereCenter +
            (radialUp * (AtmosphereSurfaceRadius + Math.Max(0.0f, altitude)));
        return new Transform3D(basis, position);
    }

    private void InitializeAtmosphere()
    {
        _atmosphereBody = GetNodeOrNull<Node3D>(AtmosphereBodyPath);
        if (_atmosphereBody is null)
        {
            GD.PushWarning(
                "Arcade ship has no atmosphere reference; atmospheric mode disabled.");
        }

        UpdateAtmosphereContext();
    }

    private void UpdateAtmosphereContext()
    {
        if (_atmosphereBody is null)
        {
            InAtmosphere = false;
            AtmosphereBlend = 0.0f;
            AltitudeAboveSurface = float.PositiveInfinity;
            RadialSpeed = 0.0f;
            ForwardAirSpeed = Math.Max(0.0f, -LocalVelocity.Z);
            StallProtectionActive = false;
            SurfaceSafetyActive = false;
            return;
        }

        Vector3 offset = GlobalPosition - _atmosphereBody.GlobalPosition;
        float radialDistance = offset.Length();
        AtmosphereRadialUp = radialDistance <= 0.0001f
            ? Vector3.Up
            : offset / radialDistance;
        AltitudeAboveSurface = radialDistance - AtmosphereSurfaceRadius;

        float linearBlend = 1.0f - Mathf.Clamp(
            AltitudeAboveSurface / Math.Max(1.0f, AtmosphereHeight),
            0.0f,
            1.0f);
        AtmosphereBlend = linearBlend * linearBlend *
            (3.0f - (2.0f * linearBlend));
        InAtmosphere = AtmosphereBlend > 0.01f;
        RadialSpeed = Velocity.Dot(AtmosphereRadialUp);
        Basis basis = GlobalTransform.Basis.Orthonormalized();
        LocalVelocity = basis.Inverse() * Velocity;
        ForwardAirSpeed = Math.Max(0.0f, -LocalVelocity.Z);

        if (InAtmosphere != _wasInAtmosphere)
        {
            if (InAtmosphere)
            {
                AtmosphereEntryCount++;
                GD.Print(
                    "Ship atmosphere transition: ENTER " +
                    $"altitude={AltitudeAboveSurface:F1} m");
            }
            else
            {
                AtmosphereExitCount++;
                GD.Print(
                    "Ship atmosphere transition: EXIT " +
                    $"altitude={AltitudeAboveSurface:F1} m");
            }

            _wasInAtmosphere = InAtmosphere;
        }
    }

    private void ApplyAtmosphericFlight(
        ShipControlCommand command,
        float deltaSeconds)
    {
        StallProtectionActive = false;
        SurfaceSafetyActive = false;

        if (_atmosphereBody is null || AtmosphereBlend <= 0.0f)
        {
            return;
        }

        Basis basis = GlobalTransform.Basis.Orthonormalized();
        Vector3 forward = -basis.Z;
        float blend = AtmosphereBlend;

        float gravityAcceleration = AtmosphereGravityAcceleration * blend;
        Velocity -= AtmosphereRadialUp * gravityAcceleration * deltaSeconds;

        float liftRatio = Mathf.Clamp(
            ForwardAirSpeed / Math.Max(1.0f, AtmosphereMinimumForwardSpeed),
            0.0f,
            1.15f);
        float liftAcceleration = gravityAcceleration *
            liftRatio * AtmosphereLiftMultiplier;
        Velocity += AtmosphereRadialUp * liftAcceleration * deltaSeconds;

        float speed = Velocity.Length();
        if (speed > 0.05f)
        {
            float dragAcceleration = Math.Min(
                AtmosphereMaximumDragAcceleration,
                AtmosphereDragCoefficient * blend * speed * speed);
            if (dragAcceleration > 0.01f)
            {
                Velocity = Velocity.MoveToward(
                    Vector3.Zero,
                    dragAcceleration * deltaSeconds);
                AtmosphereDragApplications++;
            }
        }

        bool allowMinimumSpeedAssist =
            !command.Brake &&
            command.Forward >= -0.05f &&
            ForwardAirSpeed < AtmosphereMinimumForwardSpeed;
        if (allowMinimumSpeedAssist)
        {
            float deficit = AtmosphereMinimumForwardSpeed - ForwardAirSpeed;
            float assistAcceleration = Math.Min(
                AtmosphereMinimumSpeedAssist,
                deficit * 2.0f) * blend;
            Velocity += forward * assistAcceleration * deltaSeconds;
            StallProtectionActive = true;
            MinimumSpeedAssistApplications++;
        }

        RadialSpeed = Velocity.Dot(AtmosphereRadialUp);
        if (RadialSpeed > AtmosphereMaximumClimbSpeed)
        {
            Velocity -= AtmosphereRadialUp *
                (RadialSpeed - AtmosphereMaximumClimbSpeed);
            RadialSpeed = AtmosphereMaximumClimbSpeed;
            ClimbLimitApplications++;
        }

        float inwardSpeed = Math.Max(0.0f, -RadialSpeed);
        float stoppingDistance =
            (inwardSpeed * inwardSpeed) /
            Math.Max(1.0f, 2.0f * SurfaceSafetyAcceleration);
        float triggerAltitude = Math.Max(
            SurfaceSafetyActivationAltitude,
            SurfaceSafetyClearance + stoppingDistance);

        if (AltitudeAboveSurface < triggerAltitude)
        {
            float normalizedDanger = Mathf.Clamp(
                (triggerAltitude - AltitudeAboveSurface) /
                Math.Max(1.0f, triggerAltitude - SurfaceHardFloor),
                0.0f,
                1.0f);
            float safetyAcceleration = SurfaceSafetyAcceleration *
                (0.25f + (0.95f * normalizedDanger));
            Velocity += AtmosphereRadialUp * safetyAcceleration * deltaSeconds;
            SurfaceSafetyActive = true;
            SurfaceSafetyApplications++;

            if (AltitudeAboveSurface <= SurfaceSafetyClearance &&
                Velocity.Dot(AtmosphereRadialUp) < 0.0f)
            {
                float inwardComponent = Velocity.Dot(AtmosphereRadialUp);
                Velocity -= AtmosphereRadialUp * inwardComponent;
            }
        }
    }

    private void ApplyAtmosphericSurfaceCorrection()
    {
        if (_atmosphereBody is null)
        {
            return;
        }

        UpdateAtmosphereContext();
        if (AltitudeAboveSurface >= SurfaceHardFloor)
        {
            return;
        }

        Vector3 correctedPosition = AtmosphereCenter +
            (AtmosphereRadialUp *
                (AtmosphereSurfaceRadius + SurfaceHardFloor));
        GlobalPosition = correctedPosition;

        float inwardComponent = Velocity.Dot(AtmosphereRadialUp);
        if (inwardComponent < 0.0f)
        {
            Velocity -= AtmosphereRadialUp * inwardComponent;
        }

        SurfaceSafetyActive = true;
        SurfaceRecoveryCount++;
        GD.PushWarning(
            "Atmospheric surface recovery applied: " +
            $"altitude={AltitudeAboveSurface:F2} m");
        UpdateAtmosphereContext();
    }
}
