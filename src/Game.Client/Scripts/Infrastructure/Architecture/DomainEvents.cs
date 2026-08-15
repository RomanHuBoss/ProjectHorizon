using System;

/// <summary>
/// Marker contract for typed business events exchanged outside Godot scene-local signals.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Gets the UTC instant at which the domain event occurred.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>Raised when an inventory definition is added to an authoritative inventory.</summary>
public sealed record ItemAdded(
    string DefinitionId,
    int Quantity,
    string Source,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised when an inventory definition is removed from an authoritative inventory.</summary>
public sealed record ItemRemoved(
    string DefinitionId,
    int Quantity,
    string Source,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised after a world resource node has been mined successfully.</summary>
public sealed record ResourceMined(
    string ResourceNodeId,
    string DefinitionId,
    int Quantity,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised after the player transitions into an active planetary runtime.</summary>
public sealed record PlanetEntered(
    string PlanetId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised after the player leaves an active planetary runtime.</summary>
public sealed record PlanetExited(
    string PlanetId,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised when a star system is newly reached or discovered.</summary>
public sealed record SystemDiscovered(
    string SystemId,
    int VisitedSystemCount,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised when a quest changes from offered to accepted.</summary>
public sealed record QuestAccepted(
    string QuestId,
    string QuestSource,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised when a quest reaches its claimed/completed terminal state.</summary>
public sealed record QuestCompleted(
    string QuestId,
    string QuestSource,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised when a player-controlled ship system receives damage.</summary>
public sealed record ShipDamaged(
    string SystemId,
    double Damage,
    double RemainingHealth,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised after a base module has been placed into the authoritative base graph.</summary>
public sealed record BaseModulePlaced(
    string InstanceId,
    string ModuleId,
    int GridX,
    int GridZ,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>Raised whenever the persistence queue receives a new save request.</summary>
public sealed record SaveRequested(
    string SlotId,
    int Revision,
    string Trigger,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
