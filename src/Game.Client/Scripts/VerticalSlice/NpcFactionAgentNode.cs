using System;
using Godot;

public sealed record NpcNavigationAgentDiagnostics(
    string NpcId,
    bool NavigationEnabled,
    bool NavigationActive,
    int PathRequests,
    int AvoidanceSamples,
    int StuckRecoveries,
    int ForcedSnaps,
    int LastPathPointCount);

public partial class NpcFactionAgentNode : CharacterBody3D, IInteractable, IHitscanTarget
{
    private NpcFactionAgentDefinition? _definition;
    private NpcFactionRuntime? _runtime;
    private PlayerController? _player;
    private NpcNavigationSurfaceNode? _navigationSurface;
    private NavigationAgent3D? _navigationAgent;
    private Vector3 _home;
    private double _ageSeconds;
    private double _lastHitAt = -100.0;
    private double _nextAttackAt;
    private double _nextNavigationTargetRefreshAt;
    private double _nextProgressCheckAt;
    private double _recoveryUntil;
    private Vector3 _lastRequestedTarget;
    private Vector3 _progressAnchor;
    private Vector3 _recoveryTarget;
    private bool _navigationActive;
    private bool _navigationSnapped;
    private int _pathRequests;
    private int _avoidanceSamples;
    private int _stuckRecoveries;
    private int _forcedSnaps;
    private int _lastPathPointCount;

    public string NpcId => _definition?.NpcId ?? string.Empty;

    public NpcNavigationAgentDiagnostics NavigationDiagnostics => new(
        NpcId,
        _navigationAgent is not null && _navigationSurface is not null,
        _navigationActive,
        _pathRequests,
        _avoidanceSamples,
        _stuckRecoveries,
        _forcedSnaps,
        _lastPathPointCount);

    private readonly SystemFrequencyGate _behaviorDecisionGate =
        new(SystemFrequencyPolicy.NearbyAiHz);
    private Vector3 _cachedBehaviorTarget;
    private double _cachedBehaviorSpeedScale = 1.0;
    private int _behaviorDecisionCount;

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

    public void EnableNavigation(NpcNavigationSurfaceNode navigationSurface)
    {
        ArgumentNullException.ThrowIfNull(navigationSurface);
        _navigationSurface = navigationSurface;
        if (_navigationAgent is null)
        {
            _navigationAgent = new NavigationAgent3D
            {
                Name = "NavigationAgent3D",
                PathDesiredDistance = 0.65f,
                TargetDesiredDistance = 0.8f,
                Radius = 0.46f,
                Height = 1.8f,
                MaxSpeed = (float)Math.Max(0.5, _definition?.WalkSpeed ?? 1.5),
                NeighborDistance = 6.0f,
                MaxNeighbors = 8,
                TimeHorizonAgents = 0.8f,
                TimeHorizonObstacles = 0.65f,
                NavigationLayers = 1,
                AvoidanceLayers = 1,
                AvoidanceMask = 1,
                AvoidanceEnabled = true,
                KeepYVelocity = false,
                Use3DAvoidance = true
            };
            _navigationAgent.VelocityComputed += OnNavigationVelocityComputed;
            AddChild(_navigationAgent);
        }
        _navigationAgent.SetNavigationMap(navigationSurface.NavigationMap);
        _navigationAgent.AvoidanceEnabled = true;
        _navigationSnapped = false;
        _navigationActive = false;
        _nextNavigationTargetRefreshAt = 0.0;
        _nextProgressCheckAt = 0.0;
        _progressAnchor = GlobalPosition;
        _behaviorDecisionGate.Reset();
        _cachedBehaviorTarget = GlobalPosition;
        _cachedBehaviorSpeedScale = 1.0;
    }

    public void PrepareNavigationMapChange()
    {
        if (_navigationAgent is null)
        {
            return;
        }

        // TASK-172.1: detach the scene agent from the map that is about to be
        // freed/recreated with a different radial UP. EnableNavigation() will
        // bind it to the replacement map immediately after the frame handoff.
        _navigationAgent.AvoidanceEnabled = false;
        _navigationAgent.Velocity = Vector3.Zero;
        _navigationAgent.SetNavigationMap(new Rid());
        _navigationActive = false;
        _navigationSnapped = false;
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
            _navigationActive = false;
            if (_navigationAgent is not null)
            {
                _navigationAgent.AvoidanceEnabled = false;
                _navigationAgent.Velocity = Vector3.Zero;
            }
            return;
        }

        Visible = true;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        if (_navigationSurface is not null && _navigationAgent is not null)
        {
            UpdateNavigationMovement(delta);
            return;
        }

