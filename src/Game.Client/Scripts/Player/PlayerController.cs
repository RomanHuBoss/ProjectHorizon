using System;
using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export]
    public float SprintMultiplier { get; set; } = 1.65f;

    [Export]
    public float CrouchMultiplier { get; set; } = 0.52f;

    [Export]
    public float JumpVelocity { get; set; } = 5.0f;

    [Export]
    public float JetpackAcceleration { get; set; } = 9.0f;

    [Export]
    public float SwimSpeed { get; set; } = 3.2f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.0025f;

    [Export]
    public bool InvertLookX { get; set; }

    [Export]
    public bool InvertLookY { get; set; }

    [Export(PropertyHint.Range, "1.0,6.0,0.1")]
    public float InteractionFallbackRadius { get; set; } = 2.75f;

    [Export(PropertyHint.Range, "-1.0,1.0,0.05")]
    public float InteractionFallbackMinForwardDot { get; set; } = -0.10f;

    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private RayCast3D _interactionRay = null!;
    private CollisionShape3D _collisionShape = null!;
    private CapsuleShape3D _capsuleShape = null!;
    private HitscanWeapon _weapon = null!;
    private float _gravity;
    private float _standingCapsuleHeight;
    private float _standingHeadY;

    public IPlayerMovementResourceProvider? MovementResources { get; set; }
    public Action<double, string>? ExternalDamageHandler { get; set; }
    public Action? WeaponFired { get; set; }

    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsJetpacking { get; private set; }
    public bool IsSwimming { get; private set; }

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _interactionRay = GetNode<RayCast3D>("Head/Camera3D/InteractionRay");
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
        _capsuleShape = _collisionShape.Shape as CapsuleShape3D ??
            throw new InvalidOperationException("Player collision must use CapsuleShape3D.");
        _weapon = GetNode<HitscanWeapon>("Head/Camera3D/HitscanWeapon");
        _weapon.FireCommitted = () => WeaponFired?.Invoke();
        _interactionRay.AddException(this);
        _standingCapsuleHeight = _capsuleShape.Height;
        _standingHeadY = _head.Position.Y;

        _gravity = ProjectSettings
            .GetSetting("physics/3d/default_gravity")
            .AsSingle();

        GameUserSettingsService.ApplyToPlayer(this);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float yawSign = InvertLookX ? 1.0f : -1.0f;
            float pitchSign = InvertLookY ? 1.0f : -1.0f;
            RotateY(mouseMotion.Relative.X * MouseSensitivity * yawSign);
            _head.RotateX(mouseMotion.Relative.Y * MouseSensitivity * pitchSign);

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

    public bool TryInteract()
    {
        if (TryGetRayInteractable(out IInteractable rayInteractable, out Node3D rayNode))
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
        if (TryGetRayInteractable(out _, out Node3D rayNode))
        {
            return GameLocalizationService.Format("ui.player.interaction.aimed", ("target", rayNode.Name));
        }

        if (TryGetFallbackInteractable(out _, out Node3D fallbackNode, out float distance))
        {
            return GameLocalizationService.Format("ui.player.interaction.near", ("target", fallbackNode.Name), ("distance", distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return GameLocalizationService.Text("ui.player.interaction.none");
    }

    public void ReceiveExternalDamage(double damage, string source)
    {
        if (damage <= 0.0 || !double.IsFinite(damage))
        {
            return;
        }
        ExternalDamageHandler?.Invoke(damage, source);
    }

    public void SetSwimming(bool swimming)
    {
        IsSwimming = swimming;
        MovementResources?.SetSwimming(swimming);
    }

    public void SetFieldOfView(float degrees)
    {
        if (_camera is not null)
        {
            _camera.Fov = Mathf.Clamp(degrees, 60.0f, 110.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)Math.Max(0.0, delta);
        Vector3 velocity = Velocity;
        bool jumpHeld = Input.IsActionPressed("jump");
        bool sprintRequested = Input.IsActionPressed("player_sprint");
        bool crouchRequested = Input.IsActionPressed("player_crouch");
        Vector2 input = Input.GetVector(
            "move_left",
            "move_right",
            "move_forward",
            "move_backward");

        IsCrouching = !IsSwimming && crouchRequested;
        UpdateCrouchGeometry(IsCrouching);

        bool hasMoveInput = input.LengthSquared() > 0.0001f;
        IsSprinting = !IsSwimming && !IsCrouching && sprintRequested && hasMoveInput &&
            (MovementResources?.TryConsumeStamina(delta) ?? true);

        IsJetpacking = !IsSwimming && !IsOnFloor() && jumpHeld &&
            (MovementResources?.TryConsumeJetpackEnergy(delta) ?? true);

        if (IsSwimming)
        {
            velocity.Y = Mathf.MoveToward(velocity.Y, 0.0f, SwimSpeed * dt * 3.0f);
            if (jumpHeld)
            {
                velocity.Y = SwimSpeed;
            }
            else if (crouchRequested)
            {
                velocity.Y = -SwimSpeed;
            }
        }
        else if (IsJetpacking)
        {
            velocity.Y = Mathf.MoveToward(
                velocity.Y,
                JumpVelocity,
                JetpackAcceleration * dt);
        }
        else if (!IsOnFloor())
        {
            velocity.Y -= _gravity * dt;
        }

        if (!IsSwimming && Input.IsActionJustPressed("jump") && IsOnFloor())
        {
            velocity.Y = JumpVelocity;
        }

        Vector3 direction = new Vector3(input.X, 0.0f, input.Y);
        direction = (Transform.Basis * direction).Normalized();
        float speed = IsSwimming
            ? SwimSpeed
            : MoveSpeed * (IsSprinting ? SprintMultiplier : IsCrouching ? CrouchMultiplier : 1.0f);

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * speed;
            velocity.Z = direction.Z * speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0.0f, speed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0.0f, speed);
        }

        Velocity = velocity;
        MoveAndSlide();
        MovementResources?.RecoverMovementResources(
            delta,
            IsSprinting,
            IsJetpacking);
    }

    private void UpdateCrouchGeometry(bool crouching)
    {
        float targetHeight = crouching
            ? Math.Max(_capsuleShape.Radius * 2.0f, _standingCapsuleHeight * 0.66f)
            : _standingCapsuleHeight;
        _capsuleShape.Height = targetHeight;
        Vector3 headPosition = _head.Position;
        headPosition.Y = crouching ? _standingHeadY * 0.62f : _standingHeadY;
        _head.Position = headPosition;
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

        return TryResolveInteractable(collider, out interactable, out interactableNode);
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
                (candidate is CollisionObject3D collisionObject && collisionObject.CollisionLayer == 0u) ||
                !TryResolveInteractable(
                    candidate,
                    out IInteractable candidateInteractable,
                    out Node3D candidateNode))
            {
                continue;
            }

            Vector3 offset = candidateNode.GlobalPosition - origin;
            float candidateDistance = offset.Length();
            if (candidateDistance <= 0.001f || candidateDistance > InteractionFallbackRadius)
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
            if (current is IInteractable resolved && current is Node3D resolvedNode)
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
