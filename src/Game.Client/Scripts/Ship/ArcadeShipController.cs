using System;
using Godot;

public enum ShipCameraMode
{
    Chase = 0,
    Cockpit = 1
}

public readonly record struct ShipControlCommand(
    float Forward,
    float Strafe,
    float Lift,
    float Pitch,
    float Yaw,
    float Roll,
    bool Boost,
    bool Brake)
{
    public static ShipControlCommand Neutral => new(
        0.0f,
        0.0f,
        0.0f,
        0.0f,
        0.0f,
        0.0f,
        false,
        false);
}

public readonly record struct ShipCollisionImpact(
    Vector3 Normal,
    Vector3 Position,
    float NormalClosingSpeed,
    float TotalSpeed,
    string ColliderName);

public readonly record struct ArcadeShipRuntimeState(
    Transform3D GlobalTransform,
    Vector3 Velocity,
    Vector3 AngularVelocityLocal,
    ShipCameraMode CameraMode,
    bool AutoStabilizationEnabled);

public partial class ArcadeShipController : CharacterBody3D
{
    public const bool FullAttitudeRotationEnabled = true;
    public const bool MouseTranslationCouplingEnabled = false;
    public const bool StatefulVirtualFlightStickEnabled = true;
    public const float DefaultFlightCameraNearMeters = 0.25f;
    public const float DefaultFlightCameraFarMeters = 900000.0f;

    [Export]
    public bool StartPilotEnabled { get; set; } = true;

    [Export]
    public bool AllowRuntimeReset { get; set; } = true;

