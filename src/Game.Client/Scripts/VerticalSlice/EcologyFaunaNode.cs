using System;
using Godot;

public partial class EcologyFaunaNode : CharacterBody3D, IHitscanTarget, IInteractable
{
    public const float FlyingMinimumClearanceMeters = 1.6f;
    public const float FlyingPreferredClearanceMeters = 3.4f;
    public const float FlyingMaximumClearanceMeters = 7.2f;
    public const float FlyingAcceptanceSlackMeters = 0.35f;
    public const float FlyingMaximumVerticalSpeed = 3.0f;

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
    private PlanetSurfaceCurvedPatchDescriptor? _curvedPatch;
    private float _weatherSpeedMultiplier = 1.0f;
    private Vector3 _weatherWindVelocity = Vector3.Zero;
    private FaunaMorphologyProfile? _morphology;
    private Node3D? _visualRoot;
    private FaunaSimulationTier _simulationTier = FaunaSimulationTier.Near;
    private FaunaFlockSteering _flockSteering = new(
        Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0);
    private NavigationAgent3D? _groundNavigationAgent;
    private NpcNavigationSurfaceNode? _groundNavigationSurface;
    private double _nextGroundTargetRefreshAt;
    private Vector3 _groundNavigationTarget;
    private int _groundNavigationSamples;
    private int _visualInterpolationFrames;

    public string InstanceId { get; private set; } = string.Empty;

    public string FaunaId => _definition?.FaunaId ?? string.Empty;

    public string BehaviorState { get; private set; } = "Idle";

    public double Health { get; private set; }

    public double MaximumHealth => _definition?.Health ?? 0.0;

    public int DecisionCount { get; private set; }

    public string MovementMode => _definition?.MovementMode ?? string.Empty;

    public FaunaSimulationTier SimulationTier => _simulationTier;

    public FaunaMorphologyProfile? Morphology => _morphology;

    public bool GroundNavigationBound => _groundNavigationAgent is not null &&
        _groundNavigationSurface is not null &&
        string.Equals(MovementMode, "Ground", StringComparison.Ordinal);

    public int FlockNeighbors => _flockSteering.Neighbors;

    public int GroundNavigationSamples => _groundNavigationSamples;

    public int VisualInterpolationFrames => _visualInterpolationFrames;

    public bool AerialSteeringBound => _aerialSteering is not null &&
        string.Equals(MovementMode, "Flying", StringComparison.Ordinal);

    public bool IsActiveFlyingNavigationParticipant =>
        string.Equals(MovementMode, "Flying", StringComparison.Ordinal) &&
        Visible &&
        Health > 0.0;

    public bool InsideFlyingAltitudeEnvelope
    {
        get
        {
            if (_definition is null ||
                !string.Equals(MovementMode, "Flying", StringComparison.Ordinal) ||
                !IsActiveFlyingNavigationParticipant)
            {
                // Dead/hidden fauna is intentionally removed from the live aerial
                // steering set. Its frozen transform is persistence evidence, not
                // a live altitude invariant and must not make TASK-126 fail.
                return true;
            }

            float clearance = FlyingAltitudeClearanceMeters;
            return clearance >= FlyingMinimumClearanceMeters - FlyingAcceptanceSlackMeters &&
                clearance <= FlyingMaximumClearanceMeters + FlyingAcceptanceSlackMeters;
        }
    }

    public float FlyingAltitudeClearanceMeters => Position.Y - CurrentTerrainFloorY();

    public event Action<EcologyFaunaNode>? Observed;

    public void SetWeatherResponse(
        double speedMultiplier,
        double windMetersPerSecond,
        double windDirectionDegrees)
    {
        _weatherSpeedMultiplier = (float)Math.Clamp(speedMultiplier, 0.45, 1.25);
        if (!string.Equals(MovementMode, "Flying", StringComparison.Ordinal))
        {
            _weatherWindVelocity = Vector3.Zero;
            return;
        }
        float angle = Mathf.DegToRad((float)windDirectionDegrees);
        float drift = (float)Math.Clamp(windMetersPerSecond * 0.035, 0.0, 0.72);
        _weatherWindVelocity = new Vector3(
            Mathf.Sin(angle) * drift,
            0.0f,
            Mathf.Cos(angle) * drift);
    }

