using System;
using System.Collections.Generic;
using Godot;

public enum NpcShipNavigationRole
{
    PatrolLeader,
    FormationWing,
    TraderArrival,
    HostileRaider
}

public enum NpcShipNavigationState
{
    Arrive,
    Formation,
    Pursuit,
    CombatApproach,
    BreakAway,
    Evade
}

public sealed record NpcShipNavigationDiagnostics(
    string ShipId,
    NpcShipNavigationRole Role,
    NpcShipNavigationState State,
    int SteeringSamples,
    int StateTransitions,
    int AvoidanceActivations,
    int NeighborSamples,
    int WaypointAdvances,
    float MinimumObstacleClearance,
    bool HasTarget,
    bool Active);

public partial class NpcShipNavigationNode : CharacterBody3D
{
    private AerialSteeringRuntime? _steering;
    private Node3D? _primaryTarget;
    private NpcShipNavigationNode? _formationLeader;
    private Vector3 _formationOffset;
    private Vector3[] _route = Array.Empty<Vector3>();
    private int _routeIndex;
    private float _maximumSpeed = 16.0f;
    private float _acceleration = 16.0f;
    private float _radius = 1.15f;
    private double _ageSeconds;
    private double _stateAgeSeconds;
    private NpcShipNavigationState _state;
    private int _steeringSamples;
    private int _stateTransitions;
    private int _avoidanceActivations;
    private int _neighborSamples;
    private int _waypointAdvances;
    private float _minimumObstacleClearance = float.PositiveInfinity;
    private readonly SystemFrequencyGate _decisionGate =
        new(SystemFrequencyPolicy.NearbyAiHz);
    private Vector3 _cachedDesiredVelocity;
    private int _decisionSamples;

    public string ShipId { get; private set; } = string.Empty;

    public string ShipClassId { get; private set; } = string.Empty;

    public NpcShipNavigationRole Role { get; private set; }

    public NpcShipNavigationState NavigationState => _state;

    public void Configure(
        AerialSteeringRuntime steering,
        string shipId,
        string shipClassId,
        NpcShipNavigationRole role,
        Vector3 spawnPosition,
        float maximumSpeed,
        float acceleration,
        Color bodyColor,
        IReadOnlyList<Vector3> route)
    {
        ArgumentNullException.ThrowIfNull(steering);
        ArgumentException.ThrowIfNullOrWhiteSpace(shipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shipClassId);
        ArgumentNullException.ThrowIfNull(route);
        _steering = steering;
        ShipId = shipId;
        ShipClassId = shipClassId;
        Role = role;
        Name = shipId.Replace('.', '_');
        GlobalPosition = spawnPosition;
        _maximumSpeed = Math.Clamp(maximumSpeed, 8.0f, 30.0f);
        _acceleration = Math.Clamp(acceleration, 6.0f, 32.0f);
        _route = route.Count == 0 ? new[] { spawnPosition } : new List<Vector3>(route).ToArray();
        _routeIndex = 0;
        CollisionLayer = 1u;
        CollisionMask = 1u;
        AddToGroup("npc_ship_navigation");
        AddToGroup("aerial_steering_entity");

        _state = role switch
        {
            NpcShipNavigationRole.FormationWing => NpcShipNavigationState.Formation,
            NpcShipNavigationRole.HostileRaider => NpcShipNavigationState.Pursuit,
            _ => NpcShipNavigationState.Arrive
        };

        BuildVisual(bodyColor);
        _steering.UpsertEntity(
            ShipId,
            "npc_ship",
            GlobalPosition,
            Velocity,
            _radius);
    }

    public void SetPrimaryTarget(Node3D? target)
    {
        _primaryTarget = target;
    }

    public void PrimeAcceptanceCombatCycle()
    {
        if (Role != NpcShipNavigationRole.HostileRaider)
        {
            return;
        }
        ChangeState(NpcShipNavigationState.CombatApproach);
        _stateAgeSeconds = 0.0;
    }

    public void SetFormationLeader(
        NpcShipNavigationNode leader,
        Vector3 localOffset)
    {
        ArgumentNullException.ThrowIfNull(leader);
        _formationLeader = leader;
        _formationOffset = localOffset;
        if (Role == NpcShipNavigationRole.FormationWing)
        {
            ChangeState(NpcShipNavigationState.Formation);
        }
    }

