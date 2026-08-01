using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CancellationToken = System.Threading.CancellationToken;
using CancellationTokenSource = System.Threading.CancellationTokenSource;
using System.Threading.Tasks;
using Godot;

public partial class TerrainChunkManager : Node3D
{
    [Export(PropertyHint.Range, "1,4,1")]
    public int ActiveRadius { get; set; } = 1;

    [Export(PropertyHint.Range, "0,3,1")]
    public int HighDetailRadius { get; set; } = 0;

    [Export(PropertyHint.Range, "0,4,1")]
    public int CollisionRadius { get; set; } = 1;

    [Export(PropertyHint.Range, "3,257,2")]
    public int HighDetailResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "3,257,2")]
    public int LowDetailResolution { get; set; } = 17;

    [Export(PropertyHint.Range, "3,257,2")]
    public int CollisionResolution { get; set; } = 33;

    [Export(PropertyHint.Range, "4.0,512.0,1.0")]
    public float ChunkSize { get; set; } = 32.0f;

    [Export(PropertyHint.Range, "0.0,64.0,0.1")]
    public float HeightScale { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.001,1.0,0.001")]
    public float NoiseFrequency { get; set; } = 0.035f;

    [Export(PropertyHint.Range, "0.0,32.0,0.1")]
    public float SkirtDepth { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.0,12.0,0.25")]
    public float ChunkSwitchHysteresis { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.0,0.25,0.01")]
    public float OperationIntervalSeconds { get; set; } = 0.06f;

    [Export(PropertyHint.Range, "1,4,1")]
    public int MaxOperationsPerStep { get; set; } = 1;

    [Export(PropertyHint.Range, "4,32,1")]
    public int StressTestRevisionCount { get; set; } = 12;

    [Export(PropertyHint.Range, "0.01,0.25,0.01")]
    public float StressTestStepIntervalSeconds { get; set; } = 0.03f;

    [Export(PropertyHint.Range, "5.0,60.0,1.0")]
    public float StressTestTimeoutSeconds { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "30.0,900.0,10.0")]
    public float SoakTestDurationSeconds { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "0.10,5.00,0.10")]
    public float SoakTestDwellSeconds { get; set; } = 0.75f;

    [Export(PropertyHint.Range, "1,12,1")]
    public int SoakTestRouteRadius { get; set; } = 3;

    [Export(PropertyHint.Range, "5.0,60.0,1.0")]
    public float SoakTestTransitionTimeoutSeconds { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "8.0,512.0,8.0")]
    public float SoakTestAllowedManagedMemoryGrowthMegabytes { get; set; } =
        64.0f;

    [Export]
    public int NoiseSeed { get; set; } = 20260801;

    [Export]
    public TerrainDebugViewMode DebugViewMode { get; set; } =
        TerrainDebugViewMode.HeightAndSlope;

    [Export]
    public bool ShowWorldGrid { get; set; } = true;

    [Export]
    public bool ShowWireframe { get; set; } = true;

    [Export]
    public bool ShowChunkBorders { get; set; } = true;

    [Export(PropertyHint.Range, "1.0,32.0,1.0")]
    public float DebugGridSpacing { get; set; } = 4.0f;

    [Export]
    public NodePath PlayerPath { get; set; } = new("../Player");

    [Export]
    public NodePath StatusLabelPath { get; set; } =
        new("../Hud/MarginContainer/PanelContainer/Label");

    private readonly Dictionary<Vector2I, TerrainChunk> _activeChunks = new();
    private readonly Queue<ChunkOperation> _pendingOperations = new();
    private readonly ConcurrentQueue<CompletedChunkJob> _completedJobs = new();
    private readonly Dictionary<long, ActiveChunkJob> _activeJobs = new();
    private readonly Queue<long> _jobApplyOrder = new();
    private readonly Dictionary<long, ReadyChunkJob> _readyJobs = new();
    private Dictionary<Vector2I, ChunkSpec> _desiredSpecs = new();
    private CharacterBody3D? _player;
    private Label? _statusLabel;
    private Vector2I _currentChunk = new(int.MinValue, int.MinValue);
    private double _hudUpdateAccumulator;
    private Godot.Timer? _operationTimer;
    private int _planRevision;
    private int _completedRevision;
    private int _operationsCompletedLastStep;
    private int _plannedCreates;
    private int _plannedUpdates;
    private int _plannedRemovals;
    private int _completedCreates;
    private int _completedUpdates;
    private int _completedRemovals;
    private CancellationTokenSource? _planCancellation;
    private long _nextJobId;
    private int _workerLimit = 1;
    private int _failedJobs;
    private int _cancelledJobs;
    private int _discardedStaleJobs;
    private int _totalFailedJobs;
    private int _totalCancelledJobs;
    private int _totalDiscardedStaleJobs;
    private TerrainStressTestState _stressTestState =
        TerrainStressTestState.Idle;
    private readonly List<Vector2I> _stressTestCenters = new();
    private string _stressTestStatus = "не запускался";
    private double _stressTestElapsedSeconds;
    private double _stressTestStepAccumulator;
    private int _stressTestNextCenterIndex;
    private Vector3 _stressTestOriginalPlayerPosition;
    private Vector2I _stressTestOriginalChunk;
    private bool _stressTestPlayerPhysicsWasEnabled;
    private int _stressTestStartingRevision;
    private int _stressTestBaselineFailedJobs;
    private int _stressTestBaselineCancelledJobs;
    private int _stressTestBaselineStaleJobs;
    private TerrainSoakTestState _soakTestState = TerrainSoakTestState.Idle;
    private readonly List<Vector2I> _soakTestRoute = new();
    private string _soakTestStatus = "не запускался";
    private double _soakTestElapsedSeconds;
    private double _soakTestDwellAccumulator;
    private double _soakTestTransitionElapsedSeconds;
    private int _soakTestNextCenterIndex;
    private int _soakTestCompletedMoves;
    private int _soakTestIdleSamples;
    private bool _soakTestSampleCapturedForCurrentIdle;
    private Vector3 _soakTestOriginalPlayerPosition;
    private Vector2I _soakTestOriginalChunk;
    private bool _soakTestPlayerPhysicsWasEnabled;
    private int _soakTestStartingRevision;
    private int _soakTestBaselineFailedJobs;
    private int _soakTestBaselineCancelledJobs;
    private int _soakTestBaselineStaleJobs;
    private long _soakTestBaselineManagedBytes;
    private long _soakTestPeakManagedBytes;
    private int _soakTestPeakActiveChunks;
    private int _soakTestPeakQueuedWork;
    private int _soakTestPeakWorkers;
    private int _soakTestLastMeshCount;
    private int _soakTestLastCollisionCount;
    private int _soakTestLastVertexCount;

    public override void _Ready()
    {
        _player = GetNode<CharacterBody3D>(PlayerPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _currentChunk = WorldToChunkWithoutHysteresis(_player.GlobalPosition);
        _workerLimit = Math.Max(
            1,
            Math.Min(4, System.Environment.ProcessorCount - 2));
        _operationTimer = new Godot.Timer
        {
            Name = "TerrainOperationTimer",
            OneShot = false,
            Autostart = false,
            WaitTime = GetOperationInterval()
        };
        _operationTimer.Timeout += ProcessOperationQueue;
        AddChild(_operationTimer);

        PlanRefresh(executeImmediately: true);
        UpdateHud();
    }


    public override void _ExitTree()
    {
        _operationTimer?.Stop();
        _planCancellation?.Cancel();
        _planCancellation?.Dispose();
        _planCancellation = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey eventKey ||
            !eventKey.Pressed ||
            eventKey.IsEcho())
        {
            return;
        }

        if (eventKey.Keycode == Key.F10)
        {
            StartTerrainStressTest();
            return;
        }

        if (eventKey.Keycode == Key.P)
        {
            if (_soakTestState == TerrainSoakTestState.Idle)
            {
                StartTerrainSoakTest();
            }
            else
            {
                CancelTerrainSoakTest("остановлен оператором");
            }

            return;
        }

        bool changed = true;

        switch (eventKey.Keycode)
        {
            case Key.F1:
                DebugViewMode = DebugViewMode switch
                {
                    TerrainDebugViewMode.HeightAndSlope =>
                        TerrainDebugViewMode.Lod,
                    TerrainDebugViewMode.Lod =>
                        TerrainDebugViewMode.Normals,
                    _ => TerrainDebugViewMode.HeightAndSlope
                };
                break;

            case Key.F2:
                ShowWorldGrid = !ShowWorldGrid;
                break;

            case Key.F3:
                ShowWireframe = !ShowWireframe;
                break;

            case Key.F4:
                ShowChunkBorders = !ShowChunkBorders;
                break;

            default:
                changed = false;
                break;
        }

        if (!changed)
        {
            return;
        }

        ApplyDebugVisualization();
        UpdateHud();

        GD.Print(
            $"Terrain diagnostics: mode={DebugViewMode}; " +
            $"grid={ShowWorldGrid}; wireframe={ShowWireframe}; " +
            $"borders={ShowChunkBorders}; spacing={DebugGridSpacing:F1} m");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
        {
            return;
        }

        if (_stressTestState != TerrainStressTestState.Idle)
        {
            UpdateTerrainStressTest(delta);
        }
        else if (_soakTestState != TerrainSoakTestState.Idle)
        {
            UpdateTerrainSoakTest(delta);
        }
        else
        {
            Vector2I nextCenter =
                CalculateHystereticCenter(_player.GlobalPosition);

            if (nextCenter != _currentChunk)
            {
                _currentChunk = nextCenter;
                PlanRefresh(executeImmediately: false);
            }
        }

        _hudUpdateAccumulator += delta;

        if (_hudUpdateAccumulator >= 0.10)
        {
            _hudUpdateAccumulator = 0.0;
            UpdateHud();
        }
    }

    private void ProcessOperationQueue()
    {
        _operationsCompletedLastStep = 0;
        int operationBudget = Math.Max(1, MaxOperationsPerStep);

        ApplyCompletedJobs(operationBudget);
        StartGenerationJobs();
        ExecuteReadyRemovals(operationBudget);

        if (IsRefreshComplete())
        {
            CompleteRefresh();
        }
        else
        {
            EnsureOperationTimerRunning();
            UpdateHud();
        }
    }

    private void ApplyCompletedJobs(int operationBudget)
    {
        DrainCompletedJobs();

        while (_operationsCompletedLastStep < operationBudget &&
            _jobApplyOrder.Count > 0)
        {
            long nextJobId = _jobApplyOrder.Peek();

            if (!_readyJobs.Remove(
                    nextJobId,
                    out ReadyChunkJob readyJob))
            {
                // Later jobs may already be ready, but apply order must match
                // the planned create/demotion/promotion sequence to avoid a
                // transient incompatible LOD border.
                break;
            }

            _jobApplyOrder.Dequeue();
            CompletedChunkJob completedJob = readyJob.CompletedJob;
            ActiveChunkJob activeJob = readyJob.ActiveJob;

            if (completedJob.IsCancelled)
            {
                _cancelledJobs++;
                _totalCancelledJobs++;
                continue;
            }

            if (completedJob.Error is not null)
            {
                _failedJobs++;
                _totalFailedJobs++;
                GD.PushError(
                    $"Terrain worker failed for " +
                    $"({activeJob.Operation.Coordinate.X}, " +
                    $"{activeJob.Operation.Coordinate.Y}): " +
                    completedJob.Error);
                continue;
            }

            if (completedJob.Result is null)
            {
                _failedJobs++;
                _totalFailedJobs++;
                GD.PushError(
                    "Terrain worker completed without a result.");
                continue;
            }

            GD.Print(
                $"Terrain worker: applying job={completedJob.JobId}; " +
                $"revision={completedJob.Revision}; " +
                $"type={activeJob.Operation.Type}; " +
                $"coordinate={activeJob.Operation.Coordinate}; " +
                $"worker={completedJob.Result.WorkerElapsedMilliseconds:F2} ms");
            ApplyCompletedGeneration(
                activeJob.Operation,
                completedJob.Result);
            _operationsCompletedLastStep++;
        }
    }

    private void DrainCompletedJobs()
    {
        while (_completedJobs.TryDequeue(out CompletedChunkJob completedJob))
        {
            if (!_activeJobs.Remove(
                    completedJob.JobId,
                    out ActiveChunkJob activeJob))
            {
                continue;
            }

            if (completedJob.Revision != _planRevision ||
                activeJob.Operation.Revision != _planRevision)
            {
                if (completedJob.Error is not null)
                {
                    _failedJobs++;
                    _totalFailedJobs++;
                    GD.PushError(
                        $"Terrain stale worker failed for " +
                        $"({activeJob.Operation.Coordinate.X}, " +
                        $"{activeJob.Operation.Coordinate.Y}); " +
                        $"jobRevision={completedJob.Revision}; " +
                        $"currentRevision={_planRevision}: " +
                        completedJob.Error);
                }

                _discardedStaleJobs++;
                _totalDiscardedStaleJobs++;
                GD.Print(
                    $"Terrain worker: discarded stale job={completedJob.JobId}; " +
                    $"jobRevision={completedJob.Revision}; " +
                    $"currentRevision={_planRevision}; " +
                    $"cancelled={completedJob.IsCancelled}");
                continue;
            }

            _readyJobs.Add(
                completedJob.JobId,
                new ReadyChunkJob(activeJob, completedJob));
        }
    }

    private void ApplyCompletedGeneration(
        ChunkOperation operation,
        TerrainChunkBuildResult result)
    {
        if (!_desiredSpecs.TryGetValue(
                operation.Coordinate,
                out ChunkSpec currentSpec))
        {
            return;
        }

        switch (operation.Type)
        {
            case ChunkOperationType.Create:
                if (_activeChunks.ContainsKey(operation.Coordinate))
                {
                    return;
                }

                TerrainChunk createdChunk = CreateChunk(
                    operation.Coordinate,
                    currentSpec);
                createdChunk.ApplyGeneratedData(result, "generated");
                _activeChunks.Add(operation.Coordinate, createdChunk);
                _completedCreates++;
                break;

            case ChunkOperationType.Update:
                if (!_activeChunks.TryGetValue(
                        operation.Coordinate,
                        out TerrainChunk? chunk))
                {
                    return;
                }

                ConfigureChunk(
                    chunk,
                    operation.Coordinate,
                    currentSpec);
                chunk.ApplyGeneratedData(result, "updated");
                _completedUpdates++;
                break;

            default:
                throw new InvalidOperationException(
                    "Only create and update operations produce worker results.");
        }
    }

    private void StartGenerationJobs()
    {
        while (_activeJobs.Count < _workerLimit &&
            _pendingOperations.Count > 0)
        {
            ChunkOperation nextOperation = _pendingOperations.Peek();

            if (nextOperation.Type == ChunkOperationType.Remove)
            {
                return;
            }

            _pendingOperations.Dequeue();

            if (!TryCreateBuildRequest(
                    nextOperation,
                    out TerrainChunkBuildRequest? request) ||
                request is null)
            {
                continue;
            }

            StartGenerationJob(nextOperation, request);
        }
    }

    private bool TryCreateBuildRequest(
        ChunkOperation operation,
        out TerrainChunkBuildRequest? request)
    {
        request = null;

        if (operation.Revision != _planRevision ||
            !_desiredSpecs.TryGetValue(
                operation.Coordinate,
                out ChunkSpec spec))
        {
            return false;
        }

        if (operation.Type == ChunkOperationType.Create &&
            _activeChunks.ContainsKey(operation.Coordinate))
        {
            return false;
        }

        if (operation.Type == ChunkOperationType.Update &&
            !_activeChunks.ContainsKey(operation.Coordinate))
        {
            return false;
        }

        bool rebuildCollision = operation.Type == ChunkOperationType.Create;

        if (operation.Type == ChunkOperationType.Update &&
            _activeChunks.TryGetValue(
                operation.Coordinate,
                out TerrainChunk? activeChunk))
        {
            rebuildCollision =
                activeChunk.GenerateCollision != spec.GenerateCollision ||
                activeChunk.CollisionResolution != CollisionResolution;
        }

        request = new TerrainChunkBuildRequest(
            operation.Coordinate.X,
            operation.Coordinate.Y,
            spec.LodLevel,
            spec.VisualResolution,
            CollisionResolution,
            ChunkSize,
            HeightScale,
            NoiseFrequency,
            NoiseSeed,
            SkirtDepth,
            spec.GenerateCollision,
            rebuildCollision,
            spec.StitchMask,
            spec.SkirtMask);
        return true;
    }

    private void StartGenerationJob(
        ChunkOperation operation,
        TerrainChunkBuildRequest request)
    {
        if (_planCancellation is null)
        {
            throw new InvalidOperationException(
                "Terrain generation plan has no cancellation source.");
        }

        long jobId = ++_nextJobId;
        CancellationToken cancellationToken = _planCancellation.Token;
        _activeJobs.Add(
            jobId,
            new ActiveChunkJob(jobId, operation, request));
        _jobApplyOrder.Enqueue(jobId);

        GD.Print(
            $"Terrain worker: started job={jobId}; " +
            $"revision={operation.Revision}; type={operation.Type}; " +
            $"coordinate={operation.Coordinate}; " +
            $"visual={request.VisualResolution}; " +
            $"collision={request.RebuildCollision}");

        _ = Task.Run(() =>
        {
            try
            {
                TerrainChunkBuildResult result =
                    TerrainChunkDataBuilder.Build(
                        request,
                        cancellationToken);
                _completedJobs.Enqueue(
                    CompletedChunkJob.Success(
                        jobId,
                        operation.Revision,
                        result));
            }
            catch (OperationCanceledException)
            {
                _completedJobs.Enqueue(
                    CompletedChunkJob.Cancelled(
                        jobId,
                        operation.Revision));
            }
            catch (Exception exception)
            {
                _completedJobs.Enqueue(
                    CompletedChunkJob.Failed(
                        jobId,
                        operation.Revision,
                        exception));
            }
        });
    }

    private void ExecuteReadyRemovals(int operationBudget)
    {
        if (_activeJobs.Count > 0 || _jobApplyOrder.Count > 0)
        {
            return;
        }

        while (_pendingOperations.Count > 0 &&
            _operationsCompletedLastStep < operationBudget &&
            _pendingOperations.Peek().Type == ChunkOperationType.Remove)
        {
            ExecuteRemove(_pendingOperations.Dequeue());
            _operationsCompletedLastStep++;
        }
    }

    private void ExecuteRemove(ChunkOperation operation)
    {
        if (operation.Revision != _planRevision ||
            _desiredSpecs.ContainsKey(operation.Coordinate) ||
            !_activeChunks.TryGetValue(
                operation.Coordinate,
                out TerrainChunk? chunk))
        {
            return;
        }

        chunk.ReleaseGeneratedResources();
        chunk.QueueFree();
        _activeChunks.Remove(operation.Coordinate);
        _completedRemovals++;
    }

    private bool IsRefreshComplete()
    {
        return _pendingOperations.Count == 0 &&
            _activeJobs.Count == 0 &&
            _jobApplyOrder.Count == 0 &&
            _readyJobs.Count == 0 &&
            _completedRevision != _planRevision;
    }

    private void EnsureOperationTimerRunning()
    {
        if (_operationTimer is null)
        {
            return;
        }

        _operationTimer.WaitTime = GetOperationInterval();

        if (_operationTimer.IsStopped())
        {
            _operationTimer.Start();
        }
    }

    private double GetOperationInterval()
    {
        return Math.Max(0.001, OperationIntervalSeconds);
    }

    private void PlanRefresh(bool executeImmediately)
    {
        int discardedReadyJobCount = _readyJobs.Count;
        int discardedReadyFailedJobCount = 0;

        foreach (ReadyChunkJob readyJob in _readyJobs.Values)
        {
            if (readyJob.CompletedJob.Error is not null)
            {
                discardedReadyFailedJobCount++;
                _totalFailedJobs++;
                GD.PushError(
                    $"Terrain ready worker result failed before revision " +
                    $"switch; job={readyJob.CompletedJob.JobId}; " +
                    $"jobRevision={readyJob.CompletedJob.Revision}; " +
                    $"nextRevision={_planRevision + 1}: " +
                    readyJob.CompletedJob.Error);
            }
        }

        // Cancel the previous revision, but do not dispose its source here:
        // in-flight workers may still be observing tokens created from it.
        // Once those tasks finish, the old source becomes unreachable and can
        // be collected normally.
        _planCancellation?.Cancel();
        _planCancellation = new CancellationTokenSource();
        _planRevision++;
        _pendingOperations.Clear();
        _jobApplyOrder.Clear();
        _readyJobs.Clear();
        _operationTimer?.Stop();
        _desiredSpecs = BuildDesiredSpecs();

        List<Vector2I> coordinatesByPriority = new(_desiredSpecs.Keys);
        coordinatesByPriority.Sort(CompareDesiredPriority);

        List<ChunkOperation> createOperations = new();
        List<ChunkOperation> demotionOperations = new();
        List<ChunkOperation> promotionOperations = new();
        List<ChunkOperation> neutralUpdateOperations = new();
        List<ChunkOperation> removeOperations = new();

        foreach (Vector2I coordinate in coordinatesByPriority)
        {
            ChunkSpec desiredSpec = _desiredSpecs[coordinate];

            if (!_activeChunks.TryGetValue(coordinate, out TerrainChunk? chunk))
            {
                createOperations.Add(ChunkOperation.Create(
                    _planRevision,
                    coordinate));
                continue;
            }

            if (chunk.RequiresDetailLevel(
                desiredSpec.LodLevel,
                desiredSpec.VisualResolution,
                desiredSpec.GenerateCollision,
                desiredSpec.StitchMask,
                desiredSpec.SkirtMask))
            {
                ChunkOperation updateOperation = ChunkOperation.Update(
                    _planRevision,
                    coordinate);

                if (chunk.LodLevel < desiredSpec.LodLevel)
                {
                    demotionOperations.Add(updateOperation);
                }
                else if (chunk.LodLevel > desiredSpec.LodLevel)
                {
                    promotionOperations.Add(updateOperation);
                }
                else
                {
                    neutralUpdateOperations.Add(updateOperation);
                }
            }
        }

        List<Vector2I> coordinatesToRemove = new();

        foreach (Vector2I coordinate in _activeChunks.Keys)
        {
            if (!_desiredSpecs.ContainsKey(coordinate))
            {
                coordinatesToRemove.Add(coordinate);
            }
        }

        coordinatesToRemove.Sort((first, second) =>
            ChebyshevDistance(second, _currentChunk)
                .CompareTo(ChebyshevDistance(first, _currentChunk)));

        foreach (Vector2I coordinate in coordinatesToRemove)
        {
            removeOperations.Add(ChunkOperation.Remove(
                _planRevision,
                coordinate));
        }

        // Incoming chunks are generated first. Demotions precede promotions so
        // no temporary high/high border appears without the required stitching.
        // Outgoing chunks remain visible until every create/update result has
        // been applied on the main thread.
        EnqueueOperations(createOperations);
        EnqueueOperations(demotionOperations);
        EnqueueOperations(promotionOperations);
        EnqueueOperations(neutralUpdateOperations);
        EnqueueOperations(removeOperations);

        _plannedCreates = createOperations.Count;
        _plannedUpdates = demotionOperations.Count +
            promotionOperations.Count + neutralUpdateOperations.Count;
        _plannedRemovals = removeOperations.Count;
        _completedCreates = 0;
        _completedUpdates = 0;
        _completedRemovals = 0;
        _failedJobs = discardedReadyFailedJobCount;
        _cancelledJobs = 0;
        _discardedStaleJobs = discardedReadyJobCount;
        _totalDiscardedStaleJobs += discardedReadyJobCount;

        if (discardedReadyJobCount > 0)
        {
            GD.Print(
                $"Terrain worker: discarded {discardedReadyJobCount} " +
                "ready result(s) from the previous revision.");
        }

        GD.Print(
            $"TerrainChunkManager: planned center={_currentChunk}; " +
            $"create={_plannedCreates}; update={_plannedUpdates}; " +
            $"remove={_plannedRemovals}; queued={_pendingOperations.Count}; " +
            $"workers={_workerLimit}; revision={_planRevision}");

        // Even the initial load is asynchronous. This call only starts worker
        // jobs; all Node, mesh and collision changes remain on the main thread.
        _ = executeImmediately;
        ProcessOperationQueue();
    }

    private void EnqueueOperations(IEnumerable<ChunkOperation> operations)
    {
        foreach (ChunkOperation operation in operations)
        {
            _pendingOperations.Enqueue(operation);
        }
    }

    private void CompleteRefresh()
    {
        _operationTimer?.Stop();
        _completedRevision = _planRevision;
        CountLodChunks(out int highDetailCount, out int lowDetailCount);

        GD.Print(
            $"TerrainChunkManager: completed center={_currentChunk}; " +
            $"active={_activeChunks.Count}; high={highDetailCount}; " +
            $"low={lowDetailCount}; created={_completedCreates}; " +
            $"updated={_completedUpdates}; removed={_completedRemovals}; " +
            $"cancelled={_cancelledJobs}; stale={_discardedStaleJobs}; " +
            $"failed={_failedJobs}; " +
            $"revision={_completedRevision}");

        UpdateHud();
    }

    private Dictionary<Vector2I, ChunkSpec> BuildDesiredSpecs()
    {
        int activeRadius = Math.Max(1, ActiveRadius);
        int highDetailRadius = Math.Clamp(
            HighDetailRadius,
            0,
            activeRadius);
        Dictionary<Vector2I, ChunkSpec> specs = new();
        List<Vector2I> coordinates = new();

        for (int offsetZ = -activeRadius; offsetZ <= activeRadius; offsetZ++)
        {
            for (int offsetX = -activeRadius; offsetX <= activeRadius; offsetX++)
            {
                Vector2I coordinate =
                    _currentChunk + new Vector2I(offsetX, offsetZ);
                int distance = ChebyshevDistance(coordinate, _currentChunk);
                int lodLevel = distance <= highDetailRadius ? 0 : 1;
                int visualResolution = lodLevel == 0
                    ? HighDetailResolution
                    : LowDetailResolution;
                bool generateCollision = distance <= CollisionRadius;

                specs.Add(
                    coordinate,
                    new ChunkSpec(
                        lodLevel,
                        visualResolution,
                        generateCollision,
                        TerrainEdgeStitchMask.None,
                        TerrainEdgeStitchMask.None));
                coordinates.Add(coordinate);
            }
        }

        foreach (Vector2I coordinate in coordinates)
        {
            ChunkSpec spec = specs[coordinate];
            TerrainEdgeStitchMask stitchMask = DetermineStitchMask(
                coordinate,
                spec.LodLevel,
                specs);
            TerrainEdgeStitchMask skirtMask = DetermineSkirtMask(
                coordinate,
                specs);
            specs[coordinate] = spec.WithEdgeMasks(
                stitchMask,
                skirtMask);
        }

        return specs;
    }

    private static TerrainEdgeStitchMask DetermineStitchMask(
        Vector2I coordinate,
        int lodLevel,
        IReadOnlyDictionary<Vector2I, ChunkSpec> specs)
    {
        TerrainEdgeStitchMask mask = TerrainEdgeStitchMask.None;

        AddStitchIfNeighborIsLowerDetail(
            coordinate + Vector2I.Up,
            lodLevel,
            TerrainEdgeStitchMask.North,
            specs,
            ref mask);
        AddStitchIfNeighborIsLowerDetail(
            coordinate + Vector2I.Right,
            lodLevel,
            TerrainEdgeStitchMask.East,
            specs,
            ref mask);
        AddStitchIfNeighborIsLowerDetail(
            coordinate + Vector2I.Down,
            lodLevel,
            TerrainEdgeStitchMask.South,
            specs,
            ref mask);
        AddStitchIfNeighborIsLowerDetail(
            coordinate + Vector2I.Left,
            lodLevel,
            TerrainEdgeStitchMask.West,
            specs,
            ref mask);

        return mask;
    }


    private static TerrainEdgeStitchMask DetermineSkirtMask(
        Vector2I coordinate,
        IReadOnlyDictionary<Vector2I, ChunkSpec> specs)
    {
        TerrainEdgeStitchMask mask = TerrainEdgeStitchMask.None;

        if (!specs.ContainsKey(coordinate + Vector2I.Up))
        {
            mask |= TerrainEdgeStitchMask.North;
        }

        if (!specs.ContainsKey(coordinate + Vector2I.Right))
        {
            mask |= TerrainEdgeStitchMask.East;
        }

        if (!specs.ContainsKey(coordinate + Vector2I.Down))
        {
            mask |= TerrainEdgeStitchMask.South;
        }

        if (!specs.ContainsKey(coordinate + Vector2I.Left))
        {
            mask |= TerrainEdgeStitchMask.West;
        }

        return mask;
    }

    private static void AddStitchIfNeighborIsLowerDetail(
        Vector2I neighborCoordinate,
        int lodLevel,
        TerrainEdgeStitchMask edge,
        IReadOnlyDictionary<Vector2I, ChunkSpec> specs,
        ref TerrainEdgeStitchMask mask)
    {
        if (specs.TryGetValue(
                neighborCoordinate,
                out ChunkSpec neighborSpec) &&
            neighborSpec.LodLevel > lodLevel)
        {
            mask |= edge;
        }
    }

    private TerrainChunk CreateChunk(
        Vector2I coordinate,
        ChunkSpec spec)
    {
        TerrainChunk chunk = new()
        {
            Name = $"TerrainChunk_{coordinate.X}_{coordinate.Y}",
            Position = new Vector3(
                coordinate.X * ChunkSize,
                0.0f,
                coordinate.Y * ChunkSize)
        };

        ConfigureChunk(chunk, coordinate, spec);

        chunk.AddChild(new MeshInstance3D
        {
            Name = "MeshInstance3D"
        });
        chunk.AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D"
        });

        AddChild(chunk);
        return chunk;
    }

    private void ConfigureChunk(
        TerrainChunk chunk,
        Vector2I coordinate,
        ChunkSpec spec)
    {
        chunk.Configure(
            coordinate.X,
            coordinate.Y,
            spec.LodLevel,
            spec.VisualResolution,
            CollisionResolution,
            ChunkSize,
            HeightScale,
            NoiseFrequency,
            NoiseSeed,
            SkirtDepth,
            spec.GenerateCollision,
            spec.StitchMask,
            spec.SkirtMask,
            DebugViewMode,
            ShowWorldGrid,
            ShowWireframe,
            ShowChunkBorders,
            DebugGridSpacing);
    }

    private Vector2I CalculateHystereticCenter(Vector3 worldPosition)
    {
        int chunkX = _currentChunk.X;
        int chunkZ = _currentChunk.Y;
        float halfSize = ChunkSize * 0.5f;
        float hysteresis = Math.Clamp(
            ChunkSwitchHysteresis,
            0.0f,
            Math.Max(0.0f, halfSize - 0.25f));
        float switchDistance = halfSize + hysteresis;

        while (worldPosition.X - (chunkX * ChunkSize) > switchDistance)
        {
            chunkX++;
        }

        while (worldPosition.X - (chunkX * ChunkSize) < -switchDistance)
        {
            chunkX--;
        }

        while (worldPosition.Z - (chunkZ * ChunkSize) > switchDistance)
        {
            chunkZ++;
        }

        while (worldPosition.Z - (chunkZ * ChunkSize) < -switchDistance)
        {
            chunkZ--;
        }

        return new Vector2I(chunkX, chunkZ);
    }

    private Vector2I WorldToChunkWithoutHysteresis(Vector3 worldPosition)
    {
        float halfSize = ChunkSize * 0.5f;
        int chunkX = Mathf.FloorToInt((worldPosition.X + halfSize) / ChunkSize);
        int chunkZ = Mathf.FloorToInt((worldPosition.Z + halfSize) / ChunkSize);
        return new Vector2I(chunkX, chunkZ);
    }

    private int CompareDesiredPriority(Vector2I first, Vector2I second)
    {
        int firstDistance = ChebyshevDistance(first, _currentChunk);
        int secondDistance = ChebyshevDistance(second, _currentChunk);
        int distanceComparison = firstDistance.CompareTo(secondDistance);

        if (distanceComparison != 0)
        {
            return distanceComparison;
        }

        if (_player is not null)
        {
            Vector2 velocity = new(_player.Velocity.X, _player.Velocity.Z);

            if (velocity.LengthSquared() > 0.01f)
            {
                Vector2 direction = velocity.Normalized();
                Vector2 firstOffset = new(
                    first.X - _currentChunk.X,
                    first.Y - _currentChunk.Y);
                Vector2 secondOffset = new(
                    second.X - _currentChunk.X,
                    second.Y - _currentChunk.Y);
                float firstForwardScore = firstOffset.Dot(direction);
                float secondForwardScore = secondOffset.Dot(direction);
                int directionComparison =
                    secondForwardScore.CompareTo(firstForwardScore);

                if (directionComparison != 0)
                {
                    return directionComparison;
                }
            }
        }

        int zComparison = first.Y.CompareTo(second.Y);
        return zComparison != 0
            ? zComparison
            : first.X.CompareTo(second.X);
    }

    private void CountLodChunks(
        out int highDetailCount,
        out int lowDetailCount)
    {
        highDetailCount = 0;
        lowDetailCount = 0;

        foreach (TerrainChunk chunk in _activeChunks.Values)
        {
            if (chunk.LodLevel == 0)
            {
                highDetailCount++;
            }
            else
            {
                lowDetailCount++;
            }
        }
    }


    private void StartTerrainStressTest()
    {
        if (_player is null)
        {
            GD.PushError("Terrain async stress test requires a player node.");
            return;
        }

        if (_stressTestState != TerrainStressTestState.Idle)
        {
            GD.Print("Terrain async stress test is already running.");
            return;
        }

        if (_soakTestState != TerrainSoakTestState.Idle)
        {
            GD.Print(
                "Terrain async stress test cannot start while TASK-026 " +
                "soak test is running.");
            return;
        }

        _stressTestOriginalPlayerPosition = _player.GlobalPosition;
        _stressTestOriginalChunk = _currentChunk;
        _stressTestPlayerPhysicsWasEnabled = _player.IsPhysicsProcessing();
        _player.SetPhysicsProcess(false);
        _player.Velocity = Vector3.Zero;
        _stressTestElapsedSeconds = 0.0;
        _stressTestStepAccumulator = 0.0;
        _stressTestNextCenterIndex = 0;
        _stressTestCenters.Clear();
        _stressTestStatus = "подготовка: ожидание пустых очередей";
        _stressTestState = TerrainStressTestState.WaitingForInitialIdle;

        GD.Print(
            "Terrain async stress test: requested; waiting for the current " +
            "streaming revision to become idle.");
        UpdateHud();
    }

    private void UpdateTerrainStressTest(double delta)
    {
        if (_player is null)
        {
            FailTerrainStressTest("player node was lost");
            return;
        }

        _stressTestElapsedSeconds += delta;

        if (_stressTestElapsedSeconds >
            Math.Max(5.0, StressTestTimeoutSeconds))
        {
            FailTerrainStressTest(
                $"timeout after {_stressTestElapsedSeconds:F1} s; " +
                GetQueueSnapshot());
            return;
        }

        switch (_stressTestState)
        {
            case TerrainStressTestState.WaitingForInitialIdle:
                _player.GlobalPosition = _stressTestOriginalPlayerPosition;
                _player.Velocity = Vector3.Zero;

                if (IsStreamingIdle())
                {
                    BeginTerrainStressRevisions();
                }
                break;

            case TerrainStressTestState.IssuingRevisions:
                _stressTestStepAccumulator += delta;

                if (_stressTestNextCenterIndex < _stressTestCenters.Count &&
                    _stressTestStepAccumulator >=
                    Math.Max(0.01, StressTestStepIntervalSeconds))
                {
                    _stressTestStepAccumulator = 0.0;
                    ForceStressTestCenter(
                        _stressTestCenters[_stressTestNextCenterIndex]);
                    _stressTestNextCenterIndex++;
                    _stressTestStatus =
                        $"RUNNING: revision " +
                        $"{_stressTestNextCenterIndex}/" +
                        $"{_stressTestCenters.Count}";
                }

                if (_stressTestNextCenterIndex >= _stressTestCenters.Count)
                {
                    _stressTestState =
                        TerrainStressTestState.WaitingForFinalIdle;
                    _stressTestStatus =
                        "RUNNING: ожидание workers=0 и queue=0";
                }
                break;

            case TerrainStressTestState.WaitingForFinalIdle:
                _player.GlobalPosition = _stressTestOriginalPlayerPosition;
                _player.Velocity = Vector3.Zero;

                if (IsStreamingIdle())
                {
                    CompleteTerrainStressTest();
                }
                break;
        }
    }

    private void BeginTerrainStressRevisions()
    {
        if (_player is null)
        {
            FailTerrainStressTest("player node was lost before start");
            return;
        }

        Vector2I origin = _stressTestOriginalChunk;
        int requestedRevisionCount = Math.Clamp(
            StressTestRevisionCount,
            4,
            32);

        for (int index = 0; index < requestedRevisionCount; index++)
        {
            int ring = 1 + (index / 8);
            int positionOnRing = index % 8;
            Vector2I offset = positionOnRing switch
            {
                0 => new Vector2I(ring, 0),
                1 => new Vector2I(ring, ring),
                2 => new Vector2I(0, ring),
                3 => new Vector2I(-ring, ring),
                4 => new Vector2I(-ring, 0),
                5 => new Vector2I(-ring, -ring),
                6 => new Vector2I(0, -ring),
                _ => new Vector2I(ring, -ring)
            };
            _stressTestCenters.Add(origin + offset);
        }

        // The final revision always returns to the exact starting chunk so the
        // test validates a deterministic final active set and leaves the scene
        // where the operator started it.
        _stressTestCenters.Add(origin);
        _stressTestStartingRevision = _planRevision;
        _stressTestBaselineFailedJobs = _totalFailedJobs;
        _stressTestBaselineCancelledJobs = _totalCancelledJobs;
        _stressTestBaselineStaleJobs = _totalDiscardedStaleJobs;
        _stressTestStepAccumulator =
            Math.Max(0.01, StressTestStepIntervalSeconds);
        _stressTestState = TerrainStressTestState.IssuingRevisions;
        _stressTestStatus =
            $"RUNNING: revision 0/{_stressTestCenters.Count}";

        GD.Print(
            $"Terrain async stress test: started at center={origin}; " +
            $"forcedRevisions={_stressTestCenters.Count}; " +
            $"interval={Math.Max(0.01, StressTestStepIntervalSeconds):F2} s; " +
            $"timeout={Math.Max(5.0, StressTestTimeoutSeconds):F1} s");
    }

    private void ForceStressTestCenter(Vector2I center)
    {
        if (_player is null)
        {
            return;
        }

        _currentChunk = center;
        _player.GlobalPosition = new Vector3(
            center.X * ChunkSize,
            _stressTestOriginalPlayerPosition.Y,
            center.Y * ChunkSize);
        _player.Velocity = Vector3.Zero;
        PlanRefresh(executeImmediately: false);
    }

    private void CompleteTerrainStressTest()
    {
        int failedDelta =
            _totalFailedJobs - _stressTestBaselineFailedJobs;
        int cancelledDelta =
            _totalCancelledJobs - _stressTestBaselineCancelledJobs;
        int staleDelta =
            _totalDiscardedStaleJobs - _stressTestBaselineStaleJobs;
        int revisionDelta = _planRevision - _stressTestStartingRevision;

        if (!TryValidateTerrainStressResult(
                failedDelta,
                cancelledDelta,
                staleDelta,
                revisionDelta,
                out string failureReason))
        {
            FailTerrainStressTest(failureReason, restoreWithReplan: false);
            return;
        }

        _stressTestStatus =
            $"PASS: rev={revisionDelta}, cancel={cancelledDelta}, " +
            $"stale={staleDelta}, {_activeChunks.Count}/" +
            $"{_desiredSpecs.Count}, queue=0, workers=0";

        GD.Print(
            $"Terrain async stress test: PASS; revisions={revisionDelta}; " +
            $"cancelled={cancelledDelta}; stale={staleDelta}; " +
            $"failed={failedDelta}; active={_activeChunks.Count}/" +
            $"{_desiredSpecs.Count}; {GetQueueSnapshot()}; " +
            $"elapsed={_stressTestElapsedSeconds:F2} s");
        RestorePlayerAfterTerrainStressTest(replan: false);
    }

    private bool TryValidateTerrainStressResult(
        int failedDelta,
        int cancelledDelta,
        int staleDelta,
        int revisionDelta,
        out string failureReason)
    {
        if (!IsStreamingIdle())
        {
            failureReason = "streaming did not become idle; " +
                GetQueueSnapshot();
            return false;
        }

        if (failedDelta != 0)
        {
            failureReason = $"worker failures detected: {failedDelta}";
            return false;
        }

        if (cancelledDelta + staleDelta <= 0)
        {
            failureReason =
                "rapid revisions produced neither cancellation nor stale " +
                "results; increase resolution or reduce stress interval";
            return false;
        }

        if (revisionDelta < _stressTestCenters.Count)
        {
            failureReason =
                $"expected at least {_stressTestCenters.Count} revisions, " +
                $"observed {revisionDelta}";
            return false;
        }

        if (_activeChunks.Count != _desiredSpecs.Count)
        {
            failureReason =
                $"active set mismatch: {_activeChunks.Count}/" +
                $"{_desiredSpecs.Count}";
            return false;
        }

        foreach (KeyValuePair<Vector2I, ChunkSpec> entry in _desiredSpecs)
        {
            if (!_activeChunks.TryGetValue(
                    entry.Key,
                    out TerrainChunk? chunk))
            {
                failureReason = $"missing final chunk {entry.Key}";
                return false;
            }

            ChunkSpec spec = entry.Value;

            if (chunk.RequiresDetailLevel(
                    spec.LodLevel,
                    spec.VisualResolution,
                    spec.GenerateCollision,
                    spec.StitchMask,
                    spec.SkirtMask))
            {
                failureReason =
                    $"final chunk {entry.Key} does not match the latest " +
                    "revision";
                return false;
            }
        }

        foreach (Vector2I coordinate in _activeChunks.Keys)
        {
            if (!_desiredSpecs.ContainsKey(coordinate))
            {
                failureReason =
                    $"unexpected stale chunk remained active: {coordinate}";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    private void FailTerrainStressTest(
        string reason,
        bool restoreWithReplan = true)
    {
        _stressTestStatus = "FAIL: " + reason;
        GD.PushError(
            $"Terrain async stress test: FAIL; reason={reason}; " +
            $"{GetQueueSnapshot()}; elapsed={_stressTestElapsedSeconds:F2} s");
        RestorePlayerAfterTerrainStressTest(restoreWithReplan);
    }

    private void RestorePlayerAfterTerrainStressTest(bool replan)
    {
        if (_player is not null)
        {
            _player.GlobalPosition = _stressTestOriginalPlayerPosition;
            _player.Velocity = Vector3.Zero;
            _player.SetPhysicsProcess(_stressTestPlayerPhysicsWasEnabled);

            if (replan)
            {
                Vector2I restoredCenter = _stressTestOriginalChunk;

                if (restoredCenter != _currentChunk || !IsStreamingIdle())
                {
                    _currentChunk = restoredCenter;
                    PlanRefresh(executeImmediately: false);
                }
            }
        }

        _stressTestState = TerrainStressTestState.Idle;
        UpdateHud();
    }

    private void StartTerrainSoakTest()
    {
        if (_player is null)
        {
            GD.PushError("Terrain soak test requires a player node.");
            return;
        }

        if (_stressTestState != TerrainStressTestState.Idle)
        {
            GD.Print(
                "Terrain soak test cannot start while TASK-025 stress test " +
                "is running.");
            return;
        }

        if (_soakTestState != TerrainSoakTestState.Idle)
        {
            GD.Print("Terrain soak test is already running.");
            return;
        }

        _soakTestOriginalPlayerPosition = _player.GlobalPosition;
        _soakTestOriginalChunk = _currentChunk;
        _soakTestPlayerPhysicsWasEnabled = _player.IsPhysicsProcessing();
        _player.SetPhysicsProcess(false);
        _player.Velocity = Vector3.Zero;
        _soakTestElapsedSeconds = 0.0;
        _soakTestDwellAccumulator = 0.0;
        _soakTestTransitionElapsedSeconds = 0.0;
        _soakTestNextCenterIndex = 0;
        _soakTestCompletedMoves = 0;
        _soakTestIdleSamples = 0;
        _soakTestSampleCapturedForCurrentIdle = false;
        _soakTestPeakActiveChunks = _activeChunks.Count;
        _soakTestPeakQueuedWork = GetQueuedWorkCount();
        _soakTestPeakWorkers = _activeJobs.Count;
        _soakTestLastMeshCount = 0;
        _soakTestLastCollisionCount = 0;
        _soakTestLastVertexCount = 0;
        _soakTestRoute.Clear();
        _soakTestStatus = "подготовка: ожидание пустых очередей";
        _soakTestState = TerrainSoakTestState.WaitingForInitialIdle;

        GD.Print(
            "Terrain soak test: requested; waiting for the current " +
            "streaming revision to become idle.");
        UpdateHud();
    }

    private void UpdateTerrainSoakTest(double delta)
    {
        if (_player is null)
        {
            FailTerrainSoakTest("player node was lost");
            return;
        }

        _soakTestElapsedSeconds += delta;
        _soakTestTransitionElapsedSeconds += delta;
        UpdateTerrainSoakPeaks();

        double transitionTimeout =
            Math.Max(5.0, SoakTestTransitionTimeoutSeconds);

        if (_soakTestTransitionElapsedSeconds > transitionTimeout &&
            !IsStreamingIdle())
        {
            FailTerrainSoakTest(
                $"streaming transition timeout after " +
                $"{_soakTestTransitionElapsedSeconds:F1} s; " +
                GetQueueSnapshot());
            return;
        }

        switch (_soakTestState)
        {
            case TerrainSoakTestState.WaitingForInitialIdle:
                _player.GlobalPosition = _soakTestOriginalPlayerPosition;
                _player.Velocity = Vector3.Zero;

                if (IsStreamingIdle())
                {
                    BeginTerrainSoakTest();
                }
                break;

            case TerrainSoakTestState.Running:
                if (!IsStreamingIdle())
                {
                    _soakTestDwellAccumulator = 0.0;
                    _soakTestSampleCapturedForCurrentIdle = false;
                    break;
                }

                if (!_soakTestSampleCapturedForCurrentIdle)
                {
                    if (!TryCaptureTerrainSoakSample(
                            out string failureReason))
                    {
                        FailTerrainSoakTest(failureReason);
                        return;
                    }

                    _soakTestSampleCapturedForCurrentIdle = true;
                }

                _soakTestDwellAccumulator += delta;

                if (_soakTestElapsedSeconds >=
                    Math.Max(30.0, SoakTestDurationSeconds))
                {
                    ReturnTerrainSoakTestToOrigin();
                    return;
                }

                if (_soakTestDwellAccumulator >=
                    Math.Max(0.10, SoakTestDwellSeconds))
                {
                    _soakTestDwellAccumulator = 0.0;
                    Vector2I nextCenter =
                        _soakTestRoute[_soakTestNextCenterIndex];
                    _soakTestNextCenterIndex =
                        (_soakTestNextCenterIndex + 1) %
                        _soakTestRoute.Count;
                    ForceSoakTestCenter(nextCenter);
                    _soakTestCompletedMoves++;
                    _soakTestSampleCapturedForCurrentIdle = false;
                    _soakTestTransitionElapsedSeconds = 0.0;
                    _soakTestStatus =
                        $"RUNNING: {_soakTestElapsedSeconds:F0}/" +
                        $"{Math.Max(30.0, SoakTestDurationSeconds):F0} s, " +
                        $"moves={_soakTestCompletedMoves}, " +
                        $"mem={BytesToMegabytes(GC.GetTotalMemory(false)):F1} MB";
                }
                break;

            case TerrainSoakTestState.ReturningToOrigin:
                _player.GlobalPosition = _soakTestOriginalPlayerPosition;
                _player.Velocity = Vector3.Zero;

                if (IsStreamingIdle())
                {
                    CompleteTerrainSoakTest();
                }
                break;
        }
    }

    private void BeginTerrainSoakTest()
    {
        if (_player is null)
        {
            FailTerrainSoakTest("player node was lost before start");
            return;
        }

        BuildTerrainSoakRoute(
            _soakTestOriginalChunk,
            Math.Clamp(SoakTestRouteRadius, 1, 12));

        if (_soakTestRoute.Count == 0)
        {
            FailTerrainSoakTest("deterministic route is empty");
            return;
        }

        // The configured duration measures the active route, not the time
        // spent waiting for an already-running revision to become idle.
        _soakTestElapsedSeconds = 0.0;
        _soakTestStartingRevision = _planRevision;
        _soakTestBaselineFailedJobs = _totalFailedJobs;
        _soakTestBaselineCancelledJobs = _totalCancelledJobs;
        _soakTestBaselineStaleJobs = _totalDiscardedStaleJobs;
        _soakTestBaselineManagedBytes = GC.GetTotalMemory(true);
        _soakTestPeakManagedBytes = _soakTestBaselineManagedBytes;
        _soakTestTransitionElapsedSeconds = 0.0;
        _soakTestState = TerrainSoakTestState.Running;
        _soakTestStatus =
            $"RUNNING: 0/{Math.Max(30.0, SoakTestDurationSeconds):F0} s";

        if (!TryCaptureTerrainSoakSample(out string failureReason))
        {
            FailTerrainSoakTest(failureReason);
            return;
        }

        _soakTestSampleCapturedForCurrentIdle = true;

        GD.Print(
            $"Terrain soak test: started at center=" +
            $"{_soakTestOriginalChunk}; duration=" +
            $"{Math.Max(30.0, SoakTestDurationSeconds):F0} s; " +
            $"routePoints={_soakTestRoute.Count}; " +
            $"dwell={Math.Max(0.10, SoakTestDwellSeconds):F2} s; " +
            $"baselineManaged=" +
            $"{BytesToMegabytes(_soakTestBaselineManagedBytes):F2} MB");
    }

    private void BuildTerrainSoakRoute(Vector2I origin, int radius)
    {
        _soakTestRoute.Clear();

        for (int x = 1; x <= radius; x++)
        {
            _soakTestRoute.Add(origin + new Vector2I(x, 0));
        }

        for (int z = 1; z <= radius; z++)
        {
            _soakTestRoute.Add(origin + new Vector2I(radius, z));
        }

        for (int x = radius - 1; x >= -radius; x--)
        {
            _soakTestRoute.Add(origin + new Vector2I(x, radius));
        }

        for (int z = radius - 1; z >= -radius; z--)
        {
            _soakTestRoute.Add(origin + new Vector2I(-radius, z));
        }

        for (int x = -radius + 1; x <= radius; x++)
        {
            _soakTestRoute.Add(origin + new Vector2I(x, -radius));
        }

        for (int z = -radius + 1; z <= 0; z++)
        {
            _soakTestRoute.Add(origin + new Vector2I(radius, z));
        }

        for (int x = radius - 1; x >= 0; x--)
        {
            _soakTestRoute.Add(origin + new Vector2I(x, 0));
        }
    }

    private void ForceSoakTestCenter(Vector2I center)
    {
        if (_player is null)
        {
            return;
        }

        _currentChunk = center;
        _player.GlobalPosition = new Vector3(
            center.X * ChunkSize,
            _soakTestOriginalPlayerPosition.Y,
            center.Y * ChunkSize);
        _player.Velocity = Vector3.Zero;
        PlanRefresh(executeImmediately: false);
    }

    private bool TryCaptureTerrainSoakSample(out string failureReason)
    {
        if (!IsStreamingIdle())
        {
            failureReason = "sample requested while streaming is active";
            return false;
        }

        if (!TryGetTerrainResourceSnapshot(
                out int meshCount,
                out int collisionCount,
                out int vertexCount,
                out failureReason))
        {
            return false;
        }

        _soakTestLastMeshCount = meshCount;
        _soakTestLastCollisionCount = collisionCount;
        _soakTestLastVertexCount = vertexCount;
        _soakTestIdleSamples++;
        long managedBytes = GC.GetTotalMemory(false);
        _soakTestPeakManagedBytes =
            Math.Max(_soakTestPeakManagedBytes, managedBytes);
        failureReason = string.Empty;
        return true;
    }

    private bool TryGetTerrainResourceSnapshot(
        out int meshCount,
        out int collisionCount,
        out int vertexCount,
        out string failureReason)
    {
        meshCount = 0;
        collisionCount = 0;
        vertexCount = 0;
        int expectedCollisionCount = 0;

        if (_activeChunks.Count != _desiredSpecs.Count)
        {
            failureReason =
                $"active set mismatch: {_activeChunks.Count}/" +
                $"{_desiredSpecs.Count}";
            return false;
        }

        foreach (KeyValuePair<Vector2I, ChunkSpec> entry in _desiredSpecs)
        {
            if (!_activeChunks.TryGetValue(entry.Key, out TerrainChunk? chunk))
            {
                failureReason = $"missing chunk {entry.Key}";
                return false;
            }

            if (chunk.RequiresDetailLevel(
                    entry.Value.LodLevel,
                    entry.Value.VisualResolution,
                    entry.Value.GenerateCollision,
                    entry.Value.StitchMask,
                    entry.Value.SkirtMask))
            {
                failureReason =
                    $"chunk {entry.Key} does not match current specification";
                return false;
            }

            if (!chunk.HasGeneratedVisualMesh ||
                chunk.TopSurfaceVertexCount <= 0)
            {
                failureReason =
                    $"chunk {entry.Key} has no generated visual mesh";
                return false;
            }

            meshCount++;
            vertexCount += chunk.TopSurfaceVertexCount;

            if (entry.Value.GenerateCollision)
            {
                expectedCollisionCount++;

                if (!chunk.HasGeneratedCollisionShape)
                {
                    failureReason =
                        $"chunk {entry.Key} has no generated collision shape";
                    return false;
                }
            }
            else if (chunk.HasGeneratedCollisionShape)
            {
                failureReason =
                    $"chunk {entry.Key} retained collision outside the " +
                    "collision radius";
                return false;
            }

            if (chunk.HasGeneratedCollisionShape)
            {
                collisionCount++;
            }
        }

        if (meshCount != _desiredSpecs.Count)
        {
            failureReason =
                $"mesh count mismatch: {meshCount}/{_desiredSpecs.Count}";
            return false;
        }

        if (collisionCount != expectedCollisionCount)
        {
            failureReason =
                $"collision count mismatch: {collisionCount}/" +
                $"{expectedCollisionCount}";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private void UpdateTerrainSoakPeaks()
    {
        _soakTestPeakActiveChunks =
            Math.Max(_soakTestPeakActiveChunks, _activeChunks.Count);
        _soakTestPeakQueuedWork =
            Math.Max(_soakTestPeakQueuedWork, GetQueuedWorkCount());
        _soakTestPeakWorkers =
            Math.Max(_soakTestPeakWorkers, _activeJobs.Count);
        _soakTestPeakManagedBytes = Math.Max(
            _soakTestPeakManagedBytes,
            GC.GetTotalMemory(false));
    }

    private void ReturnTerrainSoakTestToOrigin()
    {
        _soakTestState = TerrainSoakTestState.ReturningToOrigin;
        _soakTestStatus = "RUNNING: возврат и финальная стабилизация";
        _soakTestSampleCapturedForCurrentIdle = false;
        _soakTestTransitionElapsedSeconds = 0.0;
        ForceSoakTestCenter(_soakTestOriginalChunk);
    }

    private void CompleteTerrainSoakTest()
    {
        if (!TryCaptureTerrainSoakSample(out string failureReason))
        {
            FailTerrainSoakTest(failureReason, restoreWithReplan: false);
            return;
        }

        int failedDelta =
            _totalFailedJobs - _soakTestBaselineFailedJobs;
        int cancelledDelta =
            _totalCancelledJobs - _soakTestBaselineCancelledJobs;
        int staleDelta =
            _totalDiscardedStaleJobs - _soakTestBaselineStaleJobs;
        int revisionDelta = _planRevision - _soakTestStartingRevision;
        long finalManagedBytes = GC.GetTotalMemory(true);
        long memoryGrowthBytes =
            finalManagedBytes - _soakTestBaselineManagedBytes;
        long allowedGrowthBytes = (long)(
            Math.Max(8.0, SoakTestAllowedManagedMemoryGrowthMegabytes) *
            1024.0 * 1024.0);

        if (failedDelta != 0)
        {
            FailTerrainSoakTest(
                $"worker failures detected: {failedDelta}",
                restoreWithReplan: false);
            return;
        }

        if (_soakTestCompletedMoves < 4 || _soakTestIdleSamples < 5)
        {
            FailTerrainSoakTest(
                $"insufficient coverage: moves={_soakTestCompletedMoves}; " +
                $"samples={_soakTestIdleSamples}",
                restoreWithReplan: false);
            return;
        }

        if (memoryGrowthBytes > allowedGrowthBytes)
        {
            FailTerrainSoakTest(
                $"managed memory growth " +
                $"{BytesToMegabytes(memoryGrowthBytes):F1} MB exceeds " +
                $"limit {BytesToMegabytes(allowedGrowthBytes):F1} MB",
                restoreWithReplan: false);
            return;
        }

        _soakTestStatus =
            $"PASS: {_soakTestElapsedSeconds:F0} s, " +
            $"moves={_soakTestCompletedMoves}, " +
            $"memΔ={BytesToMegabytes(memoryGrowthBytes):F1} MB, " +
            $"mesh={_soakTestLastMeshCount}, " +
            $"collision={_soakTestLastCollisionCount}";

        GD.Print(
            $"Terrain soak test: PASS; duration=" +
            $"{_soakTestElapsedSeconds:F2} s; moves={_soakTestCompletedMoves}; " +
            $"idleSamples={_soakTestIdleSamples}; revisions={revisionDelta}; " +
            $"cancelled={cancelledDelta}; stale={staleDelta}; " +
            $"failed={failedDelta}; active={_activeChunks.Count}/" +
            $"{_desiredSpecs.Count}; meshes={_soakTestLastMeshCount}; " +
            $"collisions={_soakTestLastCollisionCount}; " +
            $"topVertices={_soakTestLastVertexCount}; " +
            $"managedBaseline=" +
            $"{BytesToMegabytes(_soakTestBaselineManagedBytes):F2} MB; " +
            $"managedPeak={BytesToMegabytes(_soakTestPeakManagedBytes):F2} MB; " +
            $"managedFinal={BytesToMegabytes(finalManagedBytes):F2} MB; " +
            $"managedDelta={BytesToMegabytes(memoryGrowthBytes):F2} MB; " +
            $"peakActive={_soakTestPeakActiveChunks}; " +
            $"peakQueue={_soakTestPeakQueuedWork}; " +
            $"peakWorkers={_soakTestPeakWorkers}; {GetQueueSnapshot()}");
        RestorePlayerAfterTerrainSoakTest(replan: false);
    }

    private void FailTerrainSoakTest(
        string reason,
        bool restoreWithReplan = true)
    {
        _soakTestStatus = "FAIL: " + reason;
        GD.PushError(
            $"Terrain soak test: FAIL; reason={reason}; " +
            $"moves={_soakTestCompletedMoves}; " +
            $"samples={_soakTestIdleSamples}; {GetQueueSnapshot()}; " +
            $"elapsed={_soakTestElapsedSeconds:F2} s");
        RestorePlayerAfterTerrainSoakTest(restoreWithReplan);
    }

    private void CancelTerrainSoakTest(string reason)
    {
        _soakTestStatus = "CANCELLED: " + reason;
        GD.Print(
            $"Terrain soak test: CANCELLED; reason={reason}; " +
            $"elapsed={_soakTestElapsedSeconds:F2} s");
        RestorePlayerAfterTerrainSoakTest(replan: true);
    }

    private void RestorePlayerAfterTerrainSoakTest(bool replan)
    {
        if (_player is not null)
        {
            _player.GlobalPosition = _soakTestOriginalPlayerPosition;
            _player.Velocity = Vector3.Zero;
            _player.SetPhysicsProcess(_soakTestPlayerPhysicsWasEnabled);

            if (replan)
            {
                Vector2I restoredCenter = _soakTestOriginalChunk;

                if (restoredCenter != _currentChunk || !IsStreamingIdle())
                {
                    _currentChunk = restoredCenter;
                    PlanRefresh(executeImmediately: false);
                }
            }
        }

        _soakTestState = TerrainSoakTestState.Idle;
        UpdateHud();
    }

    private int GetQueuedWorkCount()
    {
        return _pendingOperations.Count +
            _jobApplyOrder.Count +
            _readyJobs.Count +
            _completedJobs.Count;
    }

    private static double BytesToMegabytes(long bytes)
    {
        return bytes / (1024.0 * 1024.0);
    }

    private bool IsStreamingIdle()
    {
        return _pendingOperations.Count == 0 &&
            _activeJobs.Count == 0 &&
            _jobApplyOrder.Count == 0 &&
            _readyJobs.Count == 0 &&
            _completedJobs.IsEmpty &&
            _completedRevision == _planRevision;
    }

    private string GetQueueSnapshot()
    {
        return $"pending={_pendingOperations.Count}; " +
            $"apply={_jobApplyOrder.Count}; ready={_readyJobs.Count}; " +
            $"completed={_completedJobs.Count}; " +
            $"workers={_activeJobs.Count}";
    }

    private void ApplyDebugVisualization()
    {
        foreach (TerrainChunk chunk in _activeChunks.Values)
        {
            chunk.SetDebugVisualization(
                DebugViewMode,
                ShowWorldGrid,
                ShowWireframe,
                ShowChunkBorders,
                DebugGridSpacing);
        }
    }

    private static string GetDebugViewName(
        TerrainDebugViewMode debugViewMode)
    {
        return debugViewMode switch
        {
            TerrainDebugViewMode.Lod => "LOD",
            TerrainDebugViewMode.Normals => "нормали",
            _ => "высота/уклон"
        };
    }

    private static string GetToggleState(bool enabled)
    {
        return enabled ? "вкл" : "выкл";
    }

    private void UpdateHud()
    {
        if (_statusLabel is null || _player is null)
        {
            return;
        }

        CountLodChunks(out int highDetailCount, out int lowDetailCount);
        int sideLength = (Math.Max(1, ActiveRadius) * 2) + 1;
        bool transitionActive = _pendingOperations.Count > 0 ||
            _activeJobs.Count > 0 ||
            _jobApplyOrder.Count > 0 ||
            _readyJobs.Count > 0 ||
            !_completedJobs.IsEmpty;
        string transitionState = transitionActive
            ? "выполняется"
            : (_failedJobs > 0 ? "ошибка" : "стабильно");
        int queuedWork = _pendingOperations.Count + _jobApplyOrder.Count;

        _statusLabel.Text =
            "ПРОТОТИП B — ДИАГНОСТИКА РЕЛЬЕФА, СТРИМИНГА И LOD\n" +
            $"Позиция: X={_player.GlobalPosition.X:F1}, " +
            $"Z={_player.GlobalPosition.Z:F1}  •  " +
            $"чанк: ({_currentChunk.X}, {_currentChunk.Y})\n" +
            $"Активно: {_activeChunks.Count}/{sideLength * sideLength}  •  " +
            $"LOD0: {highDetailCount}  •  LOD1: {lowDetailCount}  •  " +
            $"переход: {transitionState}  •  очередь: {queuedWork}  •  " +
            $"workers: {_activeJobs.Count}/{_workerLimit}\n" +
            $"Вид: {GetDebugViewName(DebugViewMode)}  •  " +
            $"сетка: {GetToggleState(ShowWorldGrid)}  •  " +
            $"wireframe: {GetToggleState(ShowWireframe)}  •  " +
            $"границы: {GetToggleState(ShowChunkBorders)}\n" +
            $"Фоновая генерация с отменой  •  main-thread apply  •  " +
            $"ошибки: {_failedJobs}  •  stale: {_discardedStaleJobs}\n" +
            $"TASK-025 stress (F10): {_stressTestStatus}\n" +
            $"TASK-026 soak (P): {_soakTestStatus}\n" +
            $"Глобальные нормали  •  stitching  •  " +
            $"гистерезис: {ChunkSwitchHysteresis:F1} м  •  seed: {NoiseSeed}\n" +
            "F1 — режим цвета, F2 — мировая сетка, F3 — wireframe, " +
            "F4 — границы, F10 — stress, P — soak/stop\n" +
            "WASD — движение, Space — прыжок, мышь — обзор, " +
            "Esc — освободить курсор";
    }

    private static int ChebyshevDistance(Vector2I first, Vector2I second)
    {
        return Math.Max(
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));
    }

    private enum TerrainStressTestState
    {
        Idle,
        WaitingForInitialIdle,
        IssuingRevisions,
        WaitingForFinalIdle
    }

    private enum TerrainSoakTestState
    {
        Idle,
        WaitingForInitialIdle,
        Running,
        ReturningToOrigin
    }

    private enum ChunkOperationType
    {
        Create,
        Update,
        Remove
    }

    private readonly struct ActiveChunkJob
    {
        public ActiveChunkJob(
            long jobId,
            ChunkOperation operation,
            TerrainChunkBuildRequest request)
        {
            JobId = jobId;
            Operation = operation;
            Request = request;
        }

        public long JobId { get; }

        public ChunkOperation Operation { get; }

        public TerrainChunkBuildRequest Request { get; }
    }

    private readonly struct ReadyChunkJob
    {
        public ReadyChunkJob(
            ActiveChunkJob activeJob,
            CompletedChunkJob completedJob)
        {
            ActiveJob = activeJob;
            CompletedJob = completedJob;
        }

        public ActiveChunkJob ActiveJob { get; }

        public CompletedChunkJob CompletedJob { get; }
    }

    private sealed class CompletedChunkJob
    {
        private CompletedChunkJob(
            long jobId,
            int revision,
            TerrainChunkBuildResult? result,
            bool isCancelled,
            Exception? error)
        {
            JobId = jobId;
            Revision = revision;
            Result = result;
            IsCancelled = isCancelled;
            Error = error;
        }

        public long JobId { get; }

        public int Revision { get; }

        public TerrainChunkBuildResult? Result { get; }

        public bool IsCancelled { get; }

        public Exception? Error { get; }

        public static CompletedChunkJob Success(
            long jobId,
            int revision,
            TerrainChunkBuildResult result)
        {
            return new CompletedChunkJob(
                jobId,
                revision,
                result,
                false,
                null);
        }

        public static CompletedChunkJob Cancelled(
            long jobId,
            int revision)
        {
            return new CompletedChunkJob(
                jobId,
                revision,
                null,
                true,
                null);
        }

        public static CompletedChunkJob Failed(
            long jobId,
            int revision,
            Exception error)
        {
            return new CompletedChunkJob(
                jobId,
                revision,
                null,
                false,
                error);
        }
    }

    private readonly struct ChunkSpec
    {
        public ChunkSpec(
            int lodLevel,
            int visualResolution,
            bool generateCollision,
            TerrainEdgeStitchMask stitchMask,
            TerrainEdgeStitchMask skirtMask)
        {
            LodLevel = lodLevel;
            VisualResolution = visualResolution;
            GenerateCollision = generateCollision;
            StitchMask = stitchMask;
            SkirtMask = skirtMask;
        }

        public int LodLevel { get; }

        public int VisualResolution { get; }

        public bool GenerateCollision { get; }

        public TerrainEdgeStitchMask StitchMask { get; }

        public TerrainEdgeStitchMask SkirtMask { get; }

        public ChunkSpec WithEdgeMasks(
            TerrainEdgeStitchMask stitchMask,
            TerrainEdgeStitchMask skirtMask)
        {
            return new ChunkSpec(
                LodLevel,
                VisualResolution,
                GenerateCollision,
                stitchMask,
                skirtMask);
        }
    }

    private readonly struct ChunkOperation
    {
        private ChunkOperation(
            int revision,
            ChunkOperationType type,
            Vector2I coordinate)
        {
            Revision = revision;
            Type = type;
            Coordinate = coordinate;
        }

        public int Revision { get; }

        public ChunkOperationType Type { get; }

        public Vector2I Coordinate { get; }

        public static ChunkOperation Create(
            int revision,
            Vector2I coordinate)
        {
            return new ChunkOperation(
                revision,
                ChunkOperationType.Create,
                coordinate);
        }

        public static ChunkOperation Update(
            int revision,
            Vector2I coordinate)
        {
            return new ChunkOperation(
                revision,
                ChunkOperationType.Update,
                coordinate);
        }

        public static ChunkOperation Remove(
            int revision,
            Vector2I coordinate)
        {
            return new ChunkOperation(
                revision,
                ChunkOperationType.Remove,
                coordinate);
        }
    }
}
