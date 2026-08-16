using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public readonly record struct AerialGridCell(int X, int Y, int Z)
{
    public override string ToString() => $"{X}:{Y}:{Z}";
}

public sealed record AerialEntitySample(
    string EntityId,
    string Group,
    Vector3 Position,
    Vector3 Velocity,
    float Radius);

public sealed record AerialObstacleSphere(
    string ObstacleId,
    Vector3 Center,
    float Radius);

public sealed record AerialPointOfInterest(
    string PoiId,
    string Group,
    Vector3 Position,
    float Radius);

public sealed record AerialSteeringSnapshot(
    float CellSize,
    int EntityCount,
    int OccupiedCells,
    int ObstacleCount,
    int PointOfInterestCount,
    int GridQueries,
    int FlyingFaunaSamples,
    int ShipSamples,
    int ObstacleAvoidanceActivations,
    int EntityAvoidanceActivations,
    int AltitudeCorrections,
    int PoiSelections,
    int PursuitSamples,
    int EvadeSamples,
    int ArriveSamples,
    int FormationSamples,
    int CombatStateTransitions);

/// <summary>
/// Shared bounded-neighborhood data structure for local 3D steering. It never
/// builds a world-sized navigation volume: entities live only in occupied grid
/// cells and neighbor queries inspect cells intersecting the requested radius.
/// </summary>
public sealed class AerialSteeringRuntime
{
    public const float DefaultCellSizeMeters = 10.0f;

    private readonly float _cellSize;
    private readonly Dictionary<string, AerialEntitySample> _entities =
        new(StringComparer.Ordinal);
    private readonly Dictionary<AerialGridCell, HashSet<string>> _cells = new();
    private readonly Dictionary<AerialGridCell, HashSet<int>> _obstacleCells = new();
    private readonly List<AerialObstacleSphere> _obstacles = new();
    private readonly List<AerialPointOfInterest> _pointsOfInterest = new();

    private int _gridQueries;
    private int _flyingFaunaSamples;
    private int _shipSamples;
    private int _obstacleAvoidanceActivations;
    private int _entityAvoidanceActivations;
    private int _altitudeCorrections;
    private int _poiSelections;
    private int _pursuitSamples;
    private int _evadeSamples;
    private int _arriveSamples;
    private int _formationSamples;
    private int _combatStateTransitions;

    public AerialSteeringRuntime(float cellSize = DefaultCellSizeMeters)
    {
        if (cellSize <= 0.1f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }
        _cellSize = cellSize;
    }

    public float CellSize => _cellSize;

    public IReadOnlyList<AerialObstacleSphere> Obstacles => _obstacles;

    public IReadOnlyList<AerialPointOfInterest> PointsOfInterest =>
        _pointsOfInterest;

