using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public sealed record WorldStreamingDiagnostics(
    bool Active,
    WorldStreamingTravelMode TravelMode,
    double FullDetailRadiusMeters,
    double SimplifiedRadiusMeters,
    int FullRegions,
    int SimplifiedRegions,
    int PreloadRegions,
    int QueuedOperations,
    int WorkerLimit,
    int Revision,
    int CompletedRevision,
    int CancelledPlans,
    int StalePlans,
    double LastApplyMilliseconds,
    double PeakApplyMilliseconds,
    int BudgetOverruns,
    double MainThreadBudgetMilliseconds);

public partial class WorldStreamingCoordinatorNode : Node
{
    private sealed record PendingPlan(int Revision, WorldStreamingPlan Plan);

    private readonly Dictionary<WorldStreamingRegionCoordinate,
        WorldStreamingRegionPlan> _residentRegions = new();
    private readonly Queue<WorldStreamingRegionPlan> _applyQueue = new();
    private readonly Queue<WorldStreamingRegionCoordinate> _evictionQueue = new();
    private CancellationTokenSource? _planCancellation;
    private Task<PendingPlan>? _planTask;
    private WorldStreamingObserverSample _lastObserver;
    private WorldStreamingPlan? _lastCompletedPlan;
    private double _replanAccumulator;
    private int _revision;
    private int _completedRevision;
    private int _cancelledPlans;
    private int _stalePlans;
    private int _budgetOverruns;
    private double _lastApplyMilliseconds;
    private double _peakApplyMilliseconds;
    private bool _active;
    private bool _hasObserver;
    private double _presentationDistanceScale = 1.0;

    public int WorkerLimit { get; private set; } = 1;

    public double PresentationDistanceScale => _presentationDistanceScale;

    public void SetPresentationDistanceScale(double scale)
    {
        double clamped = Math.Clamp(scale, 0.45, 1.25);
        if (Math.Abs(clamped - _presentationDistanceScale) < 0.0001)
        {
            return;
        }
        _presentationDistanceScale = clamped;
        _replanAccumulator = WorldStreamingRuntime.ReplanIntervalSeconds;
        _planCancellation?.Cancel();
    }

    public override void _Ready()
    {
        WorkerLimit = WorldStreamingRuntime.ResolveWorkerCount(
            System.Environment.ProcessorCount);
    }

    public override void _ExitTree()
    {
        _planCancellation?.Cancel();
        _planCancellation?.Dispose();
        _planCancellation = null;
    }

    public void Tick(double delta, WorldStreamingObserverSample observer)
    {
        _active = true;
        PollPlanTask();
        ApplyPlanSlice(WorldStreamingFrameBudgetMode.Regular);

        _replanAccumulator += Math.Max(0.0, delta);
        bool regionChanged = !_hasObserver ||
            WorldStreamingRuntime.WorldToRegion(
                observer.EastMeters,
                observer.NorthMeters) !=
            WorldStreamingRuntime.WorldToRegion(
                _lastObserver.EastMeters,
                _lastObserver.NorthMeters);
        bool modeChanged = !_hasObserver ||
            observer.TravelMode != _lastObserver.TravelMode;
        bool intervalElapsed = _replanAccumulator >=
            WorldStreamingRuntime.ReplanIntervalSeconds;
        if (_planTask is null &&
            (_lastCompletedPlan is null || regionChanged || modeChanged || intervalElapsed))
        {
            RequestPlan(observer);
        }
        _lastObserver = observer;
        _hasObserver = true;
    }

    public void Suspend()
    {
        _active = false;
        _planCancellation?.Cancel();
        _applyQueue.Clear();
        _evictionQueue.Clear();
    }

    public WorldStreamingRegionDetail? GetDetailAt(
        double eastMeters,
        double northMeters)
    {
        WorldStreamingRegionCoordinate coordinate =
            WorldStreamingRuntime.WorldToRegion(eastMeters, northMeters);
        return _residentRegions.TryGetValue(coordinate, out WorldStreamingRegionPlan? plan)
            ? plan.Detail
            : null;
    }

