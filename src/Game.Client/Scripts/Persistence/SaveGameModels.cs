using System;
using System.Collections.Generic;

public sealed record PlayerSaveData(
    string PlayerId,
    double PositionX,
    double PositionY,
    double PositionZ,
    string CurrentPlanetId);

public sealed record ShipSaveData(
    string ShipId,
    string TemplateId,
    string DisplayName,
    double Health,
    double Fuel,
    double PositionX,
    double PositionY,
    double PositionZ);

public sealed record InventoryItemSaveData(
    string ItemId,
    string DefinitionId,
    int Quantity,
    double Durability);

public sealed record VisitedPlanetSaveData(
    string PlanetId,
    string SystemId,
    string FirstVisitedUtc,
    int VisitCount);

public sealed record SaveGameSnapshot(
    string SlotId,
    int Revision,
    int GeneratorVersion,
    int ContentVersion,
    string UpdatedUtc,
    PlayerSaveData Player,
    ShipSaveData Ship,
    IReadOnlyList<InventoryItemSaveData> Inventory,
    VisitedPlanetSaveData VisitedPlanet);

public sealed record SaveDatabaseDiagnostics(
    int SchemaVersion,
    string JournalMode,
    bool ForeignKeysEnabled,
    int SynchronousMode,
    int BusyTimeoutMilliseconds,
    string IntegrityResult,
    long DatabaseBytes,
    int InventoryRows,
    int VisitedPlanetRows,
    int QueuedWrites,
    int CompletedWrites,
    int MaximumConcurrentWriters);

public sealed record SavePrototypeAcceptanceReport(
    bool Passed,
    string Result,
    SaveGameSnapshot? LoadedSnapshot,
    SaveDatabaseDiagnostics Diagnostics,
    int ConcurrentWritesSubmitted,
    int ExactComparisons,
    double ElapsedMilliseconds);
