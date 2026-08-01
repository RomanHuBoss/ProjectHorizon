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

public readonly record struct ArcadeShipRuntimeState(
    Transform3D GlobalTransform,
    Vector3 Velocity,
    Vector3 AngularVelocityLocal,
    ShipCameraMode CameraMode,
    bool AutoStabilizationEnabled);

public partial class ArcadeShipController : CharacterBody3D
{
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

    [Export(PropertyHint.Range, "0.05,2.0,0.05")]
    public float MouseInputDecay { get; set; } = 0.32f;

    private Camera3D? _chaseCamera;
    private Camera3D? _cockpitCamera;
    private Transform3D _spawnTransform;
    private Vector2 _mouseLookInput;
    private ShipControlCommand _externalCommand = ShipControlCommand.Neutral;
    private bool _externalControlActive;
    private bool _manualControlEnabled = true;
    private int _runtimeErrorCount;
    private int _collisionEvents;

    public Vector3 AngularVelocityLocal { get; private set; } = Vector3.Zero;
    public Vector3 LocalVelocity { get; private set; } = Vector3.Zero;
    public float Speed { get; private set; }
    public float AngularSpeedDegrees { get; private set; }
    public bool BoostActive { get; private set; }
    public bool BrakeActive { get; private set; }
    public bool AutoStabilizationEnabled { get; private set; } = true;
    public bool ExternalControlActive => _externalControlActive;
    public bool ManualControlEnabled => _manualControlEnabled;
    public ShipCameraMode CameraMode { get; private set; } = ShipCameraMode.Chase;
    public int CameraSwitchCount { get; private set; }
    public int RuntimeErrorCount => _runtimeErrorCount;
    public int CollisionEvents => _collisionEvents;

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

        MotionMode = CharacterBody3D.MotionModeEnum.Floating;
        MaxSlides = 8;
        SafeMargin = 0.02f;
        _spawnTransform = GlobalTransform;
        InitializeAtmosphere();
        SetCameraMode(ShipCameraMode.Chase, false);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        UpdateDiagnostics();

