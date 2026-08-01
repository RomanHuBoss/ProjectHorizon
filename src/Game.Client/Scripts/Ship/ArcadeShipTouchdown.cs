using System;
using System.Collections.Generic;
using Godot;

public enum ShipTouchdownState
{
    Idle = 0,
    Descending = 1,
    GearContact = 2,
    Landed = 3,
    TakingOff = 4,
    Failed = 5
}

public partial class ArcadeShipController
{
    [Export]
    public NodePath LandingGearVisualPath { get; set; } =
        new("Visuals/LandingGear");

    [Export]
    public NodePath LandingGearProbesPath { get; set; } =
        new("LandingGearProbes");

    [Export(PropertyHint.Range, "0.5,5.0,0.05")]
    public float TouchdownGearHeight { get; set; } = 1.55f;

    [Export(PropertyHint.Range, "0.5,10.0,0.1")]
    public float TouchdownMaximumDescentSpeed { get; set; } = 2.8f;

    [Export(PropertyHint.Range, "1.0,40.0,0.5")]
    public float TouchdownDescentAcceleration { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public float TouchdownPositionTolerance { get; set; } = 0.28f;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    public float TouchdownAngularToleranceDegrees { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.05,1.0,0.05")]
    public float TouchdownStableContactSeconds { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0.2,3.0,0.05")]
    public float TouchdownProbeContactDistance { get; set; } = 1.45f;

    [Export(PropertyHint.Range, "0.5,20.0,0.5")]
    public float LandingGearDeploymentSpeed { get; set; } = 3.5f;

    [Export(PropertyHint.Range, "4.0,40.0,0.5")]
    public float TakeoffClearanceHeight { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1.0,20.0,0.5")]
    public float TakeoffMaximumSpeed { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "1.0,40.0,0.5")]
    public float TakeoffAcceleration { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.0,10.0,0.25")]
    public float TakeoffDepartureSpeed { get; set; } = 3.0f;

    private readonly List<RayCast3D> _landingGearProbes = new();
    private Node3D? _landingGearVisual;
    private Transform3D _touchdownTargetTransform;
    private Transform3D _landedTransform;
    private Transform3D _takeoffTargetTransform;
    private float _touchdownStableElapsed;
    private float _gearDeployment;
    private float _gearDeploymentTarget;
    private string _touchdownFailureReason = string.Empty;

    public ShipTouchdownState TouchdownState { get; private set; } =
        ShipTouchdownState.Idle;
    public bool TouchdownSequenceActive =>
        TouchdownState is ShipTouchdownState.Descending or
            ShipTouchdownState.GearContact or
            ShipTouchdownState.Landed or
            ShipTouchdownState.TakingOff;
    public bool PhysicsLockedOnGear { get; private set; }
    public float LandingGearDeployment => _gearDeployment;
    public int LandingGearContactCount { get; private set; }
    public int LandingGearProbeCount => _landingGearProbes.Count;
    public float TouchdownClearance { get; private set; } = float.PositiveInfinity;
    public float TouchdownPositionError { get; private set; } =
        float.PositiveInfinity;
    public float TouchdownAngularErrorDegrees { get; private set; } =
        float.PositiveInfinity;
    public float TouchdownSpeed { get; private set; }
    public float MaximumRecordedTouchdownSpeed { get; private set; }
    public int TouchdownAttempts { get; private set; }
    public int TouchdownCompletions { get; private set; }
    public int LandedLockCompletions { get; private set; }
    public int TakeoffAttempts { get; private set; }
    public int TakeoffCompletions { get; private set; }
    public int TouchdownRecoveries { get; private set; }
    public float LastTakeoffClearance { get; private set; }
    public string TouchdownFailureReason => _touchdownFailureReason;

    public bool RequestTouchdown()
    {
        if (LandingState != ShipLandingAssistState.Aligned ||
            !HasLandingReservation ||
            TouchdownState != ShipTouchdownState.Idle)
        {
            return false;
        }

        Vector3 normal = LandingReservedNormal.Normalized();
        _touchdownTargetTransform = new Transform3D(
            _landingTargetTransform.Basis.Orthonormalized(),
            LandingReservedPoint + (normal * TouchdownGearHeight));
        _touchdownStableElapsed = 0.0f;
        _touchdownFailureReason = string.Empty;
        PhysicsLockedOnGear = false;
        LandingGearContactCount = 0;
        TouchdownSpeed = 0.0f;
        TouchdownPositionError = GlobalPosition.DistanceTo(
            _touchdownTargetTransform.Origin);
        TouchdownAngularErrorDegrees = CalculateAngularError(
            _touchdownTargetTransform.Basis);
        _gearDeploymentTarget = 1.0f;
        TouchdownState = ShipTouchdownState.Descending;
        TouchdownAttempts++;
        SetManualControlEnabled(false);
        ClearExternalCommand();
        ClearRadialGuidance();

        GD.Print(
            "Ship touchdown started: " +
            $"height={LandingAlignmentHeight:F1}->{TouchdownGearHeight:F2} m; " +
            $"maxDescent={TouchdownMaximumDescentSpeed:F1} m/s");
        return true;
    }

    public bool PrepareTouchdownSoakCycle(float startClearance)
    {
        if (!HasLandingReservation)
        {
            return false;
        }

        CancelTouchdownSequence(false);
        LandingState = ShipLandingAssistState.Aligned;
        Vector3 normal = LandingReservedNormal.Normalized();
        float safeStartClearance = Math.Max(
            TouchdownGearHeight + 0.75f,
            startClearance);
        GlobalTransform = new Transform3D(
            _landingTargetTransform.Basis.Orthonormalized(),
            LandingReservedPoint + (normal * safeStartClearance));
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        _gearDeployment = 1.0f;
        _gearDeploymentTarget = 1.0f;
        ApplyLandingGearVisual();
        return RequestTouchdown();
    }

    public bool RequestTakeoff()
    {
        if (TouchdownState != ShipTouchdownState.Landed ||
            !HasLandingReservation)
        {
            return false;
        }

        Vector3 normal = LandingReservedNormal.Normalized();
        _takeoffTargetTransform = new Transform3D(
            _landedTransform.Basis.Orthonormalized(),
            LandingReservedPoint + (normal * TakeoffClearanceHeight));
        PhysicsLockedOnGear = false;
        TouchdownState = ShipTouchdownState.TakingOff;
        TakeoffAttempts++;
        SetManualControlEnabled(false);
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        GD.Print(
            "Ship takeoff started: " +
            $"clearance={TakeoffClearanceHeight:F1} m");
        return true;
    }

    public void CancelTouchdownSequence(bool restoreManualControl = true)
    {
        TouchdownState = ShipTouchdownState.Idle;
        PhysicsLockedOnGear = false;
        _touchdownStableElapsed = 0.0f;
        _gearDeploymentTarget = 0.0f;
        LandingGearContactCount = 0;
        TouchdownClearance = float.PositiveInfinity;
        TouchdownPositionError = float.PositiveInfinity;
        TouchdownAngularErrorDegrees = float.PositiveInfinity;
        TouchdownSpeed = 0.0f;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        if (restoreManualControl)
        {
            SetManualControlEnabled(true);
        }
    }

    private void InitializeTouchdownSystem()
    {
        _landingGearVisual = GetNodeOrNull<Node3D>(LandingGearVisualPath);
        Node3D? probesRoot = GetNodeOrNull<Node3D>(LandingGearProbesPath);
        if (_landingGearVisual is null || probesRoot is null)
        {
            throw new InvalidOperationException(
                "ArcadeShip requires LandingGear visual and LandingGearProbes.");
        }

        _landingGearProbes.Clear();
        foreach (Node child in probesRoot.GetChildren())
        {
            if (child is RayCast3D rayCast)
            {
                _landingGearProbes.Add(rayCast);
            }
        }

        if (_landingGearProbes.Count < 3)
        {
            throw new InvalidOperationException(
                "ArcadeShip requires at least three landing gear probes.");
        }

        _gearDeployment = 0.0f;
        _gearDeploymentTarget = 0.0f;
        ApplyLandingGearVisual();
        TouchdownState = ShipTouchdownState.Idle;
        GD.Print(
            "Ship touchdown system ready: " +
            $"gearProbes={_landingGearProbes.Count}; " +
            $"gearHeight={TouchdownGearHeight:F2} m");
    }

    private bool ProcessTouchdownPhysics(float deltaSeconds)
    {
        UpdateLandingGearDeployment(deltaSeconds);

        switch (TouchdownState)
        {
            case ShipTouchdownState.Descending:
            case ShipTouchdownState.GearContact:
                ApplyTouchdownDescent(deltaSeconds);
                MoveAndSlide();
                UpdateTouchdownMetrics();
                EvaluateTouchdownContact(deltaSeconds);
                return true;

            case ShipTouchdownState.Landed:
                GlobalTransform = _landedTransform;
                Velocity = Vector3.Zero;
                AngularVelocityLocal = Vector3.Zero;
                PhysicsLockedOnGear = true;
                UpdateTouchdownMetrics();
                return true;

            case ShipTouchdownState.TakingOff:
                ApplyTakeoff(deltaSeconds);
                MoveAndSlide();
                UpdateTouchdownMetrics();
                EvaluateTakeoffCompletion();
                return true;

            case ShipTouchdownState.Failed:
                Velocity = Vector3.Zero;
                AngularVelocityLocal = Vector3.Zero;
                return true;

            default:
                return false;
        }
    }

    private void ApplyTouchdownDescent(float deltaSeconds)
    {
        Vector3 positionError =
            _touchdownTargetTransform.Origin - GlobalPosition;
        Vector3 desiredVelocity = positionError * 1.4f;
        float desiredSpeed = desiredVelocity.Length();
        if (desiredSpeed > TouchdownMaximumDescentSpeed)
        {
            desiredVelocity = desiredVelocity.Normalized() *
                TouchdownMaximumDescentSpeed;
        }

        Velocity = Velocity.MoveToward(
            desiredVelocity,
            TouchdownDescentAcceleration * deltaSeconds);
        AlignToTouchdownBasis(
            _touchdownTargetTransform.Basis,
            deltaSeconds);
    }

    private void EvaluateTouchdownContact(float deltaSeconds)
    {
        bool contactReady =
            LandingGearContactCount == LandingGearProbeCount &&
            TouchdownPositionError <= TouchdownPositionTolerance &&
            TouchdownAngularErrorDegrees <= TouchdownAngularToleranceDegrees &&
            TouchdownSpeed <= TouchdownMaximumDescentSpeed + 0.25f;

        if (!contactReady)
        {
            _touchdownStableElapsed = 0.0f;
            TouchdownState = ShipTouchdownState.Descending;
            return;
        }

        TouchdownState = ShipTouchdownState.GearContact;
        _touchdownStableElapsed += deltaSeconds;
        if (_touchdownStableElapsed < TouchdownStableContactSeconds)
        {
            return;
        }

        float confirmedTouchdownSpeed = TouchdownSpeed;
        _landedTransform = _touchdownTargetTransform;
        GlobalTransform = _landedTransform;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        PhysicsLockedOnGear = true;
        TouchdownCompletions++;
        LandedLockCompletions++;
        TouchdownState = ShipTouchdownState.Landed;
        UpdateTouchdownMetrics();
        GD.Print(
            "Ship touchdown completed: " +
            $"contacts={LandingGearContactCount}/{LandingGearProbeCount}; " +
            $"speed={confirmedTouchdownSpeed:F2} m/s; " +
            $"positionError={TouchdownPositionError:F3} m; " +
            $"angularError={TouchdownAngularErrorDegrees:F3}°");
    }

    private void ApplyTakeoff(float deltaSeconds)
    {
        Vector3 normal = LandingReservedNormal.Normalized();
        Vector3 positionError =
            _takeoffTargetTransform.Origin - GlobalPosition;
        Vector3 desiredVelocity = positionError * 1.25f;
        float desiredSpeed = desiredVelocity.Length();
        if (desiredSpeed > TakeoffMaximumSpeed)
        {
            desiredVelocity = desiredVelocity.Normalized() * TakeoffMaximumSpeed;
        }

        Velocity = Velocity.MoveToward(
            desiredVelocity,
            TakeoffAcceleration * deltaSeconds);
        AlignToTouchdownBasis(_takeoffTargetTransform.Basis, deltaSeconds);

        float clearance = (GlobalPosition - LandingReservedPoint).Dot(normal);
        _gearDeploymentTarget = clearance >= 3.0f ? 0.0f : 1.0f;
    }

    private void EvaluateTakeoffCompletion()
    {
        if (GlobalPosition.DistanceTo(_takeoffTargetTransform.Origin) > 0.45f ||
            TouchdownAngularErrorDegrees > TouchdownAngularToleranceDegrees)
        {
            return;
        }

        Vector3 departureNormal = LandingReservedNormal.Normalized();
        Vector3 departurePoint = _takeoffTargetTransform.Origin;
        Basis departureBasis = _takeoffTargetTransform.Basis;
        LastTakeoffClearance =
            (departurePoint - LandingReservedPoint).Dot(departureNormal);
        TakeoffCompletions++;
        TouchdownState = ShipTouchdownState.Idle;
        PhysicsLockedOnGear = false;
        _gearDeploymentTarget = 0.0f;
        CancelLandingAssist(false);
        GlobalTransform = new Transform3D(departureBasis, departurePoint);
        Velocity = departureNormal * TakeoffDepartureSpeed;
        AngularVelocityLocal = Vector3.Zero;
        SetManualControlEnabled(true);
        GD.Print(
            "Ship takeoff completed: " +
            $"clearance={TakeoffClearanceHeight:F1} m; " +
            $"departureSpeed={TakeoffDepartureSpeed:F1} m/s");
    }

    private void UpdateTouchdownMetrics()
    {
        if (!HasLandingReservation)
        {
            LandingGearContactCount = 0;
            return;
        }

        Vector3 normal = LandingReservedNormal.Normalized();
        TouchdownClearance =
            (GlobalPosition - LandingReservedPoint).Dot(normal);
        TouchdownPositionError = GlobalPosition.DistanceTo(
            TouchdownState == ShipTouchdownState.TakingOff
                ? _takeoffTargetTransform.Origin
                : _touchdownTargetTransform.Origin);
        Basis targetBasis = TouchdownState == ShipTouchdownState.TakingOff
            ? _takeoffTargetTransform.Basis
            : _touchdownTargetTransform.Basis;
        TouchdownAngularErrorDegrees = CalculateAngularError(targetBasis);
        TouchdownSpeed = Math.Max(0.0f, -Velocity.Dot(normal));
        MaximumRecordedTouchdownSpeed = Math.Max(
            MaximumRecordedTouchdownSpeed,
            TouchdownSpeed);
        LandingGearContactCount = CountLandingGearContacts();
    }

    private int CountLandingGearContacts()
    {
        int contacts = 0;
        foreach (RayCast3D probe in _landingGearProbes)
        {
            probe.ForceRaycastUpdate();
            if (!probe.IsColliding())
            {
                continue;
            }

            float distance = probe.GlobalPosition.DistanceTo(
                probe.GetCollisionPoint());
            Vector3 normal = probe.GetCollisionNormal().Normalized();
            if (distance <= TouchdownProbeContactDistance &&
                normal.Dot(LandingReservedNormal.Normalized()) >= 0.75f)
            {
                contacts++;
            }
        }

        return contacts;
    }

    private void AlignToTouchdownBasis(Basis targetBasis, float deltaSeconds)
    {
        Quaternion currentRotation =
            GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion targetRotation =
            targetBasis.Orthonormalized().GetRotationQuaternion();
        float interpolation = 1.0f -
            Mathf.Exp(-LandingOrientationSharpness * deltaSeconds);
        Basis alignedBasis = new Basis(
            currentRotation.Slerp(targetRotation, interpolation))
            .Orthonormalized();
        GlobalTransform = new Transform3D(alignedBasis, GlobalPosition);
        AngularVelocityLocal = Vector3.Zero;
    }

    private float CalculateAngularError(Basis targetBasis)
    {
        Quaternion current =
            GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion target =
            targetBasis.Orthonormalized().GetRotationQuaternion();
        return Mathf.RadToDeg(current.AngleTo(target));
    }

    private void UpdateLandingGearDeployment(float deltaSeconds)
    {
        _gearDeployment = Mathf.MoveToward(
            _gearDeployment,
            _gearDeploymentTarget,
            LandingGearDeploymentSpeed * deltaSeconds);
        ApplyLandingGearVisual();
    }

    private void ApplyLandingGearVisual()
    {
        if (_landingGearVisual is null)
        {
            return;
        }

        _landingGearVisual.Visible = _gearDeployment > 0.01f;
        float verticalScale = Mathf.Lerp(0.12f, 1.0f, _gearDeployment);
        _landingGearVisual.Scale = new Vector3(1.0f, verticalScale, 1.0f);
    }
}