    public override void _ExitTree()
    {
        _steering?.RemoveEntity(ShipId);
    }

    public override void _PhysicsProcess(double delta)
    {
        StepNavigation(delta, performMovement: true);
    }

    public void StepForAcceptance(double delta)
    {
        Vector3 originalVelocity = Velocity;
        StepNavigation(delta, performMovement: false);
        Velocity = originalVelocity;
        _steering?.UpsertEntity(
            ShipId,
            "npc_ship",
            GlobalPosition,
            originalVelocity,
            _radius);
    }

    private void StepNavigation(double delta, bool performMovement)
    {
        if (_steering is null || string.IsNullOrWhiteSpace(ShipId))
        {
            return;
        }
        _ageSeconds += delta;
        _stateAgeSeconds += delta;
        _steering.UpsertEntity(
            ShipId,
            "npc_ship",
            GlobalPosition,
            Velocity,
            _radius);

        if (_decisionGate.Consume(delta) || _decisionSamples == 0)
        {
            UpdateRoleState();
            Vector3 desiredVelocity = ComputeDesiredVelocity();
            Vector3 separation = _steering.ComputeEntitySeparation(
                ShipId,
                "npc_ship",
                GlobalPosition,
                8.0f,
                8.5f);
            IReadOnlyList<AerialEntitySample> neighbors = _steering.QueryNeighbors(
                GlobalPosition,
                12.0f,
                "npc_ship",
                ShipId);
            _neighborSamples += neighbors.Count;

            Vector3 obstacleAvoidance = _steering.ComputeObstacleAvoidance(
                GlobalPosition,
                desiredVelocity.LengthSquared() > 0.01f ? desiredVelocity : Velocity,
                _radius,
                1.6f,
                18.0f);
            if (obstacleAvoidance.LengthSquared() > 0.0001f)
            {
                _avoidanceActivations++;
            }

            desiredVelocity += separation + obstacleAvoidance;
            desiredVelocity = _steering.ApplyAltitudeEnvelope(
                desiredVelocity,
                GlobalPosition.Y,
                14.0f,
                PreferredAltitude(),
                62.0f,
                0.65f,
                10.0f);
            if (desiredVelocity.Length() > _maximumSpeed * 1.2f)
            {
                desiredVelocity = desiredVelocity.Normalized() * (_maximumSpeed * 1.2f);
            }
            _cachedDesiredVelocity = desiredVelocity;
            _decisionSamples++;
        }

        float weight = Math.Clamp((float)delta * _acceleration, 0.0f, 1.0f);
        Velocity = Velocity.Lerp(_cachedDesiredVelocity, weight);
        if (performMovement)
        {
            MoveAndSlide();
        }
        _steeringSamples++;
        _steering.RecordShipSample();
        UpdateMinimumObstacleClearance();
        if (performMovement)
        {
            OrientToVelocity();
        }
        _steering.UpsertEntity(
            ShipId,
            "npc_ship",
            GlobalPosition,
            Velocity,
            _radius);
    }

    public NpcShipNavigationDiagnostics CreateDiagnostics()
    {
        return new NpcShipNavigationDiagnostics(
            ShipId,
            Role,
            _state,
            _steeringSamples,
            _stateTransitions,
            _avoidanceActivations,
            _neighborSamples,
            _waypointAdvances,
            float.IsPositiveInfinity(_minimumObstacleClearance)
                ? 9999.0f
                : _minimumObstacleClearance,
            ResolveTarget() is not null,
            IsRuntimeActiveByResidency());
    }

    private bool IsRuntimeActiveByResidency()
    {
        if (!IsInsideTree())
        {
            return false;
        }

        Node? current = this;
        while (current is not null)
        {
            if (current.ProcessMode == Node.ProcessModeEnum.Disabled)
            {
                return false;
            }
            if (current is Node3D node3D && !node3D.Visible)
            {
                return false;
            }
            current = current.GetParent();
        }
        return true;
    }

    private void UpdateRoleState()
    {
        switch (Role)
        {
            case NpcShipNavigationRole.PatrolLeader:
            case NpcShipNavigationRole.TraderArrival:
                ChangeState(NpcShipNavigationState.Arrive);
                break;
            case NpcShipNavigationRole.FormationWing:
                ChangeState(NpcShipNavigationState.Formation);
                break;
            case NpcShipNavigationRole.HostileRaider:
                UpdateRaiderState();
                break;
        }
    }