        GD.Print(
            "Arcade ship initialized: " +
            $"maxSpeed={MaxSpeed:F1}; boost={BoostMaxSpeed:F1}; " +
            $"camera={CameraMode}; stabilization={AutoStabilizationEnabled}");
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo)
        {
            Key physical = keyEvent.PhysicalKeycode;
            Key logical = keyEvent.Keycode;

            if ((physical == Key.R || logical == Key.R) &&
                !_externalControlActive)
            {
                ResetToSpawn();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (physical == Key.F2 || logical == Key.F2)
            {
                ToggleCamera();
                GetViewport().SetInputAsHandled();
                return;
            }

            if ((physical == Key.G || logical == Key.G) &&
                !_externalControlActive)
            {
                AutoStabilizationEnabled = !AutoStabilizationEnabled;
                GD.Print(
                    "Ship auto stabilization: " +
                    (AutoStabilizationEnabled ? "ON" : "OFF"));
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (!_manualControlEnabled || _externalControlActive)
        {
            return;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouseLookInput += new Vector2(
                -mouseMotion.Relative.Y * MouseSensitivity,
                -mouseMotion.Relative.X * MouseSensitivity);
            _mouseLookInput = _mouseLookInput.Clamp(
                new Vector2(-1.0f, -1.0f),
                new Vector2(1.0f, 1.0f));
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
        float deltaSeconds = (float)delta;
        ShipControlCommand command = _externalControlActive
            ? _externalCommand
            : ReadManualCommand();

        UpdateAtmosphereContext();
        ApplyLinearFlight(command, deltaSeconds);
        ApplyAtmosphericFlight(command, deltaSeconds);
        ApplyAngularFlight(command, deltaSeconds);
        MoveAndSlide();
        ApplyAtmosphericSurfaceCorrection();

        int slideCount = GetSlideCollisionCount();
        if (slideCount > 0)
        {
            _collisionEvents += slideCount;
        }

        _mouseLookInput = _mouseLookInput.MoveToward(
            Vector2.Zero,
            MouseInputDecay);
        UpdateDiagnostics();
    }

    public void SetExternalCommand(ShipControlCommand command)
    {
        _externalControlActive = true;
        _externalCommand = SanitizeCommand(command);
    }

    public void ClearExternalCommand()
    {
        _externalCommand = ShipControlCommand.Neutral;
        _externalControlActive = false;
    }

    public void SetManualControlEnabled(bool enabled)
    {
        _manualControlEnabled = enabled;
        if (!enabled)
        {
            _mouseLookInput = Vector2.Zero;
        }
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
        ClearExternalCommand();
        GlobalTransform = _spawnTransform;
        Velocity = Vector3.Zero;
        AngularVelocityLocal = Vector3.Zero;
        _mouseLookInput = Vector2.Zero;
        _collisionEvents = 0;
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

    public void RestoreRuntimeState(ArcadeShipRuntimeState state)
    {
        ClearExternalCommand();
        GlobalTransform = state.GlobalTransform;
        Velocity = state.Velocity;
        AngularVelocityLocal = state.AngularVelocityLocal;
        AutoStabilizationEnabled = state.AutoStabilizationEnabled;
        SetCameraMode(state.CameraMode, false);
        _mouseLookInput = Vector2.Zero;
        UpdateAtmosphereContext();
        UpdateDiagnostics();
    }

    private ShipControlCommand ReadManualCommand()
    {
        float forward = Axis(Key.S, Key.W);
        float strafe = Axis(Key.A, Key.D);
        float lift = Axis(Key.C, Key.Space);
        float roll = Axis(Key.Q, Key.E);
        float pitchKeyboard = Axis(Key.Down, Key.Up);
        float yawKeyboard = Axis(Key.Right, Key.Left);

        return new ShipControlCommand(
            forward,
            strafe,
            lift,
            Mathf.Clamp(_mouseLookInput.X + pitchKeyboard, -1.0f, 1.0f),
            Mathf.Clamp(_mouseLookInput.Y + yawKeyboard, -1.0f, 1.0f),
            roll,
            Input.IsPhysicalKeyPressed(Key.B),
            Input.IsPhysicalKeyPressed(Key.X));
    }

    private static float Axis(Key negative, Key positive)
    {
        return (Input.IsPhysicalKeyPressed(positive) ? 1.0f : 0.0f) -
            (Input.IsPhysicalKeyPressed(negative) ? 1.0f : 0.0f);
    }

    private void ApplyLinearFlight(
        ShipControlCommand command,
        float deltaSeconds)
    {
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

        if (command.Brake)
        {
            Velocity = Velocity.MoveToward(
                Vector3.Zero,
                BrakeDeceleration * deltaSeconds);
        }
        else if (PassiveLinearDamping > 0.0f)
        {
            Velocity = Velocity.MoveToward(
                Vector3.Zero,
                PassiveLinearDamping * deltaSeconds);
        }

        float speedLimit = command.Boost
            ? BoostMaxSpeed
            : MaxSpeed;
        if (Velocity.LengthSquared() > speedLimit * speedLimit)
        {
            Velocity = Velocity.Normalized() * speedLimit;
        }

        BoostActive = command.Boost;
        BrakeActive = command.Brake;
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
            AngularVelocityLocal = AngularVelocityLocal.MoveToward(
                desiredAngularVelocity,
                AngularAcceleration * deltaSeconds);
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

    private void UpdateDiagnostics()
    {
        Basis basis = GlobalTransform.Basis.Orthonormalized();
        LocalVelocity = basis.Inverse() * Velocity;
        Speed = Velocity.Length();
        AngularSpeedDegrees = Mathf.RadToDeg(AngularVelocityLocal.Length());

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