    public void ReplaceEnvironment(
        IEnumerable<AerialObstacleSphere> obstacles,
        IEnumerable<AerialPointOfInterest> pointsOfInterest)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        ArgumentNullException.ThrowIfNull(pointsOfInterest);
        _obstacles.Clear();
        _obstacleCells.Clear();
        _obstacles.AddRange(obstacles
            .Where(item => item.Radius > 0.01f)
            .OrderBy(item => item.ObstacleId, StringComparer.Ordinal));
        IndexObstacles();
        _pointsOfInterest.Clear();
        _pointsOfInterest.AddRange(pointsOfInterest
            .OrderBy(item => item.Group, StringComparer.Ordinal)
            .ThenBy(item => item.PoiId, StringComparer.Ordinal));
    }

    public void UpsertEntity(
        string entityId,
        string group,
        Vector3 position,
        Vector3 velocity,
        float radius)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("Entity ID is required.", nameof(entityId));
        }
        if (string.IsNullOrWhiteSpace(group))
        {
            throw new ArgumentException("Entity group is required.", nameof(group));
        }
        radius = Math.Max(0.1f, radius);
        AerialGridCell nextCell = ToCell(position);
        if (_entities.TryGetValue(entityId, out AerialEntitySample? previous))
        {
            AerialGridCell previousCell = ToCell(previous.Position);
            if (!previousCell.Equals(nextCell) &&
                _cells.TryGetValue(previousCell, out HashSet<string>? previousIds))
            {
                previousIds.Remove(entityId);
                if (previousIds.Count == 0)
                {
                    _cells.Remove(previousCell);
                }
            }
        }
        _entities[entityId] = new AerialEntitySample(
            entityId,
            group,
            position,
            velocity,
            radius);
        if (!_cells.TryGetValue(nextCell, out HashSet<string>? ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            _cells.Add(nextCell, ids);
        }
        ids.Add(entityId);
    }

    public void RemoveEntity(string entityId)
    {
        if (!_entities.Remove(entityId, out AerialEntitySample? previous))
        {
            return;
        }
        AerialGridCell cell = ToCell(previous.Position);
        if (_cells.TryGetValue(cell, out HashSet<string>? ids))
        {
            ids.Remove(entityId);
            if (ids.Count == 0)
            {
                _cells.Remove(cell);
            }
        }
    }

    public void RemoveGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return;
        }
        string[] ids = _entities.Values
            .Where(sample => string.Equals(sample.Group, group, StringComparison.Ordinal))
            .Select(sample => sample.EntityId)
            .ToArray();
        foreach (string id in ids)
        {
            RemoveEntity(id);
        }
    }

    public IReadOnlyList<AerialEntitySample> QueryNeighbors(
        Vector3 position,
        float radius,
        string? group = null,
        string? excludeEntityId = null)
    {
        _gridQueries++;
        if (radius <= 0.0f)
        {
            return Array.Empty<AerialEntitySample>();
        }
        int cellRadius = Math.Max(1, (int)Math.Ceiling(radius / _cellSize));
        AerialGridCell center = ToCell(position);
        float radiusSquared = radius * radius;
        List<AerialEntitySample> result = new();
        for (int x = center.X - cellRadius; x <= center.X + cellRadius; x++)
        {
            for (int y = center.Y - cellRadius; y <= center.Y + cellRadius; y++)
            {
                for (int z = center.Z - cellRadius; z <= center.Z + cellRadius; z++)
                {
                    AerialGridCell cell = new(x, y, z);
                    if (!_cells.TryGetValue(cell, out HashSet<string>? ids))
                    {
                        continue;
                    }
                    foreach (string id in ids)
                    {
                        if (string.Equals(id, excludeEntityId, StringComparison.Ordinal) ||
                            !_entities.TryGetValue(id, out AerialEntitySample? sample) ||
                            (group is not null && !string.Equals(
                                group,
                                sample.Group,
                                StringComparison.Ordinal)) ||
                            sample.Position.DistanceSquaredTo(position) > radiusSquared)
                        {
                            continue;
                        }
                        result.Add(sample);
                    }
                }
            }
        }
        result.Sort((left, right) => string.Compare(
            left.EntityId,
            right.EntityId,
            StringComparison.Ordinal));
        return result;
    }

    public AerialPointOfInterest? FindClosestPointOfInterest(
        Vector3 position,
        string group,
        float maximumDistance)
    {
        AerialPointOfInterest? selected = null;
        float best = maximumDistance * maximumDistance;
        foreach (AerialPointOfInterest poi in _pointsOfInterest)
        {
            if (!string.Equals(poi.Group, group, StringComparison.Ordinal))
            {
                continue;
            }
            float distance = poi.Position.DistanceSquaredTo(position);
            if (distance > best)
            {
                continue;
            }
            best = distance;
            selected = poi;
        }
        if (selected is not null)
        {
            _poiSelections++;
        }
        return selected;
    }

    public Vector3 ComputeObstacleAvoidance(
        Vector3 position,
        Vector3 velocity,
        float entityRadius,
        float lookAheadSeconds,
        float strength)
    {
        Vector3 forward = velocity.LengthSquared() > 0.01f
            ? velocity.Normalized()
            : Vector3.Forward;
        float lookAhead = Math.Max(
            entityRadius * 2.0f,
            velocity.Length() * Math.Max(0.1f, lookAheadSeconds));
        Vector3 probe = position + forward * lookAhead;
        Vector3 correction = Vector3.Zero;
        foreach (AerialObstacleSphere obstacle in QueryObstacleCandidates(
            probe,
            entityRadius))
        {
            float safeRadius = obstacle.Radius + entityRadius;
            Vector3 offset = probe - obstacle.Center;
            float distance = offset.Length();
            if (distance >= safeRadius || distance <= 0.0001f)
            {
                continue;
            }
            float weight = (safeRadius - distance) / safeRadius;
            correction += offset.Normalized() * weight * strength;
        }
        if (correction.LengthSquared() > 0.0001f)
        {
            _obstacleAvoidanceActivations++;
        }
        return correction;
    }

    public Vector3 ComputeEntitySeparation(
        string entityId,
        string group,
        Vector3 position,
        float queryRadius,
        float strength)
    {
        Vector3 separation = Vector3.Zero;
        IReadOnlyList<AerialEntitySample> neighbors = QueryNeighbors(
            position,
            queryRadius,
            group,
            entityId);
        foreach (AerialEntitySample neighbor in neighbors)
        {
            Vector3 offset = position - neighbor.Position;
            float distance = Math.Max(0.05f, offset.Length());
            float desired = Math.Max(0.5f, neighbor.Radius + 0.7f);
            if (distance >= queryRadius)
            {
                continue;
            }
            float weight = Math.Clamp(
                (queryRadius - distance) / queryRadius,
                0.0f,
                1.0f);
            if (distance < desired)
            {
                weight += (desired - distance) / desired;
            }
            separation += offset.Normalized() * weight * strength;
        }
        if (separation.LengthSquared() > 0.0001f)
        {
            _entityAvoidanceActivations++;
        }
        return separation;
    }

    public static Vector3 ClampHorizontalAndVerticalSpeed(
        Vector3 localVelocity,
        float maximumHorizontalSpeed,
        float maximumVerticalSpeed)
    {
        maximumHorizontalSpeed = Math.Max(0.0f, maximumHorizontalSpeed);
        maximumVerticalSpeed = Math.Max(0.0f, maximumVerticalSpeed);
        Vector3 horizontal = new(localVelocity.X, 0.0f, localVelocity.Z);
        if (horizontal.Length() > maximumHorizontalSpeed &&
            horizontal.LengthSquared() > 0.000001f)
        {
            horizontal = horizontal.Normalized() * maximumHorizontalSpeed;
        }
        return new Vector3(
            horizontal.X,
            Math.Clamp(localVelocity.Y, -maximumVerticalSpeed, maximumVerticalSpeed),
            horizontal.Z);
    }

    public Vector3 ApplyAltitudeEnvelope(
        Vector3 desiredVelocity,
        float currentY,
        float minimumY,
        float preferredY,
        float maximumY,
        float gain,
        float maximumVerticalSpeed)
    {
        if (minimumY > maximumY)
        {
            (minimumY, maximumY) = (maximumY, minimumY);
        }
        preferredY = Math.Clamp(preferredY, minimumY, maximumY);
        float clampedY = Math.Clamp(currentY, minimumY, maximumY);
        float targetY = preferredY;
        if (currentY < minimumY)
        {
            targetY = minimumY;
        }
        else if (currentY > maximumY)
        {
            targetY = maximumY;
        }
        float correction = (targetY - currentY) * gain;
        if (Math.Abs(currentY - clampedY) > 0.001f ||
            Math.Abs(targetY - currentY) > 0.25f)
        {
            _altitudeCorrections++;
        }
        desiredVelocity.Y = Math.Clamp(
            desiredVelocity.Y + correction,
            -maximumVerticalSpeed,
            maximumVerticalSpeed);
        return desiredVelocity;
    }

    public Vector3 Seek(Vector3 position, Vector3 target, float speed)
    {
        Vector3 delta = target - position;
        return delta.LengthSquared() <= 0.0001f
            ? Vector3.Zero
            : delta.Normalized() * speed;
    }

    public Vector3 Arrive(
        Vector3 position,
        Vector3 target,
        float maximumSpeed,
        float slowRadius,
        float stopRadius)
    {
        _arriveSamples++;
        Vector3 delta = target - position;
        float distance = delta.Length();
        if (distance <= stopRadius || distance <= 0.0001f)
        {
            return Vector3.Zero;
        }
        float speed = distance >= slowRadius
            ? maximumSpeed
            : maximumSpeed * Math.Clamp(
                (distance - stopRadius) /
                Math.Max(0.01f, slowRadius - stopRadius),
                0.0f,
                1.0f);
        return delta / distance * speed;
    }

    public Vector3 Pursuit(
        Vector3 position,
        Vector3 targetPosition,
        Vector3 targetVelocity,
        float maximumSpeed,
        float maximumPredictionSeconds)
    {
        _pursuitSamples++;
        float distance = position.DistanceTo(targetPosition);
        float prediction = maximumSpeed <= 0.01f
            ? 0.0f
            : Math.Min(maximumPredictionSeconds, distance / maximumSpeed);
        return Seek(
            position,
            targetPosition + targetVelocity * prediction,
            maximumSpeed);
    }

    public Vector3 Evade(
        Vector3 position,
        Vector3 threatPosition,
        Vector3 threatVelocity,
        float maximumSpeed,
        float maximumPredictionSeconds)
    {
        _evadeSamples++;
        float distance = position.DistanceTo(threatPosition);
        float prediction = maximumSpeed <= 0.01f
            ? 0.0f
            : Math.Min(maximumPredictionSeconds, distance / maximumSpeed);
        Vector3 predicted = threatPosition + threatVelocity * prediction;
        Vector3 delta = position - predicted;
        return delta.LengthSquared() <= 0.0001f
            ? Vector3.Right * maximumSpeed
            : delta.Normalized() * maximumSpeed;
    }

    public Vector3 Formation(
        Vector3 position,
        Vector3 formationTarget,
        float maximumSpeed,
        float slowRadius,
        float stopRadius)
    {
        _formationSamples++;
        return Arrive(
            position,
            formationTarget,
            maximumSpeed,
            slowRadius,
            stopRadius);
    }

    public void RecordFlyingFaunaSample() => _flyingFaunaSamples++;

    public void RecordShipSample() => _shipSamples++;

    public void RecordCombatStateTransition() => _combatStateTransitions++;

    public AerialSteeringSnapshot CreateSnapshot()
    {
        return new AerialSteeringSnapshot(
            _cellSize,
            _entities.Count,
            _cells.Count,
            _obstacles.Count,
            _pointsOfInterest.Count,
            _gridQueries,
            _flyingFaunaSamples,
            _shipSamples,
            _obstacleAvoidanceActivations,
            _entityAvoidanceActivations,
            _altitudeCorrections,
            _poiSelections,
            _pursuitSamples,
            _evadeSamples,
            _arriveSamples,
            _formationSamples,
            _combatStateTransitions);
    }

    public bool IsInsideAnyObstacle(Vector3 point, float margin = 0.0f)
    {
        return QueryObstacleCandidates(point, Math.Max(0.0f, margin)).Any(obstacle =>
            obstacle.Center.DistanceTo(point) < obstacle.Radius + margin);
    }

    public void ClearRuntimeEntities()
    {
        _entities.Clear();
        _cells.Clear();
    }


    private void IndexObstacles()
    {
        for (int index = 0; index < _obstacles.Count; index++)
        {
            AerialObstacleSphere obstacle = _obstacles[index];
            Vector3 extent = Vector3.One * obstacle.Radius;
            AerialGridCell minimum = ToCell(obstacle.Center - extent);
            AerialGridCell maximum = ToCell(obstacle.Center + extent);
            for (int x = minimum.X; x <= maximum.X; x++)
            {
                for (int y = minimum.Y; y <= maximum.Y; y++)
                {
                    for (int z = minimum.Z; z <= maximum.Z; z++)
                    {
                        AerialGridCell cell = new(x, y, z);
                        if (!_obstacleCells.TryGetValue(cell, out HashSet<int>? ids))
                        {
                            ids = new HashSet<int>();
                            _obstacleCells.Add(cell, ids);
                        }
                        ids.Add(index);
                    }
                }
            }
        }
    }

    private IReadOnlyList<AerialObstacleSphere> QueryObstacleCandidates(
        Vector3 point,
        float margin)
    {
        if (_obstacles.Count == 0)
        {
            return Array.Empty<AerialObstacleSphere>();
        }
        AerialGridCell center = ToCell(point);
        int cellRadius = Math.Max(1, (int)Math.Ceiling(
            Math.Max(0.0f, margin) / _cellSize));
        HashSet<int> indices = new();
        for (int x = center.X - cellRadius; x <= center.X + cellRadius; x++)
        {
            for (int y = center.Y - cellRadius; y <= center.Y + cellRadius; y++)
            {
                for (int z = center.Z - cellRadius; z <= center.Z + cellRadius; z++)
                {
                    if (!_obstacleCells.TryGetValue(
                        new AerialGridCell(x, y, z),
                        out HashSet<int>? cellIndices))
                    {
                        continue;
                    }
                    indices.UnionWith(cellIndices);
                }
            }
        }
        return indices
            .OrderBy(index => index)
            .Select(index => _obstacles[index])
            .ToArray();
    }

    private AerialGridCell ToCell(Vector3 position)
    {
        return new AerialGridCell(
            (int)Math.Floor(position.X / _cellSize),
            (int)Math.Floor(position.Y / _cellSize),
            (int)Math.Floor(position.Z / _cellSize));
    }
}