    public void Configure(
        EcologyFaunaDefinition definition,
        EcologyFaunaSpawn spawn,
        Node3D player,
        AerialSteeringRuntime? aerialSteering = null,
        PlanetSurfaceTerrainProfile? terrainProfile = null,
        PlanetSurfaceCurvedPatchDescriptor? curvedPatch = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(player);
        _definition = definition;
        _player = player;
        _aerialSteering = aerialSteering;
        _terrainProfile = terrainProfile;
        _curvedPatch = curvedPatch;
        InstanceId = spawn.InstanceId;
        Name = spawn.InstanceId.Replace('.', '_');
        float initialY = (float)spawn.PositionY;
        float terrainFloorY = _terrainProfile is null
            ? 0.0f
            : (float)(PlanetSurfaceTerrainRuntime.SampleHeight(
                _terrainProfile,
                spawn.PositionX,
                spawn.PositionZ) -
                (_curvedPatch?.TangentSagMeters(
                    spawn.PositionX, spawn.PositionZ) ?? 0.0));
        if (string.Equals(definition.MovementMode, "Ground", StringComparison.Ordinal))
        {
            initialY = terrainFloorY + 0.75f;
        }
        else if (string.Equals(definition.MovementMode, "Flying", StringComparison.Ordinal))
        {
            // TASK-164 regression fix: fresh flying spawns start inside the
            // same terrain-relative altitude band enforced by live steering.
            initialY = Mathf.Clamp(
                initialY,
                terrainFloorY + FlyingMinimumClearanceMeters + 0.9f,
                terrainFloorY + FlyingMaximumClearanceMeters - 0.7f);
        }
        Position = new Vector3(
            (float)spawn.PositionX,
            initialY,
            (float)spawn.PositionZ);
        Rotation = new Vector3(
            0.0f,
            Mathf.DegToRad((float)spawn.HeadingDegrees),
            0.0f);
        // Territory behavior keeps the authored airborne/home position. The
        // altitude controller below derives its floor independently from terrain,
        // so ReturnToTerritory/FollowGroup steering does not aim at the ground.
        _territoryCenter = Position;
        Health = definition.Health;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        AddToGroup("interactable");
        AddToGroup("ecology_fauna");

        _morphology = FaunaBodyPlanRuntime.Build(definition, spawn.InstanceId);
        float variation = (float)_morphology.ColorVariation;
        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(
                Mathf.Clamp((float)definition.ColorR + variation * 0.18f, 0.02f, 1.0f),
                Mathf.Clamp((float)definition.ColorG + variation * 0.10f, 0.02f, 1.0f),
                Mathf.Clamp((float)definition.ColorB - variation * 0.08f, 0.02f, 1.0f),
                1.0f),
            Roughness = (float)_morphology.MaterialRoughness
        };
        _visualRoot = new Node3D
        {
            Name = "FaunaVisualRoot"
        };
        _visualRoot.SetMeta("skeleton_family", _morphology.SkeletonId);
        _visualRoot.SetMeta("fixed_joint_count", _morphology.JointCount);
        AddChild(_visualRoot);

        SphereMesh body = new()
        {
            Radius = 0.62f,
            Height = 1.24f,
            RadialSegments = 12,
            Rings = 6,
            Material = material
        };
        Vector3 baseBodyScale = BodyScale(definition);
        Vector3 bodyScale = new(
            baseBodyScale.X * (float)_morphology.WidthScale,
            baseBodyScale.Y * (float)_morphology.HeightScale,
            baseBodyScale.Z * (float)_morphology.LengthScale);
        _visualRoot.AddChild(new MeshInstance3D
        {
            Name = "Torso_" + ModuleLeaf(_morphology.TorsoModule),
            Mesh = body,
            Scale = bodyScale
        });

