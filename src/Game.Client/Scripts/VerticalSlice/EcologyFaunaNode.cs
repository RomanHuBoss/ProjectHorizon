using System;
using Godot;

public partial class EcologyFaunaNode : CharacterBody3D, IHitscanTarget, IInteractable
{
    private EcologyFaunaDefinition? _definition;
    private Node3D? _player;
    private Vector3 _territoryCenter;
    private double _decisionAccumulator;
    private double _ageSeconds;
    private double _lastHitAge = -100.0;
    private double _nextAttackAtAge;
    private Vector3 _wanderDirection = Vector3.Forward;
    private AerialSteeringRuntime? _aerialSteering;
    private PlanetSurfaceTerrainProfile? _terrainProfile;

    public string InstanceId { get; private set; } = string.Empty;

    public string FaunaId => _definition?.FaunaId ?? string.Empty;

    public string BehaviorState { get; private set; } = "Idle";

    public double Health { get; private set; }

    public double MaximumHealth => _definition?.Health ?? 0.0;

    public int DecisionCount { get; private set; }

    public string MovementMode => _definition?.MovementMode ?? string.Empty;

    public bool AerialSteeringBound => _aerialSteering is not null &&
        string.Equals(MovementMode, "Flying", StringComparison.Ordinal);

    public bool InsideFlyingAltitudeEnvelope => _definition is null ||
        !string.Equals(MovementMode, "Flying", StringComparison.Ordinal) ||
        (GlobalPosition.Y >= _territoryCenter.Y + 1.25f &&
         GlobalPosition.Y <= _territoryCenter.Y + 7.55f);

    public event Action<EcologyFaunaNode>? Observed;