    [Export(PropertyHint.Range, "1.0,200.0,0.5")]
    public float ForwardAcceleration { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "1.0,200.0,0.5")]
    public float ReverseAcceleration { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float LateralAcceleration { get; set; } = 11.0f;

    [Export(PropertyHint.Range, "1.0,100.0,0.5")]
    public float VerticalAcceleration { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1.0,250.0,0.5")]
    public float MaxSpeed { get; set; } = 42.0f;

    [Export(PropertyHint.Range, "1.0,400.0,0.5")]
    public float BoostMaxSpeed { get; set; } = 72.0f;

    [Export(PropertyHint.Range, "1.0,5.0,0.05")]
    public float BoostAccelerationMultiplier { get; set; } = 1.85f;

    [Export(PropertyHint.Range, "0.0,20.0,0.05")]
    public float PassiveLinearDamping { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.1,12.0,0.1")]
    public float VelocityAlignmentRate { get; set; } =
        ArcadeFlightAssistRuntime.DefaultVelocityAlignmentRate;

    [Export(PropertyHint.Range, "1.0,200.0,0.5")]
    public float BrakeDeceleration { get; set; } = 38.0f;

    [Export(PropertyHint.Range, "10.0,180.0,1.0")]
    public float MaxPitchRateDegrees { get; set; } = 80.0f;

    [Export(PropertyHint.Range, "10.0,180.0,1.0")]
    public float MaxYawRateDegrees { get; set; } = 85.0f;

    [Export(PropertyHint.Range, "10.0,240.0,1.0")]
    public float MaxRollRateDegrees { get; set; } = 110.0f;

    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float AngularAcceleration { get; set; } = 5.5f;

    [Export(PropertyHint.Range, "0.1,30.0,0.1")]
    public float StabilizationAcceleration { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0035f;

    [Export]
    public bool InvertPitchLook { get; set; }

    [Export]
    public bool InvertYawLook { get; set; }

    [Export(PropertyHint.Range, "0.5,20.0,0.1")]
    public float MouseInputDecay { get; set; } = 7.5f; // legacy impulse controller only

    [Export(PropertyHint.Range, "0.5,4.0,0.05")]
    public float MouseFlightGain { get; set; } = 2.25f;

    [Export(PropertyHint.Range, "0.0,1.0,0.05")]
    public float MouseBankFactor { get; set; } = 0.62f;

    [Export(PropertyHint.Range, "1.0,6.0,0.1")]
    public float MouseAngularResponseMultiplier { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.0,0.25,0.005")]
    public float MouseVirtualStickDeadZone { get; set; } =
        ArcadeFlightAssistRuntime.DefaultVirtualStickDeadZone;

    [Export(PropertyHint.Range, "1.0,3.0,0.05")]
    public float MouseVirtualStickResponseExponent { get; set; } =
        ArcadeFlightAssistRuntime.DefaultVirtualStickResponseExponent;

    [Export(PropertyHint.Range, "0.0,0.45,0.01")]
    public float MouseCoordinatedYawFactor { get; set; } =
        ArcadeFlightAssistRuntime.DefaultCoordinatedYawFactor;

    [Export(PropertyHint.Range, "0.05,2.0,0.05")]
    public float FlightCameraNearMeters { get; set; } = DefaultFlightCameraNearMeters;

    [Export(PropertyHint.Range, "100000.0,1000000.0,1000.0")]
    public float FlightCameraFarMeters { get; set; } = DefaultFlightCameraFarMeters;

    private Camera3D? _chaseCamera;
    private Camera3D? _cockpitCamera;
    private Transform3D _spawnTransform;
    private Vector2 _mouseVirtualStick;
    private ShipControlCommand _externalCommand = ShipControlCommand.Neutral;
    private float _externalMaxSpeedOverride;
    private bool _externalControlActive;
    private bool _manualControlEnabled = true;
    private bool _pilotEnabled = true;
    private bool _parkedControlLocked;
    private uint _defaultCollisionLayer;
    private uint _defaultCollisionMask;
    private int _runtimeErrorCount;
    private int _collisionEvents;
    private int _mouseSteeringSamples;
    private float _lastMouseSteeringMagnitude;
    private ShipCollisionImpact? _pendingCollisionImpact;

    public Vector3 AngularVelocityLocal { get; private set; } = Vector3.Zero;
    public Vector3 LocalVelocity { get; private set; } = Vector3.Zero;
    public float Speed { get; private set; }
    public float AngularSpeedDegrees { get; private set; }
    public float FlightAssistHeadingErrorDegrees { get; private set; }
    public bool BoostActive { get; private set; }
    public bool BrakeActive { get; private set; }
    public bool AutoStabilizationEnabled { get; private set; } = true;
    public bool ExternalControlActive => _externalControlActive;
    public float ExternalMaxSpeedOverride => _externalMaxSpeedOverride;
    public bool ManualControlEnabled => _manualControlEnabled;
    public bool PilotEnabled => _pilotEnabled;
    public bool ParkedControlLocked => _parkedControlLocked;
    public bool ManualInputOwnershipActive =>
        _pilotEnabled &&
        !_parkedControlLocked &&
        _manualControlEnabled &&
        !_externalControlActive;
    public ShipCameraMode CameraMode { get; private set; } = ShipCameraMode.Chase;
    public int CameraSwitchCount { get; private set; }
    public int RuntimeErrorCount => _runtimeErrorCount;
    public int CollisionEvents => _collisionEvents;
    public int MouseSteeringSampleCount => _mouseSteeringSamples;
    public float LastMouseSteeringMagnitude => _lastMouseSteeringMagnitude;
    public Vector2 MouseVirtualStick => _mouseVirtualStick;

    public bool TryConsumeCollisionImpact(out ShipCollisionImpact impact)
    {
        if (_pendingCollisionImpact is ShipCollisionImpact pending)
        {
            impact = pending;
            _pendingCollisionImpact = null;
            return true;
        }

        impact = default;
        return false;
    }

    public override void _Ready()
    {
        _chaseCamera = GetNodeOrNull<Camera3D>(
            "ChaseCameraRig/SpringArm3D/Camera3D");
        _cockpitCamera = GetNodeOrNull<Camera3D>("CockpitCamera3D");

        if (_chaseCamera is null || _cockpitCamera is null)
        {
            throw new InvalidOperationException(
                "ArcadeShip scene requires chase and cockpit cameras.");
        }

        _defaultCollisionLayer = CollisionLayer;
        _defaultCollisionMask = CollisionMask;
        MotionMode = CharacterBody3D.MotionModeEnum.Floating;
        MaxSlides = 8;
        SafeMargin = 0.02f;
        _spawnTransform = GlobalTransform;
        InitializeAtmosphere();
        InitializeLandingSystem();
        InitializeTouchdownSystem();
        GameUserSettingsService.ApplyToShip(this);
        ApplyStableCameraClipEnvelope();
        SetCameraMode(ShipCameraMode.Chase, false);
        SetPilotEnabled(StartPilotEnabled);
        UpdateDiagnostics();

        GD.Print(
            "Arcade ship initialized: " +
            $"maxSpeed={MaxSpeed:F1}; boost={BoostMaxSpeed:F1}; " +
            $"camera={CameraMode}; stabilization={AutoStabilizationEnabled}");
    }

    public override void _Input(InputEvent inputEvent)
    {
        // TASK-178.6: flight mouse motion must be sampled before Controls/HUD can
        // consume the event. The old _UnhandledInput path was vulnerable to UI
        // interception, and its fixed per-tick decay erased normal 5-20 px mouse
        // deltas before angular acceleration could produce a visible manoeuvre.
        if (!ManualInputOwnershipActive)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouseVirtualStick = ArcadeFlightAssistRuntime.AccumulateVirtualFlightStick(
                _mouseVirtualStick,
                mouseMotion.Relative,
                MouseSensitivity,
                MouseFlightGain,
                InvertPitchLook,
                InvertYawLook);
            _mouseSteeringSamples++;
            _lastMouseSteeringMagnitude = _mouseVirtualStick.Length();
            if (_mouseSteeringSamples == 1)
            {
                GD.Print(
                    "TASK-180.3 ship virtual flight stick INPUT PASS: " +
                    $"relative={mouseMotion.Relative}; stick={_mouseVirtualStick}; " +
                    $"sensitivity={MouseSensitivity:0.0000}; gain={MouseFlightGain:0.00}; " +
                    "stateful=1; horizontal=roll-dominant; vertical=pitch; path=_Input.");
            }
            // While the pilot owns the ship, mouse motion is a flight-control
            // event, not UI hover input. Marking it handled prevents a full-
            // screen HUD Control from stealing the same movement downstream.
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseButton recenterButton &&
            recenterButton.Pressed &&
            recenterButton.ButtonIndex == MouseButton.Middle &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouseVirtualStick = Vector2.Zero;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            _mouseVirtualStick = Vector2.Zero;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!_pilotEnabled)
        {
            return;
        }

        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo)
        {
            Key physical = keyEvent.PhysicalKeycode;
            Key logical = keyEvent.Keycode;

            if ((physical == Key.R || logical == Key.R) &&
                AllowRuntimeReset &&
                !_externalControlActive)
            {
                ResetToSpawn();
                GetViewport().SetInputAsHandled();
                return;
            }

        }

        if (inputEvent.IsActionPressed("ship_camera"))
        {
            ToggleCamera();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inputEvent.IsActionPressed("ship_stabilize") && !_externalControlActive)
        {
            AutoStabilizationEnabled = !AutoStabilizationEnabled;
            GD.Print("Ship auto stabilization: " +
                (AutoStabilizationEnabled ? "ON" : "OFF"));
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_manualControlEnabled || _externalControlActive)
        {
            return;
        }

        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            _mouseVirtualStick = Vector2.Zero;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

    }

    public override void _PhysicsProcess(double delta)
    {
        float deltaSeconds = (float)delta;
        ShipControlCommand command = _externalControlActive
            ? _externalCommand
            : ReadManualCommand();

        UpdateAtmosphereContext();
        if (ProcessTouchdownPhysics(deltaSeconds))
        {
            UpdateAtmosphereContext();
            UpdateDiagnostics();
            return;
        }

        if (ProcessLandingPhysics(deltaSeconds))
        {
            if (LandingState != ShipLandingAssistState.Aligned)
            {
                int landingSlideCount = GetSlideCollisionCount();
                if (landingSlideCount > 0)
                {
                    _collisionEvents += landingSlideCount;
                }
            }

            UpdateAtmosphereContext();
            UpdateDiagnostics();
            return;
        }

        Vector3 velocityBeforeBrakeForces = Velocity;
        ApplyLinearFlight(command, deltaSeconds);
        ApplyAtmosphericFlight(command, deltaSeconds);
        ApplyAtmosphericRadialGuidance(deltaSeconds);
        if (command.Brake)
        {
            // TASK-178.7: environmental/gravity forces are evaluated first,
            // then the brake envelope clamps the final result. A held brake
            // therefore cannot cross zero and start accelerating backwards,
            // even at an atmosphere/space boundary.
            Velocity = ArcadeShipBrakeRuntime.ApplyMonotonicBrakeEnvelope(
                velocityBeforeBrakeForces,
                Velocity,
                BrakeDeceleration,
                deltaSeconds);
        }
        ApplyAngularFlight(command, deltaSeconds);
        ApplyArcadeFlightAssist(command, deltaSeconds);
        Vector3 velocityBeforeMove = Velocity;
        MoveAndSlide();

        int slideCount = GetSlideCollisionCount();
        if (slideCount > 0)
        {
            _collisionEvents += slideCount;
            CaptureStrongestCollisionImpact(velocityBeforeMove, slideCount);
        }

        ApplyAtmosphericSurfaceCorrection();

        UpdateDiagnostics();
    }

    public void SetExternalCommand(ShipControlCommand command)
    {
        _externalControlActive = true;
        _externalCommand = SanitizeCommand(command);
    }

    public void SetExternalSpeedLimit(float speedMetersPerSecond)
    {
        _externalMaxSpeedOverride = float.IsFinite(speedMetersPerSecond) &&
            speedMetersPerSecond > 0.0f
            ? speedMetersPerSecond
            : 0.0f;
    }

    public void ClearExternalCommand()
    {
        _externalCommand = ShipControlCommand.Neutral;
        _externalMaxSpeedOverride = 0.0f;
        _externalControlActive = false;
    }

    public void SetManualControlEnabled(bool enabled)
    {
        _manualControlEnabled = enabled;
        if (!enabled)
        {
            _mouseVirtualStick = Vector2.Zero;
        }
    }

    public void SetParkedControlLock(bool locked)
    {
        _parkedControlLocked = locked;
        ClearExternalCommand();
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        _mouseVirtualStick = Vector2.Zero;

        if (locked)
        {
            SetManualControlEnabled(false);
            SetPhysicsProcess(false);
            return;
        }

        if (_pilotEnabled)
        {
            SetManualControlEnabled(true);
            SetPhysicsProcess(true);
            UpdateAtmosphereContext();
            UpdateDiagnostics();
        }
    }

    public void SetPilotEnabled(bool enabled)
    {
        _pilotEnabled = enabled;
        Visible = enabled;
        CollisionLayer = enabled ? _defaultCollisionLayer : 0u;
        CollisionMask = enabled ? _defaultCollisionMask : 0u;
        SetPhysicsProcess(enabled);
        SetProcessInput(enabled);
        SetProcessUnhandledInput(enabled);

        if (!enabled)
        {
            _parkedControlLocked = false;
            ClearExternalCommand();
            SetManualControlEnabled(false);
            Velocity = Vector3.Zero;
            AngularVelocityLocal = Vector3.Zero;
            _mouseVirtualStick = Vector2.Zero;
            if (_chaseCamera is not null)
            {
                _chaseCamera.Current = false;
            }

            if (_cockpitCamera is not null)
            {
                _cockpitCamera.Current = false;
            }

            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }

            return;
        }

        _parkedControlLocked = false;
        SetManualControlEnabled(true);
        SetCameraMode(CameraMode, false);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        UpdateAtmosphereContext();
        UpdateDiagnostics();
    }

    public void SetAutoStabilization(bool enabled)
    {
        AutoStabilizationEnabled = enabled;
    }

    public void ToggleCamera()
    {
        SetCameraMode(
            CameraMode == ShipCameraMode.Chase
                ? ShipCameraMode.Cockpit
                : ShipCameraMode.Chase,
            true);
    }

    public void SetCameraMode(ShipCameraMode mode, bool countSwitch = true)
    {
        if (_chaseCamera is null || _cockpitCamera is null)
        {
            return;
        }

        if (CameraMode != mode && countSwitch)
        {
            CameraSwitchCount++;
        }

        CameraMode = mode;
        _chaseCamera.Current = mode == ShipCameraMode.Chase;
        _cockpitCamera.Current = mode == ShipCameraMode.Cockpit;
    }

    public void ResetToSpawn()
    {
        CancelTouchdownSequence(false);
        CancelLandingAssist(false);
        ClearExternalCommand();
        ClearRadialGuidance();
        GlobalTransform = _spawnTransform;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        _mouseVirtualStick = Vector2.Zero;
        _pendingCollisionImpact = null;
        _collisionEvents = 0;
        SetManualControlEnabled(true);
        UpdateAtmosphereContext();
        UpdateDiagnostics();
        GD.Print("Arcade ship reset to spawn.");
    }

    public ArcadeShipRuntimeState CaptureRuntimeState()
    {
        return new ArcadeShipRuntimeState(
            GlobalTransform,
            Velocity,
            AngularVelocityLocal,
            CameraMode,
            AutoStabilizationEnabled);
    }

    public void SetFieldOfView(float degrees)
    {
        float clamped = Mathf.Clamp(degrees, 60.0f, 110.0f);
        if (_chaseCamera is not null)
        {
            _chaseCamera.Fov = clamped;
        }
        if (_cockpitCamera is not null)
        {
            _cockpitCamera.Fov = clamped;
        }
    }

    public void RestoreRuntimeState(ArcadeShipRuntimeState state)
    {
        CancelTouchdownSequence(false);
        CancelLandingAssist(false);
        ClearExternalCommand();
        ClearRadialGuidance();
        GlobalTransform = state.GlobalTransform;
        Velocity = state.Velocity;
        AngularVelocityLocal = state.AngularVelocityLocal;
        AutoStabilizationEnabled = state.AutoStabilizationEnabled;
        SetCameraMode(state.CameraMode, false);
        _mouseVirtualStick = Vector2.Zero;
        _pendingCollisionImpact = null;
        UpdateAtmosphereContext();
        UpdateDiagnostics();
    }

    private void ApplyStableCameraClipEnvelope()
    {
        float nearPlane = Mathf.Clamp(FlightCameraNearMeters, 0.20f, 2.0f);
        float farPlane = Mathf.Clamp(FlightCameraFarMeters, 100000.0f, 900000.0f);
        if (farPlane <= nearPlane * 1000.0f)
        {
            farPlane = Math.Max(100000.0f, nearPlane * 1000.0f);
        }

        if (_chaseCamera is not null)
        {
            _chaseCamera.Near = nearPlane;
            _chaseCamera.Far = farPlane;
        }
        if (_cockpitCamera is not null)
        {
            _cockpitCamera.Near = nearPlane;
            _cockpitCamera.Far = farPlane;
        }
    }

    private ShipControlCommand ReadManualCommand()
    {
        // TASK-178.7: in the arcade control set S is a brake, not an
        // unlatched reverse-thrust command. Holding a brake must be monotonic:
        // speed can approach zero but may never cross zero and accelerate the
        // ship backwards. External/autopilot commands can still request signed
        // Forward values explicitly when a manoeuvre actually needs reverse
        // thrust.
        float forward = Input.GetActionStrength("ship_forward");
        bool brake = Input.IsActionPressed("ship_brake") ||
            Input.IsActionPressed("ship_reverse");
        float strafe = Input.GetAxis("ship_strafe_left", "ship_strafe_right");
        float lift = Input.GetAxis("ship_lift_down", "ship_lift_up");
        float rollKeyboard = Input.GetAxis("ship_roll_left", "ship_roll_right");
        float pitchKeyboard = Input.GetAxis("ship_pitch_down", "ship_pitch_up");
        float yawKeyboard = Input.GetAxis("ship_yaw_right", "ship_yaw_left");
        Vector3 mouseAttitude = ArcadeFlightAssistRuntime.BuildVirtualStickAttitudeCommand(
            _mouseVirtualStick,
            MouseVirtualStickDeadZone,
            MouseVirtualStickResponseExponent,
            MouseCoordinatedYawFactor);

        return new ShipControlCommand(
            forward,
            strafe,
            lift,
            Mathf.Clamp(mouseAttitude.X + pitchKeyboard, -1.0f, 1.0f),
            Mathf.Clamp(mouseAttitude.Y + yawKeyboard, -1.0f, 1.0f),
            Mathf.Clamp(mouseAttitude.Z + rollKeyboard, -1.0f, 1.0f),
            Input.IsActionPressed("ship_boost") && !brake,
            brake);
    }

    private void ApplyLinearFlight(
        ShipControlCommand command,
        float deltaSeconds)
    {
        if (command.Brake)
        {
            // Braking is exclusive with pilot thrust. The actual monotonic
            // deceleration is applied after atmosphere/environment forces so
            // those forces cannot push the velocity through zero in the same
            // physics tick.
            BoostActive = false;
            BrakeActive = true;
            return;
        }

        float forwardAcceleration = command.Forward >= 0.0f
            ? ForwardAcceleration
            : ReverseAcceleration;
        float boostMultiplier = command.Boost && command.Forward > 0.0f
            ? BoostAccelerationMultiplier
            : 1.0f;

        Vector3 localAcceleration = new(
            command.Strafe * LateralAcceleration,
            command.Lift * VerticalAcceleration,
            -command.Forward * forwardAcceleration * boostMultiplier);
        Vector3 worldAcceleration =
            GlobalTransform.Basis.Orthonormalized() * localAcceleration;
        Velocity += worldAcceleration * deltaSeconds;

        if (PassiveLinearDamping > 0.0f)
        {
            Velocity = Velocity.MoveToward(
                Vector3.Zero,
                PassiveLinearDamping * deltaSeconds);
        }

        float speedLimit = _externalControlActive &&
            _externalMaxSpeedOverride > 0.0f
            ? _externalMaxSpeedOverride
            : command.Boost
                ? BoostMaxSpeed
                : MaxSpeed;
        if (Velocity.LengthSquared() > speedLimit * speedLimit)
        {
            Velocity = Velocity.Normalized() * speedLimit;
        }

        BoostActive = command.Boost;
        BrakeActive = command.Brake;
    }

    private void ApplyArcadeFlightAssist(
        ShipControlCommand command,
        float deltaSeconds)
    {
        // Directional alignment preserves speed by design. While braking that
        // is the wrong contract: only speed reduction is allowed.
        if (command.Brake)
        {
            return;
        }

        Velocity = ArcadeFlightAssistRuntime.AlignVelocityToShipAxes(
            Velocity,
            GlobalTransform.Basis,
            command,
            AutoStabilizationEnabled,
            deltaSeconds,
            VelocityAlignmentRate);
    }

    private void ApplyAngularFlight(
        ShipControlCommand command,
        float deltaSeconds)
    {
        Vector3 desiredAngularVelocity = new(
            command.Pitch * Mathf.DegToRad(MaxPitchRateDegrees),
            command.Yaw * Mathf.DegToRad(MaxYawRateDegrees),
            command.Roll * Mathf.DegToRad(MaxRollRateDegrees));

        bool hasRotationInput =
            Math.Abs(command.Pitch) > 0.001f ||
            Math.Abs(command.Yaw) > 0.001f ||
            Math.Abs(command.Roll) > 0.001f;

        if (hasRotationInput)
        {
            bool mouseAttitudeActive =
                _mouseVirtualStick.Length() > MouseVirtualStickDeadZone &&
                ManualInputOwnershipActive;
            float angularResponse = AngularAcceleration *
                (mouseAttitudeActive ? MouseAngularResponseMultiplier : 1.0f);
            AngularVelocityLocal = AngularVelocityLocal.MoveToward(
                desiredAngularVelocity,
                angularResponse * deltaSeconds);
        }
        else if (AutoStabilizationEnabled || command.Brake)
        {
            AngularVelocityLocal = AngularVelocityLocal.MoveToward(
                Vector3.Zero,
                StabilizationAcceleration * deltaSeconds);
        }

        RotateObjectLocal(
            Vector3.Right,
            AngularVelocityLocal.X * deltaSeconds);
        RotateObjectLocal(
            Vector3.Up,
            AngularVelocityLocal.Y * deltaSeconds);
        RotateObjectLocal(
            Vector3.Forward,
            AngularVelocityLocal.Z * deltaSeconds);

        GlobalTransform = new Transform3D(
            GlobalTransform.Basis.Orthonormalized(),
            GlobalPosition);
    }

    private void CaptureStrongestCollisionImpact(
        Vector3 velocityBeforeMove,
        int slideCount)
    {
        if (!velocityBeforeMove.IsFinite() || slideCount <= 0)
        {
            return;
        }

        ShipCollisionImpact? strongest = null;
        for (int index = 0; index < slideCount; index++)
        {
            KinematicCollision3D collision = GetSlideCollision(index);
            Vector3 normal = collision.GetNormal();
            if (!normal.IsFinite() || normal.LengthSquared() <= 0.000001f)
            {
                continue;
            }

            normal = normal.Normalized();
            float closing = Math.Max(0.0f, -velocityBeforeMove.Dot(normal));
            if (strongest is ShipCollisionImpact current &&
                closing <= current.NormalClosingSpeed)
            {
                continue;
            }

            GodotObject? collider = collision.GetCollider();
            string colliderName = collider is Node node
                ? node.Name.ToString()
                : collider?.ToString() ?? string.Empty;
            strongest = new ShipCollisionImpact(
                normal,
                collision.GetPosition(),
                closing,
                velocityBeforeMove.Length(),
                colliderName);
        }

        if (strongest is ShipCollisionImpact resolved)
        {
            _pendingCollisionImpact = resolved;
        }
    }

    private void UpdateDiagnostics()
    {
        Basis basis = GlobalTransform.Basis.Orthonormalized();
        LocalVelocity = basis.Inverse() * Velocity;
        Speed = Velocity.Length();
        AngularSpeedDegrees = Mathf.RadToDeg(AngularVelocityLocal.Length());
        FlightAssistHeadingErrorDegrees =
            ArcadeFlightAssistRuntime.HeadingErrorDegrees(
                Velocity,
                GlobalTransform.Basis);

        if (!Velocity.IsFinite() ||
            !AngularVelocityLocal.IsFinite() ||
            !GlobalPosition.IsFinite())
        {
            _runtimeErrorCount++;
            Velocity = Vector3.Zero;
            AngularVelocityLocal = Vector3.Zero;
            GD.PushError("Arcade ship detected non-finite runtime state.");
        }
    }

    private static ShipControlCommand SanitizeCommand(
        ShipControlCommand command)
    {
        return new ShipControlCommand(
            Mathf.Clamp(command.Forward, -1.0f, 1.0f),
            Mathf.Clamp(command.Strafe, -1.0f, 1.0f),
            Mathf.Clamp(command.Lift, -1.0f, 1.0f),
            Mathf.Clamp(command.Pitch, -1.0f, 1.0f),
            Mathf.Clamp(command.Yaw, -1.0f, 1.0f),
            Mathf.Clamp(command.Roll, -1.0f, 1.0f),
            command.Boost,
            command.Brake);
    }
}
