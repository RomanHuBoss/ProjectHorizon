using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public readonly record struct NpcNavigationTileKey(int X, int Z)
{
    public override string ToString() => $"{X}:{Z}";
}

public sealed record NpcNavigationObstacleBounds(
    string Id,
    Vector3 Center,
    float HalfX,
    float HalfZ,
    float Height)
{
    public bool IntersectsCell(
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ,
        float margin)
    {
        float obstacleMinX = Center.X - HalfX - margin;
        float obstacleMaxX = Center.X + HalfX + margin;
        float obstacleMinZ = Center.Z - HalfZ - margin;
        float obstacleMaxZ = Center.Z + HalfZ + margin;
        return maximumX > obstacleMinX && minimumX < obstacleMaxX &&
            maximumZ > obstacleMinZ && minimumZ < obstacleMaxZ;
    }

    public bool ContainsXZ(Vector3 point, float margin)
    {
        return point.X >= Center.X - HalfX - margin &&
            point.X <= Center.X + HalfX + margin &&
            point.Z >= Center.Z - HalfZ - margin &&
            point.Z <= Center.Z + HalfZ + margin;
    }
}

public sealed record NpcNavigationSurfaceSnapshot(
    int ActiveRegions,
    int MaximumRegions,
    int WalkableCells,
    int StaticObstacles,
    int AvoidanceObstacles,
    int StreamGeneration,
    int CreatedRegions,
    int EvictedRegions,
    int ObstacleRevision,
    bool ReadyForQueries,
    NpcNavigationTileKey CenterTile,
    IReadOnlyList<NpcNavigationTileKey> ActiveTiles);

public partial class NpcNavigationSurfaceNode : Node3D
{
    public const float TileSizeMeters = 12.0f;
    public const float CellSizeMeters = 1.0f;
    public const int ActiveTileRadius = 2;
    public const float AgentRadiusMeters = 0.48f;
    public const float NavigationSurfaceY = 0.11f;

    private const float GroundBorderMeters = 0.55f;
    private const int NavigationLayer = 1;

    private readonly Dictionary<NpcNavigationTileKey, NavigationRegion3D> _regions = new();
    private readonly List<NpcNavigationObstacleBounds> _obstacles = new();
    private readonly List<NavigationObstacle3D> _avoidanceObstacles = new();

    private PlayerController? _player;
    private Node3D? _worldRoot;
    private PlanetSurfaceTerrainProfile? _terrainProfile;
    private Rect2 _groundXZ;
    private NpcNavigationTileKey _centerTile;
    private bool _hasCenterTile;
    private Vector3? _acceptanceCenterOverride;
    private int _syncFramesRemaining;
    private int _streamGeneration;
    private int _createdRegions;
    private int _evictedRegions;
    private int _walkableCells;
    private int _obstacleRevision;
    private long _synchronizationBaselineIteration;
    private bool _navigationSynchronizationPending;
    private Rid _navigationMap;
    private bool _navigationMapCreated;
    private Vector3 _navigationMapUp = Vector3.Up;

    public bool IsConfigured => _player is not null && _worldRoot is not null;

    public Rid NavigationMap => _navigationMapCreated
        ? _navigationMap
        : GetWorld3D().NavigationMap;

    public Vector3 NavigationMapUp => _navigationMapCreated
        ? NavigationServer3D.MapGetUp(_navigationMap).Normalized()
        : Vector3.Up;

    public bool ReadyForQueries => IsConfigured &&
        _regions.Count > 0 &&
        _syncFramesRemaining <= 0 &&
        HasNavigationMapSynchronized();