        UpdateLegacyMovement(delta);
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
            Velocity = Vector3.Zero;
            _navigationSnapped = false;
            if (_navigationAgent is not null)
            {
                _navigationAgent.Velocity = Vector3.Zero;
                _navigationAgent.SetVelocityForced(Vector3.Zero);
            }
        }
        CombatResolved?.Invoke(this, outcome);
        GD.Print(
            "TASK-122 NPC combat event: " +
            $"npc={outcome.NpcId}; health={outcome.HealthBefore:0.#}->{outcome.HealthAfter:0.#}; " +
            $"defeated={(outcome.DefeatedNow ? 1 : 0)}; respawned={(outcome.Respawned ? 1 : 0)}; " +
            $"defeatCount={outcome.DefeatCount}; repDelta={outcome.AppliedReputationDelta}.");
    }

    private void UpdateNavigationMovement(double delta)
    {
        if (_definition is null || _player is null ||
            _navigationSurface is null || _navigationAgent is null)
        {
            return;
        }
        if (!_navigationSurface.ReadyForQueries ||
            !_navigationSurface.IsPointInActiveArea(GlobalPosition))
        {
            _navigationActive = false;
            _navigationAgent.AvoidanceEnabled = false;
            _navigationAgent.Velocity = Vector3.Zero;
            Velocity = Vector3.Zero;
            return;
        }

        if (!_navigationSnapped)
        {
            Vector3 closest = _navigationSurface.GetClosestNavigationPoint(GlobalPosition);
            if (GlobalPosition.DistanceTo(closest) <= 4.0f)
            {
                if (GlobalPosition.DistanceTo(closest) > 0.04f)
                {
                    GlobalPosition = closest;
                    _navigationAgent.SetVelocityForced(Vector3.Zero);
                    _forcedSnaps++;
                }
                _home = SurfaceGlobalToLocal(closest);
            }
            _navigationSnapped = true;
            _progressAnchor = GlobalPosition;
        }

        Vector3 behaviorTarget;
        double speedScale;
        if (_ageSeconds < _recoveryUntil)
        {
            behaviorTarget = _recoveryTarget;
            speedScale = Math.Max(_cachedBehaviorSpeedScale, 1.15);
        }
        else
        {
            if (_behaviorDecisionGate.Consume(delta) || _behaviorDecisionCount == 0)
            {
                _cachedBehaviorTarget = ResolveBehaviorTarget(
                    out _cachedBehaviorSpeedScale);
                _cachedBehaviorTarget = ClampTargetToTerritory(_cachedBehaviorTarget);
                _cachedBehaviorTarget = _navigationSurface.GetClosestNavigationPoint(
                    _cachedBehaviorTarget);
                _behaviorDecisionCount++;
            }
            behaviorTarget = _cachedBehaviorTarget;
            speedScale = _cachedBehaviorSpeedScale;
        }

        bool targetChanged = _lastRequestedTarget.DistanceTo(behaviorTarget) > 0.65f;
        if (targetChanged || _ageSeconds >= _nextNavigationTargetRefreshAt)
        {
            _navigationAgent.TargetPosition = behaviorTarget;
            _lastRequestedTarget = behaviorTarget;
            _nextNavigationTargetRefreshAt = _ageSeconds + 0.45;
            _pathRequests++;
            Vector3[] probe = _navigationSurface.QueryPath(GlobalPosition, behaviorTarget);
            _lastPathPointCount = probe.Length;
        }

        Vector3 next = _navigationAgent.IsNavigationFinished()
            ? GlobalPosition
            : _navigationAgent.GetNextPathPosition();
        Vector3 offset = ProjectToSurfaceTangent(next - GlobalPosition);
        Vector3 direction = offset.LengthSquared() > 0.015f
            ? offset.Normalized()
            : Vector3.Zero;
        float speed = (float)(_definition.WalkSpeed * speedScale);
        Vector3 desiredVelocity = direction * speed;
        _navigationAgent.MaxSpeed = Math.Max(0.5f, speed);
        _navigationAgent.AvoidanceEnabled = true;
        _navigationAgent.Velocity = desiredVelocity;
        _navigationActive = true;

        if (_ageSeconds >= _nextProgressCheckAt)
        {
            float targetDistance = HorizontalDistance(GlobalPosition, behaviorTarget);
            float progress = HorizontalDistance(GlobalPosition, _progressAnchor);
            if (_nextProgressCheckAt > 0.0 &&
                targetDistance > 1.5f &&
                progress < 0.12f &&
                _navigationSurface.TryBuildRecoveryWaypoint(
                    GlobalPosition,
                    behaviorTarget,
                    StableSeed(_definition.NpcId) + _stuckRecoveries,
                    out Vector3 recovery))
            {
                _recoveryTarget = recovery;
                _recoveryUntil = _ageSeconds + 1.6;
                _navigationAgent.TargetPosition = recovery;
                _lastRequestedTarget = recovery;
                _pathRequests++;
                _stuckRecoveries++;
                GD.Print(
                    "TASK-124 NPC navigation recovery: " +
                    $"npc={_definition.NpcId}; recovery={_stuckRecoveries}; " +
                    $"target=({recovery.X:0.0},{recovery.Z:0.0}).");
            }
            _progressAnchor = GlobalPosition;
            _nextProgressCheckAt = _ageSeconds + 1.4;
        }
    }

    private Vector3 ResolveBehaviorTarget(out double speedScale)
    {
        if (_definition is null || _player is null)
        {
            speedScale = 0.0;
            return GlobalPosition;
        }
        Vector3 offsetToPlayer = ProjectToSurfaceTangent(
            _player.GlobalPosition - GlobalPosition);
        double distance = offsetToPlayer.Length();
        speedScale = 1.0;
        if (_definition.Hostile && distance <= _definition.DetectionRange)
        {
            if (distance <= _definition.AttackRange)
            {
                TryAttackPlayer();
                return GlobalPosition;
            }
            return _player.GlobalPosition;
        }
        if (!_definition.Hostile && _ageSeconds - _lastHitAt <= 4.0 && distance <= 12.0)
        {
            speedScale = 1.35;
            Vector3 away = offsetToPlayer.LengthSquared() > 0.001f
                ? -offsetToPlayer.Normalized()
                : Vector3.Back;
            return GlobalPosition + away * 5.0f;
        }

        double phase = _ageSeconds * 0.33 + StablePhase(_definition.NpcId);
        return SurfaceLocalToGlobal(
            _home + new Vector3(
                (float)Math.Cos(phase) * (float)_definition.PatrolRadius,
                0.0f,
                (float)Math.Sin(phase) * (float)_definition.PatrolRadius));
    }

    private void OnNavigationVelocityComputed(Vector3 safeVelocity)
    {
        if (!_navigationActive || _definition is null || _navigationAgent is null ||
            _navigationSurface is null)
        {
            return;
        }
        _avoidanceSamples++;
        Velocity = ProjectToSurfaceTangent(safeVelocity);
        UpDirection = SurfaceWorldUp();
        if (Velocity.LengthSquared() > 0.03f)
        {
            Vector3 look = GlobalPosition + Velocity.Normalized();
            LookAt(look, SurfaceWorldUp(), true);
        }
        MoveAndSlide();
        Position = new Vector3(
            Position.X,
            _navigationSurface.GetNavigationHeight(Position.X, Position.Z),
            Position.Z);
        ClampToTerritory();
    }

    private void UpdateLegacyMovement(double delta)
    {
        if (_definition is null || _player is null)
        {
            return;
        }

        if (_behaviorDecisionGate.Consume(delta) || _behaviorDecisionCount == 0)
        {
            _cachedBehaviorTarget = ResolveBehaviorTarget(
                out _cachedBehaviorSpeedScale);
            _cachedBehaviorTarget = ClampTargetToTerritory(_cachedBehaviorTarget);
            _behaviorDecisionCount++;
        }

        Vector3 offset = ProjectToSurfaceTangent(
            _cachedBehaviorTarget - GlobalPosition);
        Vector3 direction = offset.LengthSquared() > 0.04f
            ? offset.Normalized()
            : Vector3.Zero;
        Vector3 desired = direction *
            (float)(_definition.WalkSpeed * _cachedBehaviorSpeedScale);
        Vector3 currentTangent = ProjectToSurfaceTangent(Velocity);
        Velocity = currentTangent.MoveToward(
            desired,
            (float)(delta * 5.0));
        UpDirection = SurfaceWorldUp();
        if (Velocity.LengthSquared() > 0.03f)
        {
            Vector3 look = GlobalPosition + Velocity.Normalized();
            LookAt(look, SurfaceWorldUp(), true);
        }
        MoveAndSlide();
        float surfaceY = _navigationSurface is null
            ? _home.Y
            : _navigationSurface.GetNavigationHeight(Position.X, Position.Z);
        Position = new Vector3(Position.X, surfaceY, Position.Z);
        ClampToTerritory();
    }

    private void TryAttackPlayer()
    {
        if (_definition is null || _player is null || _ageSeconds < _nextAttackAt)
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
        if (_navigationAgent is not null)
        {
            _navigationAgent.AvoidanceEnabled = !view.Defeated;
        }
    }

    private Vector3 ClampTargetToTerritory(Vector3 globalTarget)
    {
        if (_definition is null)
        {
            return globalTarget;
        }
        Vector3 localTarget = SurfaceGlobalToLocal(globalTarget);
        float maximum = (float)Math.Max(2.0, _definition.PatrolRadius + 4.0);
        Vector3 offset = localTarget - _home;
        offset.Y = 0.0f;
        if (offset.Length() <= maximum)
        {
            return globalTarget;
        }
        Vector3 clamped = offset.Normalized() * maximum;
        Vector3 clampedLocal = new(
            _home.X + clamped.X,
            localTarget.Y,
            _home.Z + clamped.Z);
        return SurfaceLocalToGlobal(clampedLocal);
    }

    private void ClampToTerritory()
    {
        Vector3 clampedGlobal = ClampTargetToTerritory(GlobalPosition);
        Vector3 clampedLocal = SurfaceGlobalToLocal(clampedGlobal);
        float surfaceY = _navigationSurface is null
            ? clampedLocal.Y
            : _navigationSurface.GetNavigationHeight(
                clampedLocal.X,
                clampedLocal.Z);
        Position = new Vector3(clampedLocal.X, surfaceY, clampedLocal.Z);
    }

    public void ApplyWorldOriginShift(Vector3 worldShift)
    {
        _lastRequestedTarget -= worldShift;
        _progressAnchor -= worldShift;
        _recoveryTarget -= worldShift;
        _cachedBehaviorTarget -= worldShift;
        if (_navigationAgent is not null)
        {
            _navigationAgent.TargetPosition -= worldShift;
        }
        _nextNavigationTargetRefreshAt = 0.0;
        _nextProgressCheckAt = 0.0;
    }

    public void ApplyWorldFrameTransform(
        Transform3D previousFrame,
        Transform3D nextFrame)
    {
        _lastRequestedTarget = PlanetSurfacePhysicalFrameRuntime.MapPoint(
            previousFrame, nextFrame, _lastRequestedTarget);
        _progressAnchor = PlanetSurfacePhysicalFrameRuntime.MapPoint(
            previousFrame, nextFrame, _progressAnchor);
        _recoveryTarget = PlanetSurfacePhysicalFrameRuntime.MapPoint(
            previousFrame, nextFrame, _recoveryTarget);
        _cachedBehaviorTarget = PlanetSurfacePhysicalFrameRuntime.MapPoint(
            previousFrame, nextFrame, _cachedBehaviorTarget);
        Velocity = PlanetSurfacePhysicalFrameRuntime.MapVector(
            previousFrame.Basis.Orthonormalized(),
            nextFrame.Basis.Orthonormalized(),
            Velocity);
        UpDirection = nextFrame.Basis.Y.Normalized();
        if (_navigationAgent is not null)
        {
            _navigationAgent.TargetPosition = PlanetSurfacePhysicalFrameRuntime.MapPoint(
                previousFrame, nextFrame, _navigationAgent.TargetPosition);
        }
        _nextNavigationTargetRefreshAt = 0.0;
        _nextProgressCheckAt = 0.0;
    }

    private Vector3 SurfaceLocalToGlobal(Vector3 localPosition) =>
        GetParent() is Node3D parent
            ? parent.ToGlobal(localPosition)
            : localPosition;

    private Vector3 SurfaceGlobalToLocal(Vector3 globalPosition) =>
        GetParent() is Node3D parent
            ? parent.ToLocal(globalPosition)
            : globalPosition;

    private float HorizontalDistance(Vector3 left, Vector3 right) =>
        ProjectToSurfaceTangent(right - left).Length();

    private Vector3 ProjectToSurfaceTangent(Vector3 worldVector)
    {
        if (GetParent() is not Node3D parent)
        {
            worldVector.Y = 0.0f;
            return worldVector;
        }
        Basis basis = parent.GlobalTransform.Basis.Orthonormalized();
        Vector3 local = basis.Inverse() * worldVector;
        local.Y = 0.0f;
        return basis * local;
    }

    private Vector3 SurfaceWorldUp() =>
        GetParent() is Node3D parent
            ? parent.GlobalTransform.Basis.Y.Normalized()
            : Vector3.Up;

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value)
            {
                hash = hash * 31 + c;
            }
            return hash;
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
