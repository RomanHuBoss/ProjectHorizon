using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private sealed record SurfaceRuntimeNodeState(
        bool Visible,
        Node.ProcessModeEnum ProcessMode,
        bool CollisionObject,
        uint CollisionLayer,
        uint CollisionMask);

    private const float PlanetRuntimeActivationRadiusMeters = 72.0f;
    private StarSystemSimulationRuntime? _starSystemSimulationRuntime;
    private StarSystemSimulationNode? _starSystemSimulationNode;
    private readonly Dictionary<Node3D, SurfaceRuntimeNodeState>
        _surfaceRuntimeStates = new();
    private bool _surfaceRuntimeActive = true;
    private int _surfaceActivationTransitions;
    private int _planetActivationPipelineMask;
    private StarSystemSimulationAcceptanceReport?
        _starSystemSimulationAcceptanceReport;
    private string _starSystemSimulationAcceptanceHud = "READY";

    private StarSystemSimulationRuntime StarSystemSimulation =>
        _starSystemSimulationRuntime ??
        throw new InvalidOperationException(
            "Star-system simulation runtime is unavailable.");

    private void BindStarSystemSimulationSceneNodes()
    {
        _starSystemSimulationNode = GetNodeOrNull<StarSystemSimulationNode>(
            "Gameplay/StarSystemSimulation");
        if (_starSystemSimulationNode is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing Gameplay/StarSystemSimulation.");
        }
    }

    private void InitializeStarSystemSimulationRuntime()
    {
        double initialEpoch = Math.Max(0, GalaxyNavigation.JumpCount) * 7200.0;
        _starSystemSimulationRuntime = new StarSystemSimulationRuntime(
            GalaxyNavigation.CurrentSystem,
            initialEpoch);
        _starSystemSimulationNode!.Configure(StarSystemSimulation);
        _planetActivationPipelineMask = 0;
        ApplySurfaceRuntimeActivation(
            ResolveSurfaceRuntimeActive(),
            force: true);
        UpdateStarSystemSimulation(0.0);
        StarSystemSimulationDiagnostics diagnostics =
            _starSystemSimulationNode.CreateDiagnostics();
        GD.Print(
            "TASK-128 star-system simulation READY: " +
            $"system={diagnostics.SystemId}; bodies={diagnostics.GeneratedBodies}; " +
            $"planets={diagnostics.PlanetBodies}; moons={diagnostics.MoonBodies}; " +
            $"stations={diagnostics.StationBodies}; traffic={diagnostics.ShipContacts}; " +
            $"surfaceActive={(_surfaceRuntimeActive ? 1 : 0)}; " +
            "model=analytic-orbits; representation=detail/proxy/marker/statistical; " +
            "singleDetailedPlanet=1.");
    }

    private void UpdateStarSystemSimulation(double delta)
    {
        if (_starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null ||
            _stageOneVoyageRuntime is null)
        {
            return;
        }

        if (_starSystemSimulationRuntime is null ||
            !string.Equals(
                _starSystemSimulationRuntime.SystemId,
                GalaxyNavigation.CurrentSystem.SystemId,
                StringComparison.Ordinal))
        {
            InitializeStarSystemSimulationRuntime();
            return;
        }

        bool shouldActivateSurface = ResolveSurfaceRuntimeActive();
        ApplySurfaceRuntimeActivation(shouldActivateSurface, force: false);
        bool renderSystemProxies = !shouldActivateSurface;
        _starSystemSimulationNode.UpdateSimulation(
            delta,
            GalaxyNavigation.CurrentPlanetId,
            shouldActivateSurface,
            renderSystemProxies);
    }

    private bool ResolveSurfaceRuntimeActive()
    {
        if (_stageOneVoyageRuntime is null)
        {
            return true;
        }

        if (StageOneVoyage.Location == StageOneVoyageLocation.PlanetSurface)
        {
            return true;
        }

        if (StageOneVoyage.Location == StageOneVoyageLocation.OrbitalStation ||
            _voyageShip is null)
        {
            return false;
        }

        Vector3 surface = new(
            (float)StageOneVoyageRuntime.SurfacePositionX,
            (float)StageOneVoyageRuntime.SurfacePositionY,
            (float)StageOneVoyageRuntime.SurfacePositionZ);
        return _voyageShip.GlobalPosition.DistanceTo(surface) <=
            PlanetRuntimeActivationRadiusMeters;
    }

    private void ApplySurfaceRuntimeActivation(bool active, bool force)
    {
        if (!force && _surfaceRuntimeActive == active)
        {
            return;
        }

        if (active)
        {
            RestoreSurfaceRuntimeNodes();
            _surfaceRuntimeActive = true;
            _planetActivationPipelineMask = BuildPlanetActivationPipelineMask();
        }
        else
        {
            SuspendSurfaceRuntimeNodes();
            _surfaceRuntimeActive = false;
            _planetActivationPipelineMask = 0;
        }

        _surfaceActivationTransitions++;
        if (_aerialSteeringRuntime is not null)
        {
            RefreshAerialNavigationEnvironment();
        }
        if (active && _npcNavigationSurface is not null)
        {
            RefreshNpcNavigationObstacles();
        }

        if (!force)
        {
            GD.Print(
                "TASK-128 PlanetRuntime activation transition: " +
                $"active={(active ? 1 : 0)}; " +
                $"system={GalaxyNavigation.CurrentSystem.SystemId}; " +
                $"planet={GalaxyNavigation.CurrentPlanetId}; " +
                $"location={StageOneVoyage.Location}; " +
                $"surfaceNodes={_surfaceRuntimeStates.Count}; " +
                $"transitions={_surfaceActivationTransitions}; " +
                $"pipeline=0x{_planetActivationPipelineMask:X2}.");
        }
    }

    private void SuspendSurfaceRuntimeNodes()
    {
        if (_surfaceRuntimeStates.Count > 0)
        {
            return;
        }

        foreach (Node3D node in EnumerateSurfaceRuntimeNodes())
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            bool collisionObject = node is CollisionObject3D;
            uint layer = collisionObject
                ? ((CollisionObject3D)node).CollisionLayer
                : 0u;
            uint mask = collisionObject
                ? ((CollisionObject3D)node).CollisionMask
                : 0u;
            _surfaceRuntimeStates[node] = new SurfaceRuntimeNodeState(
                node.Visible,
                node.ProcessMode,
                collisionObject,
                layer,
                mask);
            node.Visible = false;
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
            if (node is CollisionObject3D collision)
            {
                collision.CollisionLayer = 0u;
                collision.CollisionMask = 0u;
            }
        }
    }

    private void RestoreSurfaceRuntimeNodes()
    {
        foreach ((Node3D node, SurfaceRuntimeNodeState state) in
                 _surfaceRuntimeStates.ToArray())
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            node.Visible = state.Visible;
            node.ProcessMode = state.ProcessMode;
            if (state.CollisionObject && node is CollisionObject3D collision)
            {
                collision.CollisionLayer = state.CollisionLayer;
                collision.CollisionMask = state.CollisionMask;
            }
        }
        _surfaceRuntimeStates.Clear();
    }

    private IEnumerable<Node3D> EnumerateSurfaceRuntimeNodes()
    {
        HashSet<Node3D> nodes = new();
        AddSurfaceNode(nodes, GetNodeOrNull<Node3D>("GroundBody"));
        string[] authoredPaths =
        {
            "Gameplay/WaterPool",
            "Gameplay/Ecology",
            "Gameplay/LandingPad",
            "Gameplay/DamagedShip",
            "Gameplay/StationTrader",
            "Gameplay/BaseConstructionModules",
            "Gameplay/PlanetaryPois",
            "Gameplay/NpcNavigation",
            "Gameplay/NpcPopulation",
            "Gameplay/BaseBuildPreview"
        };
        foreach (string path in authoredPaths)
        {
            AddSurfaceNode(nodes, GetNodeOrNull<Node3D>(path));
        }

        foreach (Node node in GetTree().GetNodesInGroup("vertical_slice_resource"))
        {
            AddSurfaceNode(nodes, node as Node3D);
        }
        foreach (Node node in GetTree().GetNodesInGroup(
                     "vertical_slice_crafting_station"))
        {
            AddSurfaceNode(nodes, node as Node3D);
        }

        return nodes.OrderBy(node => node.GetPath().ToString(), StringComparer.Ordinal);
    }

    private static void AddSurfaceNode(HashSet<Node3D> nodes, Node3D? node)
    {
        if (node is not null)
        {
            nodes.Add(node);
        }
    }

    private int BuildPlanetActivationPipelineMask()
    {
        int mask = 0;
        if (_galaxyNavigationRuntime is not null) mask |= 1 << 0; // parameters
        if (_starSystemSimulationRuntime is not null) mask |= 1 << 1; // far LOD
        if (!string.IsNullOrWhiteSpace(GalaxyNavigation.CurrentPlanetId)) mask |= 1 << 2; // focus
        if (_surfaceRuntimeActive) mask |= 1 << 3; // surface runtime
        if (GetNodeOrNull<Node3D>("Gameplay/AtmospherePlanet") is not null) mask |= 1 << 4; // atmosphere
        if (GetNodeOrNull<CollisionObject3D>("GroundBody") is not null) mask |= 1 << 5; // collision
        if (_npcNavigationSurface is not null) mask |= 1 << 6; // navigation
        if (_planetaryPoisRoot is not null && _ecologyRoot is not null) mask |= 1 << 7; // region objects
        return mask;
    }

    private bool PlanetActivationPipelineComplete =>
        !_surfaceRuntimeActive || _planetActivationPipelineMask == 0xFF;

    private void BeginStarSystemSimulationAcceptance()
    {
        if (_starSystemSimulationNode is null ||
            _galaxyNavigationRuntime is null)
        {
            _starSystemSimulationAcceptanceHud = "FAIL unavailable";
            return;
        }

        _starSystemSimulationAcceptanceHud = "RUNNING";
        UpdateStarSystemSimulation(0.0);
        StarSystemSimulationAcceptanceReport report =
            StarSystemSimulationAcceptanceRunner.Run(
                GalaxyNavigation,
                _starSystemSimulationNode,
                _surfaceRuntimeActive,
                PlanetActivationPipelineComplete);
        _starSystemSimulationAcceptanceReport = report;
        _starSystemSimulationAcceptanceHud = report.Passed
            ? $"PASS bodies={report.Bodies}, planets={report.Planets}, " +
              $"moons={report.Moons}, lod=1, activation=1"
            : $"FAIL {report.Result}";
        string output =
            "TASK-128 star-system simulation acceptance " +
            (report.Passed ? "PASS" : "FAIL") + ": " +
            $"deterministic={(report.DeterministicGeneration ? 1 : 0)}; " +
            $"bodyCoverage={(report.BodyCoverage ? 1 : 0)}; " +
            $"moonBounds={(report.MoonBounds ? 1 : 0)}; " +
            $"analyticOrbits={(report.AnalyticOrbits ? 1 : 0)}; " +
            $"representationLevels={(report.RepresentationLevels ? 1 : 0)}; " +
            $"singleDetailedPlanet={(report.SingleDetailedPlanet ? 1 : 0)}; " +
            $"systemTransition={(report.SystemTransition ? 1 : 0)}; " +
            $"visualProjection={(report.VisualProjection ? 1 : 0)}; " +
            $"runtimeSamples={(report.RuntimeSamples ? 1 : 0)}; " +
            $"surfaceActivation={(report.SurfaceActivation ? 1 : 0)}; " +
            $"activationPipeline={(report.ActivationPipeline ? 1 : 0)}; " +
            $"bodies={report.Bodies}; planets={report.Planets}; moons={report.Moons}; " +
            $"stations={report.Stations}; shipContacts={report.ShipContacts}; " +
            $"visualNodes={report.VisualNodes}; rebuilds={report.Rebuilds}; " +
            $"transitions={_surfaceActivationTransitions}; " +
            $"pipeline=0x{_planetActivationPipelineMask:X2}; " +
            $"result={report.Result}";
        if (report.Passed)
        {
            GD.Print(output);
        }
        else
        {
            GD.PushError(output);
        }
    }

    private string BuildStarSystemSimulationHudLine()
    {
        if (_starSystemSimulationNode is null ||
            _starSystemSimulationRuntime is null)
        {
            return "Star system: unavailable";
        }

        StarSystemSimulationDiagnostics diagnostics =
            _starSystemSimulationNode.CreateDiagnostics();
        return "Star system: " +
            $"{diagnostics.SystemId} • bodies={diagnostics.GeneratedBodies} " +
            $"P/M/S/T={diagnostics.PlanetBodies}/{diagnostics.MoonBodies}/" +
            $"{diagnostics.StationBodies}/{diagnostics.ShipContacts} • " +
            $"LOD={diagnostics.Proxies}/{diagnostics.Markers}/{diagnostics.Statistical} • " +
            $"PlanetRuntime={(_surfaceRuntimeActive ? "ACTIVE" : "PROXY")} • " +
            $"pipeline=0x{_planetActivationPipelineMask:X2} • " +
            $"epoch={StarSystemSimulation.SimulationSeconds.ToString("0", CultureInfo.InvariantCulture)}s";
    }
}
