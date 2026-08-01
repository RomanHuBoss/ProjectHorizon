using System;
using Godot;

public partial class PlanetaryPlayerController : CharacterBody3D
{
    [Export]
    public NodePath PlanetCenterPath { get; set; } = new("../Planet");

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

    private Node3D? _planetCenter;
    private Node3D? _cameraPitch;
    private SpringArm3D? _springArm;
    private Camera3D? _camera;
    private Transform3D _spawnTransform;
    private Vector3 _spawnCameraPitchRotation;
    private bool _controlEnabled = true;

    public Vector3 RadialUp { get; private set; } = Vector3.Up;
    public Vector3 GravityDirection { get; private set; } = Vector3.Down;
    public float RadialDistance { get; private set; }
    public float UpAlignmentErrorDegrees { get; private set; }
    public float TangentialSpeed { get; private set; }
    public bool IsGrounded => IsOnFloor();
    public bool ControlEnabled => _controlEnabled;
    public Camera3D? PlayerCamera => _camera;

    public override void _Ready()
    {
        _planetCenter = GetNodeOrNull<Node3D>(PlanetCenterPath);
        _cameraPitch = GetNodeOrNull<Node3D>("CameraPitch");
        _springArm = GetNodeOrNull<SpringArm3D>("CameraPitch/SpringArm3D");
        _camera = GetNodeOrNull<Camera3D>("CameraPitch/SpringArm3D/Camera3D");

        if (_planetCenter is null || _cameraPitch is null ||
            _springArm is null || _camera is null)
        {
            throw new InvalidOperationException(
                "PlanetaryPlayer scene is missing PlanetCenter, CameraPitch, " +
                "SpringArm3D or Camera3D.");
        }

        UpDirection = CalculateRadialUp();
        FloorSnapLength = 0.6f;
        FloorStopOnSlope = true;
        FloorMaxAngle = Mathf.DegToRad(55.0f);
        MotionMode = CharacterBody3D.MotionModeEnum.Grounded;

        SnapOrientationToRadialUp();
        _spawnTransform = GlobalTransform;
        _spawnCameraPitchRotation = _cameraPitch.Rotation;
        _springArm.AddExcludedObject(GetRid());
        SetControlEnabled(true);

        GD.Print(
            "Planetary player initialized: " +
            $"position={GlobalPosition}; radialDistance={RadialDistance:F2}; " +
            $"gravity={GravityAcceleration:F1}; upError={UpAlignmentErrorDegrees:F3}°");
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            keyEvent.Keycode == Key.R)
        {
            ResetToSpawn();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_controlEnabled)
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

        AlignToRadialUp(deltaSeconds);

        Vector3 velocity = Velocity;
        float radialSpeed = velocity.Dot(RadialUp);
        Vector3 radialVelocity = RadialUp * radialSpeed;
        Vector3 tangentialVelocity = velocity - radialVelocity;

        if (IsOnFloor() && radialSpeed < 0.0f)
        {
            radialVelocity = Vector3.Zero;
        }
        else
        {
            radialVelocity += GravityDirection * GravityAcceleration * deltaSeconds;
        }

        Vector2 movementInput = _controlEnabled
            ? Input.GetVector(
                "move_left",
                "move_right",
                "move_forward",
                "move_backward")
            : Vector2.Zero;

        Vector3 desiredDirection =
            (GlobalTransform.Basis.X * movementInput.X) +
            (GlobalTransform.Basis.Z * movementInput.Y);
        desiredDirection = desiredDirection.Slide(RadialUp);
        if (desiredDirection.LengthSquared() > 0.000001f)
        {
            desiredDirection = desiredDirection.Normalized();
        }

        float acceleration = IsOnFloor()
            ? GroundAcceleration
            : AirAcceleration;
        tangentialVelocity = tangentialVelocity.MoveToward(
            desiredDirection * MoveSpeed,
            acceleration * deltaSeconds);

        if (_controlEnabled &&
            Input.IsActionJustPressed("jump") &&
            IsOnFloor())
        {
            radialVelocity = RadialUp * JumpVelocity;
        }

        Velocity = tangentialVelocity + radialVelocity;
        MoveAndSlide();

        UpdateDiagnostics();
    }

    public void SetControlEnabled(bool enabled)
    {
        _controlEnabled = enabled;
        if (_camera is not null)
        {
            _camera.Current = enabled;
        }

        Input.MouseMode = enabled
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }

    public void ResetToSpawn()
    {
        GlobalTransform = _spawnTransform;
        Velocity = Vector3.Zero;
        if (_cameraPitch is not null)
        {
            _cameraPitch.Rotation = _spawnCameraPitchRotation;
        }

        SnapOrientationToRadialUp();
        GD.Print("Planetary player reset to radial spawn point.");
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
        Basis targetBasis = CreateRadialBasis(RadialUp);
        GlobalTransform = new Transform3D(
            targetBasis,
            GlobalPosition);
        UpdateDiagnostics();
    }

    private void AlignToRadialUp(float deltaSeconds)
    {
        Basis targetBasis = CreateRadialBasis(RadialUp);
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

    private Basis CreateRadialBasis(Vector3 radialUp)
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        forward = forward.Slide(radialUp);
        if (forward.LengthSquared() <= 0.000001f)
        {
            Vector3 reference = Math.Abs(radialUp.Dot(Vector3.Forward)) > 0.95f
                ? Vector3.Right
                : Vector3.Forward;
            forward = reference.Slide(radialUp);
        }

        forward = forward.Normalized();
        Vector3 right = forward.Cross(radialUp).Normalized();
        Vector3 back = right.Cross(radialUp).Normalized();
        return new Basis(right, radialUp, back).Orthonormalized();
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
}