    public bool ParentFrameAligned
    {
        get
        {
            if (GetParent() is not Node3D parent)
            {
                return false;
            }
            Basis basis = parent.GlobalTransform.Basis.Orthonormalized();
            bool orthonormal = Math.Abs(basis.X.Length() - 1.0f) <= 0.001f &&
                Math.Abs(basis.Y.Length() - 1.0f) <= 0.001f &&
                Math.Abs(basis.Z.Length() - 1.0f) <= 0.001f &&
                Math.Abs(basis.X.Dot(basis.Y)) <= 0.001f &&
                Math.Abs(basis.X.Dot(basis.Z)) <= 0.001f &&
                Math.Abs(basis.Y.Dot(basis.Z)) <= 0.001f;
            if (!orthonormal || !_navigationMapCreated)
            {
                return false;
            }
            Vector3 mapUp = NavigationServer3D.MapGetUp(_navigationMap).Normalized();
            bool regionBindings = _regions.Values.All(region =>
                !GodotObject.IsInstanceValid(region) ||
                region.GetNavigationMap().Equals(_navigationMap));
            bool obstacleBindings = _avoidanceObstacles.All(obstacle =>
                !GodotObject.IsInstanceValid(obstacle) ||
                obstacle.GetNavigationMap().Equals(_navigationMap));
            return mapUp.Dot(basis.Y.Normalized()) >= 0.9999f &&
                regionBindings && obstacleBindings;
        }
    }

    public int ActiveRegionCount => _regions.Count;

    public int MaximumRegionCount => (ActiveTileRadius * 2 + 1) *
        (ActiveTileRadius * 2 + 1);

    public int WalkableCellCount => _walkableCells;

    public int StaticObstacleCount => _obstacles.Count;

    public int AvoidanceObstacleCount => _avoidanceObstacles.Count;

    public int StreamGeneration => _streamGeneration;

    public int TotalCreatedRegions => _createdRegions;

    public int TotalEvictedRegions => _evictedRegions;

    public int ObstacleRevision => _obstacleRevision;