    public void Configure(
        EcologyFaunaDefinition definition,
        EcologyFaunaSpawn spawn,
        Node3D player,
        AerialSteeringRuntime? aerialSteering = null,
        PlanetSurfaceTerrainProfile? terrainProfile = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(player);
        _definition = definition;
        _player = player;
        _aerialSteering = aerialSteering;
        _terrainProfile = terrainProfile;
        InstanceId = spawn.InstanceId;
        Name = spawn.InstanceId.Replace('.', '_');
        float initialY = (float)spawn.PositionY;
        if (_terrainProfile is not null &&
            string.Equals(definition.MovementMode, "Ground", StringComparison.Ordinal))
        {
            initialY = (float)PlanetSurfaceTerrainRuntime.SampleHeight(
                _terrainProfile,
                spawn.PositionX,
                spawn.PositionZ) + 0.75f;
        }
        Position = new Vector3(
            (float)spawn.PositionX,
            initialY,
            (float)spawn.PositionZ);
        Rotation = new Vector3(
            0.0f,
            Mathf.DegToRad((float)spawn.HeadingDegrees),
            0.0f);
        _territoryCenter = Position;
        Health = definition.Health;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        AddToGroup("interactable");
        AddToGroup("ecology_fauna");

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                (float)definition.ColorR,
                (float)definition.ColorG,
                (float)definition.ColorB,
                1.0f),
            Roughness = 0.76f
        };
        SphereMesh body = new()
        {
            Radius = 0.62f,
            Height = 1.24f,
            RadialSegments = 12,
            Rings = 6,
            Material = material
        };
        MeshInstance3D bodyNode = new()
        {
            Name = "Body",
            Mesh = body,
            Scale = BodyScale(definition)
        };
        AddChild(bodyNode);

        SphereMesh head = new()
        {
            Radius = 0.32f,
            Height = 0.64f,
            RadialSegments = 10,
            Rings = 5,
            Material = material
        };
        MeshInstance3D headNode = new()
        {
            Name = "Head",
            Mesh = head,
            Position = HeadOffset(definition),
            Scale = Vector3.One * 0.82f
        };
        AddChild(headNode);

        AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new SphereShape3D
            {
                Radius = (float)Math.Clamp(
                    0.55 * definition.Scale,
                    0.42,
                    1.15)
            }
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_definition is null || _player is null || !Visible)
        {
            return;
        }

        _ageSeconds += delta;
        double distance = GlobalPosition.DistanceTo(_player.GlobalPosition);
        double frequency = EcologyRuntime.GetUpdateFrequencyHz(distance);
        if (frequency <= 0.0)
        {
            Velocity = Velocity.MoveToward(Vector3.Zero, (float)(delta * 3.0));
            return;
        }

        _decisionAccumulator += delta;
        double interval = 1.0 / frequency;
        if (_decisionAccumulator >= interval)
        {
            _decisionAccumulator = 0.0;
            UpdateDecision(distance);
        }

        ApplySteering((float)delta);
    }

    /// <summary>
    /// Exercises the same shared aerial-steering path used by live flying fauna
    /// without moving the node or depending on the player's current distance.
    /// This keeps TASK-126 acceptance valid after planet-scale traversal, where
    /// the authored ecology population may legitimately be outside the 50 m live
    /// AI update radius.
    /// </summary>
    public bool StepAerialForAcceptance()
    {
        if (_definition is null || _aerialSteering is null ||
            !string.Equals(MovementMode, "Flying", StringComparison.Ordinal))
        {
            return false;
        }

        Vector3 originalVelocity = Velocity;
        float speed = Math.Max(0.5f, (float)_definition.Speed * 0.55f);
        Vector3 direction = _wanderDirection.LengthSquared() > 0.0001f
            ? _wanderDirection.Normalized()
            : Vector3.Forward;

        _ = ApplyFlyingSteering(direction * speed, speed);

        // The acceptance probe must not resurrect or move dead fauna and must
        // not replace the velocity that gameplay owned before the probe.
        if (!Visible || Health <= 0.0)
        {
            _aerialSteering.RemoveEntity(InstanceId);
        }
        else
        {
            _aerialSteering.UpsertEntity(
                InstanceId,
                "flying_fauna",
                GlobalPosition,
                originalVelocity,
                (float)Math.Clamp(0.55 * _definition.Scale, 0.42, 1.15));
        }

        return true;
    }

    public void Interact(Node3D interactor)
    {
        Observed?.Invoke(this);
    }

    public void ReceiveHit(
        Node3D source,
        Vector3 position,
        Vector3 normal)
    {
        if (_definition is null || !Visible)
        {
            return;
        }

        Health = Math.Max(0.0, Health - 25.0);
        _lastHitAge = _ageSeconds;
        double distance = _player is null
            ? 0.0
            : GlobalPosition.DistanceTo(_player.GlobalPosition);
        BehaviorState = EcologyRuntime.SelectBehavior(
            _definition,
            new EcologyBehaviorContext(
                distance,
                Hunger(),
                Thirst(),
                Fatigue(),
                4.0,
                GlobalPosition.DistanceTo(_territoryCenter),
                IsAtWater(),
                true));
        GD.Print(
            "TASK-116 fauna reaction PASS: " +
            $"instance={InstanceId}; species={_definition.FaunaId}; " +
            $"health={Health:0.#}/{MaximumHealth:0.#}; state={BehaviorState}; " +
            $"aggression={_definition.Aggression:0.00}.");

        if (Health <= 0.0)
        {
            Visible = false;
            CollisionLayer = 0u;
            CollisionMask = 0u;
            Velocity = Vector3.Zero;
            _aerialSteering?.RemoveEntity(InstanceId);
        }
    }

    private void UpdateDecision(double distanceToPlayer)
    {
        if (_definition is null)
        {
            return;
        }

        bool hitRecently = _ageSeconds - _lastHitAge <= 4.0;
        BehaviorState = EcologyRuntime.SelectBehavior(
            _definition,
            new EcologyBehaviorContext(
                distanceToPlayer,
                Hunger(),
                Thirst(),
                Fatigue(),
                6.0 + (3.0 * Math.Sin(_ageSeconds * 0.17)),
                GlobalPosition.DistanceTo(_territoryCenter),
                IsAtWater(),
                hitRecently));
        DecisionCount++;

        double phase = _ageSeconds * 0.41 +
            (EcologyPlanner.StableHash(InstanceId) % 997) / 997.0;
        _wanderDirection = new Vector3(
            (float)Math.Cos(phase),
            0.0f,
            (float)Math.Sin(phase)).Normalized();
    }

    private void ApplySteering(float delta)
    {
        if (_definition is null || _player is null)
        {
            return;
        }

        Vector3 desiredDirection = _wanderDirection;
        float speedFactor = 0.34f;
        switch (BehaviorState)
        {
            case "Attack":
                desiredDirection = DirectionTo(_player.GlobalPosition);
                speedFactor = 1.0f;
                break;
            case "Flee":
                desiredDirection = -DirectionTo(_player.GlobalPosition);
                speedFactor = 1.15f;
                break;
            case "Threaten":
            case "Investigate":
                desiredDirection = DirectionTo(_player.GlobalPosition);
                speedFactor = 0.42f;
                break;
            case "ReturnToTerritory":
                desiredDirection = DirectionTo(_territoryCenter);
                speedFactor = 0.75f;
                break;
            case "Sleep":
            case "Graze":
            case "Drink":
            case "Idle":
                speedFactor = 0.0f;
                break;
            case "FollowGroup":
                desiredDirection = DirectionTo(_territoryCenter);
                speedFactor = 0.55f;
                break;
        }

        float speed = (float)_definition.Speed * speedFactor;
        Vector3 targetVelocity = desiredDirection * speed;
        if (string.Equals(
            _definition.MovementMode,
            "Flying",
            StringComparison.Ordinal))
        {
            targetVelocity = ApplyFlyingSteering(targetVelocity, speed);
        }
        else if (string.Equals(
            _definition.MovementMode,
            "Aquatic",
            StringComparison.Ordinal))
        {
            float targetY = _territoryCenter.Y +
                (float)(0.18 * Math.Sin(_ageSeconds * 0.51));
            targetVelocity.Y = Mathf.Clamp(
                (targetY - Position.Y) * 2.0f,
                -0.8f,
                0.8f);
        }
        else
        {
            targetVelocity.Y = 0.0f;
        }

        Velocity = Velocity.Lerp(targetVelocity, Math.Clamp(delta * 3.2f, 0.0f, 1.0f));
        MoveAndSlide();
        if (string.Equals(BehaviorState, "Attack", StringComparison.Ordinal) &&
            _player is PlayerController player &&
            GlobalPosition.DistanceTo(player.GlobalPosition) <= 1.75f &&
            _ageSeconds >= _nextAttackAtAge)
        {
            double damage = 6.0 + _definition.Aggression * 5.0;
            player.ReceiveExternalDamage(damage, $"fauna:{_definition.FaunaId}");
            _nextAttackAtAge = _ageSeconds + 1.25;
            GD.Print(
                "TASK-120 fauna damage PASS: " +
                $"species={_definition.FaunaId}; damage={damage:0.0}; cooldown=1.25s.");
        }
        if (!string.Equals(
            _definition.MovementMode,
            "Flying",
            StringComparison.Ordinal) &&
            !string.Equals(
                _definition.MovementMode,
                "Aquatic",
                StringComparison.Ordinal))
        {
            float terrainY = _terrainProfile is null
                ? 0.0f
                : (float)PlanetSurfaceTerrainRuntime.SampleHeight(
                    _terrainProfile,
                    Position.X,
                    Position.Z);
            Position = new Vector3(Position.X, terrainY + 0.75f, Position.Z);
        }

        Vector3 horizontal = new(Velocity.X, 0.0f, Velocity.Z);
        if (horizontal.LengthSquared() > 0.03f)
        {
            LookAt(GlobalPosition + horizontal, Vector3.Up);
        }
    }


    private Vector3 ApplyFlyingSteering(Vector3 targetVelocity, float speed)
    {
        if (_definition is null || _aerialSteering is null)
        {
            float fallbackY = _territoryCenter.Y +
                1.8f +
                (float)(1.2 * Math.Sin(_ageSeconds * 0.33));
            targetVelocity.Y = Mathf.Clamp(
                (fallbackY - Position.Y) * 1.8f,
                -2.5f,
                2.5f);
            return targetVelocity;
        }

        float radius = (float)Math.Clamp(0.55 * _definition.Scale, 0.42, 1.15);
        _aerialSteering.UpsertEntity(
            InstanceId,
            "flying_fauna",
            GlobalPosition,
            Velocity,
            radius);
        _aerialSteering.RecordFlyingFaunaSample();

        Vector3 desired = targetVelocity;
        AerialPointOfInterest? poi = _aerialSteering.FindClosestPointOfInterest(
            GlobalPosition,
            "fauna",
            24.0f);
        bool exploratoryState = string.Equals(BehaviorState, "Idle", StringComparison.Ordinal) ||
            string.Equals(BehaviorState, "Investigate", StringComparison.Ordinal) ||
            string.Equals(BehaviorState, "FollowGroup", StringComparison.Ordinal);
        if (poi is not null && exploratoryState && speed > 0.01f)
        {
            Vector3 poiVelocity = _aerialSteering.Arrive(
                GlobalPosition,
                poi.Position,
                speed,
                6.0f,
                Math.Max(1.0f, poi.Radius));
            desired = desired.Lerp(poiVelocity, 0.28f);
        }

        Vector3 separation = _aerialSteering.ComputeEntitySeparation(
            InstanceId,
            "flying_fauna",
            GlobalPosition,
            5.5f,
            Math.Max(1.2f, speed * 0.75f));
        Vector3 avoidance = _aerialSteering.ComputeObstacleAvoidance(
            GlobalPosition,
            desired.LengthSquared() > 0.01f ? desired : Velocity,
            radius,
            1.25f,
            Math.Max(2.0f, speed * 1.35f));
        desired += separation + avoidance;

        float phase = (float)(_ageSeconds * 0.33 +
            (EcologyPlanner.StableHash(InstanceId) % 31) * 0.17);
        float preferredY = _territoryCenter.Y + 3.4f +
            (float)Math.Sin(phase) * 1.15f;
        desired = _aerialSteering.ApplyAltitudeEnvelope(
            desired,
            GlobalPosition.Y,
            _territoryCenter.Y + 1.6f,
            preferredY,
            _territoryCenter.Y + 7.2f,
            1.65f,
            3.0f);

        float maximumSpeed = Math.Max(0.5f, speed * 1.35f);
        if (desired.Length() > maximumSpeed)
        {
            desired = desired.Normalized() * maximumSpeed;
        }
        return desired;
    }

    private Vector3 DirectionTo(Vector3 target)
    {
        Vector3 delta = target - GlobalPosition;
        if (_definition is null ||
            !string.Equals(
                _definition.MovementMode,
                "Flying",
                StringComparison.Ordinal))
        {
            delta.Y = 0.0f;
        }

        return delta.LengthSquared() <= 0.0001f
            ? _wanderDirection
            : delta.Normalized();
    }

    private bool IsAtWater()
    {
        return _definition is not null &&
            string.Equals(
                _definition.MovementMode,
                "Aquatic",
                StringComparison.Ordinal);
    }

    private double Hunger()
    {
        return 0.5 + (0.5 * Math.Sin(_ageSeconds * 0.071 + 0.4));
    }

    private double Thirst()
    {
        return 0.5 + (0.5 * Math.Sin(_ageSeconds * 0.053 + 1.3));
    }

    private double Fatigue()
    {
        return 0.5 + (0.5 * Math.Sin(_ageSeconds * 0.031 + 2.1));
    }

    private static Vector3 BodyScale(EcologyFaunaDefinition definition)
    {
        float scale = (float)definition.Scale;
        return definition.BodyPlan switch
        {
            "Biped" => new Vector3(0.75f, 1.35f, 0.70f) * scale,
            "Hexapod" => new Vector3(1.25f, 0.65f, 1.15f) * scale,
            "Flying" => new Vector3(1.55f, 0.38f, 0.80f) * scale,
            "Aquatic" => new Vector3(1.65f, 0.45f, 0.62f) * scale,
            "Crawler" => new Vector3(1.20f, 0.42f, 0.92f) * scale,
            _ => new Vector3(1.10f, 0.78f, 0.78f) * scale
        };
    }

    private static Vector3 HeadOffset(EcologyFaunaDefinition definition)
    {
        float scale = (float)definition.Scale;
        return definition.BodyPlan switch
        {
            "Biped" => new Vector3(0.0f, 0.82f, -0.22f) * scale,
            "Flying" => new Vector3(0.0f, 0.05f, -0.78f) * scale,
            "Aquatic" => new Vector3(0.0f, 0.02f, -0.86f) * scale,
            _ => new Vector3(0.0f, 0.22f, -0.66f) * scale
        };
    }
}