        SphereMesh head = new()
        {
            Radius = 0.32f,
            Height = 0.64f,
            RadialSegments = 10,
            Rings = 5,
            Material = material
        };
        _visualRoot.AddChild(new MeshInstance3D
        {
            Name = "Head_" + ModuleLeaf(_morphology.HeadModule),
            Mesh = head,
            Position = ScaleHeadOffset(
                HeadOffset(definition),
                (float)_morphology.HeightScale,
                (float)_morphology.LengthScale),
            Scale = Vector3.One * 0.82f * (float)_morphology.WidthScale
        });
        AddBodyPlanVisualDetails(definition, material, _visualRoot, _morphology);

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
        _simulationTier = FaunaBehaviorRuntime.ResolveSimulationTier(distance);
        double frequency = EcologyRuntime.GetUpdateFrequencyHz(distance);
        if (frequency <= 0.0)
        {
            Velocity = Velocity.MoveToward(Vector3.Zero, (float)(delta * 3.0));
            if (string.Equals(_definition.MovementMode, "Flying", StringComparison.Ordinal))
            {
                // Terrain/frame changes can occur while distant fauna is on the
                // zero-Hz AI tier. Preserve the hard altitude invariant even
                // while behavioral steering is intentionally suspended.
                EnforceFlyingAltitudeSafety();
            }
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

    public override void _Process(double delta)
    {
        if (_visualRoot is null || !Visible)
        {
            return;
        }
        Vector3 tangentVelocity = ProjectToSurfaceTangent(Velocity);
        float speed = tangentVelocity.Length();
        float targetYaw = speed > 0.05f
            ? Mathf.Atan2(-tangentVelocity.X, -tangentVelocity.Z)
            : _visualRoot.Rotation.Y;
        Vector3 rotation = _visualRoot.Rotation;
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, Math.Clamp((float)delta * 7.0f, 0.0f, 1.0f));
        rotation.Z = Mathf.Lerp(rotation.Z, -Mathf.Clamp(speed * 0.018f, -0.10f, 0.10f), Math.Clamp((float)delta * 4.0f, 0.0f, 1.0f));
        _visualRoot.Rotation = rotation;
        float bob = BehaviorState is "Sleep" or "Idle"
            ? 0.0f
            : Mathf.Sin((float)_ageSeconds * (3.0f + speed * 0.25f)) * Math.Min(0.045f, speed * 0.008f);
        _visualRoot.Position = new Vector3(0.0f, bob, 0.0f);
        _visualInterpolationFrames++;
    }

    public FaunaFlockSample CreateFlockSample() => new(
        InstanceId,
        FaunaId,
        GlobalPosition,
        Velocity,
        Visible && Health > 0.0);

    public void SetFlockSteering(FaunaFlockSteering steering)
    {
        ArgumentNullException.ThrowIfNull(steering);
        _flockSteering = steering;
    }

    public void EnableGroundNavigation(NpcNavigationSurfaceNode navigationSurface)
    {
        ArgumentNullException.ThrowIfNull(navigationSurface);
        if (!string.Equals(MovementMode, "Ground", StringComparison.Ordinal))
        {
            return;
        }
        _groundNavigationSurface = navigationSurface;
        if (_groundNavigationAgent is null)
        {
            _groundNavigationAgent = new NavigationAgent3D
            {
                Name = "FaunaNavigationAgent3D",
                PathDesiredDistance = 0.75f,
                TargetDesiredDistance = 0.90f,
                Radius = 0.40f,
                Height = 1.25f,
                MaxSpeed = (float)Math.Max(0.5, _definition?.Speed ?? 1.0),
                NavigationLayers = 1,
                AvoidanceEnabled = false,
                KeepYVelocity = false,
                Use3DAvoidance = false
            };
            AddChild(_groundNavigationAgent);
        }
        _groundNavigationAgent.SetNavigationMap(navigationSurface.NavigationMap);
        _nextGroundTargetRefreshAt = 0.0;
    }

    public void PrepareGroundNavigationMapChange()
    {
        if (_groundNavigationAgent is null)
        {
            return;
        }
        _groundNavigationAgent.SetNavigationMap(new Rid());
        _groundNavigationAgent.Velocity = Vector3.Zero;
        _groundNavigationSurface = null;
    }

    /// <summary>
    /// Exercises the same shared aerial-steering path used by live flying fauna
    /// without moving the node or depending on the player's current distance.
    /// This keeps TASK-126 acceptance valid after planet-scale traversal, where
    /// the authored ecology population may legitimately be outside the 150 m tiered
    /// per-entity AI radius.
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
                _flockSteering.Neighbors > 0 ? 6.0 : 18.0,
                GlobalPosition.DistanceTo(TerritoryCenterGlobal()),
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
                _flockSteering.Neighbors > 0 ? 6.0 : 18.0,
                GlobalPosition.DistanceTo(TerritoryCenterGlobal()),
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