    private void UpdateRaiderState()
    {
        Node3D? target = ResolveTarget();
        if (target is null)
        {
            ChangeState(NpcShipNavigationState.Arrive);
            return;
        }
        float distance = GlobalPosition.DistanceTo(target.GlobalPosition);
        switch (_state)
        {
            case NpcShipNavigationState.Pursuit:
                if (distance <= 18.0f)
                {
                    ChangeState(NpcShipNavigationState.CombatApproach);
                }
                break;
            case NpcShipNavigationState.CombatApproach:
                if (distance <= 7.0f || _stateAgeSeconds >= 2.0)
                {
                    ChangeState(NpcShipNavigationState.BreakAway);
                }
                break;
            case NpcShipNavigationState.BreakAway:
                if (_stateAgeSeconds >= 0.8)
                {
                    ChangeState(NpcShipNavigationState.Evade);
                }
                break;
            case NpcShipNavigationState.Evade:
                if (_stateAgeSeconds >= 1.2)
                {
                    ChangeState(NpcShipNavigationState.Pursuit);
                }
                break;
            default:
                ChangeState(NpcShipNavigationState.Pursuit);
                break;
        }
    }

    private Vector3 ComputeDesiredVelocity()
    {
        if (_steering is null)
        {
            return Vector3.Zero;
        }
        return _state switch
        {
            NpcShipNavigationState.Formation => ComputeFormationVelocity(),
            NpcShipNavigationState.Pursuit => ComputePursuitVelocity(1.0f),
            NpcShipNavigationState.CombatApproach => ComputePursuitVelocity(1.12f),
            NpcShipNavigationState.BreakAway => ComputeBreakAwayVelocity(),
            NpcShipNavigationState.Evade => ComputeEvadeVelocity(),
            _ => ComputeRouteArrivalVelocity()
        };
    }

    private Vector3 ComputeRouteArrivalVelocity()
    {
        if (_steering is null || _route.Length == 0)
        {
            return Vector3.Zero;
        }
        Vector3 target = _route[_routeIndex];
        float distance = GlobalPosition.DistanceTo(target);
        if (distance <= 2.0f && _route.Length > 1)
        {
            _routeIndex = (_routeIndex + 1) % _route.Length;
            target = _route[_routeIndex];
            _waypointAdvances++;
        }
        return _steering.Arrive(
            GlobalPosition,
            target,
            _maximumSpeed,
            12.0f,
            1.5f);
    }

    private Vector3 ComputeFormationVelocity()
    {
        if (_steering is null || _formationLeader is null)
        {
            return ComputeRouteArrivalVelocity();
        }
        Vector3 leaderVelocity = _formationLeader.Velocity;
        Vector3 forward = leaderVelocity.LengthSquared() > 0.1f
            ? leaderVelocity.Normalized()
            : Vector3.Forward;
        Vector3 right = forward.Cross(Vector3.Up);
        if (right.LengthSquared() <= 0.001f)
        {
            right = Vector3.Right;
        }
        else
        {
            right = right.Normalized();
        }
        Vector3 target = _formationLeader.GlobalPosition +
            right * _formationOffset.X +
            Vector3.Up * _formationOffset.Y +
            forward * _formationOffset.Z;
        return _steering.Formation(
            GlobalPosition,
            target,
            _maximumSpeed,
            9.0f,
            1.25f);
    }

    private Vector3 ComputePursuitVelocity(float speedFactor)
    {
        if (_steering is null)
        {
            return Vector3.Zero;
        }
        Node3D? target = ResolveTarget();
        if (target is null)
        {
            return ComputeRouteArrivalVelocity();
        }
        return _steering.Pursuit(
            GlobalPosition,
            target.GlobalPosition,
            ResolveVelocity(target),
            _maximumSpeed * speedFactor,
            1.5f);
    }

    private Vector3 ComputeBreakAwayVelocity()
    {
        if (_steering is null)
        {
            return Vector3.Zero;
        }
        Node3D? target = ResolveTarget();
        if (target is null)
        {
            return ComputeRouteArrivalVelocity();
        }
        Vector3 away = _steering.Evade(
            GlobalPosition,
            target.GlobalPosition,
            ResolveVelocity(target),
            _maximumSpeed * 1.12f,
            0.8f);
        Vector3 lateral = away.Cross(Vector3.Up);
        if (lateral.LengthSquared() > 0.01f)
        {
            away = (away + lateral.Normalized() * (_maximumSpeed * 0.45f)).Normalized() *
                (_maximumSpeed * 1.12f);
        }
        return away;
    }

