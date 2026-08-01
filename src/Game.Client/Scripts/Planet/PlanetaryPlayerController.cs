using System;
using System.Collections.Generic;
using Godot;

public enum PlanetarySeamTestState
{
    Idle = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Cancelled = 4
}

public partial class PlanetaryPlayerController : CharacterBody3D
{
    private enum SeamTestPhase
    {
        None = 0,
        Settling = 1,
        Traversing = 2
    }

    [Export]
    public NodePath PlanetCenterPath { get; set; } = new("../Planet");

    [Export]
    public NodePath GroundProbesPath { get; set; } = new("GroundProbes");

    [Export(PropertyHint.Range, "0.0,100.0,0.1")]
    public float GravityAcceleration { get; set; } = 24.0f;

    [Export(PropertyHint.Range, "0.0,50.0,0.1")]
    public float MoveSpeed { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.0,100.0,0.1")]
    public float GroundAcceleration { get; set; } = 36.0f;

    [Export(PropertyHint.Range, "0.0,100.0,0.1")]
    public float AirAcceleration { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0.0,50.0,0.1")]
    public float JumpVelocity { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.1,40.0,0.1")]
    public float OrientationSharpness { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export(PropertyHint.Range, "0.1,3.0,0.05")]
    public float SurfaceSnapLength { get; set; } = 1.25f;

    [Export(PropertyHint.Range, "0.0,10.0,0.05")]
    public float SurfaceAdhesionSpeed { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float GroundGraceSeconds { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float GroundProbeContactDistance { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.001,0.1,0.001")]
    public float CollisionSafeMargin { get; set; } = 0.03f;

    [Export(PropertyHint.Range, "1.0,30.0,0.5")]
    public float SeamTestMoveSpeed { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "2,20,1")]
    public int SeamTestTargetCrossings { get; set; } = 4;

    [Export(PropertyHint.Range, "10.0,180.0,1.0")]
    public float SeamTestTimeoutSeconds { get; set; } = 65.0f;

    [Export(PropertyHint.Range, "0.0,0.5,0.01")]
    public float SeamTestAllowedUnsupportedSeconds { get; set; } = 0.12f;

    private readonly List<RayCast3D> _groundProbes = new();
    private Node3D? _planetCenter;
    private Node3D? _cameraPitch;
    private SpringArm3D? _springArm;
    private Camera3D? _camera;
    private Transform3D _spawnTransform;
    private Vector3 _spawnCameraPitchRotation;
    private bool _controlEnabled = true;
    private bool _externalMovementLocked;
    private bool _probeGrounded;
    private Vector3 _probeGroundNormal = Vector3.Up;
    private float _nearestProbeDistance = float.PositiveInfinity;
    private float _groundGraceRemaining;
    private float _jumpDetachRemaining;
    private CubeSphereFaceId _currentFace = CubeSphereFaceId.PositiveX;
    private float _faceSwitchCooldown;
    private int _lifetimeSeamCrossings;

    private PlanetarySeamTestState _seamTestState =
        PlanetarySeamTestState.Idle;
    private SeamTestPhase _seamTestPhase = SeamTestPhase.None;
    private Transform3D _seamTestStartTransform;
    private Vector3 _seamTestStartVelocity;
    private Vector3 _seamTestStartCameraPitch;
    private Vector3 _seamTestAxis = Vector3.Up;
    private float _seamTestElapsed;
    private float _seamTestSettledSeconds;
    private float _seamTestUnsupportedCurrent;
    private float _seamTestMaxUnsupported;
    private float _seamTestMaximumUpError;
    private int _seamTestCrossings;
    private string _seamTestResult = "не запускался";

    public Vector3 RadialUp { get; private set; } = Vector3.Up;
    public Vector3 GravityDirection { get; private set; } = Vector3.Down;
    public float RadialDistance { get; private set; }
    public float UpAlignmentErrorDegrees { get; private set; }
    public float TangentialSpeed { get; private set; }
    public bool IsGrounded => IsOnFloor() || _probeGrounded ||
        _groundGraceRemaining > 0.0f;
    public bool HasPhysicalGroundContact => IsOnFloor() || _probeGrounded;
    public bool ProbeGrounded => _probeGrounded;
    public float NearestProbeDistance => _nearestProbeDistance;
    public bool ControlEnabled => _controlEnabled;
    public bool ExternalMovementLocked => _externalMovementLocked;
    public Camera3D? PlayerCamera => _camera;
    public CubeSphereFaceId CurrentFace => _currentFace;
    public string CurrentFaceName => GetFaceDisplayName(_currentFace);
    public int LifetimeSeamCrossings => _lifetimeSeamCrossings;
    public PlanetarySeamTestState SeamTestState => _seamTestState;
    public bool SeamTestRunning =>
        _seamTestState == PlanetarySeamTestState.Running;
    public int SeamTestCrossings => _seamTestCrossings;
    public float SeamTestMaxUnsupportedSeconds => _seamTestMaxUnsupported;
    public float SeamTestMaximumUpErrorDegrees => _seamTestMaximumUpError;

    public string SeamTestStatusText
    {
        get
        {
            return _seamTestState switch
            {
                PlanetarySeamTestState.Running =>
                    $"TASK-030 seam (T): RUNNING " +
                    $"{_seamTestCrossings}/{SeamTestTargetCrossings}, " +
                    $"t={_seamTestElapsed:F0} с, " +
                    $"gap={_seamTestMaxUnsupported:F2} с",
                PlanetarySeamTestState.Passed =>
                    $"TASK-030 seam (T): PASS " +
                    $"crossings={_seamTestCrossings}, " +
                    $"gap={_seamTestMaxUnsupported:F2} с, " +
                    $"Δup={_seamTestMaximumUpError:F2}°",
                PlanetarySeamTestState.Failed =>
                    $"TASK-030 seam (T): FAIL — {_seamTestResult}",
                PlanetarySeamTestState.Cancelled =>
                    "TASK-030 seam (T): остановлен пользователем",
                _ => "TASK-030 seam (T): READY"
            };
        }
    }

    public override void _Ready()
    {
        _planetCenter = GetNodeOrNull<Node3D>(PlanetCenterPath);
        _cameraPitch = GetNodeOrNull<Node3D>("CameraPitch");
        _springArm = GetNodeOrNull<SpringArm3D>("CameraPitch/SpringArm3D");
        _camera = GetNodeOrNull<Camera3D>("CameraPitch/SpringArm3D/Camera3D");
        Node3D? groundProbesRoot = GetNodeOrNull<Node3D>(GroundProbesPath);

        if (_planetCenter is null || _cameraPitch is null ||
            _springArm is null || _camera is null || groundProbesRoot is null)
        {
            throw new InvalidOperationException(
                "PlanetaryPlayer scene is missing PlanetCenter, CameraPitch, " +
                "SpringArm3D, Camera3D or GroundProbes.");
        }

        foreach (Node child in groundProbesRoot.GetChildren())
        {
            if (child is RayCast3D rayCast)
            {
                _groundProbes.Add(rayCast);
            }
        }

        if (_groundProbes.Count < 3)
        {
            throw new InvalidOperationException(
                "PlanetaryPlayer requires at least three GroundProbes RayCast3D nodes.");
        }

        UpDirection = CalculateRadialUp();
        FloorSnapLength = SurfaceSnapLength;
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        FloorMaxAngle = Mathf.DegToRad(60.0f);
        SafeMargin = CollisionSafeMargin;
        MaxSlides = 10;
        MotionMode = CharacterBody3D.MotionModeEnum.Grounded;

        SnapOrientationToRadialUp();
        _currentFace = GetDominantFace(RadialUp);
        _spawnTransform = GlobalTransform;
        _spawnCameraPitchRotation = _cameraPitch.Rotation;
        _springArm.AddExcludedObject(GetRid());
        UpdateGroundProbes();
        SetControlEnabled(true);

        GD.Print(
            "Planetary player initialized: " +
            $"position={GlobalPosition}; radialDistance={RadialDistance:F2}; " +
            $"gravity={GravityAcceleration:F1}; upError={UpAlignmentErrorDegrees:F3}°; " +
            $"groundProbes={_groundProbes.Count}; snap={FloorSnapLength:F2}");
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            (keyEvent.Keycode == Key.R ||
             keyEvent.PhysicalKeycode == Key.R) &&
            !_externalMovementLocked)
        {
            CancelSeamTraversalTest(false);
            ResetToSpawn();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_controlEnabled || _externalMovementLocked || SeamTestRunning)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateObjectLocal(
                Vector3.Up,
                -mouseMotion.Relative.X * MouseSensitivity);

            Vector3 pitchRotation = _cameraPitch!.Rotation;
            pitchRotation.X = Mathf.Clamp(
                pitchRotation.X -
                (mouseMotion.Relative.Y * MouseSensitivity),
                Mathf.DegToRad(-70.0f),
                Mathf.DegToRad(70.0f));
            _cameraPitch.Rotation = pitchRotation;
        }

        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_planetCenter is null)
        {
            return;
        }

        float deltaSeconds = (float)delta;
        RadialUp = CalculateRadialUp();
        GravityDirection = -RadialUp;
        UpDirection = RadialUp;
        FloorSnapLength = SurfaceSnapLength;
        SafeMargin = CollisionSafeMargin;

        UpdateGroundProbes();
        bool supportedBeforeMove = IsOnFloor() || _probeGrounded;
        UpdateGroundGrace(supportedBeforeMove, deltaSeconds);

        Vector3 seamTestDirection = Vector3.Zero;
        bool seamTraversalActive =
            SeamTestRunning && _seamTestPhase == SeamTestPhase.Traversing;
        if (seamTraversalActive)
        {
            seamTestDirection = _seamTestAxis.Cross(RadialUp);
            if (seamTestDirection.LengthSquared() > 0.000001f)
            {
                seamTestDirection = seamTestDirection.Normalized();
            }
        }

        AlignToRadialUp(
            deltaSeconds,
            seamTraversalActive ? seamTestDirection : null);

        Vector3 velocity = Velocity;
        float radialSpeed = velocity.Dot(RadialUp);
        Vector3 radialVelocity = RadialUp * radialSpeed;
        Vector3 tangentialVelocity = velocity - radialVelocity;

        bool jumpRequested =
            _controlEnabled &&
            !_externalMovementLocked &&
            !SeamTestRunning &&
            Input.IsActionJustPressed("jump") &&
            IsGrounded;

        if (jumpRequested)
        {
            _jumpDetachRemaining = 0.22f;
            _groundGraceRemaining = 0.0f;
            radialVelocity = RadialUp * JumpVelocity;
        }
        else
        {
            _jumpDetachRemaining = Math.Max(
                0.0f,
                _jumpDetachRemaining - deltaSeconds);

            bool shouldAdhere =
                _jumpDetachRemaining <= 0.0f &&
                (supportedBeforeMove || _groundGraceRemaining > 0.0f) &&
                radialSpeed <= 1.0f;

            radialVelocity = shouldAdhere
                ? GravityDirection * SurfaceAdhesionSpeed
                : radialVelocity +
                    (GravityDirection * GravityAcceleration * deltaSeconds);
        }

        Vector3 desiredDirection;
        float desiredSpeed;
        if (seamTraversalActive)
        {
            desiredDirection = seamTestDirection;
            desiredSpeed = SeamTestMoveSpeed;
        }
        else if (SeamTestRunning)
        {
            desiredDirection = Vector3.Zero;
            desiredSpeed = 0.0f;
        }
        else
        {
            Vector2 movementInput = _controlEnabled && !_externalMovementLocked
                ? Input.GetVector(
                    "move_left",
                    "move_right",
                    "move_forward",
                    "move_backward")
                : Vector2.Zero;

            desiredDirection =
                (GlobalTransform.Basis.X * movementInput.X) +
                (GlobalTransform.Basis.Z * movementInput.Y);
            desiredDirection = desiredDirection.Slide(RadialUp);
            if (desiredDirection.LengthSquared() > 0.000001f)
            {
                desiredDirection = desiredDirection.Normalized();
            }

            desiredSpeed = MoveSpeed;
        }

        float acceleration = IsGrounded
            ? GroundAcceleration
            : AirAcceleration;
        tangentialVelocity = tangentialVelocity.MoveToward(
            desiredDirection * desiredSpeed,
            acceleration * deltaSeconds);

        Velocity = tangentialVelocity + radialVelocity;
        MoveAndSlide();

        if (!jumpRequested &&
            _jumpDetachRemaining <= 0.0f &&
            !IsOnFloor() &&
            (supportedBeforeMove || _groundGraceRemaining > 0.0f))
        {
            ApplyFloorSnap();
        }

        UpdateGroundProbes();
        if (IsOnFloor() || _probeGrounded)
        {
            _groundGraceRemaining = GroundGraceSeconds;
        }

        UpdateDiagnostics();
        UpdateCurrentFace(deltaSeconds);
        UpdateSeamTest(deltaSeconds);
    }

    public void SetControlEnabled(bool enabled)
    {
        if (!enabled && SeamTestRunning)
        {
            CancelSeamTraversalTest(true);
        }

        _controlEnabled = enabled;
        if (_camera is not null)
        {
            _camera.Current = enabled;
        }

        Input.MouseMode = enabled
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }


    public void SetExternalMovementLocked(bool locked)
    {
        if (locked && SeamTestRunning)
        {
            CancelSeamTraversalTest(true);
        }

        _externalMovementLocked = locked;
        if (locked)
        {
            Velocity = Vector3.Zero;
        }
    }

    public void NotifyWorldTranslated(Vector3 translation)
    {
        _spawnTransform = TranslateStoredTransform(
            _spawnTransform,
            translation);

        if (SeamTestRunning)
        {
            _seamTestStartTransform = TranslateStoredTransform(
                _seamTestStartTransform,
                translation);
        }
    }

    public void ResetToSpawn()
    {
        GlobalTransform = _spawnTransform;
        Velocity = Vector3.Zero;
        _groundGraceRemaining = 0.0f;
        _jumpDetachRemaining = 0.0f;
        if (_cameraPitch is not null)
        {
            _cameraPitch.Rotation = _spawnCameraPitchRotation;
        }

        SnapOrientationToRadialUp();
        _currentFace = GetDominantFace(RadialUp);
        UpdateGroundProbes();
        GD.Print("Planetary player reset to radial spawn point.");
    }

    public bool BeginSeamTraversalTest()
    {
        if (SeamTestRunning || !_controlEnabled || _externalMovementLocked ||
            _cameraPitch is null)
        {
            return false;
        }

        _seamTestStartTransform = GlobalTransform;
        _seamTestStartVelocity = Velocity;
        _seamTestStartCameraPitch = _cameraPitch.Rotation;
        _seamTestElapsed = 0.0f;
        _seamTestSettledSeconds = 0.0f;
        _seamTestUnsupportedCurrent = 0.0f;
        _seamTestMaxUnsupported = 0.0f;
        _seamTestMaximumUpError = 0.0f;
        _seamTestCrossings = 0;
        _seamTestResult = "выполняется";
        _seamTestPhase = SeamTestPhase.Settling;
        _seamTestState = PlanetarySeamTestState.Running;
        _currentFace = GetDominantFace(RadialUp);
        _faceSwitchCooldown = 0.0f;

        Vector3 reference = new Vector3(0.37f, 0.61f, 0.70f).Normalized();
        Vector3 initialTangent = reference.Slide(RadialUp);
        if (initialTangent.LengthSquared() <= 0.000001f)
        {
            initialTangent = Vector3.Forward.Slide(RadialUp);
        }

        initialTangent = initialTangent.Normalized();
        _seamTestAxis = RadialUp.Cross(initialTangent).Normalized();

        GD.Print(
            "TASK-030 seam traversal started: " +
            $"target={SeamTestTargetCrossings}; speed={SeamTestMoveSpeed:F1}; " +
            $"face={CurrentFaceName}; probes={_groundProbes.Count}");
        return true;
    }

    public void CancelSeamTraversalTest(bool restoreStart)
    {
        if (!SeamTestRunning)
        {
            return;
        }

        FinishSeamTraversalTest(
            PlanetarySeamTestState.Cancelled,
            "остановлен пользователем",
            restoreStart);
    }

    private void UpdateSeamTest(float deltaSeconds)
    {
        if (!SeamTestRunning)
        {
            return;
        }

        _seamTestElapsed += deltaSeconds;
        _seamTestMaximumUpError = Math.Max(
            _seamTestMaximumUpError,
            UpAlignmentErrorDegrees);

        if (HasPhysicalGroundContact)
        {
            _seamTestUnsupportedCurrent = 0.0f;
        }
        else
        {
            _seamTestUnsupportedCurrent += deltaSeconds;
            _seamTestMaxUnsupported = Math.Max(
                _seamTestMaxUnsupported,
                _seamTestUnsupportedCurrent);
        }

        if (_seamTestPhase == SeamTestPhase.Settling)
        {
            if (HasPhysicalGroundContact && TangentialSpeed <= 0.5f)
            {
                _seamTestSettledSeconds += deltaSeconds;
            }
            else
            {
                _seamTestSettledSeconds = 0.0f;
            }

            if (_seamTestSettledSeconds >= 0.5f)
            {
                _seamTestPhase = SeamTestPhase.Traversing;
                _seamTestUnsupportedCurrent = 0.0f;
                _seamTestMaxUnsupported = 0.0f;
                _seamTestMaximumUpError = UpAlignmentErrorDegrees;
                _seamTestElapsed = 0.0f;
                _currentFace = GetDominantFace(RadialUp);
                GD.Print(
                    "TASK-030 seam traversal settled; movement phase started.");
            }
            else if (_seamTestElapsed >= 8.0f)
            {
                FinishSeamTraversalTest(
                    PlanetarySeamTestState.Failed,
                    "не удалось установить устойчивый контакт за 8 с",
                    true);
            }

            return;
        }

        if (_seamTestMaxUnsupported > SeamTestAllowedUnsupportedSeconds)
        {
            FinishSeamTraversalTest(
                PlanetarySeamTestState.Failed,
                $"потеря контакта {_seamTestMaxUnsupported:F2} с",
                true);
            return;
        }

        if (_seamTestMaximumUpError > 3.0f)
        {
            FinishSeamTraversalTest(
                PlanetarySeamTestState.Failed,
                $"рывок ориентации Δup={_seamTestMaximumUpError:F2}°",
                true);
            return;
        }

        if (_seamTestCrossings >= SeamTestTargetCrossings &&
            HasPhysicalGroundContact)
        {
            FinishSeamTraversalTest(
                PlanetarySeamTestState.Passed,
                "межгранные переходы устойчивы",
                true);
            return;
        }

        if (_seamTestElapsed >= SeamTestTimeoutSeconds)
        {
            FinishSeamTraversalTest(
                PlanetarySeamTestState.Failed,
                $"timeout: {_seamTestCrossings}/{SeamTestTargetCrossings} швов",
                true);
        }
    }

    private void FinishSeamTraversalTest(
        PlanetarySeamTestState finalState,
        string result,
        bool restoreStart)
    {
        _seamTestState = finalState;
        _seamTestPhase = SeamTestPhase.None;
        _seamTestResult = result;

        if (restoreStart && _cameraPitch is not null)
        {
            GlobalTransform = _seamTestStartTransform;
            Velocity = _seamTestStartVelocity;
            _cameraPitch.Rotation = _seamTestStartCameraPitch;
            SnapOrientationToRadialUp();
            _currentFace = GetDominantFace(RadialUp);
            UpdateGroundProbes();
        }
        else
        {
            Velocity = Vector3.Zero;
        }

        string status = finalState switch
        {
            PlanetarySeamTestState.Passed => "PASS",
            PlanetarySeamTestState.Failed => "FAIL",
            PlanetarySeamTestState.Cancelled => "CANCELLED",
            _ => finalState.ToString().ToUpperInvariant()
        };

        GD.Print(
            $"TASK-030 seam traversal {status}: " +
            $"crossings={_seamTestCrossings}; " +
            $"maxUnsupported={_seamTestMaxUnsupported:F3}s; " +
            $"maxUpError={_seamTestMaximumUpError:F3}°; " +
            $"result={result}");
    }

    private void UpdateGroundGrace(bool physicalContact, float deltaSeconds)
    {
        if (physicalContact)
        {
            _groundGraceRemaining = GroundGraceSeconds;
        }
        else
        {
            _groundGraceRemaining = Math.Max(
                0.0f,
                _groundGraceRemaining - deltaSeconds);
        }
    }

    private void UpdateGroundProbes()
    {
        _probeGrounded = false;
        _probeGroundNormal = RadialUp;
        _nearestProbeDistance = float.PositiveInfinity;
        Vector3 normalSum = Vector3.Zero;
        int validHits = 0;

        foreach (RayCast3D probe in _groundProbes)
        {
            probe.ForceRaycastUpdate();
            if (!probe.IsColliding())
            {
                continue;
            }

            Vector3 collisionNormal = probe.GetCollisionNormal();
            if (collisionNormal.LengthSquared() <= 0.000001f ||
                collisionNormal.Normalized().Dot(RadialUp) < 0.35f)
            {
                continue;
            }

            float distance = probe.GlobalPosition.DistanceTo(
                probe.GetCollisionPoint());
            _nearestProbeDistance = Math.Min(
                _nearestProbeDistance,
                distance);

            if (distance <= GroundProbeContactDistance)
            {
                _probeGrounded = true;
                normalSum += collisionNormal.Normalized();
                validHits++;
            }
        }

        if (validHits > 0 && normalSum.LengthSquared() > 0.000001f)
        {
            _probeGroundNormal = normalSum.Normalized();
        }
    }

    private Vector3 CalculateRadialUp()
    {
        if (_planetCenter is null)
        {
            return Vector3.Up;
        }

        Vector3 offset = GlobalPosition - _planetCenter.GlobalPosition;
        RadialDistance = offset.Length();
        return RadialDistance <= 0.0001f
            ? Vector3.Up
            : offset / RadialDistance;
    }

    private void SnapOrientationToRadialUp()
    {
        RadialUp = CalculateRadialUp();
        Basis targetBasis = CreateRadialBasis(RadialUp, null);
        GlobalTransform = new Transform3D(
            targetBasis,
            GlobalPosition);
        UpdateDiagnostics();
    }

    private void AlignToRadialUp(
        float deltaSeconds,
        Vector3? preferredForward)
    {
        Basis targetBasis = CreateRadialBasis(
            RadialUp,
            preferredForward);
        Quaternion currentRotation =
            GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        Quaternion targetRotation =
            targetBasis.GetRotationQuaternion();
        float interpolation = 1.0f -
            Mathf.Exp(-OrientationSharpness * deltaSeconds);
        Basis alignedBasis = new Basis(
            currentRotation.Slerp(targetRotation, interpolation))
            .Orthonormalized();

        GlobalTransform = new Transform3D(
            alignedBasis,
            GlobalPosition);
    }

    private Basis CreateRadialBasis(
        Vector3 radialUp,
        Vector3? preferredForward)
    {
        Vector3 forward = preferredForward ?? -GlobalTransform.Basis.Z;
        forward = forward.Slide(radialUp);
        if (forward.LengthSquared() <= 0.000001f)
        {
            Vector3 reference = Math.Abs(
                radialUp.Dot(Vector3.Forward)) > 0.95f
                ? Vector3.Right
                : Vector3.Forward;
            forward = reference.Slide(radialUp);
        }

        forward = forward.Normalized();
        Vector3 right = forward.Cross(radialUp).Normalized();
        Vector3 back = right.Cross(radialUp).Normalized();
        return new Basis(right, radialUp, back).Orthonormalized();
    }

    private void UpdateCurrentFace(float deltaSeconds)
    {
        _faceSwitchCooldown = Math.Max(
            0.0f,
            _faceSwitchCooldown - deltaSeconds);
        CubeSphereFaceId candidate = GetDominantFace(RadialUp);
        if (candidate == _currentFace)
        {
            return;
        }

        float currentScore = GetFaceScore(_currentFace, RadialUp);
        float candidateScore = GetFaceScore(candidate, RadialUp);
        if (_faceSwitchCooldown > 0.0f ||
            candidateScore < currentScore + 0.015f)
        {
            return;
        }

        CubeSphereFaceId previous = _currentFace;
        _currentFace = candidate;
        _faceSwitchCooldown = 0.25f;
        _lifetimeSeamCrossings++;

        if (SeamTestRunning &&
            _seamTestPhase == SeamTestPhase.Traversing)
        {
            _seamTestCrossings++;
        }

        GD.Print(
            "Planet face transition: " +
            $"{GetFaceDisplayName(previous)} -> {CurrentFaceName}; " +
            $"ground={HasPhysicalGroundContact}; " +
            $"probe={_probeGrounded}; r={RadialDistance:F2}");
    }

    private void UpdateDiagnostics()
    {
        RadialUp = CalculateRadialUp();
        GravityDirection = -RadialUp;
        float upDot = Mathf.Clamp(
            GlobalTransform.Basis.Y.Normalized().Dot(RadialUp),
            -1.0f,
            1.0f);
        UpAlignmentErrorDegrees = Mathf.RadToDeg(Mathf.Acos(upDot));
        TangentialSpeed = Velocity.Slide(RadialUp).Length();
    }


    private static Transform3D TranslateStoredTransform(
        Transform3D transform,
        Vector3 translation)
    {
        transform.Origin += translation;
        return transform;
    }

    private static CubeSphereFaceId GetDominantFace(Vector3 direction)
    {
        float absoluteX = Math.Abs(direction.X);
        float absoluteY = Math.Abs(direction.Y);
        float absoluteZ = Math.Abs(direction.Z);

        if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
        {
            return direction.X >= 0.0f
                ? CubeSphereFaceId.PositiveX
                : CubeSphereFaceId.NegativeX;
        }

        if (absoluteY >= absoluteZ)
        {
            return direction.Y >= 0.0f
                ? CubeSphereFaceId.PositiveY
                : CubeSphereFaceId.NegativeY;
        }

        return direction.Z >= 0.0f
            ? CubeSphereFaceId.PositiveZ
            : CubeSphereFaceId.NegativeZ;
    }

    private static float GetFaceScore(
        CubeSphereFaceId face,
        Vector3 direction)
    {
        return face switch
        {
            CubeSphereFaceId.PositiveX => direction.X,
            CubeSphereFaceId.NegativeX => -direction.X,
            CubeSphereFaceId.PositiveY => direction.Y,
            CubeSphereFaceId.NegativeY => -direction.Y,
            CubeSphereFaceId.PositiveZ => direction.Z,
            CubeSphereFaceId.NegativeZ => -direction.Z,
            _ => 0.0f
        };
    }

    private static string GetFaceDisplayName(CubeSphereFaceId face)
    {
        return face switch
        {
            CubeSphereFaceId.PositiveX => "+X",
            CubeSphereFaceId.NegativeX => "-X",
            CubeSphereFaceId.PositiveY => "+Y",
            CubeSphereFaceId.NegativeY => "-Y",
            CubeSphereFaceId.PositiveZ => "+Z",
            CubeSphereFaceId.NegativeZ => "-Z",
            _ => "?"
        };
    }
}
