using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export]
    public float JumpVelocity { get; set; } = 5.0f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export(PropertyHint.Range, "1.0,6.0,0.1")]
    public float InteractionFallbackRadius { get; set; } = 2.75f;

    [Export(PropertyHint.Range, "-1.0,1.0,0.05")]
    public float InteractionFallbackMinForwardDot { get; set; } = -0.10f;

    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private RayCast3D _interactionRay = null!;
    private float _gravity;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _interactionRay = GetNode<RayCast3D>(
            "Head/Camera3D/InteractionRay");
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
            GetViewport().SetInputAsHandled();
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

    public bool TryInteract()
    {
        if (TryGetRayInteractable(
            out IInteractable rayInteractable,
            out Node3D rayNode))
        {
            rayInteractable.Interact(this);
            GD.Print($"Interaction ray: target={rayNode.Name}.");
            return true;
        }

        if (TryGetFallbackInteractable(
            out IInteractable fallbackInteractable,
            out Node3D fallbackNode,
            out float distance))
        {
            fallbackInteractable.Interact(this);
            GD.Print(
                $"Interaction proximity fallback: target={fallbackNode.Name}; " +
                $"distance={distance:0.00}m.");
            return true;
        }

        GD.Print(
            "Interaction ignored: no interactable target in ray or " +
            $"within {InteractionFallbackRadius:0.00}m proximity range.");
        return false;
    }

    public string GetInteractionPrompt()
    {
        if (TryGetRayInteractable(
            out _,
            out Node3D rayNode))
        {
            return $"aimed at {rayNode.Name} — press E";
        }

        if (TryGetFallbackInteractable(
            out _,
            out Node3D fallbackNode,
            out float distance))
        {
            return $"near {fallbackNode.Name} ({distance:0.0} m) — press E";
        }

        return "no target in interaction range";
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

    private bool TryGetRayInteractable(
        out IInteractable interactable,
        out Node3D interactableNode)
    {
        interactable = null!;
        interactableNode = null!;
        _interactionRay.ForceRaycastUpdate();
        if (!_interactionRay.IsColliding() ||
            _interactionRay.GetCollider() is not Node collider)
        {
            return false;
        }

        return TryResolveInteractable(
            collider,
            out interactable,
            out interactableNode);
    }

    private bool TryGetFallbackInteractable(
        out IInteractable interactable,
        out Node3D interactableNode,
        out float distance)
    {
        interactable = null!;
        interactableNode = null!;
        distance = 0.0f;
        float bestScore = float.MaxValue;
        bool found = false;
        Vector3 origin = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;

        foreach (Node groupedNode in GetTree().GetNodesInGroup("interactable"))
        {
            if (groupedNode is not Node3D candidate ||
                (candidate is CollisionObject3D collisionObject &&
                 collisionObject.CollisionLayer == 0u) ||
                !TryResolveInteractable(
                    candidate,
                    out IInteractable candidateInteractable,
                    out Node3D candidateNode))
            {
                continue;
            }

            Vector3 offset = candidateNode.GlobalPosition - origin;
            float candidateDistance = offset.Length();
            if (candidateDistance <= 0.001f ||
                candidateDistance > InteractionFallbackRadius)
            {
                continue;
            }

            float forwardDot = forward.Dot(offset / candidateDistance);
            if (forwardDot < InteractionFallbackMinForwardDot)
            {
                continue;
            }

            float score = candidateDistance + (1.0f - forwardDot) * 0.35f;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            interactable = candidateInteractable;
            interactableNode = candidateNode;
            distance = candidateDistance;
            found = true;
        }

        return found;
    }

    private static bool TryResolveInteractable(
        Node source,
        out IInteractable interactable,
        out Node3D interactableNode)
    {
        interactable = null!;
        interactableNode = null!;
        Node? current = source;
        for (int depth = 0; depth < 5 && current is not null; depth++)
        {
            if (current is IInteractable resolved &&
                current is Node3D resolvedNode)
            {
                interactable = resolved;
                interactableNode = resolvedNode;
                return true;
            }

            current = current.GetParent();
        }

        return false;
    }
}
