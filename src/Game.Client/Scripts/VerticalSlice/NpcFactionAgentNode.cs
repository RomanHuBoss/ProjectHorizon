using System;
using Godot;

public partial class NpcFactionAgentNode : CharacterBody3D, IInteractable, IHitscanTarget
{
    private NpcFactionAgentDefinition? _definition;
    private NpcFactionRuntime? _runtime;
    private PlayerController? _player;
    private Vector3 _home;
    private double _ageSeconds;
    private double _lastHitAt = -100.0;
    private double _nextAttackAt;

    public string NpcId => _definition?.NpcId ?? string.Empty;

    public event Action<NpcFactionAgentNode, Node3D>? InteractionRequested;

    public event Action<NpcFactionAgentNode, NpcFactionCombatOutcome>? CombatResolved;

    public void Configure(
        NpcFactionAgentDefinition definition,
        NpcFactionRuntime runtime,
        PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(player);
        if (definition.ExistingScene)
        {
            throw new InvalidOperationException(
                $"NPC {definition.NpcId} is already represented by an authored scene node.");
        }
        _definition = definition;
        _runtime = runtime;
        _player = player;
        Name = definition.NpcId.Replace('.', '_');
        Position = new Vector3(
            (float)definition.PositionX,
            (float)definition.PositionY,
            (float)definition.PositionZ);
        _home = Position;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        AddToGroup("interactable");
        AddToGroup("npc_faction_agent");
        if (definition.Hostile)
        {
            AddToGroup("hostile_npc");
        }

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.ColorR,
                (float)definition.ColorG,
                (float)definition.ColorB,
                1.0f),
            Roughness = 0.62f,
            Metallic = definition.Archetype == NpcArchetype.Guard ? 0.35f : 0.08f,
            EmissionEnabled = definition.Hostile,
            Emission = definition.Hostile
                ? new Color(0.26f, 0.015f, 0.01f)
                : Colors.Black,
            EmissionEnergyMultiplier = definition.Hostile ? 1.45f : 0.0f
        };
        CapsuleMesh bodyMesh = new()
        {
            Radius = 0.38f,
            Height = 1.65f,
            RadialSegments = 12,
            Rings = 4,
            Material = material
        };
        AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = bodyMesh,
            Position = new Vector3(0.0f, 0.78f, 0.0f)
        });
        SphereMesh headMesh = new()
        {
            Radius = 0.27f,
            Height = 0.54f,
            RadialSegments = 12,
            Rings = 6,
            Material = material
        };
        AddChild(new MeshInstance3D
        {
            Name = "Head",
            Mesh = headMesh,
            Position = new Vector3(0.0f, 1.72f, 0.0f)
        });
        AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Position = new Vector3(0.0f, 0.85f, 0.0f),
            Shape = new CapsuleShape3D
            {
                Radius = 0.42f,
                Height = 1.75f
            }
        });
        ApplyRuntimeState();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_definition is null || _runtime is null || _player is null)
        {
            return;
        }
        _ageSeconds += delta;
        NpcFactionAgentView view = _runtime.GetAgent(_definition.NpcId);
        if (view.Defeated)
        {
            Visible = false;
            CollisionLayer = 0u;
            CollisionMask = 0u;
            Velocity = Vector3.Zero;
            return;
        }

        Visible = true;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        Vector3 offsetToPlayer = _player.GlobalPosition - GlobalPosition;
        offsetToPlayer.Y = 0.0f;
        double distance = offsetToPlayer.Length();
        Vector3 direction;
        double speedScale = 1.0;
        if (_definition.Hostile && distance <= _definition.DetectionRange)
        {
            direction = offsetToPlayer.LengthSquared() > 0.001f
                ? offsetToPlayer.Normalized()
                : Vector3.Zero;
            if (distance <= _definition.AttackRange)
            {
                direction = Vector3.Zero;
                TryAttackPlayer();
            }
        }
        else if (!_definition.Hostile && _ageSeconds - _lastHitAt <= 4.0 &&
                 distance <= 12.0)
        {
            direction = offsetToPlayer.LengthSquared() > 0.001f
                ? -offsetToPlayer.Normalized()
                : Vector3.Zero;
            speedScale = 1.35;
        }
        else
        {
            double phase = _ageSeconds * 0.33 + StablePhase(_definition.NpcId);
            Vector3 target = _home + new Vector3(
                (float)Math.Cos(phase) * (float)_definition.PatrolRadius,
                0.0f,
                (float)Math.Sin(phase) * (float)_definition.PatrolRadius);
            Vector3 offset = target - Position;
            offset.Y = 0.0f;
            direction = offset.LengthSquared() > 0.04f
                ? offset.Normalized()
                : Vector3.Zero;
        }

        Vector3 desired = direction * (float)(_definition.WalkSpeed * speedScale);
        Velocity = new Vector3(
            Mathf.MoveToward(Velocity.X, desired.X, (float)(delta * 5.0)),
            0.0f,
            Mathf.MoveToward(Velocity.Z, desired.Z, (float)(delta * 5.0)));
        if (Velocity.LengthSquared() > 0.03f)
        {
            Vector3 look = GlobalPosition + new Vector3(Velocity.X, 0.0f, Velocity.Z);
            LookAt(look, Vector3.Up, true);
        }
        MoveAndSlide();
        Position = new Vector3(Position.X, _home.Y, Position.Z);
        ClampToTerritory();
    }

    public void Interact(Node3D interactor)
    {
        if (_definition is null || _runtime is null ||
            _runtime.GetAgent(_definition.NpcId).Defeated)
        {
            return;
        }
        InteractionRequested?.Invoke(this, interactor);
    }

    public void ReceiveHit(Node3D source, Vector3 position, Vector3 normal)
    {
        if (_definition is null || _runtime is null)
        {
            return;
        }
        NpcFactionCombatOutcome outcome = _runtime.ApplyDamage(
            _definition.NpcId,
            25.0);
        _lastHitAt = _ageSeconds;
        ApplyRuntimeState();
        if (outcome.Respawned)
        {
            Position = _home;
        }
        CombatResolved?.Invoke(this, outcome);
        GD.Print(
            "TASK-122 NPC combat event: " +
            $"npc={outcome.NpcId}; health={outcome.HealthBefore:0.#}->{outcome.HealthAfter:0.#}; " +
            $"defeated={(outcome.DefeatedNow ? 1 : 0)}; respawned={(outcome.Respawned ? 1 : 0)}; " +
            $"defeatCount={outcome.DefeatCount}; repDelta={outcome.AppliedReputationDelta}.");
    }

    private void TryAttackPlayer()
    {
        if (_definition is null || _player is null ||
            _ageSeconds < _nextAttackAt)
        {
            return;
        }
        _nextAttackAt = _ageSeconds + _definition.AttackCooldownSeconds;
        _player.ReceiveExternalDamage(
            _definition.AttackDamage,
            $"npc:{_definition.NpcId}");
        GD.Print(
            "TASK-122 hostile NPC attack: " +
            $"npc={_definition.NpcId}; damage={_definition.AttackDamage:0.#}; " +
            $"cooldown={_definition.AttackCooldownSeconds:0.##}s.");
    }

    private void ApplyRuntimeState()
    {
        if (_definition is null || _runtime is null)
        {
            return;
        }
        NpcFactionAgentView view = _runtime.GetAgent(_definition.NpcId);
        Visible = !view.Defeated;
        CollisionLayer = view.Defeated ? 0u : 1u;
        CollisionMask = view.Defeated ? 0u : 1u;
    }

    private void ClampToTerritory()
    {
        if (_definition is null)
        {
            return;
        }
        float maximum = (float)Math.Max(2.0, _definition.PatrolRadius + 4.0);
        Vector3 offset = Position - _home;
        offset.Y = 0.0f;
        if (offset.Length() > maximum)
        {
            Vector3 clamped = offset.Normalized() * maximum;
            Position = new Vector3(
                _home.X + clamped.X,
                _home.Y,
                _home.Z + clamped.Z);
        }
    }

    private static double StablePhase(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (hash % 10000) / 10000.0 * Math.PI * 2.0;
        }
    }
}
