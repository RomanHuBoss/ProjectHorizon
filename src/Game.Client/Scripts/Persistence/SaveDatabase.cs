using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public sealed partial class SaveDatabase : IDisposable
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentContentVersion = 2;
    public const int BusyTimeoutMilliseconds = 5000;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _queuedWrites;
    private int _completedWrites;
    private int _activeWriters;
    private int _maximumConcurrentWriters;
    private bool _disposed;

    public SaveDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Database path must not be empty.",
                nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public int QueuedWrites => Volatile.Read(ref _queuedWrites);

    public int CompletedWrites => Volatile.Read(ref _completedWrites);

    public int MaximumConcurrentWriters =>
        Volatile.Read(ref _maximumConcurrentWriters);

    public Task<SaveDatabaseDiagnostics> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(slotId: null);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return ReadDiagnosticsCore(connection, string.Empty);
            },
            cancellationToken);
    }

    public Task SaveAsync(
        SaveGameSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(snapshot.SlotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);

                bool hadPreviousSnapshot = TryLoadSnapshotCore(
                    connection,
                    snapshot.SlotId,
                    out _);
                if (hadPreviousSnapshot)
                {
                    CreateValidatedBackupCore(connection, snapshot.SlotId);
                }

                SaveSnapshotCore(connection, snapshot);
                ValidateExpectedSnapshotCore(connection, snapshot);

                if (!hadPreviousSnapshot)
                {
                    CreateValidatedBackupCore(connection, snapshot.SlotId);
                }

                return true;
            },
            cancellationToken);
    }

    public Task<SaveGameSnapshot?> LoadAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return LoadSnapshotCore(connection, slotId);
            },
            cancellationToken);
    }

    public Task ResetSlotAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException(
                "Slot ID must not be empty.",
                nameof(slotId));
        }

        return EnqueueWriteAsync(
            () =>
            {
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);

                if (TryLoadSnapshotCore(connection, slotId, out _))
                {
                    CreateValidatedBackupCore(connection, slotId);
                }

                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM save_meta WHERE slot_id = $slot_id;";
                command.Parameters.AddWithValue("$slot_id", slotId);
                command.ExecuteNonQuery();
                transaction.Commit();
                return true;
            },
            cancellationToken);
    }

    public async Task<SavePrototypeAcceptanceReport> RunAcceptanceAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        const int concurrentWriteCount = 8;
        int exactComparisons = 0;

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await ResetSlotAsync(slotId, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot baseline = CreateAcceptanceSnapshot(
                slotId,
                revision: 1,
                playerOffset: 0.0,
                oreQuantity: 12,
                visitCount: 1);
            await SaveAsync(baseline, cancellationToken).ConfigureAwait(false);

            SaveGameSnapshot? firstLoad = await LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (!SnapshotsEqual(baseline, firstLoad, out string firstMismatch))
            {
                return BuildFailure(
                    $"baseline mismatch: {firstMismatch}",
                    firstLoad,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            exactComparisons++;

            SaveGameSnapshot finalSnapshot = CreateAcceptanceSnapshot(
                slotId,
                revision: 2,
                playerOffset: 17.5,
                oreQuantity: 57,
                visitCount: 3);

            int completedBefore = CompletedWrites;
            Task[] writes = Enumerable.Range(0, concurrentWriteCount)
                .Select(_ => SaveAsync(finalSnapshot, cancellationToken))
                .ToArray();
            await Task.WhenAll(writes).ConfigureAwait(false);

            int completedDelta = CompletedWrites - completedBefore;
            if (completedDelta != concurrentWriteCount)
            {
                return BuildFailure(
                    $"write queue completed {completedDelta}/" +
                    concurrentWriteCount,
                    null,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            SaveGameSnapshot? finalLoad = await LoadAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);
            if (!SnapshotsEqual(finalSnapshot, finalLoad, out string finalMismatch))
            {
                return BuildFailure(
                    $"final mismatch: {finalMismatch}",
                    finalLoad,
                    slotId,
                    concurrentWriteCount,
                    exactComparisons,
                    stopwatch);
            }

            exactComparisons++;
            SaveDatabaseDiagnostics diagnostics = await ReadDiagnosticsAsync(
                slotId,
                cancellationToken).ConfigureAwait(false);

            bool passed =
                diagnostics.SchemaVersion == CurrentSchemaVersion &&
                string.Equals(
                    diagnostics.JournalMode,
                    "wal",
                    StringComparison.OrdinalIgnoreCase) &&
                diagnostics.ForeignKeysEnabled &&
                diagnostics.SynchronousMode == 1 &&
                diagnostics.BusyTimeoutMilliseconds == BusyTimeoutMilliseconds &&
                string.Equals(
                    diagnostics.IntegrityResult,
                    "ok",
                    StringComparison.OrdinalIgnoreCase) &&
                diagnostics.InventoryRows == finalSnapshot.Inventory.Count &&
                diagnostics.VisitedPlanetRows == 1 &&
                diagnostics.MaximumConcurrentWriters == 1 &&
                exactComparisons == 2;

            stopwatch.Stop();
            string result = passed
                ? "migration, WAL, serialized writes and exact round-trip confirmed"
                : "diagnostic criteria not satisfied";

            return new SavePrototypeAcceptanceReport(
                passed,
                result,
                finalLoad,
                diagnostics,
                concurrentWriteCount,
                exactComparisons,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new SavePrototypeAcceptanceReport(
                false,
                $"{exception.GetType().Name}: {exception.Message}",
                null,
                EmptyDiagnostics(),
                concurrentWriteCount,
                exactComparisons,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public Task<SaveDatabaseDiagnostics> ReadDiagnosticsAsync(
        string slotId,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareDatabaseCore(slotId);
                using SqliteConnection connection = OpenConnection();
                ApplyMigrations(connection);
                return ReadDiagnosticsCore(connection, slotId);
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    public static SaveGameSnapshot CreateAcceptanceSnapshot(
        string slotId,
        int revision,
        double playerOffset,
        int oreQuantity,
        int visitCount)
    {
        string updatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return new SaveGameSnapshot(
            slotId,
            revision,
            GeneratorVersion: ProjectHorizonGenerator.Version,
            ContentVersion: CurrentContentVersion,
            updatedUtc,
            new PlayerSaveData(
                "player.prototype",
                120.25 + playerOffset,
                42.5,
                -18.75,
                "planet.prototype"),
            new ShipSaveData(
                "ship.prototype",
                "ship.arcade.prototype",
                "Horizon Test Ship",
                87.5,
                63.25,
                135.0 + playerOffset,
                55.0,
                -26.0),
            new List<InventoryItemSaveData>
            {
                new("item.ore", "resource.iron_ore", oreQuantity, 1.0),
                new("item.cell", "component.energy_cell", 4, 0.92),
                new("item.kit", "consumable.repair_kit", 2, 0.78)
            },
            new VisitedPlanetSaveData(
                "planet.prototype",
                "system.prototype",
                updatedUtc,
                visitCount));
    }

    public static bool SnapshotsEqual(
        SaveGameSnapshot expected,
        SaveGameSnapshot? actual,
        out string mismatch)
    {
        if (actual is null)
        {
            mismatch = "loaded snapshot is null";
            return false;
        }

        if (expected.SlotId != actual.SlotId ||
            expected.Revision != actual.Revision ||
            expected.GeneratorVersion != actual.GeneratorVersion ||
            expected.ContentVersion != actual.ContentVersion ||
            expected.UpdatedUtc != actual.UpdatedUtc)
        {
            mismatch = "save_meta differs";
            return false;
        }

        if (expected.Player.PlayerId != actual.Player.PlayerId ||
            expected.Player.CurrentPlanetId != actual.Player.CurrentPlanetId ||
            !NearlyEqual(expected.Player.PositionX, actual.Player.PositionX) ||
            !NearlyEqual(expected.Player.PositionY, actual.Player.PositionY) ||
            !NearlyEqual(expected.Player.PositionZ, actual.Player.PositionZ))
        {
            mismatch = "player_state differs";
            return false;
        }

        TechnologyProgressSaveData? expectedProgress =
            expected.TechnologyProgress;
        TechnologyProgressSaveData? actualProgress =
            actual.TechnologyProgress;
        if ((expectedProgress is null) != (actualProgress is null))
        {
            mismatch = "technology_progress presence differs";
            return false;
        }

        if (expectedProgress is not null && actualProgress is not null)
        {
            string[] expectedUnlocked = expectedProgress.UnlockedTechnologyIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            string[] actualUnlocked = actualProgress.UnlockedTechnologyIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (expectedProgress.ResearchPoints != actualProgress.ResearchPoints ||
                !expectedUnlocked.SequenceEqual(
                    actualUnlocked,
                    StringComparer.Ordinal))
            {
                mismatch = "technology_progress differs";
                return false;
            }
        }

        ProductionQueueSaveData? expectedQueue = expected.ProductionQueue;
        ProductionQueueSaveData? actualQueue = actual.ProductionQueue;
        if ((expectedQueue is null) != (actualQueue is null))
        {
            mismatch = "production_queue presence differs";
            return false;
        }

        if (expectedQueue is not null && actualQueue is not null)
        {
            string expectedQueueJson = JsonSerializer.Serialize(expectedQueue);
            string actualQueueJson = JsonSerializer.Serialize(actualQueue);
            if (!string.Equals(
                expectedQueueJson,
                actualQueueJson,
                StringComparison.Ordinal))
            {
                mismatch = "production_queue differs";
                return false;
            }
        }

        ProductionQueueNetworkSaveData? expectedNetwork =
            expected.ProductionQueueNetwork;
        ProductionQueueNetworkSaveData? actualNetwork =
            actual.ProductionQueueNetwork;
        if ((expectedNetwork is null) != (actualNetwork is null))
        {
            mismatch = "production_queue_network presence differs";
            return false;
        }

        if (expectedNetwork is not null && actualNetwork is not null)
        {
            ProductionQueueNetworkSaveData orderedExpected = new(
                expectedNetwork.Stations
                    .OrderBy(queue => queue.StationId, StringComparer.Ordinal)
                    .ToArray());
            ProductionQueueNetworkSaveData orderedActual = new(
                actualNetwork.Stations
                    .OrderBy(queue => queue.StationId, StringComparer.Ordinal)
                    .ToArray());
            if (!string.Equals(
                JsonSerializer.Serialize(orderedExpected),
                JsonSerializer.Serialize(orderedActual),
                StringComparison.Ordinal))
            {
                mismatch = "production_queue_network differs";
                return false;
            }
        }

        StationServicesSaveData? expectedServices = expected.StationServices;
        StationServicesSaveData? actualServices = actual.StationServices;
        if ((expectedServices is null) != (actualServices is null))
        {
            mismatch = "station_services presence differs";
            return false;
        }

        if (expectedServices is not null && actualServices is not null)
        {
            StationServicesSaveData orderedExpected = CanonicalizeStationServices(
                expectedServices);
            StationServicesSaveData orderedActual = CanonicalizeStationServices(
                actualServices);
            if (!string.Equals(
                JsonSerializer.Serialize(orderedExpected),
                JsonSerializer.Serialize(orderedActual),
                StringComparison.Ordinal))
            {
                mismatch = "station_services differs";
                return false;
            }
        }

        BaseConstructionSaveData? expectedBase = expected.BaseConstruction;
        BaseConstructionSaveData? actualBase = actual.BaseConstruction;
        if ((expectedBase is null) != (actualBase is null))
        {
            mismatch = "base_construction presence differs";
            return false;
        }

        if (expectedBase is not null && actualBase is not null)
        {
            BaseConstructionSaveData orderedExpected =
                CanonicalizeBaseConstruction(expectedBase);
            BaseConstructionSaveData orderedActual =
                CanonicalizeBaseConstruction(actualBase);
            if (!string.Equals(
                JsonSerializer.Serialize(orderedExpected),
                JsonSerializer.Serialize(orderedActual),
                StringComparison.Ordinal))
            {
                mismatch = "base_construction differs";
                return false;
            }
        }

        PlanetaryExplorationSaveData? expectedExploration =
            expected.PlanetaryExploration;
        PlanetaryExplorationSaveData? actualExploration =
            actual.PlanetaryExploration;
        if ((expectedExploration is null) != (actualExploration is null))
        {
            mismatch = "planetary_exploration presence differs";
            return false;
        }

        if (expectedExploration is not null && actualExploration is not null)
        {
            PlanetaryExplorationSaveData orderedExpected =
                CanonicalizePlanetaryExploration(expectedExploration);
            PlanetaryExplorationSaveData orderedActual =
                CanonicalizePlanetaryExploration(actualExploration);
            if (!string.Equals(
                JsonSerializer.Serialize(orderedExpected),
                JsonSerializer.Serialize(orderedActual),
                StringComparison.Ordinal))
            {
                mismatch = "planetary_exploration differs";
                return false;
            }
        }

        ShipSystemsSaveData? expectedShipSystems = expected.ShipSystems;
        ShipSystemsSaveData? actualShipSystems = actual.ShipSystems;
        if ((expectedShipSystems is null) != (actualShipSystems is null))
        {
            mismatch = "ship_systems presence differs";
            return false;
        }

        if (expectedShipSystems is not null && actualShipSystems is not null)
        {
            ShipSystemsSaveData orderedExpected = CanonicalizeShipSystems(
                expectedShipSystems);
            ShipSystemsSaveData orderedActual = CanonicalizeShipSystems(
                actualShipSystems);
            if (!string.Equals(
                JsonSerializer.Serialize(orderedExpected),
                JsonSerializer.Serialize(orderedActual),
                StringComparison.Ordinal))
            {
                mismatch = "ship_systems differs";
                return false;
            }
        }

        StageOneVoyageSaveData? expectedVoyage = expected.StageOneVoyage;
        StageOneVoyageSaveData? actualVoyage = actual.StageOneVoyage;
        if ((expectedVoyage is null) != (actualVoyage is null))
        {
            mismatch = "stage_one_voyage presence differs";
            return false;
        }

        if (expectedVoyage is not null && actualVoyage is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizeStageOneVoyage(expectedVoyage)),
                JsonSerializer.Serialize(CanonicalizeStageOneVoyage(actualVoyage)),
                StringComparison.Ordinal))
        {
            mismatch = "stage_one_voyage differs";
            return false;
        }

        GalaxyNavigationSaveData? expectedGalaxy = expected.GalaxyNavigation;
        GalaxyNavigationSaveData? actualGalaxy = actual.GalaxyNavigation;
        if ((expectedGalaxy is null) != (actualGalaxy is null))
        {
            mismatch = "galaxy_navigation presence differs";
            return false;
        }

        if (expectedGalaxy is not null && actualGalaxy is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizeGalaxyNavigation(expectedGalaxy)),
                JsonSerializer.Serialize(CanonicalizeGalaxyNavigation(actualGalaxy)),
                StringComparison.Ordinal))
        {
            mismatch = "galaxy_navigation differs";
            return false;
        }

        EcologySaveData? expectedEcology = expected.Ecology;
        EcologySaveData? actualEcology = actual.Ecology;
        if ((expectedEcology is null) != (actualEcology is null))
        {
            mismatch = "ecology presence differs";
            return false;
        }

        if (expectedEcology is not null && actualEcology is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizeEcology(expectedEcology)),
                JsonSerializer.Serialize(CanonicalizeEcology(actualEcology)),
                StringComparison.Ordinal))
        {
            mismatch = "ecology differs";
            return false;
        }

        ProceduralQuestSaveData? expectedProceduralQuests = expected.ProceduralQuests;
        ProceduralQuestSaveData? actualProceduralQuests = actual.ProceduralQuests;
        if ((expectedProceduralQuests is null) != (actualProceduralQuests is null))
        {
            mismatch = "procedural_quests presence differs";
            return false;
        }

        if (expectedProceduralQuests is not null && actualProceduralQuests is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizeProceduralQuests(expectedProceduralQuests)),
                JsonSerializer.Serialize(CanonicalizeProceduralQuests(actualProceduralQuests)),
                StringComparison.Ordinal))
        {
            mismatch = "procedural_quests differs";
            return false;
        }

        PlayerSurvivalSaveData? expectedPlayerSurvival = expected.PlayerSurvival;
        PlayerSurvivalSaveData? actualPlayerSurvival = actual.PlayerSurvival;
        if ((expectedPlayerSurvival is null) != (actualPlayerSurvival is null))
        {
            mismatch = "player_survival presence differs";
            return false;
        }

        if (expectedPlayerSurvival is not null && actualPlayerSurvival is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizePlayerSurvival(expectedPlayerSurvival)),
                JsonSerializer.Serialize(CanonicalizePlayerSurvival(actualPlayerSurvival)),
                StringComparison.Ordinal))
        {
            mismatch = "player_survival differs";
            return false;
        }

        NpcFactionSaveData? expectedNpcFactions = expected.NpcFactions;
        NpcFactionSaveData? actualNpcFactions = actual.NpcFactions;
        if ((expectedNpcFactions is null) != (actualNpcFactions is null))
        {
            mismatch = "npc_factions presence differs";
            return false;
        }

        if (expectedNpcFactions is not null && actualNpcFactions is not null &&
            !string.Equals(
                JsonSerializer.Serialize(CanonicalizeNpcFactions(expectedNpcFactions)),
                JsonSerializer.Serialize(CanonicalizeNpcFactions(actualNpcFactions)),
                StringComparison.Ordinal))
        {
            mismatch = "npc_factions differs";
            return false;
        }

        if (expected.Ship.ShipId != actual.Ship.ShipId ||
            expected.Ship.TemplateId != actual.Ship.TemplateId ||
            expected.Ship.DisplayName != actual.Ship.DisplayName ||
            expected.Ship.OriginalTemplateId != actual.Ship.OriginalTemplateId ||
            expected.Ship.TemplateResolution != actual.Ship.TemplateResolution ||
            !NearlyEqual(expected.Ship.Health, actual.Ship.Health) ||
            !NearlyEqual(expected.Ship.Fuel, actual.Ship.Fuel) ||
            !NearlyEqual(expected.Ship.PositionX, actual.Ship.PositionX) ||
            !NearlyEqual(expected.Ship.PositionY, actual.Ship.PositionY) ||
            !NearlyEqual(expected.Ship.PositionZ, actual.Ship.PositionZ))
        {
            mismatch = "ships differs";
            return false;
        }

        if (expected.Inventory.Count != actual.Inventory.Count)
        {
            mismatch = "inventory count differs";
            return false;
        }

        Dictionary<string, InventoryItemSaveData> actualItems =
            actual.Inventory.ToDictionary(item => item.ItemId);
        foreach (InventoryItemSaveData expectedItem in expected.Inventory)
        {
            if (!actualItems.TryGetValue(
                    expectedItem.ItemId,
                    out InventoryItemSaveData? actualItem) ||
                actualItem is null)
            {
                mismatch = $"inventory item {expectedItem.ItemId} is missing";
                return false;
            }

            if (expectedItem.DefinitionId != actualItem.DefinitionId ||
                expectedItem.OriginalDefinitionId != actualItem.OriginalDefinitionId ||
                expectedItem.Resolution != actualItem.Resolution ||
                expectedItem.Quantity != actualItem.Quantity ||
                !NearlyEqual(expectedItem.Durability, actualItem.Durability) ||
                expectedItem.Quality != actualItem.Quality ||
                expectedItem.Purity != actualItem.Purity ||
                expectedItem.Stability != actualItem.Stability)
            {
                mismatch =
                    $"inventory item {expectedItem.ItemId} differs: " +
                    $"expected(definition={expectedItem.DefinitionId}, " +
                    $"original={expectedItem.OriginalDefinitionId}, " +
                    $"resolution={expectedItem.Resolution}, " +
                    $"quantity={expectedItem.Quantity}, " +
                    $"durability={expectedItem.Durability:0.###}, " +
                    $"quality={expectedItem.Quality}, purity={expectedItem.Purity}, " +
                    $"stability={expectedItem.Stability}); " +
                    $"actual(definition={actualItem.DefinitionId}, " +
                    $"original={actualItem.OriginalDefinitionId}, " +
                    $"resolution={actualItem.Resolution}, " +
                    $"quantity={actualItem.Quantity}, " +
                    $"durability={actualItem.Durability:0.###}, " +
                    $"quality={actualItem.Quality}, purity={actualItem.Purity}, " +
                    $"stability={actualItem.Stability})";
                return false;
            }
        }

        if (expected.VisitedPlanet.PlanetId != actual.VisitedPlanet.PlanetId ||
            expected.VisitedPlanet.SystemId != actual.VisitedPlanet.SystemId ||
            expected.VisitedPlanet.FirstVisitedUtc != actual.VisitedPlanet.FirstVisitedUtc ||
            expected.VisitedPlanet.VisitCount != actual.VisitedPlanet.VisitCount)
        {
            mismatch = "visited_planets differs";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private async Task<T> EnqueueWriteAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _queuedWrites);
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _queuedWrites);
            throw;
        }

        Interlocked.Decrement(ref _queuedWrites);
        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int active = Interlocked.Increment(ref _activeWriters);
                    UpdateMaximumConcurrentWriters(active);
                    try
                    {
                        return operation();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeWriters);
                        Interlocked.Increment(ref _completedWrites);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void EnsureParentDirectory()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Database parent directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
    }

    private SqliteConnection OpenConnection()
    {
        ThrowIfDisposed();
        SqliteConnection connection = new(
            $"Data Source={_databasePath};Mode=ReadWriteCreate;" +
            "Cache=Shared;Pooling=False");
        connection.Open();

        ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
        ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
        ExecutePragma(connection, "PRAGMA synchronous = NORMAL;");
        ExecutePragma(
            connection,
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
        return connection;
    }

    private static MigrationTransformSummary ApplyMigrations(
        SqliteConnection connection)
    {
        using SqliteCommand bootstrap = connection.CreateCommand();
        bootstrap.CommandText =
            "CREATE TABLE IF NOT EXISTS schema_migrations (" +
            "version INTEGER PRIMARY KEY, " +
            "applied_utc TEXT NOT NULL);";
        bootstrap.ExecuteNonQuery();

        int currentVersion;
        using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText =
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            currentVersion = Convert.ToInt32(
                versionCommand.ExecuteScalar(),
                CultureInfo.InvariantCulture);
        }

        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Save schema {currentVersion} is newer than supported " +
                $"schema {CurrentSchemaVersion}.");
        }

        if (currentVersion < 1)
        {
            ApplyMigration1(connection);
            currentVersion = 1;
        }

        MigrationTransformSummary summary = MigrationTransformSummary.Empty;
        if (currentVersion < 2)
        {
            summary = ApplyMigration2(connection);
            currentVersion = 2;
        }

        if (currentVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Migration chain stopped at schema {currentVersion}; " +
                $"expected {CurrentSchemaVersion}.");
        }

        return summary;
    }

    private static void ApplyMigration1(SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "CREATE TABLE save_meta (" +
            "slot_id TEXT PRIMARY KEY, " +
            "schema_version INTEGER NOT NULL, " +
            "save_revision INTEGER NOT NULL, " +
            "generator_version INTEGER NOT NULL, " +
            "content_version INTEGER NOT NULL, " +
            "created_utc TEXT NOT NULL, " +
            "updated_utc TEXT NOT NULL);" +
            "CREATE TABLE save_settings (" +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "setting_key TEXT NOT NULL, setting_value TEXT NOT NULL, " +
            "PRIMARY KEY(slot_id, setting_key));" +
            "CREATE TABLE player_state (" +
            "slot_id TEXT PRIMARY KEY REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "player_id TEXT NOT NULL, " +
            "pos_x REAL NOT NULL, pos_y REAL NOT NULL, pos_z REAL NOT NULL, " +
            "current_planet_id TEXT NOT NULL);" +
            "CREATE TABLE ships (" +
            "ship_id TEXT PRIMARY KEY, " +
            "slot_id TEXT NOT NULL UNIQUE REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "template_id TEXT NOT NULL, display_name TEXT NOT NULL, " +
            "health REAL NOT NULL, fuel REAL NOT NULL, " +
            "pos_x REAL NOT NULL, pos_y REAL NOT NULL, pos_z REAL NOT NULL);" +
            "CREATE TABLE containers (" +
            "container_id TEXT PRIMARY KEY, " +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "owner_type TEXT NOT NULL, owner_id TEXT NOT NULL, capacity INTEGER NOT NULL);" +
            "CREATE TABLE inventory_items (" +
            "container_id TEXT NOT NULL REFERENCES containers(container_id) ON DELETE CASCADE, " +
            "item_id TEXT NOT NULL, definition_id TEXT NOT NULL, " +
            "quantity INTEGER NOT NULL CHECK(quantity >= 0), " +
            "durability REAL NOT NULL CHECK(durability >= 0 AND durability <= 1), " +
            "PRIMARY KEY(container_id, item_id));" +
            "CREATE TABLE visited_planets (" +
            "slot_id TEXT NOT NULL REFERENCES save_meta(slot_id) ON DELETE CASCADE, " +
            "planet_id TEXT NOT NULL, system_id TEXT NOT NULL, " +
            "first_visited_utc TEXT NOT NULL, visit_count INTEGER NOT NULL, " +
            "PRIMARY KEY(slot_id, planet_id));" +
            "INSERT INTO schema_migrations(version, applied_utc) " +
            "VALUES (1, $applied_utc);";
        command.Parameters.AddWithValue(
            "$applied_utc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static MigrationTransformSummary ApplyMigration2(
        SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE inventory_items ADD COLUMN " +
            "original_definition_id TEXT NULL;");
        ExecuteNonQuery(
            connection,
            transaction,
            "ALTER TABLE ships ADD COLUMN original_template_id TEXT NULL;");

        List<(long RowId, string DefinitionId)> inventoryRows = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "SELECT rowid, definition_id FROM inventory_items ORDER BY rowid;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                inventoryRows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        int aliases = 0;
        int placeholders = 0;
        foreach ((long rowId, string definitionId) in inventoryRows)
        {
            ContentResolution resolution = ResolveInventoryDefinitionCore(
                definitionId,
                originalDefinitionId: null);
            if (resolution.State == ContentResolutionState.Known)
            {
                continue;
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE inventory_items SET definition_id = $definition_id, " +
                "original_definition_id = $original_definition_id " +
                "WHERE rowid = $row_id;",
                ("$definition_id", resolution.EffectiveId),
                ("$original_definition_id", resolution.OriginalId),
                ("$row_id", rowId));

            if (resolution.State == ContentResolutionState.Aliased)
            {
                aliases++;
            }
            else
            {
                placeholders++;
            }
        }

        List<(long RowId, string TemplateId)> shipRows = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "SELECT rowid, template_id FROM ships ORDER BY rowid;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                shipRows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        foreach ((long rowId, string templateId) in shipRows)
        {
            ContentResolution resolution = ResolveShipTemplateCore(
                templateId,
                originalTemplateId: null);
            if (resolution.State == ContentResolutionState.Known)
            {
                continue;
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE ships SET template_id = $template_id, " +
                "original_template_id = $original_template_id " +
                "WHERE rowid = $row_id;",
                ("$template_id", resolution.EffectiveId),
                ("$original_template_id", resolution.OriginalId),
                ("$row_id", rowId));
            placeholders++;
        }

        string appliedUtc = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);
        ExecuteNonQuery(
            connection,
            transaction,
            "UPDATE save_meta SET schema_version = $schema_version, " +
            "content_version = CASE " +
            "WHEN content_version < $content_version THEN $content_version " +
            "ELSE content_version END;",
            ("$schema_version", CurrentSchemaVersion),
            ("$content_version", CurrentContentVersion));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO schema_migrations(version, applied_utc) " +
            "VALUES(2, $applied_utc);",
            ("$applied_utc", appliedUtc));
        transaction.Commit();
        return new MigrationTransformSummary(aliases, placeholders);
    }

    private static void SaveSnapshotCore(
        SqliteConnection connection,
        SaveGameSnapshot snapshot)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        string createdUtc = snapshot.UpdatedUtc;

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO save_meta(" +
            "slot_id, schema_version, save_revision, generator_version, " +
            "content_version, created_utc, updated_utc) " +
            "VALUES($slot_id, $schema_version, $save_revision, " +
            "$generator_version, $content_version, $created_utc, $updated_utc) " +
            "ON CONFLICT(slot_id) DO UPDATE SET " +
            "schema_version=excluded.schema_version, " +
            "save_revision=excluded.save_revision, " +
            "generator_version=excluded.generator_version, " +
            "content_version=excluded.content_version, " +
            "updated_utc=excluded.updated_utc;",
            ("$slot_id", snapshot.SlotId),
            ("$schema_version", CurrentSchemaVersion),
            ("$save_revision", snapshot.Revision),
            ("$generator_version", snapshot.GeneratorVersion),
            ("$content_version", Math.Max(snapshot.ContentVersion, CurrentContentVersion)),
            ("$created_utc", createdUtc),
            ("$updated_utc", snapshot.UpdatedUtc));

        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO player_state(" +
            "slot_id, player_id, pos_x, pos_y, pos_z, current_planet_id) " +
            "VALUES($slot_id, $player_id, $pos_x, $pos_y, $pos_z, $planet_id) " +
            "ON CONFLICT(slot_id) DO UPDATE SET " +
            "player_id=excluded.player_id, pos_x=excluded.pos_x, " +
            "pos_y=excluded.pos_y, pos_z=excluded.pos_z, " +
            "current_planet_id=excluded.current_planet_id;",
            ("$slot_id", snapshot.SlotId),
            ("$player_id", snapshot.Player.PlayerId),
            ("$pos_x", snapshot.Player.PositionX),
            ("$pos_y", snapshot.Player.PositionY),
            ("$pos_z", snapshot.Player.PositionZ),
            ("$planet_id", snapshot.Player.CurrentPlanetId));

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM save_settings WHERE slot_id = $slot_id AND " +
            "setting_key IN ('research_points', 'unlocked_technologies', " +
            "'production_queue', 'production_queue_network', " +
            "'inventory_properties', 'station_services', " +
            "'base_construction', 'planetary_exploration', 'ship_systems', " +
            "'stage_one_voyage', 'galaxy_navigation', 'ecology', " +
            "'procedural_quests', 'player_survival', 'npc_factions');",
            ("$slot_id", snapshot.SlotId));
        if (snapshot.TechnologyProgress is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'research_points', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", snapshot.TechnologyProgress.ResearchPoints
                    .ToString(CultureInfo.InvariantCulture)));
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'unlocked_technologies', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", string.Join(",",
                    snapshot.TechnologyProgress.UnlockedTechnologyIds
                        .OrderBy(id => id, StringComparer.Ordinal))));
        }

        if (snapshot.ProductionQueue is not null)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'production_queue', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    snapshot.ProductionQueue)));
        }

        if (snapshot.ProductionQueueNetwork is not null)
        {
            ValidateProductionQueueNetwork(snapshot.ProductionQueueNetwork);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'production_queue_network', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    new ProductionQueueNetworkSaveData(
                        snapshot.ProductionQueueNetwork.Stations
                            .OrderBy(
                                queue => queue.StationId,
                                StringComparer.Ordinal)
                            .ToArray()))));
        }

        if (snapshot.StationServices is not null)
        {
            ValidateStationServices(snapshot.StationServices);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'station_services', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeStationServices(snapshot.StationServices))));
        }

        if (snapshot.BaseConstruction is not null)
        {
            ValidateBaseConstruction(snapshot.BaseConstruction);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'base_construction', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeBaseConstruction(snapshot.BaseConstruction))));
        }

        if (snapshot.PlanetaryExploration is not null)
        {
            ValidatePlanetaryExploration(snapshot.PlanetaryExploration);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'planetary_exploration', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizePlanetaryExploration(
                        snapshot.PlanetaryExploration))));
        }

        if (snapshot.ShipSystems is not null)
        {
            if (Math.Abs(snapshot.Ship.Fuel - snapshot.ShipSystems.Fuel) > 0.001)
            {
                throw new InvalidDataException(
                    "ship fuel differs between ships row and ship_systems setting.");
            }

            ValidateShipSystems(snapshot.ShipSystems);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'ship_systems', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeShipSystems(snapshot.ShipSystems))));
        }

        if (snapshot.StageOneVoyage is not null)
        {
            ValidateStageOneVoyage(snapshot.StageOneVoyage);
            if (snapshot.StageOneVoyage.Location !=
                    StageOneVoyageLocation.PlanetSurface &&
                snapshot.ShipSystems?.Commissioned != true)
            {
                throw new InvalidDataException(
                    "active stage_one_voyage requires a commissioned ship.");
            }

            if (Math.Abs(snapshot.Ship.PositionX -
                    snapshot.StageOneVoyage.PositionX) > 0.001 ||
                Math.Abs(snapshot.Ship.PositionY -
                    snapshot.StageOneVoyage.PositionY) > 0.001 ||
                Math.Abs(snapshot.Ship.PositionZ -
                    snapshot.StageOneVoyage.PositionZ) > 0.001)
            {
                throw new InvalidDataException(
                    "ship position differs between ships row and stage_one_voyage setting.");
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'stage_one_voyage', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeStageOneVoyage(snapshot.StageOneVoyage))));
        }

        if (snapshot.GalaxyNavigation is not null)
        {
            ValidateGalaxyNavigation(snapshot.GalaxyNavigation);
            if (!string.Equals(
                snapshot.VisitedPlanet.SystemId,
                snapshot.GalaxyNavigation.CurrentSystemId,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "visited planet system differs from galaxy_navigation current system.");
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'galaxy_navigation', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeGalaxyNavigation(snapshot.GalaxyNavigation))));
        }

        if (snapshot.Ecology is not null)
        {
            ValidateEcology(snapshot.Ecology);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'ecology', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeEcology(snapshot.Ecology))));
        }

        if (snapshot.ProceduralQuests is not null)
        {
            ValidateProceduralQuests(snapshot.ProceduralQuests);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'procedural_quests', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeProceduralQuests(snapshot.ProceduralQuests))));
        }

        if (snapshot.PlayerSurvival is not null)
        {
            ValidatePlayerSurvival(snapshot.PlayerSurvival);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'player_survival', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizePlayerSurvival(snapshot.PlayerSurvival))));
        }

        if (snapshot.NpcFactions is not null)
        {
            ValidateNpcFactions(snapshot.NpcFactions);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
                "VALUES($slot_id, 'npc_factions', $setting_value);",
                ("$slot_id", snapshot.SlotId),
                ("$setting_value", JsonSerializer.Serialize(
                    CanonicalizeNpcFactions(snapshot.NpcFactions))));
        }

        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            if (item.Quality is < 0 or > 100 ||
                item.Purity is < 0 or > 100 ||
                item.Stability is < 0 or > 100)
            {
                throw new InvalidDataException(
                    $"Inventory item {item.ItemId} has properties outside 0..100.");
            }
        }

        InventoryItemPropertiesSaveData[] itemProperties = snapshot.Inventory
            .OrderBy(item => item.ItemId, StringComparer.Ordinal)
            .Select(item => new InventoryItemPropertiesSaveData(
                item.ItemId,
                item.Quality,
                item.Purity,
                item.Stability))
            .ToArray();
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO save_settings(slot_id, setting_key, setting_value) " +
            "VALUES($slot_id, 'inventory_properties', $setting_value);",
            ("$slot_id", snapshot.SlotId),
            ("$setting_value", JsonSerializer.Serialize(itemProperties)));

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM ships WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO ships(" +
            "ship_id, slot_id, template_id, original_template_id, " +
            "display_name, health, fuel, pos_x, pos_y, pos_z) VALUES(" +
            "$ship_id, $slot_id, $template_id, $original_template_id, " +
            "$display_name, $health, $fuel, $pos_x, $pos_y, $pos_z);",
            ("$ship_id", snapshot.Ship.ShipId),
            ("$slot_id", snapshot.SlotId),
            ("$template_id", PersistedShipTemplateCore(snapshot.Ship)),
            ("$original_template_id", PersistedShipOriginalCore(snapshot.Ship)),
            ("$display_name", snapshot.Ship.DisplayName),
            ("$health", snapshot.Ship.Health),
            ("$fuel", snapshot.Ship.Fuel),
            ("$pos_x", snapshot.Ship.PositionX),
            ("$pos_y", snapshot.Ship.PositionY),
            ("$pos_z", snapshot.Ship.PositionZ));

        string containerId = $"{snapshot.SlotId}.player_inventory";
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM containers WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO containers(container_id, slot_id, owner_type, owner_id, capacity) " +
            "VALUES($container_id, $slot_id, 'player', $owner_id, 64);",
            ("$container_id", containerId),
            ("$slot_id", snapshot.SlotId),
            ("$owner_id", snapshot.Player.PlayerId));

        foreach (InventoryItemSaveData item in snapshot.Inventory)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO inventory_items(" +
                "container_id, item_id, definition_id, original_definition_id, " +
                "quantity, durability) VALUES(" +
                "$container_id, $item_id, $definition_id, $original_definition_id, " +
                "$quantity, $durability);",
                ("$container_id", containerId),
                ("$item_id", item.ItemId),
                ("$definition_id", PersistedInventoryDefinitionCore(item)),
                ("$original_definition_id", PersistedInventoryOriginalCore(item)),
                ("$quantity", item.Quantity),
                ("$durability", item.Durability));
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM visited_planets WHERE slot_id = $slot_id;",
            ("$slot_id", snapshot.SlotId));
        ExecuteNonQuery(
            connection,
            transaction,
            "INSERT INTO visited_planets(" +
            "slot_id, planet_id, system_id, first_visited_utc, visit_count) " +
            "VALUES($slot_id, $planet_id, $system_id, $first_visited_utc, $visit_count);",
            ("$slot_id", snapshot.SlotId),
            ("$planet_id", snapshot.VisitedPlanet.PlanetId),
            ("$system_id", snapshot.VisitedPlanet.SystemId),
            ("$first_visited_utc", snapshot.VisitedPlanet.FirstVisitedUtc),
            ("$visit_count", snapshot.VisitedPlanet.VisitCount));

        transaction.Commit();
    }

    private static SaveGameSnapshot? LoadSnapshotCore(
        SqliteConnection connection,
        string slotId)
    {
        int schemaVersion = ExecuteScalarInt(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
        return LoadSnapshotCore(connection, slotId, schemaVersion);
    }

    private static SaveGameSnapshot? LoadSnapshotCore(
        SqliteConnection connection,
        string slotId,
        int schemaVersion)
    {
        int revision;
        int generatorVersion;
        int contentVersion;
        string updatedUtc;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT save_revision, generator_version, content_version, updated_utc " +
                "FROM save_meta WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            revision = reader.GetInt32(0);
            generatorVersion = reader.GetInt32(1);
            contentVersion = reader.GetInt32(2);
            updatedUtc = reader.GetString(3);
        }

        PlayerSaveData player;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT player_id, pos_x, pos_y, pos_z, current_planet_id " +
                "FROM player_state WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("player_state row is missing.");
            }

            player = new PlayerSaveData(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4));
        }

        ShipSaveData ship;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = schemaVersion >= 2
                ? "SELECT ship_id, template_id, original_template_id, " +
                  "display_name, health, fuel, pos_x, pos_y, pos_z " +
                  "FROM ships WHERE slot_id = $slot_id;"
                : "SELECT ship_id, template_id, NULL AS original_template_id, " +
                  "display_name, health, fuel, pos_x, pos_y, pos_z " +
                  "FROM ships WHERE slot_id = $slot_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("ships row is missing.");
            }

            string persistedTemplateId = reader.GetString(1);
            string? originalTemplateId = reader.IsDBNull(2)
                ? null
                : reader.GetString(2);
            ContentResolution templateResolution = ResolveShipTemplateCore(
                persistedTemplateId,
                originalTemplateId);
            ship = new ShipSaveData(
                reader.GetString(0),
                templateResolution.EffectiveId,
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                templateResolution.OriginalId,
                templateResolution.State);
        }

        List<InventoryItemSaveData> inventory = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = schemaVersion >= 2
                ? "SELECT i.item_id, i.definition_id, i.original_definition_id, " +
                  "i.quantity, i.durability FROM inventory_items i " +
                  "JOIN containers c ON c.container_id = i.container_id " +
                  "WHERE c.slot_id = $slot_id ORDER BY i.item_id;"
                : "SELECT i.item_id, i.definition_id, " +
                  "NULL AS original_definition_id, i.quantity, i.durability " +
                  "FROM inventory_items i " +
                  "JOIN containers c ON c.container_id = i.container_id " +
                  "WHERE c.slot_id = $slot_id ORDER BY i.item_id;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string persistedDefinitionId = reader.GetString(1);
                string? originalDefinitionId = reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2);
                ContentResolution resolution = ResolveInventoryDefinitionCore(
                    persistedDefinitionId,
                    originalDefinitionId);
                inventory.Add(new InventoryItemSaveData(
                    reader.GetString(0),
                    resolution.EffectiveId,
                    reader.GetInt32(3),
                    reader.GetDouble(4),
                    resolution.OriginalId,
                    resolution.State));
            }
        }

        TechnologyProgressSaveData? technologyProgress = null;
        ProductionQueueSaveData? productionQueue = null;
        ProductionQueueNetworkSaveData? productionQueueNetwork = null;
        StationServicesSaveData? stationServices = null;
        BaseConstructionSaveData? baseConstruction = null;
        PlanetaryExplorationSaveData? planetaryExploration = null;
        ShipSystemsSaveData? shipSystems = null;
        StageOneVoyageSaveData? stageOneVoyage = null;
        GalaxyNavigationSaveData? galaxyNavigation = null;
        EcologySaveData? ecology = null;
        ProceduralQuestSaveData? proceduralQuests = null;
        PlayerSurvivalSaveData? playerSurvival = null;
        NpcFactionSaveData? npcFactions = null;
        Dictionary<string, string> progressSettings = new(
            StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT setting_key, setting_value FROM save_settings " +
                "WHERE slot_id = $slot_id AND setting_key IN " +
                "('research_points', 'unlocked_technologies', " +
                "'production_queue', 'production_queue_network', " +
                "'inventory_properties', 'station_services', " +
                "'base_construction', 'planetary_exploration', " +
                "'ship_systems', 'stage_one_voyage', 'galaxy_navigation', 'ecology', " +
                "'procedural_quests', 'player_survival', 'npc_factions') ORDER BY setting_key;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                progressSettings[reader.GetString(0)] = reader.GetString(1);
            }
        }

        if (progressSettings.ContainsKey("research_points") ||
            progressSettings.ContainsKey("unlocked_technologies"))
        {
            int researchPoints = progressSettings.TryGetValue(
                    "research_points",
                    out string? researchText) &&
                int.TryParse(
                    researchText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedResearch)
                ? Math.Max(0, parsedResearch)
                : 0;
            string[] unlocked = progressSettings.TryGetValue(
                    "unlocked_technologies",
                    out string? unlockedText)
                ? unlockedText.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            technologyProgress = new TechnologyProgressSaveData(
                researchPoints,
                unlocked);
        }

        if (progressSettings.TryGetValue(
            "production_queue",
            out string? productionQueueJson))
        {
            if (string.IsNullOrWhiteSpace(productionQueueJson))
            {
                throw new InvalidDataException(
                    "production_queue setting is empty.");
            }

            try
            {
                productionQueue = JsonSerializer.Deserialize<
                    ProductionQueueSaveData>(productionQueueJson) ??
                    throw new InvalidDataException(
                        "production_queue setting deserialized to null.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "production_queue setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "production_queue_network",
            out string? productionQueueNetworkJson))
        {
            if (string.IsNullOrWhiteSpace(productionQueueNetworkJson))
            {
                throw new InvalidDataException(
                    "production_queue_network setting is empty.");
            }

            try
            {
                productionQueueNetwork = JsonSerializer.Deserialize<
                    ProductionQueueNetworkSaveData>(
                        productionQueueNetworkJson) ??
                    throw new InvalidDataException(
                        "production_queue_network setting deserialized to null.");
                ValidateProductionQueueNetwork(productionQueueNetwork);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "production_queue_network setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "station_services",
            out string? stationServicesJson))
        {
            if (string.IsNullOrWhiteSpace(stationServicesJson))
            {
                throw new InvalidDataException(
                    "station_services setting is empty.");
            }

            try
            {
                stationServices = JsonSerializer.Deserialize<
                    StationServicesSaveData>(stationServicesJson) ??
                    throw new InvalidDataException(
                        "station_services setting deserialized to null.");
                ValidateStationServices(stationServices);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "station_services setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "base_construction",
            out string? baseConstructionJson))
        {
            if (string.IsNullOrWhiteSpace(baseConstructionJson))
            {
                throw new InvalidDataException(
                    "base_construction setting is empty.");
            }

            try
            {
                baseConstruction = JsonSerializer.Deserialize<
                    BaseConstructionSaveData>(baseConstructionJson) ??
                    throw new InvalidDataException(
                        "base_construction setting deserialized to null.");
                ValidateBaseConstruction(baseConstruction);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "base_construction setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "planetary_exploration",
            out string? planetaryExplorationJson))
        {
            if (string.IsNullOrWhiteSpace(planetaryExplorationJson))
            {
                throw new InvalidDataException(
                    "planetary_exploration setting is empty.");
            }

            try
            {
                planetaryExploration = JsonSerializer.Deserialize<
                    PlanetaryExplorationSaveData>(planetaryExplorationJson) ??
                    throw new InvalidDataException(
                        "planetary_exploration setting deserialized to null.");
                ValidatePlanetaryExploration(planetaryExploration);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "planetary_exploration setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "ship_systems",
            out string? shipSystemsJson))
        {
            if (string.IsNullOrWhiteSpace(shipSystemsJson))
            {
                throw new InvalidDataException(
                    "ship_systems setting is empty.");
            }

            try
            {
                shipSystems = JsonSerializer.Deserialize<
                    ShipSystemsSaveData>(shipSystemsJson) ??
                    throw new InvalidDataException(
                        "ship_systems setting deserialized to null.");
                ValidateShipSystems(shipSystems);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "ship_systems setting contains invalid JSON.",
                    exception);
            }
        }

        if (shipSystems is not null &&
            Math.Abs(ship.Fuel - shipSystems.Fuel) > 0.001)
        {
            throw new InvalidDataException(
                "ship fuel differs between ships row and ship_systems setting.");
        }

        if (progressSettings.TryGetValue(
            "stage_one_voyage",
            out string? stageOneVoyageJson))
        {
            if (string.IsNullOrWhiteSpace(stageOneVoyageJson))
            {
                throw new InvalidDataException(
                    "stage_one_voyage setting is empty.");
            }

            try
            {
                stageOneVoyage = JsonSerializer.Deserialize<
                    StageOneVoyageSaveData>(stageOneVoyageJson) ??
                    throw new InvalidDataException(
                        "stage_one_voyage setting deserialized to null.");
                ValidateStageOneVoyage(stageOneVoyage);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "stage_one_voyage setting contains invalid JSON.",
                    exception);
            }
        }

        if (stageOneVoyage is not null)
        {
            if (stageOneVoyage.Location != StageOneVoyageLocation.PlanetSurface &&
                shipSystems?.Commissioned != true)
            {
                throw new InvalidDataException(
                    "active stage_one_voyage requires a commissioned ship.");
            }

            if (Math.Abs(ship.PositionX - stageOneVoyage.PositionX) > 0.001 ||
                Math.Abs(ship.PositionY - stageOneVoyage.PositionY) > 0.001 ||
                Math.Abs(ship.PositionZ - stageOneVoyage.PositionZ) > 0.001)
            {
                throw new InvalidDataException(
                    "ship position differs between ships row and stage_one_voyage setting.");
            }
        }

        if (progressSettings.TryGetValue(
            "galaxy_navigation",
            out string? galaxyNavigationJson))
        {
            if (string.IsNullOrWhiteSpace(galaxyNavigationJson))
            {
                throw new InvalidDataException(
                    "galaxy_navigation setting is empty.");
            }

            try
            {
                galaxyNavigation = JsonSerializer.Deserialize<
                    GalaxyNavigationSaveData>(galaxyNavigationJson) ??
                    throw new InvalidDataException(
                        "galaxy_navigation setting deserialized to null.");
                ValidateGalaxyNavigation(galaxyNavigation);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "galaxy_navigation setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "ecology",
            out string? ecologyJson))
        {
            if (string.IsNullOrWhiteSpace(ecologyJson))
            {
                throw new InvalidDataException(
                    "ecology setting is empty.");
            }

            try
            {
                ecology = JsonSerializer.Deserialize<EcologySaveData>(ecologyJson) ??
                    throw new InvalidDataException(
                        "ecology setting deserialized to null.");
                ValidateEcology(ecology);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "ecology setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "procedural_quests",
            out string? proceduralQuestsJson))
        {
            if (string.IsNullOrWhiteSpace(proceduralQuestsJson))
            {
                throw new InvalidDataException(
                    "procedural_quests setting is empty.");
            }

            try
            {
                proceduralQuests = JsonSerializer.Deserialize<ProceduralQuestSaveData>(
                    proceduralQuestsJson) ?? throw new InvalidDataException(
                        "procedural_quests setting deserialized to null.");
                ValidateProceduralQuests(proceduralQuests);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "procedural_quests setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "player_survival",
            out string? playerSurvivalJson))
        {
            if (string.IsNullOrWhiteSpace(playerSurvivalJson))
            {
                throw new InvalidDataException(
                    "player_survival setting is empty.");
            }

            try
            {
                playerSurvival = JsonSerializer.Deserialize<PlayerSurvivalSaveData>(
                    playerSurvivalJson) ?? throw new InvalidDataException(
                        "player_survival setting deserialized to null.");
                ValidatePlayerSurvival(playerSurvival);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "player_survival setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "npc_factions",
            out string? npcFactionsJson))
        {
            if (string.IsNullOrWhiteSpace(npcFactionsJson))
            {
                throw new InvalidDataException(
                    "npc_factions setting is empty.");
            }

            try
            {
                npcFactions = JsonSerializer.Deserialize<NpcFactionSaveData>(
                    npcFactionsJson) ?? throw new InvalidDataException(
                        "npc_factions setting deserialized to null.");
                ValidateNpcFactions(npcFactions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "npc_factions setting contains invalid JSON.",
                    exception);
            }
        }

        if (progressSettings.TryGetValue(
            "inventory_properties",
            out string? inventoryPropertiesJson))
        {
            if (string.IsNullOrWhiteSpace(inventoryPropertiesJson))
            {
                throw new InvalidDataException(
                    "inventory_properties setting is empty.");
            }

            try
            {
                InventoryItemPropertiesSaveData[] properties =
                    JsonSerializer.Deserialize<InventoryItemPropertiesSaveData[]>(
                        inventoryPropertiesJson) ??
                    throw new InvalidDataException(
                        "inventory_properties setting deserialized to null.");
                Dictionary<string, InventoryItemPropertiesSaveData> byItemId =
                    new(StringComparer.Ordinal);
                foreach (InventoryItemPropertiesSaveData property in properties)
                {
                    if (!byItemId.TryAdd(property.ItemId, property) ||
                        property.Quality is < 0 or > 100 ||
                        property.Purity is < 0 or > 100 ||
                        property.Stability is < 0 or > 100)
                    {
                        throw new InvalidDataException(
                            "inventory_properties contains duplicate IDs or " +
                            "scores outside 0..100.");
                    }
                }

                for (int index = 0; index < inventory.Count; index++)
                {
                    InventoryItemSaveData item = inventory[index];
                    if (byItemId.TryGetValue(
                        item.ItemId,
                        out InventoryItemPropertiesSaveData? property) &&
                        property is not null)
                    {
                        inventory[index] = item with
                        {
                            Quality = property.Quality,
                            Purity = property.Purity,
                            Stability = property.Stability
                        };
                    }
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "inventory_properties setting contains invalid JSON.",
                    exception);
            }
        }

        VisitedPlanetSaveData visitedPlanet;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT planet_id, system_id, first_visited_utc, visit_count " +
                "FROM visited_planets WHERE slot_id = $slot_id " +
                "ORDER BY planet_id LIMIT 1;";
            command.Parameters.AddWithValue("$slot_id", slotId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidDataException("visited_planets row is missing.");
            }

            visitedPlanet = new VisitedPlanetSaveData(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3));
        }

        if (galaxyNavigation is not null && !string.Equals(
            visitedPlanet.SystemId,
            galaxyNavigation.CurrentSystemId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "visited planet system differs from galaxy_navigation current system.");
        }

        return new SaveGameSnapshot(
            slotId,
            revision,
            generatorVersion,
            contentVersion,
            updatedUtc,
            player,
            ship,
            inventory,
            visitedPlanet,
            technologyProgress,
            productionQueue,
            productionQueueNetwork,
            stationServices,
            baseConstruction,
            planetaryExploration,
            shipSystems,
            stageOneVoyage,
            galaxyNavigation,
            ecology,
            proceduralQuests,
            playerSurvival,
            npcFactions);
    }

    private static StationServicesSaveData CanonicalizeStationServices(
        StationServicesSaveData services)
    {
        return services with
        {
            Stock = services.Stock
                .OrderBy(stock => stock.DefinitionId, StringComparer.Ordinal)
                .ToArray(),
            Quests = services.Quests
                .OrderBy(quest => quest.QuestId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateStationServices(
        StationServicesSaveData services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!GameContentCatalog.IsStableId(services.MarketId) ||
            !GameContentCatalog.IsStableId(services.NpcId) ||
            services.PlayerCredits < 0 ||
            services.MerchantCredits < 0 ||
            services.Reputation is < -100 or > 100 ||
            services.DayIndex < 0 ||
            services.LastEconomyUpdateUnixSeconds < 0 ||
            services.Stock is null ||
            services.Quests is null)
        {
            throw new InvalidDataException(
                "station_services contains invalid identity or scalar values.");
        }

        HashSet<string> stockIds = new(StringComparer.Ordinal);
        foreach (StationServiceStockSaveData stock in services.Stock)
        {
            if (!GameContentCatalog.IsStableId(stock.DefinitionId) ||
                stock.Quantity < 0 ||
                !stockIds.Add(stock.DefinitionId))
            {
                throw new InvalidDataException(
                    "station_services contains invalid or duplicate stock.");
            }
        }

        HashSet<string> questIds = new(StringComparer.Ordinal);
        foreach (StationServiceQuestSaveData quest in services.Quests)
        {
            if (!GameContentCatalog.IsStableId(quest.QuestId) ||
                !GameContentCatalog.IsStableId(quest.CurrentNodeId) ||
                quest.Progress < 0 ||
                !questIds.Add(quest.QuestId) ||
                !Enum.IsDefined(quest.Status))
            {
                throw new InvalidDataException(
                    "station_services contains invalid quest state.");
            }
        }
    }

    private static BaseConstructionSaveData CanonicalizeBaseConstruction(
        BaseConstructionSaveData baseConstruction)
    {
        return baseConstruction with
        {
            Stock = baseConstruction.Stock
                .OrderBy(stock => stock.ModuleId, StringComparer.Ordinal)
                .ToArray(),
            Modules = baseConstruction.Modules
                .OrderBy(module => module.InstanceId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateBaseConstruction(
        BaseConstructionSaveData baseConstruction)
    {
        ArgumentNullException.ThrowIfNull(baseConstruction);
        if (!GameContentCatalog.IsStableId(baseConstruction.BaseId) ||
            baseConstruction.NextSequence <= 0 ||
            baseConstruction.StoredEnergy < 0.0 ||
            double.IsNaN(baseConstruction.StoredEnergy) ||
            double.IsInfinity(baseConstruction.StoredEnergy) ||
            baseConstruction.Stock is null ||
            baseConstruction.Modules is null ||
            baseConstruction.Modules.Count > 500)
        {
            throw new InvalidDataException(
                "base_construction contains invalid identity or scalar values.");
        }

        HashSet<string> stockIds = new(StringComparer.Ordinal);
        foreach (BaseConstructionStockSaveData stock in baseConstruction.Stock)
        {
            if (!GameContentCatalog.IsStableId(stock.ModuleId) ||
                stock.Quantity < 0 ||
                !stockIds.Add(stock.ModuleId))
            {
                throw new InvalidDataException(
                    "base_construction contains invalid or duplicate stock.");
            }
        }

        HashSet<string> instanceIds = new(StringComparer.Ordinal);
        HashSet<(int X, int Z)> cells = new();
        foreach (BaseConstructionModuleSaveData module in
            baseConstruction.Modules)
        {
            if (!GameContentCatalog.IsStableId(module.InstanceId) ||
                !GameContentCatalog.IsStableId(module.ModuleId) ||
                module.RotationQuarterTurns is < 0 or > 3 ||
                !instanceIds.Add(module.InstanceId) ||
                !cells.Add((module.GridX, module.GridZ)))
            {
                throw new InvalidDataException(
                    "base_construction contains invalid or duplicate modules.");
            }
        }
    }

    private static PlanetaryExplorationSaveData
        CanonicalizePlanetaryExploration(
            PlanetaryExplorationSaveData exploration)
    {
        return exploration with
        {
            Pois = exploration.Pois
                .OrderBy(state => state.InstanceId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidatePlanetaryExploration(
        PlanetaryExplorationSaveData exploration)
    {
        ArgumentNullException.ThrowIfNull(exploration);
        if (exploration.WorldSeed <= 0 ||
            !GameContentCatalog.IsStableId(exploration.RegionKey) ||
            exploration.DiscoveryPoints < 0 ||
            exploration.Pois is null ||
            exploration.Pois.Count > 10000)
        {
            throw new InvalidDataException(
                "planetary_exploration contains invalid identity or scalar values.");
        }

        HashSet<string> instanceIds = new(StringComparer.Ordinal);
        foreach (PlanetaryPoiStateSaveData state in exploration.Pois)
        {
            if (!GameContentCatalog.IsStableId(state.InstanceId) ||
                !GameContentCatalog.IsStableId(state.PoiTypeId) ||
                !state.PoiTypeId.StartsWith("poi.", StringComparison.Ordinal) ||
                !instanceIds.Add(state.InstanceId) ||
                (state.Resolved && !state.Discovered) ||
                state.CustomName is null ||
                state.CustomName.Length > 40)
            {
                throw new InvalidDataException(
                    "planetary_exploration contains invalid or duplicate POI state.");
            }
        }
    }

    private static StageOneVoyageSaveData CanonicalizeStageOneVoyage(
        StageOneVoyageSaveData voyage)
    {
        ArgumentNullException.ThrowIfNull(voyage);
        return voyage with
        {
            PositionX = NormalizeSignedZero(voyage.PositionX),
            PositionY = NormalizeSignedZero(voyage.PositionY),
            PositionZ = NormalizeSignedZero(voyage.PositionZ),
            RotationX = NormalizeSignedZero(voyage.RotationX),
            RotationY = NormalizeSignedZero(voyage.RotationY),
            RotationZ = NormalizeSignedZero(voyage.RotationZ),
            VelocityX = NormalizeSignedZero(voyage.VelocityX),
            VelocityY = NormalizeSignedZero(voyage.VelocityY),
            VelocityZ = NormalizeSignedZero(voyage.VelocityZ)
        };
    }

    private static void ValidateStageOneVoyage(StageOneVoyageSaveData voyage)
    {
        ArgumentNullException.ThrowIfNull(voyage);
        if (!Enum.IsDefined(typeof(StageOneVoyageLocation), voyage.Location) ||
            voyage.TakeoffCount < 0 ||
            voyage.DockingCount < 0 ||
            voyage.LandingCount < 0 ||
            voyage.CompletedLoops < 0 ||
            voyage.DockingCount > voyage.TakeoffCount ||
            voyage.LandingCount > voyage.TakeoffCount ||
            voyage.CompletedLoops > voyage.LandingCount ||
            string.IsNullOrWhiteSpace(voyage.LastCheckpoint) ||
            voyage.LastCheckpoint.Length > 64 ||
            (voyage.Location != StageOneVoyageLocation.PlanetSurface &&
             !voyage.Piloted) ||
            (voyage.StationVisitedThisLoop && !voyage.StationVisited))
        {
            throw new InvalidDataException(
                "stage_one_voyage contains invalid state or counters.");
        }

        double[] values =
        {
            voyage.PositionX,
            voyage.PositionY,
            voyage.PositionZ,
            voyage.RotationX,
            voyage.RotationY,
            voyage.RotationZ,
            voyage.VelocityX,
            voyage.VelocityY,
            voyage.VelocityZ
        };
        foreach (double value in values)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > 1_000_000.0)
            {
                throw new InvalidDataException(
                    "stage_one_voyage contains a non-finite or unreasonable pose.");
            }
        }
    }

    private static GalaxyNavigationSaveData CanonicalizeGalaxyNavigation(
        GalaxyNavigationSaveData navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return navigation with
        {
            TotalDistanceLightYears = NormalizeSignedZero(
                navigation.TotalDistanceLightYears),
            TotalInterplanetaryDistanceMeters = NormalizeSignedZero(
                navigation.TotalInterplanetaryDistanceMeters),
            VisitedSystemIds = navigation.VisitedSystemIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateGalaxyNavigation(
        GalaxyNavigationSaveData navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (navigation.UniverseSeed <= 0 ||
            !GameContentCatalog.IsStableId(navigation.GalaxyId) ||
            !string.Equals(
                navigation.GalaxyId,
                GalaxyNavigationRuntime.PrimaryGalaxyId,
                StringComparison.Ordinal) ||
            !GameContentCatalog.IsStableId(navigation.CurrentSystemId) ||
            (!string.IsNullOrEmpty(navigation.CurrentPlanetId) &&
             (!GameContentCatalog.IsStableId(navigation.CurrentPlanetId) ||
              !navigation.CurrentPlanetId.StartsWith(
                  "planet.",
                  StringComparison.Ordinal))) ||
            (!string.IsNullOrEmpty(navigation.SelectedPlanetId) &&
             (!GameContentCatalog.IsStableId(navigation.SelectedPlanetId) ||
              !navigation.SelectedPlanetId.StartsWith(
                  "planet.",
                  StringComparison.Ordinal))) ||
            (!string.IsNullOrEmpty(navigation.SelectedDestinationSystemId) &&
             !GameContentCatalog.IsStableId(
                 navigation.SelectedDestinationSystemId)) ||
            (string.IsNullOrEmpty(navigation.SelectedDestinationSystemId) &&
             (navigation.SelectedSectorX != 0 ||
              navigation.SelectedSectorY != 0 ||
              navigation.SelectedSectorZ != 0)) ||
            navigation.JumpCount < 0 ||
            navigation.InterplanetaryTransferCount < 0 ||
            !double.IsFinite(navigation.TotalInterplanetaryDistanceMeters) ||
            navigation.TotalInterplanetaryDistanceMeters < 0.0 ||
            !double.IsFinite(navigation.TotalDistanceLightYears) ||
            navigation.TotalDistanceLightYears < 0.0 ||
            navigation.VisitedSystemIds is null ||
            navigation.VisitedSystemIds.Count is < 1 or >
                GalaxyNavigationRuntime.MaximumVisitedSystems ||
            navigation.VisitedSystemIds.Any(id =>
                !GameContentCatalog.IsStableId(id)) ||
            navigation.VisitedSystemIds
                .Distinct(StringComparer.Ordinal).Count() !=
                navigation.VisitedSystemIds.Count ||
            !navigation.VisitedSystemIds.Contains(
                navigation.CurrentSystemId,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "galaxy_navigation contains invalid identity, counters or visited systems.");
        }

        GalaxyNavigationRuntime runtime = new(navigation);
        if (!string.Equals(
            runtime.CurrentSystem.SystemId,
            navigation.CurrentSystemId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "galaxy_navigation current system is not deterministic.");
        }
    }

    private static EcologySaveData CanonicalizeEcology(EcologySaveData ecology)
    {
        ArgumentNullException.ThrowIfNull(ecology);
        return ecology with
        {
            DiscoveredFloraIds = ecology.DiscoveredFloraIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            DiscoveredFaunaIds = ecology.DiscoveredFaunaIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            RemovedFloraInstanceIds = ecology.RemovedFloraInstanceIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateEcology(EcologySaveData ecology)
    {
        ArgumentNullException.ThrowIfNull(ecology);
        if (ecology.WorldSeed <= 0 ||
            !GameContentCatalog.IsStableId(ecology.RegionKey) ||
            !ecology.RegionKey.StartsWith("region.", StringComparison.Ordinal) ||
            ecology.DiscoveryPoints < 0 ||
            ecology.DiscoveredFloraIds is null ||
            ecology.DiscoveredFaunaIds is null ||
            ecology.RemovedFloraInstanceIds is null ||
            ecology.DiscoveredFloraIds.Count > EcologyCatalog.ExpectedFloraCount ||
            ecology.DiscoveredFaunaIds.Count > EcologyCatalog.ExpectedFaunaCount ||
            ecology.RemovedFloraInstanceIds.Count > EcologyPlanner.GameplayFloraInstanceCount)
        {
            throw new InvalidDataException(
                "ecology contains invalid identity, counters or collection sizes.");
        }

        static bool HasUniqueStableIds(
            IReadOnlyList<string> ids,
            string prefix)
        {
            return ids.All(id =>
                    GameContentCatalog.IsStableId(id) &&
                    id.StartsWith(prefix, StringComparison.Ordinal)) &&
                ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
        }

        if (!HasUniqueStableIds(ecology.DiscoveredFloraIds, "flora.") ||
            !HasUniqueStableIds(ecology.DiscoveredFaunaIds, "fauna.") ||
            ecology.RemovedFloraInstanceIds.Any(id =>
                string.IsNullOrWhiteSpace(id) ||
                !id.StartsWith("ecology.flora.", StringComparison.Ordinal)) ||
            ecology.RemovedFloraInstanceIds
                .Distinct(StringComparer.Ordinal).Count() !=
                ecology.RemovedFloraInstanceIds.Count)
        {
            throw new InvalidDataException(
                "ecology contains invalid or duplicate discovery/delta IDs.");
        }
    }

    private static ProceduralQuestSaveData CanonicalizeProceduralQuests(
        ProceduralQuestSaveData quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        return quests with
        {
            States = quests.States
                .OrderBy(state => state.QuestId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateProceduralQuests(ProceduralQuestSaveData quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        if (quests.WorldSeed <= 0 ||
            quests.BoardRevision != ProceduralQuestGenerator.BoardRevision ||
            quests.States is null ||
            quests.States.Count > ProceduralQuestCatalog.ExpectedBoardSize)
        {
            throw new InvalidDataException(
                "procedural_quests contains invalid seed, revision or state count.");
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ProceduralQuestStateSaveData state in quests.States)
        {
            if (string.IsNullOrWhiteSpace(state.QuestId) ||
                !state.QuestId.StartsWith("quest.proc.", StringComparison.Ordinal) ||
                !ids.Add(state.QuestId) ||
                !Enum.IsDefined(state.Status) ||
                state.Progress < 0)
            {
                throw new InvalidDataException(
                    "procedural_quests contains an invalid or duplicate quest state.");
            }
        }
    }

    private static PlayerSurvivalSaveData CanonicalizePlayerSurvival(
        PlayerSurvivalSaveData survival)
    {
        ArgumentNullException.ThrowIfNull(survival);
        return survival with
        {
            InstalledSuitModuleIds = survival.InstalledSuitModuleIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            InstalledMultitoolModuleIds = survival.InstalledMultitoolModuleIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidatePlayerSurvival(PlayerSurvivalSaveData survival)
    {
        ArgumentNullException.ThrowIfNull(survival);
        double[] values =
        {
            survival.Health, survival.Shield, survival.Stamina,
            survival.LifeSupport, survival.HazardProtection, survival.Oxygen,
            survival.JetpackEnergy, survival.MultitoolEnergy
        };
        if (values.Any(value => !double.IsFinite(value) || value < 0.0) ||
            survival.InstalledSuitModuleIds is null ||
            survival.InstalledMultitoolModuleIds is null ||
            survival.InstalledSuitModuleIds.Count > PlayerSurvivalCatalog.ExpectedSuitModuleCount ||
            survival.InstalledMultitoolModuleIds.Count > PlayerSurvivalCatalog.ExpectedMultitoolModuleCount ||
            !Enum.TryParse(
                survival.ActiveMultitoolFunction,
                ignoreCase: false,
                out PlayerMultitoolFunction activeFunction) ||
            !Enum.IsDefined(activeFunction))
        {
            throw new InvalidDataException(
                "player_survival contains invalid vitals, equipment sizes or multitool mode.");
        }

        static bool ValidUnique(IReadOnlyList<string> ids, string prefix) =>
            ids.All(id => GameContentCatalog.IsStableId(id) &&
                id.StartsWith(prefix, StringComparison.Ordinal)) &&
            ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;

        if (!ValidUnique(survival.InstalledSuitModuleIds, "module.suit.") ||
            !ValidUnique(survival.InstalledMultitoolModuleIds, "tool."))
        {
            throw new InvalidDataException(
                "player_survival contains invalid or duplicate equipment IDs.");
        }
    }

    private static NpcFactionSaveData CanonicalizeNpcFactions(
        NpcFactionSaveData npcFactions)
    {
        ArgumentNullException.ThrowIfNull(npcFactions);
        return npcFactions with
        {
            Reputations = npcFactions.Reputations
                .OrderBy(entry => entry.FactionId, StringComparer.Ordinal)
                .ToArray(),
            Agents = npcFactions.Agents
                .OrderBy(entry => entry.NpcId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateNpcFactions(NpcFactionSaveData npcFactions)
    {
        ArgumentNullException.ThrowIfNull(npcFactions);
        if (npcFactions.WorldSeed <= 0 ||
            string.IsNullOrWhiteSpace(npcFactions.RegionKey) ||
            !npcFactions.RegionKey.StartsWith("region.", StringComparison.Ordinal) ||
            npcFactions.Reputations is null ||
            npcFactions.Agents is null ||
            npcFactions.Reputations.Count > NpcFactionCatalog.ExpectedFactionCount ||
            npcFactions.Agents.Count > NpcFactionCatalog.ExpectedAgentCount)
        {
            throw new InvalidDataException(
                "npc_factions contains invalid seed, region or delta collection sizes.");
        }

        HashSet<string> factionIds = new(StringComparer.Ordinal);
        foreach (NpcFactionReputationSaveData reputation in npcFactions.Reputations)
        {
            if (!GameContentCatalog.IsStableId(reputation.FactionId) ||
                !reputation.FactionId.StartsWith("faction.", StringComparison.Ordinal) ||
                !factionIds.Add(reputation.FactionId) ||
                reputation.Reputation is < -100 or > 100 ||
                reputation.Reputation == 0)
            {
                throw new InvalidDataException(
                    "npc_factions contains an invalid or duplicate reputation delta.");
            }
        }

        HashSet<string> npcIds = new(StringComparer.Ordinal);
        foreach (NpcFactionAgentStateSaveData agent in npcFactions.Agents)
        {
            if (!GameContentCatalog.IsStableId(agent.NpcId) ||
                !agent.NpcId.StartsWith("npc.", StringComparison.Ordinal) ||
                !npcIds.Add(agent.NpcId) ||
                !double.IsFinite(agent.Health) ||
                agent.Health < 0.0 || agent.Health > 500.0 ||
                agent.DefeatCount < 0 ||
                (agent.Defeated && agent.Health > 0.0001) ||
                (!agent.Defeated && agent.Health <= 0.0))
            {
                throw new InvalidDataException(
                    "npc_factions contains an invalid or duplicate NPC state delta.");
            }
        }
    }

    private static double NormalizeSignedZero(double value)
    {
        return Math.Abs(value) < 0.0000001 ? 0.0 : value;
    }

    private static ShipSystemsSaveData CanonicalizeShipSystems(
        ShipSystemsSaveData shipSystems)
    {
        return shipSystems with
        {
            InstalledModules = shipSystems.InstalledModules
                .OrderBy(module => module.SlotType, StringComparer.Ordinal)
                .ThenBy(module => module.SlotIndex)
                .ThenBy(module => module.ModuleId, StringComparer.Ordinal)
                .ToArray(),
            Systems = shipSystems.Systems
                .OrderBy(system => system.SystemId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ValidateShipSystems(ShipSystemsSaveData shipSystems)
    {
        ArgumentNullException.ThrowIfNull(shipSystems);
        if (!GameContentCatalog.IsStableId(shipSystems.ShipClassId) ||
            !shipSystems.ShipClassId.StartsWith(
                "ship.class.",
                StringComparison.Ordinal) ||
            shipSystems.Fuel < 0.0 ||
            double.IsNaN(shipSystems.Fuel) ||
            double.IsInfinity(shipSystems.Fuel) ||
            shipSystems.InstalledModules is null ||
            shipSystems.Systems is null ||
            shipSystems.InstalledModules.Count > 32 ||
            shipSystems.Systems.Count > 32)
        {
            throw new InvalidDataException(
                "ship_systems contains invalid identity or scalar values.");
        }

        if (shipSystems.Commissioned == false &&
            shipSystems.InstalledModules.Count > 0)
        {
            throw new InvalidDataException(
                "ship_systems cannot contain installed modules before commissioning.");
        }

        HashSet<string> moduleIds = new(StringComparer.Ordinal);
        HashSet<(string SlotType, int SlotIndex)> slots = new();
        foreach (ShipModuleInstallationSaveData module in
            shipSystems.InstalledModules)
        {
            if (!GameContentCatalog.IsStableId(module.ModuleId) ||
                !module.ModuleId.StartsWith(
                    "module.ship.",
                    StringComparison.Ordinal) ||
                module.SlotType is not "Technology" and not "Weapon" ||
                module.SlotIndex < 0 ||
                !moduleIds.Add(module.ModuleId) ||
                !slots.Add((module.SlotType, module.SlotIndex)))
            {
                throw new InvalidDataException(
                    "ship_systems contains invalid or duplicate modules.");
            }
        }

        HashSet<string> systemIds = new(StringComparer.Ordinal);
        foreach (ShipSystemHealthSaveData system in shipSystems.Systems)
        {
            if (!GameContentCatalog.IsStableId(system.SystemId) ||
                !system.SystemId.StartsWith(
                    "ship.system.",
                    StringComparison.Ordinal) ||
                system.Health < 0.0 ||
                double.IsNaN(system.Health) ||
                double.IsInfinity(system.Health) ||
                !systemIds.Add(system.SystemId))
            {
                throw new InvalidDataException(
                    "ship_systems contains invalid or duplicate system state.");
            }
        }
    }

    private static void ValidateProductionQueueNetwork(
        ProductionQueueNetworkSaveData network)
    {
        ArgumentNullException.ThrowIfNull(network);
        HashSet<string> stationIds = new(StringComparer.Ordinal);
        foreach (ProductionQueueSaveData queue in network.Stations)
        {
            if (!GameContentCatalog.IsStableId(queue.StationId) ||
                !stationIds.Add(queue.StationId))
            {
                throw new InvalidDataException(
                    "production_queue_network contains invalid or duplicate " +
                    $"station ID {queue.StationId}.");
            }
        }
    }

    private SaveDatabaseDiagnostics ReadDiagnosticsCore(
        SqliteConnection connection,
        string slotId)
    {
        int schemaVersion = ExecuteScalarInt(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
        string journalMode = ExecuteScalarString(
            connection,
            "PRAGMA journal_mode;");
        bool foreignKeys = ExecuteScalarInt(
            connection,
            "PRAGMA foreign_keys;") == 1;
        int synchronousMode = ExecuteScalarInt(
            connection,
            "PRAGMA synchronous;");
        int busyTimeout = ExecuteScalarInt(
            connection,
            "PRAGMA busy_timeout;");
        string integrityResult = ExecuteScalarString(
            connection,
            "PRAGMA integrity_check;");

        int inventoryRows = string.IsNullOrWhiteSpace(slotId)
            ? 0
            : ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM inventory_items i " +
                "JOIN containers c ON c.container_id = i.container_id " +
                "WHERE c.slot_id = $slot_id;",
                ("$slot_id", slotId));
        int visitedRows = string.IsNullOrWhiteSpace(slotId)
            ? 0
            : ExecuteScalarInt(
                connection,
                "SELECT COUNT(*) FROM visited_planets WHERE slot_id = $slot_id;",
                ("$slot_id", slotId));

        long databaseBytes = File.Exists(_databasePath)
            ? new FileInfo(_databasePath).Length
            : 0L;

        SaveFileInspection backupInspection = InspectDatabaseFileCore(
            BackupPath,
            slotId,
            requireSnapshot: false);

        return new SaveDatabaseDiagnostics(
            schemaVersion,
            journalMode,
            foreignKeys,
            synchronousMode,
            busyTimeout,
            integrityResult,
            databaseBytes,
            inventoryRows,
            visitedRows,
            QueuedWrites,
            CompletedWrites,
            MaximumConcurrentWriters,
            backupInspection.Exists,
            backupInspection.Bytes,
            backupInspection.IntegrityResult);
    }

    private SavePrototypeAcceptanceReport BuildFailure(
        string result,
        SaveGameSnapshot? snapshot,
        string slotId,
        int concurrentWriteCount,
        int exactComparisons,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        SaveDatabaseDiagnostics diagnostics;
        try
        {
            using SqliteConnection connection = OpenConnection();
            diagnostics = ReadDiagnosticsCore(connection, slotId);
        }
        catch
        {
            diagnostics = EmptyDiagnostics();
        }

        return new SavePrototypeAcceptanceReport(
            false,
            result,
            snapshot,
            diagnostics,
            concurrentWriteCount,
            exactComparisons,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private SaveDatabaseDiagnostics EmptyDiagnostics()
    {
        return new SaveDatabaseDiagnostics(
            0,
            string.Empty,
            false,
            0,
            0,
            "unavailable",
            0,
            0,
            0,
            QueuedWrites,
            CompletedWrites,
            MaximumConcurrentWriters);
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static int ExecuteScalarInt(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        object? value = ExecuteScalar(connection, commandText, parameters);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string ExecuteScalarString(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        object? value = ExecuteScalar(connection, commandText, parameters);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void ExecutePragma(
        SqliteConnection connection,
        string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteScalar();
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar();
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private void UpdateMaximumConcurrentWriters(int active)
    {
        while (true)
        {
            int observed = Volatile.Read(ref _maximumConcurrentWriters);
            if (active <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _maximumConcurrentWriters,
                    active,
                    observed) == observed)
            {
                return;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SaveDatabase));
        }
    }
}