    private Vector3 ComputeEvadeVelocity()
    {
        if (_steering is null)
        {
            return Vector3.Zero;
        }
        Node3D? target = ResolveTarget();
        if (target is null)
        {
            return ComputeRouteArrivalVelocity();
        }
        return _steering.Evade(
            GlobalPosition,
            target.GlobalPosition,
            ResolveVelocity(target),
            _maximumSpeed,
            1.6f);
    }

    private Node3D? ResolveTarget()
    {
        if (_primaryTarget is not null && GodotObject.IsInstanceValid(_primaryTarget))
        {
            return _primaryTarget;
        }
        if (_formationLeader is not null && GodotObject.IsInstanceValid(_formationLeader))
        {
            return _formationLeader;
        }
        return null;
    }

    private static Vector3 ResolveVelocity(Node3D target)
    {
        return target is CharacterBody3D body
            ? body.Velocity
            : Vector3.Zero;
    }

    private void ChangeState(NpcShipNavigationState next)
    {
        if (_state == next)
        {
            return;
        }
        bool combatTransition = Role == NpcShipNavigationRole.HostileRaider;
        _state = next;
        _stateAgeSeconds = 0.0;
        _stateTransitions++;
        if (combatTransition)
        {
            _steering?.RecordCombatStateTransition();
        }
    }

    private float PreferredAltitude()
    {
        return Role switch
        {
            NpcShipNavigationRole.TraderArrival => 35.0f,
            NpcShipNavigationRole.HostileRaider => 42.0f,
            _ => 38.0f
        };
    }

    private void UpdateMinimumObstacleClearance()
    {
        if (_steering is null)
        {
            return;
        }
        foreach (AerialObstacleSphere obstacle in _steering.Obstacles)
        {
            float clearance = GlobalPosition.DistanceTo(obstacle.Center) -
                obstacle.Radius - _radius;
            _minimumObstacleClearance = Math.Min(
                _minimumObstacleClearance,
                clearance);
        }
    }

    private void OrientToVelocity()
    {
        if (Velocity.LengthSquared() <= 0.25f)
        {
            return;
        }
        Vector3 direction = Velocity.Normalized();
        if (Math.Abs(direction.Dot(Vector3.Up)) > 0.96f)
        {
            return;
        }
        LookAt(GlobalPosition + direction, Vector3.Up);
    }

    private void BuildVisual(Color bodyColor)
    {
        StandardMaterial3D hullMaterial = new()
        {
            AlbedoColor = bodyColor,
            Metallic = 0.55f,
            Roughness = 0.32f
        };
        StandardMaterial3D engineMaterial = new()
        {
            AlbedoColor = new Color(0.08f, 0.55f, 1.0f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.05f, 0.45f, 1.0f, 1.0f),
            EmissionEnergyMultiplier = 3.0f,
            Roughness = 0.18f
        };

        MeshInstance3D body = new()
        {
            Name = "Hull",
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.4f, 0.55f, 3.0f),
                Material = hullMaterial
            }
        };
        AddChild(body);

        MeshInstance3D wings = new()
        {
            Name = "Wings",
            Position = new Vector3(0.0f, -0.02f, 0.15f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(3.8f, 0.18f, 1.15f),
                Material = hullMaterial
            }
        };
        AddChild(wings);

        MeshInstance3D nose = new()
        {
            Name = "Nose",
            Position = new Vector3(0.0f, 0.0f, -1.72f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.72f, 0.36f, 0.65f),
                Material = hullMaterial
            }
        };
        AddChild(nose);

        MeshInstance3D engine = new()
        {
            Name = "EngineGlow",
            Position = new Vector3(0.0f, 0.0f, 1.72f),
            Mesh = new SphereMesh
            {
                Radius = 0.24f,
                Height = 0.48f,
                RadialSegments = 10,
                Rings = 5,
                Material = engineMaterial
            }
        };
        AddChild(engine);

        AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new SphereShape3D { Radius = _radius }
        });
    }
}