    public WorldStreamingDiagnostics CreateDiagnostics()
    {
        int full = _residentRegions.Values.Count(region =>
            region.Detail == WorldStreamingRegionDetail.Full);
        int simplified = _residentRegions.Values.Count(region =>
            region.Detail == WorldStreamingRegionDetail.Simplified);
        int preload = _residentRegions.Values.Count(region =>
            region.Detail == WorldStreamingRegionDetail.Preload);
        double fullRadius = _lastCompletedPlan?.FullDetailRadiusMeters ??
            WorldStreamingRuntime.ResolveFullDetailRadiusMeters(
                _hasObserver ? _lastObserver.TravelMode : WorldStreamingTravelMode.OnFoot);
        double simplifiedRadius = _lastCompletedPlan?.SimplifiedRadiusMeters ??
            fullRadius * WorldStreamingRuntime.SimplifiedRadiusMultiplier;
        return new WorldStreamingDiagnostics(
            _active,
            _hasObserver ? _lastObserver.TravelMode : WorldStreamingTravelMode.OnFoot,
            fullRadius,
            simplifiedRadius,
            full,
            simplified,
            preload,
            _applyQueue.Count + _evictionQueue.Count,
            WorkerLimit,
            _revision,
            _completedRevision,
            _cancelledPlans,
            _stalePlans,
            _lastApplyMilliseconds,
            _peakApplyMilliseconds,
            _budgetOverruns,
            WorldStreamingRuntime.RegularMainThreadBudgetMilliseconds);
    }

    private void RequestPlan(WorldStreamingObserverSample observer)
    {
        _replanAccumulator = 0.0;
        _planCancellation?.Cancel();
        _planCancellation?.Dispose();
        _planCancellation = new CancellationTokenSource();
        CancellationToken token = _planCancellation.Token;
        int revision = ++_revision;
        _planTask = Task.Run(() =>
        {
            WorldStreamingPlan plan = WorldStreamingRuntime.BuildPlan(
                observer,
                _presentationDistanceScale,
                token);
            return new PendingPlan(revision, plan);
        }, token);
    }

    private void PollPlanTask()
    {
        if (_planTask is null || !_planTask.IsCompleted)
        {
            return;
        }

        Task<PendingPlan> task = _planTask;
        _planTask = null;
        if (task.IsCanceled)
        {
            _cancelledPlans++;
            return;
        }
        if (task.IsFaulted)
        {
            Exception? error = task.Exception?.GetBaseException();
            GD.PushError(
                $"TASK-194 world streaming worker failed: {error?.GetType().Name}: {error?.Message}");
            return;
        }

        PendingPlan pending = task.GetAwaiter().GetResult();
        if (pending.Revision != _revision)
        {
            _stalePlans++;
            return;
        }

        StagePlan(pending);
    }

    private void StagePlan(PendingPlan pending)
    {
        _applyQueue.Clear();
        _evictionQueue.Clear();
        HashSet<WorldStreamingRegionCoordinate> desired = pending.Plan.Regions
            .Select(region => region.Coordinate)
            .ToHashSet();
        foreach (WorldStreamingRegionPlan region in pending.Plan.Regions)
        {
            if (!_residentRegions.TryGetValue(region.Coordinate, out WorldStreamingRegionPlan? current) ||
                current.Detail != region.Detail ||
                current.Priority != region.Priority)
            {
                _applyQueue.Enqueue(region);
            }
        }
        foreach (WorldStreamingRegionCoordinate coordinate in _residentRegions.Keys
                     .Where(coordinate => !desired.Contains(coordinate))
                     .ToArray())
        {
            _evictionQueue.Enqueue(coordinate);
        }
        _lastCompletedPlan = pending.Plan;
        _completedRevision = pending.Revision;
    }

    private void ApplyPlanSlice(WorldStreamingFrameBudgetMode mode)
    {
        if (_applyQueue.Count == 0 && _evictionQueue.Count == 0)
        {
            _lastApplyMilliseconds = 0.0;
            return;
        }

        double budget = WorldStreamingRuntime.ResolveMainThreadBudgetMilliseconds(mode);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int operations = 0;
        while (_applyQueue.Count > 0 &&
            (operations == 0 || stopwatch.Elapsed.TotalMilliseconds < budget))
        {
            WorldStreamingRegionPlan region = _applyQueue.Dequeue();
            _residentRegions[region.Coordinate] = region;
            operations++;
        }
        while (_applyQueue.Count == 0 && _evictionQueue.Count > 0 &&
            (operations == 0 || stopwatch.Elapsed.TotalMilliseconds < budget))
        {
            _residentRegions.Remove(_evictionQueue.Dequeue());
            operations++;
        }
        stopwatch.Stop();
        _lastApplyMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        _peakApplyMilliseconds = Math.Max(
            _peakApplyMilliseconds,
            _lastApplyMilliseconds);
        if (_lastApplyMilliseconds > budget + 0.50)
        {
            _budgetOverruns++;
        }
    }
}
