using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private NpcNavigationSurfaceNode? _npcNavigationSurface;
    private NpcNavigationAcceptancePhase _npcNavigationAcceptancePhase;
    private NpcNavigationAcceptanceReport? _npcNavigationAcceptanceReport;
    private string _npcNavigationAcceptanceHud = "READY";
    private double _npcNavigationAcceptanceElapsed;
    private int _npcNavigationAcceptanceInitialStreamGeneration;
    private int _npcNavigationAcceptanceInitialEvictions;
    private bool _npcNavigationAcceptanceLocalBudget;
    private bool _npcNavigationAcceptanceCrossTile;
    private bool _npcNavigationAcceptanceObstacleClearance;
    private bool _npcNavigationAcceptanceBoundedStreaming;
    private bool _npcNavigationAcceptanceRecoveryProbe;
    private bool _npcNavigationAcceptanceServerSync;
    private int _npcNavigationAcceptanceTilesTouched;
    private int _npcNavigationAcceptancePathPoints;

    private bool NpcNavigationAcceptanceRunning =>
        _npcNavigationAcceptancePhase != NpcNavigationAcceptancePhase.Idle &&
        _npcNavigationAcceptancePhase != NpcNavigationAcceptancePhase.Completed;

    private void BindNpcNavigationSceneNodes()
    {
        _npcNavigationSurface = GetNodeOrNull<NpcNavigationSurfaceNode>(
            "Gameplay/NpcNavigation");
        if (_npcNavigationSurface is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing Gameplay/NpcNavigation.");
        }
    }

    private void InitializeNpcNavigationSurface()
    {
        if (_npcNavigationSurface is null || _player is null)
        {
            return;
        }
        _npcNavigationSurface.Configure(_player, this);
        AttachNpcNavigationAgents();
    }

    private void AttachNpcNavigationAgents()
    {
        if (_npcNavigationSurface is null || !_npcNavigationSurface.IsConfigured ||
            _npcPopulationRoot is null)
        {
            return;
        }
        int attached = 0;
        foreach (Node child in _npcPopulationRoot.GetChildren())
        {
            if (child is not NpcFactionAgentNode agent)
            {
                continue;
            }
            agent.EnableNavigation(_npcNavigationSurface);
            attached++;
        }
        GD.Print(
            "TASK-124 NPC NavigationAgent3D binding READY: " +
            $"dynamicAgents={attached}; boundedStreaming=1; obstacleRecovery=1; avoidance=1.");
    }

    private void RefreshNpcNavigationObstacles()
    {
        if (_npcNavigationSurface?.IsConfigured == true)
        {
            _npcNavigationSurface.RefreshObstacleGeometry();
        }
    }

    private string BuildNpcNavigationHudLine()
    {
        if (_npcNavigationSurface is null || !_npcNavigationSurface.IsConfigured)
        {
            return L("ui.hud.npc_navigation.unavailable");
        }
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        NpcNavigationAgentDiagnostics[] diagnostics = GetNpcNavigationDiagnostics();
        int active = diagnostics.Count(item => item.NavigationActive);
        int requests = diagnostics.Sum(item => item.PathRequests);
        int recoveries = diagnostics.Sum(item => item.StuckRecoveries);
        return LF(
            "ui.hud.npc_navigation.summary",
            ("regions", snapshot.ActiveRegions),
            ("maxRegions", snapshot.MaximumRegions),
            ("cells", snapshot.WalkableCells),
            ("obstacles", snapshot.StaticObstacles),
            ("active", active),
            ("agents", diagnostics.Length),
            ("paths", requests),
            ("recoveries", recoveries),
            ("sync", L(snapshot.ReadyForQueries ? "ui.common.ready" : "ui.common.wait")));
    }

    private void BeginNpcNavigationAcceptance()
    {
        _npcNavigationAcceptanceReport = null;
        _npcNavigationAcceptanceHud = "RUNNING sync";
        _npcNavigationAcceptanceElapsed = 0.0;
        _npcNavigationAcceptanceLocalBudget = false;
        _npcNavigationAcceptanceCrossTile = false;
        _npcNavigationAcceptanceObstacleClearance = false;
        _npcNavigationAcceptanceBoundedStreaming = false;
        _npcNavigationAcceptanceRecoveryProbe = false;
        _npcNavigationAcceptanceServerSync = false;
        _npcNavigationAcceptanceTilesTouched = 0;
        _npcNavigationAcceptancePathPoints = 0;
        if (_npcNavigationSurface is null || !_npcNavigationSurface.IsConfigured || _player is null)
        {
            CompleteNpcNavigationAcceptanceFailure("navigation surface is unavailable");
            return;
        }
        _npcNavigationSurface.SetAcceptanceStreamingCenter(null);
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        _npcNavigationAcceptanceInitialStreamGeneration = snapshot.StreamGeneration;
        _npcNavigationAcceptanceInitialEvictions = snapshot.EvictedRegions;
        _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.WaitInitialSync;
    }

    private void UpdateNpcNavigationAcceptance(double delta)
    {
        if (!NpcNavigationAcceptanceRunning || _npcNavigationSurface is null || _player is null)
        {
            return;
        }
        _npcNavigationAcceptanceElapsed += delta;
        if (_npcNavigationAcceptanceElapsed > 8.0)
        {
            _npcNavigationSurface.SetAcceptanceStreamingCenter(null);
            CompleteNpcNavigationAcceptanceFailure(
                $"timeout in phase {_npcNavigationAcceptancePhase}");
            return;
        }

        switch (_npcNavigationAcceptancePhase)
        {
            case NpcNavigationAcceptancePhase.WaitInitialSync:
                if (!_npcNavigationSurface.ReadyForQueries)
                {
                    return;
                }
                ProbeInitialNavigationSurface();
                _npcNavigationSurface.SetAcceptanceStreamingCenter(
                    _player.GlobalPosition + new Vector3(24.5f, 0.0f, 0.0f));
                _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.WaitShiftSync;
                _npcNavigationAcceptanceHud = "RUNNING stream-shift";
                break;

            case NpcNavigationAcceptancePhase.WaitShiftSync:
                if (!_npcNavigationSurface.ReadyForQueries)
                {
                    return;
                }
                NpcNavigationSurfaceSnapshot shifted = _npcNavigationSurface.CreateSnapshot();
                _npcNavigationAcceptanceBoundedStreaming =
                    shifted.ActiveRegions <= shifted.MaximumRegions &&
                    shifted.StreamGeneration > _npcNavigationAcceptanceInitialStreamGeneration &&
                    shifted.EvictedRegions > _npcNavigationAcceptanceInitialEvictions;
                _npcNavigationSurface.SetAcceptanceStreamingCenter(null);
                _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.WaitRestoreSync;
                _npcNavigationAcceptanceHud = "RUNNING stream-restore";
                break;

            case NpcNavigationAcceptancePhase.WaitRestoreSync:
                if (!_npcNavigationSurface.ReadyForQueries)
                {
                    return;
                }
                _npcNavigationAcceptanceServerSync = true;
                _npcNavigationAcceptanceElapsed = 0.0;
                _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.ObserveAgents;
                _npcNavigationAcceptanceHud = "RUNNING agents";
                break;

            case NpcNavigationAcceptancePhase.ObserveAgents:
                if (_npcNavigationAcceptanceElapsed < 1.25)
                {
                    return;
                }
                CompleteNpcNavigationAcceptance();
                break;
        }
    }

    private void ProbeInitialNavigationSurface()
    {
        if (_npcNavigationSurface is null || _player is null)
        {
            return;
        }
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        _npcNavigationAcceptanceLocalBudget =
            snapshot.ActiveRegions > 0 &&
            snapshot.ActiveRegions <= snapshot.MaximumRegions &&
            snapshot.MaximumRegions == 25 &&
            snapshot.WalkableCells > 0;

        Vector3 center = _player.GlobalPosition;
        Vector3[] bestPath = Array.Empty<Vector3>();
        int bestTiles = 0;
        float[] zOffsets = { -18.0f, -12.0f, -6.0f, 0.0f, 6.0f, 12.0f, 18.0f };
        foreach (float zOffset in zOffsets)
        {
            Vector3 start = new(
                center.X - 20.0f,
                NpcNavigationSurfaceNode.NavigationSurfaceY,
                center.Z + zOffset);
            Vector3 target = new(
                center.X + 20.0f,
                NpcNavigationSurfaceNode.NavigationSurfaceY,
                center.Z + zOffset);
            Vector3[] candidate = _npcNavigationSurface.QueryPath(start, target);
            int touched = _npcNavigationSurface.CountTilesTouchedByPath(candidate);
            if (candidate.Length < 2 || touched < bestTiles)
            {
                continue;
            }
            bestPath = candidate;
            bestTiles = touched;
            if (touched >= 4 && _npcNavigationSurface.PathAvoidsCapturedObstacles(candidate))
            {
                break;
            }
        }
        _npcNavigationAcceptancePathPoints = bestPath.Length;
        _npcNavigationAcceptanceTilesTouched = bestTiles;
        _npcNavigationAcceptanceCrossTile = bestPath.Length >= 2 && bestTiles >= 3;
        _npcNavigationAcceptanceObstacleClearance =
            bestPath.Length >= 2 &&
            snapshot.StaticObstacles > 0 &&
            snapshot.AvoidanceObstacles == snapshot.StaticObstacles &&
            _npcNavigationSurface.PathAvoidsCapturedObstacles(bestPath);
        _npcNavigationAcceptanceRecoveryProbe = bestPath.Length >= 2 &&
            _npcNavigationSurface.TryBuildRecoveryWaypoint(
                bestPath[0],
                bestPath[^1],
                124,
                out _);
    }

    private void CompleteNpcNavigationAcceptance()
    {
        if (_npcNavigationSurface is null)
        {
            CompleteNpcNavigationAcceptanceFailure("navigation surface disappeared");
            return;
        }
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        NpcNavigationAgentDiagnostics[] diagnostics = GetNpcNavigationDiagnostics();
        int navigationAgents = diagnostics.Count(item => item.NavigationEnabled);
        int pathRequests = diagnostics.Sum(item => item.PathRequests);
        int avoidanceSamples = diagnostics.Sum(item => item.AvoidanceSamples);
        int recoveries = diagnostics.Sum(item => item.StuckRecoveries);
        int expectedNavigationAgents = NpcFactionCatalog.ExpectedAgentCount - 1;
        NpcNavigationAgentDiagnostics[] activeDiagnostics = _npcFactionRuntime is null
            ? diagnostics
            : diagnostics.Where(item => !_npcFactionRuntime.GetAgent(item.NpcId).Defeated).ToArray();
        bool agentRuntime =
            diagnostics.Length == expectedNavigationAgents &&
            navigationAgents == expectedNavigationAgents &&
            activeDiagnostics.Length > 0 &&
            activeDiagnostics.All(item => item.NavigationEnabled && item.PathRequests > 0);
        bool avoidanceRuntime =
            navigationAgents == expectedNavigationAgents &&
            activeDiagnostics.Length > 0 &&
            activeDiagnostics.All(item => item.AvoidanceSamples > 0);
        bool passed = _npcNavigationAcceptanceLocalBudget &&
            _npcNavigationAcceptanceCrossTile &&
            _npcNavigationAcceptanceObstacleClearance &&
            _npcNavigationAcceptanceBoundedStreaming &&
            agentRuntime &&
            avoidanceRuntime &&
            _npcNavigationAcceptanceRecoveryProbe &&
            _npcNavigationAcceptanceServerSync;
        string result = passed
            ? "local tiled NavigationServer3D runtime verified"
            : "one or more navigation invariants failed";
        _npcNavigationAcceptanceReport = new NpcNavigationAcceptanceReport(
            passed,
            snapshot.ActiveRegions,
            snapshot.MaximumRegions,
            snapshot.WalkableCells,
            snapshot.StaticObstacles,
            snapshot.AvoidanceObstacles,
            _npcNavigationAcceptanceTilesTouched,
            _npcNavigationAcceptancePathPoints,
            navigationAgents,
            pathRequests,
            avoidanceSamples,
            recoveries,
            snapshot.EvictedRegions,
            _npcNavigationAcceptanceLocalBudget,
            _npcNavigationAcceptanceCrossTile,
            _npcNavigationAcceptanceObstacleClearance,
            _npcNavigationAcceptanceBoundedStreaming,
            agentRuntime,
            avoidanceRuntime,
            _npcNavigationAcceptanceRecoveryProbe,
            _npcNavigationAcceptanceServerSync,
            result);
        _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.Completed;
        _npcNavigationAcceptanceHud = passed
            ? $"PASS regions={snapshot.ActiveRegions}/{snapshot.MaximumRegions}, tiles={_npcNavigationAcceptanceTilesTouched}, paths={pathRequests}, avoid={avoidanceSamples}"
            : $"FAIL: {result}";
        PrintNpcNavigationAcceptance(_npcNavigationAcceptanceReport);
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private void CompleteNpcNavigationAcceptanceFailure(string result)
    {
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface?.CreateSnapshot() ??
            new NpcNavigationSurfaceSnapshot(0, 25, 0, 0, 0, 0, 0, 0, 0, false,
                new NpcNavigationTileKey(0, 0), Array.Empty<NpcNavigationTileKey>());
        _npcNavigationAcceptanceReport = new NpcNavigationAcceptanceReport(
            false,
            snapshot.ActiveRegions,
            snapshot.MaximumRegions,
            snapshot.WalkableCells,
            snapshot.StaticObstacles,
            snapshot.AvoidanceObstacles,
            _npcNavigationAcceptanceTilesTouched,
            _npcNavigationAcceptancePathPoints,
            0, 0, 0, 0,
            snapshot.EvictedRegions,
            false, false, false, false, false, false, false, false,
            result);
        _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.Completed;
        _npcNavigationAcceptanceHud = $"FAIL: {result}";
        PrintNpcNavigationAcceptance(_npcNavigationAcceptanceReport);
        UpdateCombinedCatalogAndShipAcceptanceState();
    }

    private void PrintNpcNavigationAcceptance(NpcNavigationAcceptanceReport report)
    {
        string line =
            $"TASK-124 NPC navigation acceptance {(report.Passed ? "PASS" : "FAIL")}: " +
            $"regions={report.Regions}/{report.MaximumRegions}; walkableCells={report.WalkableCells}; " +
            $"obstacles={report.StaticObstacles}; avoidanceObstacles={report.AvoidanceObstacles}; " +
            $"tilesTouched={report.TilesTouched}; pathPoints={report.PathPoints}; " +
            $"localBudget={(report.LocalTileBudget ? 1 : 0)}; crossTilePath={(report.CrossTilePath ? 1 : 0)}; " +
            $"obstacleClearance={(report.ObstacleClearance ? 1 : 0)}; boundedStreaming={(report.BoundedStreaming ? 1 : 0)}; " +
            $"navigationAgents={report.NavigationAgents}; pathRequests={report.PathRequests}; " +
            $"avoidanceSamples={report.AvoidanceSamples}; agentRuntime={(report.NavigationAgentRuntime ? 1 : 0)}; " +
            $"avoidanceRuntime={(report.AvoidanceRuntime ? 1 : 0)}; recoveryProbe={(report.RecoveryProbe ? 1 : 0)}; " +
            $"recoveries={report.Recoveries}; evicted={report.EvictedRegions}; sync={(report.ServerSynchronized ? 1 : 0)}; " +
            $"result={report.Result}.";
        if (report.Passed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushError(line);
        }
    }

    private NpcNavigationAgentDiagnostics[] GetNpcNavigationDiagnostics()
    {
        if (_npcPopulationRoot is null)
        {
            return Array.Empty<NpcNavigationAgentDiagnostics>();
        }
        return _npcPopulationRoot.GetChildren()
            .OfType<NpcFactionAgentNode>()
            .Select(agent => agent.NavigationDiagnostics)
            .OrderBy(item => item.NpcId, StringComparer.Ordinal)
            .ToArray();
    }
}
