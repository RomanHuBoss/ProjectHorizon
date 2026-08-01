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

    [Export]
    public int NoiseSeed { get; set; } = 20260801;

    [Export]
    public NodePath PlayerPath { get; set; } = new("../Player");

    [Export]
    public NodePath StatusLabelPath { get; set; } =
        new("../Hud/MarginContainer/PanelContainer/Label");

    private readonly Dictionary<Vector2I, TerrainChunk> _activeChunks = new();
    private CharacterBody3D? _player;
    private Label? _statusLabel;
    private Timer? _refreshTimer;
    private Vector2I _currentChunk = new(int.MinValue, int.MinValue);
    private double _hudUpdateAccumulator;
    private int _refreshRevision;

    public override void _Ready()
    {
        _player = GetNode<CharacterBody3D>(PlayerPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);

        _refreshTimer = new Timer
        {
            Name = "RefreshTimer",
            OneShot = true,
            WaitTime = 0.05
        };
        _refreshTimer.Timeout += RefreshChunks;
        AddChild(_refreshTimer);

        _currentChunk = WorldToChunk(_player.GlobalPosition);
        RefreshChunks();
        UpdateHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
        {
            return;
        }

        Vector2I observedChunk = WorldToChunk(_player.GlobalPosition);

        if (observedChunk != _currentChunk)
        {
            _currentChunk = observedChunk;
            QueueRefresh();
        }

        _hudUpdateAccumulator += delta;

        if (_hudUpdateAccumulator >= 0.10)
        {
            _hudUpdateAccumulator = 0.0;
            UpdateHud();
        }
    }

    private void QueueRefresh()
    {
        if (_refreshTimer is null)
        {
            return;
        }

        _refreshTimer.Start();
    }

    private void RefreshChunks()
    {
        int activeRadius = Math.Max(1, ActiveRadius);
        List<Vector2I> desiredCoordinates = new();

        for (int offsetZ = -activeRadius; offsetZ <= activeRadius; offsetZ++)
        {
            for (int offsetX = -activeRadius; offsetX <= activeRadius; offsetX++)
            {
                desiredCoordinates.Add(
                    _currentChunk + new Vector2I(offsetX, offsetZ));
            }
        }

        desiredCoordinates.Sort((first, second) =>
        {
            int distanceComparison = ChebyshevDistance(first, _currentChunk)
                .CompareTo(ChebyshevDistance(second, _currentChunk));

            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int zComparison = first.Y.CompareTo(second.Y);
            return zComparison != 0
                ? zComparison
                : first.X.CompareTo(second.X);
        });

        HashSet<Vector2I> desiredSet = new(desiredCoordinates);
        int removedCount = RemoveUndesiredChunks(desiredSet);
        int createdCount = 0;
        int regeneratedCount = 0;

        foreach (Vector2I coordinate in desiredCoordinates)
        {
            int distance = ChebyshevDistance(coordinate, _currentChunk);
            int lodLevel = distance <= HighDetailRadius ? 0 : 1;
            int visualResolution = lodLevel == 0
                ? HighDetailResolution
                : LowDetailResolution;
            bool generateCollision = distance <= CollisionRadius;

            if (_activeChunks.TryGetValue(coordinate, out TerrainChunk? chunk))
            {
                if (chunk.SetDetailLevel(
                    lodLevel,
                    visualResolution,
                    generateCollision))
                {
                    regeneratedCount++;
                }

                continue;
            }

            TerrainChunk createdChunk = CreateChunk(
                coordinate,
                lodLevel,
                visualResolution,
                generateCollision);
            _activeChunks.Add(coordinate, createdChunk);
            createdCount++;
        }

        _refreshRevision++;
        CountLodChunks(out int highDetailCount, out int lowDetailCount);

        GD.Print(
            $"TerrainChunkManager: refreshed center={_currentChunk}; " +
            $"active={_activeChunks.Count}; high={highDetailCount}; " +
            $"low={lowDetailCount}; created={createdCount}; " +
            $"regenerated={regeneratedCount}; removed={removedCount}; " +
            $"revision={_refreshRevision}");

        UpdateHud();
    }

    private TerrainChunk CreateChunk(
        Vector2I coordinate,
        int lodLevel,
        int visualResolution,
        bool generateCollision)
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
            lodLevel,
            visualResolution,
            CollisionResolution,
            ChunkSize,
            HeightScale,
            NoiseFrequency,
            NoiseSeed,
            SkirtDepth,
            generateCollision);

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

    private int RemoveUndesiredChunks(HashSet<Vector2I> desiredCoordinates)
    {
        List<Vector2I> coordinatesToRemove = new();

        foreach (Vector2I coordinate in _activeChunks.Keys)
        {
            if (!desiredCoordinates.Contains(coordinate))
            {
                coordinatesToRemove.Add(coordinate);
            }
        }

        foreach (Vector2I coordinate in coordinatesToRemove)
        {
            TerrainChunk chunk = _activeChunks[coordinate];
            chunk.ReleaseGeneratedResources();
            chunk.QueueFree();
            _activeChunks.Remove(coordinate);
        }

        return coordinatesToRemove.Count;
    }

    private Vector2I WorldToChunk(Vector3 worldPosition)
    {
        float halfSize = ChunkSize * 0.5f;
        int chunkX = Mathf.FloorToInt((worldPosition.X + halfSize) / ChunkSize);
        int chunkZ = Mathf.FloorToInt((worldPosition.Z + halfSize) / ChunkSize);
        return new Vector2I(chunkX, chunkZ);
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

        _statusLabel.Text =
            "ПРОТОТИП B — СТРИМИНГ ЧАНКОВ И LOD\n" +
            $"Чанк игрока: ({_currentChunk.X}, {_currentChunk.Y})  •  " +
            $"активно: {_activeChunks.Count}/{sideLength * sideLength}  •  " +
            $"LOD0: {highDetailCount} × {HighDetailResolution}  •  " +
            $"LOD1: {lowDetailCount} × {LowDetailResolution}\n" +
            $"Seed: {NoiseSeed}  •  размер чанка: {ChunkSize:F0} м  •  " +
            "skirts скрывают щели между LOD\n" +
            "WASD — движение, Space — прыжок, мышь — обзор, " +
            "Esc — освободить курсор";
    }

    private static int ChebyshevDistance(Vector2I first, Vector2I second)
    {
        return Math.Max(
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));
    }
}
