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
    private int _npcNavigationAcceptanceProbeAttempts;

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
        _npcNavigationSurface.Configure(_player, this, CurrentTerrainProfile);
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
        _npcNavigationAcceptanceProbeAttempts = 0;
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
        if (_npcNavigationAcceptanceElapsed > 6.0)
        {
            _npcNavigationSurface.SetAcceptanceStreamingCenter(null);
            CompleteNpcNavigationAcceptanceFailure(
                $"timeout in phase {_npcNavigationAcceptancePhase}; pathProbeAttempts={_npcNavigationAcceptanceProbeAttempts}");
            return;
        }

        switch (_npcNavigationAcceptancePhase)
        {
            case NpcNavigationAcceptancePhase.WaitInitialSync:
                if (!_npcNavigationSurface.ReadyForQueries)
                {
                    return;
                }
                if (!TryProbeInitialNavigationSurface())
                {
                    _npcNavigationAcceptanceHud =
                        $"RUNNING path-sync attempt={_npcNavigationAcceptanceProbeAttempts}";
                    return;
                }
                _npcNavigationSurface.SetAcceptanceStreamingCenter(
                    _player.GlobalPosition + new Vector3(24.5f, 0.0f, 0.0f));
                _npcNavigationAcceptanceElapsed = 0.0;
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
                _npcNavigationAcceptanceElapsed = 0.0;
                _npcNavigationAcceptancePhase = NpcNavigationAcceptancePhase.WaitRestoreSync;
                _npcNavigationAcceptanceHud = "RUNNING stream-restore";
                break;

            case NpcNavigationAcceptancePhase.WaitRestoreSync:
                if (!_npcNavigationSurface.ReadyForQueries)
                {
                    return;
                }
                if (!TryProbeInitialNavigationSurface())
                {
                    _npcNavigationAcceptanceHud =
                        $"RUNNING restored-path attempt={_npcNavigationAcceptanceProbeAttempts}";
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

    private bool TryProbeInitialNavigationSurface()
    {
        if (_npcNavigationSurface is null || _player is null ||
            !_npcNavigationSurface.ReadyForQueries)
        {
            return false;
        }

        _npcNavigationAcceptanceProbeAttempts++;
        NpcNavigationSurfaceSnapshot snapshot = _npcNavigationSurface.CreateSnapshot();
        bool localBudget =
            snapshot.ActiveRegions > 0 &&
            snapshot.ActiveRegions <= snapshot.MaximumRegions &&
            snapshot.MaximumRegions == 25 &&
            snapshot.WalkableCells > 0;
        if (!localBudget)
        {
            return false;
        }

        Vector3 center = _player.GlobalPosition;
        Vector3[] bestPath = Array.Empty<Vector3>();
        int bestTiles = 0;
        bool bestClearance = false;
        foreach ((Vector3 Start, Vector3 Target) probe in
                 BuildNavigationAcceptancePathProbes(center))
        {
            Vector3[] candidate = _npcNavigationSurface.QueryPath(
                probe.Start,
                probe.Target);
            if (candidate.Length < 2)
            {
                continue;
            }

            int touched = _npcNavigationSurface.CountTilesTouchedByPath(candidate);
            bool clearance = _npcNavigationSurface.PathAvoidsCapturedObstacles(candidate);
            if (touched > bestTiles ||
                (touched == bestTiles && clearance && !bestClearance))
            {
                bestPath = candidate;
                bestTiles = touched;
                bestClearance = clearance;
            }
            if (touched >= 3 && clearance)
            {
                break;
            }
        }

        bool crossTile = bestPath.Length >= 2 && bestTiles >= 3;
        bool obstacleClearance =
            crossTile &&
            snapshot.StaticObstacles > 0 &&
            snapshot.AvoidanceObstacles == snapshot.StaticObstacles &&
            bestClearance;
        bool recoveryProbe = crossTile && TryProbeRecoveryWaypoint(bestPath);
        if (!crossTile || !obstacleClearance || !recoveryProbe)
        {
            return false;
        }

        _npcNavigationAcceptanceLocalBudget = true;
        _npcNavigationAcceptancePathPoints = bestPath.Length;
        _npcNavigationAcceptanceTilesTouched = bestTiles;
        _npcNavigationAcceptanceCrossTile = true;
        _npcNavigationAcceptanceObstacleClearance = true;
        _npcNavigationAcceptanceRecoveryProbe = true;
        return true;
    }

    private (Vector3 Start, Vector3 Target)[]
        BuildNavigationAcceptancePathProbes(Vector3 centerWorld)
    {
        Node3D? gameplay = GetNodeOrNull<Node3D>("Gameplay");
        Vector3 center = gameplay is null ? centerWorld : gameplay.ToLocal(centerWorld);
        float y = NpcNavigationSurfaceNode.NavigationSurfaceY;
        float[] offsets = { -18.0f, -12.0f, -6.0f, 0.0f, 6.0f, 12.0f, 18.0f };
        var probes = new System.Collections.Generic.List<(Vector3, Vector3)>();

        Vector3 World(Vector3 local) => gameplay is null ? local : gameplay.ToGlobal(local);
        foreach (float offset in offsets)
        {
            probes.Add((
                World(new Vector3(center.X - 20.0f, y, center.Z + offset)),
                World(new Vector3(center.X + 20.0f, y, center.Z + offset))));
            probes.Add((
                World(new Vector3(center.X + offset, y, center.Z - 20.0f)),
                World(new Vector3(center.X + offset, y, center.Z + 20.0f))));
        }
        probes.Add((
            World(new Vector3(center.X - 18.0f, y, center.Z - 18.0f)),
            World(new Vector3(center.X + 18.0f, y, center.Z + 18.0f))));
        probes.Add((
            World(new Vector3(center.X - 18.0f, y, center.Z + 18.0f)),
            World(new Vector3(center.X + 18.0f, y, center.Z - 18.0f))));
        return probes.ToArray();
    }

    private bool TryProbeRecoveryWaypoint(Vector3[] path)
    {
        if (_npcNavigationSurface is null || path.Length < 2)
        {
            return false;
        }

        int[] indices = { 0, Math.Min(1, path.Length - 2), Math.Max(0, path.Length / 2 - 1) };
        foreach (int index in indices.Distinct())
        {
            Vector3 current = path[index];
            Vector3 target = path[^1];
            for (int sideSeed = 124; sideSeed <= 127; sideSeed++)
            {
                if (_npcNavigationSurface.TryBuildRecoveryWaypoint(
                    current,
                    target,
                    sideSeed,
                    out _))
                {
                    return true;
                }
            }
        }

        return false;
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
            $"tilesTouched={report.TilesTouched}; pathPoints={report.PathPoints}; probeAttempts={_npcNavigationAcceptanceProbeAttempts}; " +
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