    public void Configure(
        PlayerController player,
        Node3D worldRoot,
        PlanetSurfaceTerrainProfile? terrainProfile = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(worldRoot);
        _player = player;
        _worldRoot = worldRoot;
        _terrainProfile = terrainProfile;
        Name = "NpcNavigation";
        AddToGroup("npc_navigation_surface");
        Vector3 initialUp = GetParent() is Node3D surfaceRoot
            ? surfaceRoot.GlobalTransform.Basis.Y.Normalized()
            : Vector3.Up;
        EnsureDedicatedNavigationMap(initialUp, recreate: false);
        ResolveGroundBounds();
        CaptureStaticObstacles();
        RebuildAvoidanceObstacles();
        RefreshStreaming(force: true);
        GD.Print(
            "TASK-124 NPC navigation surface READY: " +
            $"tile={TileSizeMeters:0.#}m; cell={CellSizeMeters:0.#}m; radius={ActiveTileRadius}; " +
            $"regions={ActiveRegionCount}/{MaximumRegionCount}; walkableCells={WalkableCellCount}; " +
            $"obstacles={StaticObstacleCount}; avoidanceObstacles={AvoidanceObstacleCount}; " +
            "server=NavigationServer3D; regions=NavigationRegion3D; streaming=bounded.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsConfigured)
        {
            return;
        }
        RefreshStreaming(force: false);
        if (_syncFramesRemaining > 0)
        {
            _syncFramesRemaining--;
        }
    }

    public void PrepareSurfaceFrameChange(Vector3 nextWorldUp)
    {
        Vector3 up = nextWorldUp.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : nextWorldUp.Normalized();

        // TASK-172.1: Godot navigation maps have an explicit UP orientation.
        // A NavigationRegion3D may not be rotated >= 90 degrees away from it.
        // Remove the old regions before the Gameplay parent rotates, then use a
        // fresh dedicated map whose UP already matches the next radial frame.
        DestroyNavigationRegions();
        EnsureDedicatedNavigationMap(up, recreate: true);
        _hasCenterTile = false;
    }

    public void NotifySurfaceFrameChanged()
    {
        if (!IsConfigured)
        {
            return;
        }

        Vector3 expectedUp = GetParent() is Node3D surfaceRoot
            ? surfaceRoot.GlobalTransform.Basis.Y.Normalized()
            : Vector3.Up;
        if (!_navigationMapCreated ||
            _navigationMapUp.Dot(expectedUp) < 0.9999f)
        {
            DestroyNavigationRegions();
            EnsureDedicatedNavigationMap(expectedUp, recreate: true);
        }
        MarkNavigationSynchronizationPending(GetNavigationMapIteration());
        _hasCenterTile = false;
        RefreshStreaming(force: true);
    }

    public void SetTerrainProfile(PlanetSurfaceTerrainProfile? terrainProfile)
    {
        _terrainProfile = terrainProfile;
        if (!IsConfigured)
        {
            return;
        }
        ResolveGroundBounds();
        CaptureStaticObstacles();
        RebuildAvoidanceObstacles();
        RebuildCurrentTiles();
        _hasCenterTile = false;
        RefreshStreaming(force: true);
    }

    public float GetNavigationHeight(float x, float z)
    {
        return _terrainProfile is null
            ? NavigationSurfaceY
            : (float)PlanetSurfaceTerrainRuntime.SampleHeight(
                _terrainProfile,
                x,
                z) + NavigationSurfaceY;
    }

    public void RefreshObstacleGeometry()
    {
        if (!IsConfigured)
        {
            return;
        }
        CaptureStaticObstacles();
        RebuildAvoidanceObstacles();
        RebuildCurrentTiles();
        _obstacleRevision++;
        GD.Print(
            "TASK-124 NPC navigation obstacles refreshed: " +
            $"revision={_obstacleRevision}; obstacles={StaticObstacleCount}; " +
            $"avoidanceObstacles={AvoidanceObstacleCount}; regions={ActiveRegionCount}; " +
            $"walkableCells={WalkableCellCount}.");
    }

    public void SetAcceptanceStreamingCenter(Vector3? center)
    {
        _acceptanceCenterOverride = center;
        _hasCenterTile = false;
        RefreshStreaming(force: true);
    }

    public bool IsPointInActiveArea(Vector3 point)
    {
        NpcNavigationTileKey key = ToTileKey(ToSurfaceLogical(point));
        return _regions.ContainsKey(key);
    }

    public Vector3 GetClosestNavigationPoint(Vector3 point)
    {
        if (!ReadyForQueries)
        {
            return point;
        }
        return NavigationServer3D.MapGetClosestPoint(
            NavigationMap,
            point);
    }

    public Vector3[] QueryPath(Vector3 from, Vector3 to)
    {
        if (!ReadyForQueries)
        {
            return Array.Empty<Vector3>();
        }
        Vector3 start = NavigationServer3D.MapGetClosestPoint(
            NavigationMap,
            from);
        Vector3 target = NavigationServer3D.MapGetClosestPoint(
            NavigationMap,
            to);
        return NavigationServer3D.MapGetPath(
            NavigationMap,
            start,
            target,
            true,
            NavigationLayer);
    }

    public bool TryBuildRecoveryWaypoint(
        Vector3 current,
        Vector3 target,
        int sideSeed,
        out Vector3 waypoint)
    {
        waypoint = current;
        if (!ReadyForQueries)
        {
            return false;
        }
        Vector3 currentLocal = ToSurfaceLogical(current);
        Vector3 targetLocal = ToSurfaceLogical(target);
        Vector3 towardLocal = targetLocal - currentLocal;
        towardLocal.Y = 0.0f;
        if (towardLocal.LengthSquared() < 0.25f)
        {
            return false;
        }
        towardLocal = towardLocal.Normalized();
        Vector3 lateralLocal = new(-towardLocal.Z, 0.0f, towardLocal.X);
        float sign = (sideSeed & 1) == 0 ? 1.0f : -1.0f;
        Vector3 candidateLocal = currentLocal + towardLocal * 1.4f +
            lateralLocal * sign * 2.6f;
        Vector3 candidate = SurfaceLogicalToWorld(candidateLocal);
        Vector3 snapped = GetClosestNavigationPoint(candidate);
        Vector3[] path = QueryPath(current, snapped);
        if (path.Length < 2 || current.DistanceTo(snapped) < 0.75f)
        {
            candidateLocal = currentLocal + towardLocal * 0.8f -
                lateralLocal * sign * 2.6f;
            candidate = SurfaceLogicalToWorld(candidateLocal);
            snapped = GetClosestNavigationPoint(candidate);
            path = QueryPath(current, snapped);
        }
        if (path.Length < 2 || current.DistanceTo(snapped) < 0.75f)
        {
            return false;
        }
        waypoint = snapped;
        return true;
    }

    public bool PathAvoidsCapturedObstacles(
        IReadOnlyList<Vector3> path,
        float margin = 0.08f)
    {
        if (path.Count < 2)
        {
            return false;
        }
        for (int index = 0; index < path.Count - 1; index++)
        {
            Vector3 start = path[index];
            Vector3 end = path[index + 1];
            float distance = start.DistanceTo(end);
            int samples = Math.Max(1, (int)Math.Ceiling(distance / 0.25f));
            for (int sample = 0; sample <= samples; sample++)
            {
                float weight = sample / (float)samples;
                Vector3 point = ToSurfaceLogical(start.Lerp(end, weight));
                if (_obstacles.Any(obstacle => obstacle.ContainsXZ(point, margin)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public int CountTilesTouchedByPath(IReadOnlyList<Vector3> path)
    {
        HashSet<NpcNavigationTileKey> touched = new();
        if (path.Count == 0)
        {
            return 0;
        }
        if (path.Count == 1)
        {
            touched.Add(ToTileKey(ToSurfaceLogical(path[0])));
            return touched.Count;
        }
        for (int index = 0; index < path.Count - 1; index++)
        {
            Vector3 start = path[index];
            Vector3 end = path[index + 1];
            float distance = start.DistanceTo(end);
            int samples = Math.Max(1, (int)Math.Ceiling(distance / 1.0f));
            for (int sample = 0; sample <= samples; sample++)
            {
                touched.Add(ToTileKey(ToSurfaceLogical(
                    start.Lerp(end, sample / (float)samples))));
            }
        }
        return touched.Count;
    }

    public NpcNavigationSurfaceSnapshot CreateSnapshot()
    {
        return new NpcNavigationSurfaceSnapshot(
            ActiveRegionCount,
            MaximumRegionCount,
            WalkableCellCount,
            StaticObstacleCount,
            AvoidanceObstacleCount,
            StreamGeneration,
            TotalCreatedRegions,
            TotalEvictedRegions,
            ObstacleRevision,
            ReadyForQueries,
            _centerTile,
            _regions.Keys.OrderBy(key => key.X).ThenBy(key => key.Z).ToArray());
    }

    private void RefreshStreaming(bool force)
    {
        if (_player is null)
        {
            return;
        }
        Vector3 center = ToSurfaceLogical(
            _acceptanceCenterOverride ?? _player.GlobalPosition);
        NpcNavigationTileKey nextCenter = ToTileKey(center);
        if (!force && _hasCenterTile && nextCenter.Equals(_centerTile))
        {
            return;
        }
        long synchronizationBaseline = GetNavigationMapIteration();
        bool navigationChanged = false;
        _centerTile = nextCenter;
        _hasCenterTile = true;
        HashSet<NpcNavigationTileKey> desired = BuildDesiredTileSet(nextCenter);
        foreach (NpcNavigationTileKey key in _regions.Keys.ToArray())
        {
            if (desired.Contains(key))
            {
                continue;
            }
            NavigationRegion3D region = _regions[key];
            _regions.Remove(key);
            RemoveChild(region);
            region.QueueFree();
            _evictedRegions++;
            navigationChanged = true;
        }
        foreach (NpcNavigationTileKey key in desired.OrderBy(key => key.X).ThenBy(key => key.Z))
        {
            if (_regions.ContainsKey(key))
            {
                continue;
            }
            NavigationRegion3D? region = BuildRegion(key, out _);
            if (region is null)
            {
                continue;
            }
            _regions.Add(key, region);
            AddChild(region);
            _createdRegions++;
            navigationChanged = true;
        }
        RecountWalkableCells();
        _streamGeneration++;
        if (navigationChanged)
        {
            MarkNavigationSynchronizationPending(synchronizationBaseline);
        }
    }

    private void RebuildCurrentTiles()
    {
        long synchronizationBaseline = GetNavigationMapIteration();
        bool navigationChanged = false;
        NpcNavigationTileKey[] keys = _regions.Keys.ToArray();
        foreach (NpcNavigationTileKey key in keys)
        {
            NavigationRegion3D old = _regions[key];
            _regions.Remove(key);
            RemoveChild(old);
            old.QueueFree();
            _evictedRegions++;
            navigationChanged = true;
        }
        foreach (NpcNavigationTileKey key in keys.OrderBy(key => key.X).ThenBy(key => key.Z))
        {
            NavigationRegion3D? region = BuildRegion(key, out _);
            if (region is null)
            {
                continue;
            }
            _regions.Add(key, region);
            AddChild(region);
            _createdRegions++;
            navigationChanged = true;
        }
        RecountWalkableCells();
        _streamGeneration++;
        if (navigationChanged)
        {
            MarkNavigationSynchronizationPending(synchronizationBaseline);
        }
    }

    private void EnsureDedicatedNavigationMap(Vector3 worldUp, bool recreate)
    {
        Vector3 up = worldUp.LengthSquared() <= 0.000001f
            ? Vector3.Up
            : worldUp.Normalized();
        if (_navigationMapCreated && !recreate &&
            _navigationMapUp.Dot(up) >= 0.9999f)
        {
            return;
        }

        if (_navigationMapCreated)
        {
            // TASK-172.1: obstacle nodes also carry their own navigation-map
            // binding. Detach them before freeing/replacing the dedicated map;
            // otherwise they may keep an obsolete default-UP map while the
            // Gameplay parent rotates into a radial frame.
            foreach (NavigationObstacle3D obstacle in _avoidanceObstacles)
            {
                if (GodotObject.IsInstanceValid(obstacle))
                {
                    obstacle.SetNavigationMap(new Rid());
                }
            }
            NavigationServer3D.MapSetActive(_navigationMap, false);
            NavigationServer3D.FreeRid(_navigationMap);
            _navigationMapCreated = false;
        }

        _navigationMap = NavigationServer3D.MapCreate();
        _navigationMapCreated = true;
        _navigationMapUp = up;
        NavigationServer3D.MapSetUp(_navigationMap, up);
        NavigationServer3D.MapSetCellSize(_navigationMap, CellSizeMeters);
        NavigationServer3D.MapSetCellHeight(_navigationMap, 0.25f);
        NavigationServer3D.MapSetUseEdgeConnections(_navigationMap, true);
        NavigationServer3D.MapSetEdgeConnectionMargin(_navigationMap, 0.2f);
        NavigationServer3D.MapSetActive(_navigationMap, true);
        foreach (NavigationObstacle3D obstacle in _avoidanceObstacles)
        {
            if (GodotObject.IsInstanceValid(obstacle))
            {
                obstacle.SetNavigationMap(_navigationMap);
            }
        }
        _navigationSynchronizationPending = true;
        _synchronizationBaselineIteration = 0;
        _syncFramesRemaining = Math.Max(_syncFramesRemaining, 2);
    }

    private void DestroyNavigationRegions()
    {
        foreach (NavigationRegion3D region in _regions.Values)
        {
            if (!GodotObject.IsInstanceValid(region))
            {
                continue;
            }
            region.Enabled = false;
            region.SetNavigationMap(new Rid());
            if (region.GetParent() == this)
            {
                RemoveChild(region);
            }
            region.QueueFree();
            _evictedRegions++;
        }
        _regions.Clear();
        _walkableCells = 0;
    }

    public override void _ExitTree()
    {
        DestroyNavigationRegions();
        if (_navigationMapCreated)
        {
            // TASK-172.1: obstacle nodes also carry their own navigation-map
            // binding. Detach them before freeing/replacing the dedicated map;
            // otherwise they may keep an obsolete default-UP map while the
            // Gameplay parent rotates into a radial frame.
            foreach (NavigationObstacle3D obstacle in _avoidanceObstacles)
            {
                if (GodotObject.IsInstanceValid(obstacle))
                {
                    obstacle.SetNavigationMap(new Rid());
                }
            }
            NavigationServer3D.MapSetActive(_navigationMap, false);
            NavigationServer3D.FreeRid(_navigationMap);
            _navigationMapCreated = false;
        }
    }

    private long GetNavigationMapIteration()
    {
        if (!IsInsideTree())
        {
            return 0;
        }
        return NavigationServer3D.MapGetIterationId(
            NavigationMap);
    }

    private void MarkNavigationSynchronizationPending(long baselineIteration)
    {
        _synchronizationBaselineIteration = baselineIteration;
        _navigationSynchronizationPending = true;
        _syncFramesRemaining = Math.Max(_syncFramesRemaining, 2);
    }

    private bool HasNavigationMapSynchronized()
    {
        long currentIteration = GetNavigationMapIteration();
        if (currentIteration <= 0)
        {
            return false;
        }
        if (!_navigationSynchronizationPending)
        {
            return true;
        }
        if (currentIteration == _synchronizationBaselineIteration)
        {
            return false;
        }
        _navigationSynchronizationPending = false;
        _synchronizationBaselineIteration = currentIteration;
        return true;
    }

    private HashSet<NpcNavigationTileKey> BuildDesiredTileSet(
        NpcNavigationTileKey center)
    {
        HashSet<NpcNavigationTileKey> desired = new();
        for (int x = center.X - ActiveTileRadius; x <= center.X + ActiveTileRadius; x++)
        {
            for (int z = center.Z - ActiveTileRadius; z <= center.Z + ActiveTileRadius; z++)
            {
                NpcNavigationTileKey key = new(x, z);
                if (TileIntersectsGround(key))
                {
                    desired.Add(key);
                }
            }
        }
        return desired;
    }

    private NavigationRegion3D? BuildRegion(
        NpcNavigationTileKey key,
        out int walkableCells)
    {
        walkableCells = 0;
        int cellsPerSide = (int)Math.Round(TileSizeMeters / CellSizeMeters);
        float originX = key.X * TileSizeMeters;
        float originZ = key.Z * TileSizeMeters;
        List<Vector3> vertices = new();
        Dictionary<(int X, int Z), int> vertexIndices = new();
        List<int[]> polygons = new();
        for (int cellX = 0; cellX < cellsPerSide; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsPerSide; cellZ++)
            {
                float minimumX = originX + cellX * CellSizeMeters;
                float maximumX = minimumX + CellSizeMeters;
                float minimumZ = originZ + cellZ * CellSizeMeters;
                float maximumZ = minimumZ + CellSizeMeters;
                if (!IsCellWalkable(minimumX, maximumX, minimumZ, maximumZ))
                {
                    continue;
                }
                int v00 = GetOrAddVertex(cellX, cellZ);
                int v01 = GetOrAddVertex(cellX, cellZ + 1);
                int v11 = GetOrAddVertex(cellX + 1, cellZ + 1);
                int v10 = GetOrAddVertex(cellX + 1, cellZ);
                polygons.Add(new[] { v00, v01, v11, v10 });
                walkableCells++;
            }
        }
        if (polygons.Count == 0)
        {
            return null;
        }

        NavigationMesh navigationMesh = new()
        {
            Vertices = vertices.ToArray(),
            AgentRadius = AgentRadiusMeters,
            CellSize = CellSizeMeters,
            CellHeight = 0.25f
        };
        foreach (int[] polygon in polygons)
        {
            navigationMesh.AddPolygon(polygon);
        }
        NavigationRegion3D region = new()
        {
            Name = $"Tile_{key.X}_{key.Z}",
            Position = new Vector3(originX, 0.0f, originZ),
            NavigationMesh = navigationMesh,
            NavigationLayers = NavigationLayer,
            UseEdgeConnections = true,
            Enabled = true
        };
        region.SetNavigationMap(NavigationMap);
        region.AddToGroup("npc_navigation_tile");
        return region;

        int GetOrAddVertex(int x, int z)
        {
            (int X, int Z) vertexKey = (x, z);
            if (vertexIndices.TryGetValue(vertexKey, out int existing))
            {
                return existing;
            }
            int index = vertices.Count;
            float worldX = originX + x * CellSizeMeters;
            float worldZ = originZ + z * CellSizeMeters;
            vertices.Add(new Vector3(
                x * CellSizeMeters,
                GetNavigationHeight(worldX, worldZ),
                z * CellSizeMeters));
            vertexIndices.Add(vertexKey, index);
            return index;
        }
    }

    private bool IsCellWalkable(
        float minimumX,
        float maximumX,
        float minimumZ,
        float maximumZ)
    {
        float groundMinX = _groundXZ.Position.X + GroundBorderMeters;
        float groundMinZ = _groundXZ.Position.Y + GroundBorderMeters;
        float groundMaxX = _groundXZ.End.X - GroundBorderMeters;
        float groundMaxZ = _groundXZ.End.Y - GroundBorderMeters;
        if (minimumX < groundMinX || maximumX > groundMaxX ||
            minimumZ < groundMinZ || maximumZ > groundMaxZ)
        {
            return false;
        }
        if (_terrainProfile is not null)
        {
            double centerX = (minimumX + maximumX) * 0.5;
            double centerZ = (minimumZ + maximumZ) * 0.5;
            PlanetSurfaceTerrainSample terrain = PlanetSurfaceTerrainRuntime.Sample(
                _terrainProfile,
                centerX,
                centerZ);
            if (terrain.SlopeDegrees > _terrainProfile.MaximumWalkableSlopeDegrees)
            {
                return false;
            }
        }
        return !_obstacles.Any(obstacle => obstacle.IntersectsCell(
            minimumX,
            maximumX,
            minimumZ,
            maximumZ,
            AgentRadiusMeters));
    }

    private bool TileIntersectsGround(NpcNavigationTileKey key)
    {
        float minimumX = key.X * TileSizeMeters;
        float minimumZ = key.Z * TileSizeMeters;
        Rect2 tile = new(
            minimumX,
            minimumZ,
            TileSizeMeters,
            TileSizeMeters);
        return tile.Intersects(_groundXZ, true);
    }

    private void ResolveGroundBounds()
    {
        if (_worldRoot is null)
        {
            throw new InvalidOperationException("Navigation world root is unavailable.");
        }
        if (_terrainProfile is not null)
        {
            // TASK-158 promotes navigation from the authored 80x80 starter patch
            // to the same moving traversal envelope as streamed terrain. The
            // height sampler is global/deterministic, while only 5x5 nav tiles
            // remain resident around the player.
            float extent = (float)Math.Max(
                PlanetSurfaceStreamingRuntime.NavigationTraversalExtentMeters,
                PlanetSurfaceFrameRuntime.PlanetLogicalHalfExtentMeters);
            _groundXZ = new Rect2(-extent, -extent, extent * 2.0f, extent * 2.0f);
            return;
        }

        CollisionShape3D? groundShape = _worldRoot.GetNodeOrNull<CollisionShape3D>(
            "GroundBody/CollisionShape3D");
        if (groundShape?.Shape is not BoxShape3D box)
        {
            throw new InvalidOperationException(
                "TASK-124 requires terrain bounds or a BoxShape3D GroundBody collision.");
        }
        Vector3 center = groundShape.GlobalPosition;
        Vector3 size = box.Size;
        _groundXZ = new Rect2(
            center.X - size.X * 0.5f,
            center.Z - size.Z * 0.5f,
            size.X,
            size.Z);
    }

    private void CaptureStaticObstacles()
    {
        if (_worldRoot is null)
        {
            return;
        }
        _obstacles.Clear();
        foreach (CollisionShape3D shapeNode in EnumerateCollisionShapes(_worldRoot))
        {
            if (shapeNode.Disabled ||
                shapeNode.GetParent() is not StaticBody3D body ||
                body.CollisionLayer == 0u ||
                body is TerrainChunk ||
                string.Equals(body.Name, "GroundBody", StringComparison.Ordinal) ||
                string.Equals(body.Name, "LandingPad", StringComparison.Ordinal))
            {
                continue;
            }
            if (!TryCreateObstacleBounds(shapeNode, body, out NpcNavigationObstacleBounds obstacle))
            {
                continue;
            }
            obstacle = obstacle with
            {
                Center = ToSurfaceLogical(obstacle.Center)
            };
            if (!ObstacleIntersectsGround(obstacle))
            {
                continue;
            }
            _obstacles.Add(obstacle);
        }
        _obstacles.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
    }

    private void RebuildAvoidanceObstacles()
    {
        foreach (NavigationObstacle3D obstacle in _avoidanceObstacles)
        {
            RemoveChild(obstacle);
            obstacle.QueueFree();
        }
        _avoidanceObstacles.Clear();
        foreach (NpcNavigationObstacleBounds bounds in _obstacles)
        {
            NavigationObstacle3D obstacle = new()
            {
                Name = "Obstacle_" + SanitizeNodeName(bounds.Id),
                Position = new Vector3(
                    bounds.Center.X,
                    GetNavigationHeight(bounds.Center.X, bounds.Center.Z),
                    bounds.Center.Z),
                Radius = Math.Max(bounds.HalfX, bounds.HalfZ) + AgentRadiusMeters * 0.35f,
                Height = Math.Max(1.0f, bounds.Height),
                AvoidanceEnabled = true,
                AvoidanceLayers = NavigationLayer,
                Use3DAvoidance = true
            };
            // Bind before entering the tree so the obstacle never joins the
            // default world map whose UP remains Vector3.Up.
            obstacle.SetNavigationMap(NavigationMap);
            obstacle.AddToGroup("npc_navigation_obstacle");
            AddChild(obstacle);
            _avoidanceObstacles.Add(obstacle);
        }
        _syncFramesRemaining = Math.Max(_syncFramesRemaining, 2);
    }

    private static IEnumerable<CollisionShape3D> EnumerateCollisionShapes(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is CollisionShape3D collisionShape)
            {
                yield return collisionShape;
            }
            foreach (CollisionShape3D nested in EnumerateCollisionShapes(child))
            {
                yield return nested;
            }
        }
    }

    private static bool TryCreateObstacleBounds(
        CollisionShape3D shapeNode,
        StaticBody3D body,
        out NpcNavigationObstacleBounds obstacle)
    {
        obstacle = null!;
        Vector3 center = shapeNode.GlobalPosition;
        float halfX;
        float halfZ;
        float height;
        switch (shapeNode.Shape)
        {
            case BoxShape3D box:
            {
                Vector3 half = box.Size * 0.5f;
                Basis basis = shapeNode.GlobalTransform.Basis;
                halfX = Math.Abs(basis.X.X) * half.X +
                    Math.Abs(basis.Y.X) * half.Y +
                    Math.Abs(basis.Z.X) * half.Z;
                halfZ = Math.Abs(basis.X.Z) * half.X +
                    Math.Abs(basis.Y.Z) * half.Y +
                    Math.Abs(basis.Z.Z) * half.Z;
                height = box.Size.Y;
                break;
            }
            case CylinderShape3D cylinder:
                halfX = cylinder.Radius;
                halfZ = cylinder.Radius;
                height = cylinder.Height;
                break;
            case CapsuleShape3D capsule:
                halfX = capsule.Radius;
                halfZ = capsule.Radius;
                height = capsule.Height;
                break;
            case SphereShape3D sphere:
                halfX = sphere.Radius;
                halfZ = sphere.Radius;
                height = sphere.Radius * 2.0f;
                break;
            default:
                return false;
        }
        if (halfX <= 0.01f || halfZ <= 0.01f)
        {
            return false;
        }
        obstacle = new NpcNavigationObstacleBounds(
            body.GetPath().ToString(),
            center,
            halfX,
            halfZ,
            height);
        return true;
    }

    private bool ObstacleIntersectsGround(NpcNavigationObstacleBounds obstacle)
    {
        Rect2 bounds = new(
            obstacle.Center.X - obstacle.HalfX,
            obstacle.Center.Z - obstacle.HalfZ,
            obstacle.HalfX * 2.0f,
            obstacle.HalfZ * 2.0f);
        return bounds.Intersects(_groundXZ, true);
    }

    private void RecountWalkableCells()
    {
        _walkableCells = 0;
        foreach (NavigationRegion3D region in _regions.Values)
        {
            _walkableCells += region.NavigationMesh?.GetPolygonCount() ?? 0;
        }
    }

    private Vector3 ToSurfaceLogical(Vector3 worldPosition)
    {
        return GetParent() is Node3D surfaceRoot
            ? surfaceRoot.ToLocal(worldPosition)
            : worldPosition;
    }

    private Vector3 SurfaceLogicalToWorld(Vector3 logicalPosition)
    {
        return GetParent() is Node3D surfaceRoot
            ? surfaceRoot.ToGlobal(logicalPosition)
            : logicalPosition;
    }

    private static NpcNavigationTileKey ToTileKey(Vector3 position)
    {
        return new NpcNavigationTileKey(
            (int)Math.Floor(position.X / TileSizeMeters),
            (int)Math.Floor(position.Z / TileSizeMeters));
    }

    private static string SanitizeNodeName(string value)
    {
        char[] characters = value.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(characters);
    }
}
