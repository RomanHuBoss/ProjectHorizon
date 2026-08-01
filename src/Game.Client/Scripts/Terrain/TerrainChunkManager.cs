using System;
using System.Collections.Generic;
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
    public NodePath PlayerPath { get; set; } = new("../Player");

    [Export]
    public NodePath StatusLabelPath { get; set; } =
        new("../Hud/MarginContainer/PanelContainer/Label");

    private readonly Dictionary<Vector2I, TerrainChunk> _activeChunks = new();
    private readonly Queue<ChunkOperation> _pendingOperations = new();
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

    public override void _Ready()
    {
        _player = GetNode<CharacterBody3D>(PlayerPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _currentChunk = WorldToChunkWithoutHysteresis(_player.GlobalPosition);
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

        while (_pendingOperations.Count > 0 &&
            _operationsCompletedLastStep < operationBudget)
        {
            ExecuteOperation(_pendingOperations.Dequeue());
            _operationsCompletedLastStep++;
        }

        if (_pendingOperations.Count == 0)
        {
            CompleteRefresh();
        }
        else
        {
            UpdateHud();
        }
    }

    private double GetOperationInterval()
    {
        return Math.Max(0.001, OperationIntervalSeconds);
    }

    private void PlanRefresh(bool executeImmediately)
    {
        _planRevision++;
        _pendingOperations.Clear();
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

        // Incoming chunks are built first. High-detail chunks that must become
        // low-detail are demoted before the new center is promoted, preventing a
        // temporary high/high border where only one side is stitched. Remaining
        // mask-only updates follow, and outgoing chunks are removed last.
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

        GD.Print(
            $"TerrainChunkManager: planned center={_currentChunk}; " +
            $"create={_plannedCreates}; update={_plannedUpdates}; " +
            $"remove={_plannedRemovals}; queued={_pendingOperations.Count}; " +
            $"revision={_planRevision}");

        if (executeImmediately)
        {
            while (_pendingOperations.Count > 0)
            {
                ExecuteOperation(_pendingOperations.Dequeue());
            }

            CompleteRefresh();
        }
        else if (_pendingOperations.Count > 0 && _operationTimer is not null)
        {
            _operationTimer.WaitTime = GetOperationInterval();
            _operationTimer.Start();
        }
    }

    private void EnqueueOperations(IEnumerable<ChunkOperation> operations)
    {
        foreach (ChunkOperation operation in operations)
        {
            _pendingOperations.Enqueue(operation);
        }
    }

    private void ExecuteOperation(ChunkOperation operation)
    {
        if (operation.Revision != _planRevision)
        {
            return;
        }

        switch (operation.Type)
        {
            case ChunkOperationType.Create:
                ExecuteCreate(operation);
                break;

            case ChunkOperationType.Update:
                ExecuteUpdate(operation);
                break;

            case ChunkOperationType.Remove:
                ExecuteRemove(operation);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation.Type),
                    operation.Type,
                    "Unknown terrain chunk operation.");
        }
    }

    private void ExecuteCreate(ChunkOperation operation)
    {
        if (_activeChunks.ContainsKey(operation.Coordinate) ||
            !_desiredSpecs.TryGetValue(
                operation.Coordinate,
                out ChunkSpec currentSpec))
        {
            return;
        }

        TerrainChunk createdChunk = CreateChunk(
            operation.Coordinate,
            currentSpec);
        _activeChunks.Add(operation.Coordinate, createdChunk);
        _completedCreates++;
    }

    private void ExecuteUpdate(ChunkOperation operation)
    {
        if (!_activeChunks.TryGetValue(
                operation.Coordinate,
                out TerrainChunk? chunk) ||
            !_desiredSpecs.TryGetValue(
                operation.Coordinate,
                out ChunkSpec currentSpec))
        {
            return;
        }

        if (chunk.SetDetailLevel(
            currentSpec.LodLevel,
            currentSpec.VisualResolution,
            currentSpec.GenerateCollision,
            currentSpec.StitchMask,
            currentSpec.SkirtMask))
        {
            _completedUpdates++;
        }
    }

    private void ExecuteRemove(ChunkOperation operation)
    {
        if (_desiredSpecs.ContainsKey(operation.Coordinate) ||
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
            spec.SkirtMask);

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

    private void UpdateHud()
    {
        if (_statusLabel is null || _player is null)
        {
            return;
        }

        CountLodChunks(out int highDetailCount, out int lowDetailCount);
        int sideLength = (Math.Max(1, ActiveRadius) * 2) + 1;
        string transitionState = _pendingOperations.Count > 0
            ? "выполняется"
            : "стабильно";

        _statusLabel.Text =
            "ПРОТОТИП B — БЕСШОВНЫЙ СТРИМИНГ И LOD\n" +
            $"Чанк игрока: ({_currentChunk.X}, {_currentChunk.Y})  •  " +
            $"активно: {_activeChunks.Count}/{sideLength * sideLength}  •  " +
            $"LOD0: {highDetailCount}  •  LOD1: {lowDetailCount}\n" +
            $"Переход: {transitionState}  •  очередь: {_pendingOperations.Count}  •  " +
            $"операций за шаг: {_operationsCompletedLastStep}\n" +
            $"Stitching кромок + глобальные нормали  •  " +
            $"гистерезис: {ChunkSwitchHysteresis:F1} м  •  seed: {NoiseSeed}\n" +
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
