using System;

public enum WorldSceneKind
{
    Surface = 0,
    Orbit = 1,
    StationInterior = 2,
    HyperspaceTransit = 3,
    InterplanetaryTransit = 4
}

public enum WorldSceneTransitionResult
{
    Applied = 0,
    Unchanged = 1,
    Rejected = 2
}

public sealed record WorldSceneContext(
    WorldSceneKind Kind,
    string SystemId,
    string PlanetId)
{
    public static WorldSceneContext Create(
        WorldSceneKind kind,
        string systemId,
        string planetId)
    {
        string normalizedSystemId = NormalizeId(systemId, nameof(systemId));
        string normalizedPlanetId = NormalizeId(planetId, nameof(planetId));
        return new WorldSceneContext(kind, normalizedSystemId, normalizedPlanetId);
    }

    private static string NormalizeId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "World scene context identifiers must not be empty.",
                parameterName);
        }

        return value.Trim();
    }
}

/// <summary>
/// Exact in-memory snapshot used by self-restoring runtime acceptance and by the
/// Godot coordinator when a staged scene transition has to roll back. This is
/// deliberately not persistence state: voyage/galaxy remain the save source of
/// truth for the active world location.
/// </summary>
public sealed record WorldSceneCoordinatorRuntimeSnapshot(
    WorldSceneContext Current,
    int Generation,
    int TransitionCount,
    int RejectedTransitions,
    int HyperspaceTransitions);

/// <summary>
/// Owns the application-level world-scene state machine. It contains no Godot
/// objects and derives scene residency from already persisted voyage/galaxy state.
/// </summary>
public sealed class WorldSceneCoordinatorRuntime
{
    public WorldSceneCoordinatorRuntime(WorldSceneContext initialContext)
    {
        ArgumentNullException.ThrowIfNull(initialContext);
        ValidateContext(initialContext);
        Current = initialContext;
        Generation = 1;
    }

    public WorldSceneContext Current { get; private set; }

    public int Generation { get; private set; }

    public int TransitionCount { get; private set; }

    public int RejectedTransitions { get; private set; }

    public int HyperspaceTransitions { get; private set; }

    public WorldSceneCoordinatorRuntimeSnapshot CaptureSnapshot()
    {
        return new WorldSceneCoordinatorRuntimeSnapshot(
            Current,
            Generation,
            TransitionCount,
            RejectedTransitions,
            HyperspaceTransitions);
    }

    public WorldSceneTransitionResult TryTransition(
        WorldSceneContext next,
        out string result)
    {
        ArgumentNullException.ThrowIfNull(next);
        ValidateContext(next);

        if (Current == next)
        {
            result = "World scene context is already active.";
            return WorldSceneTransitionResult.Unchanged;
        }

        if (!IsAllowedTransition(Current, next))
        {
            RejectedTransitions++;
            result = $"World scene transition {Current.Kind}->{next.Kind} is not allowed.";
            return WorldSceneTransitionResult.Rejected;
        }

        if (next.Kind == WorldSceneKind.HyperspaceTransit)
        {
            HyperspaceTransitions++;
        }

        Current = next;
        TransitionCount++;
        Generation++;
        result = $"World scene transition applied: {next.Kind}.";
        return WorldSceneTransitionResult.Applied;
    }

    public void Restore(WorldSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);
        Current = context;
        Generation++;
    }

    /// <summary>
    /// Restores the exact volatile coordinator state without incrementing any
    /// counters. Used only for transactional rollback / acceptance cleanup.
    /// </summary>
    public void RestoreSnapshot(WorldSceneCoordinatorRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateContext(snapshot.Current);
        if (snapshot.Generation < 1 ||
            snapshot.TransitionCount < 0 ||
            snapshot.RejectedTransitions < 0 ||
            snapshot.HyperspaceTransitions < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                "World scene coordinator snapshot counters are invalid.");
        }

        Current = snapshot.Current;
        Generation = snapshot.Generation;
        TransitionCount = snapshot.TransitionCount;
        RejectedTransitions = snapshot.RejectedTransitions;
        HyperspaceTransitions = snapshot.HyperspaceTransitions;
    }

    public static bool IsAllowedTransition(
        WorldSceneContext current,
        WorldSceneContext next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        ValidateContext(current);
        ValidateContext(next);

        bool sameSystem = string.Equals(
            current.SystemId,
            next.SystemId,
            StringComparison.Ordinal);
        bool samePlanet = string.Equals(
            current.PlanetId,
            next.PlanetId,
            StringComparison.Ordinal);

        return (current.Kind, next.Kind) switch
        {
            (WorldSceneKind.Surface, WorldSceneKind.Orbit) =>
                sameSystem && samePlanet,
            (WorldSceneKind.Orbit, WorldSceneKind.Surface) =>
                sameSystem && samePlanet,
            (WorldSceneKind.Orbit, WorldSceneKind.StationInterior) =>
                sameSystem && samePlanet,
            (WorldSceneKind.StationInterior, WorldSceneKind.Orbit) =>
                sameSystem && samePlanet,
            (WorldSceneKind.StationInterior, WorldSceneKind.HyperspaceTransit) =>
                sameSystem && samePlanet,
            (WorldSceneKind.HyperspaceTransit, WorldSceneKind.StationInterior) =>
                true,
            (WorldSceneKind.Orbit, WorldSceneKind.InterplanetaryTransit) =>
                sameSystem && samePlanet,
            (WorldSceneKind.InterplanetaryTransit, WorldSceneKind.Orbit) =>
                sameSystem,
            _ => false
        };
    }

    public static void ValidateContext(WorldSceneContext context)
    {
        if (!Enum.IsDefined(context.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Kind,
                "Unknown world scene kind.");
        }

        if (string.IsNullOrWhiteSpace(context.SystemId) ||
            string.IsNullOrWhiteSpace(context.PlanetId))
        {
            throw new ArgumentException(
                "World scene context requires stable system and planet IDs.",
                nameof(context));
        }
    }
}