        Vector3 desiredDirection = SurfaceLocalVectorToWorld(_wanderDirection);
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
                desiredDirection = DirectionTo(TerritoryCenterGlobal());
                speedFactor = 0.75f;
                break;
            case "Sleep":
            case "Graze":
            case "Drink":
            case "Idle":
                speedFactor = 0.0f;
                break;
            case "FollowGroup":
                desiredDirection = DirectionTo(TerritoryCenterGlobal());
                speedFactor = 0.55f;
                break;
        }

        float speed = (float)_definition.Speed * speedFactor * _weatherSpeedMultiplier;
        Vector3 targetVelocity = desiredDirection * speed;
        if (_flockSteering.Neighbors > 0 &&
            (string.Equals(BehaviorState, "FollowGroup", StringComparison.Ordinal) ||
             string.Equals(BehaviorState, "Wander", StringComparison.Ordinal)))
        {
            Vector3 flock = _flockSteering.Combined;
            if (!string.Equals(_definition.MovementMode, "Flying", StringComparison.Ordinal))
            {
                flock = ProjectToSurfaceTangent(flock);
            }
            targetVelocity += flock * speed * 0.42f;
        }
        if (string.Equals(_definition.MovementMode, "Ground", StringComparison.Ordinal))
        {
            targetVelocity = ApplyGroundNavigation(targetVelocity, speed);
        }
        if (string.Equals(
            _definition.MovementMode,
            "Flying",
            StringComparison.Ordinal))
        {
            targetVelocity = ApplyFlyingSteering(targetVelocity, speed) +
                SurfaceLocalVectorToWorld(_weatherWindVelocity);
        }
        else if (string.Equals(
            _definition.MovementMode,
            "Aquatic",
            StringComparison.Ordinal))
        {
            float targetY = _territoryCenter.Y +
                (float)(0.18 * Math.Sin(_ageSeconds * 0.51));
            Vector3 localVelocity = SurfaceWorldVectorToLocal(targetVelocity);
            localVelocity.Y = Mathf.Clamp(
                (targetY - Position.Y) * 2.0f,
                -0.8f,
                0.8f);
            targetVelocity = SurfaceLocalVectorToWorld(localVelocity);
        }
        else
        {
            targetVelocity = ProjectToSurfaceTangent(targetVelocity);
        }

        UpDirection = SurfaceWorldUp();
        Velocity = Velocity.Lerp(targetVelocity, Math.Clamp(delta * 3.2f, 0.0f, 1.0f));
        MoveAndSlide();
        if (string.Equals(_definition.MovementMode, "Flying", StringComparison.Ordinal))
        {
            EnforceFlyingAltitudeSafety();
        }
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

        Vector3 horizontal = ProjectToSurfaceTangent(Velocity);
        if (horizontal.LengthSquared() > 0.03f)
        {
            LookAt(GlobalPosition + horizontal.Normalized(), SurfaceWorldUp());
        }
    }


    private Vector3 ApplyGroundNavigation(Vector3 targetVelocity, float speed)
    {
        if (_groundNavigationAgent is null || _groundNavigationSurface is null ||
            !_groundNavigationSurface.ReadyForQueries || speed <= 0.01f ||
            !_groundNavigationSurface.IsPointInActiveArea(GlobalPosition))
        {
            return ProjectToSurfaceTangent(targetVelocity);
        }
        Vector3 tangent = ProjectToSurfaceTangent(targetVelocity);
        Vector3 direction = tangent.LengthSquared() > 0.001f
            ? tangent.Normalized()
            : Vector3.Zero;
        if (_ageSeconds >= _nextGroundTargetRefreshAt ||
            _groundNavigationTarget.DistanceTo(GlobalPosition) < 1.0f)
        {
            Vector3 desiredTarget = GlobalPosition + direction * Math.Max(3.0f, speed * 1.35f);
            _groundNavigationTarget = _groundNavigationSurface.GetClosestNavigationPoint(desiredTarget);
            _groundNavigationAgent.TargetPosition = _groundNavigationTarget;
            _nextGroundTargetRefreshAt = _ageSeconds + 0.55;
            _groundNavigationSamples++;
        }
        Vector3 next = _groundNavigationAgent.IsNavigationFinished()
            ? _groundNavigationTarget
            : _groundNavigationAgent.GetNextPathPosition();
        Vector3 pathDirection = ProjectToSurfaceTangent(next - GlobalPosition);
        return pathDirection.LengthSquared() > 0.01f
            ? pathDirection.Normalized() * speed
            : tangent;
    }

    private Vector3 ApplyFlyingSteering(Vector3 targetVelocity, float speed)
    {
        if (_definition is null || _aerialSteering is null)
        {
            float fallbackY = CurrentTerrainFloorY() +
                3.4f +
                (float)(1.2 * Math.Sin(_ageSeconds * 0.33));
            Vector3 localVelocity = SurfaceWorldVectorToLocal(targetVelocity);
            localVelocity.Y = Mathf.Clamp(
                (fallbackY - Position.Y) * 1.8f,
                -2.5f,
                2.5f);
            return SurfaceLocalVectorToWorld(localVelocity);
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
        // Follow the terrain under the current horizontal position rather than
        // the original spawn point. Macro relief introduced in TASK-162.2 can
        // vary by several metres across one fauna territory.
        float terrainFloorY = CurrentTerrainFloorY();
        float preferredY = terrainFloorY + FlyingPreferredClearanceMeters +
            (float)Math.Sin(phase) * 1.15f;
        Vector3 desiredLocal = SurfaceWorldVectorToLocal(desired);
        desiredLocal = _aerialSteering.ApplyAltitudeEnvelope(
            desiredLocal,
            Position.Y,
            terrainFloorY + FlyingMinimumClearanceMeters,
            preferredY,
            terrainFloorY + FlyingMaximumClearanceMeters,
            1.65f,
            FlyingMaximumVerticalSpeed);

        // TASK-176.1: horizontal obstacle/POI/separation steering must not
        // normalize away the vertical authority of the terrain-following
        // controller. Bound tangent and altitude speeds independently.
        float maximumHorizontalSpeed = Math.Max(0.5f, speed * 1.35f);
        desiredLocal = AerialSteeringRuntime.ClampHorizontalAndVerticalSpeed(
            desiredLocal,
            maximumHorizontalSpeed,
            FlyingMaximumVerticalSpeed);
        return SurfaceLocalVectorToWorld(desiredLocal);
    }

    private void EnforceFlyingAltitudeSafety()
    {
        if (!IsActiveFlyingNavigationParticipant)
        {
            return;
        }

        float floorY = CurrentTerrainFloorY();
        float minimumY = floorY + FlyingMinimumClearanceMeters;
        float maximumY = floorY + FlyingMaximumClearanceMeters;
        float clampedY = Mathf.Clamp(Position.Y, minimumY, maximumY);
        if (Mathf.Abs(clampedY - Position.Y) <= 0.001f)
        {
            return;
        }

        bool below = Position.Y < minimumY;
        bool above = Position.Y > maximumY;
        Position = new Vector3(Position.X, clampedY, Position.Z);

        Vector3 localVelocity = SurfaceWorldVectorToLocal(Velocity);
        if ((below && localVelocity.Y < 0.0f) || (above && localVelocity.Y > 0.0f))
        {
            localVelocity.Y = 0.0f;
            Velocity = SurfaceLocalVectorToWorld(localVelocity);
        }
    }

    public void ApplyWorldOriginShift()
    {
        if (_definition is null || _aerialSteering is null ||
            !Visible || Health <= 0.0 ||
            !string.Equals(MovementMode, "Flying", StringComparison.Ordinal))
        {
            return;
        }
        _aerialSteering.UpsertEntity(
            InstanceId,
            "flying_fauna",
            GlobalPosition,
            Velocity,
            (float)Math.Clamp(0.55 * _definition.Scale, 0.42, 1.15));
    }

    public void ApplyWorldFrameTransform(
        Transform3D previousFrame,
        Transform3D nextFrame,
        PlanetSurfaceCurvedPatchDescriptor? previousPatch = null,
        PlanetSurfaceCurvedPatchDescriptor? nextPatch = null)
    {
        if (previousPatch is not null && nextPatch is not null)
        {
            double semanticHomeY = _territoryCenter.Y +
                previousPatch.TangentSagMeters(_territoryCenter.X, _territoryCenter.Z);
            _territoryCenter.Y = (float)(semanticHomeY -
                nextPatch.TangentSagMeters(_territoryCenter.X, _territoryCenter.Z));
            _curvedPatch = nextPatch;
        }
        Velocity = PlanetSurfacePhysicalFrameRuntime.MapVector(
            previousFrame.Basis.Orthonormalized(),
            nextFrame.Basis.Orthonormalized(),
            Velocity);
        UpDirection = nextFrame.Basis.Y.Normalized();
        EnforceFlyingAltitudeSafety();
        ApplyWorldOriginShift();
    }

    private float CurrentTerrainFloorY()
    {
        if (_terrainProfile is null)
        {
            return TerritoryCenterGlobal().Y;
        }

        return (float)(PlanetSurfaceTerrainRuntime.SampleHeight(
            _terrainProfile,
            Position.X,
            Position.Z) -
            (_curvedPatch?.TangentSagMeters(Position.X, Position.Z) ?? 0.0));
    }

    private Vector3 TerritoryCenterGlobal() =>
        GetParent() is Node3D parent
            ? parent.ToGlobal(_territoryCenter)
            : _territoryCenter;

    private Vector3 DirectionTo(Vector3 target)
    {
        Vector3 delta = target - GlobalPosition;
        bool flying = _definition is not null &&
            string.Equals(
                _definition.MovementMode,
                "Flying",
                StringComparison.Ordinal);
        if (!flying)
        {
            delta = ProjectToSurfaceTangent(delta);
        }

        return delta.LengthSquared() <= 0.0001f
            ? SurfaceLocalVectorToWorld(_wanderDirection)
            : delta.Normalized();
    }

    private Vector3 SurfaceWorldUp() =>
        GetParent() is Node3D parent
            ? parent.GlobalTransform.Basis.Y.Normalized()
            : Vector3.Up;

    private Vector3 SurfaceLocalVectorToWorld(Vector3 localVector) =>
        GetParent() is Node3D parent
            ? parent.GlobalTransform.Basis.Orthonormalized() * localVector
            : localVector;

    private Vector3 SurfaceWorldVectorToLocal(Vector3 worldVector) =>
        GetParent() is Node3D parent
            ? parent.GlobalTransform.Basis.Orthonormalized().Inverse() * worldVector
            : worldVector;

    private Vector3 ProjectToSurfaceTangent(Vector3 worldVector)
    {
        Vector3 local = SurfaceWorldVectorToLocal(worldVector);
        local.Y = 0.0f;
        return SurfaceLocalVectorToWorld(local);
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

    private static void AddBodyPlanVisualDetails(
        EcologyFaunaDefinition definition,
        StandardMaterial3D material,
        Node3D visualRoot,
        FaunaMorphologyProfile morphology)
    {
        Node3D details = new()
        {
            Name = "Modules"
        };
        float scale = (float)definition.Scale;
        visualRoot.AddChild(details);

        if (string.Equals(definition.BodyPlan, "Flying", StringComparison.Ordinal))
        {
            for (int side = -1; side <= 1; side += 2)
            {
                details.AddChild(new MeshInstance3D
                {
                    Name = side < 0 ? "WingL_" + ModuleLeaf(morphology.LimbModule) : "WingR_" + ModuleLeaf(morphology.LimbModule),
                    Mesh = new BoxMesh
                    {
                        Material = material,
                        Size = new Vector3(1.15f, 0.08f, 0.52f) * scale
                    },
                    Position = new Vector3(side * 0.72f, 0.08f, 0.0f) * scale,
                    Rotation = new Vector3(0.0f, side * -0.18f, side * 0.12f)
                });
            }
        }
        else if (string.Equals(definition.BodyPlan, "Aquatic", StringComparison.Ordinal))
        {
            for (int side = -1; side <= 1; side += 2)
            {
                details.AddChild(new MeshInstance3D
                {
                    Name = side < 0 ? "FinL_" + ModuleLeaf(morphology.LimbModule) : "FinR_" + ModuleLeaf(morphology.LimbModule),
                    Mesh = new BoxMesh
                    {
                        Material = material,
                        Size = new Vector3(0.55f, 0.06f, 0.34f) * scale
                    },
                    Position = new Vector3(side * 0.58f, -0.04f, 0.0f) * scale,
                    Rotation = new Vector3(0.0f, side * 0.30f, side * 0.10f)
                });
            }
        }
        else
        {
            int legPairs = definition.BodyPlan switch
            {
                "Biped" => 1,
                "Hexapod" => 3,
                "Crawler" => 2,
                _ => 2
            };
            for (int pair = 0; pair < legPairs; pair++)
            {
                float z = ((pair - (legPairs - 1) * 0.5f) * 0.46f) * scale;
                for (int side = -1; side <= 1; side += 2)
                {
                    details.AddChild(new MeshInstance3D
                    {
                        Name = $"Leg{pair}{(side < 0 ? "L" : "R")}_{ModuleLeaf(morphology.LimbModule)}",
                        Mesh = new CylinderMesh
                        {
                            Material = material,
                            TopRadius = 0.07f * scale,
                            BottomRadius = 0.10f * scale,
                            Height = 0.70f * scale,
                            RadialSegments = 6
                        },
                        Position = new Vector3(side * 0.48f, -0.45f, z),
                        Rotation = new Vector3(0.0f, 0.0f, side * -0.42f)
                    });
                }
            }
        }

        if (!morphology.TailModule.EndsWith(".none", StringComparison.Ordinal))
        {
            details.AddChild(new MeshInstance3D
            {
                Name = "Tail_" + ModuleLeaf(morphology.TailModule),
                Mesh = new CylinderMesh
                {
                    Material = material,
                    TopRadius = 0.04f * scale,
                    BottomRadius = 0.14f * scale,
                    Height = 0.82f * scale,
                    RadialSegments = 6
                },
                Position = new Vector3(0.0f, 0.02f, 0.78f) * scale,
                Rotation = new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f)
            });
        }

        if (!morphology.HornModule.EndsWith(".none", StringComparison.Ordinal))
        {
            for (int side = -1; side <= 1; side += 2)
            {
                details.AddChild(new MeshInstance3D
                {
                    Name = side < 0 ? "HornL_" + ModuleLeaf(morphology.HornModule) : "HornR_" + ModuleLeaf(morphology.HornModule),
                    Mesh = new CylinderMesh
                    {
                        Material = material,
                        TopRadius = 0.015f * scale,
                        BottomRadius = 0.07f * scale,
                        Height = 0.34f * scale,
                        RadialSegments = 5
                    },
                    Position = HeadOffset(definition) + new Vector3(side * 0.16f, 0.20f, -0.05f) * scale,
                    Rotation = new Vector3(0.35f, 0.0f, side * -0.22f)
                });
            }
        }

        if (!morphology.ShellModule.EndsWith(".none", StringComparison.Ordinal))
        {
            details.AddChild(new MeshInstance3D
            {
                Name = "Shell_" + ModuleLeaf(morphology.ShellModule),
                Mesh = new SphereMesh
                {
                    Material = material,
                    Radius = 0.52f * scale,
                    Height = 0.58f * scale,
                    RadialSegments = 8,
                    Rings = 4
                },
                Position = new Vector3(0.0f, 0.28f, 0.12f) * scale,
                Scale = new Vector3(1.05f, 0.42f, 0.86f)
            });
        }

        details.SetMeta("surface_visual_parts", details.GetChildCount());
        details.SetMeta("skeleton_family", morphology.SkeletonId);
        details.SetMeta("module_compatibility", FaunaBodyPlanRuntime.IsCompatible(morphology));
    }

    private static string ModuleLeaf(string module)
    {
        int separator = module.IndexOf('.');
        return separator >= 0 && separator + 1 < module.Length
            ? module[(separator + 1)..]
            : module;
    }

    private static Vector3 ScaleHeadOffset(
        Vector3 offset,
        float heightScale,
        float lengthScale) =>
        new(offset.X, offset.Y * heightScale, offset.Z * lengthScale);

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
