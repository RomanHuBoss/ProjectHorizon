using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export]
    public float JumpVelocity { get; set; } = 5.0f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.0025f;

    private Node3D _head = null!;
    private RayCast3D _interactionRay = null!;
    private float _gravity;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _interactionRay = GetNode<RayCast3D>("Head/Camera3D/InteractionRay");
        _interactionRay.AddException(this);

        _gravity = ProjectSettings
            .GetSetting("physics/3d/default_gravity")
            .AsSingle();

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Поворот всего персонажа по горизонтали.
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            // Поворот головы с камерой по вертикали.
            _head.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);

            Vector3 headRotation = _head.Rotation;
            headRotation.X = Mathf.Clamp(
                headRotation.X,
                Mathf.DegToRad(-89.0f),
                Mathf.DegToRad(89.0f));

            _head.Rotation = headRotation;
        }

        if (inputEvent.IsActionPressed("interact"))
        {
            TryInteract();
        }

        // Escape освобождает курсор.
        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        // Щелчок, выполненный при свободном курсоре, только возвращает захват
        // и не должен одновременно считаться выстрелом.
        if (inputEvent is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    private void TryInteract()
    {
        _interactionRay.ForceRaycastUpdate();

        if (!_interactionRay.IsColliding())
        {
            return;
        }

        if (_interactionRay.GetCollider() is IInteractable interactable)
        {
            interactable.Interact(this);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        // Гравитация.
        if (!IsOnFloor())
        {
            velocity.Y -= _gravity * (float)delta;
        }

        // Прыжок разрешён только с поверхности.
        if (Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
        }

        Vector2 input = Input.GetVector(
            "move_left",
            "move_right",
            "move_forward",
            "move_backward");

        Vector3 direction = new Vector3(input.X, 0.0f, input.Y);
        direction = (Transform.Basis * direction).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * MoveSpeed;
            velocity.Z = direction.Z * MoveSpeed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0.0f, MoveSpeed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0.0f, MoveSpeed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }
}