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
    GracefulExit = 7,
    DiscoveryChanged = 8,
    ShipChanged = 9,
    PlayerChanged = 10,
    NpcChanged = 11
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

public sealed record ProductionQueueNetworkSaveData(
    IReadOnlyList<ProductionQueueSaveData> Stations);

public enum StationServiceQuestStatus
{
    Offered = 0,
    Accepted = 1,
    ReadyToClaim = 2,
    Completed = 3
}

public sealed record StationServiceStockSaveData(
    string DefinitionId,
    int Quantity);

public sealed record StationServiceQuestSaveData(
    string QuestId,
    StationServiceQuestStatus Status,
    string CurrentNodeId,
    int Progress);

public sealed record StationServicesSaveData(
    string MarketId,
    string NpcId,
    int PlayerCredits,
    int MerchantCredits,
    int Reputation,
    long DayIndex,
    long LastEconomyUpdateUnixSeconds,
    IReadOnlyList<StationServiceStockSaveData> Stock,
    IReadOnlyList<StationServiceQuestSaveData> Quests);

public sealed record BaseConstructionStockSaveData(
    string ModuleId,
    int Quantity);

public sealed record BaseConstructionModuleSaveData(
    string InstanceId,
    string ModuleId,
    int GridX,
    int GridZ,
    int RotationQuarterTurns,
    bool Enabled);

public sealed record BaseConstructionSaveData(
    string BaseId,
    long NextSequence,
    double StoredEnergy,
    IReadOnlyList<BaseConstructionStockSaveData> Stock,
    IReadOnlyList<BaseConstructionModuleSaveData> Modules);

public sealed record PlanetaryPoiStateSaveData(
    string InstanceId,
    string PoiTypeId,
    bool Discovered,
    bool Resolved,
    string CustomName);

public sealed record PlanetaryExplorationSaveData(
    long WorldSeed,
    string RegionKey,
    int DiscoveryPoints,
    IReadOnlyList<PlanetaryPoiStateSaveData> Pois);

public sealed record ShipModuleInstallationSaveData(
    string ModuleId,
    string SlotType,
    int SlotIndex);

public sealed record ShipSystemHealthSaveData(
    string SystemId,
    double Health);

public sealed record ShipSystemsSaveData(
    string ShipClassId,
    double Fuel,
    IReadOnlyList<ShipModuleInstallationSaveData> InstalledModules,
    IReadOnlyList<ShipSystemHealthSaveData> Systems,
    bool? Commissioned = null);

public sealed record StageOneVoyageSaveData(
    StageOneVoyageLocation Location,
    bool Piloted,
    bool StationVisited,
    bool StationVisitedThisLoop,
    int TakeoffCount,
    int DockingCount,
    int LandingCount,
    int CompletedLoops,
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double VelocityX,
    double VelocityY,
    double VelocityZ,
    string LastCheckpoint);

public sealed record GalaxyNavigationSaveData(
    long UniverseSeed,
    string GalaxyId,
    string CurrentSystemId,
    int CurrentSectorX,
    int CurrentSectorY,
    int CurrentSectorZ,
    string SelectedDestinationSystemId,
    int SelectedSectorX,
    int SelectedSectorY,
    int SelectedSectorZ,
    int JumpCount,
    double TotalDistanceLightYears,
    IReadOnlyList<string> VisitedSystemIds);

public sealed record EcologySaveData(
    long WorldSeed,
    string RegionKey,
    int DiscoveryPoints,
    IReadOnlyList<string> DiscoveredFloraIds,
    IReadOnlyList<string> DiscoveredFaunaIds,
    IReadOnlyList<string> RemovedFloraInstanceIds);

public sealed record ProceduralQuestStateSaveData(
    string QuestId,
    ProceduralQuestStatus Status,
    int Progress);

public sealed record ProceduralQuestSaveData(
    long WorldSeed,
    int BoardRevision,
    IReadOnlyList<ProceduralQuestStateSaveData> States);

public sealed record PlayerSurvivalSaveData(
    double Health,
    double Shield,
    double Stamina,
    double LifeSupport,
    double HazardProtection,
    double Oxygen,
    double JetpackEnergy,
    double MultitoolEnergy,
    string ActiveMultitoolFunction,
    IReadOnlyList<string> InstalledSuitModuleIds,
    IReadOnlyList<string> InstalledMultitoolModuleIds);

public sealed record NpcFactionReputationSaveData(
    string FactionId,
    int Reputation);

public sealed record NpcFactionAgentStateSaveData(
    string NpcId,
    double Health,
    bool Interacted,
    bool Defeated,
    int DefeatCount);

public sealed record NpcFactionSaveData(
    long WorldSeed,
    string RegionKey,
    IReadOnlyList<NpcFactionReputationSaveData> Reputations,
    IReadOnlyList<NpcFactionAgentStateSaveData> Agents);

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
    ProductionQueueSaveData? ProductionQueue = null,
    ProductionQueueNetworkSaveData? ProductionQueueNetwork = null,
    StationServicesSaveData? StationServices = null,
    BaseConstructionSaveData? BaseConstruction = null,
    PlanetaryExplorationSaveData? PlanetaryExploration = null,
    ShipSystemsSaveData? ShipSystems = null,
    StageOneVoyageSaveData? StageOneVoyage = null,
    GalaxyNavigationSaveData? GalaxyNavigation = null,
    EcologySaveData? Ecology = null,
    ProceduralQuestSaveData? ProceduralQuests = null,
    PlayerSurvivalSaveData? PlayerSurvival = null,
    NpcFactionSaveData? NpcFactions = null);

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
