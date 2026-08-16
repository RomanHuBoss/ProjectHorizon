using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private AerialSteeringRuntime? _aerialSteeringRuntime;
    private Node3D? _npcShipTrafficRoot;
    private readonly List<NpcShipNavigationNode> _npcShipNavigationNodes = new();
    private AerialNavigationAcceptanceReport? _aerialNavigationAcceptanceReport;
    private string _aerialNavigationAcceptanceHud = "READY";
    private bool _aerialNavigationAcceptanceRunning;
    private double _aerialNavigationAcceptanceElapsed;
    private AerialSteeringSnapshot? _aerialNavigationAcceptanceBaseline;
    private bool _aerialGridProbe;
    private bool _aerialObstacleProbe;
    private bool _aerialAltitudeProbe;
    private bool _aerialPoiProbe;
    private bool _aerialAcceptanceForceLeaderTarget;
    private int _aerialAcceptanceFaunaProbeSamples;

    private AerialSteeringRuntime AerialSteering =>
        _aerialSteeringRuntime ??
        throw new InvalidOperationException("Aerial steering runtime is unavailable.");

    private void BindAerialNavigationSceneNodes()
    {
        _npcShipTrafficRoot = GetNodeOrNull<Node3D>("Gameplay/NpcShipTraffic");
        if (_npcShipTrafficRoot is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing Gameplay/NpcShipTraffic.");
        }
    }

    private void InitializeAerialSteeringRuntime()
    {
        _aerialSteeringRuntime = new AerialSteeringRuntime();
        RefreshAerialNavigationEnvironment();
        GD.Print(
            "TASK-126 aerial steering READY: " +
            $"gridCell={AerialSteering.CellSize:0.#}m; " +
            $"obstacles={AerialSteering.Obstacles.Count}; " +
            $"poi={AerialSteering.PointsOfInterest.Count}; " +
            "fauna=steering+spherical-avoidance+altitude+POI+local-grid; " +
            "ships=steering+pursuit+evade+arrive+formation+avoidance+combat-states.");
    }

    private void RebuildNpcShipTraffic()
    {
        if (_npcShipTrafficRoot is null || _aerialSteeringRuntime is null ||
            _shipSystemsCatalog is null)
        {
            return;
        }
        AerialSteering.RemoveGroup("npc_ship");
        foreach (Node child in _npcShipTrafficRoot.GetChildren())
        {
            _npcShipTrafficRoot.RemoveChild(child);
            child.QueueFree();
        }
        _npcShipNavigationNodes.Clear();

        Vector3 station = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation")?.GlobalPosition ??
            (GetNodeOrNull<Node3D>("Gameplay")?.ToGlobal(new Vector3(0.0f, 35.0f, -145.0f)) ??
                new Vector3(0.0f, 35.0f, -145.0f));
        Vector3 Offset(Vector3 local) => SurfaceLocalDirectionToWorld(local);
        Vector3[] patrolRoute =
        {
            station + Offset(new Vector3(-38.0f, 3.0f, 0.0f)),
            station + Offset(new Vector3(0.0f, 7.0f, -38.0f)),
            station + Offset(new Vector3(38.0f, 3.0f, 0.0f)),
            station + Offset(new Vector3(0.0f, 1.0f, 38.0f))
        };
        Vector3[] traderRoute =
        {
            station + Offset(new Vector3(48.0f, 0.0f, 18.0f)),
            station + Offset(new Vector3(25.0f, 0.0f, 18.0f)),
            station + Offset(new Vector3(0.0f, 0.0f, 27.0f)),
            station + Offset(new Vector3(-30.0f, 4.0f, 30.0f))
        };

        NpcShipNavigationNode leader = CreateNpcShip(
            "npc.ship.aegis_leader",
            "ship.class.fighter",
            NpcShipNavigationRole.PatrolLeader,
            station + Offset(new Vector3(-30.0f, 3.0f, 12.0f)),
            new Color(0.18f, 0.46f, 0.78f, 1.0f),
            patrolRoute);
        NpcShipNavigationNode wing = CreateNpcShip(
            "npc.ship.aegis_wing",
            "ship.class.explorer",
            NpcShipNavigationRole.FormationWing,
            station + Offset(new Vector3(-24.0f, 5.0f, 14.0f)),
            new Color(0.22f, 0.58f, 0.86f, 1.0f),
            patrolRoute);
        wing.SetFormationLeader(leader, new Vector3(5.5f, 1.7f, -6.0f));

        CreateNpcShip(
            "npc.ship.frontier_trader",
            "ship.class.cargo",
            NpcShipNavigationRole.TraderArrival,
            traderRoute[0],
            new Color(0.72f, 0.52f, 0.16f, 1.0f),
            traderRoute);
        NpcShipNavigationNode raider = CreateNpcShip(
            "npc.ship.raider_interceptor",
            "ship.class.exotic",
            NpcShipNavigationRole.HostileRaider,
            leader.GlobalPosition + Offset(new Vector3(8.5f, 1.8f, 0.0f)),
            new Color(0.72f, 0.16f, 0.18f, 1.0f),
            patrolRoute);
        raider.SetPrimaryTarget(leader);

        UpdateNpcShipTrafficTargets();
        GD.Print(
            "TASK-126 NPC ship traffic READY: " +
            $"ships={_npcShipNavigationNodes.Count}; roles=4; " +
            "arrive=1; formation=1; pursuit=1; evade=1; avoidance=spherical+grid; " +
            "combat=approach+break-away+evade+reacquire.");
    }

    private NpcShipNavigationNode CreateNpcShip(
        string shipId,
        string shipClassId,
        NpcShipNavigationRole role,
        Vector3 spawn,
        Color color,
        IReadOnlyList<Vector3> route)
    {
        ShipClassDefinition shipClass = ShipSystemsCatalog.GetClass(shipClassId);
        float maximumSpeed = (float)Math.Clamp(
            shipClass.BaseStats.MaxSpeed * 0.20,
            12.0,
            24.0);
        float acceleration = (float)Math.Clamp(
            shipClass.BaseStats.Acceleration * 0.70,
            8.0,
            24.0);
        NpcShipNavigationNode ship = new();
        _npcShipTrafficRoot!.AddChild(ship);
        ship.Configure(
            AerialSteering,
            shipId,
            shipClassId,
            role,
            spawn,
            maximumSpeed,
            acceleration,
            color,
            route);
        _npcShipNavigationNodes.Add(ship);
        return ship;
    }

    private void UpdateAerialNavigation(double delta)
    {
        if (_aerialSteeringRuntime is null)
        {
            return;
        }
        UpdateNpcShipTrafficTargets();
        UpdateAerialNavigationAcceptance(delta);
    }

    private void UpdateNpcShipTrafficTargets()
    {
        if (_npcShipNavigationNodes.Count == 0)
        {
            return;
        }
        NpcShipNavigationNode? leader = _npcShipNavigationNodes.FirstOrDefault(
            node => node.Role == NpcShipNavigationRole.PatrolLeader);
        NpcShipNavigationNode? raider = _npcShipNavigationNodes.FirstOrDefault(
            node => node.Role == NpcShipNavigationRole.HostileRaider);
        if (raider is null)
        {
            return;
        }
        if (!_aerialAcceptanceForceLeaderTarget &&
            _stageOneVoyageRuntime?.Piloted == true &&
            _voyageShip is not null)
        {
            raider.SetPrimaryTarget(_voyageShip);
        }
        else
        {
            raider.SetPrimaryTarget(leader);
        }
    }

    private void ExerciseAerialNavigationDuringWorldAcceptance(
        WorldSceneContext context)
    {
        if (!_aerialNavigationAcceptanceRunning ||
            context.Kind != WorldSceneKind.Orbit ||
            _npcShipNavigationNodes.Count == 0)
        {
            return;
        }

        // TASK-148 deliberately suspends orbit traffic while the player is on the
        // surface. Exercise the actual ship steering only while its Orbit shell is
        // live, then let TASK-148 restore the original residency state. Two orbit
        // legs x 150 fixed steps provide five seconds of deterministic decision
        // time without moving CharacterBody3D instances outside a physics frame.
        const double step = 1.0 / SystemFrequencyPolicy.PhysicsHz;
        const int stepsPerOrbitLeg = 150;
        for (int frame = 0; frame < stepsPerOrbitLeg; frame++)
        {
            foreach (NpcShipNavigationNode ship in _npcShipNavigationNodes)
            {
                ship.StepForAcceptance(step);
            }
        }
    }

    private void RefreshAerialNavigationEnvironment()
    {
        if (_aerialSteeringRuntime is null)
        {
            return;
        }
        AerialSteering.ReplaceEnvironment(
            CaptureAerialObstacleSpheres(),
            BuildAerialPointsOfInterest());
    }

    private IReadOnlyList<AerialObstacleSphere> CaptureAerialObstacleSpheres()
    {
        List<AerialObstacleSphere> obstacles = new();
        foreach (CollisionShape3D shapeNode in EnumerateAerialCollisionShapes(this))
        {
            if (shapeNode.Disabled ||
                shapeNode.GetParent() is not StaticBody3D body ||
                body.CollisionLayer == 0u ||
                string.Equals(body.Name, "GroundBody", StringComparison.Ordinal))
            {
                continue;
            }
            float localRadius = shapeNode.Shape switch
            {
                BoxShape3D box => (box.Size * 0.5f).Length(),
                CylinderShape3D cylinder => MathF.Sqrt(
                    (cylinder.Radius * cylinder.Radius) +
                    (cylinder.Height * 0.5f * cylinder.Height * 0.5f)),
                CapsuleShape3D capsule => Math.Max(
                    capsule.Radius,
                    capsule.Height * 0.5f),
                SphereShape3D sphere => sphere.Radius,
                _ => 0.0f
            };
            Basis globalBasis = shapeNode.GlobalTransform.Basis;
            float maximumScale = Math.Max(
                globalBasis.X.Length(),
                Math.Max(globalBasis.Y.Length(), globalBasis.Z.Length()));
            float radius = localRadius * maximumScale;
            if (radius <= 0.05f)
            {
                continue;
            }
            obstacles.Add(new AerialObstacleSphere(
                shapeNode.GetPath().ToString(),
                shapeNode.GlobalPosition,
                radius));
        }
        return obstacles
            .GroupBy(item => item.ObstacleId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Radius).First())
            .OrderBy(item => item.ObstacleId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<AerialPointOfInterest> BuildAerialPointsOfInterest()
    {
        List<AerialPointOfInterest> points = new();
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        Node3D? water = GetNodeOrNull<Node3D>("Gameplay/WaterPool");
        Node3D? landingPad = GetNodeOrNull<Node3D>("Gameplay/LandingPad");
        Node3D? station = GetNodeOrNull<Node3D>("Gameplay/OrbitalStation");
        if (water is not null)
        {
            points.Add(new AerialPointOfInterest(
                "poi.fauna.water",
                "fauna",
                water.GlobalPosition + SurfaceLocalDirectionToWorld(Vector3.Up * 4.2f),
                2.5f));
        }
        if (landingPad is not null)
        {
            points.Add(new AerialPointOfInterest(
                "poi.fauna.landing_pad",
                "fauna",
                landingPad.GlobalPosition + SurfaceLocalDirectionToWorld(Vector3.Up * 5.0f),
                2.0f));
        }
        Vector3 ridgeWest = gameplay?.ToGlobal(
            new Vector3(-20.0f, 6.5f, -4.0f)) ??
            new Vector3(-20.0f, 6.5f, -4.0f);
        Vector3 ridgeEast = gameplay?.ToGlobal(
            new Vector3(22.0f, 7.0f, 16.0f)) ??
            new Vector3(22.0f, 7.0f, 16.0f);
        points.Add(new AerialPointOfInterest(
            "poi.fauna.ridge_west",
            "fauna",
            ridgeWest,
            2.0f));
        points.Add(new AerialPointOfInterest(
            "poi.fauna.ridge_east",
            "fauna",
            ridgeEast,
            2.0f));

        Vector3 stationPosition = station?.GlobalPosition ??
            new Vector3(0.0f, 35.0f, -145.0f);
        points.Add(new AerialPointOfInterest(
            "poi.ship.dock_approach",
            "ship",
            stationPosition + SurfaceLocalDirectionToWorld(new Vector3(0.0f, 0.0f, 27.0f)),
            3.0f));
        points.Add(new AerialPointOfInterest(
            "poi.ship.west_lane",
            "ship",
            stationPosition + SurfaceLocalDirectionToWorld(new Vector3(-38.0f, 3.0f, 0.0f)),
            4.0f));
        points.Add(new AerialPointOfInterest(
            "poi.ship.east_lane",
            "ship",
            stationPosition + SurfaceLocalDirectionToWorld(new Vector3(38.0f, 3.0f, 0.0f)),
            4.0f));
        points.Add(new AerialPointOfInterest(
            "poi.ship.outer_lane",
            "ship",
            stationPosition + SurfaceLocalDirectionToWorld(new Vector3(0.0f, 7.0f, -42.0f)),
            4.0f));
        return points;
    }

    private static IEnumerable<CollisionShape3D> EnumerateAerialCollisionShapes(
        Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                yield return shape;
            }
            foreach (CollisionShape3D nested in EnumerateAerialCollisionShapes(child))
            {
                yield return nested;
            }
        }
    }

    private void BeginAerialNavigationAcceptance()
    {
        _aerialNavigationAcceptanceReport = null;
        _aerialNavigationAcceptanceHud = "RUNNING";
        _aerialNavigationAcceptanceElapsed = 0.0;
        _aerialGridProbe = false;
        _aerialObstacleProbe = false;
        _aerialAltitudeProbe = false;
        _aerialPoiProbe = false;
        _aerialAcceptanceFaunaProbeSamples = 0;
        if (_aerialSteeringRuntime is null || _npcShipNavigationNodes.Count != 4)
        {
            CompleteAerialNavigationAcceptanceFailure(
                "aerial steering runtime or NPC ship traffic is unavailable");
            return;
        }
        _aerialNavigationAcceptanceBaseline = AerialSteering.CreateSnapshot();
        _aerialAcceptanceForceLeaderTarget = true;
        UpdateNpcShipTrafficTargets();
        NpcShipNavigationNode? acceptanceRaider =
            _npcShipNavigationNodes.FirstOrDefault(node =>
                node.Role == NpcShipNavigationRole.HostileRaider);
        acceptanceRaider?.PrimeAcceptanceCombatCycle();
        RunAerialNavigationStaticProbes();
        _aerialAcceptanceFaunaProbeSamples =
            ExerciseFlyingFaunaForAerialAcceptance();
        _aerialNavigationAcceptanceRunning = true;
    }

    private int ExerciseFlyingFaunaForAerialAcceptance()
    {
        int samples = 0;
        foreach (EcologyFaunaNode fauna in _ecologyFaunaNodes
            .Where(node => string.Equals(
                node.MovementMode,
                "Flying",
                StringComparison.Ordinal)))
        {
            if (fauna.StepAerialForAcceptance())
            {
                samples++;
            }
        }
        return samples;
    }

    private void RunAerialNavigationStaticProbes()
    {
        AerialSteering.UpsertEntity(
            "acceptance.aerial.a",
            "acceptance",
            new Vector3(1.0f, 5.0f, 1.0f),
            Vector3.Right,
            0.5f);
        AerialSteering.UpsertEntity(
            "acceptance.aerial.b",
            "acceptance",
            new Vector3(2.2f, 5.2f, 1.0f),
            Vector3.Left,
            0.5f);
        _aerialGridProbe = AerialSteering.QueryNeighbors(
            new Vector3(1.0f, 5.0f, 1.0f),
            3.0f,
            "acceptance",
            "acceptance.aerial.a")
            .Any(item => string.Equals(
                item.EntityId,
                "acceptance.aerial.b",
                StringComparison.Ordinal));
        AerialSteering.RemoveEntity("acceptance.aerial.a");
        AerialSteering.RemoveEntity("acceptance.aerial.b");

        AerialObstacleSphere? obstacle = AerialSteering.Obstacles.FirstOrDefault();
        if (obstacle is not null)
        {
            Vector3 position = obstacle.Center +
                Vector3.Right * (obstacle.Radius + 0.40f);
            Vector3 correction = AerialSteering.ComputeObstacleAvoidance(
                position,
                Vector3.Left * 0.20f,
                0.50f,
                0.5f,
                4.0f);
            _aerialObstacleProbe = correction.LengthSquared() > 0.0001f;
        }

        // TASK-174.2: exercise the altitude controller independently of fauna
        // lifecycle. Dead fauna legitimately keeps its frozen transform and is
        // excluded from the live altitude envelope, but the controller itself
        // must still be proven on every TASK-126 run.
        Vector3 altitudeCorrection = AerialSteering.ApplyAltitudeEnvelope(
            Vector3.Zero,
            0.0f,
            1.6f,
            3.4f,
            7.2f,
            1.65f,
            3.0f);
        _aerialAltitudeProbe = altitudeCorrection.Y > 0.01f;

        AerialPointOfInterest? poi = AerialSteering.PointsOfInterest.FirstOrDefault();
        _aerialPoiProbe = poi is not null &&
            AerialSteering.FindClosestPointOfInterest(
                poi.Position,
                poi.Group,
                1.0f) is not null;
    }

    private void UpdateAerialNavigationAcceptance(double delta)
    {
        if (!_aerialNavigationAcceptanceRunning ||
            _aerialNavigationAcceptanceBaseline is null)
        {
            return;
        }
        _aerialNavigationAcceptanceElapsed += delta;
        if (_aerialNavigationAcceptanceElapsed < 4.5)
        {
            return;
        }
        AerialSteeringSnapshot after = AerialSteering.CreateSnapshot();
        _aerialNavigationAcceptanceReport =
            AerialNavigationAcceptanceEvaluator.Evaluate(
                _aerialNavigationAcceptanceBaseline,
                after,
                _ecologyFaunaNodes,
                _npcShipNavigationNodes,
                _aerialGridProbe,
                _aerialObstacleProbe,
                _aerialAltitudeProbe,
                _aerialPoiProbe,
                shipTrafficExpectedActive:
                    _worldSceneCoordinatorRuntime?.Current.Kind == WorldSceneKind.Orbit);
        _aerialNavigationAcceptanceRunning = false;
        _aerialAcceptanceForceLeaderTarget = false;
        UpdateNpcShipTrafficTargets();
        AerialNavigationAcceptanceReport report = _aerialNavigationAcceptanceReport;
        _aerialNavigationAcceptanceHud = report.Passed
            ? $"PASS fauna={report.FlyingFauna}, ships={report.NpcShips}, grid={report.OccupiedGridCells}, avoid=1, modes=5/5"
            : $"FAIL {report.Result}";
        string output = BuildAerialNavigationAcceptanceOutput(report, after);
        if (report.Passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private void CompleteAerialNavigationAcceptanceFailure(string result)
    {
        _aerialNavigationAcceptanceRunning = false;
        _aerialAcceptanceForceLeaderTarget = false;
        _aerialNavigationAcceptanceReport = new AerialNavigationAcceptanceReport(
            _ecologyFaunaNodes.Count(node => string.Equals(
                node.MovementMode,
                "Flying",
                StringComparison.Ordinal)),
            _npcShipNavigationNodes.Count,
            0,
            _aerialSteeringRuntime?.Obstacles.Count ?? 0,
            _aerialSteeringRuntime?.PointsOfInterest.Count ?? 0,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            result);
        _aerialNavigationAcceptanceHud = $"FAIL: {result}";
        GD.PushError(BuildAerialNavigationAcceptanceOutput(
            _aerialNavigationAcceptanceReport,
            _aerialSteeringRuntime?.CreateSnapshot()));
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private string BuildAerialNavigationAcceptanceOutput(
        AerialNavigationAcceptanceReport report,
        AerialSteeringSnapshot? snapshot)
    {
        return "TASK-126 aerial navigation acceptance " +
            (report.Passed ? "PASS" : "FAIL") + ": " +
            $"flyingFauna={report.FlyingFauna}; npcShips={report.NpcShips}; " +
            $"gridCells={report.OccupiedGridCells}; obstacles={report.Obstacles}; poi={report.PointsOfInterest}; " +
            $"faunaCoverage={(report.FlyingFaunaCoverage ? 1 : 0)}; activeFlying={_ecologyFaunaNodes.Count(node => node.IsActiveFlyingNavigationParticipant)}; " +
            $"sharedRuntime={(report.SharedSteeringRuntime ? 1 : 0)}; " +
            $"localGrid={(report.LocalSpatialGrid ? 1 : 0)}; sphericalAvoidance={(report.SphericalObstacleAvoidance ? 1 : 0)}; " +
            $"altitude={(report.AltitudeEnvelope ? 1 : 0)}; altitudeProbe={(_aerialAltitudeProbe ? 1 : 0)}; poiSteering={(report.PointOfInterestSteering ? 1 : 0)}; " +
            $"shipSteering={(report.ShipSteering ? 1 : 0)}; pursuit={(report.Pursuit ? 1 : 0)}; " +
            $"evade={(report.Evade ? 1 : 0)}; arrive={(report.Arrive ? 1 : 0)}; formation={(report.Formation ? 1 : 0)}; " +
            $"combatStates={(report.CombatStates ? 1 : 0)}; clearance={(report.ShipObstacleClearance ? 1 : 0)}; " +
            $"runtimeSamples={(report.RuntimeSamples ? 1 : 0)}; " +
            $"faunaProbeSamples={_aerialAcceptanceFaunaProbeSamples}; " +
            $"queries={snapshot?.GridQueries ?? 0}; faunaSamples={snapshot?.FlyingFaunaSamples ?? 0}; shipSamples={snapshot?.ShipSamples ?? 0}; " +
            $"avoidance={snapshot?.ObstacleAvoidanceActivations ?? 0}; transitions={snapshot?.CombatStateTransitions ?? 0}; " +
            $"result={report.Result}";
    }

    private string BuildAerialNavigationHudLine()
    {
        if (_aerialSteeringRuntime is null)
        {
            return L("ui.hud.aerial.unavailable");
        }
        AerialSteeringSnapshot snapshot = AerialSteering.CreateSnapshot();
        int flying = _ecologyFaunaNodes.Count(node => string.Equals(
            node.MovementMode,
            "Flying",
            StringComparison.Ordinal));
        string states = string.Join(",",
            _npcShipNavigationNodes
                .OrderBy(node => node.ShipId, StringComparer.Ordinal)
                .Select(node => node.NavigationState.ToString()));
        return LF(
            "ui.hud.aerial.summary",
            ("fauna", flying),
            ("ships", _npcShipNavigationNodes.Count),
            ("cells", snapshot.OccupiedCells),
            ("obstacles", snapshot.ObstacleCount),
            ("poi", snapshot.PointOfInterestCount),
            ("avoidance", snapshot.ObstacleAvoidanceActivations),
            ("states", states));
    }
}
