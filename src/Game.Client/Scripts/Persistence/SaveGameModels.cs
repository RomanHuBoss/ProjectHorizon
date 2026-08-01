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
