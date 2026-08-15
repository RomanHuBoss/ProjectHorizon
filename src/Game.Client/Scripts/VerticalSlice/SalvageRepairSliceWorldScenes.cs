using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private WorldSceneCoordinatorRuntime? _worldSceneCoordinatorRuntime;
    private WorldSceneCoordinatorNode? _worldSceneCoordinatorNode;
    private readonly Dictionary<Node3D, SurfaceRuntimeNodeState>
        _orbitRuntimeStates = new();
    private bool _orbitRuntimeActive = true;
    private int _worldResidencyTransitions;
    private WorldSceneCoordinatorAcceptanceReport?
        _worldSceneCoordinatorAcceptanceReport;
    private string _worldSceneCoordinatorAcceptanceHud = "READY";

    private WorldSceneCoordinatorRuntime WorldScenes =>
        _worldSceneCoordinatorRuntime ??
        throw new InvalidOperationException(
            "World scene coordinator runtime is unavailable.");

    private void BindWorldSceneCoordinatorSceneNodes()
    {
        Node3D gameplay = GetNodeOrNull<Node3D>("Gameplay") ??
            throw new InvalidOperationException(
                "Vertical slice scene is missing Gameplay host.");

        _worldSceneCoordinatorNode =
            gameplay.GetNodeOrNull<WorldSceneCoordinatorNode>(
                "WorldSceneCoordinator");
        if (_worldSceneCoordinatorNode is null)
        {
            // TASK-148.1: the coordinator is an orchestration object, not authored
            // scene content. Creating it after SalvageRepairSlice has loaded keeps
            // a newly added C# script from becoming a hard ext_resource dependency
            // that can make the whole gameplay PackedScene return CantOpen while
            // Godot is refreshing an overlaid project's C# resource/UID cache.
            _worldSceneCoordinatorNode = new WorldSceneCoordinatorNode
            {
                Name = "WorldSceneCoordinator"
            };
            gameplay.AddChild(_worldSceneCoordinatorNode);
            GD.Print(
                "TASK-148.1 world scene coordinator bootstrap PASS: " +
                "mode=runtime-node; gameplaySceneHardDependency=0.");
        }
    }

    private void InitializeWorldSceneCoordinator()
    {
        if (_worldSceneCoordinatorNode is null ||
            _stageOneVoyageRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }

        WorldSceneContext initial = ResolveWorldSceneContext();
        _worldSceneCoordinatorRuntime = new WorldSceneCoordinatorRuntime(initial);
        _worldSceneCoordinatorNode.Configure(WorldScenes);
        ApplyWorldResidencyPolicy(force: true);
        WorldSceneCoordinatorDiagnostics diagnostics =
            _worldSceneCoordinatorNode.CreateDiagnostics();
        GD.Print(
            "TASK-148 world scene coordinator READY: " +
            $"kind={diagnostics.Kind}; system={diagnostics.SystemId}; " +
            $"planet={diagnostics.PlanetId}; shell={diagnostics.ScenePath}; " +
            $"hostChildren={diagnostics.HostChildren}; surfaceActive={(_surfaceRuntimeActive ? 1 : 0)}; " +
            $"orbitActive={(_orbitRuntimeActive ? 1 : 0)}; " +
            "contexts=Surface/Orbit/StationInterior/HyperspaceTransit; " +
            "persistence=derived-from-voyage+galaxy; wholeGalaxyResident=0; F5=acceptance.");
    }

    private WorldSceneContext ResolveWorldSceneContext()
    {
        WorldSceneKind kind = StageOneVoyage.Location switch
        {
            StageOneVoyageLocation.PlanetSurface => WorldSceneKind.Surface,
            StageOneVoyageLocation.OutboundFlight => WorldSceneKind.Orbit,
            StageOneVoyageLocation.OrbitalStation => WorldSceneKind.StationInterior,
            StageOneVoyageLocation.InboundFlight => WorldSceneKind.Orbit,
            _ => throw new ArgumentOutOfRangeException(
                nameof(StageOneVoyage.Location),
                StageOneVoyage.Location,
                "Unknown voyage location.")
        };

        return WorldSceneContext.Create(
            kind,
            GalaxyNavigation.CurrentSystem.SystemId,
            GalaxyNavigation.CurrentPlanetId);
    }

    private void SynchronizeWorldSceneCoordinator(bool force = false)
    {
        if (_worldSceneCoordinatorRuntime is null ||
            _worldSceneCoordinatorNode is null ||
            _stageOneVoyageRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }

        WorldSceneContext desired = ResolveWorldSceneContext();
        if (WorldScenes.Current != desired)
        {
            WorldSceneTransitionResult transition =
                _worldSceneCoordinatorNode.TryTransition(
                    desired,
                    out string result);
            if (transition == WorldSceneTransitionResult.Rejected)
            {
                GD.PushError(
                    "TASK-148 world scene coordinator transition FAIL: " +
                    $"from={WorldScenes.Current.Kind}; to={desired.Kind}; " +
                    $"system={desired.SystemId}; planet={desired.PlanetId}; result={result}");
                return;
            }

            if (transition == WorldSceneTransitionResult.Applied)
            {
                _worldResidencyTransitions++;
                GD.Print(
                    "TASK-148 world scene transition PASS: " +
                    $"kind={desired.Kind}; system={desired.SystemId}; planet={desired.PlanetId}; " +
                    $"generation={WorldScenes.Generation}; transitions={WorldScenes.TransitionCount}.");
            }
        }
        else if (force)
        {
            _worldSceneCoordinatorNode.ReloadCurrentShell(force: true);
        }

        ApplyWorldResidencyPolicy(force);
    }

    private void ApplyWorldResidencyPolicy(bool force)
    {
        if (_worldSceneCoordinatorRuntime is null)
        {
            return;
        }

        WorldSceneKind kind = WorldScenes.Current.Kind;
        bool surfaceActive = kind switch
        {
            WorldSceneKind.Surface => true,
            WorldSceneKind.Orbit => ResolveSurfaceRuntimeActive(),
            WorldSceneKind.StationInterior => false,
            WorldSceneKind.HyperspaceTransit => false,
            _ => false
        };
        bool orbitActive = kind == WorldSceneKind.Orbit;

        ApplySurfaceRuntimeActivation(surfaceActive, force);
        ApplyOrbitRuntimeActivation(orbitActive, force);
    }

    private void ApplyOrbitRuntimeActivation(bool active, bool force)
    {
        if (!force && _orbitRuntimeActive == active)
        {
            if (!active)
            {
                EnforceOrbitRuntimeSuspended();
            }
            return;
        }

        if (active)
        {
            RestoreOrbitRuntimeNodes();
        }
        else
        {
            SuspendOrbitRuntimeNodes();
        }

        _orbitRuntimeActive = active;
    }

    private void SuspendOrbitRuntimeNodes()
    {
        if (_orbitRuntimeStates.Count == 0)
        {
            foreach (Node3D node in EnumerateOrbitRuntimeNodes())
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
                _orbitRuntimeStates[node] = new SurfaceRuntimeNodeState(
                    node.Visible,
                    node.ProcessMode,
                    collisionObject,
                    layer,
                    mask);
            }
        }

        EnforceOrbitRuntimeSuspended();
    }

    private void EnforceOrbitRuntimeSuspended()
    {
        foreach (Node3D node in _orbitRuntimeStates.Keys.ToArray())
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            node.Visible = false;
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
            if (node is CollisionObject3D collision)
            {
                collision.CollisionLayer = 0u;
                collision.CollisionMask = 0u;
            }
        }
    }

    private void RestoreOrbitRuntimeNodes()
    {
        foreach ((Node3D node, SurfaceRuntimeNodeState state) in
                 _orbitRuntimeStates.ToArray())
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
        _orbitRuntimeStates.Clear();
    }

    private IEnumerable<Node3D> EnumerateOrbitRuntimeNodes()
    {
        Node3D?[] nodes =
        {
            GetNodeOrNull<Node3D>("Gameplay/OrbitalStation"),
            GetNodeOrNull<Node3D>("Gameplay/OrbitalDockMarker"),
            GetNodeOrNull<Node3D>("Gameplay/PlanetApproachMarker"),
            GetNodeOrNull<Node3D>("Gameplay/NpcShipTraffic")
        };
        return nodes
            .Where(node => node is not null)
            .Cast<Node3D>()
            .OrderBy(node => node.GetPath().ToString(), StringComparer.Ordinal);
    }

    private bool BeginWorldHyperspaceTransit()
    {
        if (_worldSceneCoordinatorRuntime is null ||
            _worldSceneCoordinatorNode is null)
        {
            return false;
        }

        SynchronizeWorldSceneCoordinator();
        WorldSceneContext current = WorldScenes.Current;
        if (current.Kind != WorldSceneKind.StationInterior)
        {
            return false;
        }

        WorldSceneTransitionResult transition =
            _worldSceneCoordinatorNode.TryTransition(
                WorldSceneContext.Create(
                    WorldSceneKind.HyperspaceTransit,
                    current.SystemId,
                    current.PlanetId),
                out _);
        if (transition != WorldSceneTransitionResult.Applied)
        {
            return false;
        }

        _worldResidencyTransitions++;
        ApplyWorldResidencyPolicy(force: true);
        GD.Print(
            "TASK-148 hyperspace scene transition PASS: " +
            $"phase=begin; system={current.SystemId}; planet={current.PlanetId}; " +
            $"generation={WorldScenes.Generation}.");
        return true;
    }

    private void CompleteWorldHyperspaceTransit(bool successfulJump)
    {
        if (_worldSceneCoordinatorRuntime is null ||
            _worldSceneCoordinatorNode is null ||
            WorldScenes.Current.Kind != WorldSceneKind.HyperspaceTransit)
        {
            return;
        }

        WorldSceneContext destination = WorldSceneContext.Create(
            WorldSceneKind.StationInterior,
            GalaxyNavigation.CurrentSystem.SystemId,
            GalaxyNavigation.CurrentPlanetId);
        WorldSceneTransitionResult transition =
            _worldSceneCoordinatorNode.TryTransition(destination, out string result);
        if (transition == WorldSceneTransitionResult.Rejected)
        {
            GD.PushError(
                "TASK-148 hyperspace scene transition FAIL: " +
                $"phase=complete; result={result}");
            return;
        }

        _worldResidencyTransitions++;
        ApplyWorldResidencyPolicy(force: true);
        GD.Print(
            "TASK-148 hyperspace scene transition PASS: " +
            $"phase=complete; applied={(successfulJump ? 1 : 0)}; " +
            $"system={destination.SystemId}; planet={destination.PlanetId}; " +
            $"generation={WorldScenes.Generation}.");
    }

    private bool WorldResidencyPolicyMatches()
    {
        if (_worldSceneCoordinatorRuntime is null ||
            _worldSceneCoordinatorNode is null)
        {
            return false;
        }

        WorldSceneKind kind = WorldScenes.Current.Kind;
        bool expectedSurface = kind switch
        {
            WorldSceneKind.Surface => true,
            WorldSceneKind.Orbit => ResolveSurfaceRuntimeActive(),
            _ => false
        };
        bool expectedOrbit = kind == WorldSceneKind.Orbit;
        WorldSceneCoordinatorDiagnostics diagnostics =
            _worldSceneCoordinatorNode.CreateDiagnostics();
        return _surfaceRuntimeActive == expectedSurface &&
            _orbitRuntimeActive == expectedOrbit &&
            diagnostics.SingleScene &&
            diagnostics.ShellMatchesContext;
    }

    private void RunWorldSceneCoordinatorAcceptance()
    {
        if (_worldSceneCoordinatorNode is null ||
            _worldSceneCoordinatorRuntime is null)
        {
            _worldSceneCoordinatorAcceptanceHud = "FAIL unavailable";
            return;
        }

        SynchronizeWorldSceneCoordinator();
        int surfaceActivationTransitionsBefore = _surfaceActivationTransitions;
        int planetActivationPipelineMaskBefore = _planetActivationPipelineMask;
        WorldSceneCoordinatorAcceptanceReport report;
        try
        {
            report = WorldSceneCoordinatorAcceptanceRunner.Run(
                _worldSceneCoordinatorNode,
                () => ApplyWorldResidencyPolicy(force: false),
                WorldResidencyPolicyMatches);
        }
        finally
        {
            _surfaceActivationTransitions = surfaceActivationTransitionsBefore;
            _planetActivationPipelineMask = planetActivationPipelineMaskBefore;
        }

        _worldSceneCoordinatorAcceptanceReport = report;
        _worldSceneCoordinatorAcceptanceHud = report.Passed
            ? $"PASS live={(report.LiveTransitionPath ? 1 : 0)}, " +
              $"oneScene={(report.SingleLiveScene ? 1 : 0)}, " +
              $"steps={report.LiveSteps}, " +
              $"restored={(report.StateRestored ? 1 : 0)}"
            : $"FAIL {report.Result}";

        WorldSceneCoordinatorDiagnostics diagnostics =
            _worldSceneCoordinatorNode.CreateDiagnostics();
        string output =
            "TASK-148 world scene coordinator acceptance " +
            (report.Passed ? "PASS" : "FAIL") + ": " +
            $"transitionGraph={(report.TransitionGraph ? 1 : 0)}; " +
            $"illegalRejected={(report.IllegalTransitionRejected ? 1 : 0)}; " +
            $"hyperspaceSystemChange={(report.HyperspaceSystemChange ? 1 : 0)}; " +
            $"contextValidation={(report.ContextValidation ? 1 : 0)}; " +
            $"packedScenes={(report.PackedScenes ? 1 : 0)}; " +
            $"singleLiveScene={(report.SingleLiveScene ? 1 : 0)}; " +
            $"liveContextMatch={(report.LiveContextMatch ? 1 : 0)}; " +
            $"residencyPolicy={(report.ResidencyPolicy ? 1 : 0)}; " +
            $"livePath={(report.LiveTransitionPath ? 1 : 0)}; " +
            $"transactionalSwap={(report.TransactionalSwap ? 1 : 0)}; " +
            $"stateRestored={(report.StateRestored ? 1 : 0)}; " +
            $"steps={report.LiveSteps}; maxHostChildren={report.MaxHostChildren}; " +
            $"testTransitions={report.TransitionCount}; testReloads={report.Reloads}; " +
            $"testRejected={report.RejectedTransitions}; " +
            $"testHyperspace={report.HyperspaceTransitions}; " +
            $"kind={diagnostics.Kind}; hostChildren={diagnostics.HostChildren}; " +
            $"generation={diagnostics.Generation}; reloads={diagnostics.Reloads}; " +
            $"sceneLoadFailures={diagnostics.SceneLoadFailures}; " +
            $"rollbacks={diagnostics.Rollbacks}; " +
            $"surfaceActive={(_surfaceRuntimeActive ? 1 : 0)}; " +
            $"orbitActive={(_orbitRuntimeActive ? 1 : 0)}; " +
            $"worldTransitions={_worldResidencyTransitions}; " +
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

    private string BuildWorldSceneCoordinatorHudLine()
    {
        if (_worldSceneCoordinatorNode is null ||
            _worldSceneCoordinatorRuntime is null)
        {
            return L("ui.hud.world_scene.unavailable");
        }

        WorldSceneCoordinatorDiagnostics diagnostics =
            _worldSceneCoordinatorNode.CreateDiagnostics();
        string kindKey = diagnostics.Kind switch
        {
            WorldSceneKind.Surface => "ui.world_scene.kind.surface",
            WorldSceneKind.Orbit => "ui.world_scene.kind.orbit",
            WorldSceneKind.StationInterior => "ui.world_scene.kind.station",
            WorldSceneKind.HyperspaceTransit => "ui.world_scene.kind.hyperspace",
            _ => "ui.world_scene.kind.unknown"
        };
        return LF(
            "ui.hud.world_scene.summary",
            ("kind", L(kindKey)),
            ("environment", diagnostics.EnvironmentProfile),
            ("generation", diagnostics.Generation),
            ("transitions", diagnostics.Transitions),
            ("surface", _surfaceRuntimeActive ? 1 : 0),
            ("orbit", _orbitRuntimeActive ? 1 : 0));
    }
}
