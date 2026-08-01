using System;
using System.Collections.Generic;
using Godot;

public enum ShipLandingAssistState
{
    Idle = 0,
    Searching = 1,
    Reserved = 2,
    Aligning = 3,
    Aligned = 4,
    Failed = 5
}

public readonly record struct ShipLandingReservation(
    Vector3 SurfacePoint,
    Vector3 SurfaceNormal,
    float SlopeDegrees,
    float ObstacleClearance,
    int CandidateIndex);

public partial class ArcadeShipController
{
    [Export]
    public NodePath LandingTestSitePath { get; set; } =
        new("../AtmospherePlanet/LandingTestSite");

    [Export]
    public uint LandingSurfaceMask { get; set; } = 1;

    [Export(PropertyHint.Range, "10.0,300.0,1.0")]
    public float LandingProbeHeight { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "5.0,100.0,1.0")]
    public float LandingProbeDepth { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "1.0,45.0,0.5")]
    public float LandingMaximumSlopeDegrees { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1.0,20.0,0.25")]
    public float LandingObstacleClearance { get; set; } = 5.5f;

    [Export(PropertyHint.Range, "2.0,40.0,0.5")]
    public float LandingAlignmentHeight { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1.0,40.0,0.5")]
    public float LandingMaximumApproachSpeed { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float LandingApproachAcceleration { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "0.1,30.0,0.1")]
    public float LandingOrientationSharpness { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0.05,3.0,0.05")]
    public float LandingPositionTolerance { get; set; } = 0.65f;

    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float LandingAngularToleranceDegrees { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float LandingStableHoldSeconds { get; set; } = 0.6f;

    private ShipLandingTestSite? _landingTestSite;
    private ShipLandingReservation _landingReservation;
    private Transform3D _landingTargetTransform;
    private bool _landingSearchRequested;
    private bool _landingAutoAlignRequested;
    private float _landingStableElapsed;
    private string _landingFailureReason = string.Empty;

    public ShipLandingAssistState LandingState { get; private set; } =
        ShipLandingAssistState.Idle;
    public bool LandingAssistActive =>
        LandingState is ShipLandingAssistState.Searching or
            ShipLandingAssistState.Reserved or
            ShipLandingAssistState.Aligning or
            ShipLandingAssistState.Aligned;
    public bool HasLandingReservation =>
        LandingState is ShipLandingAssistState.Reserved or
            ShipLandingAssistState.Aligning or
            ShipLandingAssistState.Aligned;
    public Vector3 LandingReservedPoint => _landingReservation.SurfacePoint;
    public Vector3 LandingReservedNormal => _landingReservation.SurfaceNormal;
    public float LandingReservedSlopeDegrees => _landingReservation.SlopeDegrees;
    public float LandingReservedClearance => _landingReservation.ObstacleClearance;
    public int LandingCandidateChecks { get; private set; }
    public int LandingSurfaceHits { get; private set; }
    public int LandingSlopeRejections { get; private set; }
    public int LandingObstacleRejections { get; private set; }
    public int LandingReservations { get; private set; }
    public int LandingAlignmentCompletions { get; private set; }
    public float LandingPositionError { get; private set; } = float.PositiveInfinity;
    public float LandingAngularErrorDegrees { get; private set; } = float.PositiveInfinity;
    public string LandingFailureReason => _landingFailureReason;
    public ShipLandingTestSite? LandingTestSite => _landingTestSite;

    public void RequestLandingAssist(bool useAutomaticAlignment = true)
    {
        if (_atmosphereBody is null || LandingAssistActive)
        {
            return;
        }

        _landingSearchRequested = true;
        _landingAutoAlignRequested = useAutomaticAlignment;
        _landingStableElapsed = 0.0f;
        _landingFailureReason = string.Empty;
        LandingState = ShipLandingAssistState.Searching;
        SetManualControlEnabled(false);
        ClearExternalCommand();
        ClearRadialGuidance();
        GD.Print("Ship landing assist: search requested.");
    }

    public void CancelLandingAssist(bool restoreManualControl = true)
    {
        _landingSearchRequested = false;
        _landingAutoAlignRequested = false;
        _landingStableElapsed = 0.0f;
        LandingPositionError = float.PositiveInfinity;
        LandingAngularErrorDegrees = float.PositiveInfinity;
        LandingState = ShipLandingAssistState.Idle;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;

        if (restoreManualControl)
        {
            SetManualControlEnabled(true);
        }
    }

    private void InitializeLandingSystem()
    {
        _landingTestSite = GetNodeOrNull<ShipLandingTestSite>(
            LandingTestSitePath);
        LandingState = ShipLandingAssistState.Idle;
    }

    private bool ProcessLandingPhysics(float deltaSeconds)
    {
        if (_landingSearchRequested)
        {
            _landingSearchRequested = false;
            if (!TryReserveLandingPoint())
            {
                LandingState = ShipLandingAssistState.Failed;
                SetManualControlEnabled(true);
                return false;
            }

            if (_landingAutoAlignRequested)
            {
                BeginLandingAlignment();
            }
        }

        if (LandingState == ShipLandingAssistState.Aligning)
        {
            ApplyLandingAlignment(deltaSeconds);
            MoveAndSlide();
            UpdateLandingErrors();

            if (LandingPositionError <= LandingPositionTolerance &&
                LandingAngularErrorDegrees <= LandingAngularToleranceDegrees &&
                Speed <= 0.8f)
            {
                _landingStableElapsed += deltaSeconds;
                if (_landingStableElapsed >= LandingStableHoldSeconds)
                {
                    CompleteLandingAlignment();
                }
            }
            else
            {
                _landingStableElapsed = 0.0f;
            }

            return true;
        }

        if (LandingState == ShipLandingAssistState.Aligned)
        {
            GlobalTransform = _landingTargetTransform;
            Velocity = Vector3.Zero;
            AngularVelocityLocal = Vector3.Zero;
            UpdateLandingErrors();
            return true;
        }

        return false;
    }

    private bool TryReserveLandingPoint()
    {
        LandingCandidateChecks = 0;
        LandingSurfaceHits = 0;
        LandingSlopeRejections = 0;
        LandingObstacleRejections = 0;
        LandingPositionError = float.PositiveInfinity;
        LandingAngularErrorDegrees = float.PositiveInfinity;

        IReadOnlyList<Vector3> candidates = _landingTestSite is not null
            ? _landingTestSite.GetCandidateDirections()
            : BuildFallbackLandingCandidates();

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        foreach (Vector3 candidateDirection in candidates)
        {
            LandingCandidateChecks++;
            Vector3 radialDirection = candidateDirection.Normalized();
            Vector3 rayFrom = AtmosphereCenter +
                (radialDirection *
                    (AtmosphereSurfaceRadius + LandingProbeHeight));
            Vector3 rayTo = AtmosphereCenter +
                (radialDirection *
                    (AtmosphereSurfaceRadius - LandingProbeDepth));
            PhysicsRayQueryParameters3D query =
                PhysicsRayQueryParameters3D.Create(rayFrom, rayTo);
            query.CollisionMask = LandingSurfaceMask;
            query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                continue;
            }

            LandingSurfaceHits++;
            Vector3 surfacePoint = hit["position"].AsVector3();
            Vector3 surfaceNormal = hit["normal"].AsVector3().Normalized();
            Vector3 radialUp = (surfacePoint - AtmosphereCenter).Normalized();
            float slopeDegrees = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
                surfaceNormal.Dot(radialUp),
                -1.0f,
                1.0f)));

            if (slopeDegrees > LandingMaximumSlopeDegrees)
            {
                LandingSlopeRejections++;
                continue;
            }

            float obstacleClearance = FindNearestLandingObstacleDistance(
                surfacePoint,
                surfaceNormal);
            if (obstacleClearance < LandingObstacleClearance)
            {
                LandingObstacleRejections++;
                continue;
            }

            _landingReservation = new ShipLandingReservation(
                surfacePoint,
                surfaceNormal,
                slopeDegrees,
                obstacleClearance,
                LandingCandidateChecks - 1);
            LandingReservations++;
            LandingState = ShipLandingAssistState.Reserved;
            GD.Print(
                "Ship landing point reserved: " +
                $"candidate={_landingReservation.CandidateIndex}; " +
                $"slope={slopeDegrees:F2}°; " +
                $"clearance={FormatClearance(obstacleClearance)}; " +
                $"checks={LandingCandidateChecks}; " +
                $"slopeReject={LandingSlopeRejections}; " +
                $"obstacleReject={LandingObstacleRejections}");
            return true;
        }

        _landingFailureReason =
            $"no valid surface: checks={LandingCandidateChecks}, " +
            $"hits={LandingSurfaceHits}, slopeReject={LandingSlopeRejections}, " +
            $"obstacleReject={LandingObstacleRejections}";
        GD.PushWarning("Ship landing search failed: " + _landingFailureReason);
        return false;
    }

    private void BeginLandingAlignment()
    {
        Vector3 targetUp = _landingReservation.SurfaceNormal.Normalized();
        Vector3 currentForward = -GlobalTransform.Basis.Z;
        Vector3 targetForward = currentForward.Slide(targetUp);
        if (targetForward.LengthSquared() <= 0.000001f)
        {
            targetForward = Vector3.Forward.Slide(targetUp);
        }

        targetForward = targetForward.Normalized();
        Vector3 targetRight = targetForward.Cross(targetUp).Normalized();
        Vector3 targetBack = targetRight.Cross(targetUp).Normalized();
        Basis targetBasis = new Basis(
            targetRight,
            targetUp,
            targetBack).Orthonormalized();
        Vector3 targetPosition = _landingReservation.SurfacePoint +
            (targetUp * LandingAlignmentHeight);
        _landingTargetTransform = new Transform3D(
            targetBasis,
            targetPosition);
        _landingStableElapsed = 0.0f;
        LandingState = ShipLandingAssistState.Aligning;
        GD.Print(
            "Ship landing alignment started: " +
            $"height={LandingAlignmentHeight:F1} m");
    }

    private void ApplyLandingAlignment(float deltaSeconds)
    {
        Vector3 positionError =
            _landingTargetTransform.Origin - GlobalPosition;
        Vector3 desiredVelocity = positionError * 1.6f;
        float desiredSpeed = desiredVelocity.Length();
        if (desiredSpeed > LandingMaximumApproachSpeed)
        {
            desiredVelocity = desiredVelocity.Normalized() *
                LandingMaximumApproachSpeed;
        }

        Velocity = Velocity.MoveToward(
            desiredVelocity,
            LandingApproachAcceleration * deltaSeconds);

        Quaternion currentRotation =
            GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion targetRotation =
            _landingTargetTransform.Basis.GetRotationQuaternion();
        float interpolation = 1.0f -
            Mathf.Exp(-LandingOrientationSharpness * deltaSeconds);
        Basis alignedBasis = new Basis(
            currentRotation.Slerp(targetRotation, interpolation))
            .Orthonormalized();
        GlobalTransform = new Transform3D(alignedBasis, GlobalPosition);
        AngularVelocityLocal = Vector3.Zero;
    }

    private void CompleteLandingAlignment()
    {
        GlobalTransform = _landingTargetTransform;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        LandingAlignmentCompletions++;
        LandingState = ShipLandingAssistState.Aligned;
        UpdateLandingErrors();
        GD.Print(
            "Ship landing alignment completed: " +
            $"positionError={LandingPositionError:F3} m; " +
            $"angularError={LandingAngularErrorDegrees:F3}°");
    }

    private void UpdateLandingErrors()
    {
        LandingPositionError = GlobalPosition.DistanceTo(
            _landingTargetTransform.Origin);
        Quaternion current =
            GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion target =
            _landingTargetTransform.Basis.GetRotationQuaternion();
        LandingAngularErrorDegrees = Mathf.RadToDeg(current.AngleTo(target));
    }

    private float FindNearestLandingObstacleDistance(
        Vector3 surfacePoint,
        Vector3 surfaceNormal)
    {
        float nearest = float.PositiveInfinity;
        Vector3 clearanceCenter = surfacePoint +
            (surfaceNormal * 1.5f);
        foreach (Node node in GetTree().GetNodesInGroup("landing_obstacle"))
        {
            if (node is not Node3D obstacle)
            {
                continue;
            }

            nearest = Math.Min(
                nearest,
                clearanceCenter.DistanceTo(obstacle.GlobalPosition));
        }

        return nearest;
    }

    private IReadOnlyList<Vector3> BuildFallbackLandingCandidates()
    {
        Vector3 centerDirection = AtmosphereRadialUp.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : AtmosphereRadialUp.Normalized();
        Vector3 tangent = centerDirection.Cross(Vector3.Forward);
        if (tangent.LengthSquared() <= 0.000001f)
        {
            tangent = centerDirection.Cross(Vector3.Right);
        }

        tangent = tangent.Normalized();
        Vector3 bitangent = centerDirection.Cross(tangent).Normalized();
        List<Vector3> candidates = new() { centerDirection };
        const float angularOffset = 0.10f;
        for (int index = 0; index < 8; index++)
        {
            float angle = (Mathf.Pi * 2.0f) * index / 8.0f;
            Vector3 offset =
                (tangent * Mathf.Cos(angle)) +
                (bitangent * Mathf.Sin(angle));
            candidates.Add(
                (centerDirection + (offset * angularOffset)).Normalized());
        }

        return candidates;
    }

    private static string FormatClearance(float clearance)
    {
        return float.IsPositiveInfinity(clearance)
            ? "∞"
            : $"{clearance:F2} m";
    }
}
