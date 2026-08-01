using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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
    private Timer? _operationTimer;
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

    public override void _Ready()
    {
        _player = GetNode<CharacterBody3D>(PlayerPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _currentChunk = WorldToChunkWithoutHysteresis(_player.GlobalPosition);
        _workerLimit = Math.Max(
            1,
            Math.Min(4, System.Environment.ProcessorCount - 2));
        _operationTimer = new Timer
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

        Vector2I nextCenter = CalculateHystereticCenter(_player.GlobalPosition);

        if (nextCenter != _currentChunk)
        {
            _currentChunk = nextCenter;
            PlanRefresh(executeImmediately: false);
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
                continue;
            }

            if (completedJob.Error is not null)
            {
                _failedJobs++;
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
                _discardedStaleJobs++;
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
        _failedJobs = 0;
        _cancelledJobs = 0;
        _discardedStaleJobs = discardedReadyJobCount;

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
            _jobApplyOrder.Count > 0;
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
            $"Глобальные нормали  •  stitching  •  " +
            $"гистерезис: {ChunkSwitchHysteresis:F1} м  •  seed: {NoiseSeed}\n" +
            "F1 — режим цвета, F2 — мировая сетка, F3 — wireframe, " +
            "F4 — границы чанков\n" +
            "WASD — движение, Space — прыжок, мышь — обзор, " +
            "Esc — освободить курсор";
    }

    private static int ChebyshevDistance(Vector2I first, Vector2I second)
    {
        return Math.Max(
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));
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
