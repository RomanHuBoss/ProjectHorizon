using System;
using System.Collections.Generic;

public enum ContentResolutionState
{
    Known = 0,
    Aliased = 1,
    Placeholder = 2
}

public enum AutosaveTrigger
{
    Periodic = 0,
    Landing = 1,
    Takeoff = 2,
    Hyperspace = 3,
    QuestCompleted = 4,
    ShipPurchased = 5,
    BaseChanged = 6,
    GracefulExit = 7
}

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
    double PositionZ,
    string OriginalTemplateId = "",
    ContentResolutionState TemplateResolution = ContentResolutionState.Known);

public sealed record InventoryItemSaveData(
    string ItemId,
    string DefinitionId,
    int Quantity,
    double Durability,
    string OriginalDefinitionId = "",
    ContentResolutionState Resolution = ContentResolutionState.Known,
    int Quality = 100,
    int Purity = 100,
    int Stability = 100);

public sealed record InventoryItemPropertiesSaveData(
    string ItemId,
    int Quality,
    int Purity,
    int Stability);

public sealed record VisitedPlanetSaveData(
    string PlanetId,
    string SystemId,
    string FirstVisitedUtc,
    int VisitCount);

public sealed record TechnologyProgressSaveData(
    int ResearchPoints,
    IReadOnlyList<string> UnlockedTechnologyIds);

public enum ProductionQueueJobStatus
{
    Queued = 0,
    Running = 1,
    Paused = 2
}

public sealed record ProductionQueueStackSaveData(
    string DefinitionId,
    int Quantity);

public sealed record ProductionQueueJobSaveData(
    string JobId,
    string RecipeId,
    int RequestedBatches,
    double DurationSeconds,
    double ElapsedSeconds,
    ProductionQueueJobStatus Status,
    int SlotIndex,
    long JobSequence,
    long ProcessSequence,
    double ReservedEnergy,
    double TemperatureKelvin,
    double PressureKPa,
    bool IsVacuum,
    IReadOnlyList<ProductionQueueStackSaveData> ReservedInputs,
    IReadOnlyList<ProductionQueueStackSaveData> ReservedCatalysts);

public sealed record ProductionQueueSaveData(
    string StationId,
    double EnergyRemaining,
    long NextJobSequence,
    long NextProcessSequence,
    IReadOnlyList<ProductionQueueJobSaveData> Jobs);

public sealed record SaveGameSnapshot(
    string SlotId,
    int Revision,
    int GeneratorVersion,
    int ContentVersion,
    string UpdatedUtc,
    PlayerSaveData Player,
    ShipSaveData Ship,
    IReadOnlyList<InventoryItemSaveData> Inventory,
    VisitedPlanetSaveData VisitedPlanet,
    TechnologyProgressSaveData? TechnologyProgress = null,
    ProductionQueueSaveData? ProductionQueue = null);

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
    int MaximumConcurrentWriters,
    bool BackupExists = false,
    long BackupBytes = 0,
    string BackupIntegrityResult = "missing");

public sealed record SavePrototypeAcceptanceReport(
    bool Passed,
    string Result,
    SaveGameSnapshot? LoadedSnapshot,
    SaveDatabaseDiagnostics Diagnostics,
    int ConcurrentWritesSubmitted,
    int ExactComparisons,
    double ElapsedMilliseconds);

public sealed record SaveBackupReport(
    bool Succeeded,
    string Result,
    string BackupPath,
    SaveGameSnapshot? Snapshot,
    string IntegrityResult,
    long BackupBytes,
    bool AtomicReplacementUsed,
    string Sha256,
    double ElapsedMilliseconds);

public sealed record SaveRecoveryReport(
    bool Recovered,
    bool PrimaryWasValid,
    string Result,
    SaveGameSnapshot? Snapshot,
    string PrimaryIntegrityResult,
    string BackupIntegrityResult,
    bool AtomicReplacementUsed,
    string QuarantinePath,
    double ElapsedMilliseconds);

public sealed record SaveRecoveryAcceptanceReport(
    bool Passed,
    string Result,
    SaveGameSnapshot? RecoveredSnapshot,
    SaveDatabaseDiagnostics Diagnostics,
    int ProtectedRevision,
    int NewerRevision,
    bool CandidateRejected,
    bool BackupPreserved,
    bool CorruptionDetected,
    bool AtomicReplacementUsed,
    bool QuarantinePreserved,
    bool RecoveryLogWritten,
    int ExactComparisons,
    string BackupSha256Before,
    string BackupSha256After,
    double ElapsedMilliseconds);

public sealed record SaveMigrationReport(
    bool Migrated,
    bool Succeeded,
    string Result,
    int FromSchemaVersion,
    int ToSchemaVersion,
    int FromContentVersion,
    int ToContentVersion,
    string PreservedSourcePath,
    bool SourcePreserved,
    bool AtomicReplacementUsed,
    int AliasedReferences,
    int PlaceholderReferences,
    string SourceSha256,
    string PreservedSha256,
    double ElapsedMilliseconds);

public sealed record SaveMigrationAcceptanceReport(
    bool Passed,
    string Result,
    SaveGameSnapshot? LoadedSnapshot,
    SaveDatabaseDiagnostics Diagnostics,
    SaveMigrationReport Migration,
    bool LegacySourceUnchanged,
    bool AliasResolved,
    bool UnknownItemPreserved,
    bool UnknownShipPreserved,
    bool RoundTripPreserved,
    int ExactContentChecks,
    double ElapsedMilliseconds);

public sealed record SaveAutosaveAcceptanceReport(
    bool Passed,
    string Result,
    SaveGameSnapshot? LoadedSnapshot,
    SaveDatabaseDiagnostics Diagnostics,
    int TriggerTypesCovered,
    int RequestedSaves,
    int CompletedBatches,
    int CoalescedRequests,
    bool PeriodicTriggered,
    bool GracefulExitFlushed,
    bool ExactRoundTrip,
    bool LogWritten,
    double ElapsedMilliseconds);
